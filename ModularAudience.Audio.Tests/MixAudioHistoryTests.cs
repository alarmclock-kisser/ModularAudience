using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Arrangement;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class MixAudioHistoryTests
{
    [TestMethod]
    public void AudioUndoPreservesLaterNavigationViewSnapNamesAndTrackOrder()
    {
        using var mix = Example(); string before = MixAudioHistory.Capture(mix);
        mix.Lanes[0].Gain = 0.3; mix.Lanes[0].Clips[0].Start = -0.2;
        string edit = MixAudioHistory.Create(before, MixAudioHistory.Capture(mix));
        Guid changed = mix.Lanes[0].Id;
        mix.Lanes.Reverse(); mix.Lanes.Single(lane => lane.Id == changed).Name = "Renamed later";
        mix.Snap = true; mix.SnapDivision = 16; mix.Bpm = 145; mix.Visual.Background = "#123456";
        mix.Visual.HorizontalScrollSeconds = -2; mix.Visual.ScrollMode = MixScrollMode.Page;
        mix.LoopStart = 0.2; mix.LoopEnd = 0.4; mix.Loop = true;
        MixAudioHistory.Apply(mix, edit, undo: true);
        Assert.AreEqual(1d, mix.Lanes.Single(lane => lane.Id == changed).Gain);
        Assert.AreEqual(0d, mix.Lanes.Single(lane => lane.Id == changed).Clips[0].Start);
        Assert.AreEqual("Renamed later", mix.Lanes[1].Name); Assert.AreEqual(changed, mix.Lanes[1].Id);
        Assert.AreEqual(145d, mix.Bpm); Assert.IsTrue(mix.Snap); Assert.AreEqual(16, mix.SnapDivision);
        Assert.AreEqual("#123456", mix.Visual.Background); Assert.AreEqual(-2d, mix.Visual.HorizontalScrollSeconds);
        Assert.AreEqual(MixScrollMode.Page, mix.Visual.ScrollMode); Assert.IsTrue(mix.Loop); Assert.AreEqual(0.4, mix.LoopEnd);
        MixAudioHistory.Apply(mix, edit, undo: false); Assert.AreEqual(0.3, mix.Lanes[1].Gain); Assert.AreEqual(-0.2, mix.Lanes[1].Clips[0].Start);
        using var json = JsonDocument.Parse(edit);
        Assert.IsFalse(json.RootElement.GetProperty("Before").TryGetProperty("Visual", out _));
        Assert.IsFalse(json.RootElement.GetProperty("Before").TryGetProperty("Loop", out _));
    }

    [TestMethod]
    public void AudioKeyIgnoresViewportTransportMetadataAndGridOnlyBpmButDetectsEffectiveTempo()
    {
        using var mix = Example(); string key = MixAudioHistory.Key(mix);
        mix.Visual.PixelsPerSecond = 400; mix.Visual.Hue = 45; mix.Visual.FirstVisibleTrack = 1;
        mix.DisplayLaneHeight = 130; mix.Snap = true; mix.SnapDivision = 32; mix.Name = "Renamed";
        mix.Loop = true; mix.LoopStart = 1; mix.LoopEnd = 2; mix.AutoSave = true; mix.Bpm = 150; mix.Lanes.Reverse();
        mix.Lanes.Add(new() { Name = "Empty lane" });
        Assert.AreEqual(key, MixAudioHistory.Key(mix));
        mix.Lanes[0].Clips[0].TempoMode = ClipTempoMode.Varispeed;
        Assert.AreNotEqual(key, MixAudioHistory.Key(mix));
    }

    [TestMethod]
    public async Task ProjectAndAutosaveOmitTransientStateButKeepSnapAndExplicitViewSettings()
    {
        string path = Path.Combine(Path.GetTempPath(), "mix-state-" + Guid.NewGuid().ToString("N") + ".mamix");
        try
        {
            using var mix = Example(); mix.Snap = true; mix.SnapDivision = 16; mix.DisplayLaneHeight = 155;
            mix.Visual.Background = "#112233"; mix.Visual.PixelsPerSecond = 180;
            mix.Loop = true; mix.LoopStart = 0.2; mix.LoopEnd = 0.4; mix.Visual.HorizontalScrollSeconds = -3; mix.Visual.FirstVisibleTrack = 9; mix.Visual.ScrollMode = MixScrollMode.Follow;
            using var json = JsonDocument.Parse(mix.CaptureProject());
            Assert.IsTrue(json.RootElement.TryGetProperty("Loop", out _)); Assert.IsFalse(json.RootElement.TryGetProperty("LoopStart", out _));
            Assert.IsFalse(json.RootElement.GetProperty("Visual").TryGetProperty("HorizontalScrollSeconds", out _));
            Assert.IsTrue(json.RootElement.GetProperty("Visual").TryGetProperty("ScrollMode", out _));
            await mix.SaveAsync(path); long saved = new FileInfo(path).Length;
            // Navigation-only change (HorizontalScrollSeconds) must not change file size.
            mix.Visual.HorizontalScrollSeconds = 12; mix.LoopStart = 0;
            await mix.SaveAsync(path); Assert.AreEqual(saved, new FileInfo(path).Length, "A navigation-only repeat save must append no metadata record.");
            using var loaded = await MixArrangement.LoadAsync(path);
            Assert.IsTrue(loaded.Snap); Assert.AreEqual(16, loaded.SnapDivision); Assert.AreEqual(155, loaded.DisplayLaneHeight);
            Assert.AreEqual("#112233", loaded.Visual.Background); Assert.AreEqual(180d, loaded.Visual.PixelsPerSecond);
            Assert.AreEqual(0d, loaded.Visual.HorizontalScrollSeconds); Assert.AreEqual(0, loaded.Visual.FirstVisibleTrack);
            Assert.AreEqual(MixScrollMode.Follow, loaded.Visual.ScrollMode); Assert.IsTrue(loaded.Loop);
        }
        finally { File.Delete(path); }
    }
    private static MixArrangement Example()
    {
        var mix = new MixArrangement(); var source = new MixSource { Data = new float[8000], SampleRate = 8000, Channels = 1, Bpm = 100 };
        mix.Sources.Add(source.Id, source);
        for (int i = 0; i < 2; i++) { var lane = new MixLane { Name = "Lane " + i }; mix.Lanes.Add(lane); mix.AddClip(lane, source, i); }
        return mix;
    }
}
