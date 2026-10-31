using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Generators;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class BreakbeatInterleavedRenderTests
{
    [TestMethod]
    [DataRow("AmenSnareScale")]
    [DataRow("AmenSnareScale_Old")]
    [DataRow(" - None - ")]
    public async Task PresetHitsReceiveReproducibleHalfSemitonePitches(string preset)
    {
        using var source = new AudioObj { Data = new float[4410], Channels = 1, SampleRate = 44100 };
        var pattern = await BreakbeatGenerator_V2.GenerateBreakPatternAsync([DrumsetElement.Snare, DrumsetElement.Snare],
            2, 0.6f, 64, 0, 0.666f, true, 123, preset);
        var notes = BreakbeatGenerator_V2.CreateGeneratedPitchNotes(pattern, [source, source], 100, 64, true, 3.5f, 100, 123);
        Assert.IsTrue(notes.Count > 0);
        Assert.IsTrue(notes.All(n => n.PitchSemitones != 0 && Math.Abs(n.PitchSemitones) <= 3.5f));
        Assert.IsTrue(notes.All(n => n.PitchSemitones * 2 == MathF.Round(n.PitchSemitones * 2)));
        CollectionAssert.AreEqual(notes, BreakbeatGenerator_V2.CreateGeneratedPitchNotes(pattern, [source, source], 100, 64, true, 3.5f, 100, 123));
        Assert.IsTrue(BreakbeatGenerator_V2.CreateGeneratedPitchNotes(pattern, [source, source], 100, 64, true, 3.5f, 0, 123).All(n => n.PitchSemitones == 0));
        for (int i = 0; i + 1 < notes.Count; i++) Assert.IsTrue(notes[i].StartTick + notes[i].DurationTicks <= notes[i + 1].StartTick);
    }

    [TestMethod]
    public async Task GeneratedPitchIsAudibleAndDoesNotMutateSource()
    {
        using var source = new AudioObj { Data = new float[4410], SampleRate = 44100, Channels = 1, Volume = 100 };
        for (int i = 0; i < source.Data.Length; i++) source.Data[i] = (float)(0.2 * Math.Sin(2 * Math.PI * 440 * i / 44100));
        var before = (float[])source.Data.Clone();
        List<bool[]> pattern = [new[] { true, false, false, false }];
        using var plain = await BreakbeatGenerator_V2.RenderBreakbeatAsync(pattern, [source], 120, 4, 0, interleaved: true);
        using var pitched = await BreakbeatGenerator_V2.RenderBreakbeatAsync(pattern, [source], 120, 4, 0, interleaved: true,
            pitchRangeSemitones: 12, pitchChancePercent: 100, pitchSeed: 42);
        Assert.AreEqual(plain.Data.Length, pitched.Data.Length);
        Assert.IsTrue(pitched.Data.All(float.IsFinite));
        Assert.IsTrue(plain.Data.Zip(pitched.Data).Sum(pair => Math.Abs(pair.First - pair.Second)) > 1);
        CollectionAssert.AreEqual(before, source.Data);
    }

    [TestMethod]
    public async Task InterleavedLongSamplesDoNotAccumulateTowardEndAndSourcesStayUnchanged()
    {
        using var first = new AudioObj { Data = Enumerable.Repeat(0.25f, 44100 * 6).ToArray(), Channels = 1, SampleRate = 44100, Volume = 100 };
        using var second = new AudioObj { Data = Enumerable.Repeat(-0.25f, 44100 * 6).ToArray(), Channels = 1, SampleRate = 44100, Volume = 100 };
        var pattern = new List<bool[]> { new bool[128], new bool[128] };
        for (int step = 0; step < 128; step++) pattern[step % 2][step] = true;
        using var result = await BreakbeatGenerator_V2.RenderBreakbeatAsync(pattern, [first, second], 100, 64, 0, interleaved: true);
        Assert.AreEqual(4.8, result.Data.Length / (double)(result.SampleRate * result.Channels), 0.0001);
        double Rms(int begin, int end) => Math.Sqrt(result.Data.Skip(begin).Take(end - begin).Sum(v => (double)v * v) / (end - begin));
        double start = Rms(result.Data.Length / 8, result.Data.Length / 4);
        double end = Rms(result.Data.Length * 3 / 4, result.Data.Length * 7 / 8);
        Assert.AreEqual(start, end, 0.01, "Long overlapping source tails must not create a crescendo.");
        Assert.IsTrue(result.Data.Max(Math.Abs) <= 0.801f);
        Assert.IsTrue(first.Data.All(v => v == 0.25f));
        Assert.IsTrue(second.Data.All(v => v == -0.25f));
    }

    [TestMethod]
    public async Task InterleavedRenderHonorsSwingSourceSampleRateAndVolume()
    {
        float[] data = new float[4800];
        Array.Fill(data, 0.25f, 0, 2400);
        using var source = new AudioObj { Data = data, Channels = 1, SampleRate = 48000, Volume = 50 };
        using var result = await BreakbeatGenerator_V2.RenderBreakbeatAsync([new[] { true, true, false, false }], [source], 120, 4, 0.25f, interleaved: true);
        source.Volume = 100;
        using var fullVolume = await BreakbeatGenerator_V2.RenderBreakbeatAsync([new[] { true, true, false, false }], [source], 120, 4, 0.25f, interleaved: true);
        Assert.AreEqual(fullVolume.Data.Max(Math.Abs) / 2, result.Data.Max(Math.Abs), 0.0001f, "Volume is a percentage, not a 0..1 factor.");
        int silentFrame = (int)(0.075 * 44100);
        Assert.IsTrue(Math.Abs(result.Data[2 * silentFrame]) < 0.001f);
        int swungStart = (int)Math.Round(0.625 * 44100);
        Assert.IsTrue(Math.Abs(result.Data[2 * (swungStart - 100)]) < 0.001f);
        Assert.IsTrue(Math.Abs(result.Data[2 * (swungStart + 100)]) > 0.1f);
    }

    [TestMethod]
    public async Task ZeroVolumeStaysSilentAndUninterleavedMixingStillAllowsOverlap()
    {
        using var source = new AudioObj { Data = Enumerable.Repeat(0.25f, 44100).ToArray(), Channels = 1, SampleRate = 44100, Volume = 0 };
        using var silent = await BreakbeatGenerator_V2.RenderBreakbeatAsync([new[] { true, true, true, true }], [source], 120, 4, 0, interleaved: true);
        Assert.IsTrue(silent.Data.All(v => v == 0));
        source.Volume = 100;
        using var layered = await BreakbeatGenerator_V2.RenderBreakbeatAsync([new[] { true, true, true, true }], [source], 120, 4, 0);
        Assert.IsTrue(layered.Data[(int)(0.75 * 44100) * 2] > layered.Data[(int)(0.25 * 44100) * 2] * 1.5);
    }
}
