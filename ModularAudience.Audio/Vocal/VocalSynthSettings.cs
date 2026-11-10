namespace ModularAudience.Audio.Vocal;

public sealed class VocalSynthSettings
{
    public VocalLanguage Language { get; set; } = VocalLanguage.English;
    public string Text { get; set; } = string.Empty;
    public double Speed { get; set; } = 1.0;
    public int SampleRate { get; set; } = 44100;
    public int Channels { get; set; } = 1;
    public VocalEmphasisSyntax EmphasisSyntax { get; set; } = VocalEmphasisSyntax.Default();

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

    public void Validate()
    {
        if (SampleRate < 8000 || SampleRate > 192000) throw new ArgumentOutOfRangeException(nameof(SampleRate));
        if (Channels < 1 || Channels > 2) throw new ArgumentOutOfRangeException(nameof(Channels));
        if (Speed <= 0 || Speed > 4) throw new ArgumentOutOfRangeException(nameof(Speed));
        if (BasePitchHz <= 0) throw new ArgumentOutOfRangeException(nameof(BasePitchHz));
        if (string.IsNullOrWhiteSpace(Text)) throw new ArgumentException("Text must not be empty.", nameof(Text));
    }

    public VocalSynthSettings Clone() => new()
    {
        Language = Language,
        Text = Text,
        Speed = Speed,
        SampleRate = SampleRate,
        Channels = Channels,
        EmphasisSyntax = EmphasisSyntax,
        VoiceName = VoiceName,
        BasePitchHz = BasePitchHz,
        FormantScale = FormantScale,
        Breathiness = Breathiness,
        Roughness = Roughness,
        VibratoCents = VibratoCents,
        VibratoHz = VibratoHz,
        Voicing = Voicing,
        Noise = Noise,
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
        };
        return s;
    }
}