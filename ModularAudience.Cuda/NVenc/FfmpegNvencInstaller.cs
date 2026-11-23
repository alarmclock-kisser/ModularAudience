using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
    /// of a driver-compatible build. Maps the installed NVIDIA driver version to the
    /// correct BtbN FFmpeg build (NVENC SDK version).
    /// </summary>
    public static class FfmpegNvencInstaller
    {
        private const string GithubRepository = "BtbN/FFmpeg-Builds";

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
                proc.WaitForExit(15000);

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
        /// Returns the installed NVIDIA driver version as a double (e.g. 582.70), or null if nvidia-smi unavailable.
        /// </summary>
        public static double? GetNvidiaDriverVersion()
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

                if (string.IsNullOrWhiteSpace(output))
                {
                    return null;
                }

                // Parse "582.70" -> 582.70
                // Handle both "." and "," as decimal separator
                string normalized = output.Replace(',', '.');
                return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var ver) ? ver : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Returns the installed NVIDIA driver version as a display string (e.g. "582.70"), or null.
        /// </summary>
        public static string? GetNvidiaDriverVersionDisplay()
        {
            double? ver = GetNvidiaDriverVersion();
            return ver.HasValue ? ver.Value.ToString("0.##", CultureInfo.InvariantCulture) : null;
        }

        /// <summary>
        /// Safely deletes all managed NVENC FFmpeg installations (ffmpeg-nvenc-* directories).
        /// </summary>
        public static void RemoveManagedNvencFfmpeg()
        {
            if (!Directory.Exists(BinaryRootDirectory)) return;

            foreach (var dir in Directory.EnumerateDirectories(BinaryRootDirectory, "ffmpeg-nvenc-*", SearchOption.TopDirectoryOnly))
            {
                try { Directory.Delete(dir, recursive: true); } catch { /* ignore locked files */ }
            }

            // Also clean up any staging directories
            foreach (var dir in Directory.EnumerateDirectories(BinaryRootDirectory, ".stage-nvenc-*", SearchOption.TopDirectoryOnly))
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }

        /// <summary>
        /// Determines which BtbN FFmpeg build to install based on the installed NVIDIA driver version.
        /// Returns the asset name suffix (e.g. "8.1") and the release tag to use.
        /// </summary>
        private static (string? AssetSuffix, string? ReleaseTag) GetRecommendedBuild(double? driverVersion)
        {
            // NVENC SDK version requirements (from FFmpeg nv-codec-headers):
            //   SDK 13.1: driver >= 610.00  -> master builds (no version suffix)
            //   SDK 13.0: driver >= 570.00  -> 8.1 / 7.1 builds
            //   SDK 11.1: driver >= 530.00  -> 6.1 builds
            //
            // BtbN "latest" release assets:
            //   ffmpeg-master-latest-win64-gpl-shared.zip      (SDK 13.1, driver >= 610)
            //   ffmpeg-n8.1-latest-win64-gpl-shared-8.1.zip   (SDK 13.0, driver >= 570)
            //   ffmpeg-n9.0-latest-win64-gpl-shared-9.0.zip   (SDK 13.0, driver >= 570)

            if (driverVersion is >= 610.0)
            {
                // Latest master build works
                return ("master-latest", "latest");
            }

            if (driverVersion is >= 570.0)
            {
                // 8.1 build (SDK 13.0) works
                return ("8.1", "latest");
            }

            if (driverVersion is >= 530.0)
            {
                // 6.1 build (SDK 11.1) - only available in monthly autobuild releases,
                // not in the "latest" release. Need to find a recent monthly release.
                return ("6.1", null); // null tag = need to search monthly releases
            }

            // Driver too old for any NVENC build
            return (null, null);
        }

        /// <summary>
        /// Downloads and installs the correct FFmpeg build with NVENC support
        /// based on the installed NVIDIA driver version.
        /// </summary>
        public static async Task<string> InstallNvencFfmpegAsync(
            IProgress<string>? progress,
            CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(BinaryRootDirectory);

            double? driverVersion = GetNvidiaDriverVersion();
            string? driverDisplay = GetNvidiaDriverVersionDisplay();
            if (!string.IsNullOrWhiteSpace(driverDisplay))
            {
                progress?.Report($"Detected NVIDIA driver {driverDisplay}. Selecting compatible FFmpeg build...");
            }

            var (assetSuffix, releaseTag) = GetRecommendedBuild(driverVersion);

            if (assetSuffix == null)
            {
                throw new InvalidDataException(
                    $"NVIDIA driver {driverDisplay ?? "unknown"} is too old for NVENC. " +
                    "No compatible FFmpeg build found. Use CPU codecs (X264/X265) for rendering.");
            }

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("ModularAudience", "1.0"));

            string? downloadUrl = null;
            string? assetName = null;
            string effectiveTag = releaseTag ?? "latest";

            // Find the download URL for the appropriate build
            if (releaseTag != null)
            {
                // "latest" release - find the matching asset
                string json = await httpClient.GetStringAsync(
                    $"https://api.github.com/repos/{GithubRepository}/releases/latest").ConfigureAwait(false);

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // For "master-latest", find the asset without a version suffix
                // For "8.1" / "9.0", find the asset with that suffix
                if (root.TryGetProperty("assets", out var assets))
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        if (!asset.TryGetProperty("name", out var nameProp)) continue;
                        string name = nameProp.GetString() ?? "";

                        bool matches = assetSuffix switch
                        {
                            "master-latest" => name.Contains("master-latest") && name.Contains("win64-gpl-shared"),
                            _ => name.Contains($"-win64-gpl-shared-{assetSuffix}.zip") || name.Contains($"-win64-gpl-{assetSuffix}.zip")
                        };

                        if (matches)
                        {
                            downloadUrl = asset.TryGetProperty("browser_download_url", out var urlProp) ? urlProp.GetString() : null;
                            assetName = name;
                            break;
                        }
                    }
                }
            }
            else
            {
                // Need to find a monthly release that has the 6.1 asset
                // Search recent releases (last ~6 months)
                string releasesJson = await httpClient.GetStringAsync(
                    $"https://api.github.com/repos/{GithubRepository}/releases?per_page=25").ConfigureAwait(false);

                using var doc = JsonDocument.Parse(releasesJson);
                foreach (var rel in doc.RootElement.EnumerateArray())
                {
                    string? tag = rel.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
                    if (string.IsNullOrWhiteSpace(tag) || tag == "latest") continue;

                    progress?.Report($"Checking release {tag} for {assetSuffix} build...");

                    string releaseJson = await httpClient.GetStringAsync(
                        $"https://api.github.com/repos/{GithubRepository}/releases/tags/{tag}").ConfigureAwait(false);

                    using var rdoc = JsonDocument.Parse(releaseJson);
                    if (!rdoc.RootElement.TryGetProperty("assets", out var rassets)) continue;

                    foreach (var asset in rassets.EnumerateArray())
                    {
                        if (!asset.TryGetProperty("name", out var nameProp)) continue;
                        string name = nameProp.GetString() ?? "";

                        // Look for a 6.1 asset with win64-gpl (static, smaller)
                        if (name.Contains($"-win64-gpl-{assetSuffix}.zip") ||
                            name.Contains($"-win64-gpl-shared-{assetSuffix}.zip"))
                        {
                            // Prefer shared build (has NVENC)
                            if (name.Contains("shared"))
                            {
                                downloadUrl = asset.TryGetProperty("browser_download_url", out var urlProp) ? urlProp.GetString() : null;
                                assetName = name;
                                effectiveTag = tag!;
                                break;
                            }
                        }
                    }

                    if (downloadUrl != null) break;
                }
            }

            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                throw new InvalidDataException(
                    $"Could not find a compatible FFmpeg NVENC build for driver {driverDisplay ?? "unknown"}. " +
                    "Use CPU codecs (X264/X265) for rendering.");
            }

            progress?.Report($"Downloading {assetName}...");
            string installed = await DownloadAndInstallAsync(httpClient, downloadUrl, progress, cancellationToken, effectiveTag, assetSuffix).ConfigureAwait(false);

            // Verify the driver can actually use this build
            if (!CanDriverUseNvenc(installed))
            {
                progress?.Report($"Warning: installed build may not be fully compatible with driver {driverDisplay}.");
            }

            progress?.Report($"FFmpeg NVENC ({assetSuffix}) installed successfully.");
            return installed;
        }

        private static async Task<string> DownloadAndInstallAsync(
            HttpClient httpClient, string url,
            IProgress<string>? progress, CancellationToken cancellationToken,
            string tag, string assetSuffix)
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

                var destination = Path.Combine(BinaryRootDirectory, $"ffmpeg-nvenc-{assetSuffix}");
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
