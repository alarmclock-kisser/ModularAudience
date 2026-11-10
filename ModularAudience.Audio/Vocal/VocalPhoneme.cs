namespace ModularAudience.Audio.Vocal;

/// <summary>A single speakable unit with acoustic properties.</summary>
public sealed class VocalPhoneme
{
    public string Symbol { get; init; } = string.Empty;
    public bool Voiced { get; init; } = true;
    /// <summary>Base duration in milliseconds at speed 1.0.</summary>
    public double DurationMs { get; init; } = 80;
    /// <summary>Formant frequencies in Hz (F1, F2, F3). Empty for pure noise.</summary>
    public double[] Formants { get; init; } = [];
    /// <summary>Optional ending frequencies for a moving vowel.</summary>
    public double[] EndFormants { get; init; } = [];
    /// <summary>Formant amplitudes 0..1.</summary>
    public double[] FormantAmplitudes { get; init; } = [];
    /// <summary>Voice-source strength 0..1.</summary>
    public double Voicing { get; init; } = 0.8;
    /// <summary>Noise strength 0..1.</summary>
    public double Noise { get; init; } = 0.2;
    /// <summary>Whether this is a vowel (sustained formant).</summary>
    public bool IsVowel { get; init; } = false;
}