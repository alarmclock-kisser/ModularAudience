using System.Text.Json.Serialization;

namespace ModularAudience.Audio.Vocal;

/// <summary>A hard-coded computer-voice color profile.</summary>
public sealed class VocalVoicePreset
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    /// <summary>Baseline fundamental frequency in Hz.</summary>
    public double BasePitchHz { get; init; } = 175;
    /// <summary>Multiplier applied to formant frequencies (1.0 = neutral).</summary>
    public double FormantScale { get; init; } = 1.0;
    /// <summary>Breathiness 0..1.</summary>
    public double Breathiness { get; init; } = 0.2;
    /// <summary>Roughness/jitter 0..1.</summary>
    public double Roughness { get; init; } = 0.1;
    /// <summary>Vibrato depth in cents.</summary>
    public double VibratoCents { get; init; } = 0;
    /// <summary>Vibrato rate in Hz.</summary>
    public double VibratoHz { get; init; } = 5;
    /// <summary>Voicing strength 0..1.</summary>
    public double Voicing { get; init; } = 0.85;
    /// <summary>Noise strength for consonants 0..1.</summary>
    public double Noise { get; init; } = 0.5;

    public static IReadOnlyList<VocalVoicePreset> BuiltIn { get; } =
    [
        new() { Id = "child", Name = "Child", BasePitchHz = 260, FormantScale = 1.15, Breathiness = 0.15, Roughness = 0.05, VibratoCents = 15, VibratoHz = 6, Voicing = 0.9, Noise = 0.4 },
        new() { Id = "young-female", Name = "Young Female", BasePitchHz = 220, FormantScale = 1.08, Breathiness = 0.18, Roughness = 0.06, VibratoCents = 12, VibratoHz = 5.5, Voicing = 0.88, Noise = 0.45 },
        new() { Id = "adult-female", Name = "Adult Female", BasePitchHz = 195, FormantScale = 1.0, Breathiness = 0.2, Roughness = 0.08, VibratoCents = 10, VibratoHz = 5, Voicing = 0.85, Noise = 0.5 },
        new() { Id = "adult-male", Name = "Adult Male", BasePitchHz = 125, FormantScale = 0.9, Breathiness = 0.15, Roughness = 0.1, VibratoCents = 8, VibratoHz = 4.5, Voicing = 0.8, Noise = 0.55 },
        new() { Id = "deep-male", Name = "Deep Male", BasePitchHz = 95, FormantScale = 0.82, Breathiness = 0.12, Roughness = 0.12, VibratoCents = 6, VibratoHz = 4, Voicing = 0.78, Noise = 0.6 },
        new() { Id = "robot", Name = "Robot", BasePitchHz = 150, FormantScale = 1.0, Breathiness = 0.0, Roughness = 0.0, VibratoCents = 0, VibratoHz = 0, Voicing = 1.0, Noise = 0.3 },
        new() { Id = "whisper", Name = "Whisper", BasePitchHz = 180, FormantScale = 1.0, Breathiness = 0.9, Roughness = 0.05, VibratoCents = 0, VibratoHz = 0, Voicing = 0.3, Noise = 0.7 },
        new() { Id = "elderly", Name = "Elderly", BasePitchHz = 140, FormantScale = 0.95, Breathiness = 0.35, Roughness = 0.25, VibratoCents = 18, VibratoHz = 6.5, Voicing = 0.75, Noise = 0.6 },
    ];
}