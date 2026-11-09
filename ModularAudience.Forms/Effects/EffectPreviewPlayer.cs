using NAudio.Wave;

namespace ModularAudience.Forms.Effects;

internal interface IEffectPreviewPlayer : IDisposable
{
    bool IsPlaying { get; }
    void Play(float[] samples, int sampleRate, int channels, bool loop);
    void Stop();
}

/// <summary>Plays immutable float samples directly from RAM, without WAV/temp files.</summary>
internal sealed class EffectPreviewPlayer : IEffectPreviewPlayer
{
    private WaveOut? output;
    public bool IsPlaying => this.output?.PlaybackState == PlaybackState.Playing;

    public void Play(float[] samples, int sampleRate, int channels, bool loop)
    {
        this.Stop();
        var next = new WaveOut { BufferMilliseconds = 100 };
        try
        {
            next.Init(new EffectPreviewSamples(samples, sampleRate, channels, loop));
            next.Play();
            this.output = next;
        }
        catch { next.Dispose(); throw; }
    }

    public void Stop()
    {
        WaveOut? previous = this.output;
        this.output = null;
        if (previous is null) return;
        try { previous.Stop(); }
        finally { previous.Dispose(); }
    }

    public void Dispose() => this.Stop();
}

internal sealed class EffectPreviewSamples : ISampleProvider
{
    private readonly float[] samples;
    private readonly bool loop;
    private int position;

    internal EffectPreviewSamples(float[] samples, int sampleRate, int channels, bool loop)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (sampleRate <= 0 || channels <= 0 || samples.Length % channels != 0) throw new ArgumentException("Invalid preview format.");
        this.samples = samples;
        this.loop = loop;
        this.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        if (offset < 0 || count < 0 || offset > buffer.Length - count) throw new ArgumentOutOfRangeException(nameof(count));
        return this.Read(buffer.AsSpan(offset, count));
    }

    public int Read(Span<float> buffer)
    {
        int written = 0;
        while (written < buffer.Length && this.samples.Length > 0)
        {
            if (this.position == this.samples.Length)
            {
                if (!this.loop) break;
                this.position = 0;
            }
            int copy = Math.Min(buffer.Length - written, this.samples.Length - this.position);
            this.samples.AsSpan(this.position, copy).CopyTo(buffer[written..]);
            this.position += copy;
            written += copy;
        }
        return written;
    }
}
