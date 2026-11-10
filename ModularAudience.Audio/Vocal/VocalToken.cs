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

public sealed record VocalPhonemeToken(string Phoneme, VocalEmphasis Emphasis = VocalEmphasis.Normal) : VocalToken;