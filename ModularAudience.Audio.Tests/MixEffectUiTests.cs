using System.Diagnostics;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Arrangement;
using ModularAudience.Audio.Effects;
using ModularAudience.Audio.Effects.Curves;
using ModularAudience.Forms.Effects;
using ModularAudience.Forms.Modules;
using ModularAudience.Forms.Modules.Dialogs;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class MixEffectUiTests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

    [STATestMethod]
    public void EmptySpaceAndAudioTargets_OpenDistinctMenusWithIdenticalSharedEffectHierarchy()
    {
        using var fixture = new Fixture();
        using ContextMenuStrip general = fixture.Mix.CreateTimelineContextMenu(new Point(700, 300));
        Assert.AreEqual("mixGeneralContextMenu", general.Name);
        Assert.IsFalse(general.Items.OfType<ToolStripMenuItem>().Any(item => item.Name == "menuItem_applyEffects"));
        Assert.IsTrue(general.Items.OfType<ToolStripMenuItem>().Any(item => item.Text == "View Settings…"));
        using ContextMenuStrip audio = fixture.Mix.CreateTimelineContextMenu(new Point(192, 50));
        Assert.AreEqual("mixAudioContextMenu", audio.Name);
        ToolStripMenuItem effects = audio.Items.OfType<ToolStripMenuItem>().Single(item => item.Name == "menuItem_applyEffects");
        using ToolStripMenuItem reference = EffectMenuFactory.CreateApplyMenu(_ => { });
        CollectionAssert.AreEqual(MenuNames(reference), MenuNames(effects));
        Assert.AreEqual(37, effects.DropDownItems.OfType<ToolStripMenuItem>().Sum(group => group.DropDownItems.Count));
        audio.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "Clear selection").PerformClick();
        using ContextMenuStrip cleared = fixture.Mix.CreateTimelineContextMenu(new Point(700, 300));
        Assert.AreEqual("mixGeneralContextMenu", cleared.Name);
    }

    [STATestMethod]
    public void RightClickWithRangeOrMultiselection_DoesNotCollapseOrRetargetIt()
    {
        using var fixture = new Fixture();
        Set(fixture.Mix, "selectionStart", 0.02); Set(fixture.Mix, "selectionEnd", 0.14);
        Set(fixture.Mix, "selectionFirstRow", 0); Set(fixture.Mix, "selectionLastRow", 1);
        using ContextMenuStrip rangeMenu = fixture.Mix.CreateTimelineContextMenu(new Point(700, 300));
        Assert.AreEqual("mixAudioContextMenu", rangeMenu.Name);
        MixEffectRegion[] range = fixture.Mix.CaptureEffectRegions(); Assert.AreEqual(2, range.Length);
        Assert.IsTrue(range.All(region => region.Start == 0.02 && region.End == 0.14));
        Call(fixture.Mix, "ClearAudioEffectSelection");
        Call(fixture.Mix, "SelectWholeClip", fixture.First.Id, false); Call(fixture.Mix, "SelectWholeClip", fixture.Second.Id, true);
        using ContextMenuStrip multiple = fixture.Mix.CreateTimelineContextMenu(new Point(700, 300));
        MixEffectRegion[] clips = fixture.Mix.CaptureEffectRegions();
        Assert.AreEqual(2, clips.Length); Assert.AreEqual(2, clips.Sum(region => region.ClipIds.Count));
        CollectionAssert.AreEquivalent(new[] { fixture.First.Id, fixture.Second.Id }, clips.SelectMany(region => region.ClipIds).ToArray());
    }

    [STATestMethod]
    public void RangeSelection_IncludesEmptySelectedLanesAndExactSilenceBoundaries()
    {
        using var fixture = new Fixture(); fixture.Mix.Arrangement.Lanes[1].Clips.Clear();
        Set(fixture.Mix, "selectionStart", 0.02); Set(fixture.Mix, "selectionEnd", 0.18);
        Set(fixture.Mix, "selectionFirstRow", 0); Set(fixture.Mix, "selectionLastRow", 1);
        MixEffectRegion[] regions = fixture.Mix.CaptureEffectRegions();
        Assert.AreEqual(2, regions.Length); Assert.AreEqual(0, regions[1].ClipIds.Count);
        Assert.AreEqual(0.18, regions[1].End); Assert.AreEqual(0.02, regions[1].Start);
        using ContextMenuStrip menu = fixture.Mix.CreateTimelineContextMenu(new Point(700, 300));
        Assert.AreEqual("mixAudioContextMenu", menu.Name);
    }

    [STATestMethod]
    public void GainDialog_IsModelessSharedPresetPreviewAndOneMixUndoRedoStepForMultipleLanes()
    {
        using var fixture = new Fixture(); fixture.SelectBoth(); fixture.InstallDialogFactory();
        int history = History(fixture.Mix); string original = fixture.Mix.Arrangement.Capture(); int assets = fixture.Mix.Arrangement.Sources.Count;
        Drain(fixture.Mix.OpenMixEffectAsync(AudioEffectKind.Gain));
        Assert.IsNotNull(fixture.Opened); Assert.IsNotNull(fixture.Editor);
        Assert.IsFalse(fixture.Opened.Modal); Assert.AreSame(fixture.Mix, fixture.Opened.Owner);
        fixture.Editor.SetSettings(fixture.Editor.CurrentSettings.With(("GainDb", -6.020599913279624)));
        Drain(fixture.Editor.PrehearAsync());
        Assert.AreEqual(original, fixture.Mix.Arrangement.Capture()); Assert.AreEqual(history, History(fixture.Mix));
        Assert.IsTrue(fixture.Preview.IsPlaying); Assert.AreEqual(2, fixture.Preview.Channels);
        Assert.AreEqual(44100, fixture.Preview.Rate);
        Drain(fixture.Editor.AcceptEffectAsync());
        Assert.AreEqual(history + 1, History(fixture.Mix)); Assert.AreEqual(1, fixture.RenderCount);
        Assert.AreEqual(assets + 2, fixture.Mix.Arrangement.Sources.Count);
        foreach (MixLane lane in fixture.Mix.Arrangement.Lanes)
        {
            MixClip clip = lane.Clips.Single(); Assert.IsTrue(clip.HasRenderedEnvelope);
            Assert.AreEqual(0.1f, fixture.Mix.Arrangement.Sources[clip.SourceId].Data[100], 1e-6f);
        }
        fixture.Key(Keys.Control | Keys.Z); Assert.AreEqual(original, fixture.Mix.Arrangement.Capture());
        fixture.Key(Keys.Control | Keys.Y); Assert.IsTrue(fixture.Mix.Arrangement.Lanes.All(lane => lane.Clips.Single().HasRenderedEnvelope));
        Assert.AreEqual(0.2f, fixture.Original.Data[100]);
    }

    [STATestMethod]
    public void TimeStretchDialog_AppliesRangeIncludingGapAndRipplesFollowingClipWithCtrlZY()
    {
        using var fixture = new Fixture(); var source = fixture.Mix.Arrangement.Sources[fixture.First.SourceId];
        MixClip later = fixture.Mix.Arrangement.AddClip(fixture.Mix.Arrangement.Lanes[0], source, 0.3); Call(fixture.Mix, "Changed");
        Set(fixture.Mix, "selectionStart", 0.02); Set(fixture.Mix, "selectionEnd", 0.14);
        Set(fixture.Mix, "selectionFirstRow", 0); Set(fixture.Mix, "selectionLastRow", 0);
        fixture.InstallDialogFactory(); int history = History(fixture.Mix);
        Drain(fixture.Mix.OpenMixEffectAsync(AudioEffectKind.TimeStretch)); Assert.IsNotNull(fixture.Editor);
        fixture.Editor.SetSettings(fixture.Editor.CurrentSettings.With(("Factor", 1.5)));
        Drain(fixture.Editor.AcceptEffectAsync());
        Assert.AreEqual(history + 1, History(fixture.Mix));
        Assert.AreEqual(0.36, fixture.Mix.Arrangement.Lanes[0].Clips.Single(clip => clip.Id == later.Id).Start, 1e-7);
        Assert.AreEqual(0.2, (double)Get(fixture.Mix, "selectionEnd")!, 1e-7);
        Assert.AreEqual(0d, fixture.Mix.Arrangement.Lanes[1].Clips.Single().Start);
        fixture.Key(Keys.Control | Keys.Z);
        Assert.AreEqual(0.3, fixture.Mix.Arrangement.Lanes[0].Clips.Single(clip => clip.Id == later.Id).Start, 1e-7);
        fixture.Key(Keys.Control | Keys.Y);
        Assert.AreEqual(0.36, fixture.Mix.Arrangement.Lanes[0].Clips.Single(clip => clip.Id == later.Id).Start, 1e-7);
    }

    [STATestMethod]
    [DataRow(AudioEffectKind.TimeStretch, "Factor", 0.5)]
    [DataRow(AudioEffectKind.VarispeedShift, "Speed", 2d)]
    public void CurvedSpeedEffects_PreviewAndCommitBothLanesOnceAndSupportMixCtrlZY(AudioEffectKind effect, string key, double target)
    {
        using var fixture = new Fixture(); fixture.SelectBoth(); fixture.InstallDialogFactory();
        int history = History(fixture.Mix); string before = fixture.Mix.Arrangement.Capture();
        string beforeAudio = MixAudioHistory.Capture(fixture.Mix.Arrangement);
        Drain(fixture.Mix.OpenMixEffectAsync(effect)); Assert.IsNotNull(fixture.Editor);
        fixture.Editor.SetSettings(fixture.Editor.CurrentSettings.With((key, target)) with
            { Automation = new() { Parameters = new() { [key] = new() { StartValue = 1 } }, Timing = new() { Scope = EffectCurveScope.AcrossSelection } } });
        Drain(fixture.Editor.PrehearAsync()); Assert.AreEqual(before, fixture.Mix.Arrangement.Capture()); Assert.AreEqual(history, History(fixture.Mix));
        Drain(fixture.Editor.AcceptEffectAsync()); Assert.AreEqual(history + 1, History(fixture.Mix)); Assert.AreEqual(1, fixture.RenderCount);
        foreach (MixLane lane in fixture.Mix.Arrangement.Lanes)
        {
            MixSource source = fixture.Mix.Arrangement.Sources[lane.Clips.Single().SourceId];
            Assert.AreEqual(Math.Round(4410 * Math.Log(2)) * 2, source.Data.Length, 2);
            Assert.IsTrue(source.TempoRecipe!.Effect!.Automation!.Parameters[key].Enabled);
        }
        string afterAudio = MixAudioHistory.Capture(fixture.Mix.Arrangement); double mappedLoopEnd = fixture.Mix.Arrangement.LoopEnd;
        fixture.Key(Keys.Control | Keys.Z); Assert.AreEqual(beforeAudio, MixAudioHistory.Capture(fixture.Mix.Arrangement));
        Assert.AreEqual(mappedLoopEnd, fixture.Mix.Arrangement.LoopEnd, "Mix history intentionally preserves current navigation/loop preferences.");
        fixture.Key(Keys.Control | Keys.Y); Assert.AreEqual(afterAudio, MixAudioHistory.Capture(fixture.Mix.Arrangement)); Assert.AreEqual(0.2f, fixture.Original.Data[100]);
    }

    [STATestMethod]
    public void NestedCurveEditor_UsesMixLocalOwnershipBlockingMinimizeRestoreAndClose()
    {
        using var fixture = new Fixture(); fixture.SelectBoth(); fixture.InstallDialogFactory();
        Drain(fixture.Mix.OpenMixEffectAsync(AudioEffectKind.Gain)); Assert.IsNotNull(fixture.Editor); Assert.IsNotNull(fixture.Opened);
        var group = (MixWindowGroup)Get(fixture.Mix, "windowGroup")!;
        Descendants(fixture.Editor).OfType<Button>().Single(button => button.Name == "GainDbEditCurve").PerformClick(); Application.DoEvents();
        var curve = group.Windows.OfType<CurveEditorDialog>().Single();
        Assert.AreSame(fixture.Opened, curve.Owner); Assert.IsFalse(curve.Modal); Assert.IsFalse(curve.ShowInTaskbar); Assert.IsFalse(curve.MinimizeBox);
        Assert.IsFalse(fixture.Opened.Enabled); Assert.IsTrue(curve.Enabled);
        fixture.Mix.WindowState = FormWindowState.Minimized; group.SynchronizeWindowState(); Application.DoEvents(); Assert.IsFalse(curve.Visible);
        fixture.Mix.WindowState = FormWindowState.Normal; group.SynchronizeWindowState(); Application.DoEvents(); Assert.IsTrue(curve.Visible); Assert.IsTrue(curve.Enabled);
        curve.AcceptCurve(); Application.DoEvents(); Assert.IsTrue(fixture.Opened.Enabled); Assert.AreEqual(1, group.Windows.Count);
        Assert.IsTrue(fixture.Editor.CurrentSettings.Automation!.Parameters["GainDb"].Enabled);
        Descendants(fixture.Editor).OfType<Button>().Single(button => button.Name == "GainDbEditCurve").PerformClick(); Application.DoEvents();
        var next = group.Windows.OfType<CurveEditorDialog>().Single(); fixture.Mix.Dispose(); Application.DoEvents(); Assert.IsTrue(next.IsDisposed);
        Assert.AreEqual(0.2f, fixture.Original.Data[100]);
    }

    [STATestMethod]
    public void AutoTune_UsesTheSameTypedDialogAndCommitsLanesWithoutTouchingOriginalAssets()
    {
        using var fixture = new Fixture(); fixture.SelectBoth(); AutoTuneDialog? dialog = null;
        fixture.Mix.CreateMixAutoTuneDialog = context => dialog = new AutoTuneDialog(context,
            new AutoTunePresetStore(Path.Combine(fixture.Directory, "AutoTunePresets.json")), fixture.Preview);
        int history = History(fixture.Mix);
        Drain(fixture.Mix.OpenMixEffectAsync(AudioEffectKind.AutoTune)); Assert.IsNotNull(dialog);
        Assert.IsFalse(dialog.Modal); Assert.AreSame(fixture.Mix, dialog.Owner);
        dialog.SetSettings(dialog.CurrentSettings with { IntensityPercent = 0, OutputGainDb = -6.020599913279624 });
        Drain(dialog.PrehearAsync()); Assert.AreEqual(history, History(fixture.Mix));
        Drain(dialog.AcceptEffectAsync()); Assert.AreEqual(history + 1, History(fixture.Mix));
        Assert.IsTrue(fixture.Mix.Arrangement.Lanes.All(lane => lane.Clips.Single().HasRenderedEnvelope));
        Assert.AreEqual(0.2f, fixture.Original.Data[100]); fixture.Key(Keys.Control | Keys.Z);
        Assert.AreEqual(fixture.First.Id, fixture.Mix.Arrangement.Lanes[0].Clips.Single().Id);
    }

    [STATestMethod]
    public void ImmediateMonoAction_HasNoDialogAndRemainsUndoableWithOriginalChannelsIntact()
    {
        using var fixture = new Fixture(); fixture.SelectBoth(); int history = History(fixture.Mix);
        fixture.Mix.CreateMixEffectDialog = _ => throw new AssertFailedException("Mono must be an immediate action.");
        Drain(fixture.Mix.OpenMixEffectAsync(AudioEffectKind.ConvertToMono));
        Assert.AreEqual(history + 1, History(fixture.Mix));
        Assert.IsTrue(fixture.Mix.Arrangement.Lanes.All(lane => fixture.Mix.Arrangement.Sources[lane.Clips.Single().SourceId].Channels == 1));
        Assert.AreEqual(2, fixture.Original.Channels);
        fixture.Key(Keys.Control | Keys.Z);
        Assert.IsTrue(fixture.Mix.Arrangement.Lanes.All(lane => fixture.Mix.Arrangement.Sources[lane.Clips.Single().SourceId].Channels == 2));
        fixture.Key(Keys.Control | Keys.Y);
        Assert.IsTrue(fixture.Mix.Arrangement.Lanes.All(lane => fixture.Mix.Arrangement.Sources[lane.Clips.Single().SourceId].Channels == 1));
    }

    [STATestMethod]
    public void EditingMixWhileDialogIsOpen_RejectsStaleApplyWithoutCreatingAssetsOrHistory()
    {
        using var fixture = new Fixture(); fixture.SelectBoth(); fixture.InstallDialogFactory();
        Drain(fixture.Mix.OpenMixEffectAsync(AudioEffectKind.Gain)); Assert.IsNotNull(fixture.Editor);
        fixture.Editor.SetSettings(fixture.Editor.CurrentSettings.With(("GainDb", -6)));
        fixture.First.Start = 0.01; Call(fixture.Mix, "Changed"); int history = History(fixture.Mix), assets = fixture.Mix.Arrangement.Sources.Count;
        string current = fixture.Mix.Arrangement.Capture(); Drain(fixture.Editor.AcceptEffectAsync());
        Assert.AreEqual(current, fixture.Mix.Arrangement.Capture()); Assert.AreEqual(history, History(fixture.Mix));
        Assert.AreEqual(assets, fixture.Mix.Arrangement.Sources.Count); Assert.IsFalse(fixture.Opened!.IsDisposed);
        Assert.IsTrue(Descendants(fixture.Editor).OfType<Label>().Any(label => label.Text.Contains("mix audio changed", StringComparison.OrdinalIgnoreCase)));
    }

    [STATestMethod]
    public void ClosingMix_CancelsOwnedEffectRenderAndLeavesSourceAudioUntouched()
    {
        using var fixture = new Fixture(); fixture.SelectBoth(); CancellationToken observed = default;
        fixture.InstallDialogFactory(async (_, _, token) =>
        { observed = token; await Task.Delay(Timeout.Infinite, token); return new([], 2); });
        Drain(fixture.Mix.OpenMixEffectAsync(AudioEffectKind.HallReverb)); Assert.IsNotNull(fixture.Editor);
        Task work = fixture.Editor.AcceptEffectAsync(); Assert.IsTrue(observed.CanBeCanceled);
        fixture.Mix.Dispose(); Drain(work);
        Assert.IsTrue(observed.IsCancellationRequested); Assert.IsTrue(fixture.Opened!.IsDisposed);
        Assert.AreEqual(0.2f, fixture.Original.Data[100]);
    }

    private static string[] MenuNames(ToolStripMenuItem root) => root.DropDownItems.OfType<ToolStripMenuItem>()
        .SelectMany(group => new[] { group.Text ?? "" }.Concat(group.DropDownItems.OfType<ToolStripMenuItem>().Select(item => item.Text ?? ""))).ToArray();
    private static IEnumerable<Control> Descendants(Control parent)
    { foreach (Control child in parent.Controls) { yield return child; foreach (Control descendant in Descendants(child)) yield return descendant; } }
    private static object? Get(MixTrackView mix, string field) => typeof(MixTrackView).GetField(field, Flags)!.GetValue(mix);
    private static void Set(MixTrackView mix, string field, object value) => typeof(MixTrackView).GetField(field, Flags)!.SetValue(mix, value);
    private static object? Call(MixTrackView mix, string method, params object[] args) => typeof(MixTrackView).GetMethod(method, Flags)!.Invoke(mix, args);
    private static int History(MixTrackView mix) => ((Stack<string>)Get(mix, "undo")!).Count;
    private static void Drain(Task task)
    {
        SynchronizationContext? context = SynchronizationContext.Current;
        var watch = Stopwatch.StartNew();
        while (!task.IsCompleted && watch.Elapsed < TimeSpan.FromSeconds(15))
        { Application.DoEvents(); SynchronizationContext.SetSynchronizationContext(context); Thread.Sleep(1); }
        Assert.IsTrue(task.IsCompleted, "The modeless operation must finish/cancel without blocking the UI."); task.GetAwaiter().GetResult(); Application.DoEvents();
        SynchronizationContext.SetSynchronizationContext(context);
    }
    private sealed class Fixture : IDisposable
    {
        private readonly WinFormsTestContext uiContext;
        internal string Directory { get; } = Path.Combine(Path.GetTempPath(), "ModularAudience-mix-effect-ui-" + Guid.NewGuid().ToString("N"));
        internal MixTrackView Mix { get; } = new();
        internal AudioObj Original { get; } = new() { Name = "Original", Data = Enumerable.Range(0, 4410).SelectMany(_ => new[] { 0.2f, -0.1f }).ToArray(), Channels = 2, SampleRate = 44100, Bpm = 120 };
        internal MixClip First, Second;
        internal FakePreview Preview { get; } = new();
        internal Form? Opened;
        internal EffectOptionsEditor? Editor;
        internal int RenderCount;
        internal Fixture()
        {
            System.IO.Directory.CreateDirectory(this.Directory);
            MixSource source = this.Mix.Arrangement.AddSource(this.Original);
            MixLane first = new() { Name = "One" }, second = new() { Name = "Two" };
            this.Mix.Arrangement.Lanes.AddRange([first, second]); this.First = this.Mix.Arrangement.AddClip(first, source, 0); this.Second = this.Mix.Arrangement.AddClip(second, source, 0);
            Call(this.Mix, "Changed"); this.Mix.Show(); Application.DoEvents();
            // These synchronous STA tests pump DoEvents, not Application.Run.
            // Keep the real WinForms context across pump exits so asynchronous
            // window construction/render continuations stay on the owning STA.
            this.uiContext = new WinFormsTestContext();
        }
        internal void SelectBoth() { Call(this.Mix, "SelectWholeClip", this.First.Id, false); Call(this.Mix, "SelectWholeClip", this.Second.Id, true); }
        internal void InstallDialogFactory(Func<AudioEffectSettings, IProgress<double>, CancellationToken, Task<AudioEffectResult>>? render = null)
        {
            this.Mix.CreateMixEffectDialog = context =>
            {
                var form = new Form { Size = new Size(900, 650) };
                this.Editor = new EffectOptionsEditor(context, form.Close, form.Close,
                    new EffectPresetStore(context.Defaults.Effect, Path.Combine(this.Directory, context.Defaults.Effect + ".json")), this.Preview,
                    render ?? (async (settings, progress, token) => { this.RenderCount++; return await context.RenderAsync!(settings, new(), progress, token); }));
                form.Controls.Add(this.Editor); this.Opened = form; return form;
            };
        }
        internal void Key(Keys keys) => typeof(MixTrackView).GetMethod("ProcessCmdKey", Flags)!.Invoke(this.Mix,
            [Message.Create(this.Mix.Handle, 0, IntPtr.Zero, IntPtr.Zero), keys]);
        public void Dispose()
        {
            this.Opened?.Dispose(); this.Mix.Dispose(); this.Original.Dispose(); System.IO.Directory.Delete(this.Directory, true); Application.DoEvents();
            this.uiContext.Dispose();
        }
    }
    private sealed class FakePreview : IEffectPreviewPlayer
    {
        public bool IsPlaying { get; private set; }
        internal int Rate, Channels;
        public void Play(float[] samples, int sampleRate, int channels, bool loop) { this.IsPlaying = true; this.Rate = sampleRate; this.Channels = channels; }
        public void Stop() => this.IsPlaying = false;
        public void Dispose() => this.Stop();
    }
}
