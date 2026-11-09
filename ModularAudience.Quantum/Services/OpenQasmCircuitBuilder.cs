using System.Globalization;
using System.Text;
using ModularAudience.Quantum.Abstractions;
using ModularAudience.Quantum.Models;

namespace ModularAudience.Quantum.Services;

public sealed class OpenQasmCircuitBuilder : IQuantumCircuitBuilder
{
    private readonly string[] gates;
    private readonly OpenQasmVersion version;

    public OpenQasmCircuitBuilder(int qubitCount = 4, OpenQasmVersion version = OpenQasmVersion.Version2)
        : this(qubitCount, version, []) { }

    private OpenQasmCircuitBuilder(int qubitCount, OpenQasmVersion version, string[] gates)
    {
        if (qubitCount is < 4 or > 16)
            throw new ArgumentOutOfRangeException(nameof(qubitCount), "A circuit must contain between 4 and 16 qubits.");
        if (!Enum.IsDefined(version)) throw new ArgumentOutOfRangeException(nameof(version), "Unsupported OpenQASM version.");
        this.QubitCount = qubitCount;
        this.version = version;
        this.gates = gates;
    }

    public int QubitCount { get; }

    /// <summary>Starts a new, empty circuit with the same QASM version.</summary>
    public IQuantumCircuitBuilder WithQubitCount(int qubitCount) => new OpenQasmCircuitBuilder(qubitCount, this.version);

    public IQuantumCircuitBuilder WithSuperposition(int qubitIndex) => this.AddSingleQubitGate("h", qubitIndex);
    public IQuantumCircuitBuilder WithBitFlip(int qubitIndex) => this.AddSingleQubitGate("x", qubitIndex);

    public IQuantumCircuitBuilder WithEntanglement(int controlQubit, int targetQubit)
    {
        this.ValidateQubit(controlQubit);
        this.ValidateQubit(targetQubit);
        if (controlQubit == targetQubit)
            throw new ArgumentException("CNOT control and target must be different qubits.");
        return this.AddGate(FormattableString.Invariant($"cx q[{controlQubit}], q[{targetQubit}];"));
    }

    public IQuantumCircuitBuilder WithRotation(int qubitIndex, double theta)
    {
        this.ValidateQubit(qubitIndex);
        if (!double.IsFinite(theta)) throw new ArgumentOutOfRangeException(nameof(theta), "Rotation angles must be finite radians.");
        return this.AddGate($"ry({theta.ToString("R", CultureInfo.InvariantCulture)}) q[{qubitIndex.ToString(CultureInfo.InvariantCulture)}];");
    }

    public string Build()
    {
        var qasm = new StringBuilder();
        if (this.version == OpenQasmVersion.Version2)
        {
            qasm.Append("OPENQASM 2.0;\ninclude \"qelib1.inc\";\n");
            qasm.Append(CultureInfo.InvariantCulture, $"qreg q[{this.QubitCount}];\ncreg c[{this.QubitCount}];\n");
        }
        else
        {
            qasm.Append("OPENQASM 3.0;\ninclude \"stdgates.inc\";\n");
            qasm.Append(CultureInfo.InvariantCulture, $"qubit[{this.QubitCount}] q;\nbit[{this.QubitCount}] c;\n");
        }
        foreach (string gate in this.gates) qasm.Append(gate).Append('\n');
        qasm.Append(this.version == OpenQasmVersion.Version2 ? "measure q -> c;\n" : "c = measure q;\n");
        return qasm.ToString();
    }

    private IQuantumCircuitBuilder AddSingleQubitGate(string name, int qubitIndex)
    {
        this.ValidateQubit(qubitIndex);
        return this.AddGate(FormattableString.Invariant($"{name} q[{qubitIndex}];"));
    }

    private OpenQasmCircuitBuilder AddGate(string gate) => new(this.QubitCount, this.version, [.. this.gates, gate]);

    private void ValidateQubit(int index)
    {
        if (index < 0 || index >= this.QubitCount)
            throw new ArgumentOutOfRangeException(nameof(index), "The qubit index is outside the circuit register.");
    }
}
