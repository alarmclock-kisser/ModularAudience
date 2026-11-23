using System;
using System.Collections.Generic;
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
    /// of a build with CUDA/NVENC support when missing. Handles driver compatibility
    /// by trying multiple FFmpeg versions until one works with the installed driver.
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
        /// Returns true if the given FFmpeg binary lists NVENC codecs (h264_nvenc, hevc_nvenc).
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

                return output.Contains("h264_nvenc", StringComparison.OrdinalIgnoreCase) ||
                       output.Contains("hevc_nvenc", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Tests if the installed NVIDIA driver can actually use this FFmpeg build for NVENC encoding.
        /// Tries to open the h264_nvenc encoder with a minimal nullsrc input.
        /// </summary>
        public static bool CanDriverUseNvenc(string? ffmpegPath, string? codec = null)
        {
            if (string.IsNullOrWhiteSpace(ffmpegPath) || !File.Exists(ffmpegPath))
            {
                return false;
            }

            string testCodec = codec ?? "h264_nvenc";

            try
            {
                string args = $"-f lavfi -i nullsrc=s=1280x720:r=1 -t 0.1 -c:v {testCodec} -f null -";
                var psi = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var proc = new Process { StartInfo = psi };
                proc.Start();
                string stderr = proc.StandardError.ReadToEnd();
                string stdout = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(15000);

                // Driver version mismatch or encoder failure means the build is incompatible
                bool incompatible = stderr.Contains("Driver does not support", StringComparison.OrdinalIgnoreCase) ||
                                   stderr.Contains("The minimum required Nvidia driver", StringComparison.OrdinalIgnoreCase) ||
                                   stderr.Contains("Error while opening encoder", StringComparison.OrdinalIgnoreCase) ||
                                   stderr.Contains("Could not open encoder", StringComparison.OrdinalIgnoreCase);

                return !incompatible && proc.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Finds an installed FFmpeg that both lists NVENC codecs AND works with the current driver.
        /// Returns null if none found.
        /// </summary>
        public static string? FindNvencFfmpeg()
        {
            var candidates = new List<string>();

            // 1) Check managed NVENC directory
            if (Directory.Exists(BinaryRootDirectory))
            {
                foreach (var dir in Directory.EnumerateDirectories(BinaryRootDirectory, "ffmpeg-nvenc-*", SearchOption.TopDirectoryOnly))
                {
                    var candidate = Path.Combine(dir, "ffmpeg.exe");
                    if (File.Exists(candidate))
                    {
                        candidates.Add(candidate);
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
                    if (File.Exists(candidate))
                    {
                        candidates.Add(candidate);
                    }
                }
            }

            // 3) Check app directory
            var local = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
            if (File.Exists(local))
            {
                candidates.Add(local);
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
                    if (File.Exists(candidate))
                    {
                        candidates.Add(Path.GetFullPath(candidate));
                    }
                }
                catch { /* ignore */ }
            }

            // First check codec list, then driver compatibility
            foreach (var candidate in candidates)
            {
                if (HasNvencSupport(candidate) && CanDriverUseNvenc(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// Returns the installed NVIDIA driver version string (e.g. "582.70"), or null if nvidia-smi unavailable.
        /// </summary>
        public static string? GetNvidiaDriverVersion()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "nvidia-smi",
                    Arguments = "--query-gpu=driver_version --format=csv,noheader",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var proc = new Process { StartInfo = psi };
                if (!proc.Start())
                {
                    return null;
                }

                string output = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit(5000);

                return string.IsNullOrWhiteSpace(output) ? null : output;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Downloads and installs FFmpeg builds with NVENC support, trying multiple versions
        /// until one is compatible with the installed NVIDIA driver.
        /// </summary>
        public static async Task<string> InstallNvencFfmpegAsync(
            IProgress<string>? progress,
            CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(BinaryRootDirectory);

            string? driverVersion = GetNvidiaDriverVersion();
            if (!string.IsNullOrWhiteSpace(driverVersion))
            {
                progress?.Report($"Detected NVIDIA driver {driverVersion}. Searching for a compatible FFmpeg build...");
            }

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("ModularAudience", "1.0"));

            // Strategy: try the latest build first, then progressively older dated releases.
            var candidates = new List<string> { "latest" };

            // Fetch dated releases from the past ~3 months (these have NVENC builds for older drivers)
            string? releasesJson = null;
            try
            {
                releasesJson = await httpClient.GetStringAsync($"https://api.github.com/repos/{GithubRepository}/releases?per_page=100").ConfigureAwait(false);
            }
            catch { /* offline or rate-limited: only try latest */ }

            if (!string.IsNullOrWhiteSpace(releasesJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(releasesJson!);
                    foreach (var rel in doc.RootElement.EnumerateArray())
                    {
                        string? tag = rel.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
                        string? publishedAt = rel.TryGetProperty("published_at", out var p) ? p.GetString() : null;
                        if (string.IsNullOrWhiteSpace(tag) || tag == "latest" || tag == "stable")
                        {
                            continue;
                        }

                        // Only consider releases from the last ~3 months
                        if (!string.IsNullOrWhiteSpace(publishedAt) &&
                            DateTime.TryParse(publishedAt, out var pubDate) &&
                            pubDate < DateTime.UtcNow.AddMonths(-3))
                        {
                            continue;
                        }

                        candidates.Add(tag!);
                    }
                }
                catch { /* ignore malformed JSON */ }
            }

            foreach (var tag in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string? downloadUrl = null;
                string? assetName = null;

                try
                {
                    string releaseUrl = tag == "latest"
                        ? $"https://api.github.com/repos/{GithubRepository}/releases/latest"
                        : $"https://api.github.com/repos/{GithubRepository}/releases/tags/{tag}";

                    progress?.Report($"Checking FFmpeg build {tag}...");
                    string json = await httpClient.GetStringAsync(releaseUrl).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

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
                }
                catch
                {
                    // Tag may not have a release (only a tag) - skip
                    continue;
                }

                if (string.IsNullOrWhiteSpace(downloadUrl))
                {
                    continue;
                }

                progress?.Report($"Downloading {tag} ({assetName} ~100 MB)...");

                string installed = await DownloadAndInstallAsync(httpClient, downloadUrl!, progress, cancellationToken, tag).ConfigureAwait(false);

                // Verify the driver can actually use this build
                if (CanDriverUseNvenc(installed))
                {
                    progress?.Report($"FFmpeg NVENC ({tag}) installed and driver-compatible.");
                    return installed;
                }

                progress?.Report($"FFmpeg build {tag} is not compatible with the current driver. Trying an older build...");
            }

            throw new InvalidDataException(
                "No FFmpeg build with working NVENC support was found for your NVIDIA driver. " +
                (string.IsNullOrWhiteSpace(driverVersion)
                    ? "nvidia-smi was not available."
                    : $"Driver version: {driverVersion}.") +
                " You can still use CPU codecs (X264/X265) for rendering.");
        }

        private static async Task<string> DownloadAndInstallAsync(
            HttpClient httpClient, string url,
            IProgress<string>? progress, CancellationToken cancellationToken, string tag)
        {
            var staging = Path.Combine(BinaryRootDirectory, ".stage-nvenc-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            try
            {
                var zipPath = Path.Combine(staging, "ffmpeg-nvenc.zip");
                await DownloadFileAsync(httpClient, url, zipPath, progress, cancellationToken).ConfigureAwait(false);

                progress?.Report("Extracting FFmpeg NVENC build...");
                var payload = Path.Combine(staging, "payload");
                Directory.CreateDirectory(payload);

                using var archive = ZipFile.OpenRead(zipPath);
                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
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

                var destination = Path.Combine(BinaryRootDirectory, $"ffmpeg-nvenc-{tag}");
                if (Directory.Exists(destination))
                {
                    Directory.Delete(destination, recursive: true);
                }
                Directory.Move(payload, destination);

                return Path.Combine(destination, "ffmpeg.exe");
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
