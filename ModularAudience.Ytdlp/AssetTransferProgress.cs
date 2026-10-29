using System.Globalization;
using System.Diagnostics;

namespace ModularAudience.Ytdlp;

internal sealed class AssetTransferProgress(string name, long totalBytes, IProgress<string>? progress,
    IProgress<YtdlpDownloadProgress>? downloadProgress = null)
{
    private long lastTick = long.MinValue;
    private long lastBytes;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private TimeSpan lastElapsed;

    internal void Report(long downloadedBytes, bool completed = false)
    {
        long now = Environment.TickCount64;
        if (!completed && lastTick != long.MinValue && now - lastTick < 250) return;
        lastTick = now;
        var elapsed = clock.Elapsed;
        double seconds = (elapsed - lastElapsed).TotalSeconds;
        double speed = seconds > 0 ? Math.Max(0, downloadedBytes - lastBytes) / seconds : 0;
        TimeSpan? eta = speed > 0
            ? TimeSpan.FromSeconds(Math.Min(TimeSpan.MaxValue.TotalSeconds / 2, Math.Max(0, totalBytes - downloadedBytes) / speed)) : null;
        lastBytes = downloadedBytes;
        lastElapsed = elapsed;
        progress?.Report(Format(name, downloadedBytes, totalBytes) + string.Create(CultureInfo.InvariantCulture,
            $" | {speed / 1_000_000:0.00} MB/s | ETA: {(eta is { } remaining ? remaining.ToString(@"hh\:mm\:ss") : "calculating...")}"));
        downloadProgress?.Report(new YtdlpDownloadProgress
        {
            Status = completed ? "finished" : "downloading", DownloadedBytes = downloadedBytes,
            TotalBytes = totalBytes, SpeedBytesPerSecond = speed, Eta = eta, Elapsed = elapsed,
            Percent = totalBytes > 0 ? Math.Clamp(100d * downloadedBytes / totalBytes, 0, 100) : null
        });
    }

    internal static string Format(string name, long downloadedBytes, long totalBytes) =>
        string.Create(CultureInfo.InvariantCulture,
            $"Downloading {name}: {downloadedBytes / 1_000_000d:0.00} / {totalBytes / 1_000_000d:0.00} MB");
}
