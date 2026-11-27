using ModularAudience.Audio.Processors_V4;

namespace ModularAudience.Audio;

/// <summary>
/// Public wrapper around the internal pYIN pitch tracker, so that
/// UI layers (e.g. the CustomMidiEditorDialog) can access pitch tracking
/// without needing InternalsVisibleTo into the VocalCore assembly.
/// </summary>
public static class PitchTracker
{
    /// <summary>
    /// Runs monophonic pYIN pitch tracking on the given mono signal.
    /// Returns one frame per hop; frames with a non-positive FrequencyHz are unvoiced.
    /// </summary>
    public static PitchFrame[] Track(
        float[] mono,
        int sampleRate,
        int hopSize,
        double minimumHz,
        double maximumHz,
        int threads,
        CancellationToken cancellationToken)
    {
        PyinPitchFrame[] frames = PyinPitchTracker.Track(mono, sampleRate, hopSize, minimumHz, maximumHz, threads, cancellationToken);
        return frames
            .Select(frame => new PitchFrame(frame.FrequencyHz, frame.VoicedProbability))
            .ToArray();
    }
}

/// <summary>
/// A single pYIN pitch frame. FrequencyHz is zero for an unvoiced frame.
/// VoicedProbability is the acoustic candidate mass before HMM decoding.
/// </summary>
public sealed record PitchFrame(double FrequencyHz, double VoicedProbability);
