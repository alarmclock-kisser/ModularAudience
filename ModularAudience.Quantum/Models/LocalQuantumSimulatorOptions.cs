namespace ModularAudience.Quantum.Models;

public sealed class LocalQuantumSimulatorOptions
{
    /// <summary>A fixed seed makes repeated executions reproducible. Null uses a fresh random seed.</summary>
    public int? Seed { get; set; }
}
