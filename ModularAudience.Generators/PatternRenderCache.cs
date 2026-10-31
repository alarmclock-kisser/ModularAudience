namespace ModularAudience.Generators;

/// <summary>Editor-owned bounded cache of immutable transformed sample buffers, never playback objects.</summary>
public sealed class PatternRenderCache : IDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<string, (float[] Data, int Channels, long Used)> entries = [];
    private long clock, bytes;
    private bool disposed;
    public long MaximumBytes { get; }
    public int Count { get { lock (gate) return entries.Count; } }
    public long Hits { get; private set; }
    public PatternRenderCache(long maximumBytes = 1024L * 1024 * 1024) => MaximumBytes = Math.Max(0, maximumBytes);
    internal bool TryGet(string key, out (float[] Data, int Channels) value)
    {
        lock (gate)
        {
            if (!disposed && entries.TryGetValue(key, out var entry))
            {
                entries[key] = (entry.Data, entry.Channels, ++clock);
                Hits++; value = (entry.Data, entry.Channels); return true;
            }
            value = default; return false;
        }
    }
    internal void Store(string key, (float[] Data, int Channels) value)
    {
        long size = value.Data.LongLength * sizeof(float);
        lock (gate)
        {
            if (disposed || size > MaximumBytes || entries.ContainsKey(key)) return;
            while (bytes + size > MaximumBytes && entries.Count > 0)
            {
                string oldest = entries.MinBy(p => p.Value.Used).Key;
                bytes -= entries[oldest].Data.LongLength * sizeof(float); entries.Remove(oldest);
            }
            entries[key] = (value.Data, value.Channels, ++clock); bytes += size;
        }
    }
    public void Dispose() { lock (gate) { disposed = true; entries.Clear(); bytes = 0; } }
}
