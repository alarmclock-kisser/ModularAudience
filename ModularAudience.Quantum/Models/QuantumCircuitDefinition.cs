namespace ModularAudience.Quantum.Models;

public enum QuantumGateKind { Hadamard, BitFlip, ControlledNot, RotationY }

public sealed record QuantumGateOperation(QuantumGateKind Kind, int Qubit, int Target = 0, double AngleRadians = 0, bool Enabled = true);

public sealed record QuantumCircuitDefinition(int QubitCount, IReadOnlyList<QuantumGateOperation> Gates,
    OpenQasmVersion Version = OpenQasmVersion.Version2);

public sealed record QuantumCircuitRandomization(int Layers = 2, double HadamardChance = 0.4,
    double EntanglementChance = 0.45, double MinimumAngleRadians = -Math.PI, double MaximumAngleRadians = Math.PI,
    int Seed = 42);
