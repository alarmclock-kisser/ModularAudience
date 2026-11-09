using ModularAudience.Quantum.Abstractions;
using ModularAudience.Quantum.Models;

namespace ModularAudience.Quantum.Services;

public sealed class QuantumPatternService(IQuantumClient client, IQuantumCircuitBuilder circuitBuilder,
    IQuantumPatternMapper mapper) : IQuantumPatternService
{
    public async Task<QuantumPatternResult> GenerateBreakbeatAsync(int qubits, int shots,
        CancellationToken cancellationToken, int stepCount = 16)
    {
        cancellationToken.ThrowIfCancellationRequested();
        QuantumCounts.ValidateStepCount(stepCount);
        IQuantumCircuitBuilder circuit = circuitBuilder.WithQubitCount(qubits)
            .WithSuperposition(0).WithEntanglement(0, 1).WithBitFlip(1)
            .WithRotation(2, Math.PI / 3).WithRotation(3, Math.PI / 4).WithEntanglement(2, 3);
        for (int qubit = 4; qubit < qubits; qubit++)
            circuit = circuit.WithRotation(qubit, Math.PI / (qubit + 1)).WithEntanglement(qubit - 1, qubit);

        string qasm = circuit.Build();
        var request = new QuantumJobRequest(qasm, qubits, shots);
        request.Validate();
        var response = await client.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        QuantumCounts.Snapshot(response.Counts, qubits);
        return mapper.Map(qasm, response.Counts, response.ExecutionLatency, stepCount);
    }
}
