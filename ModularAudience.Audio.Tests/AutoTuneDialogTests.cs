using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Effects;
using ModularAudience.Audio.Effects.AutoTune;
using ModularAudience.Forms.Effects;
using ModularAudience.Forms.Modules.Dialogs;
using ModularAudience.Forms.Modules;
using System.Reflection;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class AutoTuneDialogTests
{
    [STATestMethod]
    public void ParameterEditor_RoundTripsEverySettingAndOffersAllMinimumDurations()
    {
        using var editor = new AutoTuneParameterEditor();

        editor.ApplySettings(AutoTunePresetStoreTests.DetailedSettings);

        Assert.AreEqual(AutoTunePresetStoreTests.DetailedSettings, editor.CaptureSettings());
        var duration = Descendants(editor).OfType<DomainUpDown>().Single();
        CollectionAssert.AreEqual(AutoTuneSettings.NoteLengths.ToArray(), duration.Items.Cast<string>().ToArray());
    }

    [STATestMethod]
    public void Prehear_IsModelessAndDoesNotMutateSourceOrUndoHistory_ThenAcceptUsesCachedRender()
    {
        using var fixture = new DialogFixture();
        float[] before = (float[])fixture.Audio.Data.Clone();
        using AutoTuneDialog dialog = fixture.Dialog();
        dialog.Show();
        Application.DoEvents();
        Assert.IsFalse(dialog.Modal);

        Drain(dialog.PrehearAsync());

        CollectionAssert.AreEqual(before, fixture.Audio.Data);
        Assert.IsFalse(fixture.Audio.CanUndo);
        Assert.IsTrue(fixture.Preview.IsPlaying);
        Assert.AreEqual(1, fixture.RenderCount);
        Drain(dialog.AcceptEffectAsync());
        Assert.AreEqual(1, fixture.RenderCount);
        Assert.AreEqual(1, fixture.Audio.PreviousSteps.Count);
        CollectionAssert.AreEqual(new float[] { 0.2f, 0.4f, 0.6f, 0.8f }, fixture.Audio.Data);
        fixture.Audio.Undo(); CollectionAssert.AreEqual(before, fixture.Audio.Data);
        fixture.Audio.Redo(); CollectionAssert.AreEqual(new float[] { 0.2f, 0.4f, 0.6f, 0.8f }, fixture.Audio.Data);
    }

    [STATestMethod]
    public void SelectingPreset_LoadsUiAndDeleteResetsEveryParameterToDefaults()
    {
        using var fixture = new DialogFixture();
        fixture.Store.Save("Detailed", AutoTunePresetStoreTests.DetailedSettings);
        using AutoTuneDialog dialog = fixture.Dialog();
        ComboBox presets = Descendants(dialog).OfType<ComboBox>().Single(control => control.Name == "autoTunePresets");

        presets.SelectedItem = UserPreset(presets, "Detailed");
        Assert.AreEqual(AutoTunePresetStoreTests.DetailedSettings, dialog.CurrentSettings);
        dialog.DeletePreset();

        Assert.AreEqual(fixture.Defaults, dialog.CurrentSettings);
        Assert.HasCount(0, fixture.Store.List());
        Assert.AreEqual(AutoTuneBuiltInPresets.All.Count, presets.Items.OfType<AutoTunePreset>().Count(preset => preset.IsBuiltIn));
        Assert.IsFalse(fixture.Audio.CanUndo);
    }

    [STATestMethod]
    [DataRow(128.5f, 150.25f, 128.5, 128.5)]
    [DataRow(0f, 150.25f, 150.25, 150.25)]
    [DataRow(401f, 150.25f, 150.25, 150.25)]
    [DataRow(0f, 0f, 135.5, 120d)]
    public void SelectingUserAndBuiltInPresets_AdaptsOnlyTempoToTrackWithoutChangingSavedValues(
        float bpm, float scannedBpm, double expectedUserTempo, double expectedBuiltInTempo)
    {
        using var fixture = new DialogFixture();
        fixture.Audio.Bpm = bpm; fixture.Audio.ScannedBpm = scannedBpm;
        float[] before = (float[])fixture.Audio.Data.Clone();
        AutoTuneSettings user = AutoTunePresetStoreTests.DetailedSettings;
        fixture.Store.Save("Detailed", user);
        using AutoTuneDialog dialog = fixture.Dialog();
        string savedJson = File.ReadAllText(fixture.Store.FilePath);
        ComboBox presets = Descendants(dialog).OfType<ComboBox>().Single(control => control.Name == "autoTunePresets");
        NumericUpDown threads = Descendants(dialog).OfType<NumericUpDown>().Single(control => control.Name == "maxThreads");
        threads.Value = Environment.ProcessorCount;

        presets.SelectedItem = UserPreset(presets, "Detailed");
        Assert.AreEqual(user with { TempoBpm = expectedUserTempo }, dialog.CurrentSettings);
        AutoTunePreset soft = presets.Items.OfType<AutoTunePreset>().Single(preset => preset.BuiltInId == "soft");
        presets.SelectedItem = soft;
        Assert.AreEqual(soft.Parameters with { TempoBpm = expectedBuiltInTempo }, dialog.CurrentSettings);

        Assert.AreEqual(savedJson, File.ReadAllText(fixture.Store.FilePath));
        CollectionAssert.AreEqual(before, fixture.Audio.Data);
        Assert.AreEqual(bpm, fixture.Audio.Bpm);
        Assert.AreEqual(scannedBpm, fixture.Audio.ScannedBpm);
        Assert.AreEqual((decimal)Environment.ProcessorCount, threads.Value);
        Assert.IsFalse(fixture.Audio.CanUndo);
        Assert.IsFalse(fixture.Audio.CanRedo);
    }

    [STATestMethod]
    public void PresetTempo_ReadsCurrentTrackMetadataOnEverySelectionAndStillAllowsManualEdits()
    {
        using var fixture = new DialogFixture();
        fixture.Audio.Bpm = 128.5f; fixture.Audio.ScannedBpm = 150.25f;
        fixture.Store.Save("Detailed", AutoTunePresetStoreTests.DetailedSettings);
        using AutoTuneDialog dialog = fixture.Dialog();
        ComboBox presets = Descendants(dialog).OfType<ComboBox>().Single(control => control.Name == "autoTunePresets");
        Assert.AreEqual(128.5, dialog.CurrentSettings.TempoBpm);
        presets.SelectedItem = UserPreset(presets, "Detailed");
        Assert.AreEqual(128.5, dialog.CurrentSettings.TempoBpm);
        dialog.SetSettings(dialog.CurrentSettings with { TempoBpm = 93.75 });
        Assert.AreEqual(93.75, dialog.CurrentSettings.TempoBpm);

        fixture.Audio.Bpm = 0;
        presets.SelectedItem = presets.Items.OfType<AutoTunePreset>().Single(preset => preset.BuiltInId == "soft");
        Assert.AreEqual(150.25, dialog.CurrentSettings.TempoBpm);
        fixture.Audio.Bpm = 172.5f;
        presets.SelectedItem = UserPreset(presets, "Detailed");
        Assert.AreEqual(172.5, dialog.CurrentSettings.TempoBpm);
        fixture.Audio.Bpm = fixture.Audio.ScannedBpm = 0;
        presets.SelectedItem = presets.Items.OfType<AutoTunePreset>().Single(preset => preset.BuiltInId == "soft");
        Assert.AreEqual(120d, dialog.CurrentSettings.TempoBpm);
        presets.SelectedItem = UserPreset(presets, "Detailed");
        Assert.AreEqual(135.5, dialog.CurrentSettings.TempoBpm);
        Assert.AreEqual(AutoTunePresetStoreTests.DetailedSettings, fixture.Store.Load("Detailed").Parameters);
        Assert.IsFalse(fixture.Audio.CanUndo);
    }

    [STATestMethod]
    public void SavingWithoutName_UpdatesComboBoxWithUniqueGenericName()
    {
        using var fixture = new DialogFixture();
        using AutoTuneDialog dialog = fixture.Dialog();

        dialog.SavePreset();

        Assert.AreEqual("Preset#1", fixture.Store.List().Single().Name);
        Assert.AreEqual("Preset#1", ((AutoTunePreset)Descendants(dialog).OfType<ComboBox>().Single(control => control.Name == "autoTunePresets").SelectedItem!).Name);
    }

    [STATestMethod]
    public void ExplicitLoadAndRename_UseTheSharedPresetUiWithoutChangingAudio()
    {
        using var fixture = new DialogFixture(); fixture.Store.Save("Detailed", AutoTunePresetStoreTests.DetailedSettings);
        using AutoTuneDialog dialog = fixture.Dialog();
        ComboBox presets = Descendants(dialog).OfType<ComboBox>().Single(control => control.Name == "autoTunePresets");
        presets.SelectedItem = UserPreset(presets, "Detailed"); dialog.SetSettings(new AutoTuneSettings()); dialog.LoadSelectedPreset();
        Assert.AreEqual(AutoTunePresetStoreTests.DetailedSettings, dialog.CurrentSettings);
        Descendants(dialog).OfType<TextBox>().Single(control => control.Name == "presetName").Text = "Renamed";
        dialog.RenamePreset();
        Assert.AreEqual(AutoTunePresetStoreTests.DetailedSettings, fixture.Store.Load("Renamed").Parameters);
        Assert.ThrowsExactly<KeyNotFoundException>(() => fixture.Store.Load("Detailed")); Assert.IsFalse(fixture.Audio.CanUndo);
    }

    [STATestMethod]
    public void MaxThreads_IsCpuLimitedAndIndependentOfPresetLoadAndDelete()
    {
        using var fixture = new DialogFixture();
        fixture.Store.Save("Detailed", AutoTunePresetStoreTests.DetailedSettings);
        using AutoTuneDialog dialog = fixture.Dialog();
        NumericUpDown threads = Descendants(dialog).OfType<NumericUpDown>().Single(control => control.Name == "maxThreads");
        Assert.AreEqual((decimal)Environment.ProcessorCount, threads.Maximum);
        Assert.AreEqual((decimal)Math.Max(1, Environment.ProcessorCount / 2), threads.Value);
        threads.Value = Environment.ProcessorCount;

        ComboBox presets = Descendants(dialog).OfType<ComboBox>().Single(control => control.Name == "autoTunePresets");
        presets.SelectedItem = UserPreset(presets, "Detailed");
        Assert.AreEqual((decimal)Environment.ProcessorCount, threads.Value);
        dialog.DeletePreset();
        Assert.AreEqual((decimal)Environment.ProcessorCount, threads.Value);
        presets.SelectedItem = presets.Items.OfType<AutoTunePreset>().Single(preset => preset.BuiltInId == "soft");
        Assert.AreEqual((decimal)Environment.ProcessorCount, threads.Value);
        dialog.SavePreset();
        Assert.AreEqual((decimal)Environment.ProcessorCount, threads.Value);
        Button reset = Descendants(dialog).OfType<Button>().Single(control => control.Text == "Defaults");
        typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(reset, [EventArgs.Empty]);
        Assert.AreEqual(fixture.Defaults, dialog.CurrentSettings);
        Assert.AreEqual((decimal)Environment.ProcessorCount, threads.Value);
    }

    [STATestMethod]
    public void SelectingEachBuiltIn_LoadsEveryParameterWithoutChangingAudioHistoryOrThreads()
    {
        using var fixture = new DialogFixture();
        float[] before = (float[])fixture.Audio.Data.Clone();
        using AutoTuneDialog dialog = fixture.Dialog();
        ComboBox presets = Descendants(dialog).OfType<ComboBox>().Single(control => control.Name == "autoTunePresets");
        NumericUpDown threads = Descendants(dialog).OfType<NumericUpDown>().Single(control => control.Name == "maxThreads");
        threads.Value = Environment.ProcessorCount;

        foreach (AutoTuneBuiltInPreset definition in AutoTuneBuiltInPresets.All)
        {
            presets.SelectedItem = presets.Items.OfType<AutoTunePreset>().Single(preset => preset.BuiltInId == definition.Id);

            Assert.AreEqual(definition.Parameters, dialog.CurrentSettings, definition.Id);
            Assert.AreEqual((decimal)Environment.ProcessorCount, threads.Value, definition.Id);
        }

        CollectionAssert.AreEqual(before, fixture.Audio.Data);
        Assert.IsFalse(fixture.Audio.CanUndo);
        Assert.IsFalse(fixture.Audio.CanRedo);
    }

    [STATestMethod]
    public void BuiltInPresets_AreAlwaysShownAndSavingAVariantDoesNotOverwriteSameNamedUserPreset()
    {
        using var fixture = new DialogFixture();
        fixture.Store.Save("Soft", AutoTunePresetStoreTests.DetailedSettings);
        using AutoTuneDialog dialog = fixture.Dialog();
        ComboBox presets = Descendants(dialog).OfType<ComboBox>().Single(control => control.Name == "autoTunePresets");
        Assert.AreEqual(AutoTuneBuiltInPresets.All.Count, presets.Items.OfType<AutoTunePreset>().Count(preset => preset.IsBuiltIn));

        AutoTunePreset soft = presets.Items.OfType<AutoTunePreset>().Single(preset => preset.BuiltInId == "soft");
        presets.SelectedItem = soft;
        Assert.AreEqual(soft.Parameters, dialog.CurrentSettings);
        Assert.IsFalse(Descendants(dialog).OfType<Button>().Single(control => control.Name == "deletePreset").Enabled);
        Assert.AreEqual(string.Empty, Descendants(dialog).OfType<TextBox>().Single(control => control.Name == "presetName").Text);
        dialog.DeletePreset();
        Assert.AreEqual(soft.Parameters, dialog.CurrentSettings, "Delete must not remove or reset a selected default.");

        dialog.SetSettings(soft.Parameters with { IntensityPercent = 45 });
        dialog.SavePreset();

        Assert.AreEqual(AutoTunePresetStoreTests.DetailedSettings, fixture.Store.Load("Soft").Parameters);
        Assert.AreEqual(45d, fixture.Store.Load("Preset#1").Parameters.IntensityPercent);
        Assert.AreEqual(soft.Parameters, fixture.Store.ListAvailable().Single(preset => preset.BuiltInId == "soft").Parameters);
        Assert.IsFalse(fixture.Audio.CanUndo);
    }

    [STATestMethod]
    public void MalformedUserFile_DoesNotHideOrPreventLoadingDefaults()
    {
        using var fixture = new DialogFixture();
        File.WriteAllText(fixture.Store.FilePath, "{broken JSON");
        using AutoTuneDialog dialog = fixture.Dialog();
        ComboBox presets = Descendants(dialog).OfType<ComboBox>().Single(control => control.Name == "autoTunePresets");

        presets.SelectedItem = presets.Items.OfType<AutoTunePreset>().Single(preset => preset.BuiltInId == "soft");

        Assert.AreEqual(AutoTuneBuiltInPresets.All.Single(definition => definition.Id == "soft").Parameters, dialog.CurrentSettings);
        Assert.AreEqual("{broken JSON", File.ReadAllText(fixture.Store.FilePath));
    }

    [STATestMethod]
    public void LockedPresetFile_StillOffersDefaultsAndUsersWithoutReplacingTheFile()
    {
        using var fixture = new DialogFixture();
        fixture.Store.Save("My Voice", AutoTunePresetStoreTests.DetailedSettings);
        string before = File.ReadAllText(fixture.Store.FilePath);
        using var locked = new FileStream(fixture.Store.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using AutoTuneDialog dialog = fixture.Dialog();
        ComboBox presets = Descendants(dialog).OfType<ComboBox>().Single(control => control.Name == "autoTunePresets");
        Assert.AreEqual(AutoTuneBuiltInPresets.All.Count + 1, presets.Items.Count);

        presets.SelectedItem = presets.Items.OfType<AutoTunePreset>().Single(preset => preset.BuiltInId == "medium");
        Assert.AreEqual(AutoTuneBuiltInPresets.All.Single(definition => definition.Id == "medium").Parameters, dialog.CurrentSettings);
        presets.SelectedItem = UserPreset(presets, "My Voice");
        Assert.AreEqual(AutoTunePresetStoreTests.DetailedSettings, dialog.CurrentSettings);
        Assert.AreEqual(before, File.ReadAllText(fixture.Store.FilePath));
        Assert.HasCount(1, Directory.GetFiles(Path.GetDirectoryName(fixture.Store.FilePath)!));
    }

    [STATestMethod]
    public void DropdownRefresh_PreservesOriginSelectionAndUnsavedParameterChanges()
    {
        using var fixture = new DialogFixture();
        fixture.Store.Save("Soft", AutoTunePresetStoreTests.DetailedSettings);
        using AutoTuneDialog dialog = fixture.Dialog();
        ComboBox presets = Descendants(dialog).OfType<ComboBox>().Single(control => control.Name == "autoTunePresets");
        MethodInfo refresh = typeof(ComboBox).GetMethod("OnDropDown", BindingFlags.Instance | BindingFlags.NonPublic)!;
        presets.SelectedItem = presets.Items.OfType<AutoTunePreset>().Single(preset => preset.BuiltInId == "soft");
        AutoTuneSettings variant = dialog.CurrentSettings with { IntensityPercent = 43 };
        dialog.SetSettings(variant);
        fixture.Store.Save("Other window", new());

        refresh.Invoke(presets, [EventArgs.Empty]);

        Assert.AreEqual("soft", ((AutoTunePreset)presets.SelectedItem!).BuiltInId);
        Assert.AreEqual(variant, dialog.CurrentSettings);
        Assert.IsNotNull(UserPreset(presets, "Other window"));

        presets.SelectedItem = UserPreset(presets, "Soft");
        refresh.Invoke(presets, [EventArgs.Empty]);

        Assert.IsFalse(((AutoTunePreset)presets.SelectedItem!).IsBuiltIn);
        Assert.AreEqual(AutoTunePresetStoreTests.DetailedSettings, dialog.CurrentSettings);
        Assert.IsTrue(Descendants(dialog).OfType<Button>().Single(control => control.Name == "deletePreset").Enabled);
    }

    [STATestMethod]
    public void ParameterChanges_InvalidatePreviewAndRenderAgainWithoutCreatingUndo()
    {
        using var fixture = new DialogFixture();
        using AutoTuneDialog dialog = fixture.Dialog();
        Drain(dialog.PrehearAsync());

        dialog.SetSettings(fixture.Defaults with { IntensityPercent = 50 });
        Assert.IsFalse(fixture.Preview.IsPlaying);
        Drain(dialog.PrehearAsync());

        Assert.AreEqual(2, fixture.RenderCount);
        Assert.IsFalse(fixture.Audio.CanUndo);
        Assert.IsFalse(fixture.Audio.CanRedo);
    }

    [STATestMethod]
    public void StaleTarget_DoesNotGetOverwrittenByAccept()
    {
        using var fixture = new DialogFixture();
        using AutoTuneDialog dialog = fixture.Dialog();
        fixture.Audio.Data[0] = 0.9f;

        Drain(dialog.AcceptEffectAsync());

        Assert.AreEqual(0.9f, fixture.Audio.Data[0]);
        Assert.IsFalse(fixture.Audio.CanUndo);
        Assert.AreNotEqual(DialogResult.OK, dialog.DialogResult);
    }

    [STATestMethod]
    public void ClosingDuringRender_CancelsWithoutApplying()
    {
        using var fixture = new DialogFixture();
        CancellationToken observed = default;
        using AutoTuneDialog dialog = fixture.Dialog(async (settings, progress, token) =>
        {
            observed = token;
            await Task.Delay(Timeout.Infinite, token);
            return new([], 0, 0, 0, 1);
        });
        dialog.Show();
        Task work = dialog.AcceptEffectAsync();
        Assert.IsTrue(observed.CanBeCanceled);

        dialog.Close();
        Drain(work);

        Assert.IsTrue(observed.IsCancellationRequested);
        Assert.IsFalse(fixture.Audio.CanUndo);
    }

    [TestMethod]
    public void PreviewProvider_ReadsAndLoopsRamSamplesWithoutFiles()
    {
        var provider = new EffectPreviewSamples([1, 2, 3, 4], 8000, 2, loop: true);
        float[] output = new float[10];

        Assert.AreEqual(10, provider.Read(output));

        CollectionAssert.AreEqual(new float[] { 1, 2, 3, 4, 1, 2, 3, 4, 1, 2 }, output);
        Assert.AreEqual(8000, provider.WaveFormat.SampleRate);
        Assert.AreEqual(2, provider.WaveFormat.Channels);
    }

    [STATestMethod]
    public void FutureParameterizedEffectNames_UseSpaceEllipsis()
    {
        using ToolStripMenuItem parameters = EffectMenuFactory.Create("Auto Tune...", true, (_, _) => { });
        using ToolStripMenuItem immediate = EffectMenuFactory.Create("Invert", false, (_, _) => { });

        Assert.AreEqual("Auto Tune ...", parameters.Text);
        Assert.AreEqual("Invert", immediate.Text);
    }

    [STATestMethod]
    public void TrackViewMenu_OpensModelessForSelection_AndControlZYUndoRedoEffect()
    {
        using var fixture = new DialogFixture();
        fixture.Store.Save("Detailed", AutoTunePresetStoreTests.DetailedSettings);
        using var audio = new AudioObj
        {
            Data = [1, 2, 3, 4, 5, 6, 7, 8], Channels = 2, SampleRate = 8000, Length = 8, Name = "Selection",
            Bpm = 401, ScannedBpm = 131.25f
        };
        using var view = new TrackView(audio);
        AutoTuneDialog? opened = null;
        AutoTuneDialogContext? captured = null;
        try
        {
            view.OriginalAudio.SelectionStart = 2; view.OriginalAudio.SelectionEnd = 6;
            view.CreateAutoTuneDialog = context =>
            {
                captured = context;
                return opened = new AutoTuneDialog(context, fixture.Store, fixture.Preview, (_, _, token) =>
                    Task.FromResult(new AutoTuneResult(context.Session.Input.ToArray().Select(value => value * 2).ToArray(), 1, 1, 1, 1)));
            };
            var menu = (ContextMenuStrip)typeof(TrackView).GetField("contextMenu_waveform", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
            ToolStripMenuItem effects = menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "Apply Effect ...");
            ToolStripMenuItem tuning = effects.DropDownItems.OfType<ToolStripMenuItem>().Single(item => item.Text == "Pitch && Tuning");
            ToolStripMenuItem autoTune = tuning.DropDownItems.OfType<ToolStripMenuItem>().Single(item => item.Text == "Auto Tune ...");

            autoTune.PerformClick();
            Application.DoEvents();

            Assert.IsNotNull(opened);
            Assert.IsFalse(opened.Modal);
            Assert.AreSame(view, opened.Owner);
            Assert.IsNotNull(captured);
            Assert.IsTrue(captured.Session.IsSelection);
            Assert.AreEqual(4, captured.Session.SampleCount);
            Assert.AreEqual(131.25, captured.Defaults.TempoBpm);
            ComboBox presets = Descendants(opened).OfType<ComboBox>().Single(control => control.Name == "autoTunePresets");
            presets.SelectedItem = presets.Items.OfType<AutoTunePreset>().Single(preset => preset.BuiltInId == "soft");
            Assert.AreEqual(131.25, opened.CurrentSettings.TempoBpm);
            view.OriginalAudio.Bpm = 147.5f;
            presets.SelectedItem = UserPreset(presets, "Detailed");
            Assert.AreEqual(147.5, opened.CurrentSettings.TempoBpm, "The modeless dialog must reread the current TrackView metadata.");
            Drain(opened.AcceptEffectAsync());
            CollectionAssert.AreEqual(new float[] { 1, 2, 6, 8, 10, 12, 7, 8 }, view.OriginalAudio.Data);
            Assert.AreEqual(1, view.OriginalAudio.PreviousSteps.Count);

            MethodInfo keys = typeof(TrackView).GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)!;
            keys.Invoke(view, [Message.Create(view.Handle, 0, IntPtr.Zero, IntPtr.Zero), Keys.Control | Keys.Z]);
            CollectionAssert.AreEqual(audio.Data, view.OriginalAudio.Data);
            keys.Invoke(view, [Message.Create(view.Handle, 0, IntPtr.Zero, IntPtr.Zero), Keys.Control | Keys.Y]);
            CollectionAssert.AreEqual(new float[] { 1, 2, 6, 8, 10, 12, 7, 8 }, view.OriginalAudio.Data);
        }
        finally
        {
            opened?.Close(); view.Close();
            var watch = Stopwatch.StartNew();
            while (!view.IsDisposed && watch.Elapsed < TimeSpan.FromSeconds(5)) { Application.DoEvents(); Thread.Sleep(1); }
        }
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            yield return control;
            foreach (Control child in Descendants(control)) yield return child;
        }
    }

    private static AutoTunePreset UserPreset(ComboBox combo, string name) => combo.Items.OfType<AutoTunePreset>()
        .Single(preset => !preset.IsBuiltIn && preset.Name == name);

    private static void Drain(Task task)
    {
        var watch = Stopwatch.StartNew();
        while (!task.IsCompleted && watch.Elapsed < TimeSpan.FromSeconds(10)) { Application.DoEvents(); Thread.Sleep(1); }
        Assert.IsTrue(task.IsCompleted, "The modeless operation must finish or cancel without blocking the UI.");
        task.GetAwaiter().GetResult();
        Application.DoEvents();
    }

    private sealed class DialogFixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "ModularAudience-autotune-dialog-test-" + Guid.NewGuid().ToString("N"));
        internal AudioObj Audio { get; } = new() { Data = [0.1f, 0.2f, 0.3f, 0.4f], Channels = 1, SampleRate = 8000 };
        internal AutoTuneSettings Defaults { get; } = new();
        internal AutoTunePresetStore Store { get; }
        internal FakePreview Preview { get; } = new();
        internal int RenderCount { get; private set; }

        internal DialogFixture() { Directory.CreateDirectory(this.directory); this.Store = new(Path.Combine(this.directory, "AutoTunePresets.json")); }

        internal AutoTuneDialog Dialog(Func<AutoTuneSettings, IProgress<double>, CancellationToken, Task<AutoTuneResult>>? render = null)
        {
            AudioEffectEditSession session = AudioEffectEditSession.Capture(this.Audio);
            var context = new AutoTuneDialogContext(session, "Test", this.Defaults, (samples, token) =>
            {
                token.ThrowIfCancellationRequested(); session.Apply(this.Audio, samples); return Task.CompletedTask;
            }, ReadTrackTempo: () => (this.Audio.Bpm, this.Audio.ScannedBpm));
            return new AutoTuneDialog(context, this.Store, this.Preview, render ?? ((_, _, token) =>
            {
                token.ThrowIfCancellationRequested(); this.RenderCount++;
                return Task.FromResult(new AutoTuneResult(session.Input.ToArray().Select(value => value * 2).ToArray(), 1, 1, 1, 1));
            }));
        }

        public void Dispose() { this.Audio.Dispose(); Directory.Delete(this.directory, recursive: true); }
    }

    private sealed class FakePreview : IEffectPreviewPlayer
    {
        public bool IsPlaying { get; private set; }
        public void Play(float[] samples, int sampleRate, int channels, bool loop) => this.IsPlaying = true;
        public void Stop() => this.IsPlaying = false;
        public void Dispose() => this.Stop();
    }
}
