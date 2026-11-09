using ModularAudience.Quantum.Models;

namespace ModularAudience.Quantum.Abstractions;

/// <summary>Read-only discovery. These operations never submit a quantum job.</summary>
public interface IQuantumBackendClient
{
    Task<IReadOnlyList<QuantumBackendInfo>> GetBackendsAsync(CancellationToken cancellationToken);
    Task<QuantumBackendConfiguration> GetBackendConfigurationAsync(string backend, CancellationToken cancellationToken);
}
