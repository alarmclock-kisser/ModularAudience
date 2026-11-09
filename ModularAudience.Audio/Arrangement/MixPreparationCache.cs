using System.Text.Json;

namespace ModularAudience.Audio.Arrangement;

/// <summary>Retains pre-rendered unity-gain lanes. Mixer controls never re-run DSP.</summary>
public sealed class MixPreparationCache : IDisposable
{
    private sealed record PreparedLane(string Key, float[] Data);
    private readonly object gate = new();
    private readonly Dictionary<Guid, PreparedLane> lanes = [];
    private bool disposed;
    public long LaneRenders { get; private set; }
    public static string AudioKey(MixArrangement mix) => JsonSerializer.Serialize(new
    {
        Lanes = mix.Lanes.OrderBy(l => l.Id).Select(l => new { l.Id, l.Gain, l.Mute, l.Solo, l.AutoCrossfade,
            Clips = l.Clips.OrderBy(c => c.Id).Select(c => new { c.Id, c.SourceId, c.Start, c.SourceOffset, c.SourceDuration,
                c.Gain, c.FadeIn, c.FadeOut, c.TempoMode, Rate = mix.Rate(c), c.HasRenderedEnvelope }) })
    });
    private static string LaneKey(MixArrangement mix, MixLane lane) => JsonSerializer.Serialize(new
    {
        lane.AutoCrossfade,
        Clips = lane.Clips.OrderBy(c => c.Id).Select(c => new { c.Id, c.SourceId, c.Start, c.SourceOffset, c.SourceDuration,
            c.Gain, c.FadeIn, c.FadeOut, c.TempoMode, Rate = mix.Rate(c), c.HasRenderedEnvelope,
            Source = new { mix.Sources[c.SourceId].SampleRate, mix.Sources[c.SourceId].Channels } })
    });
    public async Task PrepareAsync(MixArrangement mix, CancellationToken token = default)
    {
        var snapshot = new MixArrangement(); snapshot.Restore(mix.Capture());
        foreach (var source in mix.Sources) snapshot.Sources.Add(source.Key, source.Value);
        foreach (var lane in snapshot.Lanes)
        {
            token.ThrowIfCancellationRequested();
            string key = LaneKey(snapshot, lane);
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (lanes.TryGetValue(lane.Id, out var cached) && cached.Key == key) continue;
            }
            var one = new MixArrangement { Bpm = snapshot.Bpm, Name = snapshot.Name };
            foreach (var source in snapshot.Sources) one.Sources.Add(source.Key, source.Value);
            one.Lanes.Add(new MixLane { Id = lane.Id, Name = lane.Name, AutoCrossfade = lane.AutoCrossfade, Clips = lane.Clips });
            using var audio = await MixRenderer.RenderAsync(one, token, normalizeOutput: false).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            lock (gate)
            {
                if (disposed) return;
                lanes[lane.Id] = new(key, audio.Data); LaneRenders++;
            }
        }
        lock (gate)
            foreach (var id in lanes.Keys.Where(id => !snapshot.Lanes.Any(l => l.Id == id)).ToArray()) lanes.Remove(id);
    }
    public bool CanMix(MixArrangement mix)
    {
        lock (gate) return !disposed && mix.Lanes.All(l => lanes.TryGetValue(l.Id, out var cached) && cached.Key == LaneKey(mix, l));
    }
    public float[] Mix(MixArrangement mix, float[]? reuse = null, CancellationToken token = default)
    {
        const int channels = 2, sampleRate = 44100;
        long length = (long)Math.Ceiling(Math.Max(0.01, mix.Duration) * sampleRate) * channels;
        if (length > int.MaxValue) throw new InvalidOperationException("The mix exceeds the RAM audio buffer limit.");
        var output = reuse?.Length == length ? reuse : new float[(int)length];
        (float[] Data, double Gain)[] inputs;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            bool solo = mix.Lanes.Any(l => l.Solo);
            inputs = mix.Lanes.Where(l => !l.Mute && (!solo || l.Solo))
                .Select(l => lanes.TryGetValue(l.Id, out var cached) ? (cached.Data, l.Gain)
                    : throw new InvalidOperationException("The arrangement is not prepared yet.")).ToArray();
        }
        // Disjoint sample ranges: write each sample once, without clearing the playing
        // buffer between sums. This makes slider changes audible without restarting it.
        Parallel.For(0, (output.Length + 4095) / 4096,
            new ParallelOptions { CancellationToken = token, MaxDegreeOfParallelism = Environment.ProcessorCount }, block =>
            {
                int begin = block * 4096, end = Math.Min(output.Length, begin + 4096);
                for (int i = begin; i < end; i++)
                {
                    double sum = 0;
                    foreach (var input in inputs) if (i < input.Data.Length) sum += input.Data[i] * input.Gain;
                    output[i] = (float)Math.Clamp(sum, -0.98, 0.98);
                }
            });
        return output;
    }
    public void Dispose() { lock (gate) { disposed = true; lanes.Clear(); } }
}
