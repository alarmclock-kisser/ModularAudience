using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Arrangement;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class MixNegativeTimelineTests
{
    [TestMethod]
    public void NegativePositionsSnapSymmetricallyAndRemainValidProjectData()
    {
        using var mix = new MixArrangement { Bpm = 120, SnapDivision = 8 };
        Assert.AreEqual(-0.3, mix.SnapTime(-0.3)); Assert.AreEqual(-0.25, mix.SnapTime(-0.3, true));
        Assert.AreEqual(-0.5, mix.SnapTime(-0.375, true)); Assert.AreEqual(0.5, mix.SnapTime(0.375, true));
        var source = new MixSource { Data = new float[8000], Channels = 1, SampleRate = 8000 }; mix.Sources.Add(source.Id, source);
        var lane = new MixLane(); mix.Lanes.Add(lane); MixClip clip = mix.AddClip(lane, source, -0.5);
        mix.Visual.HorizontalScrollSeconds = -2; mix.Validate(); Assert.AreEqual(-0.5, clip.Start); Assert.AreEqual(0.5, mix.Duration);
    }

    [TestMethod]
    public async Task RenderCropsBeforeZeroWithoutNegativeArrayIndicesOrRestartingFade()
    {
        using var mix = new MixArrangement(); var source = new MixSource { Data = Enumerable.Repeat(0.4f, 44100).ToArray(), Channels = 1, SampleRate = 44100 };
        mix.Sources.Add(source.Id, source); var lane = new MixLane(); mix.Lanes.Add(lane);
        var clip = mix.AddClip(lane, source, -0.5); clip.FadeIn = 1;
        mix.AddClip(lane, source, -3); // Completely before zero: must not render or index negative samples.
        using AudioObj result = await MixRenderer.RenderAsync(mix, normalizeOutput: false);
        Assert.AreEqual(0.5, result.Duration.TotalSeconds, 1e-7);
        Assert.AreEqual(0.2f, result.Data[0], 1e-6f); Assert.AreEqual(0.3f, result.Data[22050], 1e-6f);
        using var cache = new MixPreparationCache(); await cache.PrepareAsync(mix);
        CollectionAssert.AreEqual(result.Data, cache.Mix(mix));
    }

    [TestMethod]
    public async Task NegativeSelectionCanBeCapturedAndProcessedWithoutLosingItsOrigin()
    {
        using var mix = new MixArrangement(); var source = new MixSource { Data = Enumerable.Repeat(0.2f, 44100).ToArray(), Channels = 1, SampleRate = 44100 };
        mix.Sources.Add(source.Id, source); var lane = new MixLane(); mix.Lanes.Add(lane); var clip = mix.AddClip(lane, source, -0.5);
        using var session = await MixEffectEditSession.CaptureAsync(mix, [new(lane.Id, -0.25, 0.25, new HashSet<Guid> { clip.Id })]);
        Assert.AreEqual(-0.25, session.Start); Assert.AreEqual(22050, session.Inputs[0].Samples.Length);
        var result = session.Inputs[0].Samples.ToArray().Select(value => value * 0.5f).ToArray();
        MixEffectCommit edit = session.Apply(mix, [new(result, 1)], "Gain");
        Assert.IsTrue(edit.Changed); Assert.AreEqual(-0.25, mix.Lanes[0].Clips.Single(value => edit.SelectedClipIds.Contains(value.Id)).Start); mix.Validate();
    }
}
