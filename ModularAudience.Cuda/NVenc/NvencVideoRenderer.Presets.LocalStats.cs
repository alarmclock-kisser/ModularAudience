#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

#if WINDOWS
// Add the required assembly reference for System.Management
// Make sure your project has a reference to System.Management.
// For .NET Core/.NET 5+ you need to install the "System.Management" NuGet package.
using System.Management;
#endif

namespace ModularAudience.Cuda
{
    /// <summary>
    /// Persists hardware stats + run metrics to a user-writable LocalStats.txt.
    /// Embedded resource LocalStats.txt is used as TEMPLATE only (read-only).
    /// </summary>
    public static partial class NvencVideoRenderer
    {
        // ----------------------------
        // Public lambda: read embedded template text (if present)
        // ----------------------------
        public static readonly Func<string?> ReadEmbeddedLocalStatsTemplateText = () =>
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();

                // Try find a resource that ends with "LocalStats.txt"
                var resName = asm.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("LocalStats.txt", StringComparison.OrdinalIgnoreCase));

                if (string.IsNullOrWhiteSpace(resName))
                {
                    return null;
                }

                using var s = asm.GetManifestResourceStream(resName);
                if (s is null)
                {
                    return null;
                }

                using var r = new StreamReader(s, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                return r.ReadToEnd();
            }
            catch
            {
                return null;
            }
        };

        // ----------------------------
        // Persistence location
        // ----------------------------
        public static string LocalStatsPersistentFilePath
        {
            get
            {
                string baseDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ModularAudience");

                Directory.CreateDirectory(baseDir);
                return Path.Combine(baseDir, "LocalStats.txt");
            }
        }

        // First N lines reserved for hardware/runtime header (we overwrite these)
        private const int HardwareHeaderLinesCount = 22;

        // ----------------------------
        // Core read/write helpers
        // ----------------------------
        private static void EnsureLocalStatsFileExists()
        {
            try
            {
                var path = LocalStatsPersistentFilePath;

                // Only create from embedded template if the AppData file does NOT exist yet
                if (File.Exists(path))
                {
                    // If file exists but is empty/corrupt, we still try to keep it and just ensure it has some content.
                    var fi = new FileInfo(path);
                    if (fi.Length > 0)
                    {
                        return;
                    }

                    // If empty, write minimal skeleton (do NOT use export copies anywhere)
                    File.WriteAllText(path, BuildMinimalLocalStatsSkeleton(), Encoding.UTF8);
                    return;
                }

                // First run: try embedded template
                string? template = ReadEmbeddedLocalStatsTemplateText();
                if (string.IsNullOrWhiteSpace(template))
                {
                    template = BuildMinimalLocalStatsSkeleton();
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, template, Encoding.UTF8);
            }
            catch
            {
                // ignore
            }
        }

        private static string BuildMinimalLocalStatsSkeleton()
        {
            // Ensure at least HardwareHeaderLinesCount lines exist for replacement
            var sb = new StringBuilder();
            sb.AppendLine("LocalStats");
            sb.AppendLine("--------------------------------");

            // Reserve header lines (we overwrite these)
            for (int i = 0; i < HardwareHeaderLinesCount; i++)
            {
                sb.AppendLine("N/A");
            }

            sb.AppendLine();
            sb.AppendLine("----- HISTORY -----");
            sb.AppendLine();

            return sb.ToString();
        }

        private static string[] SafeReadAllLines()
        {
            try
            {
                EnsureLocalStatsFileExists();

                var path = LocalStatsPersistentFilePath;
                if (!File.Exists(path))
                {
                    return [];
                }

                // IMPORTANT: Only ever read from the AppData file.
                return File.ReadAllLines(path, Encoding.UTF8);
            }
            catch
            {
                return [];
            }
        }

        private static void SafeWriteAllLines(string[] lines)
        {
            try
            {
                EnsureLocalStatsFileExists();

                var path = LocalStatsPersistentFilePath;

                // IMPORTANT: Only ever write to the AppData file.
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                var tmp = path + ".tmp";
                File.WriteAllLines(tmp, lines, Encoding.UTF8);
                File.Copy(tmp, path, overwrite: true);
                File.Delete(tmp);
            }
            catch
            {
                // ignore
            }
        }

        private static void SafeAppendLines(IEnumerable<string> linesToAppend, int findAndOverwriteByFirstWordsMatching = 0)
        {
            try
            {
                EnsureLocalStatsFileExists();

                var path = LocalStatsPersistentFilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                var newLines = linesToAppend.ToList();
                if (newLines.Count == 0)
                {
                    return;
                }

                if (!File.Exists(path))
                {
                    File.WriteAllLines(path, newLines, Encoding.UTF8);
                    return;
                }

                // Read existing
                var existingLines = File.ReadAllLines(path, Encoding.UTF8).ToList();

                // ---------- overwrite mode ----------
                if (findAndOverwriteByFirstWordsMatching > 0)
                {
                    // tokenizer: split by whitespace + punctuation
                    static string[] Tokenize(string line) =>
                        line.Split(
                            [' ', '\t', ',', '.', ':', ';', '-', '_', '(', ')', '[', ']'],
                            StringSplitOptions.RemoveEmptyEntries);

                    // Pre-tokenize new block
                    var newTokens = newLines
                        .Select(l => Tokenize(l)
                            .Take(findAndOverwriteByFirstWordsMatching)
                            .Select(w => w.ToLowerInvariant())
                            .ToArray())
                        .ToList();

                    for (int i = 0; i <= existingLines.Count - newLines.Count; i++)
                    {
                        bool blockMatches = true;

                        for (int j = 0; j < newLines.Count; j++)
                        {
                            var existingTok = Tokenize(existingLines[i + j])
                                .Take(findAndOverwriteByFirstWordsMatching)
                                .Select(w => w.ToLowerInvariant())
                                .ToArray();

                            if (existingTok.Length < newTokens[j].Length ||
                                !existingTok.SequenceEqual(newTokens[j]))
                            {
                                blockMatches = false;
                                break;
                            }
                        }

                        if (blockMatches)
                        {
                            // Overwrite block in-place
                            for (int j = 0; j < newLines.Count; j++)
                            {
                                existingLines[i + j] = newLines[j];
                            }

                            File.WriteAllLines(path, existingLines, Encoding.UTF8);
                            return;
                        }
                    }
                }

                // ---------- fallback: append ----------
                File.AppendAllLines(path, newLines, Encoding.UTF8);
            }
            catch
            {
                // ignore – stats must never crash the app
            }
        }




        private static string NA(string? s) => string.IsNullOrWhiteSpace(s) ? "N/A" : s!;
        private static string NA(int? v) => v.HasValue ? v.Value.ToString(CultureInfo.InvariantCulture) : "N/A";
        private static string NA(long? v) => v.HasValue ? v.Value.ToString(CultureInfo.InvariantCulture) : "N/A";
        private static string NA(double? v, string fmt = "0.##") => v.HasValue ? v.Value.ToString(fmt, CultureInfo.InvariantCulture) : "N/A";

        // ----------------------------
        // Public API
        // ----------------------------
        public static string Reset_LocalStats_File()
        {
            try
            {
                var path = LocalStatsPersistentFilePath;
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                EnsureLocalStatsFileExists();
                return LocalStatsPersistentFilePath;
            }
            catch (Exception ex)
            {
                return $"Failed to reset LocalStats file: {ex.Message}";
            }
        }

        public static void WriteHardwareInfo_To_LocalStats()
        {
            try
            {
                EnsureLocalStatsFileExists();

                var cpuName = GetCpuName(getModelName: true);
                var cpuC = GetCoresCount(physical: true);
                var cpuT = GetCoresCount(physical: false);
                var cpuMHz = GetCpuFrequencyMHz();

                var gpuName = GetGpuName(getModelName: true);
                var vramMb = GetGpuVramMb();
                var gpuCoreMHz = GetGpuClockRate(memoryClock: false);
                var gpuMemMHz = GetGpuClockRate(memoryClock: true);

                var ramGb = GetRamSizeGb();
                var ramMHz = GetRamSpeedMHz();
                var ramChannels = GetRamChannelMode();
                var ramEcc = GetRamEccState();

                var driveModel = GetSystemDriveModelAndInterface();
                var driveSizeGb = GetSystemDriveSizeGb();
                var driveRead = GetSystemDriveReadSpeedMb_s();   // likely N/A (stub)
                var driveWrite = GetSystemDriveWriteSpeedMb_s(); // likely N/A (stub)

                var os = GetOsInfo();
                var dotnet = GetDotnetVersion();
                var cuda = GetCudaVersion();
                var opencl = GetOpenClVersion();  // stub-ish
                var vulkan = GetVulkanVersion();  // stub-ish
                var directx = GetDirectXVersion(); // stub-ish
                var ffmpeg = GetFfmpegVersion();

                var header = new List<string>
                {
                    $"CPU: {NA(cpuName)}",
                    $"-- Cores/Threads: {NA(cpuC)}/{NA(cpuT)}",
                    $"-- Frequency (MHz): {NA(cpuMHz)}",

                    $"GPU: {NA(gpuName)}",
                    $"-- VRAM (MB): {NA(vramMb)}",
                    $"-- Core Clock (MHz): {NA(gpuCoreMHz)}",
                    $"-- Memory Clock (MHz): {NA(gpuMemMHz)}",

                    $"RAM: {NA(ramGb, "0.##")} GB",
                    $"-- Speed (MHz): {NA(ramMHz)}",
                    $"-- Channels: {NA(ramChannels)}",
                    $"-- ECC: {NA(ramEcc)}",

                    $"C:\\ Drive: {NA(driveModel)}",
                    $"-- Size (GB): {NA(driveSizeGb, "0.##")}",
                    $"-- Read (MB/s): {NA(driveRead, "0.##")}",
                    $"-- Write (MB/s): {NA(driveWrite, "0.##")}",

                    $"Runtimes: {NA(os)}",
                    $"-- .NET: {NA(dotnet)}",
                    $"-- CUDA: {NA(cuda)}",
                    $"-- OpenCL: {NA(opencl)}",
                    $"-- Vulkan: {NA(vulkan)}",
                    $"-- DirectX: {NA(directx)}",
                    $"-- FFMPEG: {NA(ffmpeg)}"
                };

                // Replace first N lines of file with this header
                var lines = SafeReadAllLines().ToList();

                // Ensure file has at least HardwareHeaderLinesCount lines
                while (lines.Count < HardwareHeaderLinesCount)
                {
                    lines.Add("N/A");
                }

                for (int i = 0; i < HardwareHeaderLinesCount && i < header.Count; i++)
                {
                    lines[i] = header[i];
                }

                // If header list is longer than count, we still only write the reserved chunk.
                SafeWriteAllLines(lines.ToArray());

                // Optional separator after header (only if file is very short)
                // (we do NOT force it to avoid messing user edits)
            }
            catch
            {
                // ignore
            }
        }

        public static void WriteExportPath_To_LocalStats(string exportFilePath)
        {
            var lines = new List<string>
            {
                "----- ----- ----- ----- -----",
                $"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                $"Exported Videos to: {exportFilePath}",
                "----- ----- ----- ----- -----",
                ""
            };
            SafeAppendLines(lines, 1);
        }

        public static string? ReadExportPath_From_LocalStats(bool fallbackToMyVideos = false)
        {
            try
            {
                var lines = SafeReadAllLines();
                for (int i = lines.Length - 1; i >= 0; i--)
                {
                    var line = lines[i];
                    if (line.StartsWith("Exported Videos to:", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = line.Split([':'], 2);
                        if (parts.Length == 2)
                        {
                            var path = parts[1].Trim();
                            return string.IsNullOrWhiteSpace(path) ? null : Directory.Exists(path) ? Path.GetFullPath(path) : fallbackToMyVideos ? Environment.GetFolderPath(Environment.SpecialFolder.MyVideos) : null;
                        }
                    }
                }
            }
            catch
            {
                // ignore
            }
            return fallbackToMyVideos ? Environment.GetFolderPath(Environment.SpecialFolder.MyVideos) : null;
        }

        public static void WriteVideoRenderingResult_To_LocalStats(
            string presetKey,
            double renderTimeSeconds,
            double averageFps,
            long totalFrames,
            double totalRenderedFramesMb,
            long outputFileSizeBytes)
        {
            var lines = new List<string>
            {
                "----- ----- ----- ----- -----",
                $"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                $"Preset Key: {presetKey}",
                $"Render Time (s): {renderTimeSeconds:F2}",
                $"Average FPS: {averageFps:F2}",
                $"Total Frames: {totalFrames}",
                $"Total Rendered Frames (MB): {totalRenderedFramesMb:F2}",
                $"Output File Size (MB): {outputFileSizeBytes / (1024.0 * 1024.0):F2}",
                "----- ----- ----- ----- -----",
                ""
            };

            SafeAppendLines(lines);
        }

        public static string[] ReadAllLines_LocalStats(bool copyToClipboard = false)
        {
            string[] lines = [];

            try
            {
                lines = SafeReadAllLines();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
            finally
            {
                if (copyToClipboard && lines.Length > 0)
                {
                    try
                    {
                        // Clipboard requires STA thread; caller should ensure that.
                        System.Windows.Forms.Clipboard.SetText(string.Join(Environment.NewLine, lines));
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }

            return lines;
        }

        public static string? ExportFile_LocalStats(string? exportDirectoryPath = null, bool copyToClipboard = true)
        {
            string? exportFilePath = null;
            if (string.IsNullOrEmpty(exportDirectoryPath))
            {
                exportDirectoryPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }

            try
            {
                var lines = ReadAllLines_LocalStats();
                if (lines.Length == 0)
                {
                    return null;
                }

                string baseFileName = "LocalStats";
                string extension = ".txt";

                int sameNameCount = 0;
                var files = Directory.GetFiles(exportDirectoryPath, $"{baseFileName}*{extension}");
                foreach (var f in files)
                {
                    var fn = Path.GetFileNameWithoutExtension(f);
                    if (fn.StartsWith(baseFileName, StringComparison.OrdinalIgnoreCase))
                    {
                        sameNameCount++;
                    }
                }

                string fileName = baseFileName + (sameNameCount > 0 ? $"({sameNameCount})" : "") + extension;

                exportFilePath = Path.Combine(exportDirectoryPath, fileName);
                File.WriteAllLines(exportFilePath, lines, Encoding.UTF8);

                if (copyToClipboard)
                {
                    try { System.Windows.Forms.Clipboard.SetText(exportFilePath); } catch { }
                }
            }
            catch
            {
                exportFilePath = null;
            }

            return exportFilePath;
        }

        // =====================================================================
        // Hardware / runtime getters (best-effort, null on failure)
        // =====================================================================

        public static string? GetCpuName(bool getModelName = false)
        {
            try
            {
#if WINDOWS
                var name = WmiFirstString("Win32_Processor", "Name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    return null;
                }

                return getModelName ? name.Trim() : name.Trim();
#else
                return null;
#endif
            }
            catch { return null; }
        }

        public static int? GetCoresCount(bool physical = false)
        {
            try
            {
#if WINDOWS
                if (physical)
                {
                    var cores = WmiFirstInt("Win32_Processor", "NumberOfCores");
                    return cores;
                }
                else
                {
                    var threads = WmiFirstInt("Win32_Processor", "NumberOfLogicalProcessors");
                    return threads ?? Environment.ProcessorCount;
                }
#else
                return physical ? null : Environment.ProcessorCount;
#endif
            }
            catch { return physical ? null : Environment.ProcessorCount; }
        }

        public static int? GetCpuFrequencyMHz()
        {
            try
            {
#if WINDOWS
                // CurrentClockSpeed exists on Win32_Processor
                return WmiFirstInt("Win32_Processor", "CurrentClockSpeed");
#else
                return null;
#endif
            }
            catch { return null; }
        }

        public static string? GetGpuName(bool getModelName = false)
        {
            try
            {
                // Prefer nvidia-smi if NVIDIA, otherwise WMI fallback
                var nvsmi = GetNvidiaSmiGpuName();
                if (!string.IsNullOrWhiteSpace(nvsmi))
                {
                    return nvsmi;
                }

#if WINDOWS
                var name = WmiFirstString("Win32_VideoController", "Name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    return null;
                }

                return name.Trim();
#else
                return null;
#endif
            }
            catch { return null; }
        }

        public static double? GetGpuVramMb()
        {
            try
            {
                // Prefer nvidia-smi if available
                var mb = GetNvidiaSmiMemoryTotalMb();
                if (mb.HasValue)
                {
                    return mb.Value;
                }

#if WINDOWS
                // AdapterRAM is bytes (often)
                var bytes = WmiFirstLong("Win32_VideoController", "AdapterRAM");
                if (!bytes.HasValue || bytes.Value <= 0)
                {
                    return null;
                }

                return bytes.Value / (1024.0 * 1024.0);
#else
                return null;
#endif
            }
            catch { return null; }
        }

        public static int? GetGpuClockRate(bool memoryClock = false)
        {
            try
            {
                // NVIDIA only (best effort)
                return GetNvidiaSmiClockMhz(memoryClock);
            }
            catch { return null; }
        }

        public static double? GetRamSizeGb()
        {
            try
            {
#if WINDOWS
                // TotalPhysicalMemory is bytes
                var bytes = WmiFirstLong("Win32_ComputerSystem", "TotalPhysicalMemory");
                if (!bytes.HasValue || bytes.Value <= 0)
                {
                    return null;
                }

                return bytes.Value / (1024.0 * 1024.0 * 1024.0);
#else
                return null;
#endif
            }
            catch { return null; }
        }

        public static int? GetRamSpeedMHz()
        {
            try
            {
#if WINDOWS
                // Many sticks -> pick max or most common. We'll use max.
                var speeds = WmiAllInts("Win32_PhysicalMemory", "ConfiguredClockSpeed")
                    .Concat(WmiAllInts("Win32_PhysicalMemory", "Speed"))
                    .Where(x => x.HasValue && x.Value > 0)
                    .Select(x => x!.Value)
                    .ToList();

                if (speeds.Count == 0)
                {
                    return null;
                }

                return speeds.Max();
#else
                return null;
#endif
            }
            catch { return null; }
        }

        public static string? GetRamChannelMode()
        {
            try
            {
                // Reliable channel mode is not consistently exposed via WMI.
                // Best-effort: infer by number of modules and typical dual/quad patterns.
#if WINDOWS
                var modules = WmiAllStrings("Win32_PhysicalMemory", "Capacity").Count();
                if (modules <= 0)
                {
                    return null;
                }

                return modules switch
                {
                    1 => "Single (likely)",
                    2 => "Dual (likely)",
                    4 => "Quad (possible)",
                    _ => $"{modules} modules"
                };
#else
                return null;
#endif
            }
            catch { return null; }
        }

        public static string? GetRamEccState()
        {
            try
            {
                // ECC detection via WMI is inconsistent. We expose "Unknown" if we can't tell.
#if WINDOWS
                // There is Win32_PhysicalMemory.ErrorCorrectionType but often empty.
                var ect = WmiFirstInt("Win32_PhysicalMemory", "ErrorCorrectionType");
                if (!ect.HasValue)
                {
                    return "Unknown";
                }

                // Rough mapping: 0/1/2 vary by vendor. We'll be conservative.
                return ect.Value switch
                {
                    6 => "ECC",
                    7 => "ECC",
                    _ => "Unknown"
                };
#else
                return null;
#endif
            }
            catch { return null; }
        }

        public static string? GetSystemDriveModelAndInterface()
        {
            try
            {
#if WINDOWS
                // Find disk containing C: (best effort)
                // WMI association queries are painful; we do a simpler heuristic:
                // pick the first DiskDrive (often fine) + InterfaceType.
                var model = WmiFirstString("Win32_DiskDrive", "Model");
                var iface = WmiFirstString("Win32_DiskDrive", "InterfaceType");
                if (string.IsNullOrWhiteSpace(model) && string.IsNullOrWhiteSpace(iface))
                {
                    return null;
                }

                return $"{NA(model)} ({NA(iface)})";
#else
                return null;
#endif
            }
            catch { return null; }
        }

        public static double? GetSystemDriveSizeGb()
        {
            try
            {
                var drive = new DriveInfo("C");
                if (!drive.IsReady)
                {
                    return null;
                }

                return drive.TotalSize / (1024.0 * 1024.0 * 1024.0);
            }
            catch { return null; }
        }

        // These are hard to measure reliably without dedicated benchmarks. Keep as stubs for now.
        public static double? GetSystemDriveReadSpeedMb_s() => null;
        public static double? GetSystemDriveWriteSpeedMb_s() => null;

        public static string? GetOsInfo()
        {
            try
            {
                return $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})";
            }
            catch { return null; }
        }

        public static string? GetDotnetVersion()
        {
            try
            {
                // RuntimeInformation.FrameworkDescription is best for display
                return RuntimeInformation.FrameworkDescription;
            }
            catch
            {
                try { return Environment.Version.ToString(); } catch { return null; }
            }
        }

        public static string? GetCudaVersion()
        {
            try
            {
                // easiest: parse "CUDA Version: X.Y" from nvidia-smi
                var s = TryRunProcessCapture("nvidia-smi", "", timeoutMs: 1500);
                if (string.IsNullOrWhiteSpace(s))
                {
                    return null;
                }

                // Look for "CUDA Version:"
                var idx = s.IndexOf("CUDA Version", StringComparison.OrdinalIgnoreCase);
                if (idx < 0)
                {
                    return null;
                }

                // crude parse
                var sub = s.Substring(idx);
                var colon = sub.IndexOf(':');
                if (colon < 0)
                {
                    return null;
                }

                var after = sub[(colon + 1)..].Trim();
                var token = after.Split([' ', '|', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)
                                 .FirstOrDefault();

                return string.IsNullOrWhiteSpace(token) ? null : token;
            }
            catch { return null; }
        }

        public static string? GetOpenClVersion() => null;   // stub – add later if you integrate an OpenCL loader
        public static string? GetVulkanVersion() => null;   // stub – could be via vulkaninfo if installed
        public static string? GetDirectXVersion() => null;  // stub – could be via dxdiag output

        public static string? GetFfmpegVersion()
        {
            try
            {
                var s = TryRunProcessCapture("ffmpeg", "-version", timeoutMs: 1500);
                if (string.IsNullOrWhiteSpace(s))
                {
                    return null;
                }

                // first line: "ffmpeg version X ..."
                var first = s.Split('\n').FirstOrDefault()?.Trim();
                if (string.IsNullOrWhiteSpace(first))
                {
                    return null;
                }

                // Try extract second token
                var parts = first.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3 && parts[0].Equals("ffmpeg", StringComparison.OrdinalIgnoreCase) && parts[1].Equals("version", StringComparison.OrdinalIgnoreCase))
                {
                    return parts[2];
                }

                return first;
            }
            catch { return null; }
        }

        // =====================================================================
        // NVIDIA-SMI helpers (optional, best-effort)
        // =====================================================================
        private static string? GetNvidiaSmiGpuName()
        {
            try
            {
                // --query-gpu=name --format=csv,noheader
                var s = TryRunProcessCapture("nvidia-smi", "--query-gpu=name --format=csv,noheader,nounits", 1500);
                var line = s?.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                return string.IsNullOrWhiteSpace(line) ? null : line.Trim();
            }
            catch { return null; }
        }

        private static double? GetNvidiaSmiMemoryTotalMb()
        {
            try
            {
                var s = TryRunProcessCapture("nvidia-smi", "--query-gpu=memory.total --format=csv,noheader,nounits", 1500);
                var line = s?.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (string.IsNullOrWhiteSpace(line))
                {
                    return null;
                }

                if (double.TryParse(line.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var mb))
                {
                    return mb;
                }

                if (double.TryParse(line.Trim(), NumberStyles.Any, CultureInfo.CurrentCulture, out mb))
                {
                    return mb;
                }

                return null;
            }
            catch { return null; }
        }

        private static int? GetNvidiaSmiClockMhz(bool memoryClock)
        {
            try
            {
                string field = memoryClock ? "clocks.mem" : "clocks.gr";
                var s = TryRunProcessCapture("nvidia-smi", $"--query-gpu={field} --format=csv,noheader,nounits", 1500);
                var line = s?.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (string.IsNullOrWhiteSpace(line))
                {
                    return null;
                }

                if (int.TryParse(line.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var mhz))
                {
                    return mhz;
                }

                if (int.TryParse(line.Trim(), NumberStyles.Any, CultureInfo.CurrentCulture, out mhz))
                {
                    return mhz;
                }

                return null;
            }
            catch { return null; }
        }

        // =====================================================================
        // Process runner
        // =====================================================================
        private static string? TryRunProcessCapture(string fileName, string args, int timeoutMs)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = ResolveExternalToolPath(fileName),
                    Arguments = args,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var p = new Process { StartInfo = psi };
                if (!p.Start())
                {
                    return null;
                }

                var sb = new StringBuilder();

                // read async-ish
                string stdout = p.StandardOutput.ReadToEnd();
                string stderr = p.StandardError.ReadToEnd();

                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(entireProcessTree: true); } catch { }
                    return null;
                }

                if (!string.IsNullOrWhiteSpace(stdout))
                {
                    sb.AppendLine(stdout);
                }

                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    sb.AppendLine(stderr);
                }

                var text = sb.ToString().Trim();
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }
            catch
            {
                return null;
            }
        }

        // =====================================================================
        // WMI helpers (Windows only)
        // =====================================================================
#if WINDOWS
        private static string? WmiFirstString(string wmiClass, string prop)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher($"SELECT {prop} FROM {wmiClass}");
                using var results = searcher.Get();
                foreach (ManagementObject mo in results)
                {
                    var v = mo[prop]?.ToString();
                    if (!string.IsNullOrWhiteSpace(v))
                    {
                        return v;
                    }
                }
                return null;
            }
            catch { return null; }
        }

        private static int? WmiFirstInt(string wmiClass, string prop)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher($"SELECT {prop} FROM {wmiClass}");
                using var results = searcher.Get();
                foreach (ManagementObject mo in results)
                {
                    var o = mo[prop];
                    if (o is null)
                    {
                        continue;
                    }

                    if (o is int i)
                    {
                        return i;
                    }

                    if (int.TryParse(o.ToString(), out i))
                    {
                        return i;
                    }
                }
                return null;
            }
            catch { return null; }
        }

        private static long? WmiFirstLong(string wmiClass, string prop)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher($"SELECT {prop} FROM {wmiClass}");
                using var results = searcher.Get();
                foreach (ManagementObject mo in results)
                {
                    var o = mo[prop];
                    if (o is null)
                    {
                        continue;
                    }

                    if (o is long l)
                    {
                        return l;
                    }

                    if (long.TryParse(o.ToString(), out l))
                    {
                        return l;
                    }
                }
                return null;
            }
            catch { return null; }
        }

        private static IEnumerable<int?> WmiAllInts(string wmiClass, string prop)
        {
            try
            {
                var list = new List<int?>();

                using (var searcher = new ManagementObjectSearcher($"SELECT {prop} FROM {wmiClass}"))
                using (var results = searcher.Get())
                {
                    foreach (ManagementObject mo in results)
                    {
                        object? o = null;
                        try { o = mo[prop]; } catch { /* ignore per-row */ }

                        if (o is null) { list.Add(null); continue; }

                        if (o is int i) { list.Add(i); continue; }
                        if (int.TryParse(o.ToString(), out i)) { list.Add(i); continue; }

                        list.Add(null);
                    }
                }

                return list;
            }
            catch
            {
                return [];
            }
        }

        private static IEnumerable<string?> WmiAllStrings(string wmiClass, string prop)
        {
            try
            {
                var list = new List<string?>();

                using (var searcher = new ManagementObjectSearcher($"SELECT {prop} FROM {wmiClass}"))
                using (var results = searcher.Get())
                {
                    foreach (ManagementObject mo in results)
                    {
                        try
                        {
                            list.Add(mo[prop]?.ToString());
                        }
                        catch
                        {
                            list.Add(null);
                        }
                    }
                }

                return list;
            }
            catch
            {
                return [];
            }
        }
#endif

    }
}
