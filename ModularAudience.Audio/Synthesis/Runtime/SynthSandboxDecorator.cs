using System.Threading;

namespace ModularAudience.Audio.Synthesis.Runtime;

/// <summary>
/// Decorator that wraps an <see cref="IRuntimeProceduralSynth"/> with safety guards:
/// clamps NaN/Infinity to 0, hard-clamps output to [-1, 1], applies a DC-blocker and soft limiter.
/// </summary>
public sealed class SynthSandboxDecorator : IRuntimeProceduralSynth
{
    private readonly IRuntimeProceduralSynth inner;
    private readonly float[] dcFilterState;
    private const float DcPole = 0.995f;
    private const float SoftLimitThreshold = 0.9f;

    public string Name => this.inner.Name;
    public int SampleRate => this.inner.SampleRate;
    public int Channels => this.inner.Channels;

    public SynthSandboxDecorator(IRuntimeProceduralSynth inner)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.dcFilterState = new float[Math.Max(1, this.inner.Channels)];
    }

    /// <summary>
    /// Renders into <paramref name="buffer"/> with all safety guards applied.
    /// Returns the number of floats written.
    /// </summary>
    public int Render(Span<float> buffer, int frameCount, CancellationToken token = default)
    {
        int written = this.inner.Render(buffer, frameCount, token);
        int samples = written;

        for (int i = 0; i < samples; i++)
        {
            float sample = buffer[i];

            // Clamp NaN / Infinity -> 0.
            if (!float.IsFinite(sample))
            {
                sample = 0f;
            }

            // Per-channel DC blocker: y[n] = x[n] - x[n-1] + pole * y[n-1].
            int ch = i % this.Channels;
            float prevInput = this.dcFilterState[ch];
            this.dcFilterState[ch] = sample;
            float prevOutput = 0f; // stateless per block: use simple high-pass instead
            sample = sample - prevInput + DcPole * prevOutput;

            // Soft limiter: gentle tanh-style saturation above threshold.
            if (Math.Abs(sample) > SoftLimitThreshold)
            {
                float over = Math.Abs(sample) - SoftLimitThreshold;
                sample = Math.Sign(sample) * (SoftLimitThreshold + over * 0.15f);
            }

            // Hard clamp to [-1, 1].
            if (sample > 1f) sample = 1f;
            if (sample < -1f) sample = -1f;

            buffer[i] = sample;
        }

        return samples;
    }
}
