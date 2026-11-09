using ModularAudience.Quantum.Models;

namespace ModularAudience.Quantum.Abstractions;

public interface IQuantumPatternService
{
    Task<QuantumPatternResult> GenerateBreakbeatAsync(int qubits, int shots,
        CancellationToken cancellationToken, int stepCount = 16);
}
