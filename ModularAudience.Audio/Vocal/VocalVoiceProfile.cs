using System.Text.Json.Serialization;

namespace ModularAudience.Audio.Vocal;

/// <summary>A user-created, JSON-persistable voice color profile.</summary>
public sealed class VocalVoiceProfile
{
    public string Name { get; set; } = "New Profile";
    public string? BasePresetId { get; set; }
    public double BasePitchHz { get; set; } = 175;
    public double FormantScale { get; set; } = 1.0;
    public double Breathiness { get; set; } = 0.2;
    public double Roughness { get; set; } = 0.1;
    public double VibratoCents { get; set; } = 0;
    public double VibratoHz { get; set; } = 5;
    public double Voicing { get; set; } = 0.85;
    public double Noise { get; set; } = 0.5;

    public VocalVoiceProfile Clone() => new()
    {
        Name = Name,
        BasePresetId = BasePresetId,
        BasePitchHz = BasePitchHz,
        FormantScale = FormantScale,
        Breathiness = Breathiness,
        Roughness = Roughness,
        VibratoCents = VibratoCents,
        VibratoHz = VibratoHz,
        Voicing = Voicing,
        Noise = Noise,
    };

    public static VocalVoiceProfile FromPreset(VocalVoicePreset preset, string? name = null) => new()
    {
        Name = name ?? preset.Name,
        BasePresetId = preset.Id,
        BasePitchHz = preset.BasePitchHz,
        FormantScale = preset.FormantScale,
        Breathiness = preset.Breathiness,
        Roughness = preset.Roughness,
        VibratoCents = preset.VibratoCents,
        VibratoHz = preset.VibratoHz,
        Voicing = preset.Voicing,
        Noise = preset.Noise,
    };

    public VocalVoicePreset ToPreset(string id) => new()
    {
        Id = id,
        Name = Name,
        BasePitchHz = BasePitchHz,
        FormantScale = FormantScale,
        Breathiness = Breathiness,
        Roughness = Roughness,
        VibratoCents = VibratoCents,
        VibratoHz = VibratoHz,
        Voicing = Voicing,
        Noise = Noise,
    };
}