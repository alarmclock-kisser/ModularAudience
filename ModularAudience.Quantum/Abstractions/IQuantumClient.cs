using ModularAudience.Quantum.Models;

namespace ModularAudience.Quantum.Abstractions;

/// <summary>Executes a circuit without depending on an audio engine or a particular provider.</summary>
public interface IQuantumClient
{
    Task<QuantumJobResponse> ExecuteAsync(QuantumJobRequest request, CancellationToken cancellationToken);
}
