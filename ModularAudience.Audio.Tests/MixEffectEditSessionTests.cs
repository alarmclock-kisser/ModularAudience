using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Arrangement;
using ModularAudience.Audio.Effects;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class MixEffectEditSessionTests
{
    [TestMethod]
    public async Task Capture_PreservesLeadingTrailingAndInteriorSilenceAndKeepsLanesIndependent()
    {
        using var arrangement = new MixArrangement(); MixSource stereo = Source(arrangement, 0.1, 2, 0.2f), mono = Source(arrangement, 0.2, 1, 0.3f);
        MixLane first = new() { Name = "Stereo", Gain = 0.6, Mute = true }, second = new() { Name = "Mono", Solo = true };
        arrangement.Lanes.AddRange([first, second]);
        MixClip one = arrangement.AddClip(first, stereo, 1), two = arrangement.AddClip(first, stereo, 1.3), three = arrangement.AddClip(second, mono, 1.05);
        string before = arrangement.Capture();
        using MixEffectEditSession session = await MixEffectEditSession.CaptureAsync(arrangement,
            [new(first.Id, 0.9, 1.6, new HashSet<Guid> { one.Id, two.Id }), new(second.Id, 0.9, 1.6, new HashSet<Guid> { three.Id })]);

        Assert.AreEqual(before, arrangement.Capture()); Assert.AreEqual(2, session.Inputs.Count);
        Assert.AreEqual(2, session.Inputs[0].Channels); Assert.AreEqual(1, session.Inputs[1].Channels);
        Assert.AreEqual(30870 * 2, session.Inputs[0].Samples.Length);
        Assert.AreEqual(0f, At(session.Inputs[0], 0.05)); Assert.AreEqual(0.2f, At(session.Inputs[0], 0.15));
        Assert.AreEqual(0f, At(session.Inputs[0], 0.3)); Assert.AreEqual(0.2f, At(session.Inputs[0], 0.45)); Assert.AreEqual(0f, At(session.Inputs[0], 0.65));
        Assert.AreEqual(0.3f, At(session.Inputs[1], 0.2)); Assert.AreEqual(0f, At(session.Inputs[1], 0.5));
        using AudioObj preview = await session.RenderPreviewAsync();
        Assert.AreEqual(0.7, preview.Duration.TotalSeconds, 1e-7); Assert.AreEqual(2, preview.Channels);
        Assert.IsTrue(session.IsCurrent(arrangement));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RegionGain_PreservesOutsideAudioIncludingExistingFadesAndCrossfades(bool autoCrossfade)
    {
        using var arrangement = new MixArrangement(); var source = Source(arrangement, 1, 2, 0.2f);
        var lane = new MixLane { Name = "Faded", Gain = 0.7, AutoCrossfade = autoCrossfade }; arrangement.Lanes.Add(lane);
        var clip = arrangement.AddClip(lane, source, 0); clip.Gain = 0.8; clip.FadeIn = 0.8; clip.FadeOut = 0.7;
        var overlapping = arrangement.AddClip(lane, source, 0.05, 0, 0.35); overlapping.Gain = 0.4;
        var following = arrangement.AddClip(lane, source, 1.2, 0, 0.2);
        float[] originalSource = (float[])source.Data.Clone();
        using AudioObj before = await MixRenderer.RenderAsync(arrangement, normalizeOutput: false);
        using var session = await MixEffectEditSession.CaptureAsync(arrangement,
            [new(lane.Id, 0.12, 0.21, new HashSet<Guid> { clip.Id, overlapping.Id })]);
        AudioEffectResult[] results = [new(session.Inputs[0].Samples.ToArray().Select(value => value * 2).ToArray(), 2)];

        MixEffectCommit edit = session.Apply(arrangement, results, "Gain");
        using AudioObj after = await MixRenderer.RenderAsync(arrangement, normalizeOutput: false);

        Assert.IsTrue(edit.Changed); Assert.AreEqual(before.Data.Length, after.Data.Length);
        for (int frame = 0; frame < before.Data.Length / 2; frame++)
        {
            double time = frame / 44100d; double expected = time >= 0.12 && time < 0.21 ? before.Data[frame * 2] * 2 : before.Data[frame * 2];
            Assert.AreEqual(expected, after.Data[frame * 2], 0.000002, $"Unexpected edit outside scope at {time:F6}s; crossfade={autoCrossfade}.");
        }
        CollectionAssert.AreEqual(originalSource, source.Data);
        Assert.AreEqual(0.7, arrangement.Lanes[0].Gain); Assert.AreEqual(autoCrossfade, arrangement.Lanes[0].AutoCrossfade);
        Assert.IsTrue(arrangement.Lanes[0].Clips.Any(c => c.Id == following.Id));
        Assert.IsTrue(arrangement.Lanes[0].Clips.Where(c => c.SourceId != source.Id).All(c => c.HasRenderedEnvelope));
    }

    [TestMethod]
    [DataRow(0.25)]
    [DataRow(1d)]
    public async Task LengthChange_SplicesBoundaryRemaindersAndRipplesOnlyAffectedLanes(double newDuration)
    {
        using var arrangement = new MixArrangement { LoopStart = 1, LoopEnd = 3 };
        var source = Source(arrangement, 1, 1, 0.2f);
        MixLane lane = new() { Name = "Selected" }, untouched = new() { Name = "Untouched" }; arrangement.Lanes.AddRange([lane, untouched]);
        var selected = arrangement.AddClip(lane, source, 1); var later = arrangement.AddClip(lane, source, 3);
        var other = arrangement.AddClip(untouched, source, 3); Guid otherId = other.Id;
        string before = arrangement.Capture();
        using var session = await MixEffectEditSession.CaptureAsync(arrangement, [new(lane.Id, 1.25, 1.75, new HashSet<Guid> { selected.Id })]);
        float[] processed = Enumerable.Repeat(0.4f, (int)Math.Round(newDuration * 44100)).ToArray();
        MixEffectCommit edit = session.Apply(arrangement, [new(processed, 1)], "Stretch");
        MixLane edited = arrangement.Lanes[0]; double delta = newDuration - 0.5;

        Assert.IsTrue(edit.Changed); Assert.AreEqual(newDuration, edit.NewDuration, 1e-7);
        Assert.AreEqual(4, edited.Clips.Count); Assert.AreEqual(1d, edited.Clips[0].Start); Assert.AreEqual(0.25, edited.Clips[0].SourceDuration, 1e-7);
        Assert.AreEqual(1.25, edited.Clips[1].Start, 1e-7); Assert.AreEqual(newDuration, edited.Clips[1].SourceDuration, 1e-7);
        Assert.AreEqual(1.25 + newDuration, edited.Clips[2].Start, 1e-7); Assert.AreEqual(0.25, edited.Clips[2].SourceDuration, 1e-7);
        Assert.AreEqual(0.75, edited.Clips[2].SourceOffset, 1e-7);
        Assert.AreEqual(3 + delta, edited.Clips.Single(c => c.Id == later.Id).Start, 1e-7);
        Assert.AreEqual(3d, arrangement.Lanes[1].Clips.Single(c => c.Id == otherId).Start);
        Assert.AreEqual(3 + delta, arrangement.LoopEnd, 1e-7); Assert.AreEqual(1.25 + newDuration / 2, session.MapTime(1.5, newDuration), 1e-7);
        string after = arrangement.Capture(); arrangement.Restore(before);
        Assert.AreEqual(selected.Id, arrangement.Lanes[0].Clips[0].Id);
        arrangement.Restore(after); Assert.AreEqual(4, arrangement.Lanes[0].Clips.Count);
        Assert.IsTrue(arrangement.Sources.ContainsKey(source.Id), "Original immutable assets must remain available for undo.");
    }

    [TestMethod]
    public async Task WholeClipMultiselection_ProcessesGapsWithoutTouchingUnselectedSegmentsInsideTheSpan()
    {
        using var arrangement = new MixArrangement(); var source = Source(arrangement, 0.2, 1, 0.2f);
        var lane = new MixLane(); arrangement.Lanes.Add(lane);
        MixClip first = arrangement.AddClip(lane, source, 0), middle = arrangement.AddClip(lane, source, 0.4), last = arrangement.AddClip(lane, source, 0.8);
        using var session = await MixEffectEditSession.CaptureAsync(arrangement, [new(lane.Id, 0, 1, new HashSet<Guid> { first.Id, last.Id })]);
        Assert.AreEqual(0f, At(session.Inputs[0], 0.5));
        AudioEffectResult processed = await AudioEffectProcessor.ProcessAsync(session.Inputs[0].Samples, 44100, 1, AudioEffectCatalog.Get(AudioEffectKind.Gain).CreateDefaults().With(("GainDb", -6)));
        session.Apply(arrangement, [processed], "Gain");
        MixClip kept = arrangement.Lanes[0].Clips.Single(c => c.Id == middle.Id);
        Assert.AreEqual(source.Id, kept.SourceId); Assert.AreEqual(0.4, kept.Start); Assert.AreEqual(0.2, kept.SourceDuration);
        using AudioObj rendered = await MixRenderer.RenderAsync(arrangement, normalizeOutput: false);
        Assert.AreEqual(0.2f, rendered.Data[(int)(0.5 * 44100) * 2], 1e-6f);
        Assert.AreEqual((float)(0.2 * Math.Pow(10, -6d / 20)), rendered.Data[(int)(0.1 * 44100) * 2], 1e-6f);
    }

    [TestMethod]
    public async Task NoOpOrInvalidOrStaleOutput_NeverChangesAssetsOrArrangement()
    {
        using var arrangement = new MixArrangement(); var source = Source(arrangement, 0.1, 2, 0.2f); var lane = new MixLane(); arrangement.Lanes.Add(lane);
        var clip = arrangement.AddClip(lane, source, 0);
        using var session = await MixEffectEditSession.CaptureAsync(arrangement, [new(lane.Id, 0, 0.1, new HashSet<Guid> { clip.Id })]);
        string before = arrangement.Capture(); int assets = arrangement.Sources.Count;
        Assert.IsFalse(session.Apply(arrangement, [new(session.Inputs[0].Samples.ToArray(), 2)], "Bypass").Changed);
        Assert.ThrowsExactly<ArgumentException>(() => session.Apply(arrangement, [new([float.NaN, 0], 2)], "Invalid"));
        Assert.ThrowsExactly<ArgumentException>(() => session.Apply(arrangement, [], "Invalid"));
        Assert.AreEqual(before, arrangement.Capture()); Assert.AreEqual(assets, arrangement.Sources.Count);
        source.Data[0] = 0.9f; Assert.IsFalse(session.IsCurrent(arrangement));
        Assert.ThrowsExactly<InvalidOperationException>(() => session.Apply(arrangement, [new([0, 0], 2)], "Stale"));
        Assert.AreEqual(before, arrangement.Capture()); Assert.AreEqual(assets, arrangement.Sources.Count); Assert.AreEqual(0.9f, source.Data[0]);
    }

    [TestMethod]
    public async Task MonoRegion_CanHaveItsOwnFormatAndEffectAssetsSurviveProjectRoundTrip()
    {
        using var arrangement = new MixArrangement(); var source = Source(arrangement, 0.1, 2, 0.2f); var lane = new MixLane(); arrangement.Lanes.Add(lane);
        var clip = arrangement.AddClip(lane, source, 0);
        using var session = await MixEffectEditSession.CaptureAsync(arrangement, [new(lane.Id, 0.02, 0.08, new HashSet<Guid> { clip.Id })]);
        AudioEffectResult result = await AudioEffectProcessor.ProcessAsync(session.Inputs[0].Samples, 44100, 2, AudioEffectCatalog.Get(AudioEffectKind.ConvertToMono).CreateDefaults());
        MixEffectCommit edit = session.Apply(arrangement, [result], "Mono");
        MixClip effect = arrangement.Lanes[0].Clips.Single(c => edit.SelectedClipIds.Contains(c.Id));
        Assert.AreEqual(1, arrangement.Sources[effect.SourceId].Channels); Assert.AreEqual(2, source.Channels);
        string file = Path.Combine(Path.GetTempPath(), "ModularAudience-mix-effects-" + Guid.NewGuid().ToString("N") + ".mamix");
        try
        {
            await arrangement.SaveAsync(file); using MixArrangement loaded = await MixArrangement.LoadAsync(file);
            Assert.IsTrue(loaded.Lanes[0].Clips.All(c => c.HasRenderedEnvelope));
            using AudioObj expected = await MixRenderer.RenderAsync(arrangement), actual = await MixRenderer.RenderAsync(loaded);
            CollectionAssert.AreEqual(expected.Data, actual.Data);
        }
        finally { File.Delete(file); }
    }

    [TestMethod]
    public async Task Capture_HonorsCancellationAndRejectsEmptyOrOversizedSelection()
    {
        using var arrangement = new MixArrangement(); var lane = new MixLane(); arrangement.Lanes.Add(lane);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => MixEffectEditSession.CaptureAsync(arrangement,
            [new(lane.Id, 0, 1, new HashSet<Guid>())], cancellation.Token));
        Assert.ThrowsExactly<ArgumentException>(() => MixEffectEditSession.CaptureAsync(arrangement, []));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => MixEffectEditSession.CaptureAsync(arrangement,
            [new(lane.Id, 0, 5000, new HashSet<Guid>())]));
    }

    [TestMethod]
    [DataRow(ClipTempoMode.Varispeed)]
    [DataRow(ClipTempoMode.TimeStretch)]
    public async Task SelectionUsesAudibleTempoAndSourceOffset_NotRawUntrimmedSource(ClipTempoMode mode)
    {
        using var arrangement = new MixArrangement { Bpm = 120 };
        var source = new MixSource { SampleRate = 16000, Channels = 2, Bpm = 60,
            Data = Enumerable.Range(0, 16000).SelectMany(frame => new[]
            { (float)(0.2 * Math.Sin(2 * Math.PI * 220 * frame / 16000)), (float)(-0.1 * Math.Sin(2 * Math.PI * 220 * frame / 16000)) }).ToArray() };
        arrangement.Sources.Add(source.Id, source); var lane = new MixLane { Gain = 0.8 }; arrangement.Lanes.Add(lane);
        MixClip clip = arrangement.AddClip(lane, source, 0, 0.1, 0.8); clip.TempoMode = mode; clip.FadeIn = 0.25;
        using AudioObj before = await MixRenderer.RenderAsync(arrangement, normalizeOutput: false);
        using var session = await MixEffectEditSession.CaptureAsync(arrangement, [new(lane.Id, 0.1, 0.2, new HashSet<Guid> { clip.Id })]);
        Assert.AreEqual(4410 * 2, session.Inputs[0].Samples.Length);
        for (int frame = 0; frame < 4410; frame++) Assert.AreEqual(before.Data[(frame + 4410) * 2] / lane.Gain, session.Inputs[0].Samples.Span[frame * 2], 0.000002);
        float[] processed = session.Inputs[0].Samples.ToArray().Select(value => value * 0.5f).ToArray();
        session.Apply(arrangement, [new(processed, 2)], "Gain");
        Assert.IsTrue(arrangement.Lanes[0].Clips.All(value => value.TempoMode == ClipTempoMode.Original));
        using AudioObj after = await MixRenderer.RenderAsync(arrangement, normalizeOutput: false);
        Assert.AreEqual(before.Data.Length, after.Data.Length);
        for (int frame = 0; frame < before.Data.Length / 2; frame++)
        {
            double expected = before.Data[frame * 2] * (frame >= 4410 && frame < 8820 ? 0.5 : 1);
            Assert.AreEqual(expected, after.Data[frame * 2], 0.000002, mode + " / frame " + frame);
        }
    }

    private static float At(MixEffectInput input, double time) => input.Samples.Span[(int)Math.Round(time * 44100) * input.Channels];
    private static MixSource Source(MixArrangement arrangement, double duration, int channels, float level)
    {
        int frames = (int)Math.Round(duration * 44100);
        var source = new MixSource { SampleRate = 44100, Channels = channels, Bpm = 120,
            Data = Enumerable.Range(0, frames).SelectMany(_ => Enumerable.Range(0, channels).Select(channel => channel % 2 == 0 ? level : -level * 0.5f)).ToArray() };
        arrangement.Sources.Add(source.Id, source); return source;
    }
}
