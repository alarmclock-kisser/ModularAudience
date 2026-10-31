using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Generators;
using System.Diagnostics;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class BreakbeatRenderPerformanceTests
{
    [TestMethod]
    public async Task SharedPreviewCacheReusesPitchButInvalidatesChangedSamplesAndPitch()
    {
        using var sample = new AudioObj { Data = new float[4410], Channels = 1, SampleRate = 44100 };
        for (int i = 0; i < sample.Data.Length; i++) sample.Data[i] = (float)(0.2 * Math.Sin(i * 0.1));
        using var cache = new PatternRenderCache();
        var note = new BreakbeatPatternNote(0, 0, 128, PitchSemitones: 3);
        using var preview = await BreakbeatGenerator_V2.RenderPatternNotesAsync([note], [sample], 1, 120, 16, 0, renderCache: cache);
        Assert.AreEqual(1, cache.Count);
        using var hear = await BreakbeatGenerator_V2.RenderPatternNotesAsync([note, note with { StartTick = 256, VolumePercent = 80 }], [sample], 1, 120, 16, 0, renderCache: cache);
        Assert.IsGreaterThan(0L, cache.Hits);
        Assert.AreEqual(1, cache.Count);
        using var changedPitch = await BreakbeatGenerator_V2.RenderPatternNotesAsync([note with { PitchSemitones = 4 }], [sample], 1, 120, 16, 0, renderCache: cache);
        Assert.AreEqual(2, cache.Count);
        sample.Data[100] = 0.9f; // In-place edits must invalidate too, not just Data replacement.
        using var changedSample = await BreakbeatGenerator_V2.RenderPatternNotesAsync([note], [sample], 1, 120, 16, 0, renderCache: cache);
        Assert.AreEqual(3, cache.Count);
        cache.Dispose();
        Assert.AreEqual(0, cache.Count);
        using var afterDispose = await BreakbeatGenerator_V2.RenderPatternNotesAsync([note], [sample], 1, 120, 16, 0, renderCache: cache);
        Assert.AreEqual(0, cache.Count, "Finishing renders cannot repopulate a closed editor's cache.");
        using var tinyCache = new PatternRenderCache(16);
        using var uncachedLarge = await BreakbeatGenerator_V2.RenderPatternNotesAsync([note], [sample], 1, 120, 16, 0, renderCache: tinyCache);
        Assert.AreEqual(0, tinyCache.Count, "Oversized entries must not exceed the cache memory budget.");
    }

    [TestMethod]
    public async Task RepeatedPitchTransformsMatchSeparateRendersAndReportElapsedTime()
    {
        using var sample = new AudioObj { Data = new float[22050], Channels = 1, SampleRate = 44100 };
        for (int i = 0; i < sample.Data.Length; i++) sample.Data[i] = (float)(0.2 * Math.Sin(2 * Math.PI * 220 * i / 44100));
        var notes = Enumerable.Range(0, 24).Select(i => new BreakbeatPatternNote(0, i * 64, 256,
            PitchSemitones: i % 3 * 2f, VolumePercent: i % 2 == 0 ? 80 : 100)).ToArray();
        using var warm = await BreakbeatGenerator_V2.RenderPatternNotesAsync(notes.Take(3).ToArray(), [sample], 2, 120, 16, 0);
        var clock = Stopwatch.StartNew();
        using var combined = await BreakbeatGenerator_V2.RenderPatternNotesAsync(notes, [sample], 2, 120, 16, 0);
        long combinedMs = clock.ElapsedMilliseconds;
        clock.Restart();
        float[] reference = new float[combined.Data.Length];
        foreach (var note in notes)
        {
            using var individual = await BreakbeatGenerator_V2.RenderPatternNotesAsync([note], [sample], 2, 120, 16, 0);
            for (int i = 0; i < individual.Data.Length; i++) reference[i] += individual.Data[i];
        }
        Console.WriteLine($"24 pitched notes / 3 unique transforms: optimized={combinedMs} ms; separate serial renders={clock.ElapsedMilliseconds} ms; CPUs={Environment.ProcessorCount}");
        CollectionAssert.AreEqual(reference, combined.Data, "Reusing transforms must preserve note gains and deterministic summation.");
        Assert.IsTrue(sample.Data.Max(Math.Abs) < 0.21f, "Source data must remain unchanged.");
    }

    [TestMethod]
    public async Task CancelledRenderDoesNotStartCpuWork()
    {
        using var source = new AudioObj { Data = new float[44100], SampleRate = 44100, Channels = 1 };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await BreakbeatGenerator_V2.RenderPatternNotesAsync([new(0, 0, 256, PitchSemitones: 7)], [source],
                1, 120, 16, 0, cancellationToken: cancellation.Token));
    }
}
