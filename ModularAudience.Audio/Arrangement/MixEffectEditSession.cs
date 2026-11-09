using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using ModularAudience.Audio.Effects;

namespace ModularAudience.Audio.Arrangement;

public sealed record MixEffectRegion(Guid LaneId, double Start, double End, IReadOnlySet<Guid> ClipIds);
public sealed record MixEffectInput(Guid LaneId, string Name, int Channels, ReadOnlyMemory<float> Samples);
public sealed record MixEffectCommit(bool Changed, double NewDuration, IReadOnlyList<Guid> SelectedClipIds);

/// <summary>
/// Frozen lane-local effect regions, including actual silence between selected
/// clips. Sources stay immutable; commit installs new assets/clip metadata once.
/// MixTrackView owns the single arrangement-level undo step.
/// </summary>
public sealed class MixEffectEditSession : IDisposable
{
    public const int SampleRate = 44100;
    private const int MaximumSamples = 134_217_728;
    private sealed record SourceStamp(MixSource Source, float[] Data, int Rate, int Channels, float Bpm, byte[] Hash);
    private sealed record RenderedClip(MixClip Original, int Channels, float[] Samples);
    private readonly MixArrangement snapshot;
    private readonly string audioKey;
    private readonly MixEffectRegion[] regions;
    private readonly Dictionary<Guid, SourceStamp> stamps;
    private readonly Dictionary<Guid, RenderedClip> renderedClips = [];
    private MixEffectInput[] inputs = [];
    private bool disposed;

    private MixEffectEditSession(MixArrangement snapshot, string audioKey, MixEffectRegion[] regions, Dictionary<Guid, SourceStamp> stamps)
    {
        this.snapshot = snapshot; this.audioKey = audioKey; this.regions = regions; this.stamps = stamps;
        this.Start = regions.Min(region => region.Start); this.End = regions.Max(region => region.End);
    }
    public double Start { get; }
    public double End { get; }
    public double Duration => this.End - this.Start;
    public double TempoBpm => this.snapshot.Bpm;
    public IReadOnlyList<MixEffectInput> Inputs => this.inputs;
    public IReadOnlyList<Guid> LaneIds => this.regions.Select(region => region.LaneId).ToArray();

    public static Task<MixEffectEditSession> CaptureAsync(MixArrangement arrangement, IEnumerable<MixEffectRegion> regions, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(arrangement); ArgumentNullException.ThrowIfNull(regions);
        MixEffectRegion[] targets = regions.Select(region => region with { ClipIds = region.ClipIds.ToHashSet() }).ToArray();
        if (targets.Length == 0 || targets.Select(region => region.LaneId).Distinct().Count() != targets.Length ||
            targets.Any(region => !double.IsFinite(region.Start) || !double.IsFinite(region.End) || region.Start < 0 || region.End <= region.Start ||
                !arrangement.Lanes.Any(lane => lane.Id == region.LaneId))) throw new ArgumentException("Select one or more valid audio regions.");
        double start = Math.Round(targets.Min(region => region.Start) * SampleRate) / SampleRate;
        double end = Math.Round(targets.Max(region => region.End) * SampleRate) / SampleRate;
        if (end <= start) throw new ArgumentException("The selection must contain at least one audio frame.");
        targets = targets.Select(region => region with { Start = start, End = end }).ToArray();
        string key = MixPreparationCache.AudioKey(arrangement);
        var snapshot = new MixArrangement(); snapshot.Restore(arrangement.Capture());
        foreach (var pair in arrangement.Sources)
            snapshot.Sources.Add(pair.Key, new MixSource { Id = pair.Key, Name = pair.Value.Name, Data = pair.Value.Data,
                SampleRate = pair.Value.SampleRate, Channels = pair.Value.Channels, Bpm = pair.Value.Bpm });
        var capturedSources = arrangement.Sources.ToDictionary(pair => pair.Key, pair => pair.Value);
        return Task.Run(async () =>
        {
            token.ThrowIfCancellationRequested(); snapshot.Validate();
            var usedIds = snapshot.Lanes.Where(lane => targets.Any(region => region.LaneId == lane.Id))
                .SelectMany(lane => lane.Clips).Select(clip => clip.SourceId).Distinct().ToArray();
            var stamps = usedIds.ToDictionary(id => id, id =>
            {
                token.ThrowIfCancellationRequested(); MixSource source = snapshot.Sources[id];
                return new SourceStamp(capturedSources[id], source.Data, source.SampleRate, source.Channels, source.Bpm, Hash(source.Data));
            });
            var session = new MixEffectEditSession(snapshot, key, targets, stamps);
            try { await session.RenderInputsAsync(token).ConfigureAwait(false); return session; }
            catch { session.Dispose(); throw; }
        }, token);
    }

    private async Task RenderInputsAsync(CancellationToken token)
    {
        var renderedInputs = new List<MixEffectInput>(); long allocated = 0;
        double requestedFrames = Math.Round(this.Duration * SampleRate);
        if (requestedFrames < 1 || requestedFrames > MaximumSamples / 2) throw new InvalidOperationException("The selected interval exceeds the effect preview buffer limit.");
        int frameCount = (int)requestedFrames;
        foreach (MixEffectRegion region in this.regions)
        {
            token.ThrowIfCancellationRequested(); MixLane lane = this.snapshot.Lanes.Single(lane => lane.Id == region.LaneId);
            if (region.ClipIds.Any(id => !lane.Clips.Any(clip => clip.Id == id))) throw new ArgumentException("A selected segment no longer exists.");
            int channels = lane.Clips.Where(clip => region.ClipIds.Contains(clip.Id))
                .Select(clip => this.snapshot.Sources[clip.SourceId].Channels).DefaultIfEmpty(2).Max();
            long samples = (long)frameCount * channels;
            if (samples > MaximumSamples || allocated + samples > MaximumSamples) throw new InvalidOperationException("The selected lane buffers exceed 512 MiB. Select a smaller region.");
            float[] input = new float[(int)samples]; allocated += samples;
            foreach (MixClip clip in lane.Clips.Where(clip => lane.AutoCrossfade || region.ClipIds.Contains(clip.Id)))
            {
                token.ThrowIfCancellationRequested();
                int sourceChannels = this.snapshot.Sources[clip.SourceId].Channels;
                double expected = Math.Ceiling(this.snapshot.ClipDuration(clip) * SampleRate) * sourceChannels;
                if (!double.IsFinite(expected) || expected > MaximumSamples || allocated + expected > MaximumSamples)
                    throw new InvalidOperationException("Rendering the selected clips/envelopes exceeds 512 MiB. Trim or split very long clips first.");
                float[] data = await MixRenderer.RenderClipForEffectAsync(this.snapshot, lane, clip, token).ConfigureAwait(false);
                this.renderedClips.Add(clip.Id, new(clip, sourceChannels, data)); allocated += data.Length;
                if (!region.ClipIds.Contains(clip.Id)) continue;
                int offset = checked((int)Math.Round((clip.Start - this.Start) * SampleRate));
                int first = Math.Max(0, -offset), last = Math.Min(data.Length / sourceChannels, frameCount - offset);
                for (int frame = first; frame < last; frame++)
                {
                    if ((frame & 8191) == 0) token.ThrowIfCancellationRequested();
                    for (int channel = 0; channel < channels; channel++)
                    {
                        // Preserve the widest selected layout; mono duplicates and
                        // missing channels in a smaller non-mono layout stay silent.
                        int from = sourceChannels == 1 ? 0 : channel;
                        if (from < sourceChannels) input[(frame + offset) * channels + channel] += data[frame * sourceChannels + from];
                    }
                }
            }
            renderedInputs.Add(new(region.LaneId, lane.Name, channels, input));
        }
        this.inputs = renderedInputs.ToArray(); token.ThrowIfCancellationRequested();
    }

    public bool IsCurrent(MixArrangement target)
    {
        if (this.disposed || MixPreparationCache.AudioKey(target) != this.audioKey) return false;
        return this.stamps.All(pair => target.Sources.TryGetValue(pair.Key, out MixSource? source) &&
            ReferenceEquals(source, pair.Value.Source) && ReferenceEquals(source.Data, pair.Value.Data) && source.SampleRate == pair.Value.Rate &&
            source.Channels == pair.Value.Channels && source.Bpm == pair.Value.Bpm &&
            CryptographicOperations.FixedTimeEquals(pair.Value.Hash, Hash(source.Data)));
    }

    public Task<AudioObj> RenderPreviewAsync(IReadOnlyList<AudioEffectResult>? processed = null, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        if (processed is not null) this.ValidateResults(processed);
        var buffers = this.inputs.Select((input, index) => (
            Samples: processed is null ? input.Samples : processed[index].Samples.AsMemory(),
            Channels: processed is null ? input.Channels : processed[index].Channels,
            Gain: this.snapshot.Lanes.Single(lane => lane.Id == input.LaneId).Gain)).ToArray();
        int longest = Math.Max(1, buffers.Max(buffer => buffer.Samples.Length / buffer.Channels));
        if ((long)longest * 2 > MaximumSamples) throw new InvalidOperationException("The stereo preview exceeds 512 MiB. Select a smaller region.");
        string name = this.snapshot.Name + " — selected audio"; float bpm = (float)this.TempoBpm;
        // All lane buffers are already at the mix clock with clip envelopes
        // rendered. A sequential stereo bus avoids repeating DSP or multiplying
        // the dialog's worker budget merely to audition several selected lanes.
        return Task.Run(() =>
        {
            token.ThrowIfCancellationRequested(); var output = new float[longest * 2];
            foreach (var buffer in buffers)
            {
                ReadOnlySpan<float> samples = buffer.Samples.Span;
                for (int frame = 0; frame < samples.Length / buffer.Channels; frame++)
                {
                    if ((frame & 8191) == 0) token.ThrowIfCancellationRequested();
                    for (int channel = 0; channel < 2; channel++)
                    {
                        double sum = 0; int contributors = 0;
                        for (int sourceChannel = buffer.Channels == 1 ? 0 : channel; sourceChannel < buffer.Channels; sourceChannel += buffer.Channels == 1 ? 1 : 2)
                        { sum += samples[frame * buffer.Channels + sourceChannel]; contributors++; }
                        output[frame * 2 + channel] += (float)(sum / Math.Max(1, contributors) * buffer.Gain);
                    }
                }
            }
            float peak = output.Max(Math.Abs);
            if (peak > 0.98f)
                for (int i = 0; i < output.Length; i++)
                { if ((i & 65535) == 0) token.ThrowIfCancellationRequested(); output[i] *= 0.98f / peak; }
            token.ThrowIfCancellationRequested();
            return new AudioObj { Name = name, Data = output, SampleRate = SampleRate, Channels = 2, Length = output.Length,
                Duration = TimeSpan.FromSeconds(longest / (double)SampleRate), Bpm = bpm, BitDepth = 32 };
        }, token);
    }

    public MixEffectCommit Apply(MixArrangement target, IReadOnlyList<AudioEffectResult> processed, string effectName)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this); this.ValidateResults(processed);
        if (!this.IsCurrent(target)) throw new InvalidOperationException("The mix audio changed while the effect window was open. Reopen the effect for the current selection.");
        bool Different(int index) => processed[index].Channels != this.inputs[index].Channels ||
            !MemoryMarshal.AsBytes(processed[index].Samples.AsSpan()).SequenceEqual(MemoryMarshal.AsBytes(this.inputs[index].Samples.Span));
        if (!Enumerable.Range(0, this.inputs.Length).Any(Different)) return new(false, this.Duration, []);
        int longestFrames = processed.Max(result => result.Samples.Length / result.Channels);
        double newDuration = longestFrames / (double)SampleRate, delta = newDuration - this.Duration;
        var staged = new MixArrangement(); staged.Restore(target.Capture());
        foreach (var pair in target.Sources) staged.Sources.Add(pair.Key, pair.Value);
        var selectedIds = new List<Guid>(); var added = new List<MixSource>();
        for (int index = 0; index < this.regions.Length; index++)
        {
            MixEffectRegion region = this.regions[index]; MixEffectInput input = this.inputs[index]; AudioEffectResult result = processed[index];
            MixLane lane = staged.Lanes.Single(lane => lane.Id == region.LaneId);
            var clips = new List<MixClip>();
            foreach (MixClip clip in lane.Clips)
            {
                bool selected = region.ClipIds.Contains(clip.Id);
                if (!selected)
                {
                    if (lane.AutoCrossfade) clips.Add(this.BakeClip(clip, added, staged));
                    else clips.Add(clip);
                    if (clip.Start >= this.End - 0.5 / SampleRate) clips[^1].Start = Math.Max(this.Start, clips[^1].Start + delta);
                    continue;
                }
                double oldEnd = clip.Start + staged.ClipDuration(clip);
                bool hasLeft = clip.Start < this.Start - 0.5 / SampleRate, hasRight = oldEnd > this.End + 0.5 / SampleRate;
                if (!hasLeft && !hasRight) continue;
                MixClip baked = this.BakeClip(clip, added, staged);
                if (hasLeft)
                {
                    MixClip left = Clone(baked); left.Id = Guid.NewGuid(); left.SourceDuration = Math.Min(baked.SourceDuration, this.Start - clip.Start); clips.Add(left);
                }
                if (hasRight)
                {
                    MixClip right = Clone(baked); right.Id = Guid.NewGuid(); right.Start = this.Start + newDuration;
                    right.SourceOffset = Math.Clamp(this.End - clip.Start, 0, baked.SourceDuration);
                    right.SourceDuration = Math.Max(0, baked.SourceDuration - right.SourceOffset); clips.Add(right);
                }
            }
            var effected = new MixSource { Name = effectName + " — " + input.Name, Data = (float[])result.Samples.Clone(), Channels = result.Channels,
                SampleRate = SampleRate, Bpm = (float)(this.TempoBpm * (result.TempoMultiplier ?? 1)) };
            staged.Sources.Add(effected.Id, effected); added.Add(effected);
            var replacement = new MixClip { Name = effected.Name, SourceId = effected.Id, Start = this.Start, SourceDuration = effected.Duration, HasRenderedEnvelope = true };
            clips.Add(replacement); selectedIds.Add(replacement.Id); lane.Clips = clips.OrderBy(clip => clip.Start).ToList();
        }
        staged.LoopStart = this.MapTime(staged.LoopStart, newDuration); staged.LoopEnd = this.MapTime(staged.LoopEnd, newDuration);
        staged.Validate(); // Build/validate everything before touching the live arrangement.
        foreach (MixSource source in added) target.Sources.Add(source.Id, source);
        target.Lanes = staged.Lanes; target.LoopStart = staged.LoopStart; target.LoopEnd = staged.LoopEnd;
        return new(true, newDuration, selectedIds);
    }

    private MixClip BakeClip(MixClip clip, List<MixSource> added, MixArrangement staged)
    {
        RenderedClip rendered = this.renderedClips[clip.Id];
        var source = new MixSource { Name = clip.Name + " — frozen envelope", Data = rendered.Samples, Channels = rendered.Channels,
            SampleRate = SampleRate, Bpm = (float)this.TempoBpm };
        staged.Sources.Add(source.Id, source); added.Add(source);
        return new MixClip { Id = clip.Id, Name = clip.Name, SourceId = source.Id, Start = clip.Start,
            SourceDuration = source.Duration, HasRenderedEnvelope = true };
    }

    public double MapTime(double time, double newDuration) => time <= this.Start ? time : time >= this.End
        ? Math.Max(this.Start, time + newDuration - this.Duration) : this.Start + (time - this.Start) * newDuration / this.Duration;

    private void ValidateResults(IReadOnlyList<AudioEffectResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        if (results.Count != this.inputs.Length || results.Any(result => result is null || result.Channels is < 1 or > 64 || result.Samples is null ||
            result.Samples.Length == 0 || result.Samples.Length % result.Channels != 0 || result.Samples.Length > MaximumSamples ||
            result.Samples.Any(sample => !float.IsFinite(sample)) || result.TempoMultiplier is double tempo && (!double.IsFinite(tempo) || tempo <= 0)))
            throw new ArgumentException("The effect must return one finite, frame-aligned result per selected lane.");
        if (results.Sum(result => (long)result.Samples.Length) > MaximumSamples) throw new InvalidOperationException("Effect results exceed 512 MiB. Select a smaller region.");
    }
    private static MixClip Clone(MixClip clip) => JsonSerializer.Deserialize<MixClip>(JsonSerializer.Serialize(clip))!;
    private static byte[] Hash(float[] data) => SHA256.HashData(MemoryMarshal.AsBytes(data.AsSpan()));
    public void Dispose() { if (this.disposed) return; this.disposed = true; this.renderedClips.Clear(); this.inputs = []; this.snapshot.Dispose(); }
}
