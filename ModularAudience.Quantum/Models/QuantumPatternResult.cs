namespace ModularAudience.Quantum.Models;

public record QuantumPatternResult(
    string CircuitQasm,
    IReadOnlyList<DrumSliceTrigger> Slices,
    IReadOnlyDictionary<string, int> RawCounts,
    TimeSpan ExecutionLatency);
