using ModularAudience.Audio.Midi;

namespace ModularAudience.Audio.Vocal;

public sealed class VocalSynthSettings
{
    public VocalLanguage Language { get; set; } = VocalLanguage.English;
    public bool UseWindowsSpeech { get; set; }
    public bool BeepForUnsupportedSounds { get; set; }
    public string Text { get; set; } = string.Empty;
    public double Speed { get; set; } = 1.0;
    public int SampleRate { get; set; } = 44100;
    public int Channels { get; set; } = 1;
    public VocalEmphasisSyntax EmphasisSyntax { get; set; } = VocalEmphasisSyntax.Default();
    public MidiFileData? SingingMidiFile { get; set; }
    public int? SingingMidiTrackIndex { get; set; }

    // Voice params
    public string VoiceName { get; set; } = "Adult Female";
    public double BasePitchHz { get; set; } = 195;
    public double FormantScale { get; set; } = 1.0;
    public double Breathiness { get; set; } = 0.2;
    public double Roughness { get; set; } = 0.08;
    public double VibratoCents { get; set; } = 10;
    public double VibratoHz { get; set; } = 5;
    public double Voicing { get; set; } = 0.85;
    public double Noise { get; set; } = 0.5;
    public bool Singing { get; set; }

    public void Validate()
    {
        if (SampleRate < 8000 || SampleRate > 192000) throw new ArgumentOutOfRangeException(nameof(SampleRate));
        if (Channels < 1 || Channels > 2) throw new ArgumentOutOfRangeException(nameof(Channels));
        if (!double.IsFinite(Speed) || Speed <= 0 || Speed > 4) throw new ArgumentOutOfRangeException(nameof(Speed));
        if (!double.IsFinite(BasePitchHz) || BasePitchHz < 20 || BasePitchHz >= SampleRate * 0.2)
            throw new ArgumentOutOfRangeException(nameof(BasePitchHz));
        if (!double.IsFinite(FormantScale) || FormantScale <= 0 || FormantScale > 3)
            throw new ArgumentOutOfRangeException(nameof(FormantScale));
        ValidateUnitRange(Breathiness, nameof(Breathiness));
        ValidateUnitRange(Roughness, nameof(Roughness));
        ValidateUnitRange(Voicing, nameof(Voicing));
        ValidateUnitRange(Noise, nameof(Noise));
        if (!double.IsFinite(VibratoCents) || VibratoCents < 0 || VibratoCents > 200)
            throw new ArgumentOutOfRangeException(nameof(VibratoCents));
        if (!double.IsFinite(VibratoHz) || VibratoHz < 0 || VibratoHz > 40)
            throw new ArgumentOutOfRangeException(nameof(VibratoHz));
        if (string.IsNullOrWhiteSpace(Text)) throw new ArgumentException("Text must not be empty.", nameof(Text));
        if (UseWindowsSpeech && Language is not (VocalLanguage.English or VocalLanguage.German))
            throw new ArgumentException("Windows speech is available for English and German only.", nameof(Language));
        if (UseWindowsSpeech && Speed < 0.5)
            throw new ArgumentOutOfRangeException(nameof(Speed), "Windows speech speed must be at least 0.5.");
        if (Singing && SingingMidiFile is not null)
        {
            if (SingingMidiTrackIndex is null
                || !SingingMidiFile.Tracks.Any(track => track.Index == SingingMidiTrackIndex.Value))
                throw new ArgumentException("Select an existing MIDI track for singing.", nameof(SingingMidiTrackIndex));
        }
    }

    private static void ValidateUnitRange(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0 || value > 1)
            throw new ArgumentOutOfRangeException(name);
    }

    public VocalSynthSettings Clone() => new()
    {
        Language = Language,
        UseWindowsSpeech = UseWindowsSpeech,
        BeepForUnsupportedSounds = BeepForUnsupportedSounds,
        Text = Text,
        Speed = Speed,
        SampleRate = SampleRate,
        Channels = Channels,
        EmphasisSyntax = EmphasisSyntax,
        SingingMidiFile = SingingMidiFile,
        SingingMidiTrackIndex = SingingMidiTrackIndex,
        VoiceName = VoiceName,
        BasePitchHz = BasePitchHz,
        FormantScale = FormantScale,
        Breathiness = Breathiness,
        Roughness = Roughness,
        VibratoCents = VibratoCents,
        VibratoHz = VibratoHz,
        Voicing = Voicing,
        Noise = Noise,
        Singing = Singing,
    };

    public static VocalSynthSettings FromPreset(VocalVoicePreset preset, VocalLanguage language = VocalLanguage.English, string text = "")
    {
        var s = new VocalSynthSettings
        {
            Language = language,
            Text = text,
            VoiceName = preset.Name,
            BasePitchHz = preset.BasePitchHz,
            FormantScale = preset.FormantScale,
            Breathiness = preset.Breathiness,
            Roughness = preset.Roughness,
            VibratoCents = preset.VibratoCents,
            VibratoHz = preset.VibratoHz,
            Voicing = preset.Voicing,
            Noise = preset.Noise,
            Singing = preset.Singing,
        };
        return s;
    }

    public static VocalSynthSettings FromProfile(VocalVoiceProfile profile, VocalLanguage language = VocalLanguage.English, string text = "")
    {
        var s = new VocalSynthSettings
        {
            Language = language,
            Text = text,
            VoiceName = profile.Name,
            BasePitchHz = profile.BasePitchHz,
            FormantScale = profile.FormantScale,
            Breathiness = profile.Breathiness,
            Roughness = profile.Roughness,
            VibratoCents = profile.VibratoCents,
            VibratoHz = profile.VibratoHz,
            Voicing = profile.Voicing,
            Noise = profile.Noise,
            Singing = profile.Singing,
        };
        return s;
    }
}