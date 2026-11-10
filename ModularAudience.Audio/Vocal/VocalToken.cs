namespace ModularAudience.Audio.Vocal;

public enum VocalEmphasis
{
    Normal,
    Strong,
    Whisper,
    Lengthened,
    PitchUp,
    PitchDown,
}

public abstract record VocalToken;

public sealed record VocalPauseToken(double Milliseconds) : VocalToken;

public sealed record VocalPhonemeToken(
    string Phoneme,
    VocalEmphasis Emphasis = VocalEmphasis.Normal,
    bool Stressed = false,
    bool Capitalized = false,
    bool Question = false) : VocalToken;

internal sealed record VocalTextToken(
    string Text, VocalEmphasis Emphasis = VocalEmphasis.Normal, bool Capitalized = false, bool Question = false) : VocalToken;