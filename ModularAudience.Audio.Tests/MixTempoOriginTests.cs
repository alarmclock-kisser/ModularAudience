using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Arrangement;
using ModularAudience.Audio.Effects;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class MixTempoOriginTests
{
    [TestMethod]
    [DataRow(ClipTempoMode.Varispeed)]
    [DataRow(ClipTempoMode.TimeStretch)]
    public async Task RepeatedTempoChanges_RenderFreshOriginalAndReturningToOriginalTempoIsBitExact(ClipTempoMode mode)
    {
        using var mix = new MixArrangement(); MixSource original = Source(100); mix.Sources.Add(original.Id, original);
        float[] pristine = (float[])original.Data.Clone(); MixSource current = original;
        foreach (double target in new[] { 125d, 80, 145, 110, 100 })
        {
            MixSource next = await MixTempoOrigins.PrepareAsync(mix, current, target, 120, mode);
            MixSource fresh = await MixTempoOrigins.PrepareAsync(mix, original, target, 120, mode);
            CollectionAssert.AreEqual(fresh.Data, next.Data, $"{mode} at {target} must not use a previous tempo rendering.");
            Assert.AreEqual(original.Id, next.TempoOriginId); Assert.AreEqual(target / 100, next.TempoOriginScale, 1e-10);
            mix.Sources.Add(next.Id, next); current = next;
            CollectionAssert.AreEqual(pristine, original.Data);
        }
        CollectionAssert.AreEqual(pristine, current.Data);
    }

    [TestMethod]
    public async Task UnknownOriginalTempo_UsesTheInitialFallbackOnceNotEachSubsequentGridTempo()
    {
        using var mix = new MixArrangement(); MixSource original = Source(0); mix.Sources.Add(original.Id, original);
        MixSource first = await MixTempoOrigins.PrepareAsync(mix, original, 150, 100, ClipTempoMode.Varispeed); mix.Sources.Add(first.Id, first);
        MixSource second = await MixTempoOrigins.PrepareAsync(mix, first, 200, 150, ClipTempoMode.Varispeed);
        MixSource expected = await MixTempoOrigins.PrepareAsync(mix, original, 200, 100, ClipTempoMode.Varispeed);
        CollectionAssert.AreEqual(expected.Data, second.Data); Assert.AreEqual(100f, second.TempoOriginBpm);
    }

    [TestMethod]
    public async Task ProjectRoundTrip_PreservesUnreferencedPristineOriginForFurtherTempoChanges()
    {
        string path = Path.Combine(Path.GetTempPath(), "mix-origins-" + Guid.NewGuid().ToString("N") + ".mamix");
        try
        {
            using var mix = new MixArrangement(); MixSource original = Source(100); mix.Sources.Add(original.Id, original);
            MixSource prepared = await MixTempoOrigins.PrepareAsync(mix, original, 125, 100, ClipTempoMode.Varispeed); mix.Sources.Add(prepared.Id, prepared);
            var lane = new MixLane(); mix.Lanes.Add(lane); mix.AddClip(lane, prepared, 0, 0.05, prepared.Duration - 0.05);
            await mix.SaveAsync(path);
            using MixArrangement loaded = await MixArrangement.LoadAsync(path);
            Assert.AreEqual(2, loaded.Sources.Count); Assert.IsFalse(loaded.Lanes.SelectMany(value => value.Clips).Any(clip => clip.SourceId == original.Id));
            CollectionAssert.AreEqual(original.Data, loaded.Sources[original.Id].Data);
            MixSource next = await MixTempoOrigins.PrepareAsync(loaded, loaded.Sources[prepared.Id], 150, 125, ClipTempoMode.Varispeed);
            MixSource expected = await MixTempoOrigins.PrepareAsync(mix, original, 150, 100, ClipTempoMode.Varispeed);
            CollectionAssert.AreEqual(expected.Data, next.Data);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public async Task LegacyIndependentAsset_BecomesAFixedBaselineWithoutMutatingOrLosingAudio()
    {
        using var mix = new MixArrangement(); MixSource legacy = Source(125); mix.Sources.Add(legacy.Id, legacy);
        MixSource first = await MixTempoOrigins.PrepareAsync(mix, legacy, 150, 125, ClipTempoMode.Varispeed); mix.Sources.Add(first.Id, first);
        MixSource back = await MixTempoOrigins.PrepareAsync(mix, first, 125, 150, ClipTempoMode.Varispeed);
        CollectionAssert.AreEqual(legacy.Data, back.Data);
        Assert.AreEqual(legacy.Id, back.TempoOriginId);
    }

    [TestMethod]
    public void MissingOrCyclicOrigins_AreRejectedRatherThanUsingStretchedFallbacksSilently()
    {
        using var mix = new MixArrangement(); MixSource source = Source(100); source.TempoOriginId = Guid.NewGuid(); mix.Sources.Add(source.Id, source);
        Assert.ThrowsExactly<InvalidDataException>(() => MixTempoOrigins.Resolve(mix, source));
        MixSource second = Source(120); mix.Sources.Add(second.Id, second); source.TempoOriginId = second.Id; second.TempoOriginId = source.Id;
        Assert.ThrowsExactly<InvalidDataException>(() => MixTempoOrigins.Resolve(mix, source));
    }

    [TestMethod]
    public async Task EffectBounceReplaysFromPristineDependenciesInsteadOfStretchingItsOldProcessedPcm()
    {
        using var mix = new MixArrangement { Bpm = 125 }; MixSource original = Source(100); mix.Sources.Add(original.Id, original);
        MixSource imported = await MixTempoOrigins.PrepareAsync(mix, original, 125, 100, ClipTempoMode.TimeStretch); mix.Sources.Add(imported.Id, imported);
        var lane = new MixLane { Name = "Lead" }; mix.Lanes.Add(lane); var clip = mix.AddClip(lane, imported, 0);
        using var session = await MixEffectEditSession.CaptureAsync(mix, [new(lane.Id, 0, imported.Duration, new HashSet<Guid> { clip.Id })]);
        AudioEffectSettings gain = AudioEffectCatalog.Get(AudioEffectKind.Gain).CreateDefaults().With(("GainDb", -6));
        var processed = await AudioEffectProcessor.ProcessAsync(session.Inputs[0].Samples, 44100, 2, gain);
        MixEffectCommit edit = session.Apply(mix, [processed], "Gain", gain);
        MixSource bounced = mix.Sources[mix.Lanes[0].Clips.Single(value => edit.SelectedClipIds.Contains(value.Id)).SourceId];
        Assert.IsNotNull(bounced.TempoRecipe);
        foreach (double tempo in new[] { 150d, 90, 180 })
        {
            MixSource freshOriginal = await MixTempoOrigins.PrepareAsync(mix, original, tempo, 100, ClipTempoMode.TimeStretch);
            AudioEffectResult expected = await AudioEffectProcessor.ProcessAsync(freshOriginal.Data, freshOriginal.SampleRate, 2, gain);
            MixSource actual = await MixTempoOrigins.PrepareAsync(mix, bounced, tempo, 125, ClipTempoMode.TimeStretch);
            CollectionAssert.AreEqual(expected.Samples, actual.Data, "A gain bounce must not cause another generation of pitch/time processing.");
            mix.Sources.Add(actual.Id, actual); bounced = actual;
        }
    }

    [TestMethod]
    public async Task EffectRecipeAndAllOriginalDependenciesSurviveProjectSaveLoad()
    {
        string path = Path.Combine(Path.GetTempPath(), "mix-effect-origins-" + Guid.NewGuid().ToString("N") + ".mamix");
        try
        {
            using var mix = new MixArrangement { Bpm = 125 }; MixSource original = Source(100); mix.Sources.Add(original.Id, original);
            MixSource imported = await MixTempoOrigins.PrepareAsync(mix, original, 125, 100, ClipTempoMode.TimeStretch); mix.Sources.Add(imported.Id, imported);
            var lane = new MixLane(); mix.Lanes.Add(lane); var clip = mix.AddClip(lane, imported, 0);
            using var session = await MixEffectEditSession.CaptureAsync(mix, [new(lane.Id, 0, imported.Duration, new HashSet<Guid> { clip.Id })]);
            AudioEffectSettings gain = AudioEffectCatalog.Get(AudioEffectKind.Gain).CreateDefaults().With(("GainDb", -3));
            var result = await AudioEffectProcessor.ProcessAsync(session.Inputs[0].Samples, 44100, 2, gain); session.Apply(mix, [result], "Gain", gain);
            await mix.SaveAsync(path); using MixArrangement loaded = await MixArrangement.LoadAsync(path);
            Assert.IsTrue(loaded.Sources.ContainsKey(original.Id)); Assert.IsTrue(loaded.Sources.ContainsKey(imported.Id));
            MixSource effect = loaded.Sources[loaded.Lanes[0].Clips.Single().SourceId]; Assert.IsNotNull(effect.TempoRecipe);
            MixSource rerendered = await MixTempoOrigins.PrepareAsync(loaded, effect, 150, 125, ClipTempoMode.TimeStretch);
            MixSource fromOriginal = await MixTempoOrigins.PrepareAsync(loaded, loaded.Sources[original.Id], 150, 100, ClipTempoMode.TimeStretch);
            AudioEffectResult expected = await AudioEffectProcessor.ProcessAsync(fromOriginal.Data, fromOriginal.SampleRate, 2, gain);
            CollectionAssert.AreEqual(expected.Samples, rerendered.Data);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public async Task PartialEffectRemainderRebuildsItsFullOriginalEnvelopeBeforeCroppingAtNewTempo()
    {
        using var mix = new MixArrangement { Bpm = 125 }; MixSource original = Source(100); mix.Sources.Add(original.Id, original);
        MixSource imported = await MixTempoOrigins.PrepareAsync(mix, original, 125, 100, ClipTempoMode.TimeStretch); mix.Sources.Add(imported.Id, imported);
        var lane = new MixLane { Name = "Faded" }; mix.Lanes.Add(lane); var clip = mix.AddClip(lane, imported, 0);
        clip.Gain = 0.6; clip.FadeIn = 0.1; clip.FadeOut = 0.1;
        using var session = await MixEffectEditSession.CaptureAsync(mix, [new(lane.Id, 0.1, 0.2, new HashSet<Guid> { clip.Id })]);
        AudioEffectSettings gain = AudioEffectCatalog.Get(AudioEffectKind.Gain).CreateDefaults().With(("GainDb", -6));
        var result = await AudioEffectProcessor.ProcessAsync(session.Inputs[0].Samples, 44100, 2, gain); session.Apply(mix, [result], "Gain", gain);
        MixSource remainder = mix.Sources[mix.Lanes[0].Clips.OrderBy(value => value.Start).First().SourceId];
        Assert.IsNotNull(remainder.TempoRecipe?.FrozenClipId);
        MixSource actual = await MixTempoOrigins.PrepareAsync(mix, remainder, 150, 125, ClipTempoMode.TimeStretch);
        MixSource fresh = await MixTempoOrigins.PrepareAsync(mix, original, 150, 100, ClipTempoMode.TimeStretch);
        using var expectedMix = new MixArrangement { Bpm = 150 }; expectedMix.Sources.Add(fresh.Id, fresh);
        var expectedLane = new MixLane(); expectedMix.Lanes.Add(expectedLane); var expectedClip = expectedMix.AddClip(expectedLane, fresh, 0);
        expectedClip.Gain = 0.6; expectedClip.FadeIn = expectedClip.FadeOut = 0.1 * 125 / 150;
        float[] expected = await MixRenderer.RenderClipForEffectAsync(expectedMix, expectedLane, expectedClip, CancellationToken.None);
        CollectionAssert.AreEqual(expected, actual.Data);
    }

    private static MixSource Source(float bpm) => new() { Name = "Pristine", Bpm = bpm, SampleRate = 16000, Channels = 2,
        Data = Enumerable.Range(0, 8000).SelectMany(frame => new[]
        { (float)(0.2 * Math.Sin(2 * Math.PI * 330 * frame / 16000)), (float)(-0.1 * Math.Sin(2 * Math.PI * 330 * frame / 16000)) }).ToArray() };
}
