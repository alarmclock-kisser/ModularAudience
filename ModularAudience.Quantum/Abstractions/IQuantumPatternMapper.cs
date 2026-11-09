using ModularAudience.Quantum.Models;

namespace ModularAudience.Quantum.Abstractions;

public interface IQuantumPatternMapper
{
    QuantumPatternResult Map(string circuitQasm, IReadOnlyDictionary<string, int> rawCounts,
        TimeSpan executionLatency, int stepCount = 16);
}
