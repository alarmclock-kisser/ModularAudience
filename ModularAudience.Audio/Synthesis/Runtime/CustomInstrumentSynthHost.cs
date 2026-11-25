using ModularAudience.Audio.Synthesis.Runtime;

namespace ModularAudience.Audio.Synthesis.Runtime;

/// <summary>
/// Host for a live custom instrument synth slot. Holds the active synth instance
/// and its descriptor, allowing hot-swap without interrupting the playback graph.
/// </summary>
public sealed class CustomInstrumentSynthHost
{
    private readonly object gate = new();
    private IRuntimeProceduralSynth? activeSynth;

    public CustomInstrumentDescriptor Descriptor { get; }

    public IRuntimeProceduralSynth? ActiveSynth
    {
        get
        {
            lock (gate) { return activeSynth; }
        }
        set
        {
            lock (gate) { activeSynth = value; }
        }
    }

    public double Bpm => Descriptor.Bpm;
    public double NoteBeats => Descriptor.NoteBeats;
    public double GateSeconds() => 60.0 / Math.Max(1.0, Descriptor.Bpm) * Descriptor.NoteBeats;

    public CustomInstrumentSynthHost(CustomInstrumentDescriptor descriptor)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
    }

    public void Swap(IRuntimeProceduralSynth? synth)
    {
        ActiveSynth = synth;
    }
}
