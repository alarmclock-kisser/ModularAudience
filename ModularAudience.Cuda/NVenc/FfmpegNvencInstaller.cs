using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ModularAudience.Cuda.NVenc
{
    /// <summary>
    /// Checks for NVENC codec support in FFmpeg and provides automatic installation
    /// of a build with CUDA/NVENC support when missing.
    /// </summary>
    public static class FfmpegNvencInstaller
    {
        private const string GithubRepository = "BtbN/FFmpeg-Builds";
        private const string NvencAssetName = "ffmpeg-master-latest-win64-gpl-shared.zip";

        private static readonly string BinaryRootDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ModularAudience", "binaries_nvenc");

        public static string BinaryDirectory => BinaryRootDirectory;

        /// <summary>
        /// Returns true if the given FFmpeg binary supports NVENC codecs (h264_nvenc, hevc_nvenc).
        /// </summary>
        public static bool HasNvencSupport(string? ffmpegPath)
        {
            if (string.IsNullOrWhiteSpace(ffmpegPath) || !File.Exists(ffmpegPath))
            {
                return false;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = "-codecs",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var proc = new Process { StartInfo = psi };
                proc.Start();
                string output = proc.StandardOutput.ReadToEnd();
                string error = proc.StandardError.ReadToEnd();
                proc.WaitForExit(5000);

                // NVENC codecs appear as "h264_nvenc" and "hevc_nvenc" in the codec list
                return output.Contains("h264_nvenc", StringComparison.OrdinalIgnoreCase) ||
                       output.Contains("hevc_nvenc", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Finds an installed FFmpeg that supports NVENC. Returns null if none found.
        /// </summary>
        public static string? FindNvencFfmpeg()
        {
            // 1) Check managed NVENC directory
            if (Directory.Exists(BinaryRootDirectory))
            {
                foreach (var dir in Directory.EnumerateDirectories(BinaryRootDirectory, "ffmpeg-nvenc-*", SearchOption.TopDirectoryOnly))
                {
                    var candidate = Path.Combine(dir, "ffmpeg.exe");
                    if (File.Exists(candidate) && HasNvencSupport(candidate))
                    {
                        return candidate;
                    }
                }
            }

            // 2) Check main binaries directory (from FfmpegBinaryManager)
            string mainBinDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ModularAudience", "binaries_ytdlp");
            if (Directory.Exists(mainBinDir))
            {
                foreach (var dir in Directory.EnumerateDirectories(mainBinDir, "ffmpeg-*", SearchOption.TopDirectoryOnly))
                {
                    var candidate = Path.Combine(dir, "ffmpeg.exe");
                    if (File.Exists(candidate) && HasNvencSupport(candidate))
                    {
                        return candidate;
                    }
                }
            }

            // 3) Check app directory
            var local = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
            if (File.Exists(local) && HasNvencSupport(local))
            {
                return local;
            }

            // 4) Check PATH
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var entry in pathEnv.Split(Path.PathSeparator))
            {
                var dir = entry.Trim().Trim('"');
                if (string.IsNullOrWhiteSpace(dir) || !Path.IsPathFullyQualified(dir)) continue;
                try
                {
                    var candidate = Path.Combine(dir, "ffmpeg.exe");
                    if (File.Exists(candidate) && HasNvencSupport(candidate))
                    {
                        return Path.GetFullPath(candidate);
                    }
                }
                catch { /* ignore */ }
            }

            return null;
        }

        /// <summary>
        /// Downloads and installs the latest FFmpeg build with NVENC support from BtbN GitHub releases.
        /// </summary>
        public static async Task<string> InstallNvencFfmpegAsync(
            IProgress<string>? progress,
            CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(BinaryRootDirectory);

            progress?.Report("Checking for the latest FFmpeg NVENC release on GitHub...");
            var releaseUrl = $"https://api.github.com/repos/{GithubRepository}/releases/latest";

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("ModularAudience", "1.0"));

            string json;
            try
            {
                json = await httpClient.GetStringAsync(releaseUrl).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Unable to query the FFmpeg GitHub release: {ex.Message}", ex);
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string? downloadUrl = null;
            string? assetName = null;

            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    if (asset.TryGetProperty("name", out var nameProp) &&
                        string.Equals(nameProp.GetString(), NvencAssetName, StringComparison.OrdinalIgnoreCase) &&
                        asset.TryGetProperty("browser_download_url", out var urlProp))
                    {
                        downloadUrl = urlProp.GetString();
                        assetName = nameProp.GetString();
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                throw new InvalidDataException($"The GitHub release does not contain {NvencAssetName}.");
            }

            progress?.Report($"Downloading {assetName} (~100 MB)...");

            var staging = Path.Combine(BinaryRootDirectory, ".stage-nvenc-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            try
            {
                var zipPath = Path.Combine(staging, "ffmpeg-nvenc.zip");

                await DownloadFileAsync(httpClient, downloadUrl!, zipPath, progress, cancellationToken).ConfigureAwait(false);

                progress?.Report("Extracting FFmpeg NVENC build...");
                var payload = Path.Combine(staging, "payload");
                Directory.CreateDirectory(payload);

                using var archive = ZipFile.OpenRead(zipPath);
                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // Extract ffmpeg.exe and all .dll files (shared build needs DLLs)
                    if ((string.Equals(entry.Name, "ffmpeg.exe", StringComparison.OrdinalIgnoreCase) ||
                         entry.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) &&
                        entry.Length > 0)
                    {
                        var targetPath = Path.Combine(payload, entry.Name);
                        await using var source = entry.Open();
                        await using var target = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
                        await source.CopyToAsync(target, 81920, cancellationToken).ConfigureAwait(false);
                    }
                }

                var ffmpegExe = Path.Combine(payload, "ffmpeg.exe");
                if (!File.Exists(ffmpegExe))
                {
                    throw new InvalidDataException("ffmpeg.exe was not found in the archive.");
                }

                // Verify NVENC support
                if (!HasNvencSupport(ffmpegExe))
                {
                    throw new InvalidDataException("The downloaded FFmpeg build does not support NVENC codecs.");
                }

                var destination = Path.Combine(BinaryRootDirectory, $"ffmpeg-nvenc-{Guid.NewGuid():N}");
                Directory.Move(payload, destination);

                var finalPath = Path.Combine(destination, "ffmpeg.exe");
                progress?.Report("FFmpeg NVENC installed successfully.");
                return finalPath;
            }
            finally
            {
                try { Directory.Delete(staging, recursive: true); } catch { }
            }
        }

        private static async Task DownloadFileAsync(HttpClient httpClient, string url, string destination,
            IProgress<string>? progress, CancellationToken cancellationToken)
        {
            using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            using var sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var targetStream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);

            var buffer = new byte[81920];
            long totalRead = 0;
            int read;
            while ((read = await sourceStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await targetStream.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                totalRead += read;

                if (totalBytes.HasValue && totalBytes.Value > 0)
                {
                    double pct = (double)totalRead / totalBytes.Value * 100.0;
                    progress?.Report($"Downloaded: {totalRead / 1024.0 / 1024.0:F1} MB / {totalBytes.Value / 1024.0 / 1024.0:F1} MB ({pct:F1}%)");
                }
                else
                {
                    progress?.Report($"Downloaded: {totalRead / 1024.0 / 1024.0:F1} MB");
                }
            }
        }
    }
}
