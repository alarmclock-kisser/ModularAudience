using System.Text.Json;
using ModularAudience.Audio.Effects;
using ModularAudience.Audio.Effects.AutoTune;
using ModularAudience.Audio.Effects.Curves;

namespace ModularAudience.Audio.Arrangement;

/// <summary>
/// A rendered effect/envelope asset retains an immutable recipe, not just its
/// stretched PCM. A later grid change can replay it from pristine dependencies.
/// </summary>
public sealed class MixTempoRecipe
{
    public double Bpm { get; set; }
    public MixLane Lane { get; set; } = new();
    public double Start { get; set; }
    public double End { get; set; }
    public Guid[] ClipIds { get; set; } = [];
    public Guid? FrozenClipId { get; set; }
    public AudioEffectSettings? Effect { get; set; }
    public AutoTuneSettings? AutoTune { get; set; }
    public EffectCurveRenderScope? CurveScope { get; set; }

    internal static MixTempoRecipe Create(MixArrangement snapshot, MixLane lane, double start, double end, IEnumerable<Guid> ids,
        Guid? frozenClip = null, AudioEffectSettings? effect = null, AutoTuneSettings? autoTune = null, EffectCurveRenderScope? curveScope = null) => new()
    {
        Bpm = snapshot.Bpm, Lane = JsonSerializer.Deserialize<MixLane>(JsonSerializer.Serialize(lane))!, Start = start, End = end,
        ClipIds = ids.ToArray(), FrozenClipId = frozenClip, Effect = effect?.Copy(), AutoTune = autoTune?.Copy(), CurveScope = curveScope?.Copy()
    };

    internal static IEnumerable<Guid> Dependencies(MixSource source) =>
        (source.TempoOriginId is Guid origin ? new[] { origin } : [])
        .Concat(source.TempoRecipe?.Lane.Clips.Select(clip => clip.SourceId) ?? []).Distinct();

    internal void Validate(IReadOnlyDictionary<Guid, MixSource> sources)
    {
        if (!double.IsFinite(this.Bpm) || this.Bpm is < 10 or > 400 || !double.IsFinite(this.Start) || !double.IsFinite(this.End) || this.End <= this.Start ||
            this.Lane is null || this.ClipIds is null || this.Lane.Clips.Any(clip => !sources.ContainsKey(clip.SourceId)) ||
            this.ClipIds.Any(id => !this.Lane.Clips.Any(clip => clip.Id == id)) ||
            this.FrozenClipId is Guid frozen && !this.Lane.Clips.Any(clip => clip.Id == frozen) ||
            (this.FrozenClipId is null && (this.Effect is null) == (this.AutoTune is null)))
            throw new InvalidDataException("Invalid tempo rendering recipe.");
        this.Effect?.Validate(); this.AutoTune?.Validate(MixEffectEditSession.SampleRate);
        if (this.CurveScope is not null)
        {
            int frames = checked((int)Math.Round((this.End - this.Start) * MixEffectEditSession.SampleRate));
            EffectCurveClock.Normalize(this.CurveScope.Segments, frames);
            if (this.CurveScope.SelectionSegments is not null) EffectCurveClock.Normalize(this.CurveScope.SelectionSegments, frames);
            if (this.CurveScope.AudibleSegments is not null) EffectCurveClock.Normalize(this.CurveScope.AudibleSegments, frames);
        }
    }
}
