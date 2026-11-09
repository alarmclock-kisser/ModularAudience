using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Effects;
using ModularAudience.Forms.Effects;
using ModularAudience.Forms.Modules.Dialogs;
using ModularAudience.Forms.Modules;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class EffectPresetAndDialogTests
{
    [TestMethod]
    public async Task PerEffectStores_SeedIdempotentlyAndSaveLoadRenameDeleteWithoutCrossContamination()
    {
        using var files = new Files(); var store = files.Store(AudioEffectKind.Bitcrusher);
        var defaults = store.ListAvailable(); string seeded = File.ReadAllText(store.FilePath);
        Assert.AreEqual(seeded, File.ReadAllText(store.FilePath)); Assert.AreEqual(defaults.Count, store.ListAvailable().Count);
        AudioEffectSettings sound = AudioEffectCatalog.Get(AudioEffectKind.Bitcrusher).CreateDefaults().With(("Bits", 5), ("Mode", 2));
        store.Save("My crunch", sound); store.Save("Balanced", sound);
        Assert.IsTrue(sound.SoundEquals(new EffectPresetStore(AudioEffectKind.Bitcrusher, store.FilePath).Load("MY CRUNCH").Parameters));
        Assert.IsTrue(defaults[0].Parameters.SoundEquals(store.ListAvailable().Single(preset => preset.BuiltInId == "default").Parameters));
        EffectPreset renamed = store.Rename("My crunch", "Renamed crunch");
        Assert.AreEqual("Renamed crunch", renamed.Name); Assert.IsTrue(sound.SoundEquals(renamed.Parameters));
        Assert.ThrowsExactly<InvalidOperationException>(() => store.Rename("Renamed crunch", "Balanced"));
        Assert.IsTrue(store.Delete("RENAMED CRUNCH")); Assert.IsFalse(store.Delete("Renamed crunch"));
        Assert.IsTrue(files.Store(AudioEffectKind.Gain).ListAvailable().All(preset => preset.Parameters.Effect == AudioEffectKind.Gain));
        await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() => new EffectPresetStore(AudioEffectKind.Bitcrusher, store.FilePath).Save(null, sound))));
        Assert.AreEqual(12, store.List().Count(preset => preset.Name.StartsWith("Preset#", StringComparison.Ordinal)));
        Assert.AreEqual(13, store.List().Select(preset => preset.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.AreEqual(2, Directory.GetFiles(files.Directory).Length);
    }

    [TestMethod]
    public void InvalidPresetFile_AndWrongEffectAreNeverSilentlyOverwritten()
    {
        using var files = new Files(); var store = files.Store(AudioEffectKind.Chorus);
        File.WriteAllText(store.FilePath, "{ broken");
        Assert.ThrowsExactly<JsonException>(() => store.ListAvailable());
        Assert.ThrowsExactly<JsonException>(() => store.Save(null, AudioEffectCatalog.Get(AudioEffectKind.Chorus).CreateDefaults()));
        Assert.AreEqual("{ broken", File.ReadAllText(store.FilePath));
        Assert.ThrowsExactly<InvalidDataException>(() => store.Save("Wrong", AudioEffectCatalog.Get(AudioEffectKind.Gain).CreateDefaults()));
    }

    [STATestMethod]
    public void EveryParameterEditor_RoundTripsEveryFactoryPresetAndUsesCombosForSubtypes()
    {
        foreach (AudioEffectDefinition definition in AudioEffectCatalog.All.Where(effect => effect.OpensOptions && effect.Kind != AudioEffectKind.AutoTune))
        {
            using var editor = new EffectParameterEditor(definition);
            foreach (EffectPresetDefinition preset in definition.BuiltInPresets)
            {
                editor.ApplySettings(preset.Parameters);
                Assert.IsTrue(preset.Parameters.SoundEquals(editor.CaptureSettings()), definition.Name + "/" + preset.Name);
            }
            foreach (EffectParameter parameter in definition.Parameters.Where(parameter => parameter.Kind == EffectParameterKind.Choice))
                Assert.IsInstanceOfType<ComboBox>(Descendants(editor).Single(control => control.Name == parameter.Key));
        }
    }

    [STATestMethod]
    public void SharedPresetUi_LoadsRenamesDeletesAndPreservesUnsavedEditsOnRefresh()
    {
        using var files = new Files(); using var audio = TestAudio(); var store = files.Store(AudioEffectKind.Bitcrusher);
        using var editor = Editor(audio, store, new FakePreview());
        ComboBox presets = Descendants(editor).OfType<ComboBox>().Single(control => control.Name == "effectPresets");
        TextBox name = Descendants(editor).OfType<TextBox>().Single(control => control.Name == "presetName");
        AudioEffectSettings custom = editor.CurrentSettings.With(("Bits", 5), ("Mode", 2)); editor.SetSettings(custom);
        editor.SavePreset(); Assert.AreEqual("Preset#1", store.List().Single().Name);
        name.Text = "Custom crunch"; editor.RenamePreset();
        Assert.AreEqual("Custom crunch", store.List().Single().Name);
        editor.SetSettings(custom.With(("Bits", 7))); editor.LoadSelectedPreset(); Assert.IsTrue(custom.SoundEquals(editor.CurrentSettings));
        editor.SetSettings(custom.With(("Bits", 6)));
        store.Save("Other window", custom);
        typeof(ComboBox).GetMethod("OnDropDown", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(presets, [EventArgs.Empty]);
        Assert.AreEqual(6d, editor.CurrentSettings["Bits"]); Assert.AreEqual("Custom crunch", ((EffectPreset)presets.SelectedItem!).Name);
        presets.SelectedItem = presets.Items.OfType<EffectPreset>().Single(preset => preset.BuiltInId == "default");
        Assert.IsFalse(Descendants(editor).OfType<Button>().Single(button => button.Name == "renamePreset").Enabled);
        Assert.IsFalse(Descendants(editor).OfType<Button>().Single(button => button.Name == "deletePreset").Enabled);
        Assert.AreEqual(string.Empty, name.Text);
        presets.SelectedItem = presets.Items.OfType<EffectPreset>().Single(preset => !preset.IsBuiltIn && preset.Name == "Custom crunch");
        editor.DeletePreset(); Assert.IsTrue(AudioEffectCatalog.Get(AudioEffectKind.Bitcrusher).CreateDefaults().SoundEquals(editor.CurrentSettings));
        Assert.IsFalse(audio.CanUndo); Assert.IsFalse(audio.CanRedo);
    }

    [STATestMethod]
    public void PreviewAndCancelDoNotMutate_ApplyCommitsCachedVariableLengthResultAsOneUndoStep()
    {
        using var files = new Files(); using var audio = TestAudio(); audio.SelectionStart = 1; audio.SelectionEnd = 3;
        float[] before = (float[])audio.Data.Clone(); var preview = new FakePreview(); int rendered = 0, accepted = 0;
        AudioEffectEditSession session = AudioEffectEditSession.Capture(audio);
        var defaults = AudioEffectCatalog.Get(AudioEffectKind.TimeStretch).CreateDefaults();
        var context = new EffectDialogContext(session, "Selection", defaults, (result, token) =>
        { token.ThrowIfCancellationRequested(); session.Apply(audio, result); return Task.CompletedTask; });
        using var editor = new EffectOptionsEditor(context, () => accepted++, () => { }, files.Store(AudioEffectKind.TimeStretch), preview,
            (_, _, token) => { token.ThrowIfCancellationRequested(); rendered++; return Task.FromResult(new AudioEffectResult([8, 9, 10, 11], 1)); });
        Drain(editor.PrehearAsync());
        CollectionAssert.AreEqual(before, audio.Data); Assert.IsFalse(audio.CanUndo); Assert.IsTrue(preview.IsPlaying);
        Assert.AreEqual(4, preview.Samples!.Length); Assert.AreEqual(8000, preview.Rate);
        Drain(editor.AcceptEffectAsync());
        Assert.AreEqual(1, rendered); Assert.AreEqual(1, accepted); Assert.AreEqual(1, audio.PreviousSteps.Count);
        CollectionAssert.AreEqual(new float[] { 0.1f, 8, 9, 10, 11, 0.4f }, audio.Data);
        audio.Undo(); CollectionAssert.AreEqual(before, audio.Data);
        audio.Redo(); CollectionAssert.AreEqual(new float[] { 0.1f, 8, 9, 10, 11, 0.4f }, audio.Data);
    }

    [STATestMethod]
    public void ClosingOrChangingParametersCancelsRenderWithoutAudioHistory()
    {
        using var files = new Files(); using var audio = TestAudio(); CancellationToken observed = default;
        using var editor = Editor(audio, files.Store(AudioEffectKind.HallReverb), new FakePreview(), async (_, _, token) =>
        { observed = token; await Task.Delay(Timeout.Infinite, token); return new([], 1); });
        Task work = editor.AcceptEffectAsync(); editor.CancelWork(); Drain(work);
        Assert.IsTrue(observed.IsCancellationRequested); Assert.IsFalse(audio.CanUndo);
    }

    [STATestMethod]
    public void MalformedUserFile_StillOffersFactoryProfilesAndRuntimeThreadsStayIndependent()
    {
        using var files = new Files(); using var audio = TestAudio(); var store = files.Store(AudioEffectKind.Saturation);
        File.WriteAllText(store.FilePath, "{ invalid");
        using var editor = Editor(audio, store, new FakePreview());
        ComboBox presets = Descendants(editor).OfType<ComboBox>().Single(control => control.Name == "effectPresets");
        NumericUpDown threads = Descendants(editor).OfType<NumericUpDown>().Single(control => control.Name == "maxThreads");
        threads.Value = Environment.ProcessorCount;
        presets.SelectedIndex = 1;
        Assert.IsTrue(((EffectPreset)presets.SelectedItem!).Parameters.SoundEquals(editor.CurrentSettings));
        Assert.AreEqual((decimal)Environment.ProcessorCount, threads.Value);
        Assert.AreEqual("{ invalid", File.ReadAllText(store.FilePath));
    }

    [STATestMethod]
    public void EffectMenu_HasExactlyTheSharedRequestedHierarchyAndOnlyParameterActionsHaveEllipses()
    {
        var clicked = new List<AudioEffectKind>(); using ToolStripMenuItem root = EffectMenuFactory.CreateApplyMenu(clicked.Add);
        Assert.AreEqual("Apply Effect ...", root.Text); Assert.AreEqual(9, root.DropDownItems.Count);
        foreach (AudioEffectDefinition definition in AudioEffectCatalog.All)
        {
            var group = root.DropDownItems.OfType<ToolStripMenuItem>().Single(item => item.Text?.Replace("&&", "&") == definition.Group);
            var item = group.DropDownItems.OfType<ToolStripMenuItem>().Single(item => item.Tag is AudioEffectKind kind && kind == definition.Kind);
            Assert.AreEqual(definition.Name + (definition.OpensOptions ? " ..." : ""), item.Text);
            item.PerformClick();
        }
        CollectionAssert.AreEquivalent(Enum.GetValues<AudioEffectKind>(), clicked.ToArray());
    }

    [STATestMethod]
    public void TrackView_OpensSharedModelessEffectForSelectionAndCtrlZYRestoresRippleEdit()
    {
        using var files = new Files(); using var audio = new AudioObj
        { Data = [1, 2, 3, 4, 5, 6, 7, 8], Channels = 2, SampleRate = 8000, Length = 8, Name = "Ripple" };
        using var view = new TrackView(audio); Form? opened = null; EffectOptionsEditor? editor = null;
        try
        {
            view.OriginalAudio.SelectionStart = 2; view.OriginalAudio.SelectionEnd = 6;
            view.CreateEffectDialog = context =>
            {
                Assert.AreEqual(AudioEffectKind.TimeStretch, context.Defaults.Effect); Assert.IsTrue(context.Session.IsSelection);
                opened = new Form { Size = new Size(900, 600) };
                editor = new EffectOptionsEditor(context, opened.Close, opened.Close, files.Store(AudioEffectKind.TimeStretch), new FakePreview(),
                    (_, _, _) => Task.FromResult(new AudioEffectResult([30, 40, 50, 60, 70, 80], 2)));
                opened.Controls.Add(editor); return opened;
            };
            var menu = (ContextMenuStrip)typeof(TrackView).GetField("contextMenu_waveform", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(view)!;
            var command = menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Name == "menuItem_applyEffects")
                .DropDownItems.OfType<ToolStripMenuItem>().SelectMany(item => item.DropDownItems.OfType<ToolStripMenuItem>())
                .Single(item => item.Tag is AudioEffectKind.TimeStretch);
            command.PerformClick(); Application.DoEvents();
            Assert.IsNotNull(opened); Assert.IsNotNull(editor); Assert.IsFalse(opened.Modal); Assert.AreSame(view, opened.Owner);
            Drain(editor.AcceptEffectAsync());
            CollectionAssert.AreEqual(new float[] { 1, 2, 30, 40, 50, 60, 70, 80, 7, 8 }, view.OriginalAudio.Data);
            Assert.AreEqual(1, view.OriginalAudio.PreviousSteps.Count); Assert.AreEqual(8L, view.OriginalAudio.SelectionEnd);
            MethodInfo keys = typeof(TrackView).GetMethod("ProcessCmdKey", BindingFlags.NonPublic | BindingFlags.Instance)!;
            keys.Invoke(view, [Message.Create(view.Handle, 0, IntPtr.Zero, IntPtr.Zero), Keys.Control | Keys.Z]);
            CollectionAssert.AreEqual(audio.Data, view.OriginalAudio.Data); Assert.AreEqual(6L, view.OriginalAudio.SelectionEnd);
            keys.Invoke(view, [Message.Create(view.Handle, 0, IntPtr.Zero, IntPtr.Zero), Keys.Control | Keys.Y]);
            Assert.AreEqual(10, view.OriginalAudio.Data.Length); Assert.AreEqual(8L, view.OriginalAudio.SelectionEnd);
        }
        finally { opened?.Close(); view.Close(); Application.DoEvents(); }
    }

    private static AudioObj TestAudio() => new() { Data = [0.1f, 0.2f, 0.3f, 0.4f], Channels = 1, SampleRate = 8000, Length = 4 };
    private static EffectOptionsEditor Editor(AudioObj audio, EffectPresetStore store, FakePreview preview,
        Func<AudioEffectSettings, IProgress<double>, CancellationToken, Task<AudioEffectResult>>? render = null)
    {
        AudioEffectEditSession session = AudioEffectEditSession.Capture(audio);
        return new(new(session, "Test", AudioEffectCatalog.Get(store.Effect).CreateDefaults(), (result, token) =>
            { token.ThrowIfCancellationRequested(); session.Apply(audio, result); return Task.CompletedTask; }), () => { }, () => { }, store, preview, render);
    }
    private static IEnumerable<Control> Descendants(Control control)
    {
        foreach (Control child in control.Controls) { yield return child; foreach (Control descendant in Descendants(child)) yield return descendant; }
    }
    private static void Drain(Task task)
    {
        var watch = Stopwatch.StartNew();
        while (!task.IsCompleted && watch.Elapsed < TimeSpan.FromSeconds(15)) { Application.DoEvents(); Thread.Sleep(1); }
        Assert.IsTrue(task.IsCompleted); task.GetAwaiter().GetResult(); Application.DoEvents();
    }
    private sealed class FakePreview : IEffectPreviewPlayer
    {
        public bool IsPlaying { get; private set; }
        internal float[]? Samples; internal int Rate;
        public void Play(float[] samples, int sampleRate, int channels, bool loop) { this.Samples = samples; this.Rate = sampleRate; this.IsPlaying = true; }
        public void Stop() => this.IsPlaying = false;
        public void Dispose() => this.Stop();
    }
    private sealed class Files : IDisposable
    {
        internal string Directory { get; } = Path.Combine(Path.GetTempPath(), "ModularAudience-effects-test-" + Guid.NewGuid().ToString("N"));
        internal Files() => System.IO.Directory.CreateDirectory(this.Directory);
        internal EffectPresetStore Store(AudioEffectKind kind) => new(kind, Path.Combine(this.Directory, kind + "Presets.json"));
        public void Dispose() => System.IO.Directory.Delete(this.Directory, true);
    }
}
