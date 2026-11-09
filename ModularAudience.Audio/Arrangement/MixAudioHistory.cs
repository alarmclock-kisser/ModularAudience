using System.Text.Json;

namespace ModularAudience.Audio.Arrangement;

/// <summary>Audio-only deltas. Applying history never restores navigation, view, snap or project preferences.</summary>
public static class MixAudioHistory
{
    public sealed class State
    {
        public double Bpm { get; set; }
        public List<MixLane> Lanes { get; set; } = [];
    }
    public sealed record Entry(State Before, State After, bool RestoreTempo);

    public static string Capture(MixArrangement mix) => JsonSerializer.Serialize(new State { Bpm = mix.Bpm, Lanes = mix.Lanes });
    public static string Key(MixArrangement mix) => JsonSerializer.Serialize(mix.Lanes.Where(lane => lane.Clips.Count > 0).OrderBy(lane => lane.Id).Select(lane => new
    {
        lane.Id, lane.Gain, lane.Mute, lane.Solo, lane.AutoCrossfade,
        Clips = lane.Clips.OrderBy(clip => clip.Id).Select(clip => new
        {
            clip.Id, clip.SourceId, clip.Start, clip.SourceOffset, clip.SourceDuration, clip.Gain, clip.FadeIn, clip.FadeOut,
            Rate = mix.Rate(clip), PreservePitch = clip.TempoMode == ClipTempoMode.TimeStretch && Math.Abs(mix.Rate(clip) - 1) > 0.00001,
            RenderedEnvelope = lane.AutoCrossfade && clip.HasRenderedEnvelope
        })
    }));

    public static string Create(string before, string after)
    {
        State a = Read(before), b = Read(after);
        return JsonSerializer.Serialize(new Entry(a, b, a.Bpm != b.Bpm));
    }
    public static void Apply(MixArrangement mix, string entryJson, bool undo)
    {
        Entry entry = JsonSerializer.Deserialize<Entry>(entryJson) ?? throw new InvalidDataException("Invalid audio history.");
        State from = undo ? entry.After : entry.Before, to = undo ? entry.Before : entry.After;
        var fromLanes = from.Lanes.ToDictionary(lane => lane.Id); var toLanes = to.Lanes.ToDictionary(lane => lane.Id);
        foreach (MixLane removed in from.Lanes.Where(lane => !toLanes.ContainsKey(lane.Id))) mix.Lanes.RemoveAll(lane => lane.Id == removed.Id);
        foreach (MixLane added in to.Lanes.Where(lane => !fromLanes.ContainsKey(lane.Id)))
            if (!mix.Lanes.Any(lane => lane.Id == added.Id)) mix.Lanes.Insert(Math.Min(to.Lanes.IndexOf(added), mix.Lanes.Count), Clone(added));

        var fromClips = from.Lanes.SelectMany(lane => lane.Clips.Select(clip => (Lane: lane.Id, Clip: clip))).ToDictionary(part => part.Clip.Id);
        var toClips = to.Lanes.SelectMany(lane => lane.Clips.Select(clip => (Lane: lane.Id, Clip: clip))).ToDictionary(part => part.Clip.Id);
        foreach (Guid id in fromClips.Keys.Except(toClips.Keys))
            foreach (MixLane lane in mix.Lanes) lane.Clips.RemoveAll(clip => clip.Id == id);
        foreach (var (id, part) in toClips)
        {
            MixLane? lane = mix.Lanes.FirstOrDefault(lane => lane.Id == part.Lane); if (lane is null) continue;
            MixClip? live = mix.Lanes.SelectMany(value => value.Clips).FirstOrDefault(clip => clip.Id == id);
            if (!fromClips.TryGetValue(id, out var old))
            { if (live is null) lane.Clips.Add(Clone(part.Clip)); continue; }
            if (live is null) { lane.Clips.Add(Clone(part.Clip)); continue; }
            if (old.Lane != part.Lane) { foreach (MixLane value in mix.Lanes) value.Clips.Remove(live); lane.Clips.Add(live); }
            SetClipDelta(live, old.Clip, part.Clip);
        }
        foreach (var (id, value) in toLanes)
        {
            if (!fromLanes.TryGetValue(id, out MixLane? old)) continue;
            MixLane? live = mix.Lanes.FirstOrDefault(lane => lane.Id == id); if (live is null) continue;
            if (old.Gain != value.Gain) live.Gain = value.Gain;
            if (old.Mute != value.Mute) live.Mute = value.Mute;
            if (old.Solo != value.Solo) live.Solo = value.Solo;
            if (old.AutoCrossfade != value.AutoCrossfade) live.AutoCrossfade = value.AutoCrossfade;
        }
        if (entry.RestoreTempo) mix.Bpm = to.Bpm;
    }
    private static void SetClipDelta(MixClip live, MixClip before, MixClip after)
    {
        if (before.SourceId != after.SourceId) live.SourceId = after.SourceId;
        if (before.Start != after.Start) live.Start = after.Start;
        if (before.SourceOffset != after.SourceOffset) live.SourceOffset = after.SourceOffset;
        if (before.SourceDuration != after.SourceDuration) live.SourceDuration = after.SourceDuration;
        if (before.Gain != after.Gain) live.Gain = after.Gain;
        if (before.FadeIn != after.FadeIn) live.FadeIn = after.FadeIn;
        if (before.FadeOut != after.FadeOut) live.FadeOut = after.FadeOut;
        if (before.TempoMode != after.TempoMode) live.TempoMode = after.TempoMode;
        if (before.HasRenderedEnvelope != after.HasRenderedEnvelope) live.HasRenderedEnvelope = after.HasRenderedEnvelope;
    }
    private static State Read(string state) => JsonSerializer.Deserialize<State>(state) ?? throw new InvalidDataException("Invalid audio snapshot.");
    private static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
}
