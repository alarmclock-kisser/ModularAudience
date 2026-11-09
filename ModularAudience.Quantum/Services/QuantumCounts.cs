using System.Collections.ObjectModel;

namespace ModularAudience.Quantum.Services;

internal static class QuantumCounts
{
    internal static IReadOnlyDictionary<string, int> Snapshot(IReadOnlyDictionary<string, int> counts, int? expectedWidth = null)
    {
        ArgumentNullException.ThrowIfNull(counts);
        var copy = new Dictionary<string, int>(counts.Count, StringComparer.Ordinal);
        int? width = expectedWidth;
        foreach (var (bits, count) in counts)
        {
            if (bits is null || bits.Length is < 4 or > 16 || bits.Any(bit => bit is not ('0' or '1')) ||
                (width.HasValue && bits.Length != width.Value) || count < 0)
                throw new ArgumentException("Counts must contain equally sized, 4-to-16-bit binary strings and non-negative frequencies.", nameof(counts));
            width ??= bits.Length;
            if (count > 0) copy.Add(bits, count);
        }
        return new ReadOnlyDictionary<string, int>(copy);
    }

    internal static void ValidateStepCount(int stepCount)
    {
        if (stepCount is < 1 or > 1024)
            throw new ArgumentOutOfRangeException(nameof(stepCount), "A pattern must contain between 1 and 1,024 steps.");
    }
}
