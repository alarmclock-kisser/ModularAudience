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
    bool Question = false) : VocalToken
{
    public int Tone { get; init; }
    public double ToneStart { get; init; }
    public double ToneEnd { get; init; } = 1;
    // Zero means unknown; three represents three or more source letters.
    internal int SourceRepetitionLength { get; init; }
}

internal sealed record VocalTextToken(
    string Text, VocalEmphasis Emphasis = VocalEmphasis.Normal, bool Capitalized = false, bool Question = false) : VocalToken;