using System.Threading.Channels;
using ModularAudience.Audio;
using ModularAudience.Audio.Vocal;
using NAudio.Wave;

namespace ModularAudience.Forms.Synthesis;

/// <summary>
/// Turns LLM text deltas into a continuous spoken stream. Text is buffered into
/// coherent chunks, rendered with the current VocalSynth voice and fed into one
/// WaveOut through a growable audio queue, so chunks never cause playback gaps
/// and a long answer is never dropped. When the token stream is slower than the
/// requested reading speed, the speed of upcoming chunks is scaled down smoothly
/// instead of stuttering.
/// </summary>
public sealed class VocalStreamSpeaker : IDisposable
{
    public const long DefaultMaxBufferedBytes = StreamingWaveBuffer.DefaultMaxBufferedBytes;

    private readonly Func<VocalSynthSettings> settingsFactory;
    private readonly VocalStreamChunker chunker;
    private readonly Channel<string> deltas = Channel.CreateUnbounded<string>();
    private readonly CancellationTokenSource shutdown = new();
    private readonly StreamingWaveBuffer buffer;
    private readonly int flushMilliseconds;
    private readonly int minBufferMilliseconds;
    private readonly bool adaptiveSpeed;
    private readonly double minSpeedFactor;
    private readonly double configuredSpeed;

    private WaveOut? output;
    private Task? worker;
    private double speedFactor = 1.0;
    private bool disposed;

    public VocalStreamSpeaker(
        Func<VocalSynthSettings> settingsFactory,
        VocalStreamChunkingSettings chunking,
        int flushMilliseconds,
        int minBufferMilliseconds,
        bool adaptiveSpeed,
        double minSpeedFactor,
        double configuredSpeed,
        int sampleRate = 44100,
        int channels = 1)
    {
        this.settingsFactory = settingsFactory;
        chunker = new VocalStreamChunker(chunking);
        this.flushMilliseconds = Math.Clamp(flushMilliseconds, 100, 10000);
        this.minBufferMilliseconds = Math.Clamp(minBufferMilliseconds, 100, 10000);
        this.adaptiveSpeed = adaptiveSpeed;
        this.minSpeedFactor = Math.Clamp(minSpeedFactor, 0.25, 1.0);
        this.configuredSpeed = Math.Clamp(configuredSpeed, 0.25, 4.0);

        buffer = new StreamingWaveBuffer(WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels));
    }

    /// <summary>Estimated buffered audio in milliseconds.</summary>
    public double BufferedMilliseconds => buffer.BufferedDuration.TotalMilliseconds;

    /// <summary>Soft memory limit for queued audio (1 GiB by default).</summary>
    public long MaxBufferedBytes => buffer.MaxBufferedBytes;

    /// <summary>Current speed multiplier applied on top of the VocalSynth speed.</summary>
    public double SpeedFactor => speedFactor;

    public double EffectiveSpeed => configuredSpeed * speedFactor;

    public event Action<string>? Error;

    /// <summary>Raised after a rendered chunk has been handed to the playback buffer.</summary>
    public event Action<string, double>? ChunkRendered;

    /// <summary>
    /// Raised when all text of a turn has been rendered and the audio buffer has
    /// drained. Until this fires, the turn is still speaking.
    /// </summary>
    public event Action? TurnAudioCompleted;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (worker != null)
        {
            return;
        }

        output = new WaveOut
        {
            BufferMilliseconds = 80,
            NumberOfBuffers = 3,
        };
        output.Init(buffer);
        output.Play();
        worker = Task.Run(() => RenderLoopAsync(shutdown.Token), CancellationToken.None);
    }

    public void PushDelta(string? delta)
    {
        if (!string.IsNullOrEmpty(delta) && !shutdown.IsCancellationRequested)
        {
            deltas.Writer.TryWrite(delta);
        }
    }

    /// <summary>Ends the current assistant turn: speaks the remaining partial text.</summary>
    public void EndTurn()
    {
        if (!shutdown.IsCancellationRequested)
        {
            deltas.Writer.TryWrite(Sentinel);
        }
    }

    private const string Sentinel = "\u0000__vocal_llm_turn_end__";

    public void Stop()
    {
        if (disposed)
        {
            return;
        }

        deltas.Writer.TryComplete();
        shutdown.Cancel();
        try
        {
            worker?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // Rendering cancellation is normal during shutdown.
        }

        try
        {
            output?.Stop();
            output?.Dispose();
        }
        catch (InvalidOperationException)
        {
            // Device already released.
        }

        output = null;
        worker = null;
        buffer.ClearBuffer();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Stop();
        shutdown.Dispose();
    }

    private async Task RenderLoopAsync(CancellationToken token)
    {
        try
        {
            bool hasPending = false;
            while (!token.IsCancellationRequested)
            {
                Task<bool> readTask = deltas.Reader.WaitToReadAsync(token).AsTask();
                Task delayTask = Task.Delay(hasPending ? flushMilliseconds : 250, token);
                Task finished = await Task.WhenAny(readTask, delayTask).ConfigureAwait(false);

                if (finished == readTask)
                {
                    if (!await readTask.ConfigureAwait(false))
                    {
                        break;
                    }

                    while (deltas.Reader.TryRead(out string? delta))
                    {
                        if (string.Equals(delta, Sentinel, StringComparison.Ordinal))
                        {
                            foreach (string chunk in chunker.Flush())
                            {
                                await RenderAsync(chunk, token).ConfigureAwait(false);
                            }

                            hasPending = false;
                            // The SSE stream has ended, but the sentence is not done
                            // until the rendered audio has actually been played.
                            await WaitForAudioDrainAsync(token).ConfigureAwait(false);
                            TurnAudioCompleted?.Invoke();
                            continue;
                        }

                        foreach (string chunk in chunker.Append(delta))
                        {
                            await RenderAsync(chunk, token).ConfigureAwait(false);
                        }
                    }

                    hasPending = chunker.HasBufferedText;
                }
                else if (hasPending)
                {
                    foreach (string chunk in chunker.Flush())
                    {
                        await RenderAsync(chunk, token).ConfigureAwait(false);
                    }

                    hasPending = false;
                }

                AdaptSpeed();
            }

            if (!token.IsCancellationRequested)
            {
                foreach (string chunk in chunker.Flush())
                {
                    await RenderAsync(chunk, token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch (Exception error)
        {
            Error?.Invoke(error.Message);
        }
    }

    /// <summary>
    /// Waits until every queued chunk of the turn has actually been played.
    /// There is deliberately no timeout: a long answer must be spoken to the
    /// end before the turn is reported as completed.
    /// </summary>
    private async Task WaitForAudioDrainAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && BufferedMilliseconds > 0)
        {
            await Task.Delay(20, token).ConfigureAwait(false);
        }

        // The provider is empty, but WaveOut may still be playing the last
        // buffers it already pulled. Wait for that latency too so the turn is
        // only reported complete after the final sample has been heard.
        int deviceLatencyMilliseconds = output == null
            ? 0
            : Math.Max(0, output.BufferMilliseconds * Math.Max(1, output.NumberOfBuffers));
        if (deviceLatencyMilliseconds > 0 && !token.IsCancellationRequested)
        {
            await Task.Delay(deviceLatencyMilliseconds, token).ConfigureAwait(false);
        }
    }

    private void AdaptSpeed()
    {
        if (!adaptiveSpeed)
        {
            speedFactor = 1.0;
            return;
        }

        double buffered = BufferedMilliseconds;
        if (buffered < minBufferMilliseconds)
        {
            speedFactor = Math.Max(minSpeedFactor, speedFactor - 0.05);
        }
        else if (buffered > minBufferMilliseconds * 2.0)
        {
            speedFactor = Math.Min(1.0, speedFactor + 0.05);
        }
    }

    private async Task RenderAsync(string text, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(text) || token.IsCancellationRequested)
        {
            return;
        }

        VocalSynthSettings settings = settingsFactory();
        settings.Text = text;
        settings.SingingMidiFile = null;
        settings.IgnoreTextPausesForMidiSinging = false;
        if (adaptiveSpeed)
        {
            settings.Speed = Math.Clamp(configuredSpeed * speedFactor, 0.5, 4.0);
        }

        AudioObj rendered;
        try
        {
            rendered = await Task.Run(() => VocalSynth.Render(settings, token), token).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            // A chunk may contain only symbols without a pronounceable value
            // (for example "..." or "—"). Skip that chunk instead of stopping
            // the whole stream.
            return;
        }

        float[] data = rendered.Data;
        if (data.Length == 0)
        {
            return;
        }

        if (rendered.SampleRate != buffer.WaveFormat.SampleRate || rendered.Channels != buffer.WaveFormat.Channels)
        {
            Error?.Invoke($"Stream chunk has a different audio format ({rendered.SampleRate} Hz, {rendered.Channels} ch).");
            return;
        }

        byte[] pcm = new byte[data.Length * sizeof(float)];
        Buffer.BlockCopy(data, 0, pcm, 0, pcm.Length);
        await AddSamplesAsync(pcm, token).ConfigureAwait(false);
        if (token.IsCancellationRequested)
        {
            return;
        }

        ChunkRendered?.Invoke(text, data.Length / (double)(buffer.WaveFormat.SampleRate * buffer.WaveFormat.Channels));
    }

    /// <summary>
    /// Queues the rendered chunk in full. The growable buffer never throws
    /// "Buffer full"; only the 1 GiB soft limit can delay further rendering,
    /// and that delay waits for playback instead of dropping any audio.
    /// </summary>
    private async Task AddSamplesAsync(byte[] pcm, CancellationToken token)
    {
        while (!buffer.HasRoomFor(pcm.Length) && !token.IsCancellationRequested)
        {
            await Task.Delay(20, token).ConfigureAwait(false);
        }

        if (!token.IsCancellationRequested)
        {
            buffer.AddOwnedSamples(pcm);
        }
    }
}
