namespace ModularAudience.Quantum.Models;

public sealed record QuantumBackendInfo(string Name, string Status, int Qubits, int QueueLength, bool IsSimulator);
public sealed record QuantumBackendGate(string Name, IReadOnlyList<int[]> Qubits);
public sealed record QuantumBackendConfiguration(string Name, int QubitCount, IReadOnlyList<string> BasisGates,
    IReadOnlyList<int[]> CouplingMap, IReadOnlyList<QuantumBackendGate> Gates, int MaximumShots);
