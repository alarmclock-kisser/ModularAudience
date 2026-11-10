namespace ModularAudience.Audio.Vocal;

/// <summary>What an emphasis marker does to the synthesized speech.</summary>
public enum VocalEmphasisMode
{
    /// <summary>Inserts a pause of the configured length (ms).</summary>
    Pause,
    /// <summary>Raises pitch and volume for a strongly pronounced segment.</summary>
    Strong,
    /// <summary>Lowers volume and adds breathiness for a whispered segment.</summary>
    Whisper,
    /// <summary>Lengthens the following phonemes.</summary>
    Lengthen,
    /// <summary>Raises pitch only.</summary>
    PitchUp,
    /// <summary>Lowers pitch only.</summary>
    PitchDown,
}