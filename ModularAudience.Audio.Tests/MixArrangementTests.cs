using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Arrangement;
using ModularAudience.Forms.Modules;
using System.Reflection;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class MixArrangementTests
{
    [STATestMethod]
    public void BatchTempoReplacementMapsEachSourceUsingItsOwnInitialBpmAndSupportsUndo()
    {
        using var audio = new AudioObj { Data = new float[44100], SampleRate = 44100, Channels = 1, Bpm = 100 };
        using var mix = new MixTrackView();
        mix.Arrangement.Bpm = 100;
        var first = mix.Arrangement.AddSource(audio);
        audio.Bpm = 80; var second = mix.Arrangement.AddSource(audio);
        var lane = new MixLane(); mix.Arrangement.Lanes.Add(lane);
        mix.Arrangement.AddClip(lane, first, 2); mix.Arrangement.AddClip(lane, second, 4);
        var replacements = new Dictionary<Guid, MixSource>
        {
            [first.Id] = new() { Data = new float[22050], SampleRate = 44100, Channels = 1, Bpm = 200 },
            [second.Id] = new() { Data = new float[17640], SampleRate = 44100, Channels = 1, Bpm = 200 }
        };
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(MixTrackView).GetMethod("Changed", flags)!.Invoke(mix, null);
        typeof(MixTrackView).GetMethod("ApplyPreparedTempo", flags)!.Invoke(mix, [100d, 200d, replacements]);
        typeof(MixTrackView).GetMethod("Changed", flags)!.Invoke(mix, null);
        Assert.AreEqual(0.5, lane.Clips[0].SourceDuration, 0.0001);
        Assert.AreEqual(0.4, lane.Clips[1].SourceDuration, 0.0001);
        Assert.AreEqual(1d, lane.Clips[0].Start); Assert.AreEqual(2d, lane.Clips[1].Start);
        typeof(MixTrackView).GetMethod("Undo", flags)!.Invoke(mix, null);
        Assert.AreEqual(100d, mix.Arrangement.Bpm);
        Assert.AreEqual(first.Id, mix.Arrangement.Lanes[0].Clips[0].SourceId);
    }

    [STATestMethod]
    public void ScrollModesFollowOrPageAndHandleLoopWrap()
    {
        using var mix = new MixTrackView();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(MixTrackView);
        var scroll = (HScrollBar)type.GetField("scroll", flags)!.GetValue(mix)!;
        var canvas = (Control)type.GetField("canvas", flags)!.GetValue(mix)!;
        _ = mix.Handle; _ = canvas.Handle;
        void Follow(double time)
        {
            type.GetField("caret", flags)!.SetValue(mix, time);
            type.GetMethod("FollowPlaybackCaret", flags)!.Invoke(mix, null);
        }
        Follow(20); Assert.AreEqual(0, scroll.Value);
        mix.Arrangement.Visual.ScrollMode = MixScrollMode.Page;
        Follow(1); Assert.AreEqual(0, scroll.Value);
        Follow(20); Assert.IsGreaterThan(0, scroll.Value);
        int page = scroll.Value; Follow(20.1); Assert.AreEqual(page, scroll.Value);
        Follow(0.5); Assert.AreEqual(0, scroll.Value);
        mix.Arrangement.Visual.ScrollMode = MixScrollMode.Follow;
        Follow(20); int first = scroll.Value;
        Follow(20.5); Assert.IsGreaterThan(first, scroll.Value);
    }

    [TestMethod]
    public async Task ConfiguredTimeStretchUsesChosenAlgorithmAndTargetTempoAtImport()
    {
        var source = new MixSource { Data = new float[8000], SampleRate = 16000, Channels = 1, Bpm = 100 };
        for (int i = 0; i < source.Data.Length; i++) source.Data[i] = (float)(0.2 * Math.Sin(2 * Math.PI * 220 * i / 16000));
        var settings = new ModularAudience.Audio.Processing.PlaylistStretchSettings(125, 0.8f, 1024, 0.5f,
            1, true, false, false, true, false, false);
        var prepared = await MixRenderer.PrepareSourceAsync(source, 125, ClipTempoMode.TimeStretch, stretchSettings: settings);
        Assert.AreEqual(125f, prepared.Bpm);
        Assert.IsTrue(prepared.Data.All(float.IsFinite));
        Assert.AreEqual(0.4, prepared.Duration, 0.05);
        Assert.IsTrue(prepared.Data.Any(v => Math.Abs(v) > 0.01));
    }

    [TestMethod]
    public async Task PreparedLanesReuseDspForLiveGainMuteSoloAndReordering()
    {
        using var audio = new AudioObj { Data = Enumerable.Repeat(0.2f, 4410).ToArray(), SampleRate = 44100, Channels = 1 };
        using var mix = new MixArrangement(); var source = mix.AddSource(audio);
        var first = new MixLane { Name = "First" }; var second = new MixLane { Name = "Second" };
        mix.Lanes.AddRange([first, second]); mix.AddClip(first, source, 0); mix.AddClip(second, source, 0);
        using var cache = new MixPreparationCache();
        await cache.PrepareAsync(mix);
        Assert.AreEqual(2L, cache.LaneRenders);
        var output = cache.Mix(mix);
        Assert.AreEqual(0.4f, output[1000], 0.001f);
        first.Gain = 0.5; second.Mute = true;
        Assert.IsTrue(cache.CanMix(mix));
        Assert.AreSame(output, cache.Mix(mix, output));
        Assert.AreEqual(0.1f, output[1000], 0.001f, "The playing buffer must reflect gain changes without DSP or restarting.");
        second.Mute = false; second.Solo = true;
        cache.Mix(mix, output);
        Assert.AreEqual(0.2f, output[1000], 0.001f);
        string before = MixPreparationCache.AudioKey(mix);
        mix.Lanes.Reverse();
        Assert.AreEqual(before, MixPreparationCache.AudioKey(mix));
        await cache.PrepareAsync(mix);
        Assert.AreEqual(2L, cache.LaneRenders, "Reorder/mixer settings must not repeat transformations.");
        second.Clips[0].FadeIn = 0.05;
        Assert.IsFalse(cache.CanMix(mix));
        await cache.PrepareAsync(mix);
        Assert.AreEqual(3L, cache.LaneRenders, "Only the edited track should be prepared again.");
    }

    [STATestMethod]
    public void TrackDragUsesReleasePositionAndWholeGestureIsOneUndoStep()
    {
        using var mix = new MixTrackView();
        mix.Arrangement.Lanes.AddRange([new() { Name = "One" }, new() { Name = "Two" }, new() { Name = "Three" }]);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(MixTrackView);
        object? Call(string name, params object[] args) => type.GetMethod(name, flags)!.Invoke(mix, args);
        Call("Changed");
        var canvas = (Control)type.GetField("canvas", flags)!.GetValue(mix)!;
        _ = mix.Handle; _ = canvas.Handle;
        Call("MouseDownTimeline", canvas, new MouseEventArgs(MouseButtons.Left, 1, 80, 45, 0));
        Call("MouseMoveTimeline", canvas, new MouseEventArgs(MouseButtons.Left, 0, 80, 180, 0));
        Call("MouseUpTimeline", canvas, new MouseEventArgs(MouseButtons.Left, 1, 80, 320, 0));
        CollectionAssert.AreEqual(new[] { "Two", "Three", "One" }, mix.Arrangement.Lanes.Select(l => l.Name).ToArray());
        Call("Undo");
        CollectionAssert.AreEqual(new[] { "One", "Two", "Three" }, mix.Arrangement.Lanes.Select(l => l.Name).ToArray());
    }

    [STATestMethod]
    public void TimeStretchConfigureDialogStartsWithSourceAndMixTempo()
    {
        using var audio = new AudioObj { Data = new float[4410], SampleRate = 44100, Channels = 1, Bpm = 95 };
        using var dialog = new ModularAudience.Forms.Modules.Dialogs.TimeStretchDialog(null, [audio]) { IsConfigureMode = true };
        dialog.SetConfigurationTempo(95, 128);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = dialog.GetType();
        Assert.AreEqual(95m, ((NumericUpDown)type.GetField("numericUpDown_initialBpm", flags)!.GetValue(dialog)!).Value);
        Assert.AreEqual(128m, ((NumericUpDown)type.GetField("numericUpDown_targetBpm", flags)!.GetValue(dialog)!).Value);
    }

    [TestMethod]
    [DataRow(ClipTempoMode.Varispeed)]
    [DataRow(ClipTempoMode.TimeStretch)]
    public async Task ImportPreparationBakesTempoBeforePlayback(ClipTempoMode mode)
    {
        var source = new MixSource { Data = new float[16000], SampleRate = 16000, Channels = 1, Bpm = 60 };
        for (int i = 0; i < source.Data.Length; i++) source.Data[i] = (float)(0.2 * Math.Sin(2 * Math.PI * 220 * i / 16000));
        var prepared = await MixRenderer.PrepareSourceAsync(source, 120, mode);
        Assert.AreEqual(0.5, prepared.Duration, 0.001);
        Assert.AreEqual(120f, prepared.Bpm);
        Assert.AreEqual(1d, source.Duration);
        using var mix = new MixArrangement { Bpm = 120 };
        mix.Sources.Add(prepared.Id, prepared); var lane = new MixLane(); mix.Lanes.Add(lane);
        var clip = mix.AddClip(lane, prepared, 0);
        Assert.AreEqual(ClipTempoMode.Original, clip.TempoMode, "Playback must use the already transformed source without stretching it again.");
        using var output = await MixRenderer.RenderAsync(mix);
        Assert.AreEqual(0.5, output.Duration.TotalSeconds, 0.001);
    }

    [STATestMethod]
    public void NativeMaximizeStyleSurvivesLaterUpdates()
    {
        using var view = new MixTrackView();
        _ = view.Handle;
        int style = GetWindowLong(view.Handle, -16);
        SetWindowLong(view.Handle, -16, style & ~0x10000);
        Assert.AreNotEqual(0, GetWindowLong(view.Handle, -16) & 0x10000);
        Assert.IsTrue(view.MaximizeBox);
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr window, int index, int style);

    [STATestMethod]
    public void WaveformDragSelectsAcrossTracksWithoutMovingClips()
    {
        using var audio = new AudioObj { Data = new float[44100 * 2], SampleRate = 44100, Channels = 1 };
        using var mix = new MixTrackView();
        var source = mix.Arrangement.AddSource(audio);
        for (int i = 0; i < 2; i++) { var lane = new MixLane(); mix.Arrangement.Lanes.Add(lane); mix.Arrangement.AddClip(lane, source, 0); }
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(MixTrackView);
        object? Call(string name, params object[] args) => type.GetMethod(name, flags)!.Invoke(mix, args);
        var canvas = (Control)type.GetField("canvas", flags)!.GetValue(mix)!;
        _ = mix.Handle; _ = canvas.Handle;
        Call("MouseDownTimeline", canvas, new MouseEventArgs(MouseButtons.Left, 1, 235, 85, 0));
        Assert.AreEqual(5, type.GetField("dragMode", flags)!.GetValue(mix));
        Call("MouseMoveTimeline", canvas, new MouseEventArgs(MouseButtons.Left, 0, 325, 185, 0));
        Call("MouseUpTimeline", canvas, new MouseEventArgs(MouseButtons.Left, 1, 325, 185, 0));
        Assert.AreEqual(0, type.GetField("selectionFirstRow", flags)!.GetValue(mix));
        Assert.AreEqual(1, type.GetField("selectionLastRow", flags)!.GetValue(mix));
        Assert.IsTrue(mix.Arrangement.Lanes.All(l => l.Clips[0].Start == 0));
        Call("ClearRangeSelection");
        Call("MouseDownTimeline", canvas, new MouseEventArgs(MouseButtons.Left, 1, 235, 40, 0));
        Assert.AreEqual(1, type.GetField("dragMode", flags)!.GetValue(mix));
        Call("MouseUpTimeline", canvas, new MouseEventArgs(MouseButtons.Left, 1, 235, 40, 0));
    }

    [STATestMethod]
    public void TrackReorderGainAndCrossTrackRangeDeletionAreUndoable()
    {
        using var audio = new AudioObj { Data = new float[44100 * 2], SampleRate = 44100, Channels = 1 };
        using var mix = new MixTrackView();
        var source = mix.Arrangement.AddSource(audio);
        var one = new MixLane { Name = "One" }; var two = new MixLane { Name = "Two" };
        mix.Arrangement.Lanes.AddRange([one, two]); mix.Arrangement.AddClip(one, source, 0); mix.Arrangement.AddClip(two, source, 0);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(MixTrackView);
        object? Call(string name, params object[] args) => type.GetMethod(name, flags)!.Invoke(mix, args);
        void Set(string name, object value) => type.GetField(name, flags)!.SetValue(mix, value);
        Call("Changed"); Call("MoveLane", one.Id, 2); Call("Changed");
        Assert.AreEqual("Two", mix.Arrangement.Lanes[0].Name);
        Call("Undo"); Assert.AreEqual("One", mix.Arrangement.Lanes[0].Name);
        var slider = (Rectangle)Call("LaneVolumeRect", 0)!;
        Call("SetLaneGain", 0, slider.Left + slider.Width / 4); Call("Changed");
        Assert.AreEqual(0.5, mix.Arrangement.Lanes[0].Gain, 0.02);
        Set("selectionStart", 0.5); Set("selectionEnd", 1.5); Set("selectionFirstRow", 0); Set("selectionLastRow", 1);
        Call("DeleteSelectedRange");
        foreach (var lane in mix.Arrangement.Lanes)
        {
            Assert.HasCount(2, lane.Clips);
            Assert.AreEqual(0.5, lane.Clips[0].SourceDuration, 0.0001);
            Assert.AreEqual(1.5, lane.Clips[1].Start, 0.0001);
        }
        Call("Undo"); Assert.IsTrue(mix.Arrangement.Lanes.All(l => l.Clips.Count == 1));
    }

    [TestMethod]
    public async Task LongerStereoClipRendersWithoutIntegerOverflow()
    {
        using var audio = new AudioObj { Data = new float[48000 * 3 * 2], SampleRate = 48000, Channels = 2, Bpm = 95 };
        for (int i = 0; i < audio.Data.Length; i += 2) { audio.Data[i] = 0.1f; audio.Data[i + 1] = -0.2f; }
        using var mix = new MixArrangement { Bpm = 95 };
        var source = mix.AddSource(audio); var lane = new MixLane(); mix.Lanes.Add(lane); mix.AddClip(lane, source, 0);
        using var rendered = await MixRenderer.RenderAsync(mix);
        Assert.AreEqual(3d, rendered.Duration.TotalSeconds, 0.001);
        Assert.AreEqual(44100 * 3 * 2, rendered.Data.Length);
        Assert.AreEqual(0.1f, rendered.Data[44100], 0.001f);
    }
    [TestMethod]
    public async Task EmbeddedProjectRestoresIndependentAudioClipsAndSettings()
    {
        using var audio = new AudioObj { Name = "Stereo", Data = [0.1f, -0.2f, 0.3f, -0.4f], SampleRate = 16000, Channels = 2, Bpm = 90 };
        using var mix = new MixArrangement { Bpm = 120, SnapDivision = 16 };
        mix.Visual.Clip = "#123456"; mix.Visual.Hue = 45; mix.DisplayLaneHeight = 140;
        var source = mix.AddSource(audio); var lane = new MixLane { Name = "Lead", Solo = true, Gain = 0.7, AutoCrossfade = true };
        mix.Lanes.Add(lane); var clip = mix.AddClip(lane, source, 1); clip.TempoMode = ClipTempoMode.Varispeed;
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mamix");
        try
        {
            await mix.SaveAsync(path); audio.Data[0] = 9;
            using var loaded = await MixArrangement.LoadAsync(path);
            Assert.AreEqual(120d, loaded.Bpm); Assert.AreEqual(16, loaded.SnapDivision);
            Assert.AreEqual("#123456", loaded.Visual.Clip); Assert.AreEqual(45d, loaded.Visual.Hue); Assert.AreEqual(140, loaded.DisplayLaneHeight);
            Assert.AreEqual("Lead", loaded.Lanes[0].Name); Assert.IsTrue(loaded.Lanes[0].Solo);
            CollectionAssert.AreEqual(new float[] { 0.1f, -0.2f, 0.3f, -0.4f }, loaded.Sources[source.Id].Data);
            Assert.AreEqual(ClipTempoMode.Varispeed, loaded.Lanes[0].Clips[0].TempoMode);
        }
        finally { File.Delete(path); }
    }
    [TestMethod]
    public async Task MixRendersOffsetsStereoAndMuteSoloWithoutMutatingSource()
    {
        using var sourceAudio = new AudioObj { Data = Enumerable.Range(0, 1600).SelectMany(_ => new[] { 0.2f, -0.4f }).ToArray(), SampleRate = 16000, Channels = 2 };
        using var mix = new MixArrangement(); var source = mix.AddSource(sourceAudio);
        var lane = new MixLane { Solo = true }; mix.Lanes.Add(lane); mix.AddClip(lane, source, 0.1);
        var muted = new MixLane { Solo = true, Mute = true }; mix.Lanes.Add(muted); mix.AddClip(muted, source, 0);
        using var output = await MixRenderer.RenderAsync(mix);
        Assert.AreEqual(0.2, output.Duration.TotalSeconds, 0.001);
        Assert.IsTrue(output.Data.Take(8000).All(v => Math.Abs(v) < 0.0001));
        int at = 6000 * 2;
        Assert.AreEqual(0.2f, output.Data[at], 0.005f); Assert.AreEqual(-0.4f, output.Data[at + 1], 0.005f);
        Assert.AreEqual(0.2f, sourceAudio.Data[0]);
    }
    [TestMethod]
    public void SnapAndSnapshotRestoreAreNonDestructive()
    {
        using var audio = new AudioObj { Data = new float[44100], SampleRate = 44100, Channels = 1 };
        using var mix = new MixArrangement { Bpm = 120, SnapDivision = 8 };
        var source = mix.AddSource(audio); var lane = new MixLane(); mix.Lanes.Add(lane); var clip = mix.AddClip(lane, source, 0);
        Assert.AreEqual(0.25, mix.SnapTime(0.3, true)); Assert.AreEqual(0.3, mix.SnapTime(0.3));
        string before = mix.Capture(); clip.Start = 2; clip.SourceDuration = 0.5;
        mix.Restore(before); Assert.AreEqual(0d, mix.Lanes[0].Clips[0].Start); Assert.AreEqual(1d, mix.Lanes[0].Clips[0].SourceDuration);
        Assert.AreEqual(44100, source.Data.Length);
    }
    [STATestMethod]
    public void MixWindowUndoRestoresSegmentAndUniformLaneHeightChangesWithResize()
    {
        using var window = new MixTrackView();
        using var audio = new AudioObj { Data = new float[4410], SampleRate = 44100, Channels = 1 };
        var source = window.Arrangement.AddSource(audio); var lane = new MixLane(); window.Arrangement.Lanes.Add(lane);
        var clip = window.Arrangement.AddClip(lane, source, 0);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Call(string method) => typeof(MixTrackView).GetMethod(method, flags)!.Invoke(window, null);
        Call("Changed"); clip.Start = 1; Call("Changed"); Call("Undo");
        Assert.AreEqual(0d, window.Arrangement.Lanes[0].Clips[0].Start);
        Call("Redo"); Assert.AreEqual(1d, window.Arrangement.Lanes[0].Clips[0].Start);
        int old = (int)typeof(MixTrackView).GetField("LaneHeight", flags)!.GetValue(window)!;
        window.Height += 50;
        Assert.IsGreaterThan(old, (int)typeof(MixTrackView).GetField("LaneHeight", flags)!.GetValue(window)!);
    }

    [TestMethod]
    [DataRow(ClipTempoMode.Varispeed)]
    [DataRow(ClipTempoMode.TimeStretch)]
    public async Task TempoMatchingChangesDurationAndTimeStretchPreservesPitch(ClipTempoMode mode)
    {
        using var audio = new AudioObj { Data = new float[16000], SampleRate = 16000, Channels = 1, Bpm = 60 };
        for (int i = 0; i < audio.Data.Length; i++) audio.Data[i] = (float)(0.2 * Math.Sin(2 * Math.PI * 220 * i / 16000));
        using var mix = new MixArrangement { Bpm = 120 };
        var source = mix.AddSource(audio); var lane = new MixLane(); mix.Lanes.Add(lane);
        mix.AddClip(lane, source, 0).TempoMode = mode;
        using var rendered = await MixRenderer.RenderAsync(mix);
        Assert.AreEqual(0.5, rendered.Duration.TotalSeconds, 0.001);
        double Amplitude(double frequency)
        {
            double real = 0, imaginary = 0; int begin = 4410, end = 17640;
            for (int i = begin; i < end; i++) { double phase = 2 * Math.PI * frequency * i / 44100; real += rendered.Data[2 * i] * Math.Cos(phase); imaginary += rendered.Data[2 * i] * Math.Sin(phase); }
            return Math.Sqrt(real * real + imaginary * imaginary) * 2 / (end - begin);
        }
        double wanted = mode == ClipTempoMode.TimeStretch ? 220 : 440;
        Assert.IsTrue(Amplitude(wanted) > 0.1, $"Expected {wanted} Hz after {mode}.");
    }

    [STATestMethod]
    public void JoinRemoveAndUndoRestoreOriginalTrackViewVisibility()
    {
        using var source = new AudioObj { Name = "Join test", Data = new float[4410], SampleRate = 44100, Channels = 1, Bpm = 95 };
        using var view = new TrackView(source);
        using var mix = new MixTrackView();
        mix.Join(view);
        Assert.AreEqual(95d, mix.Arrangement.Bpm, "The first joined track must set mix BPM without a stretch prompt.");
        Assert.AreEqual(ClipTempoMode.Original, mix.Arrangement.Lanes[0].Clips[0].TempoMode);
        Assert.IsFalse(view.Visible);
        Assert.HasCount(1, mix.Arrangement.Lanes);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(MixTrackView).GetMethod("RemoveLane", flags)!.Invoke(mix, [mix.Arrangement.Lanes[0].Id]);
        Assert.IsTrue(view.Visible);
        typeof(MixTrackView).GetMethod("Undo", flags)!.Invoke(mix, null);
        Assert.IsFalse(view.Visible);
        Assert.HasCount(1, mix.Arrangement.Lanes);
        mix.Dispose();
        Assert.IsTrue(view.Visible);
        view.Close();
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!view.IsDisposed && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(2); }
        Assert.IsTrue(view.IsDisposed);
    }

    [STATestMethod]
    public void TimelinePaintsMultipleChannelsAtCompactAndExpandedLaneHeights()
    {
        using var audio = new AudioObj { Data = Enumerable.Range(0, 4410).SelectMany(i => new[] { (float)Math.Sin(i * 0.1) * 0.2f, -0.1f }).ToArray(), SampleRate = 44100, Channels = 2 };
        using var mix = new MixTrackView();
        var source = mix.Arrangement.AddSource(audio);
        for (int i = 0; i < 3; i++) { var lane = new MixLane { Name = "Lane " + i }; mix.Arrangement.Lanes.Add(lane); mix.Arrangement.AddClip(lane, source, i * 0.05); }
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        _ = mix.Handle;
        var canvas = (Control)typeof(MixTrackView).GetField("canvas", flags)!.GetValue(mix)!;
        _ = canvas.Handle;
        foreach (int height in new[] { 32, 100, 160 })
        {
            typeof(MixTrackView).GetField("LaneHeight", flags)!.SetValue(mix, height);
            using var bitmap = new Bitmap(canvas.Width, canvas.Height);
            canvas.DrawToBitmap(bitmap, canvas.ClientRectangle);
        }
    }

    [STATestMethod]
    public void MixViewSettingsPreviewAndUndoRestoreColorsHeightAndGrid()
    {
        using var window = new MixTrackView();
        var options = new MixArrangement(); options.Restore(window.Arrangement.Capture());
        options.Visual.Background = "#112233"; options.Visual.Hue = 180; options.Visual.ShowGrid = false;
        options.DisplayLaneHeight = 160; options.Bpm = 95; options.SnapDivision = 16;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(MixTrackView);
        type.GetMethod("PreviewViewOptions", flags)!.Invoke(window, [options]);
        type.GetMethod("Changed", flags)!.Invoke(window, null);
        Assert.AreEqual("#112233", window.Arrangement.Visual.Background);
        Assert.AreEqual(160, window.Arrangement.DisplayLaneHeight); Assert.AreEqual(95d, window.Arrangement.Bpm);
        type.GetMethod("Undo", flags)!.Invoke(window, null);
        Assert.AreEqual("#1D2026", window.Arrangement.Visual.Background);
        Assert.AreEqual(100, window.Arrangement.DisplayLaneHeight); Assert.AreEqual(120d, window.Arrangement.Bpm);
    }

    [TestMethod]
    public async Task OverlapCrossfadeAvoidsDoublingAndManyChannelSourceFoldsToStereo()
    {
        using var audio = new AudioObj { Data = Enumerable.Range(0, 8000).SelectMany(_ => new[] { 0.1f, 0.2f, 0.3f, 0.4f }).ToArray(), SampleRate = 8000, Channels = 4 };
        using var mix = new MixArrangement(); var source = mix.AddSource(audio);
        var lane = new MixLane { AutoCrossfade = true }; mix.Lanes.Add(lane);
        mix.AddClip(lane, source, 0); mix.AddClip(lane, source, 0.5);
        using var output = await MixRenderer.RenderAsync(mix);
        int frame = (int)(0.75 * 44100);
        Assert.AreEqual(0.2f, output.Data[frame * 2], 0.01f);
        Assert.AreEqual(0.3f, output.Data[frame * 2 + 1], 0.01f);
        Assert.AreEqual(1.5, output.Duration.TotalSeconds, 0.001);
    }

    [STATestMethod]
    public void SegmentSplitCopyPasteAndUndoPreserveBoundaries()
    {
        using var audio = new AudioObj { Data = new float[44100], SampleRate = 44100, Channels = 1 };
        using var window = new MixTrackView();
        var source = window.Arrangement.AddSource(audio); var lane = new MixLane(); window.Arrangement.Lanes.Add(lane);
        var clip = window.Arrangement.AddClip(lane, source, 1);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(MixTrackView);
        void Set(string key, object value) => type.GetField(key, flags)!.SetValue(window, value);
        void Call(string method) => type.GetMethod(method, flags)!.Invoke(window, null);
        Set("selectedLane", lane.Id); Set("selectedClip", clip.Id); Set("caret", 1.5); Call("Changed"); Call("Split");
        Assert.HasCount(2, lane.Clips);
        Assert.AreEqual(0.5, lane.Clips[0].SourceDuration);
        Assert.AreEqual(0.5, lane.Clips[1].SourceOffset);
        Call("CopyClip"); Set("lastPointer", new Point(460, 50)); Call("PasteClip");
        Assert.HasCount(2, lane.Clips, "Paste begins a ghost preview and must not mutate until placed.");
        Call("PlacePastePreview"); Assert.HasCount(3, lane.Clips);
        Call("Undo"); Assert.HasCount(2, window.Arrangement.Lanes[0].Clips);
        Call("Undo"); Assert.HasCount(1, window.Arrangement.Lanes[0].Clips);
        Assert.AreEqual(1d, window.Arrangement.Lanes[0].Clips[0].SourceDuration);
    }

    [TestMethod]
    public async Task RenderCancellationAndInvalidProjectAreRejected()
    {
        using var audio = new AudioObj { Data = new float[44100], SampleRate = 44100, Channels = 1 };
        using var mix = new MixArrangement(); var source = mix.AddSource(audio);
        var lane = new MixLane(); mix.Lanes.Add(lane); var clip = mix.AddClip(lane, source, 0);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await MixRenderer.RenderAsync(mix, cancellation.Token));
        clip.SourceOffset = 2;
        Assert.Throws<InvalidDataException>(() => mix.Validate());
    }

    [STATestMethod]
    public void DraggingCollectionItemsAddsBoundedClipsToEmptyTracksAndUndoRemovesThem()
    {
        using var audio = new AudioObj { Name = "Dropped", Data = new float[4410], SampleRate = 44100, Channels = 1 };
        using var view = new MixTrackView();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(MixTrackView);
        var canvas = (Control)type.GetField("canvas", flags)!.GetValue(view)!;
        _ = view.Handle; _ = canvas.Handle;
        var data = new DataObject(); data.SetData(typeof(AudioObj[]), new[] { audio, audio });
        Point target = canvas.PointToScreen(new Point(280, 50));
        var drop = new DragEventArgs(data, 4, target.X, target.Y, DragDropEffects.Copy, DragDropEffects.Copy);
        type.GetMethod("DropAudio", flags)!.Invoke(view, [canvas, drop]);
        Assert.HasCount(2, view.Arrangement.Lanes);
        Assert.IsTrue(view.Arrangement.Lanes.All(l => l.Clips.Count == 1));
        Assert.AreEqual(1d, view.Arrangement.Lanes[0].Clips[0].Start);
        Assert.AreEqual(0.1, view.Arrangement.Lanes[0].Clips[0].SourceDuration, 0.0001);
        type.GetMethod("Undo", flags)!.Invoke(view, null);
        Assert.HasCount(0, view.Arrangement.Lanes);
    }
}
