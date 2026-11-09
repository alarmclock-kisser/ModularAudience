using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Arrangement;
using ModularAudience.Audio.Effects;
using ModularAudience.Audio.Effects.Curves;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class MixEffectCurveTests
{
    [TestMethod]
    public async Task SegmentGeometry_RestartsPerClipAndSharedClockOptionallyCountsGapFrames()
    {
        using var arrangement = new MixArrangement(); MixSource source = Source(arrangement, 100, 0.1f);
        var lane = new MixLane(); arrangement.Lanes.Add(lane);
        MixClip first = arrangement.AddClip(lane, source, 0), second = arrangement.AddClip(lane, source, 300d / 44100);
        using var session = await MixEffectEditSession.CaptureAsync(arrangement, [new(lane.Id, 0, 400d / 44100, new HashSet<Guid> { first.Id, second.Id })]);
        CollectionAssert.AreEqual(new[] { new EffectCurveSpan(0, 100), new EffectCurveSpan(300, 400) }, session.Inputs[0].Segments!.ToArray());
        AudioEffectSettings settings = Gain(new()); AudioEffectResult restart = await Render(session, 0, settings);
        Assert.AreEqual(0.1f, restart.Samples[0], 1e-6f); Assert.AreEqual(1f, restart.Samples[99], 1e-6f);
        Assert.AreEqual(0.1f, restart.Samples[300], 1e-6f); Assert.AreEqual(1f, restart.Samples[399], 1e-6f);
        Assert.IsTrue(restart.Samples.Skip(100).Take(200).All(value => value == 0));
        settings = Gain(new() { Scope = EffectCurveScope.AcrossSelection, IncludeGaps = true });
        AudioEffectResult withGaps = await Render(session, 0, settings);
        Assert.AreEqual(0.1 * Math.Pow(10, 300d / 399), withGaps.Samples[300], 1e-6);
        settings = Gain(new() { Scope = EffectCurveScope.AcrossSelection, IncludeGaps = false });
        AudioEffectResult withoutGaps = await Render(session, 0, settings);
        Assert.AreEqual(0.1 * Math.Pow(10, 100d / 199), withoutGaps.Samples[300], 1e-6);
        Assert.AreEqual(400, withoutGaps.Samples.Length); Assert.IsTrue(session.IsCurrent(arrangement));
    }

    [TestMethod]
    public async Task SimultaneousLanes_ShareTheUnionOfSelectedTime_NotAConcatenationOrPerLaneClock()
    {
        using var arrangement = new MixArrangement(); MixSource source = Source(arrangement, 100, 0.1f);
        MixLane one = new(), two = new(); arrangement.Lanes.AddRange([one, two]);
        MixClip a = arrangement.AddClip(one, source, 0), b = arrangement.AddClip(two, source, 300d / 44100);
        using var session = await MixEffectEditSession.CaptureAsync(arrangement,
            [new(one.Id, 0, 400d / 44100, new HashSet<Guid> { a.Id }), new(two.Id, 0, 400d / 44100, new HashSet<Guid> { b.Id })]);
        var timing = new EffectCurveTiming { Scope = EffectCurveScope.AcrossSelection, IncludeGaps = false, IncludeSilence = false, MinimumSilenceMs = 1 };
        EffectCurveRenderScope firstScope = session.CreateCurveScope(0, timing), secondScope = session.CreateCurveScope(1, timing);
        CollectionAssert.AreEqual(firstScope.SelectionSegments!.ToArray(), secondScope.SelectionSegments!.ToArray());
        CollectionAssert.AreEqual(firstScope.AudibleSegments!.ToArray(), secondScope.AudibleSegments!.ToArray());
        var clockOne = new EffectCurveClock(session.Inputs[0].Samples.Span, 1, 44100, timing, firstScope);
        var clockTwo = new EffectCurveClock(session.Inputs[1].Samples.Span, 1, 44100, timing, secondScope);
        Assert.AreEqual(clockOne.PositionAt(300), clockTwo.PositionAt(300)); Assert.AreEqual(100d / 199, clockTwo.PositionAt(300), 1e-12);
        AudioEffectResult rendered = await Render(session, 1, Gain(timing));
        Assert.AreEqual(0.1 * Math.Pow(10, 100d / 199), rendered.Samples[300], 1e-6);
    }

    [TestMethod]
    public async Task SilenceInsideASelectedSegment_CanPauseCurveTimeWithoutRemovingOrShorteningSamples()
    {
        using var arrangement = new MixArrangement(); MixSource source = Source(arrangement, 400, 0.1f); Array.Clear(source.Data, 100, 200);
        var lane = new MixLane(); arrangement.Lanes.Add(lane); MixClip clip = arrangement.AddClip(lane, source, 0);
        using var session = await MixEffectEditSession.CaptureAsync(arrangement, [new(lane.Id, 0, 400d / 44100, new HashSet<Guid> { clip.Id })]);
        AudioEffectResult result = await Render(session, 0, Gain(new() { IncludeSilence = false, MinimumSilenceMs = 1 }));
        Assert.AreEqual(400, result.Samples.Length); Assert.AreEqual(0.1 * Math.Pow(10, 99d / 199), result.Samples[99], 1e-6);
        Assert.IsTrue(result.Samples.Skip(100).Take(200).All(value => value == 0)); Assert.AreEqual(1f, result.Samples[^1], 1e-6f);
    }

    [TestMethod]
    public async Task OverlappingSegments_AreOneClockRegion_ButAdjacentSegmentsRestartIndependently()
    {
        using var arrangement = new MixArrangement(); MixSource source = Source(arrangement, 100, 0.1f);
        var lane = new MixLane(); arrangement.Lanes.Add(lane);
        MixClip a = arrangement.AddClip(lane, source, 0), b = arrangement.AddClip(lane, source, 50d / 44100), c = arrangement.AddClip(lane, source, 150d / 44100);
        using var session = await MixEffectEditSession.CaptureAsync(arrangement, [new(lane.Id, 0, 250d / 44100, new HashSet<Guid> { a.Id, b.Id, c.Id })]);
        CollectionAssert.AreEqual(new[] { new EffectCurveSpan(0, 150), new EffectCurveSpan(150, 250) }, session.Inputs[0].Segments!.ToArray());
        var clock = new EffectCurveClock(session.Inputs[0].Samples.Span, 1, 44100, new(), session.CreateCurveScope(0, new()));
        Assert.AreEqual(1d, clock.PositionAt(149)); Assert.AreEqual(0d, clock.PositionAt(150));
    }

    [TestMethod]
    [DataRow(AudioEffectKind.TimeStretch, "Factor", 0.5)]
    [DataRow(AudioEffectKind.VarispeedShift, "Speed", 2d)]
    public async Task EmptySelectedLane_UsesTheSelectionAsItsCurveRegionAndDoesNotPinTheBatchDuration(AudioEffectKind effect, string key, double target)
    {
        using var arrangement = new MixArrangement(); MixSource source = Source(arrangement, 4410, 0.1f);
        MixLane audio = new(), empty = new(); arrangement.Lanes.AddRange([audio, empty]); MixClip clip = arrangement.AddClip(audio, source, 0);
        using var session = await MixEffectEditSession.CaptureAsync(arrangement,
            [new(audio.Id, 0, 0.1, new HashSet<Guid> { clip.Id }), new(empty.Id, 0, 0.1, new HashSet<Guid>())]);
        var settings = AudioEffectCatalog.Get(effect).CreateDefaults().With((key, target)) with
            { Automation = new() { Parameters = new() { [key] = new() { StartValue = 1 } } } };
        AudioEffectResult first = await Render(session, 0, settings), second = await Render(session, 1, settings);
        Assert.AreEqual(first.Samples.Length / first.Channels, second.Samples.Length / second.Channels);
        Assert.IsTrue(second.Samples.All(value => value == 0));
        MixEffectCommit edit = session.Apply(arrangement, [first, second], "Speed curve", settings);
        Assert.AreEqual(first.Samples.Length / (double)first.Channels / 44100, edit.NewDuration, 1e-10); Assert.IsTrue(edit.NewDuration < 0.1);
    }

    [TestMethod]
    public async Task CurvesAndCapturedClock_AreSavedInMixRecipesAndSurviveProjectAndTempoRoundTrips()
    {
        using var arrangement = new MixArrangement { Bpm = 120 }; MixSource source = Source(arrangement, 4410, 0.1f);
        var lane = new MixLane(); arrangement.Lanes.Add(lane);
        MixClip a = arrangement.AddClip(lane, source, 0), b = arrangement.AddClip(lane, source, 0.2);
        using var session = await MixEffectEditSession.CaptureAsync(arrangement, [new(lane.Id, 0, 0.3, new HashSet<Guid> { a.Id, b.Id })]);
        AudioEffectSettings settings = Gain(new() { Scope = EffectCurveScope.AcrossSelection, IncludeGaps = false });
        AudioEffectResult result = await Render(session, 0, settings); MixEffectCommit edit = session.Apply(arrangement, [result], "Curved gain", settings);
        MixSource effected = arrangement.Sources[arrangement.Lanes[0].Clips.Single(clip => edit.SelectedClipIds.Contains(clip.Id)).SourceId];
        Assert.IsNotNull(effected.TempoRecipe); Assert.IsNotNull(effected.TempoRecipe.CurveScope); Assert.IsTrue(settings.SoundEquals(effected.TempoRecipe.Effect));
        string file = Path.Combine(Path.GetTempPath(), "ModularAudience-curve-mix-" + Guid.NewGuid().ToString("N") + ".mamix");
        try
        {
            await arrangement.SaveAsync(file); using MixArrangement loaded = await MixArrangement.LoadAsync(file);
            MixSource stored = loaded.Sources[effected.Id]; Assert.IsTrue(settings.SoundEquals(stored.TempoRecipe!.Effect));
            CollectionAssert.AreEqual(effected.TempoRecipe.CurveScope.Segments.ToArray(), stored.TempoRecipe.CurveScope!.Segments.ToArray());
            MixSource faster = await MixTempoOrigins.PrepareAsync(loaded, stored, 180, 120, ClipTempoMode.Varispeed);
            Assert.AreEqual((int)Math.Round(result.Samples.Length * 2d / 3), faster.Data.Length, 2);
            MixSource dry = await MixTempoOrigins.PrepareAsync(loaded, loaded.Sources[source.Id], 180, 120, ClipTempoMode.Varispeed);
            Assert.IsTrue(faster.Data.All(float.IsFinite));
            Assert.AreEqual(dry.Data[0], faster.Data[0], 1e-5f);
            Assert.AreEqual(dry.Data[^1] * 10, faster.Data[^1], 0.001f); // Respect the existing WDL resampler's edge response.
            CollectionAssert.AreEqual(result.Samples, stored.Data);
        }
        finally { File.Delete(file); }
    }

    private static AudioEffectSettings Gain(EffectCurveTiming timing) => AudioEffectCatalog.Get(AudioEffectKind.Gain).CreateDefaults().With(("GainDb", 20)) with
        { Automation = new() { Parameters = new() { ["GainDb"] = new() { StartValue = 0 } }, Timing = timing } };
    private static Task<AudioEffectResult> Render(MixEffectEditSession session, int index, AudioEffectSettings settings) =>
        AudioEffectProcessor.ProcessAsync(session.Inputs[index].Samples, 44100, session.Inputs[index].Channels, settings,
            execution: new() { CurveScope = session.CreateCurveScope(index, settings.Automation!.Timing) });
    private static MixSource Source(MixArrangement arrangement, int frames, float sample)
    {
        var source = new MixSource { Data = Enumerable.Repeat(sample, frames).ToArray(), Channels = 1, SampleRate = 44100, Bpm = 120 };
        arrangement.Sources.Add(source.Id, source); return source;
    }
}
