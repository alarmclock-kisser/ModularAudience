namespace ModularAudience.Audio.Midi;

/// <summary>Window-owned immutable PCM cache shared by MIDI preview and export.</summary>
public sealed class MidiSampleRenderCache : IDisposable
{
    private sealed record Entry(float[] Data, int SampleRate, int Channels, long Used);
    private readonly object gate = new();
    private readonly Dictionary<string, Entry> entries = [];
    private long bytes, clock;
    private bool disposed;
    public long MaximumBytes { get; }
    public long Hits { get; private set; }
    public int Count { get { lock (gate) return entries.Count; } }
    public MidiSampleRenderCache(long maximumBytes = 1024L * 1024 * 1024) => MaximumBytes = Math.Max(0, maximumBytes);

    internal AudioObj GetOrCreate(string key, Func<AudioObj> prepare, CancellationToken token)
    {
        // Never hold the ownership lock while running DSP: closing the window must not
        // block its UI thread behind a long stretch operation.
        lock (gate)
        {
            token.ThrowIfCancellationRequested();
            if (!disposed && entries.TryGetValue(key, out var cached))
            {
                Hits++; entries[key] = cached with { Used = ++clock };
                return Wrap(cached);
            }
        }
        AudioObj result = prepare();
        if (token.IsCancellationRequested) { result.Dispose(); token.ThrowIfCancellationRequested(); }
        long size = result.Data.LongLength * sizeof(float);
        lock (gate)
        {
            if (!disposed && size <= MaximumBytes && !entries.ContainsKey(key))
            {
                while (bytes + size > MaximumBytes && entries.Count > 0)
                {
                    string oldest = entries.MinBy(p => p.Value.Used).Key;
                    bytes -= entries[oldest].Data.LongLength * sizeof(float); entries.Remove(oldest);
                }
                entries.Add(key, new(result.Data, result.SampleRate, result.Channels, ++clock)); bytes += size;
            }
            return result;
        }
    }
    private static AudioObj Wrap(Entry entry) => new()
    {
        Data = entry.Data, SampleRate = entry.SampleRate, Channels = entry.Channels,
        Length = entry.Data.Length, Duration = TimeSpan.FromSeconds(entry.Data.Length / (double)(entry.SampleRate * entry.Channels))
    };
    public void Dispose() { lock (gate) { disposed = true; entries.Clear(); bytes = 0; } }
}
