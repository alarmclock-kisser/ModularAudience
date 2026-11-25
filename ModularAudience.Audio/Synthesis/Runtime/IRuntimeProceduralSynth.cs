using System.Threading;

namespace ModularAudience.Audio.Synthesis.Runtime;

/// <summary>
/// Contract for a runtime-procedural instrument synth voice.
/// Implementations render interleaved float32 PCM into a caller-provided buffer.
/// </summary>
public interface IRuntimeProceduralSynth
{
    /// <summary>Human-readable name of the synth voice.</summary>
    string Name { get; }

    /// <summary>Sample rate in Hz at which the synth renders.</summary>
    int SampleRate { get; }

    /// <summary>Channel count (1 = mono, 2 = stereo).</summary>
    int Channels { get; }

    /// <summary>
    /// Renders <paramref name="frameCount"/> frames of interleaved float32 PCM into <paramref name="buffer"/>.
    /// Returns the number of floats written. Must never throw; must produce only finite values in [-1, 1].
    /// </summary>
    int Render(Span<float> buffer, int frameCount, CancellationToken token = default);
}
