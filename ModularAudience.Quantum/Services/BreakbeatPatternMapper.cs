using System.Numerics;
using ModularAudience.Quantum.Abstractions;
using ModularAudience.Quantum.Models;

namespace ModularAudience.Quantum.Services;

/// <summary>Deterministic, frequency-weighted mapping. IBM bitstrings are little-endian: q0 is the rightmost bit.</summary>
public sealed class BreakbeatPatternMapper : IQuantumPatternMapper
{
    private static readonly double[] PitchSteps = [-500, -200, 0, 300];

    public QuantumPatternResult Map(string circuitQasm, IReadOnlyDictionary<string, int> rawCounts,
        TimeSpan executionLatency, int stepCount = 16)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(circuitQasm);
        QuantumCounts.ValidateStepCount(stepCount);
        if (executionLatency < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(executionLatency), "Execution latency cannot be negative.");
        var counts = QuantumCounts.Snapshot(rawCounts);
        var slices = new DrumSliceTrigger[stepCount];
        if (counts.Count == 0)
        {
            for (int step = 0; step < slices.Length; step++) slices[step] = new(step, false, 0, 0, 1, 0);
            return new(circuitQasm, Array.AsReadOnly(slices), counts, executionLatency);
        }

        var states = counts.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => (Bits: Convert.ToInt32(pair.Key, 2), Count: pair.Value)).ToArray();
        long total = states.Sum(state => (long)state.Count);
        int width = counts.First().Key.Length;
        double density = CorrelationDensity(states, total, width);
        int stride = 5;
        while (BigInteger.GreatestCommonDivisor(stride, stepCount) != 1) stride += 2;

        for (int step = 0; step < slices.Length; step++)
        {
            // Stratified sampling of the weighted CDF, spread across the grid rather than grouped into blocks.
            double position = (((long)step * stride % stepCount) + 0.5) * total / stepCount;
            long cumulative = 0;
            var selected = states[^1];
            foreach (var state in states)
            {
                cumulative += state.Count;
                if (position < cumulative) { selected = state; break; }
            }
            bool triggered = (selected.Bits & (step % 2 == 0 ? 1 : 2)) != 0;
            int pitchIndex = (selected.Bits >> 2) & 3;
            double offset = BitOperations.PopCount((uint)selected.Bits) / (double)width * 100 * density;
            slices[step] = new(step, triggered, triggered ? selected.Count / (double)total : 0,
                PitchSteps[pitchIndex], 1 + 0.5 * density, offset);
        }
        return new(circuitQasm, Array.AsReadOnly(slices), counts, executionLatency);
    }

    private static double CorrelationDensity((int Bits, int Count)[] states, long total, int width)
    {
        // Measurements alone cannot prove entanglement. Use absolute classical phi correlations as a musical proxy.
        var probability = new double[width];
        foreach (var state in states)
            for (int bit = 0; bit < width; bit++)
                if ((state.Bits & (1 << bit)) != 0) probability[bit] += state.Count / (double)total;

        double sum = 0;
        for (int a = 0; a < width; a++)
            for (int b = a + 1; b < width; b++)
            {
                double variance = probability[a] * (1 - probability[a]) * probability[b] * (1 - probability[b]);
                if (variance <= 0) continue;
                double joint = 0;
                int mask = (1 << a) | (1 << b);
                foreach (var state in states)
                    if ((state.Bits & mask) == mask) joint += state.Count / (double)total;
                sum += Math.Clamp(Math.Abs((joint - probability[a] * probability[b]) / Math.Sqrt(variance)), 0, 1);
            }
        return sum / (width * (width - 1) / 2);
    }
}
