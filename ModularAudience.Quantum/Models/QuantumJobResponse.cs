namespace ModularAudience.Quantum.Models;

public record QuantumJobResponse(
    string JobId,
    IReadOnlyDictionary<string, int> Counts,
    TimeSpan ExecutionLatency);
