using ModularAudience.Audio.Vocal;

namespace ModularAudience.Audio.Vocal;

public static class VocalSynth
{
    internal const int MaxOutputSamples = VocalSynthEngine.MaxOutputSamples;

    public static bool IsValidCustomSampleIpa(string? value) =>
        VocalSynthEngine.IsValidCustomSampleIpa(value);

    public static IReadOnlyList<VocalSyllableTextRange> GetSyllableTextRanges(
        VocalSynthSettings settings, CancellationToken cancellationToken = default) =>
        VocalSynthEngine.GetSyllableTextRanges(settings, cancellationToken);

    public static IReadOnlyList<VocalPlaybackTextRange> GetPlaybackTimeline(
        VocalSynthSettings settings, CancellationToken cancellationToken = default) =>
        VocalSynthEngine.GetPlaybackTimeline(settings, cancellationToken);

    public static AudioObj Render(VocalSynthSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.UseWindowsSpeech
            ? VocalWindowsSpeech.Render(settings, cancellationToken)
            : ToAudioObj(VocalSynthEngine.Render(settings, cancellationToken));
    }

    public static string? GetMidiSingingWarning(
        VocalSynthSettings settings, CancellationToken cancellationToken = default) =>
        VocalSynthEngine.GetMidiSingingWarning(settings, cancellationToken);

    public static VocalMidiSyllableFillResult FillMissingMidiSyllables(
        VocalSynthSettings settings, string existingText, CancellationToken cancellationToken = default) =>
        VocalSynthEngine.FillMissingMidiSyllables(settings, existingText, cancellationToken);

    internal static AudioObj CreateAudio(
        float[] data, VocalSynthSettings settings, CancellationToken cancellationToken) =>
        ToAudioObj(VocalSynthEngine.CreateAudio(data, settings, cancellationToken));

    internal static int StableSeed(VocalSynthSettings settings) =>
        VocalSynthEngine.StableSeed(settings);

    private static AudioObj ToAudioObj(VocalAudioData source)
    {
        AudioObj result = new()
        {
            Data = source.Data,
            SampleRate = source.SampleRate,
            Channels = source.Channels,
            BitDepth = source.BitDepth,
            Length = source.Length,
            Duration = source.Duration,
            Bpm = source.Bpm,
            Volume = source.Volume,
        };
        result.Rename(source.Name);
        return result;
    }
}
