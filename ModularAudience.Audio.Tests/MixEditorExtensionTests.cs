using System.Collections;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Arrangement;
using ModularAudience.Forms.Modules;

namespace ModularAudience.Audio.Tests;

[TestClass]
[DoNotParallelize]
public sealed class MixEditorExtensionTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object Field(object target, string name) => target.GetType().GetField(name, Private)!.GetValue(target)!;
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private)!.SetValue(target, value);
    private static object? Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private)!.Invoke(target, args);
    private static void Drain(Task task)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        while (!task.IsCompleted && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(2); }
        Assert.IsTrue(task.IsCompleted); task.GetAwaiter().GetResult();
    }
    private static MixSource Populate(MixTrackView mix)
    {
        var source = new MixSource { Data = Enumerable.Repeat(0.1f, 16000).ToArray(), SampleRate = 8000, Channels = 1 };
        mix.Arrangement.Sources.Add(source.Id, source);
        for (int i = 0; i < 2; i++)
        {
            var lane = new MixLane { Name = "Lane " + i }; mix.Arrangement.Lanes.Add(lane);
            mix.Arrangement.AddClip(lane, source, i);
        }
        Call(mix, "Changed"); return source;
    }
    [TestMethod]
    public async Task JournalReusesAudioRestoresViewAndRecoversInterruptedTail()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mamix");
        try
        {
            using var mix = new MixArrangement { AutoSave = true, DisplayLaneHeight = 147 };
            var source = new MixSource { Data = Enumerable.Repeat(0.2f, 200000).ToArray(), SampleRate = 16000, Channels = 1 };
            mix.Sources.Add(source.Id, source); var lane = new MixLane(); mix.Lanes.Add(lane); mix.AddClip(lane, source, 1);
            mix.Visual.HorizontalScrollSeconds = 12.5; mix.Visual.FirstVisibleTrack = 2; mix.Visual.PixelsPerSecond = 170;
            await mix.SaveAsync(path); long first = new FileInfo(path).Length;
            lane.Gain = 0.6; await mix.SaveAsync(path); long added = new FileInfo(path).Length - first;
            Assert.IsTrue(added < 10000, $"Metadata-only save rewrote too much: {added}");
            using (var file = new FileStream(path, FileMode.Append)) file.Write(new byte[] { 1, 200, 200, 200, 200, 0, 0, 0, 0, 7 });
            using var loaded = await MixArrangement.LoadAsync(path);
            Assert.AreEqual(0.6, loaded.Lanes[0].Gain); Assert.IsTrue(loaded.AutoSave);
            Assert.AreEqual(147, loaded.DisplayLaneHeight); Assert.AreEqual(12.5, loaded.Visual.HorizontalScrollSeconds);
            Assert.AreEqual(170d, loaded.Visual.PixelsPerSecond);
            CollectionAssert.AreEqual(source.Data, loaded.Sources[source.Id].Data);
            lane.Gain = 0.4; await mix.SaveAsync(path);
            using var recovered = await MixArrangement.LoadAsync(path); Assert.AreEqual(0.4, recovered.Lanes[0].Gain);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
    [STATestMethod]
    public void WholeClipAdditiveSelectionCopiesMultipleLanesAndPasteIsOneUndoStep()
    {
        using var mix = new MixTrackView(); Populate(mix);
        var first = mix.Arrangement.Lanes[0].Clips[0]; var second = mix.Arrangement.Lanes[1].Clips[0];
        Call(mix, "SelectWholeClip", first.Id, false); Call(mix, "SelectWholeClip", second.Id, true);
        Assert.HasCount(2, (HashSet<Guid>)Field(mix, "selectedClips"));
        Call(mix, "CopySelection"); Call(mix, "CopySelection");
        Assert.HasCount(1, (ICollection)Field(mix, "clipboardHistory"));
        Set(mix, "lastPointer", new Point(550, 40)); Call(mix, "StartPastePreview");
        Assert.AreEqual(2, mix.Arrangement.Lanes.Sum(l => l.Clips.Count));
        Call(mix, "PlacePastePreview");
        Assert.AreEqual(4, mix.Arrangement.Lanes.Sum(l => l.Clips.Count));
        Assert.AreEqual(4d, mix.Arrangement.Lanes[0].Clips[1].Start);
        Assert.AreEqual(5d, mix.Arrangement.Lanes[1].Clips[1].Start);
        Call(mix, "Undo"); Assert.AreEqual(2, mix.Arrangement.Lanes.Sum(l => l.Clips.Count));
        Call(mix, "Redo"); Assert.AreEqual(4, mix.Arrangement.Lanes.Sum(l => l.Clips.Count));
    }
    [STATestMethod]
    public void RangeCopyTrimsUnlessWholeClipModifierIsActive()
    {
        using var mix = new MixTrackView(); Populate(mix);
        var clip = mix.Arrangement.Lanes[0].Clips[0];
        Set(mix, "down", new Point(235, 80));
        Call(mix, "UpdateRangeSelection", new Point(280, 80));
        var parts = (IList)Call(mix, "SelectedParts")!;
        var copied = (MixClip)parts[0]!.GetType().GetProperty("Clip")!.GetValue(parts[0])!;
        Assert.AreEqual(0.5, copied.SourceOffset, 0.001); Assert.AreEqual(0.5, copied.SourceDuration, 0.001);
        Set(mix, "selectingWhole", true); Call(mix, "UpdateRangeSelection", new Point(280, 80));
        parts = (IList)Call(mix, "SelectedParts")!;
        copied = (MixClip)parts[0]!.GetType().GetProperty("Clip")!.GetValue(parts[0])!;
        Assert.AreEqual(0d, copied.SourceOffset); Assert.AreEqual(2d, copied.SourceDuration);
    }
    [STATestMethod]
    public void PasteTapShowsGhostAndEscapeCancellationDoesNotMutateArrangement()
    {
        using var mix = new MixTrackView(); Populate(mix);
        Call(mix, "SelectWholeClip", mix.Arrangement.Lanes[0].Clips[0].Id, false);
        Call(mix, "CopySelection"); Call(mix, "BeginPasteHold");
        Assert.IsTrue(((System.Windows.Forms.Timer)Field(mix, "pasteHoldTimer")).Enabled);
        Call(mix, "PasteKeyUp", mix, new KeyEventArgs(Keys.V));
        Assert.AreEqual(true, Field(mix, "pastePreview"));
        Assert.IsFalse(((System.Windows.Forms.Timer)Field(mix, "pasteHoldTimer")).Enabled);
        Call(mix, "CancelPastePreview"); Assert.AreEqual(false, Field(mix, "pastePreview"));
        Assert.AreEqual(2, mix.Arrangement.Lanes.Sum(l => l.Clips.Count));
    }
    [STATestMethod]
    public void RenderSelectionIncludesOnlySelectedClipsAndKeepsLaneGainAndTiming()
    {
        using var mix = new MixTrackView(); Populate(mix);
        var lane = mix.Arrangement.Lanes[0]; lane.Gain = 0.4;
        var first = lane.Clips[0];
        mix.Arrangement.AddClip(lane, mix.Arrangement.Sources[first.SourceId], 10);
        Call(mix, "SelectWholeClip", first.Id, false);
        Call(mix, "SelectWholeClip", mix.Arrangement.Lanes[1].Clips[0].Id, true);
        using var selected = (MixArrangement)Call(mix, "SelectionRenderSnapshot")!;
        Assert.HasCount(2, selected.Lanes); Assert.AreEqual(2, selected.Lanes.Sum(l => l.Clips.Count));
        Assert.AreEqual(0.4, selected.Lanes[0].Gain); Assert.AreEqual(1d, selected.Lanes[1].Clips[0].Start);
    }
    [STATestMethod]
    public void AddedTracksHaveEqualVisibleHeightAndCaretGesturesRestoreManualAnchor()
    {
        using var mix = new MixTrackView(); Populate(mix); mix.Show(); Application.DoEvents();
        int height = (int)Field(mix, "LaneHeight");
        Call(mix, "AddEmptyTrack"); Application.DoEvents();
        Assert.AreEqual(height, Field(mix, "LaneHeight"));
        var canvas = (Control)Field(mix, "canvas");
        Assert.IsTrue(canvas.Height >= 30 + height * 3);
        Set(mix, "manualCaret", 1.25); Set(mix, "caret", 9d); Call(mix, "JumpToManualCaret", false);
        Assert.AreEqual(1.25, Field(mix, "caret"));
        Call(mix, "JumpToManualCaret", true); Assert.AreEqual(0d, Field(mix, "manualCaret")); Assert.AreEqual(0d, Field(mix, "caret"));
    }
    [STATestMethod]
    public void AutoSaveEnablesAfterSaveAndPersistsEditsUndoAndView()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mamix");
        try
        {
            using var mix = new MixTrackView(); Populate(mix);
            var box = (CheckBox)Field(mix, "autoSave"); Assert.IsFalse(box.Enabled);
            Drain((Task)Call(mix, "SaveToPathAsync", path, CancellationToken.None)!); Assert.IsTrue(box.Enabled);
            box.Checked = true;
            mix.Arrangement.Lanes[0].Gain = 0.25; Call(mix, "Changed");
            ((System.Windows.Forms.Timer)Field(mix, "projectSaveTimer")).Stop();
            Drain((Task)Call(mix, "FlushProjectSaveAsync")!);
            using var loaded = MixArrangement.LoadAsync(path).GetAwaiter().GetResult();
            Assert.AreEqual(0.25, loaded.Lanes[0].Gain); Assert.IsTrue(loaded.AutoSave);
            Call(mix, "Undo"); Drain((Task)Call(mix, "FlushProjectSaveAsync")!);
            using var undone = MixArrangement.LoadAsync(path).GetAwaiter().GetResult(); Assert.AreEqual(1d, undone.Lanes[0].Gain);
            using var reopened = new MixTrackView(); Drain((Task)Call(reopened, "LoadFromPathAsync", path, CancellationToken.None)!);
            Assert.IsTrue(((CheckBox)Field(reopened, "autoSave")).Checked);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
