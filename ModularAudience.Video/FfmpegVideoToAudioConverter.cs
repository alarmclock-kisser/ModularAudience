using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using ModularAudience.Shared;

namespace ModularAudience.Video;

/// <summary>
/// FFmpeg-based implementation of <see cref="IVideoToAudioConverter"/>.
/// Extracts the audio track from video files (.mp4, .mov, .avi, .flv) and writes it
/// as a WAV file (PCM 16-bit) that can be loaded directly by <see cref="ModularAudience.Audio.AudioObj"/>.
/// </summary>
/// <remarks>
/// Thread-safe: each conversion spawns its own ffmpeg process. The <see cref="SemaphoreSlim"/>
/// limits the number of concurrent conversions. Output file paths are caller-provided and
/// must be unique per concurrent conversion to avoid file-access conflicts.
/// </remarks>
public sealed class FfmpegVideoToAudioConverter : IVideoToAudioConverter, IDisposable
{
    private static readonly TimeSpan FfmpegProcessTimeout = TimeSpan.FromMinutes(30);
    private static readonly Regex TimeRegex = new(@"time=(\d{2}):(\d{2}):(\d{2})\.(\d{2,3})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly VideoConverterOptions _options;
    private readonly string _ffmpegPath;
    private readonly SemaphoreSlim _concurrencyGate;
    private readonly object _disposalLock = new();
    private bool _disposed;

    public FfmpegVideoToAudioConverter(VideoConverterOptions? options = null)
    {
        _options = options ?? new VideoConverterOptions();
        _options.EnsureOutputDirectory();

        _ffmpegPath = ResolveFfmpegPath(_options.FfmpegPath)
            ?? throw new FileNotFoundException(
                "ffmpeg.exe was not found. Please install FFmpeg or set VideoConverterOptions.FfmpegPath to the ffmpeg.exe location.");

        int maxWorkers = Math.Max(1, _options.MaxWorkers);
        _concurrencyGate = new SemaphoreSlim(maxWorkers, maxWorkers);
    }

    public async Task<VideoConversionResult> ConvertAsync(string videoFilePath, string outputAudioFilePath,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(videoFilePath))
        {
            return VideoConversionResult.FailureResult(videoFilePath, "Video file path is empty.");
        }

        if (!File.Exists(videoFilePath))
        {
            return VideoConversionResult.FailureResult(videoFilePath, $"Video file not found: {videoFilePath}");
        }

        if (!VideoFormat.IsSupported(videoFilePath))
        {
            return VideoConversionResult.FailureResult(videoFilePath,
                $"Unsupported video format: {Path.GetExtension(videoFilePath)}. Supported: {string.Join(", ", VideoFormat.SupportedExtensions)}");
        }

        if (string.IsNullOrWhiteSpace(outputAudioFilePath))
        {
            return VideoConversionResult.FailureResult(videoFilePath, "Output audio file path is empty.");
        }

        var outputDir = Path.GetDirectoryName(outputAudioFilePath);
        if (!string.IsNullOrWhiteSpace(outputDir) && !Directory.Exists(outputDir))
        {
            try { Directory.CreateDirectory(outputDir); }
            catch (Exception ex) { return VideoConversionResult.FailureResult(videoFilePath, $"Cannot create output directory: {ex.Message}"); }
        }

        await _concurrencyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await ConvertSingleAsync(videoFilePath, outputAudioFilePath, progress, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _concurrencyGate.Release();
        }
    }

    public async Task<IReadOnlyList<VideoConversionResult>> ConvertManyAsync(IEnumerable<string> videoFilePaths,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var paths = videoFilePaths?.Where(p => !string.IsNullOrWhiteSpace(p)).ToList() ?? [];
        var results = new VideoConversionResult[paths.Count];

        if (paths.Count == 0)
        {
            return results;
        }

        var completedCount = 0;
        var tasks = new Task[paths.Count];

        for (int i = 0; i < paths.Count; i++)
        {
            int index = i; // capture for closure
            tasks[i] = Task.Run(async () =>
            {
                string outputPath = BuildOutputPath(paths[index]);
                var result = await ConvertAsync(paths[index], outputPath, null, cancellationToken)
                    .ConfigureAwait(false);
                results[index] = result;

                Interlocked.Increment(ref completedCount);
                if (progress != null && !cancellationToken.IsCancellationRequested)
                {
                    double frac = (double)completedCount / paths.Count;
                    progress.Report(frac);
                }
            }, cancellationToken);
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return results;
    }

    private async Task<VideoConversionResult> ConvertSingleAsync(string videoFilePath, string outputAudioFilePath,
        IProgress<double>? progress, CancellationToken cancellationToken)
    {
        TimeSpan duration;
        try
        {
            duration = await ProbeDurationAsync(videoFilePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return VideoConversionResult.FailureResult(videoFilePath, $"Failed to probe video duration: {ex.Message}");
        }

        var arguments = BuildFfmpegArguments(videoFilePath, outputAudioFilePath);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(FfmpegProcessTimeout);

        var startInfo = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardErrorEncoding = Encoding.UTF8,
        };

        double totalSeconds = duration.TotalSeconds;

        try
        {
            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return VideoConversionResult.FailureResult(videoFilePath, "Failed to start ffmpeg process.");
            }

            // Read stderr asynchronously to capture progress updates in real time.
            // ffmpeg writes "time=HH:MM:SS.MS" to stderr for every frame processed.
            var stderrReader = process.StandardError;
            var stderrTask = Task.Run(async () =>
            {
                string? line;
                while ((line = await stderrReader.ReadLineAsync().ConfigureAwait(false)) != null)
                {
                    if (progress != null && totalSeconds > 0)
                    {
                        var match = TimeRegex.Match(line);
                        if (match.Success)
                        {
                            try
                            {
                                int h = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                                int m = int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                                int s = int.Parse(match.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
                                int ms = int.Parse(match.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture);
                                double current = h * 3600 + m * 60 + s + ms / 1000.0;
                                double frac = Math.Clamp(current / totalSeconds, 0.0, 1.0);
                                progress.Report(frac);
                            }
                            catch { /* ignore parse errors on progress lines */ }
                        }
                    }
                }
            }, cts.Token);

            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            await stderrTask.ConfigureAwait(false);

            if (cts.Token.IsCancellationRequested)
            {
                try { process.Kill(); } catch { }
                return VideoConversionResult.FailureResult(videoFilePath, "Conversion was cancelled.");
            }

            if (process.ExitCode != 0)
            {
                string stderr = await ReadStderrSafeAsync(process).ConfigureAwait(false);
                return VideoConversionResult.FailureResult(videoFilePath,
                    $"ffmpeg exited with code {process.ExitCode}. {stderr}");
            }

            if (!File.Exists(outputAudioFilePath) || new FileInfo(outputAudioFilePath).Length == 0)
            {
                return VideoConversionResult.FailureResult(videoFilePath, "ffmpeg did not produce an output audio file.");
            }

            double bitrateKbps = CalculateBitrateKbps(outputAudioFilePath, duration);
            return VideoConversionResult.SuccessResult(videoFilePath, outputAudioFilePath, duration, bitrateKbps);
        }
        catch (OperationCanceledException)
        {
            return VideoConversionResult.FailureResult(videoFilePath, "Conversion was cancelled.");
        }
        catch (Exception ex)
        {
            return VideoConversionResult.FailureResult(videoFilePath, $"Conversion failed: {ex.Message}");
        }
    }

    private static string BuildOutputPath(string videoFilePath)
    {
        string fileName = Path.GetFileNameWithoutExtension(videoFilePath) + ".wav";
        string outputDir = Path.GetTempPath();
        string outputPath = Path.Combine(outputDir, fileName);
        // Ensure uniqueness if a file with the same name already exists.
        if (File.Exists(outputPath))
        {
            outputPath = Path.Combine(outputDir, $"{Path.GetFileNameWithoutExtension(videoFilePath)}_{Guid.NewGuid():N}.wav");
        }
        return outputPath;
    }

    private string BuildFfmpegArguments(string inputPath, string outputPath)
    {
        // -vn: disable video recording (extract audio only)
        // -acodec pcm_s16le: PCM 16-bit signed little-endian (lossless, NAudio can read it)
        // -ar: sample rate
        // -ac: channel count
        // -y: overwrite output file
        return $"-i \"{EscapeArgument(inputPath)}\" -vn -acodec pcm_s16le -ar {_options.SampleRate} -ac {_options.Channels} -y \"{EscapeArgument(outputPath)}\"";
    }

    private static string EscapeArgument(string argument)
    {
        // ffmpeg on Windows accepts arguments directly; wrap in quotes to handle spaces.
        return argument.Replace("\"", "\\\"");
    }

    private async Task<TimeSpan> ProbeDurationAsync(string videoFilePath, CancellationToken cancellationToken)
    {
        string ffprobePath = Path.Combine(Path.GetDirectoryName(_ffmpegPath)!, "ffprobe.exe");
        if (!File.Exists(ffprobePath))
        {
            // Fallback: use ffmpeg itself to probe (slower but works without ffprobe).
            return await ProbeDurationWithFfmpegAsync(videoFilePath, cancellationToken);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = ffprobePath,
            Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{EscapeArgument(videoFilePath)}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start ffprobe.");
        }

        string output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (double.TryParse(output.Trim(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double seconds))
        {
            return TimeSpan.FromSeconds(seconds);
        }

        throw new InvalidDataException($"Could not parse duration from ffprobe output: '{output}'");
    }

    private static async Task<TimeSpan> ProbeDurationWithFfmpegAsync(string videoFilePath, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-i \"{EscapeArgument(videoFilePath)}\" -f null -",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start ffmpeg for duration probe.");
        }

        string stderr = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        // ffmpeg prints "Duration: HH:MM:SS.MS" in its startup banner.
        var match = Regex.Match(stderr, @"Duration:\s*(\d{2}):(\d{2}):(\d{2})\.(\d{2})",
            RegexOptions.CultureInvariant);
        if (match.Success)
        {
            int h = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            int m = int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            int s = int.Parse(match.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
            int ms = int.Parse(match.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture);
            return new TimeSpan(h, m, s, 0, ms * 10);
        }

        throw new InvalidDataException("Could not probe video duration.");
    }

    private static double CalculateBitrateKbps(string audioFilePath, TimeSpan duration)
    {
        try
        {
            var info = new FileInfo(audioFilePath);
            long bytes = info.Length;
            double totalSeconds = Math.Max(duration.TotalSeconds, 0.001);
            double bitsPerSecond = (bytes * 8.0) / totalSeconds;
            return bitsPerSecond / 1000.0;
        }
        catch { return 0.0; }
    }

    private static async Task<string> ReadStderrSafeAsync(Process process)
    {
        try
        {
            return await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
        }
        catch { return string.Empty; }
    }

    /// <summary>
    /// Attempts to locate ffmpeg.exe: explicit path, then Ytdlp binary directory,
    /// then common installation folders, then PATH.
    /// </summary>
    private static string? ResolveFfmpegPath(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
        {
            return Path.GetFullPath(explicitPath);
        }

        // 1. Ytdlp-managed ffmpeg (ModularAudience.Ytdlp installs ffmpeg here)
        string ytdlpFfmpegDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ModularAudience", "binaries_ytdlp");
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(ytdlpFfmpegDir, "ffmpeg-*", SearchOption.TopDirectoryOnly))
            {
                var candidate = Path.Combine(dir, "ffmpeg.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }
        catch { /* directory may not exist */ }

        // 2. Same directory as the entry assembly or common local folder
        string? exeDir = AppContext.BaseDirectory;
        var localCandidate = Path.Combine(exeDir, "ffmpeg.exe");
        if (File.Exists(localCandidate))
        {
            return localCandidate;
        }

        // 3. PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var entry in pathEnv.Split(Path.PathSeparator))
        {
            var dir = entry.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(dir) || !Path.IsPathFullyQualified(dir))
            {
                continue;
            }

            try
            {
                var candidate = Path.Combine(dir, "ffmpeg.exe");
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            catch { /* ignore invalid entries */ }
        }

        return null;
    }

    private void Dispose(bool disposing)
    {
        lock (_disposalLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        if (disposing)
        {
            _concurrencyGate.Dispose();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
