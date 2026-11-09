using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Effects;
using ModularAudience.Audio.Effects.AutoTune;
using ModularAudience.Audio.Effects.Curves;
using ModularAudience.Forms.Effects;
using ModularAudience.Forms.Modules;
using ModularAudience.Forms.Modules.Dialogs;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class CurvePresetAndDialogTests
{
    public TestContext TestContext { get; set; } = null!;
    [TestMethod]
    public async Task GlobalCurveStore_IsExplicitAtomicConcurrentAndProtectsCodeDefinedPresets()
    {
        using var files = new Files(); var store = files.Store();
        IReadOnlyList<CurvePreset> defaults = store.ListAvailable(); Assert.IsFalse(File.Exists(store.FilePath));
        Assert.AreEqual(EffectCurvePresets.All.Count, defaults.Count);
        EffectCurve shape = new() { Equation = "0.5+0.5*sin(2*pi*(f*x+p))", Cycles = 7, Phase = 0.25 };
        store.Save("Sweep", shape); store.Save("Linear rise", shape);
        Assert.IsTrue(shape.SoundEquals(new CurvePresetStore(store.FilePath).Load("SWEEP").Curve));
        Assert.AreEqual("x", store.ListAvailable().Single(preset => preset.BuiltInId == "linear").Curve.Equation);
        Assert.AreEqual(1, store.ListAvailable().Count(preset => !preset.IsBuiltIn && preset.Name == "Linear rise"));
        CurvePreset renamed = store.Rename("sweep", "Slow sweep"); Assert.IsTrue(shape.SoundEquals(renamed.Curve));
        Assert.ThrowsExactly<InvalidOperationException>(() => store.Rename("Slow sweep", "Linear rise"));
        Assert.IsTrue(store.Delete("SLOW SWEEP")); Assert.IsFalse(store.Delete("Slow sweep"));
        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(() => new CurvePresetStore(store.FilePath).Save(null, shape))));
        Assert.AreEqual(10, store.List().Count(preset => preset.Name.StartsWith("Curve#", StringComparison.Ordinal)));
        Assert.AreEqual(11, store.List().Select(preset => preset.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.AreEqual(1, Directory.GetFiles(files.Directory).Length);
        string json = File.ReadAllText(store.FilePath);
        Assert.IsFalse(json.Contains("StartValue", StringComparison.Ordinal)); Assert.IsFalse(json.Contains("Effect", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("BuiltInId", StringComparison.Ordinal));
    }

    [TestMethod]
    public void InvalidOrLockedCurveFiles_AreNeverReplaced_AndInvalidShapesCannotBeSaved()
    {
        using var files = new Files(); var store = files.Store(); File.WriteAllText(store.FilePath, "{ broken");
        Assert.ThrowsExactly<JsonException>(() => store.ListAvailable()); Assert.ThrowsExactly<JsonException>(() => store.Save(null, new()));
        Assert.AreEqual("{ broken", File.ReadAllText(store.FilePath));
        File.Delete(store.FilePath); Assert.ThrowsExactly<ArgumentException>(() => store.Save(null, new() { Equation = "1/x" }));
        Assert.IsFalse(File.Exists(store.FilePath)); store.Save("Curve", new()); string before = File.ReadAllText(store.FilePath);
        using (var locked = new FileStream(store.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.ThrowsExactly<IOException>(() => store.Save("Other", new()));
        Assert.AreEqual(before, File.ReadAllText(store.FilePath)); Assert.AreEqual(1, Directory.GetFiles(files.Directory).Length);
    }

    [STATestMethod]
    public void CurveEditor_ValidatesPreviewsLoadsPresetsAndSavesVariantsOnlyExplicitly()
    {
        using var files = new Files(); EffectParameterCurve? applied = null;
        var target = EffectCurveTargets.ForEffect(AudioEffectKind.PitchShift).Single(target => target.Key == "Semitones");
        using var editor = new CurveEditorDialog(target, null, () => 12, binding => applied = binding, files.Store());
        ComboBox presets = Find<ComboBox>(editor, "curvePresets"); TextBox name = Find<TextBox>(editor, "presetName"); TextBox equation = Find<TextBox>(editor, "curveEquation");
        presets.SelectedItem = presets.Items.OfType<CurvePreset>().Single(preset => preset.BuiltInId == "sine");
        Assert.IsTrue(equation.Text.Contains("sin", StringComparison.Ordinal)); Assert.AreEqual(string.Empty, name.Text);
        Assert.IsFalse(Find<Button>(editor, "renamePreset").Enabled); Assert.IsFalse(Find<Button>(editor, "deletePreset").Enabled);
        equation.Text = "x^a"; Find<NumericUpDown>(editor, "curveVariableA").Value = 2;
        Assert.IsTrue(Find<Button>(editor, "applyCurve").Enabled); Assert.IsFalse(File.Exists(files.Store().FilePath));
        editor.SavePreset(); Assert.AreEqual("Curve#1", files.Store().List().Single().Name);
        Assert.AreEqual("0.5+0.5*sin(2*pi*(f*x+p))", files.Store().FactoryPresets().Single(preset => preset.BuiltInId == "sine").Curve.Equation);
        name.Text = "Parabolic slide"; editor.RenamePreset(); equation.Text = "1-x";
        typeof(ComboBox).GetMethod("OnDropDown", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(presets, [EventArgs.Empty]);
        Assert.AreEqual("1-x", equation.Text); editor.LoadSelectedPreset(); Assert.AreEqual("x^a", equation.Text);
        equation.Text = "1/x"; Assert.IsFalse(Find<Button>(editor, "applyCurve").Enabled); Assert.IsFalse(Find<Button>(editor, "savePreset").Enabled);
        Assert.IsTrue(Find<Label>(editor, "curveValidation").Text.Contains("Undefined", StringComparison.OrdinalIgnoreCase));
        equation.Text = "x^a"; editor.AcceptCurve(); Assert.IsNotNull(applied); Assert.AreEqual(0d, applied.StartValue); Assert.IsNull(applied.EndValue);
        Assert.AreEqual("x^a", applied.Curve.Equation); Assert.AreEqual(1, files.Store().List().Count);
    }

    [STATestMethod]
    public void CurveEditor_CheckpointsAcceptWithoutSavingAndCancelDoesNotChangeCaller()
    {
        using var files = new Files(); int accepted = 0;
        var target = EffectCurveTargets.ForEffect(AudioEffectKind.Gain).Single(target => target.Key == "GainDb");
        using (var editor = new CurveEditorDialog(target, null, () => 6, _ => accepted++, files.Store()))
        {
            Find<TextBox>(editor, "curveEquation").Text = "x^2"; editor.Close(); Assert.AreEqual(0, accepted); Assert.IsFalse(File.Exists(files.Store().FilePath));
        }
        using (var editor = new CurveEditorDialog(target, null, () => 6, binding =>
        { Assert.AreEqual(EffectCurveMode.Checkpoints, binding.Curve.Mode); Assert.AreEqual(3, binding.Curve.Points.Count); accepted++; }, files.Store()))
        {
            DataGridView grid = Find<DataGridView>(editor, "curveCheckpoints"); grid.Rows.Clear(); grid.Rows.Add("0", "0"); grid.Rows.Add("0.4", "1"); grid.Rows.Add("1", "0");
            Find<TabControl>(editor, "curveMode").SelectedIndex = 1; Assert.IsTrue(Find<Button>(editor, "applyCurve").Enabled);
            editor.AcceptCurve(); Assert.AreEqual(1, accepted); Assert.IsFalse(File.Exists(files.Store().FilePath));
        }
    }

    [STATestMethod]
    public void CorruptCurveLibrary_StillOffersBuiltInsWithoutOverwritingIt()
    {
        using var files = new Files(); var store = files.Store(); File.WriteAllText(store.FilePath, "[ broken");
        using var editor = new CurveEditorDialog(EffectCurveTargets.ForEffect(AudioEffectKind.Gain).First(), null, () => 100, _ => { }, store);
        ComboBox presets = Find<ComboBox>(editor, "curvePresets"); Assert.AreEqual(EffectCurvePresets.All.Count, presets.Items.Count);
        presets.SelectedIndex = 0; Assert.AreEqual("x", editor.CurrentBinding.Curve.Equation); Assert.AreEqual("[ broken", File.ReadAllText(store.FilePath));
    }

    [STATestMethod]
    public void EveryEffectEditor_HasIndependentControlsForExactlyItsAdvertisedCurveTargetsAndRoundTripsCurves()
    {
        foreach (AudioEffectDefinition definition in AudioEffectCatalog.All.Where(definition => definition.OpensOptions && definition.Kind != AudioEffectKind.AutoTune))
        {
            using var editor = new EffectParameterEditor(definition); var targets = EffectCurveTargets.ForEffect(definition.Kind);
            Assert.AreEqual(targets.Count, Descendants(editor).OfType<CurveBindingControl>().Count(), definition.Name);
            var bindings = targets.ToDictionary(target => target.Key, target => new EffectParameterCurve { Enabled = target.Key != "OutputGainDb",
                StartValue = target.NeutralValue, Curve = new() { Equation = "x^2" } }, StringComparer.Ordinal);
            var settings = definition.CreateDefaults(); if (definition.Kind == AudioEffectKind.HumRemoval) settings = settings.With(("Mains", 2));
            if (definition.Kind == AudioEffectKind.NoiseReduction) settings = settings.With(("Profile", 2));
            settings = settings with { Automation = new() { Parameters = bindings, Timing = new() { Scope = EffectCurveScope.AcrossSelection, IncludeGaps = false, IncludeSilence = false } } };
            editor.ApplySettings(settings); Assert.IsTrue(settings.SoundEquals(editor.CaptureSettings()), definition.Name);
            foreach (EffectCurveTarget target in targets)
            {
                Assert.IsTrue(Find<Label>(editor, target.Key + "CurveEquation").Text.Contains("x^2", StringComparison.Ordinal));
                Assert.IsNotNull(Find<Button>(editor, target.Key + "EditCurve"));
            }
            EffectCurveTarget first = targets[0]; Find<CheckBox>(editor, first.Key + "CurveEnabled").Checked = false;
            Assert.IsFalse(editor.CaptureSettings().Automation!.Parameters[first.Key].Enabled);
            Assert.AreEqual("x^2", editor.CaptureSettings().Automation!.Parameters[first.Key].Curve.Equation);
        }
        using var tuning = new AutoTuneParameterEditor(); Assert.AreEqual(AutoTuneCurveTargets.All.Count, Descendants(tuning).OfType<CurveBindingControl>().Count());
        var typed = new AutoTuneSettings { Automation = new() { Parameters = AutoTuneCurveTargets.All.ToDictionary(target => target.Key,
            target => new EffectParameterCurve { StartValue = target.NeutralValue, Curve = new() { Equation = "smoothstep(x)" } }, StringComparer.Ordinal) } };
        tuning.ApplySettings(typed); Assert.IsTrue(typed.SoundEquals(tuning.CaptureSettings()));
    }

    [STATestMethod]
    public void TimeStretchHasANewDedicatedDialog_AndVarispeedIsTheNextMenuEntryWithBpmHelpers()
    {
        using var files = new Files();
        using var audio = new AudioObj { Data = [0.1f, 0.2f], SampleRate = 8000, Channels = 1, Name = "Test" };
        AudioEffectEditSession session = AudioEffectEditSession.Capture(audio);
        // Instantiate the real hosts, without showing them or opening audio devices.
        using Form stretch = new TimeStretchEffectDialog(new(session, "Test", AudioEffectCatalog.Get(AudioEffectKind.TimeStretch).CreateDefaults(), (_, _) => Task.CompletedTask),
            new(AudioEffectKind.TimeStretch, Path.Combine(files.Directory, "Stretch.json")), new FakePreview());
        Assert.IsInstanceOfType<TimeStretchEffectDialog>(stretch); Assert.IsFalse(stretch is TimeStretchDialog);
        using Form speed = new VarispeedShiftDialog(new(session, "Test", AudioEffectCatalog.Get(AudioEffectKind.VarispeedShift).CreateDefaults(), (_, _) => Task.CompletedTask),
            new(AudioEffectKind.VarispeedShift, Path.Combine(files.Directory, "Speed.json")), new FakePreview());
        Assert.IsInstanceOfType<VarispeedShiftDialog>(speed);
        NumericUpDown source = Find<NumericUpDown>(speed, "sourceBpm"), target = Find<NumericUpDown>(speed, "targetBpm");
        source.Value = 120; target.Value = 180; Assert.AreEqual(1.5m, Find<NumericUpDown>(speed, "Speed").Value);
        Find<NumericUpDown>(speed, "Speed").Value = 0.5m; Assert.AreEqual(60m, target.Value);
        using ToolStripMenuItem menu = EffectMenuFactory.CreateApplyMenu(_ => { });
        ToolStripMenuItem group = menu.DropDownItems.OfType<ToolStripMenuItem>().Single(item => item.Text == "Time && Speed");
        CollectionAssert.AreEqual(new[] { AudioEffectKind.TimeStretch, AudioEffectKind.VarispeedShift }, group.DropDownItems.OfType<ToolStripMenuItem>().Select(item => (AudioEffectKind)item.Tag!).ToArray());
        Assert.AreEqual(35, (int)AudioEffectKind.NoiseReduction); Assert.AreEqual(36, (int)AudioEffectKind.VarispeedShift);
    }

    [STATestMethod]
    public void CurveEditsInvalidatePreviewCache_StopPlaybackAndApplyOneUndoStep()
    {
        using var files = new Files(); using var audio = new AudioObj { Data = Enumerable.Repeat(0.1f, 202).ToArray(), Channels = 2, SampleRate = 8000, Length = 202 };
        var before = (float[])audio.Data.Clone(); var preview = new FakePreview(); int renders = 0;
        AudioEffectEditSession session = AudioEffectEditSession.Capture(audio);
        var settings = AudioEffectCatalog.Get(AudioEffectKind.Gain).CreateDefaults().With(("GainDb", 12));
        using var editor = new EffectOptionsEditor(new(session, "Test", settings, (result, _) => { session.Apply(audio, result); return Task.CompletedTask; }),
            () => { }, () => { }, new(AudioEffectKind.Gain, Path.Combine(files.Directory, "Gain.json")), preview,
            async (parameters, report, token) => { renders++; return await AudioEffectProcessor.ProcessAsync(session.Input, 8000, 2, parameters, report, token); });
        Drain(editor.PrehearAsync()); Assert.AreEqual(1, renders); Assert.IsTrue(preview.IsPlaying); Assert.IsFalse(audio.CanUndo);
        var curveControl = Descendants(editor).OfType<CurveBindingControl>().Single(control => control.Name == "GainDbCurveControl");
        curveControl.AcceptCurve(new() { StartValue = 0 }); Assert.IsFalse(preview.IsPlaying);
        Drain(editor.PrehearAsync()); Assert.AreEqual(2, renders); Assert.AreEqual(0.1f, preview.Samples![0], 1e-6f);
        Assert.AreEqual(0.1 * Math.Pow(10, 12d / 20), preview.Samples[^1], 1e-6); CollectionAssert.AreEqual(before, audio.Data);
        Drain(editor.AcceptEffectAsync()); Assert.AreEqual(2, renders); Assert.AreEqual(1, audio.PreviousSteps.Count);
        audio.Undo(); CollectionAssert.AreEqual(before, audio.Data); audio.Redo(); Assert.AreEqual(preview.Samples[^1], audio.Data[^1]);
    }

    [STATestMethod]
    public void SavingAnEffectPreset_PreservesAllCurvesWhileShapeLibraryRemainsIndependent()
    {
        using var files = new Files(); var store = new EffectPresetStore(AudioEffectKind.VarispeedShift, Path.Combine(files.Directory, "Varispeed.json"));
        var sound = AudioEffectCatalog.Get(AudioEffectKind.VarispeedShift).CreateDefaults() with { Automation = new() { Parameters = new()
        { ["Speed"] = new() { StartValue = 0.5, EndValue = 2, Curve = new() { Equation = "x^2" } }, ["OutputGainDb"] = new() { StartValue = 0, EndValue = -6 } },
            Timing = new() { Scope = EffectCurveScope.AcrossSelection, IncludeGaps = false } } };
        store.Save("Tape ramp", sound); Assert.IsTrue(sound.SoundEquals(store.Load("Tape ramp").Parameters));
        Assert.IsFalse(File.Exists(files.Store().FilePath));
        var tuningStore = new AutoTunePresetStore(Path.Combine(files.Directory, "AutoTune.json"));
        var tuning = new AutoTuneSettings { Automation = new() { Parameters = new() { [nameof(AutoTuneSettings.IntensityPercent)] = new() { StartValue = 0, Curve = new() { Equation = "x^2" } } } } };
        tuningStore.Save("Growing correction", tuning); Assert.IsTrue(tuning.SoundEquals(tuningStore.Load("Growing correction").Parameters));
    }

    [STATestMethod]
    public void CurveEditor_ActuallyPaintsItsLivePreviewAtNormalAndMinimumSize()
    {
        using var files = new Files();
        using var editor = new CurveEditorDialog(EffectCurveTargets.ForEffect(AudioEffectKind.PitchShift).Single(target => target.Key == "Semitones"),
            new() { StartValue = 0, Curve = EffectCurvePresets.All.Single(preset => preset.Id == "damped").Curve.Copy() }, () => 12, _ => { }, files.Store());
        editor.Show(); Application.DoEvents();
        using (var picture = new Bitmap(editor.Width, editor.Height))
        {
            editor.DrawToBitmap(picture, new Rectangle(Point.Empty, picture.Size));
            string directory = this.TestContext.TestRunResultsDirectory!; System.IO.Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "curve-editor-preview.png");
            picture.Save(path, System.Drawing.Imaging.ImageFormat.Png); this.TestContext.AddResultFile(path);
        }
        editor.Size = editor.MinimumSize; Application.DoEvents();
        using var minimum = new Bitmap(editor.ClientSize.Width, editor.ClientSize.Height); editor.DrawToBitmap(minimum, editor.ClientRectangle);
        Assert.IsGreaterThan(80, Find<PictureBox>(editor, "curvePreview").Height); Assert.IsTrue(Find<Button>(editor, "applyCurve").Visible);
        editor.Close(); Application.DoEvents();
    }

    [STATestMethod]
    [DataRow(AudioEffectKind.TimeStretch, "Factor", 0.5)]
    [DataRow(AudioEffectKind.VarispeedShift, "Speed", 2d)]
    public void TrackView_OpensTheNewSpeedDialogsAndCurvedSelectionPreviewApplyUndoRedoStayIsolated(AudioEffectKind effect, string key, double target)
    {
        using var files = new Files(); using var audio = new AudioObj { Data = Enumerable.Range(0, 400).SelectMany(_ => new[] { 0.1f, -0.05f }).ToArray(),
            SampleRate = 8000, Channels = 2, Length = 800, Name = "Selected speed", Bpm = 120 };
        using var view = new TrackView(audio); var preview = new FakePreview(); Form? opened = null; EffectOptionsEditor? editor = null;
        view.OriginalAudio.SelectionStart = 200; view.OriginalAudio.SelectionEnd = 600;
        float[] before = (float[])view.OriginalAudio.Data.Clone();
        view.CreateEffectDialog = context =>
        {
            var store = new EffectPresetStore(effect, Path.Combine(files.Directory, effect + ".json"));
            if (effect == AudioEffectKind.TimeStretch) { var dialog = new TimeStretchEffectDialog(context, store, preview); editor = dialog.Editor; return opened = dialog; }
            else { var dialog = new VarispeedShiftDialog(context, store, preview); editor = dialog.Editor; return opened = dialog; }
        };
        try
        {
            var menu = (ContextMenuStrip)typeof(TrackView).GetField("contextMenu_waveform", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(view)!;
            menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Name == "menuItem_applyEffects").DropDownItems.OfType<ToolStripMenuItem>()
                .SelectMany(group => group.DropDownItems.OfType<ToolStripMenuItem>()).Single(item => item.Tag is AudioEffectKind kind && kind == effect).PerformClick();
            Application.DoEvents(); Assert.IsNotNull(opened); Assert.IsNotNull(editor); Assert.IsFalse(opened.Modal); Assert.AreSame(view, opened.Owner);
            editor.SetSettings(editor.CurrentSettings.With((key, target)) with { Automation = new() { Parameters = new() { [key] = new() { StartValue = 1 } } } });
            Drain(editor.PrehearAsync()); CollectionAssert.AreEqual(before, view.OriginalAudio.Data); Assert.IsFalse(view.OriginalAudio.CanUndo);
            int rendered = preview.Samples!.Length; Assert.AreEqual(200 * Math.Log(2), rendered / 2d, 1, "The numerical source-frame integral is rounded to complete frames.");
            Drain(editor.AcceptEffectAsync());
            Assert.AreEqual(200 + rendered + 200, view.OriginalAudio.Data.Length); Assert.AreEqual(1, view.OriginalAudio.PreviousSteps.Count);
            CollectionAssert.AreEqual(before.Take(200).ToArray(), view.OriginalAudio.Data.Take(200).ToArray());
            CollectionAssert.AreEqual(before.Skip(600).ToArray(), view.OriginalAudio.Data.Skip(200 + rendered).ToArray());
            MethodInfo keys = typeof(TrackView).GetMethod("ProcessCmdKey", BindingFlags.NonPublic | BindingFlags.Instance)!;
            keys.Invoke(view, [Message.Create(view.Handle, 0, IntPtr.Zero, IntPtr.Zero), Keys.Control | Keys.Z]); CollectionAssert.AreEqual(before, view.OriginalAudio.Data);
            keys.Invoke(view, [Message.Create(view.Handle, 0, IntPtr.Zero, IntPtr.Zero), Keys.Control | Keys.Y]); Assert.AreEqual(400 + rendered, view.OriginalAudio.Data.Length);
            CollectionAssert.AreEqual(audio.Data, before);
        }
        finally { opened?.Dispose(); view.Close(); Application.DoEvents(); }
    }

    private static T Find<T>(Control parent, string name) where T : Control => Descendants(parent).OfType<T>().Single(control => control.Name == name);
    private static IEnumerable<Control> Descendants(Control parent) { foreach (Control child in parent.Controls) { yield return child; foreach (Control descendant in Descendants(child)) yield return descendant; } }
    private static void Drain(Task task)
    {
        SynchronizationContext? context = SynchronizationContext.Current; var watch = Stopwatch.StartNew();
        while (!task.IsCompleted && watch.Elapsed < TimeSpan.FromSeconds(20)) { Application.DoEvents(); SynchronizationContext.SetSynchronizationContext(context); Thread.Sleep(1); }
        Assert.IsTrue(task.IsCompleted); task.GetAwaiter().GetResult(); Application.DoEvents(); SynchronizationContext.SetSynchronizationContext(context);
    }
    private sealed class FakePreview : IEffectPreviewPlayer
    {
        public bool IsPlaying { get; private set; }
        internal float[]? Samples;
        public void Play(float[] samples, int sampleRate, int channels, bool loop) { this.Samples = samples; this.IsPlaying = true; }
        public void Stop() => this.IsPlaying = false;
        public void Dispose() => this.Stop();
    }
    private sealed class Files : IDisposable
    {
        internal string Directory { get; } = Path.Combine(Path.GetTempPath(), "ModularAudience-curves-test-" + Guid.NewGuid().ToString("N"));
        internal Files() => System.IO.Directory.CreateDirectory(this.Directory);
        internal CurvePresetStore Store() => new(Path.Combine(this.Directory, "CurvePresets.json"));
        public void Dispose() => System.IO.Directory.Delete(this.Directory, true);
    }
}
