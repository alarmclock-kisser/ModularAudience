namespace ModularAudience.Audio.Synthesis.Runtime;

/// <summary>
/// Context passed to a runtime procedural synth's Render method.
/// Contains all musical and audio parameters needed for rendering.
/// </summary>
public readonly struct SynthRenderContext
{
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
}
