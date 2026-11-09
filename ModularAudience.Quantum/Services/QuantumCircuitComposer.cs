using ModularAudience.Quantum.Abstractions;
using ModularAudience.Quantum.Models;

namespace ModularAudience.Quantum.Services;

public static class QuantumCircuitComposer
{
    public static string Build(QuantumCircuitDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(definition.Gates);
        if (definition.Gates.Count > 512) throw new ArgumentException("A visual circuit supports at most 512 gates.");
        IQuantumCircuitBuilder builder = new OpenQasmCircuitBuilder(definition.QubitCount, definition.Version);
        foreach (var gate in definition.Gates)
        {
            if (gate is null || !Enum.IsDefined(gate.Kind) || gate.Qubit < 0 || gate.Qubit >= definition.QubitCount ||
                !double.IsFinite(gate.AngleRadians) || (gate.Kind == QuantumGateKind.ControlledNot &&
                    (gate.Target < 0 || gate.Target >= definition.QubitCount || gate.Qubit == gate.Target)))
                throw new ArgumentException("A gate contains an invalid operation, qubit index, control/target pair or rotation angle.");
            if (!gate.Enabled) continue;
            builder = gate.Kind switch
            {
                QuantumGateKind.Hadamard => builder.WithSuperposition(gate.Qubit),
                QuantumGateKind.BitFlip => builder.WithBitFlip(gate.Qubit),
                QuantumGateKind.ControlledNot => builder.WithEntanglement(gate.Qubit, gate.Target),
                _ => builder.WithRotation(gate.Qubit, gate.AngleRadians)
            };
        }
        return builder.Build();
    }

    public static QuantumCircuitDefinition CreatePreset(string name, int qubits, double density = 0.65,
        OpenQasmVersion version = OpenQasmVersion.Version2)
    {
        _ = new OpenQasmCircuitBuilder(qubits, version);
        if (!double.IsFinite(density) || density is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(density));
        var gates = new List<QuantumGateOperation>();
        switch (name)
        {
            case "Empty": break;
            case "Bell break":
                gates.AddRange([new(QuantumGateKind.Hadamard, 0), new(QuantumGateKind.ControlledNot, 0, 1),
                    new(QuantumGateKind.BitFlip, 1), new(QuantumGateKind.RotationY, 2, AngleRadians: Math.PI / 3),
                    new(QuantumGateKind.RotationY, 3, AngleRadians: Math.PI / 4), new(QuantumGateKind.ControlledNot, 2, 3)]);
                for (int q = 4; q < qubits; q++) gates.Add(new(QuantumGateKind.RotationY, q, AngleRadians: Math.PI / 2));
                break;
            case "GHZ / correlated":
                gates.Add(new(QuantumGateKind.Hadamard, 0));
                for (int q = 1; q < qubits; q++) gates.Add(new(QuantumGateKind.ControlledNot, q - 1, q));
                break;
            case "Independent superposition":
                for (int q = 0; q < qubits; q++) gates.Add(new(QuantumGateKind.Hadamard, q));
                break;
            case "Weighted groove":
                for (int q = 0; q < qubits; q++)
                    gates.Add(new(QuantumGateKind.RotationY, q, AngleRadians: 2 * Math.Asin(Math.Sqrt(density))));
                for (int q = 1; q < qubits; q += 2) gates.Add(new(QuantumGateKind.ControlledNot, q - 1, q));
                break;
            default: throw new ArgumentException("Unknown circuit preset.", nameof(name));
        }
        return new(qubits, gates.AsReadOnly(), version);
    }

    public static QuantumCircuitDefinition Randomize(int qubits, QuantumCircuitRandomization settings,
        OpenQasmVersion version = OpenQasmVersion.Version2)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _ = new OpenQasmCircuitBuilder(qubits, version);
        if (settings.Layers is < 1 or > 16 || !double.IsFinite(settings.HadamardChance) || settings.HadamardChance is < 0 or > 1 ||
            !double.IsFinite(settings.EntanglementChance) || settings.EntanglementChance is < 0 or > 1 ||
            !double.IsFinite(settings.MinimumAngleRadians) || !double.IsFinite(settings.MaximumAngleRadians) ||
            settings.MinimumAngleRadians < -2 * Math.PI || settings.MaximumAngleRadians > 2 * Math.PI ||
            settings.MinimumAngleRadians > settings.MaximumAngleRadians)
            throw new ArgumentException("Use 1 to 16 circuit layers, probabilities from 0 to 1 and finite angles from -2π to +2π.");
        var random = new Random(settings.Seed);
        var gates = new List<QuantumGateOperation>();
        for (int layer = 0; layer < settings.Layers; layer++)
            for (int q = 0; q < qubits; q++)
            {
                gates.Add(random.NextDouble() < settings.HadamardChance
                    ? new(QuantumGateKind.Hadamard, q)
                    : new(QuantumGateKind.RotationY, q, AngleRadians: settings.MinimumAngleRadians +
                        random.NextDouble() * (settings.MaximumAngleRadians - settings.MinimumAngleRadians)));
                if (random.NextDouble() < settings.EntanglementChance)
                    gates.Add(new(QuantumGateKind.ControlledNot, q, (q + 1) % qubits));
            }
        return new(qubits, gates.AsReadOnly(), version);
    }
}
