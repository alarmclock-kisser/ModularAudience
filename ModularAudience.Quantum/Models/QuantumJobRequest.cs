namespace ModularAudience.Quantum.Models;

/// <param name="CircuitQasm">OpenQASM source. IBM QPUs require backend-specific ISA source.</param>
/// <param name="QubitCount">Number of measured logical qubits, not the backend's physical size.</param>
/// <param name="Shots">Number of measurements.</param>
/// <param name="ClassicalRegister">The single output register to read from Sampler results.</param>
public record QuantumJobRequest(string CircuitQasm, int QubitCount, int Shots, string ClassicalRegister = "c")
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(this.CircuitQasm);
        ArgumentException.ThrowIfNullOrWhiteSpace(this.ClassicalRegister);
        if (this.QubitCount is < 4 or > 16)
            throw new ArgumentOutOfRangeException(nameof(this.QubitCount), "The circuit must measure between 4 and 16 qubits.");
        if (this.Shots is < 1 or > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(this.Shots), "Shots must be between 1 and 1,000,000.");
    }
}
