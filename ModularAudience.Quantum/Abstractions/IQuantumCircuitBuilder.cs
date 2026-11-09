namespace ModularAudience.Quantum.Abstractions;

/// <summary>Immutable fluent builder. Keep the returned builder after each operation.</summary>
public interface IQuantumCircuitBuilder
{
    int QubitCount { get; }
    IQuantumCircuitBuilder WithQubitCount(int qubitCount);
    IQuantumCircuitBuilder WithSuperposition(int qubitIndex);
    IQuantumCircuitBuilder WithEntanglement(int controlQubit, int targetQubit);
    IQuantumCircuitBuilder WithRotation(int qubitIndex, double theta);
    IQuantumCircuitBuilder WithBitFlip(int qubitIndex);
    string Build();
}
