namespace ModularAudience.Audio.Synthesis.Runtime;

/// <summary>
/// Context passed to a runtime procedural synth's Render method.
/// Contains all musical and audio parameters needed for rendering.
/// </summary>
public readonly struct SynthRenderContext
{
    // Required by C# when a struct has property initializers.
    public SynthRenderContext() { }

    public int SampleRate { get; init; }
    public int Channels { get; init; }
    public int FrameCount { get; init; }
    public double NoteFrequencyHz { get; init; }
    public double Bpm { get; init; }
    public double NoteBeats { get; init; }
    public double GateSeconds { get; init; }
    public int MidiNote { get; init; }
    public double GainPercent { get; init; }
    public double PitchSemitones { get; init; }
    public double DurationMultiplier { get; init; } = 1;

    // Controls from the Sample Synthesizer UI. They are supplied to generated DSP code
    // so the numeric knobs (note, pitch, envelope, filter, ...) audibly affect the voice.
    public double AttackMs { get; init; } = 5;
    public double DecaySeconds { get; init; } = 2;
    public double SustainPercent { get; init; } = 70;
    public double ReleaseSeconds { get; init; } = 0.5;
    public double Brightness { get; init; } = 60;
    public double Body { get; init; } = 60;
    public double Noise { get; init; } = 10;
    public double Drive { get; init; }
    public double DetuneCents { get; init; } = 3;
    public double VibratoCents { get; init; } = 10;
    public double VibratoHz { get; init; } = 5;
    public double CutoffHz { get; init; } = 16000;
    public double Resonance { get; init; } = 0.707;
    public double FilterEnvelope { get; init; } = 40;
    public double RoomPercent { get; init; } = 12;
    public double StereoWidth { get; init; } = 30;
    public int Seed { get; init; } = 17;
}
