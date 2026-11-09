using System.Diagnostics;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Arrangement;
using ModularAudience.Forms;
using ModularAudience.Forms.Modules;
using ModularAudience.Forms.Modules.Dialogs;

namespace ModularAudience.Audio.Tests;

[TestClass]
[DoNotParallelize]
public sealed class MixRevisionUiTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [STATestMethod]
    public void VolumeThumbTracksMouseBeforeReleaseAndAudioMixingIsDeferredOffTheUiThread()
    {
        using var fixture = new Fixture(); fixture.Ready(); var mix = fixture.Mix;
        AudioObj cached = Field<AudioObj>(mix, "cachedMix"); float[] oldBuffer = (float[])cached.Data.Clone();
        int history = History(mix); var canvas = Field<Control>(mix, "canvas");
        Rectangle slider = (Rectangle)Call(mix, "LaneVolumeRect", 0)!;
        int x = slider.Left + slider.Width * 3 / 4, y = slider.Top + slider.Height / 2;
        Call(mix, "MouseDownTimeline", canvas, new MouseEventArgs(MouseButtons.Left, 1, slider.Left + 5, y, 0));
        Call(mix, "MouseMoveTimeline", canvas, new MouseEventArgs(MouseButtons.Left, 0, x, y, 0));
        Assert.AreEqual((x - slider.Left) / (double)slider.Width * 2, mix.Arrangement.Lanes[0].Gain, 1e-8);
        using (var picture = new Bitmap(canvas.Width, canvas.Height))
        {
            canvas.DrawToBitmap(picture, canvas.ClientRectangle);
            Assert.AreEqual(Color.LightGray.ToArgb(), picture.GetPixel(x, y).ToArgb(), "Thumb must be painted at the mouse position while capture is still active.");
        }
        Assert.AreEqual(history, History(mix)); CollectionAssert.AreEqual(oldBuffer, cached.Data, "A mouse event must not synchronously remix or partially overwrite the audible buffer.");
        Call(mix, "MouseUpTimeline", canvas, new MouseEventArgs(MouseButtons.Left, 1, x, y, 0));
        Assert.AreEqual(history + 1, History(mix));
        fixture.Ready();
        float expected = (float)(0.2 * mix.Arrangement.Lanes[0].Gain + 0.1); // Second lane remains at unity.
        Assert.AreEqual(expected, Field<AudioObj>(mix, "cachedMix").Data[200], 1e-5f);
        Assert.AreNotSame(cached.Data, Field<AudioObj>(mix, "cachedMix").Data);
        Call(mix, "Undo"); Assert.AreEqual(1d, mix.Arrangement.Lanes[0].Gain);
    }

    [STATestMethod]
    public void ConfirmedStretchTargetBecomesGridTempoAndTrimmedClipsMapThroughTheirPristineOrigin()
    {
        using var fixture = new Fixture(); var mix = fixture.Mix;
        mix.Arrangement.Bpm = 100; mix.Arrangement.Lanes.RemoveAt(1);
        MixSource root = mix.Arrangement.Sources[mix.Arrangement.Lanes[0].Clips[0].SourceId]; root.Bpm = 100;
        MixClip clip = mix.Arrangement.Lanes[0].Clips[0]; clip.Start = 1; clip.SourceOffset = 0.2; clip.SourceDuration = 0.6;
        Call(mix, "Changed"); float[] original = (float[])root.Data.Clone();
        mix.ConfirmTempoChange = (_, _) => Task.FromResult(DialogResult.Yes);
        int dialogs = 0;
        mix.DialogPresenter = form =>
        {
            Assert.IsInstanceOfType<TimeStretchDialog>(form); var dialog = (TimeStretchDialog)form;
            var target = (NumericUpDown)typeof(TimeStretchDialog).GetField("numericUpDown_targetBpm", Private)!.GetValue(dialog)!;
            Assert.AreEqual(10m, target.Minimum); Assert.AreEqual(400m, target.Maximum);
            dialog.SetConfigurationTempo(100, dialogs++ == 0 ? 147.5f : 125f);
            SetChunk(dialog, 1024);
            typeof(TimeStretchDialog).GetMethod("SaveConfirmedSettings", Private)!.Invoke(dialog, [true]);
            return Task.FromResult(DialogResult.OK);
        };
        Drain((Task)Call(mix, "RequestMixTempoChangeAsync", 130d)!);
        Assert.AreEqual(147.5, mix.Arrangement.Bpm); Assert.AreEqual(147.5m, Field<NumericUpDown>(mix, "bpm").Value);
        MixSource first = mix.Arrangement.Sources[clip.SourceId]; Assert.AreEqual(root.Id, first.TempoOriginId);
        Assert.AreEqual(0.2 / 1.475, clip.SourceOffset, 1e-6); Assert.AreEqual(0.6 / 1.475, clip.SourceDuration, 1e-6);
        Drain((Task)Call(mix, "RequestMixTempoChangeAsync", 160d)!);
        Assert.AreEqual(125d, mix.Arrangement.Bpm); MixSource second = mix.Arrangement.Sources[clip.SourceId];
        Assert.AreEqual(root.Id, second.TempoOriginId); Assert.AreEqual(1.25, second.TempoOriginScale, 1e-7);
        Assert.AreEqual(0.16, clip.SourceOffset, 1e-6); Assert.AreEqual(0.48, clip.SourceDuration, 1e-6);
        CollectionAssert.AreEqual(original, root.Data);
        Call(mix, "Undo"); Assert.AreEqual(147.5, mix.Arrangement.Bpm);
        Call(mix, "Redo"); Assert.AreEqual(125d, mix.Arrangement.Bpm);
    }

    [STATestMethod]
    public void InitialTempoMatchedJoinRetainsThePreImportPcmAndMetadata()
    {
        using var fixture = new Fixture(); var mix = fixture.Mix;
        using var audio = new AudioObj { Name = "Original import", Bpm = 100, Channels = 1, SampleRate = 16000,
            Data = Enumerable.Range(0, 8000).Select(frame => (float)(0.2 * Math.Sin(2 * Math.PI * 220 * frame / 16000))).ToArray() };
        using var collection = new AudioCollectionView([audio]); using var view = new TrackView(audio, collection.AudioC);
        mix.DialogPresenter = form =>
        {
            if (form is TimeStretchDialog stretch)
            {
                stretch.SetConfigurationTempo(100, 120); SetChunk(stretch, 1024);
                typeof(TimeStretchDialog).GetMethod("SaveConfirmedSettings", Private)!.Invoke(stretch, [true]);
            }
            else Click(form.Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<Button>().Single(button => button.Text == "Time-stretch"));
            return Task.FromResult(DialogResult.OK);
        };
        try
        {
            Drain((Task)Call(mix, "JoinAsync", view)!);
            MixClip clip = mix.Arrangement.Lanes.Last().Clips.Single(); MixSource prepared = mix.Arrangement.Sources[clip.SourceId];
            Assert.IsNotNull(prepared.TempoOriginId);
            MixSource original = mix.Arrangement.Sources[prepared.TempoOriginId.Value];
            CollectionAssert.AreEqual(audio.Data, original.Data); Assert.AreNotSame(audio.Data, original.Data);
            Assert.AreEqual(100f, original.Bpm); Assert.AreEqual(16000, original.SampleRate); Assert.AreEqual(1, original.Channels);
            Assert.AreEqual(120f, prepared.Bpm); Assert.AreEqual(1.2, prepared.TempoOriginScale, 1e-8);
        }
        finally { mix.Dispose(); if (!view.IsDisposed) { view.Close(); WaitUntil(() => view.IsDisposed); } }
    }

    [STATestMethod]
    public void GridOnlyBpmChangeDoesNotRenderOriginalModeAudioOrCreateUndo()
    {
        using var fixture = new Fixture(); fixture.Ready(); var mix = fixture.Mix;
        int history = History(mix), assets = mix.Arrangement.Sources.Count;
        long renders = Field<MixPreparationCache>(mix, "preparationCache").LaneRenders;
        mix.ConfirmTempoChange = (_, _) => Task.FromResult(DialogResult.No);
        Drain((Task)Call(mix, "RequestMixTempoChangeAsync", 155d)!); fixture.Ready();
        Assert.AreEqual(155d, mix.Arrangement.Bpm); Assert.AreEqual(history, History(mix)); Assert.AreEqual(assets, mix.Arrangement.Sources.Count);
        Assert.AreEqual(renders, Field<MixPreparationCache>(mix, "preparationCache").LaneRenders);
    }

    [STATestMethod]
    public void NavigationAndTransportDoNotChangeHistoryDirtyStateOrAutosaveFile()
    {
        using var fixture = new Fixture(); var mix = fixture.Mix; string path = Path.Combine(fixture.Directory, "project.mamix");
        Drain((Task)Call(mix, "SaveToPathAsync", path, CancellationToken.None)!);
        Field<CheckBox>(mix, "autoSave").Checked = true; fixture.FlushSave();
        byte[] before = File.ReadAllBytes(path); int history = History(mix);
        Call(mix, "Zoom", 1.7); Field<HScrollBar>(mix, "scroll").Value = 0;
        Field<VScrollBar>(mix, "vertical").Value = 1;
        Click(Field<CheckBox>(mix, "autoScroll")); Field<CheckBox>(mix, "loop").Checked = true;
        Set(mix, "manualCaret", 0.5); Set(mix, "caret", 0.8); Call(mix, "JumpToManualCaret", false);
        Assert.AreEqual(history, History(mix)); Assert.IsFalse(Field<bool>(mix, "dirty"));
        Assert.IsNull(typeof(MixTrackView).GetField("pendingProjectSave", Private)!.GetValue(mix));
        Assert.IsFalse(Field<System.Windows.Forms.Timer>(mix, "projectSaveTimer").Enabled);
        fixture.FlushSave(); CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
        Assert.AreEqual(90d, mix.Arrangement.Visual.PixelsPerSecond, "Interactive zoom is not the explicitly saved View Settings zoom.");
        Field<CheckBox>(mix, "snap").Checked = true;
        Assert.AreEqual(history, History(mix)); fixture.FlushSave();
        using MixArrangement loaded = MixArrangement.LoadAsync(path).GetAwaiter().GetResult();
        Assert.IsTrue(loaded.Snap); Assert.IsFalse(loaded.Loop); Assert.AreEqual(MixScrollMode.None, loaded.Visual.ScrollMode);
        Assert.AreEqual(0d, loaded.Visual.HorizontalScrollSeconds); Assert.AreEqual(0, loaded.Visual.FirstVisibleTrack);
    }

    [STATestMethod]
    public void UndoAnAudioEditKeepsLaterExplicitViewSettingsSnapAndNavigation()
    {
        using var fixture = new Fixture(); var mix = fixture.Mix;
        mix.Arrangement.Lanes[0].Gain = 0.25; Call(mix, "Changed"); int history = History(mix);
        var options = new MixArrangement(); options.Restore(mix.Arrangement.Capture()); options.Visual.Background = "#123456";
        options.Visual.PixelsPerSecond = 250; options.DisplayLaneHeight = 145;
        Call(mix, "PreviewViewOptions", options); Call(mix, "Changed"); Field<CheckBox>(mix, "snap").Checked = true;
        Field<HScrollBar>(mix, "scroll").Value = 0;
        Assert.AreEqual(history, History(mix)); Call(mix, "Undo");
        Assert.AreEqual(1d, mix.Arrangement.Lanes[0].Gain); Assert.AreEqual("#123456", mix.Arrangement.Visual.Background);
        Assert.AreEqual(145, mix.Arrangement.DisplayLaneHeight); Assert.AreEqual(250d, Field<double>(mix, "pixelsPerSecond"));
        Assert.IsTrue(mix.Arrangement.Snap); Assert.AreEqual(0, Field<HScrollBar>(mix, "scroll").Value);
    }

    [STATestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ClipDragCrossesZeroFreelyOrOnGridAndRemainsAudioUndoable(bool snap)
    {
        using var fixture = new Fixture(); var mix = fixture.Mix; var canvas = Field<Control>(mix, "canvas");
        MixClip clip = mix.Arrangement.Lanes[0].Clips[0]; clip.Start = 0.1; mix.Arrangement.Snap = snap; Call(mix, "Changed");
        Call(mix, "MouseDownTimeline", canvas, new MouseEventArgs(MouseButtons.Left, 1, 210, 40, 0));
        Call(mix, "MouseMoveTimeline", canvas, new MouseEventArgs(MouseButtons.Left, 0, 170, 40, 0));
        Call(mix, "MouseUpTimeline", canvas, new MouseEventArgs(MouseButtons.Left, 1, 170, 40, 0));
        Assert.IsLessThan(0d, clip.Start); if (snap) Assert.AreEqual(Math.Round(clip.Start / 0.25), clip.Start / 0.25, 1e-7);
        double negative = clip.Start; Assert.AreEqual(0, Field<HScrollBar>(mix, "scroll").Value);
        Call(mix, "Undo"); Assert.AreEqual(0.1, mix.Arrangement.Lanes[0].Clips[0].Start, 1e-7);
        Call(mix, "Redo"); Assert.AreEqual(negative, mix.Arrangement.Lanes[0].Clips[0].Start, 1e-7); mix.Arrangement.Validate();
    }

    [STATestMethod]
    public void ImportProjectMenuUsesTheConfiguredWorkingDirectoryAndMamixOnlyFilter()
    {
        using var dialog = MixProjectMenu.CreateOpenDialog();
        Assert.AreEqual(Path.Combine(AudioStoragePaths.WorkingDirectory, "_Projects"), dialog.InitialDirectory);
        Assert.AreEqual("ModularAudience mix (*.mamix)|*.mamix", dialog.Filter); Assert.IsFalse(dialog.Multiselect); Assert.IsTrue(dialog.CheckFileExists);
        int opened = 0; using var item = MixProjectMenu.CreateItem(() => { opened++; return Task.CompletedTask; }); item.PerformClick();
        Assert.AreEqual("Open Project (.mamix)", item.Text); Assert.AreEqual(1, opened);
    }

    [STATestMethod]
    public void InvalidProjectShowsAMixLocalErrorAndWaitsForAcknowledgementWithoutReplacingAudio()
    {
        using var fixture = new Fixture(); var mix = fixture.Mix;
        string invalid = Path.Combine(fixture.Directory, "invalid.mamix"); File.WriteAllText(invalid, "not a mix project");
        string before = mix.Arrangement.Capture();
        mix.FileDialogPresenter = dialog => { dialog.FileName = invalid; return Task.FromResult(DialogResult.OK); };
        Task opening = mix.OpenProjectAsync();
        WaitUntil(() => mix.WindowGroup.Windows.Any(form => form.Name == "MixQuestionDialog" && form.Visible));
        Assert.IsFalse(opening.IsCompleted); Assert.IsTrue(mix.Enabled); Assert.AreEqual(before, mix.Arrangement.Capture());
        Form error = mix.WindowGroup.Windows.Single(form => form.Name == "MixQuestionDialog");
        Button accept = error.Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<Button>().Single(button => button.Text == "OK");
        accept.PerformClick(); Drain(opening); Assert.IsFalse(mix.HasLoadedProject); Assert.IsFalse(mix.WindowGroup.IsBlocked);
        Assert.AreEqual(before, mix.Arrangement.Capture());
    }

    [STATestMethod]
    [DataRow(true, false)]
    [DataRow(false, false)]
    [DataRow(true, true)]
    public void ClosingMixRestoresJoinedTrackOnlyWhenItsOriginalCollectionAndSourceStillExist(bool collectionAlive, bool removeSource)
    {
        using var context = new WinFormsTestContext();
        using var audio = new AudioObj { Data = new float[4410], Channels = 1, SampleRate = 44100, Bpm = 120 };
        using var collection = new AudioCollectionView([audio]); using var view = new TrackView(audio, collection.AudioC);
        using var mix = new MixTrackView(); mix.Join(view); Assert.IsFalse(view.Visible);
        if (removeSource) collection.AudioC.Audios.Remove(audio);
        if (!collectionAlive) collection.Close();
        mix.Dispose();
        if (collectionAlive && !removeSource) Assert.IsTrue(view.Visible);
        else
        {
            Assert.IsFalse(view.Visible);
            WaitUntil(() => view.IsDisposed); Assert.IsTrue(view.IsDisposed);
        }
        if (!view.IsDisposed) { view.Close(); WaitUntil(() => view.IsDisposed); }
    }

    private static void SetChunk(TimeStretchDialog dialog, int size)
    { var number = (NumericUpDown)typeof(TimeStretchDialog).GetField("numericUpDown_chunkSize", Private)!.GetValue(dialog)!; number.Tag = size * 2; number.Value = size; }
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private)!.GetValue(target)!;
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private)!.SetValue(target, value);
    private static object? Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private)!.Invoke(target, args);
    private static int History(MixTrackView mix) => Field<Stack<string>>(mix, "undo").Count;
    private static void Click(Control control) => typeof(Control).GetMethod("OnClick", Private)!.Invoke(control, [EventArgs.Empty]);
    private static void Drain(Task task)
    {
        SynchronizationContext? context = SynchronizationContext.Current; var watch = Stopwatch.StartNew();
        while (!task.IsCompleted && watch.Elapsed < TimeSpan.FromSeconds(25)) { Application.DoEvents(); SynchronizationContext.SetSynchronizationContext(context); Thread.Sleep(1); }
        Assert.IsTrue(task.IsCompleted); task.GetAwaiter().GetResult(); Application.DoEvents(); SynchronizationContext.SetSynchronizationContext(context);
    }
    private static void WaitUntil(Func<bool> done)
    {
        SynchronizationContext? context = SynchronizationContext.Current; var watch = Stopwatch.StartNew();
        while (!done() && watch.Elapsed < TimeSpan.FromSeconds(5)) { Application.DoEvents(); SynchronizationContext.SetSynchronizationContext(context); Thread.Sleep(1); }
        Assert.IsTrue(done());
    }
    private sealed class Fixture : IDisposable
    {
        private readonly WinFormsTestContext context = new();
        internal MixTrackView Mix { get; } = new();
        internal string Directory { get; } = Path.Combine(Path.GetTempPath(), "mix-revision-ui-" + Guid.NewGuid().ToString("N"));
        internal Fixture()
        {
            System.IO.Directory.CreateDirectory(this.Directory);
            for (int i = 0; i < 2; i++)
            {
                var source = new MixSource { Data = Enumerable.Range(0, 44100).SelectMany(_ => new[] { 0.2f / (i + 1), -0.1f / (i + 1) }).ToArray(), Channels = 2, SampleRate = 44100, Bpm = 120 };
                this.Mix.Arrangement.Sources.Add(source.Id, source); var lane = new MixLane { Name = "Lane " + i }; this.Mix.Arrangement.Lanes.Add(lane); this.Mix.Arrangement.AddClip(lane, source, 0);
            }
            Call(this.Mix, "Changed"); this.Mix.Show();
        }
        internal void Ready() { Drain(Field<Task>(this.Mix, "liveUpdateWork")); Drain((Task)Call(this.Mix, "FlushLiveGainMixAsync")!); }
        internal void FlushSave() { Field<System.Windows.Forms.Timer>(this.Mix, "projectSaveTimer").Stop(); Drain((Task)Call(this.Mix, "FlushProjectSaveAsync")!); }
        public void Dispose() { this.Mix.Dispose(); this.context.Dispose(); System.IO.Directory.Delete(this.Directory, true); }
    }
}
