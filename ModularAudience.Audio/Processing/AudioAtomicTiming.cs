using System.Globalization;

namespace ModularAudience.Audio.Processing;

public static class AudioAtomicTiming
{
    public const string SourceStartFrameMetric = "Decompile.SourceStartFrame";
    public const string SourceFrameCountMetric = "Decompile.SourceFrameCount";

    public static void Set(AudioObj atomic, long sourceStartFrame, long sourceFrameCount)
    {
        ArgumentNullException.ThrowIfNull(atomic);
        if (sourceStartFrame < 0 || sourceFrameCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceStartFrame), "Atomic source positions must be non-negative.");
        }

        atomic.Metrics[SourceStartFrameMetric] = sourceStartFrame;
        atomic.Metrics[SourceFrameCountMetric] = sourceFrameCount;
    }

    public static bool TryGet(AudioObj atomic, out long sourceStartFrame, out long sourceFrameCount)
    {
        ArgumentNullException.ThrowIfNull(atomic);
        bool hasStart = TryReadIntegral(atomic, SourceStartFrameMetric, out sourceStartFrame);
        bool hasCount = TryReadIntegral(atomic, SourceFrameCountMetric, out sourceFrameCount);
        return hasStart && hasCount && sourceStartFrame >= 0 && sourceFrameCount >= 0;
    }

    private static bool TryReadIntegral(AudioObj atomic, string key, out long result)
    {
        if (!atomic.Metrics.TryGetValue(key, out double value)
            || !double.IsFinite(value)
            || value < 0
            || value > long.MaxValue
            || value != Math.Truncate(value))
        {
            result = 0;
            return false;
        }

        result = Convert.ToInt64(value, CultureInfo.InvariantCulture);
        return true;
    }
}
