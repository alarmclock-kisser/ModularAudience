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
}
