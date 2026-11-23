using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace ModularAudience.Video;

/// <summary>
/// Represents a video file with lazy, memory-friendly frame extraction.
/// Analogous to <see cref="ModularAudience.Audio.AudioObj"/> but for video.
/// Frames are extracted on demand via FFmpeg and are NOT cached in full — only
/// a small sliding window of recently decoded frames is kept to avoid RAM pressure.
/// </summary>
public sealed class VideoObj : IDisposable
{
    private static readonly Regex DurationRegex = new(@"Duration:\s*(\d{2,}):(\d{2}):(\d{2})\.(\d{2,3})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly string _filePath;
    private readonly string _ffmpegPath;
    private readonly SemaphoreSlim _decodeGate = new(1, 1);
    private readonly SemaphoreSlim _probeGate = new(1, 1);
    private readonly object _disposeLock = new();
    private readonly Dictionary<double, Bitmap> _frameCache = [];
    private readonly List<double> _frameCacheOrder = [];
    private const int MaxCachedFrames = 8;
    private bool _disposed;
    private TimeSpan _duration = TimeSpan.Zero;
    private int _width = 0;
    private int _height = 0;
    private double _frameRate = 0;
    private bool _probeDone = false;
    private bool _hasAudio;

    public string FilePath => _filePath;
    public string Name => Path.GetFileNameWithoutExtension(_filePath);

    public TimeSpan Duration
    {
        get
        {
            EnsureProbed();
            return _duration;
        }
    }

    public int Width
    {
        get
        {
            EnsureProbed();
            return _width;
        }
    }

    public int Height
    {
        get
        {
            EnsureProbed();
            return _height;
        }
    }

    public double FrameRate
    {
        get
        {
            EnsureProbed();
            return _frameRate > 0 ? _frameRate : 30.0;
        }
    }

    public bool HasVideo => Width > 0 && Height > 0 && Duration > TimeSpan.Zero;
    public bool HasAudio { get { EnsureProbed(); return _hasAudio; } }

    public bool IsDisposed => _disposed;

    public VideoObj(string filePath, string? ffmpegPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Video file not found.", filePath);
        }

        _filePath = Path.GetFullPath(filePath);
        _ffmpegPath = ffmpegPath ?? ResolveFfmpegPath();
    }

    private void EnsureProbed()
    {
        if (_probeDone)
        {
            return;
        }

        ProbeAsync().GetAwaiter().GetResult();
    }

    public async Task ProbeAsync(CancellationToken cancellationToken = default)
    {
        if (_probeDone)
        {
            return;
        }

        await _probeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_probeDone)
            {
                return;
            }

            string? ffprobePath = FindFfprobe();
            if (ffprobePath != null)
            {
                await ProbeWithFfprobeAsync(ffprobePath, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await ProbeWithFfmpegAsync(cancellationToken).ConfigureAwait(false);
            }

            if (_width <= 0 || _height <= 0 || _duration <= TimeSpan.Zero)
            {
                throw new InvalidDataException("FFmpeg could not determine the video's dimensions and duration.");
            }

            if (_frameRate <= 0)
            {
                _frameRate = 30;
            }

            _probeDone = true;
        }
        finally { _probeGate.Release(); }
    }

    private async Task ProbeWithFfprobeAsync(string ffprobePath, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffprobePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        AddArguments(startInfo, "-v", "error", "-show_entries", "format=duration:stream=codec_type,width,height,avg_frame_rate,r_frame_rate", "-of", "json", _filePath);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start ffprobe.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            Task<string> errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            string output = await outputTask.ConfigureAwait(false);
            string error = await errorTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                throw new InvalidDataException($"ffprobe failed: {error.Trim()}");
            }

            using JsonDocument document = JsonDocument.Parse(output);
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("format", out JsonElement format)
                && format.TryGetProperty("duration", out JsonElement duration)
                && double.TryParse(duration.GetString(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double seconds)
                && double.IsFinite(seconds) && seconds > 0)
            {
                _duration = TimeSpan.FromSeconds(seconds);
            }

            if (!root.TryGetProperty("streams", out JsonElement streams))
            {
                return;
            }

            foreach (JsonElement stream in streams.EnumerateArray())
            {
                string? type = stream.TryGetProperty("codec_type", out JsonElement codecType) ? codecType.GetString() : null;
                if (type == "audio")
                {
                    _hasAudio = true;
                }

                if (type != "video" || _width > 0)
                {
                    continue;
                }

                if (stream.TryGetProperty("width", out JsonElement width))
                {
                    _width = width.GetInt32();
                }

                if (stream.TryGetProperty("height", out JsonElement height))
                {
                    _height = height.GetInt32();
                }

                string rate = stream.TryGetProperty("avg_frame_rate", out JsonElement avg) ? avg.GetString() ?? "0/0" : "0/0";
                if (!TryParseFrameRate(rate, out _frameRate) && stream.TryGetProperty("r_frame_rate", out JsonElement real))
                {
                    TryParseFrameRate(real.GetString() ?? "0/0", out _frameRate);
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException("ffprobe did not finish within 15 seconds.");
        }
        catch { TryKill(process); throw; }
    }

    private async Task ProbeWithFfmpegAsync(CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        AddArguments(startInfo, "-hide_banner", "-i", _filePath);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start FFmpeg.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            string output = await process.StandardError.ReadToEndAsync(timeout.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            Match duration = DurationRegex.Match(output);
            if (duration.Success)
            {
                int h = int.Parse(duration.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                int m = int.Parse(duration.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                int s = int.Parse(duration.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
                string fraction = duration.Groups[4].Value.PadRight(3, '0');
                _duration = TimeSpan.FromHours(h) + TimeSpan.FromMinutes(m) + TimeSpan.FromSeconds(s)
                    + TimeSpan.FromMilliseconds(int.Parse(fraction, System.Globalization.CultureInfo.InvariantCulture));
            }
            Match dimensions = Regex.Match(output, @"\b(\d{2,5})x(\d{2,5})\b");
            if (dimensions.Success)
            {
                _width = int.Parse(dimensions.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                _height = int.Parse(dimensions.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            }
            Match rate = Regex.Match(output, @"(?<rate>\d+(?:\.\d+)?)\s*fps\b");
            if (rate.Success)
            {
                double.TryParse(rate.Groups["rate"].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out _frameRate);
            }

            _hasAudio = Regex.IsMatch(output, @"\bAudio:\s", RegexOptions.CultureInvariant);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException("FFmpeg could not probe the video within 15 seconds.");
        }
        catch { TryKill(process); throw; }
    }

    /// <summary>
    /// Extracts a single frame at the given time as a low-quality thumbnail.
    /// The returned <see cref="Bitmap"/> must be disposed by the caller.
    /// </summary>
    public async Task<Bitmap?> ExtractFrameAsync(double seconds, int maxWidth = 160, CancellationToken cancellationToken = default)
    {
        await ProbeAsync(cancellationToken).ConfigureAwait(false);
        if (_disposed)
        {
            return null;
        }

        if (maxWidth is < 16 or > 4096)
        {
            throw new ArgumentOutOfRangeException(nameof(maxWidth));
        }

        seconds = Math.Clamp(seconds, 0, Duration.TotalSeconds);

        lock (_disposeLock)
        {
            if (_disposed)
            {
                return null;
            }

            if (_frameCache.TryGetValue(seconds, out var cached))
            {
                _frameCacheOrder.Remove(seconds);
                _frameCacheOrder.Add(seconds);
                return new Bitmap(cached); // caller owns the copy
            }
        }

        await _decodeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return null;
            }

            lock (_disposeLock)
            {
                if (_disposed)
                {
                    return null;
                }

                if (_frameCache.TryGetValue(seconds, out var cached2))
                {
                    _frameCacheOrder.Remove(seconds);
                    _frameCacheOrder.Add(seconds);
                    return new Bitmap(cached2);
                }
            }

            string scale = $"scale={maxWidth}:-2";
            var startInfo = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            AddArguments(startInfo, "-v", "error", "-ss", seconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
                "-i", _filePath, "-frames:v", "1", "-vf", scale, "-f", "image2pipe", "-vcodec", "bmp", "-");

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start FFmpeg for frame extraction.");

            using var ms = new MemoryStream();
            Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            try
            {
                await process.StandardOutput.BaseStream.CopyToAsync(ms, 81920, cancellationToken).ConfigureAwait(false);
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch { TryKill(process); throw; }
            await errorTask.ConfigureAwait(false);
            if (process.ExitCode != 0 || ms.Length == 0)
            {
                return null;
            }

            ms.Position = 0;
            using var decoded = new Bitmap(ms);
            var bitmap = new Bitmap(decoded);

            lock (_disposeLock)
            {
                if (_disposed) { bitmap.Dispose(); return null; }
                if (_frameCache.Count >= MaxCachedFrames)
                {
                    double oldest = _frameCacheOrder[0];
                    _frameCacheOrder.RemoveAt(0);
                    if (_frameCache.TryGetValue(oldest, out var oldBmp))
                    {
                        oldBmp.Dispose();
                    }

                    _frameCache.Remove(oldest);
                }
                _frameCache[seconds] = bitmap;
                _frameCacheOrder.Add(seconds);
            }

            return new Bitmap(bitmap); // caller owns a copy
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return null; }
        finally { _decodeGate.Release(); }
    }

    /// <summary>Extracts the first audio stream to a unique temporary PCM WAV file.</summary>
    public Task<string?> ExtractAudioToTempWavAsync(CancellationToken cancellationToken = default) =>
        ExtractAudioToTempWavAsync(null, cancellationToken);

    public async Task<string?> ExtractAudioToTempWavAsync(IProgress<double>? progress, CancellationToken cancellationToken = default)
    {
        await ProbeAsync(cancellationToken).ConfigureAwait(false);
        if (_disposed || !_hasAudio)
        {
            return null;
        }

        string tempPath = Path.Combine(Path.GetTempPath(), $"ModularAudienceVideoAudio_{Guid.NewGuid():N}.wav");
        var startInfo = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        AddArguments(startInfo, "-hide_banner", "-nostats", "-progress", "pipe:1", "-i", _filePath,
            "-map", "0:a:0", "-vn", "-acodec", "pcm_s16le", "-ar", "44100", "-ac", "2", "-y", tempPath);

        try
        {
            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start FFmpeg for audio extraction.");
            Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            try
            {
                string? line;
                while ((line = await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
                {
                    if (line.StartsWith("out_time_ms=", StringComparison.Ordinal)
                        && long.TryParse(line.AsSpan("out_time_ms=".Length), out long microseconds)
                        && _duration.TotalSeconds > 0)
                    {
                        progress?.Report(Math.Clamp(microseconds / 1_000_000d / _duration.TotalSeconds, 0, 1));
                    }
                }
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch { TryKill(process); throw; }

            string error = await errorTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"FFmpeg audio extraction failed (exit code {process.ExitCode}): {error.Trim()}");
            }

            if (!File.Exists(tempPath) || new FileInfo(tempPath).Length == 0)
            {
                throw new InvalidDataException("FFmpeg completed without producing an audio file.");
            }

            progress?.Report(1);
            return tempPath;
        }
        catch
        {
            try { if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    private string? FindFfprobe()
    {
        string? directory = Path.GetDirectoryName(_ffmpegPath);
        if (!string.IsNullOrEmpty(directory))
        {
            string candidate = Path.Combine(directory, "ffprobe.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }

    private static bool TryParseFrameRate(string value, out double frameRate)
    {
        string[] parts = value.Split('/', 2);
        if (parts.Length == 2
            && double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double numerator)
            && double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double denominator)
            && denominator != 0)
        {
            frameRate = numerator / denominator;
            return double.IsFinite(frameRate) && frameRate > 0;
        }
        frameRate = double.TryParse(value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double parsed) && double.IsFinite(parsed) && parsed > 0 ? parsed : 0;
        return frameRate > 0;
    }

    private static void AddArguments(ProcessStartInfo startInfo, params string[] arguments)
    {
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
    }

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException) { }
    }

    private static string ResolveFfmpegPath()
    {
        // 1. Ytdlp-managed ffmpeg directory
        string ytdlpRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ModularAudience", "binaries_ytdlp");
        try
        {
            if (Directory.Exists(ytdlpRoot))
            {
                foreach (var dir in Directory.EnumerateDirectories(ytdlpRoot, "ffmpeg-*", SearchOption.TopDirectoryOnly))
                {
                    var candidate = Path.Combine(dir, "ffmpeg.exe");
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
        }
        catch { }

        // 2. Beside entry assembly
        var local = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        if (File.Exists(local))
        {
            return local;
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
            catch { }
        }

        return "ffmpeg"; // rely on PATH
    }

    private static string Escape(string argument) => argument.Replace("\"", "\\\"");

    private void Dispose(bool disposing)
    {
        lock (_disposeLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        if (disposing)
        {
            lock (_disposeLock)
            {
                foreach (var kvp in _frameCache)
                {
                    kvp.Value.Dispose();
                }

                _frameCache.Clear();
                _frameCacheOrder.Clear();
            }
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
