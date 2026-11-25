using System.Threading;
using NAudio.Wave;

namespace ModularAudience.Forms.Synthesis;

/// <summary>
/// Growable audio queue for the live LLM speaker. Unlike
/// <see cref="BufferedWaveProvider"/> it does not reserve a fixed circular
/// buffer, so a long answer can be rendered into memory in full and played
/// back afterwards without ever throwing "Buffer full" or dropping audio.
/// </summary>
public sealed class StreamingWaveBuffer : IWaveProvider
{
    /// <summary>Default upper limit: 1 GiB. Memory is only used as audio arrives.</summary>
    public const long DefaultMaxBufferedBytes = 1L * 1024 * 1024 * 1024;

    private readonly object gate = new();
    private readonly Queue<byte[]> chunks = new();
    private readonly WaveFormat waveFormat;
    private int currentOffset;
    private long bufferedBytes;

    public StreamingWaveBuffer(WaveFormat waveFormat, long maxBufferedBytes = DefaultMaxBufferedBytes)
    {
        this.waveFormat = waveFormat ?? throw new ArgumentNullException(nameof(waveFormat));
        MaxBufferedBytes = Math.Max(1, maxBufferedBytes);
        ReadFully = true;
    }

    /// <summary>When true, underruns are padded with silence so playback never stops.</summary>
    public bool ReadFully { get; set; }

    /// <summary>Soft limit. The speaker waits for playback instead of dropping audio.</summary>
    public long MaxBufferedBytes { get; set; }

    public WaveFormat WaveFormat => waveFormat;

    public long BufferedBytes => Interlocked.Read(ref bufferedBytes);

    public TimeSpan BufferedDuration =>
        TimeSpan.FromSeconds((double)BufferedBytes / waveFormat.AverageBytesPerSecond);

    public bool IsEmpty => BufferedBytes == 0;

    public bool HasRoomFor(long bytes) => MaxBufferedBytes - BufferedBytes >= bytes;

    /// <summary>Takes ownership of the byte array; it must not be modified afterwards.</summary>
    public void AddOwnedSamples(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0)
        {
            return;
        }

        lock (gate)
        {
            chunks.Enqueue(data);
            Interlocked.Add(ref bufferedBytes, data.Length);
        }
    }

    public void ClearBuffer()
    {
        lock (gate)
        {
            chunks.Clear();
            currentOffset = 0;
            Interlocked.Exchange(ref bufferedBytes, 0);
        }
    }

    public int Read(Span<byte> buffer)
    {
        int written = ReadQueued(buffer);
        if (ReadFully && written < buffer.Length)
        {
            buffer[written..].Clear();
            written = buffer.Length;
        }

        return written;
    }

    private int ReadQueued(Span<byte> destination)
    {
        int written = 0;
        lock (gate)
        {
            while (written < destination.Length && chunks.Count > 0)
            {
                byte[] chunk = chunks.Peek();
                int available = chunk.Length - currentOffset;
                if (available <= 0)
                {
                    chunks.Dequeue();
                    currentOffset = 0;
                    continue;
                }

                int take = Math.Min(available, destination.Length - written);
                chunk.AsSpan(currentOffset, take).CopyTo(destination[written..]);
                currentOffset += take;
                Interlocked.Add(ref bufferedBytes, -take);
                written += take;

                if (currentOffset >= chunk.Length)
                {
                    chunks.Dequeue();
                    currentOffset = 0;
                }
            }
        }

        return written;
    }
}
