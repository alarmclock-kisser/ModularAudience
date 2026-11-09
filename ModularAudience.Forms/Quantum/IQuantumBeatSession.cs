using ModularAudience.Quantum.Models;

namespace ModularAudience.Forms.Quantum;

internal sealed record QuantumExecutionResult(QuantumJobResponse Job, string ExecutedQasm, TimeSpan CompilationLatency);

internal interface IQuantumBeatSession : IDisposable
{
    bool IsConnected { get; }
    string ConnectedBackend { get; }
    Task<IReadOnlyList<QuantumBackendInfo>> ConnectAsync(IbmQuantumOptions options, CancellationToken cancellationToken);
    void Disconnect();
    Task<QuantumExecutionResult> ExecuteLocalAsync(QuantumCircuitDefinition circuit, int shots, int? seed, CancellationToken cancellationToken);
    Task<QuantumExecutionResult> ExecuteIbmAsync(QuantumCircuitDefinition circuit, int shots, string backend,
        string pythonExecutable, int optimizationLevel, int seed, string? preparedIsaQasm, CancellationToken cancellationToken);
}
