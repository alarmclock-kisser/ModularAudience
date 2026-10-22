using System.Globalization;

namespace ModularAudience.Ytdlp;

internal sealed class AssetTransferProgress(string name, long totalBytes, IProgress<string>? progress)
{
    private long lastTick = long.MinValue;

    internal void Report(long downloadedBytes, bool completed = false)
    {
        long now = Environment.TickCount64;
        if (!completed && lastTick != long.MinValue && now - lastTick < 250) return;
        lastTick = now;
        progress?.Report(Format(name, downloadedBytes, totalBytes));
    }

    internal static string Format(string name, long downloadedBytes, long totalBytes) =>
        string.Create(CultureInfo.InvariantCulture,
            $"Downloading {name}: {downloadedBytes / 1_000_000d:0.00} / {totalBytes / 1_000_000d:0.00} MB");
}
