namespace ModularAudience.Quantum.Models;

public enum QuantumBarArrangement { Independent, RepeatFirstBar, AlternateTwoBars }

public sealed record QuantumTrackMapping(string Name, int TriggerQubit, bool Enabled = true,
    bool InvertTrigger = false, double DensityScale = 1, int StepOffset = 0, double Gain = 1, double PitchOffsetCents = 0);

/// <summary>Classical interpretation of measured bits, not physical DSP gates on a QPU.</summary>
public sealed record QuantumBeatOptions
{
    public int Bars { get; init; } = 2;
    public int StepsPerBar { get; init; } = 16;
    public double Bpm { get; init; } = 120;
    public int Seed { get; init; } = 42;
    public double Density { get; init; } = 0.8;
    public double Swing { get; init; }
    public QuantumBarArrangement Arrangement { get; init; }
    public bool Interleaved { get; init; }
    public int ModifierQubit { get; init; } = 2;
    public bool QuantumVelocity { get; init; } = true;
    public double MinimumVelocity { get; init; } = 0.35;
    public double MaximumVelocity { get; init; } = 1;
    public bool GhostNotes { get; init; }
    public double GhostChance { get; init; } = 0.08;
    public double GhostVelocityScale { get; init; } = 0.3;
    public bool VaryDuration { get; init; }
    public double MinimumDurationSteps { get; init; } = 0.5;
    public double MaximumDurationSteps { get; init; } = 2;
    public bool PitchEnabled { get; init; }
    public double MinimumPitchCents { get; init; } = -500;
    public double MaximumPitchCents { get; init; } = 300;
    public double PitchStepCents { get; init; } = 100;
    public bool TimeStretchEnabled { get; init; }
    public double MinimumStretch { get; init; } = 0.75;
    public double MaximumStretch { get; init; } = 1.5;
    public bool VarispeedEnabled { get; init; }
    public double MinimumSpeed { get; init; } = 0.75;
    public double MaximumSpeed { get; init; } = 1.25;
    public bool CutEnabled { get; init; }
    public double MaximumCutOffsetMs { get; init; } = 80;
    public bool ReverseEnabled { get; init; }
    public bool FitToNoteDuration { get; init; }
    public bool TruncateNotes { get; init; } = true;
    public bool KeepEndTails { get; init; }
    public bool NormalizeOutput { get; init; } = true;
    public double OutputGainDb { get; init; }

    public void Validate(int qubits, IReadOnlyList<QuantumTrackMapping> tracks)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        if (qubits is < 4 or > 16 || tracks.Count is < 1 or > 32 ||
            this.Bars is < 1 or > 16 || this.StepsPerBar is < 4 or > 64 || this.Bars * this.StepsPerBar > 1024 ||
            !Enum.IsDefined(this.Arrangement) || this.ModifierQubit < 0 || this.ModifierQubit >= qubits)
            throw new ArgumentException("Use 4 to 16 qubits, 1 to 32 sample tracks and at most 1,024 arrangement steps.");
        CheckRange(this.Bpm, 20, 400, "Tempo");
        CheckRange(this.Density, 0, 1, "Density");
        CheckRange(this.Swing, 0, 0.49, "Swing");
        CheckRange(this.MinimumVelocity, 0, 1, "Minimum velocity");
        CheckRange(this.MaximumVelocity, this.MinimumVelocity, 1, "Maximum velocity");
        CheckRange(this.GhostChance, 0, 1, "Ghost-note probability");
        CheckRange(this.GhostVelocityScale, 0, 1, "Ghost-note velocity");
        CheckRange(this.MinimumDurationSteps, 0.125, 16, "Minimum note duration");
        CheckRange(this.MaximumDurationSteps, this.MinimumDurationSteps, 16, "Maximum note duration");
        CheckRange(this.MinimumPitchCents, -1200, 1200, "Minimum pitch");
        CheckRange(this.MaximumPitchCents, this.MinimumPitchCents, 1200, "Maximum pitch");
        CheckRange(this.PitchStepCents, 1, 1200, "Pitch quantization");
        CheckRange(this.MinimumStretch, 0.5, 2, "Minimum stretch");
        CheckRange(this.MaximumStretch, this.MinimumStretch, 2, "Maximum stretch");
        CheckRange(this.MinimumSpeed, 0.5, 2, "Minimum varispeed");
        CheckRange(this.MaximumSpeed, this.MinimumSpeed, 2, "Maximum varispeed");
        CheckRange(this.MaximumCutOffsetMs, 0, 10_000, "Cut offset");
        CheckRange(this.OutputGainDb, -48, 12, "Output gain");
        foreach (var track in tracks)
        {
            if (track is null || string.IsNullOrWhiteSpace(track.Name) || track.Name.Length > 256 ||
                track.TriggerQubit < 0 || track.TriggerQubit >= qubits || Math.Abs((long)track.StepOffset) > 1024)
                throw new ArgumentException("Every sample track needs a name, a valid trigger qubit and a step offset within the arrangement limit.");
            CheckRange(track.DensityScale, 0, 2, "Track density");
            CheckRange(track.Gain, 0, 1, "Track gain");
            CheckRange(track.PitchOffsetCents, -1200, 1200, "Track pitch");
        }
    }

    private static void CheckRange(double value, double minimum, double maximum, string name)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
            throw new ArgumentException(name + " is outside its supported range.");
    }
}
