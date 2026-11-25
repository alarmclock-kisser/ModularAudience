using System.Text.Json.Serialization;

namespace ModularAudience.Audio.Synthesis.Runtime;

/// <summary>
/// Persisted descriptor for a user-created custom instrument synth.
/// The C# DSP code is stored alongside metadata and compiled on load via Roslyn.
/// </summary>
public sealed class CustomInstrumentDescriptor
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Code { get; set; } = "";
    public int SampleRate { get; set; } = 44100;
    public int Channels { get; set; } = 2;
    public double GainPercent { get; set; } = 75;
    public int MidiNote { get; set; } = 60;
    public double PitchSemitones { get; set; } = 0;
    public double Bpm { get; set; } = 120;
    public double NoteBeats { get; set; } = 1;
    public double DurationMultiplier { get; set; } = 1;

    // Voice/tone controls from InstrumentSynthSettings. Generated DSP code reads these
    // through SynthRenderContext, so the numeric controls of the Sample Synthesizer
    // actually shape the rendered custom instrument.
    public double AttackMs { get; set; } = 5;
    public double DecaySeconds { get; set; } = 2;
    public double SustainPercent { get; set; } = 70;
    public double ReleaseSeconds { get; set; } = 0.5;
    public double Brightness { get; set; } = 60;
    public double Body { get; set; } = 60;
    public double Noise { get; set; } = 10;
    public double Drive { get; set; } = 0;
    public double DetuneCents { get; set; } = 3;
    public double VibratoCents { get; set; } = 10;
    public double VibratoHz { get; set; } = 5;
    public double CutoffHz { get; set; } = 16000;
    public double Resonance { get; set; } = 0.707;
    public double FilterEnvelope { get; set; } = 40;
    public double RoomPercent { get; set; } = 12;
    public double StereoWidth { get; set; } = 30;
    public int Seed { get; set; } = 17;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonConstructor]
    public CustomInstrumentDescriptor() { }

    public CustomInstrumentDescriptor(string name, string code)
    {
        Name = name;
        Code = code;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Creates an independent copy, used for temporary pre-hear renders.</summary>
    public CustomInstrumentDescriptor Clone() => new()
    {
        Id = Id,
        Name = Name,
        Description = Description,
        Code = Code,
        SampleRate = SampleRate,
        Channels = Channels,
        GainPercent = GainPercent,
        MidiNote = MidiNote,
        PitchSemitones = PitchSemitones,
        Bpm = Bpm,
        NoteBeats = NoteBeats,
        DurationMultiplier = DurationMultiplier,
        AttackMs = AttackMs,
        DecaySeconds = DecaySeconds,
        SustainPercent = SustainPercent,
        ReleaseSeconds = ReleaseSeconds,
        Brightness = Brightness,
        Body = Body,
        Noise = Noise,
        Drive = Drive,
        DetuneCents = DetuneCents,
        VibratoCents = VibratoCents,
        VibratoHz = VibratoHz,
        CutoffHz = CutoffHz,
        Resonance = Resonance,
        FilterEnvelope = FilterEnvelope,
        RoomPercent = RoomPercent,
        StereoWidth = StereoWidth,
        Seed = Seed,
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt
    };
}
