using ModularAudience.Audio.Processing;
using ModularAudience.Audio.Effects;
using ModularAudience.Audio.Effects.AutoTune;
using ModularAudience.Audio.Effects.Curves;
using System.Text.Json;

namespace ModularAudience.Audio.Arrangement;

/// <summary>Tempo renders always start at a stable original/edit asset, never at the previous tempo render.</summary>
public static class MixTempoOrigins
{
    public static (MixSource Origin, double Scale) Resolve(MixArrangement mix, MixSource source)
    {
        double scale = 1; var visited = new HashSet<Guid>();
        while (source.TempoOriginId is Guid id)
        {
            if (!visited.Add(source.Id) || !mix.Sources.TryGetValue(id, out MixSource? origin)) throw new InvalidDataException("Missing or cyclic tempo origin.");
            scale *= source.TempoOriginScale; source = origin;
        }
        return (source, scale);
    }

    public static Task<MixSource> PrepareAsync(MixArrangement mix, MixSource source, double targetBpm, double fallbackBpm,
        ClipTempoMode mode, PlaylistStretchSettings? settings = null, CancellationToken token = default) =>
        Task.Run(() => PrepareCoreAsync(mix, source, targetBpm, fallbackBpm, mode, settings, token, []), token);

    private static async Task<MixSource> PrepareCoreAsync(MixArrangement mix, MixSource source, double targetBpm, double fallbackBpm,
        ClipTempoMode mode, PlaylistStretchSettings? settings, CancellationToken token, HashSet<Guid> path)
    {
        token.ThrowIfCancellationRequested();
        if (!double.IsFinite(targetBpm) || targetBpm is < 10 or > 400) throw new ArgumentOutOfRangeException(nameof(targetBpm));
        var (origin, _) = Resolve(mix, source);
        if (!path.Add(origin.Id)) throw new InvalidDataException("Cyclic audio rendering recipes.");
        try
        {
            if (origin.TempoRecipe is MixTempoRecipe recipe)
            {
                AudioEffectResult result = Math.Abs(recipe.Bpm - targetBpm) < 1e-9
                    ? new AudioEffectResult((float[])origin.Data.Clone(), origin.Channels)
                    : await RenderRecipeAsync(mix, recipe, targetBpm, mode, settings, token, path).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                double duration = result.Samples.Length / (double)result.Channels / MixEffectEditSession.SampleRate;
                return new MixSource { Name = source.Name, Data = result.Samples, SampleRate = MixEffectEditSession.SampleRate, Channels = result.Channels,
                    Bpm = (float)(origin.Bpm * targetBpm / recipe.Bpm), TempoOriginId = origin.Id,
                    TempoOriginScale = duration > 0 ? origin.Duration / duration : 1, TempoOriginBpm = (float)recipe.Bpm };
            }
            double baseBpm = float.IsFinite(origin.Bpm) && origin.Bpm > 0 ? origin.Bpm :
                float.IsFinite(source.TempoOriginBpm) && source.TempoOriginBpm > 0 ? source.TempoOriginBpm : fallbackBpm;
            if (!double.IsFinite(baseBpm) || baseBpm <= 0) throw new ArgumentException("The original audio needs a valid base tempo.");
            var input = new MixSource { Id = origin.Id, Name = origin.Name, Data = origin.Data, SampleRate = origin.SampleRate,
                Channels = origin.Channels, Bpm = (float)baseBpm };
            MixSource rendered = Math.Abs(baseBpm - targetBpm) < 1e-9 ? input :
                await MixRenderer.PrepareSourceAsync(input, targetBpm, mode, token, settings).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return new MixSource { Name = source.Name, Data = rendered.Data, SampleRate = rendered.SampleRate, Channels = rendered.Channels,
                Bpm = (float)targetBpm, TempoOriginId = origin.Id, TempoOriginScale = targetBpm / baseBpm, TempoOriginBpm = (float)baseBpm };
        }
        finally { path.Remove(origin.Id); }
    }

    private static async Task<AudioEffectResult> RenderRecipeAsync(MixArrangement mix, MixTempoRecipe recipe, double targetBpm,
        ClipTempoMode mode, PlaylistStretchSettings? settings, CancellationToken token, HashSet<Guid> path)
    {
        recipe.Validate(mix.Sources); token.ThrowIfCancellationRequested();
        using var snapshot = new MixArrangement { Bpm = targetBpm };
        foreach (var pair in mix.Sources) snapshot.Sources.Add(pair.Key, pair.Value);
        MixLane lane = JsonSerializer.Deserialize<MixLane>(JsonSerializer.Serialize(recipe.Lane))!;
        snapshot.Lanes.Add(lane);
        var replacements = new Dictionary<Guid, MixSource>();
        foreach (Guid id in lane.Clips.Select(clip => clip.SourceId).Distinct())
        {
            token.ThrowIfCancellationRequested();
            MixSource fresh = await PrepareCoreAsync(mix, mix.Sources[id], targetBpm, recipe.Bpm, mode, settings, token, path).ConfigureAwait(false);
            replacements[id] = fresh; snapshot.Sources.TryAdd(fresh.Id, fresh);
        }
        double timelineScale = recipe.Bpm / targetBpm;
        foreach (MixClip clip in lane.Clips)
        {
            MixSource previous = mix.Sources[clip.SourceId], replacement = replacements[clip.SourceId];
            double coordinateScale = Resolve(mix, previous).Scale / replacement.TempoOriginScale;
            clip.SourceOffset = Math.Min(clip.SourceOffset * coordinateScale, replacement.Duration);
            clip.SourceDuration = Math.Min(clip.SourceDuration * coordinateScale, Math.Max(0, replacement.Duration - clip.SourceOffset));
            clip.Start *= timelineScale; clip.FadeIn *= timelineScale; clip.FadeOut *= timelineScale;
            clip.SourceId = replacement.Id; clip.TempoMode = ClipTempoMode.Original;
        }
        if (recipe.FrozenClipId is Guid frozen)
        {
            MixClip clip = lane.Clips.Single(value => value.Id == frozen);
            float[] data = await MixRenderer.RenderClipForEffectAsync(snapshot, lane, clip, token).ConfigureAwait(false);
            return new(data, snapshot.Sources[clip.SourceId].Channels);
        }
        double start = recipe.Start * timelineScale, end = recipe.End * timelineScale;
        // A whole-segment bounce follows its regenerated clip bounds. Explicit
        // time-range/gap edges remain anchored to the mix grid instead.
        MixClip[] originalSelected = recipe.Lane.Clips.Where(clip => recipe.ClipIds.Contains(clip.Id)).ToArray();
        MixClip[] selected = lane.Clips.Where(clip => recipe.ClipIds.Contains(clip.Id)).ToArray();
        if (selected.Length > 0)
        {
            if (Math.Abs(recipe.Start - originalSelected.Min(clip => clip.Start)) < 0.5 / MixEffectEditSession.SampleRate) start = selected.Min(clip => clip.Start);
            double originalEnd = originalSelected.Max(clip => clip.Start + ClipDuration(mix, recipe.Bpm, clip));
            if (Math.Abs(recipe.End - originalEnd) < 0.5 / MixEffectEditSession.SampleRate) end = selected.Max(clip => clip.Start + snapshot.ClipDuration(clip));
        }
        if (end <= start) end = start + 1d / MixEffectEditSession.SampleRate;
        using MixEffectEditSession session = await MixEffectEditSession.CaptureAsync(snapshot,
            [new MixEffectRegion(lane.Id, start, end, recipe.ClipIds.ToHashSet())], token).ConfigureAwait(false);
        MixEffectInput input = session.Inputs.Single();
        var execution = new AudioEffectExecutionOptions { MaximumThreads = Math.Clamp(settings?.Threads ?? AudioEffectExecutionOptions.DefaultMaximumThreads, 1, Environment.ProcessorCount) };
        var automation = recipe.Effect?.Automation ?? recipe.AutoTune?.Automation;
        if (automation?.HasEnabledCurves == true)
        {
            if (recipe.CurveScope is null) execution = execution with { CurveScope = session.CreateCurveScope(0, automation.Timing, token) };
            else
            {
                int frames = input.Samples.Length / input.Channels;
                double scale = frames / Math.Max(1, Math.Round((recipe.End - recipe.Start) * MixEffectEditSession.SampleRate));
                EffectCurveSpan[] Resize(IReadOnlyList<EffectCurveSpan> spans) => spans
                    .Select(span => new EffectCurveSpan(Math.Clamp((int)Math.Round(span.Start * scale), 0, frames), Math.Clamp((int)Math.Round(span.End * scale), 0, frames)))
                    .Where(span => span.End > span.Start).ToArray();
                execution = execution with { CurveScope = new(Resize(recipe.CurveScope.Segments),
                    recipe.CurveScope.SelectionSegments is null ? null : Resize(recipe.CurveScope.SelectionSegments),
                    recipe.CurveScope.AudibleSegments is null ? null : Resize(recipe.CurveScope.AudibleSegments)) };
            }
        }
        if (recipe.Effect is not null)
            return await AudioEffectProcessor.ProcessAsync(input.Samples, MixEffectEditSession.SampleRate, input.Channels, recipe.Effect,
                cancellationToken: token, execution: execution).ConfigureAwait(false);
        AutoTuneResult tuned = await AutoTuneProcessor.ProcessAsync(input.Samples, MixEffectEditSession.SampleRate, input.Channels,
            recipe.AutoTune! with { TempoBpm = Math.Clamp(targetBpm, 20, 400) }, cancellationToken: token, execution: execution).ConfigureAwait(false);
        return new(tuned.Samples, input.Channels);
    }
    private static double ClipDuration(MixArrangement mix, double bpm, MixClip clip)
    {
        MixSource source = mix.Sources[clip.SourceId];
        double rate = clip.TempoMode != ClipTempoMode.Original && source.Bpm > 0 && float.IsFinite(source.Bpm) ? Math.Clamp(bpm / source.Bpm, 0.05, 20) : 1;
        return clip.SourceDuration / rate;
    }
}
