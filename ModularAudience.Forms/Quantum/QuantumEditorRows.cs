using ModularAudience.Quantum.Models;

namespace ModularAudience.Forms.Quantum;

internal sealed class QuantumGateEditorRow
{
    public bool Enabled { get; set; } = true;
    public string Gate { get; set; } = "H";
    public int Qubit { get; set; }
    public int Target { get; set; } = 1;
    public double AngleDegrees { get; set; } = 45;

    internal QuantumGateOperation ToOperation() => new(this.Gate switch
    {
        "H" => QuantumGateKind.Hadamard, "X" => QuantumGateKind.BitFlip, "CX" => QuantumGateKind.ControlledNot,
        "Ry" => QuantumGateKind.RotationY, _ => throw new ArgumentException("Choose H, X, CX or Ry for each gate.")
    }, this.Qubit, this.Target, this.AngleDegrees * Math.PI / 180, this.Enabled);

    internal static QuantumGateEditorRow From(QuantumGateOperation operation) => new()
    {
        Enabled = operation.Enabled, Gate = operation.Kind switch
        {
            QuantumGateKind.Hadamard => "H", QuantumGateKind.BitFlip => "X", QuantumGateKind.ControlledNot => "CX", _ => "Ry"
        }, Qubit = operation.Qubit, Target = operation.Target, AngleDegrees = operation.AngleRadians * 180 / Math.PI
    };
}

internal sealed class QuantumTrackEditorRow
{
    public bool Enabled { get; set; } = true;
    public string Sample { get; set; } = "";
    public int TriggerQubit { get; set; }
    public bool Invert { get; set; }
    public double DensityPercent { get; set; } = 100;
    public int StepOffset { get; set; }
    public double GainPercent { get; set; } = 100;
    public double PitchCents { get; set; }

    internal QuantumTrackMapping ToMapping() => new(this.Sample, this.TriggerQubit, this.Enabled, this.Invert,
        this.DensityPercent / 100, this.StepOffset, this.GainPercent / 100, this.PitchCents);
    internal static QuantumTrackEditorRow From(QuantumTrackMapping mapping) => new()
    {
        Sample = mapping.Name, TriggerQubit = mapping.TriggerQubit, Enabled = mapping.Enabled, Invert = mapping.InvertTrigger,
        DensityPercent = mapping.DensityScale * 100, StepOffset = mapping.StepOffset, GainPercent = mapping.Gain * 100, PitchCents = mapping.PitchOffsetCents
    };
}
