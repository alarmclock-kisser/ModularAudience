namespace ModularAudience.Audio.Synthesis;

public enum SynthFamily { Keyboard, DrumSet, Woodwinds, Brass, PluckedStrings, BowedStrings, MalletsAndBells, Organ, SynthBassAndLead }
public enum SynthDrum { Kick, BassDrum, Snare, Rimshot, Clap, ClosedHiHat, OpenHiHat, LowTom, MidTom, HighTom, Ride, Crash, Cowbell, Shaker }

public sealed record SynthVoice(string Name, double Brightness, double Decay, double Release, double Body, double Noise, double Drive, double Detune, double Vibrato)
{
    public override string ToString() => Name;
}
public sealed record SynthDrumKit(string Name, double Tone, double Decay, double Sweep, double Noise, double Metal, double Drive, double Click)
{
    public override string ToString() => Name;
}

public static class InstrumentSynthCatalog
{
    public static string FamilyName(SynthFamily family) => family switch
    {
        SynthFamily.DrumSet => "Drum Set", SynthFamily.PluckedStrings => "Plucked Strings",
        SynthFamily.BowedStrings => "Bowed Strings", SynthFamily.MalletsAndBells => "Mallets & Bells",
        SynthFamily.SynthBassAndLead => "Synth Bass & Lead", _ => family.ToString()
    };
    public static IReadOnlyList<SynthDrumKit> DrumKits { get; } =
    [
        new("808 Deep", 0.78, 1.65, 1.1, 0.65, 0.8, 0.05, 0.25),
        new("909 Punch", 1.05, 0.8, 1.8, 1.0, 1.2, 0.15, 0.9),
        new("Beach Wood", 0.95, 0.6, 0.4, 0.6, 0.35, 0.02, 0.45),
        new("Industrial Forge", 0.85, 1.1, 2.2, 1.4, 2.1, 0.85, 0.8),
        new("Acoustic Studio", 1.0, 1.0, 0.65, 1.05, 1.0, 0.03, 0.6),
        new("Lo-Fi Dust", 0.9, 0.75, 0.9, 0.9, 0.55, 0.45, 0.3),
        new("Electro Glass", 1.3, 0.65, 2.6, 0.45, 2.5, 0.12, 0.7),
        new("Tribal Earth", 0.7, 1.3, 0.35, 0.75, 0.45, 0.2, 0.5)
    ];
    public static IReadOnlyList<SynthVoice> Voices(SynthFamily family) => family switch
    {
        SynthFamily.Keyboard => Keyboards,
        SynthFamily.Woodwinds => Winds,
        SynthFamily.Brass => Brasses,
        SynthFamily.PluckedStrings => Plucks,
        SynthFamily.BowedStrings => Strings,
        SynthFamily.MalletsAndBells => Mallets,
        SynthFamily.Organ => Organs,
        SynthFamily.SynthBassAndLead => Synths,
        _ => Keyboards
    };
    private static readonly SynthVoice[] Keyboards =
    [
        new("Concert Grand", 58, 2.8, 0.8, 65, 8, 0, 3, 0),
        new("Felt Upright", 22, 1.5, 0.5, 82, 16, 0, 5, 0),
        new("Bright Honky-Tonk", 85, 2.2, 0.55, 45, 12, 8, 18, 0),
        new("Tine Electric", 50, 3.2, 1.0, 42, 0, 8, 4, 8),
        new("Reed Electric", 65, 1.6, 0.5, 58, 4, 28, 2, 5),
        new("Glass FM Keys", 90, 4.0, 1.5, 22, 0, 0, 2, 12),
        new("Toy Piano", 80, 1.1, 0.4, 18, 12, 0, 9, 0),
        new("Dark Velvet", 12, 4.5, 1.8, 90, 2, 3, 6, 5)
    ];
    private static readonly SynthVoice[] Winds =
    [
        new("Concert Flute", 30, 2, 0.22, 45, 30, 0, 2, 22),
        new("Breathy Alto Flute", 15, 2, 0.35, 65, 65, 0, 3, 28),
        new("Clarinet", 55, 2, 0.2, 62, 16, 0, 2, 10),
        new("Oboe", 85, 2, 0.18, 35, 22, 5, 1, 24),
        new("Bassoon", 42, 2, 0.3, 85, 25, 8, 2, 15),
        new("Pan Pipes", 38, 0.9, 0.3, 40, 50, 0, 4, 12),
        new("Tenor Sax", 70, 2, 0.3, 70, 35, 22, 3, 30)
    ];
    private static readonly SynthVoice[] Brasses =
    [
        new("Trumpet", 85, 2, 0.18, 35, 8, 12, 2, 18),
        new("Muted Trumpet", 60, 2, 0.12, 15, 10, 25, 1, 10),
        new("French Horn", 30, 2, 0.45, 85, 5, 3, 4, 10),
        new("Trombone", 60, 2, 0.3, 72, 12, 16, 2, 14),
        new("Tuba", 18, 2, 0.38, 98, 9, 8, 2, 6),
        new("Brass Section", 72, 2, 0.45, 62, 14, 14, 16, 18)
    ];
    private static readonly SynthVoice[] Plucks =
    [
        new("Nylon Guitar", 32, 2.3, 0.5, 70, 20, 0, 1, 0),
        new("Steel Guitar", 80, 3.2, 0.7, 48, 35, 2, 3, 0),
        new("Harp", 55, 4.5, 1.6, 60, 8, 0, 2, 0),
        new("Koto", 85, 1.6, 0.6, 25, 45, 4, 4, 8),
        new("Muted Bass", 16, 0.45, 0.12, 90, 28, 12, 0, 0),
        new("Banjo", 95, 0.8, 0.2, 15, 55, 8, 2, 0)
    ];
    private static readonly SynthVoice[] Strings =
    [
        new("Violin", 80, 2, 0.45, 35, 28, 2, 2, 32),
        new("Viola", 58, 2, 0.5, 58, 30, 3, 3, 28),
        new("Cello", 38, 2, 0.65, 85, 24, 5, 3, 25),
        new("Contrabass", 20, 2, 0.7, 98, 35, 8, 2, 18),
        new("Chamber Ensemble", 68, 2, 0.9, 65, 20, 0, 18, 28),
        new("Glass Strings", 92, 2, 1.2, 20, 8, 2, 12, 15)
    ];
    private static readonly SynthVoice[] Mallets =
    [
        new("Marimba", 25, 1.6, 0.7, 80, 12, 0, 0, 0),
        new("Vibraphone", 45, 4.5, 2.0, 55, 5, 0, 2, 20),
        new("Xylophone", 80, 0.7, 0.2, 28, 30, 0, 0, 0),
        new("Glockenspiel", 95, 3.8, 2.0, 12, 5, 0, 0, 0),
        new("Tubular Bells", 85, 5.0, 3.0, 50, 12, 3, 2, 0),
        new("Steel Pan", 75, 2.0, 0.7, 45, 15, 8, 3, 5)
    ];
    private static readonly SynthVoice[] Organs =
    [
        new("Jazz Drawbar", 60, 2, 0.08, 65, 4, 12, 3, 10),
        new("Rock Tonewheel", 85, 2, 0.06, 65, 10, 45, 6, 22),
        new("Church Principal", 58, 2, 1.8, 85, 8, 0, 8, 0),
        new("Reed Organ", 78, 2, 0.3, 42, 12, 5, 4, 8),
        new("Flute Stops", 16, 2, 1.0, 70, 2, 0, 3, 5),
        new("Cathedral Mixture", 95, 2, 2.5, 75, 8, 3, 12, 12)
    ];
    private static readonly SynthVoice[] Synths =
    [
        new("Sub Anchor", 8, 1.5, 0.15, 100, 0, 5, 0, 0),
        new("Acid Resonator", 72, 0.7, 0.2, 45, 0, 35, 2, 0),
        new("Reese Bass", 65, 2, 0.4, 85, 4, 30, 24, 2),
        new("Rubber Pluck", 65, 0.5, 0.16, 65, 3, 12, 3, 0),
        new("Silk Lead", 55, 2, 0.5, 40, 0, 8, 10, 24),
        new("Hard Sync Lead", 95, 1.1, 0.25, 25, 3, 40, 6, 15),
        new("FM Growl", 82, 1.0, 0.4, 65, 5, 25, 8, 8),
        new("Dream Unison", 70, 2, 1.5, 55, 0, 3, 20, 18)
    ];
}

public sealed class InstrumentSynthSettings
{
    public SynthFamily Family { get; set; }
    public int VoiceIndex { get; set; }
    public int KitIndex { get; set; }
    public SynthDrum Drum { get; set; }
    public int MidiNote { get; set; } = 60;
    public double PitchSemitones { get; set; }
    public double Bpm { get; set; } = 120;
    public double NoteBeats { get; set; } = 1;
    public double DurationMultiplier { get; set; } = 1;
    public double AttackMs { get; set; } = 5;
    public double DecaySeconds { get; set; } = 2;
    public double SustainPercent { get; set; } = 70;
    public double ReleaseSeconds { get; set; } = 0.5;
    public double Brightness { get; set; } = 60;
    public double Body { get; set; } = 60;
    public double Noise { get; set; } = 10;
    public double Drive { get; set; }
    public double DetuneCents { get; set; } = 3;
    public double VibratoCents { get; set; } = 10;
    public double VibratoHz { get; set; } = 5;
    public double CutoffHz { get; set; } = 16000;
    public double Resonance { get; set; } = 0.707;
    public double FilterEnvelope { get; set; } = 40;
    public double StrikePosition { get; set; } = 25;
    public double DrumSweep { get; set; } = 65;
    public double DrumSnap { get; set; } = 60;
    public double RoomPercent { get; set; } = 12;
    public double StereoWidth { get; set; } = 30;
    public double GainPercent { get; set; } = 75;
    public int Seed { get; set; } = 17;
    public int SampleRate { get; set; } = 44100;
    public int Channels { get; set; } = 2;
    public double GateSeconds => 60 / Bpm * NoteBeats * DurationMultiplier;
    public string VoiceName => Family == SynthFamily.DrumSet ? $"{InstrumentSynthCatalog.DrumKits[KitIndex].Name} {Drum}" : InstrumentSynthCatalog.Voices(Family)[VoiceIndex].Name;
    public InstrumentSynthSettings Clone() => (InstrumentSynthSettings)MemberwiseClone();
    public void ApplyVoiceDefaults()
    {
        var voice = InstrumentSynthCatalog.Voices(Family)[VoiceIndex];
        Brightness = voice.Brightness; DecaySeconds = voice.Decay; ReleaseSeconds = voice.Release;
        Body = voice.Body; Noise = voice.Noise; Drive = voice.Drive; DetuneCents = voice.Detune; VibratoCents = voice.Vibrato;
        AttackMs = Family switch { SynthFamily.BowedStrings => 100, SynthFamily.Woodwinds => 40, SynthFamily.Brass => 30, SynthFamily.Organ => 8, _ => 3 };
        SustainPercent = Family is SynthFamily.Keyboard or SynthFamily.PluckedStrings or SynthFamily.MalletsAndBells or SynthFamily.DrumSet ? 0 : 75;
        if (Family == SynthFamily.DrumSet) { DecaySeconds = 0.5; ReleaseSeconds = 0.25; AttackMs = 1; Noise = 50; }
    }
    public void Validate()
    {
        if (!Enum.IsDefined(Family) || !Enum.IsDefined(Drum) || VoiceIndex < 0 || VoiceIndex >= InstrumentSynthCatalog.Voices(Family).Count
            || KitIndex < 0 || KitIndex >= InstrumentSynthCatalog.DrumKits.Count || MidiNote is < 12 or > 108
            || SampleRate is not (22050 or 44100 or 48000 or 96000) || Channels is < 1 or > 2 || Seed < 0)
            throw new ArgumentException("Invalid instrument, sample format or note.");
        double[] all = [PitchSemitones, Bpm, NoteBeats, DurationMultiplier, AttackMs, DecaySeconds, SustainPercent, ReleaseSeconds, Brightness, Body,
            Noise, Drive, DetuneCents, VibratoCents, VibratoHz, CutoffHz, Resonance, FilterEnvelope, StrikePosition, DrumSweep, DrumSnap, RoomPercent, StereoWidth, GainPercent];
        if (all.Any(v => !double.IsFinite(v)) || Math.Abs(PitchSemitones) > 16 || Bpm is < 20 or > 400 || NoteBeats is < 0.0625 or > 16
            || DurationMultiplier is < 0.5 or > 1.5 || AttackMs is < 0 or > 2000 || DecaySeconds is < 0.01 or > 10 || ReleaseSeconds is < 0.01 or > 10
            || SustainPercent is < 0 or > 100 || Brightness is < 0 or > 100 || Body is < 0 or > 100 || Noise is < 0 or > 100 || Drive is < 0 or > 100
            || DetuneCents is < 0 or > 50 || VibratoCents is < 0 or > 100 || VibratoHz is < 0 or > 20 || CutoffHz is < 20 or > 24000 || Resonance is < 0.1 or > 12
            || FilterEnvelope is < 0 or > 100 || StrikePosition is < 1 or > 99 || DrumSweep is < 0 or > 100 || DrumSnap is < 0 or > 100
            || RoomPercent is < 0 or > 100 || StereoWidth is < 0 or > 100 || GainPercent is < 0 or > 100)
            throw new ArgumentException("Synthesizer parameters are outside their supported ranges.");
    }
}
