using System.Globalization;

namespace ModularAudience.Audio.Synthesis;

public enum SynthFamily { Keyboard, DrumSet, Woodwinds, Brass, PluckedStrings, BowedStrings, MalletsAndBells, Organ, SynthBassAndLead, SynthPadsAndPoly, SynthKeysAndPlucks, OtherSoundFx }
public enum SynthDrum { Kick, BassDrum, Snare, Rimshot, Clap, ClosedHiHat, OpenHiHat, LowTom, MidTom, HighTom, Ride, Crash, Cowbell, Shaker, PedalHiHat }

public sealed record SynthVoice(string Name, double Brightness, double Decay, double Release, double Body, double Noise, double Drive, double Detune, double Vibrato)
{
    public override string ToString() => Name;
}
public sealed record SynthDrumKit(string Name, double Tone, double Decay, double Sweep, double Noise, double Metal, double Drive, double Click)
{
    public double BitCrush { get; init; }
    public double Stutter { get; init; }
    public override string ToString() => Name;
}

public static class InstrumentSynthCatalog
{
    public static string FamilyName(SynthFamily family) => family switch
    {
        SynthFamily.DrumSet => "Drum Set", SynthFamily.PluckedStrings => "Plucked Strings",
        SynthFamily.BowedStrings => "Bowed Strings", SynthFamily.MalletsAndBells => "Mallets & Bells",
        SynthFamily.SynthBassAndLead => "Synth Bass & Lead",
        SynthFamily.SynthPadsAndPoly => "Synth Pads & Poly",
        SynthFamily.SynthKeysAndPlucks => "Synth Keys & Plucks",
        SynthFamily.OtherSoundFx => "Other Sound FX",
        _ => family.ToString()
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
        new("Tribal Earth", 0.7, 1.3, 0.35, 0.75, 0.45, 0.2, 0.5),
        new("Minimal Techno", 0.86, 0.52, 1.35, 0.5, 0.75, 0.08, 0.42),
        new("Electro Breakbeat", 1.08, 0.78, 1.65, 0.95, 1.25, 0.18, 0.82),
        new("Hard Techno", 0.92, 1.2, 2.3, 1.1, 1.7, 0.55, 0.9),
        new("Jungle Break", 1.05, 0.78, 1.55, 0.9, 0.85, 0.16, 0.85) { BitCrush = 0.06, Stutter = 0.12 },
        new("Breakcore Crunch", 1.18, 0.5, 2.1, 1.15, 1.25, 0.62, 1.0) { BitCrush = 0.58, Stutter = 0.65 },
        new("Ragga Jungle", 0.9, 1.15, 1.05, 1.25, 0.82, 0.28, 0.75) { BitCrush = 0.08, Stutter = 0.05 },
        new("Glitch DnB", 1.22, 0.62, 2.4, 1.3, 1.8, 0.45, 0.9) { BitCrush = 0.72, Stutter = 0.88 },
        new("Chiptune DnB", 0.78, 0.48, 1.7, 0.55, 0.35, 0.16, 0.55) { BitCrush = 0.92 },
        new("Liquid DnB", 0.95, 1.05, 0.9, 0.75, 0.82, 0.05, 0.5),
        new("Neurofunk", 0.92, 0.83, 2.05, 1.05, 1.8, 0.48, 0.9) { BitCrush = 0.25, Stutter = 0.12 },
        new("Jump-Up", 1.12, 0.82, 1.7, 1.0, 1.1, 0.3, 0.88) { BitCrush = 0.14, Stutter = 0.1 }
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
        SynthFamily.SynthPadsAndPoly => SynthPads,
        SynthFamily.SynthKeysAndPlucks => SynthKeys,
        SynthFamily.OtherSoundFx => SoundFx,
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
        new("Dream Unison", 70, 2, 1.5, 55, 0, 3, 20, 18),
        new("Deep House Bass", 28, 1.4, 0.25, 95, 0, 8, 2, 0),
        new("Offbeat Bass", 48, 0.8, 0.18, 75, 0, 14, 4, 0),
        new("Donk Bass", 82, 0.5, 0.16, 58, 0, 24, 7, 0),
        new("Wobble Bass", 60, 1.3, 0.3, 65, 0, 20, 3, 0),
        new("Acid 303 Slide", 88, 0.65, 0.2, 48, 0, 38, 2, 0),
        new("Neuro Bass", 80, 1, 0.4, 70, 2, 30, 18, 0)
    ];
    private static readonly SynthVoice[] SynthPads =
    [
        new("Supersaw Stack", 90, 3.8, 1.8, 55, 0, 10, 26, 10),
        new("Wide Unison Saw", 78, 2.7, 1.4, 50, 0, 8, 18, 8),
        new("Warm Analog Pad", 48, 4, 2.2, 75, 0, 5, 12, 16),
        new("Dark Drone Pad", 22, 5.5, 2.5, 95, 1, 14, 15, 4),
        new("Hoover Rave Lead", 96, 1.3, 0.35, 65, 2, 32, 20, 9)
    ];
    private static readonly SynthVoice[] SynthKeys =
    [
        new("Digital Pluck", 82, 0.55, 0.18, 40, 0, 8, 5, 0),
        new("FM Bell", 94, 3.2, 1.3, 20, 0, 5, 2, 5),
        new("House Techno Stab", 74, 0.45, 0.2, 62, 0, 18, 10, 2)
    ];
    private static readonly SynthVoice[] SoundFx =
    [
        new("100 Hz Censor Tone", 42, 0.55, 0.12, 42, 0, 10, 0, 0),
        new("Buzzer", 58, 0.7, 0.14, 50, 5, 26, 0, 0),
        new("Synthetic Vocal Whoop", 54, 0.7, 0.22, 72, 12, 6, 5, 0),
        new("8-Bit Coin", 92, 0.22, 0.1, 18, 0, 4, 0, 0),
        new("8-Bit Laser", 88, 0.42, 0.1, 22, 4, 18, 0, 0),
        new("8-Bit Power-Up", 90, 0.62, 0.12, 30, 0, 10, 0, 0),
        new("8-Bit Game Over", 76, 1.0, 0.22, 35, 0, 12, 0, 0),
        new("DJ Scratch", 76, 0.5, 0.12, 55, 70, 14, 0, 0),
        new("Morse SOS", 34, 0.8, 0.18, 30, 0, 3, 0, 0),
        new("Sonar Ping", 42, 1.2, 0.45, 70, 2, 0, 1, 0),
        new("Boat Foghorn", 22, 2.4, 0.5, 92, 8, 18, 0, 2),
        new("Alarm Siren", 62, 1.8, 0.2, 45, 0, 10, 0, 0),
        new("Radio Static", 95, 0.7, 0.18, 22, 95, 10, 0, 0)
    ];
}

public sealed class InstrumentSynthSettings
{
    public static IReadOnlyList<(string Name, double Beats)> KeyDurations { get; } =
    [
        ("1/64", 0.0625), ("1/32", 0.125), ("1/16", 0.25), ("1/8", 0.5),
        ("1/4", 1), ("1/2", 2), ("1 Bar", 4), ("2 Bars", 8), ("4 Bars", 16)
    ];
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
    public string KeyDurationName
    {
        get
        {
            string name = $"{NoteBeats.ToString("0.###", CultureInfo.InvariantCulture)} beats";
            foreach (var duration in KeyDurations)
                if (Math.Abs(duration.Beats - NoteBeats) < 1e-9) { name = duration.Name; break; }
            return DurationMultiplier switch { > 1.25 => name + " Dotted", < 0.8 => name + " Triplet", _ => name };
        }
    }
    public InstrumentSynthSettings Clone() => (InstrumentSynthSettings)MemberwiseClone();
    public void ApplyVoiceDefaults()
    {
        var voice = InstrumentSynthCatalog.Voices(Family)[VoiceIndex];
        Brightness = voice.Brightness; DecaySeconds = voice.Decay; ReleaseSeconds = voice.Release;
        Body = voice.Body; Noise = voice.Noise; Drive = voice.Drive; DetuneCents = voice.Detune; VibratoCents = voice.Vibrato;
        AttackMs = Family switch
        {
            SynthFamily.BowedStrings => 100,
            SynthFamily.Woodwinds => 40,
            SynthFamily.Brass => 30,
            SynthFamily.Organ => 8,
            SynthFamily.SynthPadsAndPoly => voice.Name switch { "Warm Analog Pad" => 220, "Dark Drone Pad" => 360, _ => 5 },
            SynthFamily.SynthKeysAndPlucks => 2,
            _ => 3
        };
        SustainPercent = Family is SynthFamily.Keyboard or SynthFamily.PluckedStrings or SynthFamily.MalletsAndBells
            or SynthFamily.DrumSet or SynthFamily.SynthKeysAndPlucks or SynthFamily.OtherSoundFx ? 0 : 75;
        if (Family == SynthFamily.SynthBassAndLead)
        {
            switch (voice.Name)
            {
                case "Deep House Bass":
                    CutoffHz = 2200; FilterEnvelope = 20; SustainPercent = 65;
                    break;
                case "Offbeat Bass":
                    CutoffHz = 3500; FilterEnvelope = 55; SustainPercent = 20;
                    break;
                case "Donk Bass":
                    CutoffHz = 4500; FilterEnvelope = 75; SustainPercent = 0;
                    break;
                case "Wobble Bass":
                    CutoffHz = 3500; FilterEnvelope = 80; Resonance = 2.2; VibratoHz = 2.5; VibratoCents = 0; SustainPercent = 50;
                    break;
                case "Acid 303 Slide":
                    CutoffHz = 4800; FilterEnvelope = 88; Resonance = 3; SustainPercent = 0;
                    break;
                case "Neuro Bass":
                    CutoffHz = 5200; FilterEnvelope = 70; Resonance = 2.5; SustainPercent = 55;
                    break;
            }
        }
        if (Family == SynthFamily.SynthPadsAndPoly)
        {
            SustainPercent = 85;
            if (voice.Name == "Hoover Rave Lead") SustainPercent = 65;
            if (voice.Name == "Dark Drone Pad") CutoffHz = 2400;
            if (voice.Name == "Warm Analog Pad") CutoffHz = 6500;
            if (voice.Name is "Supersaw Stack" or "Wide Unison Saw") CutoffHz = 12000;
        }
        if (Family == SynthFamily.SynthKeysAndPlucks)
        {
            SustainPercent = 0;
            switch (voice.Name)
            {
                case "Digital Pluck":
                    CutoffHz = 9000; FilterEnvelope = 65;
                    break;
                case "FM Bell":
                    CutoffHz = 15000; FilterEnvelope = 20;
                    break;
                case "House Techno Stab":
                    CutoffHz = 6500; FilterEnvelope = 75;
                    break;
            }
        }
        if (Family == SynthFamily.OtherSoundFx)
        {
            (CutoffHz, FilterEnvelope, SustainPercent) = voice.Name switch
            {
                "100 Hz Censor Tone" => (2400, 0, 85),
                "Buzzer" => (5000, 8, 90),
                "Synthetic Vocal Whoop" => (8500, 20, 0),
                "8-Bit Coin" => (10000, 0, 0),
                "8-Bit Laser" => (8500, 0, 0),
                "8-Bit Power-Up" => (10000, 0, 0),
                "8-Bit Game Over" => (7500, 0, 0),
                "DJ Scratch" => (7000, 0, 0),
                "Morse SOS" => (3500, 0, 85),
                "Sonar Ping" => (6500, 30, 0),
                "Boat Foghorn" => (2500, 0, 85),
                "Alarm Siren" => (7000, 0, 85),
                "Radio Static" => (10500, 0, 0),
                _ => (8000, 0, 0)
            };
        }
        if (Family == SynthFamily.DrumSet) { DecaySeconds = 0.5; ReleaseSeconds = 0.25; AttackMs = 1; Noise = 50; }
    }
    public void Validate()
    {
        if (!Enum.IsDefined(Family) || !Enum.IsDefined(Drum) || VoiceIndex < 0 || VoiceIndex >= InstrumentSynthCatalog.Voices(Family).Count
            || KitIndex < 0 || KitIndex >= InstrumentSynthCatalog.DrumKits.Count || MidiNote is < 0 or > 127
            || SampleRate is not (22050 or 44100 or 48000 or 96000) || Channels is < 1 or > 2 || Seed < 0)
            throw new ArgumentException("Invalid instrument, sample format or note.");
        double[] all = [PitchSemitones, Bpm, NoteBeats, DurationMultiplier, AttackMs, DecaySeconds, SustainPercent, ReleaseSeconds, Brightness, Body,
            Noise, Drive, DetuneCents, VibratoCents, VibratoHz, CutoffHz, Resonance, FilterEnvelope, StrikePosition, DrumSweep, DrumSnap, RoomPercent, StereoWidth, GainPercent];
        if (all.Any(v => !double.IsFinite(v)) || Math.Abs(PitchSemitones) > 64 || Bpm is < 10 or > 400 || NoteBeats is < 0.0001 or > 256
            || DurationMultiplier is < 0.5 or > 1.5 || AttackMs is < 0 or > 2000 || DecaySeconds is < 0.01 or > 10 || ReleaseSeconds is < 0.01 or > 10
            || SustainPercent is < 0 or > 100 || Brightness is < 0 or > 100 || Body is < 0 or > 100 || Noise is < 0 or > 100 || Drive is < 0 or > 100
            || DetuneCents is < 0 or > 50 || VibratoCents is < 0 or > 100 || VibratoHz is < 0 or > 20 || CutoffHz is < 20 or > 24000 || Resonance is < 0.1 or > 12
            || FilterEnvelope is < 0 or > 100 || StrikePosition is < 1 or > 99 || DrumSweep is < 0 or > 100 || DrumSnap is < 0 or > 100
            || RoomPercent is < 0 or > 100 || StereoWidth is < 0 or > 100 || GainPercent is < 0 or > 100)
            throw new ArgumentException("Synthesizer parameters are outside their supported ranges.");
    }
}
