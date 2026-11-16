namespace ModularAudience.Audio.Vocal;

public readonly record struct VocalPlaybackTextRange(
    int TextStart,
    int TextLength,
    int StartFrame,
    int EndFrame);
