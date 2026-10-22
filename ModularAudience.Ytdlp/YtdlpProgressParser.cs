using System.Globalization;
using System.Text.Json;

namespace ModularAudience.Ytdlp;

public static class YtdlpProgressParser
{
    public const string Prefix = "__MA_PROGRESS__:";
    private const int MaximumLineLength = 1024;

    public static bool TryParse(string line, out YtdlpDownloadProgress? progress)
    {
        progress = null;
        if (line is null || line.Length > MaximumLineLength || !line.StartsWith(Prefix, StringComparison.Ordinal))
            return false;
        try
        {
            using var document = JsonDocument.Parse(line[Prefix.Length..], new JsonDocumentOptions { MaxDepth = 4 });
            return TryReadProgress(document.RootElement, out progress);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryReadProgress(JsonElement root, out YtdlpDownloadProgress? progress)
    {
        progress = null;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("status", out var status) ||
            status.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(status.GetString()) ||
            !TryReadBytes(root, "downloaded_bytes", out var downloaded) ||
            !TryReadBytes(root, "total_bytes", out var actual) ||
            !TryReadBytes(root, "total_bytes_estimate", out var estimate) ||
            !TryReadNumber(root, "speed", out var speed) ||
            !TryReadNumber(root, "eta", out var eta) || !TryReadNumber(root, "elapsed", out var elapsed) ||
            !TryGetDuration(eta, out var etaTime) || !TryGetDuration(elapsed, out var elapsedTime))
            return false;
        var total = actual is > 0 ? actual : estimate is > 0 ? estimate : null;
        var bytes = downloaded ?? 0;
        if (eta is null && speed is > 0 && total is { } knownTotal)
            TryGetDuration(Math.Max(0, knownTotal - bytes) / speed.Value, out etaTime);
        progress = new YtdlpDownloadProgress
        {
            Status = status.GetString()!, DownloadedBytes = bytes, TotalBytes = total,
            IsTotalEstimated = actual is not > 0 && total is not null,
            Percent = status.GetString() == "finished" ? 100 :
                total is > 0 ? Math.Clamp(100d * bytes / total.Value, 0, 100) : null,
            SpeedBytesPerSecond = speed, Eta = etaTime, Elapsed = elapsedTime
        };
        return true;
    }

    private static bool TryReadBytes(JsonElement root, string name, out long? value)
    {
        value = null;
        if (!TryReadNumericText(root, name, out var text))
            return false;
        if (text is null)
            return true;
        if (!decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ||
            number < 0 || number > long.MaxValue || decimal.Truncate(number) != number)
            return false;
        value = (long)number;
        return true;
    }

    private static bool TryReadNumber(JsonElement root, string name, out double? value)
    {
        value = null;
        if (!TryReadNumericText(root, name, out var text))
            return false;
        if (text is null)
            return true;
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ||
            !double.IsFinite(number) || number < 0)
            return false;
        value = number;
        return true;
    }

    private static bool TryReadNumericText(JsonElement root, string name, out string? text)
    {
        text = null;
        if (!root.TryGetProperty(name, out var field) || field.ValueKind == JsonValueKind.Null)
            return true;
        if (field.ValueKind == JsonValueKind.Number)
            text = field.GetRawText();
        else if (field.ValueKind == JsonValueKind.String)
        {
            text = field.GetString()?.Trim();
            if (string.Equals(text, "NA", StringComparison.OrdinalIgnoreCase))
                text = null;
        }
        else
            return false;
        return true;
    }

    private static bool TryGetDuration(double? seconds, out TimeSpan? duration)
    {
        duration = null;
        if (seconds is null)
            return true;
        if (!double.IsFinite(seconds.Value) || seconds < 0 ||
            seconds > long.MaxValue / (double)TimeSpan.TicksPerSecond)
            return false;
        var ticks = (decimal)seconds.Value * TimeSpan.TicksPerSecond;
        if (ticks > long.MaxValue)
            return false;
        duration = TimeSpan.FromTicks((long)ticks);
        return true;
    }
}
