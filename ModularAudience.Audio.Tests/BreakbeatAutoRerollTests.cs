using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Forms;
using ModularAudience.Forms.Modules.Dialogs;
using ModularAudience.Generators;
using System.Collections;
using System.Reflection;

namespace ModularAudience.Audio.Tests;

[TestClass]
[DoNotParallelize]
public sealed class BreakbeatAutoRerollTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object Field(object target, string name) => target.GetType().GetField(name, Private)!.GetValue(target)!;
    private static object? Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private)!.Invoke(target, args);

    private static void Configure(BreakbeatPatternEditorDialog editor, int row, int loops, bool ensemble, bool solo)
    {
        var samples = (List<AudioObj>)Field(editor, "samples");
        var states = (IDictionary)Field(editor, "autoRerolls");
        Type stateType = typeof(BreakbeatPatternEditorDialog).GetNestedType("TrackAutoRerollState", BindingFlags.NonPublic)!;
        object state = Activator.CreateInstance(stateType, true)!;
        object options = stateType.GetField("Options")!.GetValue(state)!;
        void Set(string key, object value) => options.GetType().GetProperty(key)!.SetValue(options, value);
        Set("Enabled", true); Set("Loops", loops); Set("SaveEnsemble", ensemble); Set("SaveSolo", solo);
        object generate = options.GetType().GetProperty("Generate")!.GetValue(options)!;
        void Gen(string key, object value) => generate.GetType().GetProperty(key)!.SetValue(generate, value);
        Gen("DensityPercent", 100); Gen("AmenFactorPercent", 0); Gen("MinNoteLengthSteps", 1); Gen("MaxNoteLengthSteps", 1);
        Gen("AllowTruncated", true); Gen("AllowPitchedNotes", false); Gen("VolumeMinPercent", 100f); Gen("VolumeMaxPercent", 100f);
        states[samples[row]] = state;
    }
    private static BreakbeatPatternEditorDialog Editor(AudioObj sample) => new(
        [new bool[16], new bool[16]], [sample, sample], ["One", "Two"], 1, 16, 0, 120,
        [new(0, 0, 24), new(1, 0, 24), new(0, 600, 24)], initialGridResolution: 16);

    [STATestMethod]
    public void NonLoopHearCountsCompletedPassesAndRerollsAtThresholdButNotOnCancel()
    {
        using var sample = new AudioObj { Data = new float[4800], SampleRate = 48000, Channels = 1 };
        using var editor = Editor(sample);
        Configure(editor, 0, 2, false, false);
        var states = (IDictionary)Field(editor, "autoRerolls");
        object state = states.Values.Cast<object>().Single();
        long Count() => (long)state.GetType().GetField("CompletedLoops")!.GetValue(state)!;
        Invoke(editor, "PrepareAutoRerollPlayback");
        Invoke(editor, "CompleteNonLoopHear", false); Assert.AreEqual(0L, Count());
        Invoke(editor, "CompleteNonLoopHear", true); Assert.AreEqual(1L, Count());
        Invoke(editor, "PrepareAutoRerollPlayback"); Assert.AreEqual(1L, Count());
        Invoke(editor, "CompleteNonLoopHear", true); Assert.AreEqual(0L, Count());
        Assert.IsTrue(((List<BreakbeatPatternNote>)Field(editor, "notes")).Any(n => n.TrackIndex == 0));
    }

    [STATestMethod]
    public void ManualPlacementDisablesOnlyItsTrackAndUndoRestoresReroll()
    {
        using var sample = new AudioObj { Data = new float[4800], SampleRate = 48000, Channels = 1 };
        using var editor = Editor(sample);
        Configure(editor, 0, 2, false, false); Configure(editor, 1, 3, false, false);
        Invoke(editor, "CommitHistoryAction");
        var grid = (Rectangle)Invoke(editor, "GetGridBounds")!;
        var picture = (Control)Field(editor, "pictureBox_pattern");
        Point point = new(grid.Left + grid.Width * 8 / 16 + 2, grid.Top + grid.Height / 4);
        Invoke(editor, "pictureBox_pattern_MouseDown", picture, new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0));
        Invoke(editor, "pictureBox_pattern_MouseUp", picture, new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0));
        bool Enabled(int row)
        {
            var states = (IDictionary)Field(editor, "autoRerolls");
            var samples = (List<AudioObj>)Field(editor, "samples");
            object state = states[samples[row]]!;
            object options = state.GetType().GetField("Options")!.GetValue(state)!;
            return (bool)options.GetType().GetProperty("Enabled")!.GetValue(options)!;
        }
        Assert.IsFalse(Enabled(0)); Assert.IsTrue(Enabled(1));
        Invoke(editor, "UndoEditorAction"); Assert.IsTrue(Enabled(0));
    }

    [STATestMethod]
    public void PerTrackIntervalsReplaceOnlySelectedLoopAndResetOnSelectionChange()
    {
        using var sample = new AudioObj { Data = Enumerable.Repeat(0.2f, 4800).ToArray(), SampleRate = 48000, Channels = 1 };
        using var editor = Editor(sample);
        var selected = (HashSet<int>)Field(editor, "selectedLoopSections"); selected.Add(0);
        Configure(editor, 0, 2, false, false); Configure(editor, 1, 3, false, false);
        Invoke(editor, "ResetAutoRerollClock");
        var notes = (List<BreakbeatPatternNote>)Field(editor, "notes");
        var before = notes.ToArray();
        Invoke(editor, "ObserveAutoRerollLoops", 1L);
        CollectionAssert.AreEqual(before, notes.ToArray());
        Invoke(editor, "ObserveAutoRerollLoops", 2L);
        Assert.IsTrue(notes.Contains(before.Single(n => n.TrackIndex == 1)));
        Assert.IsTrue(notes.Contains(before.Single(n => n.StartTick == 600)), "Unselected quarters must remain unchanged.");
        Assert.AreEqual(4, notes.Count(n => n.TrackIndex == 0 && n.StartTick < 256));
        Invoke(editor, "ObserveAutoRerollLoops", 3L);
        Assert.AreEqual(4, notes.Count(n => n.TrackIndex == 1 && n.StartTick < 256));
        Assert.IsTrue(notes.All(n => n.StartTick >= 256 || n.StartTick + n.DurationTicks <= 256));
        var snapshot = notes.ToArray();
        Invoke(editor, "ObserveAutoRerollLoops", 3L);
        CollectionAssert.AreEqual(snapshot, notes.ToArray(), "Same loop pass cannot trigger twice.");
        selected.Add(1);
        Invoke(editor, "ObserveAutoRerollLoops", 4L);
        CollectionAssert.AreEqual(snapshot, notes.ToArray(), "Changing the loop resets counters.");
    }

    [STATestMethod]
    public void CoincidentTrackAutosavesCreateOneEnsembleAndSeparateSolosAndDeduplicateIdenticalAudio()
    {
        using var sample = new AudioObj { Data = Enumerable.Repeat(0.1f, 4800).ToArray(), SampleRate = 48000, Channels = 1 };
        using var editor = Editor(sample);
        ((HashSet<int>)Field(editor, "selectedLoopSections")).Add(0);
        Configure(editor, 0, 1, true, true); Configure(editor, 1, 1, true, true);
        Invoke(editor, "ResetAutoRerollClock");
        Invoke(editor, "ObserveAutoRerollLoops", 1L);
        Drain((Task)Field(editor, "autoSaveWork"));
        var ensemble = (AudioCollectionView)Field(editor, "autoEnsembleCollection");
        var solos = ((IDictionary)Field(editor, "autoSoloCollections")).Values.Cast<AudioCollectionView>().ToArray();
        try
        {
            var ensembleAudio = ((AudioCollection)Field(ensemble, "AudioC")).Audios;
            var soloAudio = solos.Select(solo => ((AudioCollection)Field(solo, "AudioC")).Audios).ToArray();
            Assert.HasCount(1, ensembleAudio);
            Assert.HasCount(2, solos);
            foreach (var audios in soloAudio) Assert.HasCount(1, audios);
            Assert.AreEqual(0.5, ensembleAudio[0].Data.Length / (double)(ensembleAudio[0].SampleRate * ensembleAudio[0].Channels), 0.001);
            Assert.IsTrue(ensembleAudio[0].Data.Any(v => Math.Abs(v) > 0.01));
            Invoke(editor, "ObserveAutoRerollLoops", 2L);
            Drain((Task)Field(editor, "autoSaveWork"));
            Assert.HasCount(1, ensembleAudio, "Identical rendered ensembles must be deduplicated.");
            foreach (var audios in soloAudio) Assert.HasCount(2, audios);
        }
        finally
        {
            ensemble.Close(); ensemble.Dispose();
            foreach (var solo in solos) { solo.Close(); solo.Dispose(); }
        }
    }

    [STATestMethod]
    public void TrackConfigurationIsRestoredByUndoAndClonedWithTrack()
    {
        using var sample = new AudioObj { Data = new float[4800], SampleRate = 48000, Channels = 1 };
        using var editor = Editor(sample);
        Configure(editor, 0, 7, false, true);
        Invoke(editor, "CommitHistoryAction");
        Invoke(editor, "CloneSampleTrack", 0);
        var states = (IDictionary)Field(editor, "autoRerolls");
        var samples = (List<AudioObj>)Field(editor, "samples");
        Assert.IsTrue(states.Contains(samples[1]));
        Invoke(editor, "UndoEditorAction");
        Assert.HasCount(2, samples);
        Assert.IsTrue(states.Contains(samples[0]));
        var options = states[samples[0]]!.GetType().GetField("Options")!.GetValue(states[samples[0]])!;
        Assert.AreEqual(7, options.GetType().GetProperty("Loops")!.GetValue(options));
    }

    [STATestMethod]
    public void AutoGenerateDialogExposesIntervalAndSaveOptionsWithoutInterleaving()
    {
        Assembly assembly = typeof(BreakbeatPatternEditorDialog).Assembly;
        Type optionsType = assembly.GetType("ModularAudience.Forms.Modules.Dialogs.TrackAutoRerollOptions")!;
        object options = Activator.CreateInstance(optionsType)!;
        optionsType.GetProperty("Loops")!.SetValue(options, 9);
        optionsType.GetProperty("SaveSolo")!.SetValue(options, true);
        object generate = optionsType.GetProperty("Generate")!.GetValue(options)!;
        Type dialogType = assembly.GetType("ModularAudience.Forms.Modules.Dialogs.BreakbeatPatternGenerateDialog")!;
        using var dialog = (Form)Activator.CreateInstance(dialogType, [generate, options])!;
        Assert.AreEqual(9m, ((NumericUpDown)Field(dialog, "numeric_loops")).Value);
        Assert.IsTrue(((CheckBox)Field(dialog, "check_saveSolo")).Checked);
        Assert.IsNull(((CheckBox)Field(dialog, "check_interleaved")).Parent);
        Assert.AreEqual("Save new ensemble", ((CheckBox)Field(dialog, "check_saveEnsemble")).Text);
        object donor = Activator.CreateInstance(optionsType)!;
        optionsType.GetProperty("Loops")!.SetValue(donor, 12);
        optionsType.GetProperty("SaveEnsemble")!.SetValue(donor, true);
        object donorGenerate = optionsType.GetProperty("Generate")!.GetValue(donor)!;
        donorGenerate.GetType().GetProperty("DensityPercent")!.SetValue(donorGenerate, 75);
        Type tupleType = typeof(ValueTuple<,,>).MakeGenericType(typeof(int), typeof(string), optionsType);
        Array tracks = Array.CreateInstance(tupleType, 1);
        tracks.SetValue(Activator.CreateInstance(tupleType, [2, "Snare", donor]), 0);
        dialogType.GetMethod("SetOtherTrackOptions", Private)!.Invoke(dialog, [tracks]);
        var combo = (ComboBox)Field(dialog, "combo_applyFromTracks");
        Assert.AreEqual("#2 — Snare", combo.Items[0].ToString());
        combo.SelectedIndex = 0;
        Assert.AreEqual(12m, ((NumericUpDown)Field(dialog, "numeric_loops")).Value);
        Assert.AreEqual(75m, ((NumericUpDown)Field(dialog, "numeric_density")).Value);
        Assert.IsTrue(((CheckBox)Field(dialog, "check_saveEnsemble")).Checked);
        Assert.IsFalse(((CheckBox)Field(dialog, "check_saveSolo")).Checked);
        Assert.AreEqual(DialogResult.None, dialog.DialogResult, "Copying settings must not confirm or close the dialog.");
        Assert.AreEqual(9, optionsType.GetProperty("Loops")!.GetValue(options), "Original settings must remain unchanged until OK.");
    }

    [STATestMethod]
    public void ToggleOffStopsFutureRerolls()
    {
        using var sample = new AudioObj { Data = new float[4800], SampleRate = 48000, Channels = 1 };
        using var editor = Editor(sample);
        ((HashSet<int>)Field(editor, "selectedLoopSections")).Add(0);
        Configure(editor, 0, 1, false, false);
        Invoke(editor, "ResetAutoRerollClock");
        var bounds = (Rectangle)Invoke(editor, "AutoToggleBounds", 0)!;
        Assert.AreEqual(true, Invoke(editor, "TryClickAutoReroll", new MouseEventArgs(MouseButtons.Left, 1, bounds.X + 2, bounds.Y + 2, 0)));
        var notes = (List<BreakbeatPatternNote>)Field(editor, "notes");
        var before = notes.ToArray();
        Invoke(editor, "ObserveAutoRerollLoops", 1L);
        CollectionAssert.AreEqual(before, notes.ToArray());
    }

    [STATestMethod]
    public void EnablingEmptyTrackFillsImmediatelyButPreservesExistingTrackNotes()
    {
        using var sample = new AudioObj { Data = new float[4800], SampleRate = 48000, Channels = 1 };
        using var editor = Editor(sample);
        ((HashSet<int>)Field(editor, "selectedLoopSections")).Add(0);
        Configure(editor, 1, 8, false, false);
        var notes = (List<BreakbeatPatternNote>)Field(editor, "notes");
        notes.RemoveAll(n => n.TrackIndex == 1);
        var original = notes.ToArray();
        var samples = (List<AudioObj>)Field(editor, "samples");
        var states = (IDictionary)Field(editor, "autoRerolls");
        object options = states[samples[1]]!.GetType().GetField("Options")!.GetValue(states[samples[1]])!;
        Invoke(editor, "ActivateAutoReroll", 1, options);
        Assert.AreEqual(4, notes.Count(n => n.TrackIndex == 1));
        Assert.IsTrue(original.All(notes.Contains));
        var firstRoll = notes.ToArray();
        Invoke(editor, "ActivateAutoReroll", 1, options);
        CollectionAssert.AreEqual(firstRoll, notes.ToArray(), "Re-enabling populated tracks must not replace their notes.");
    }

    [STATestMethod]
    public void CounterClickResetsOnlyOneTrackAndRollButtonRegeneratesOnce()
    {
        using var sample = new AudioObj { Data = new float[4800], SampleRate = 48000, Channels = 1 };
        using var editor = Editor(sample);
        ((HashSet<int>)Field(editor, "selectedLoopSections")).Add(0);
        Configure(editor, 0, 8, false, false); Configure(editor, 1, 8, false, false);
        Invoke(editor, "ResetAutoRerollClock"); Invoke(editor, "ObserveAutoRerollLoops", 3L);
        var samples = (List<AudioObj>)Field(editor, "samples");
        var states = (IDictionary)Field(editor, "autoRerolls");
        long Count(int row) => (long)states[samples[row]]!.GetType().GetField("CompletedLoops")!.GetValue(states[samples[row]])!;
        void Click(string boundsMethod)
        {
            var bounds = (Rectangle)Invoke(editor, boundsMethod, 0)!;
            Assert.AreEqual(true, Invoke(editor, "TryClickAutoReroll", new MouseEventArgs(MouseButtons.Left, 1, bounds.X + 2, bounds.Y + 2, 0)));
        }
        Click("AutoCounterBounds");
        Assert.AreEqual(0L, Count(0)); Assert.AreEqual(3L, Count(1));
        Click("AutoRollBounds");
        var notes = (List<BreakbeatPatternNote>)Field(editor, "notes");
        Assert.AreEqual(4, notes.Count(n => n.TrackIndex == 0 && n.StartTick < 256));
        Assert.AreEqual(0L, Count(0)); Assert.AreEqual(3L, Count(1));
    }

    [STATestMethod]
    public void NormalStopRestartPreservesCountersButControlStopResetsAll()
    {
        using var sample = new AudioObj { Data = new float[4800], SampleRate = 48000, Channels = 1 };
        using var editor = Editor(sample);
        ((HashSet<int>)Field(editor, "selectedLoopSections")).Add(0);
        Configure(editor, 0, 8, false, false); Configure(editor, 1, 8, false, false);
        Invoke(editor, "ResetAutoRerollClock"); Invoke(editor, "ObserveAutoRerollLoops", 3L);
        var states = (IDictionary)Field(editor, "autoRerolls");
        long[] Counts() => states.Values.Cast<object>().Select(s => (long)s.GetType().GetField("CompletedLoops")!.GetValue(s)!).ToArray();
        Invoke(editor, "StopAutoRerollPlayback", false);
        Invoke(editor, "PrepareAutoRerollPlayback");
        CollectionAssert.AreEqual(new long[] { 3, 3 }, Counts());
        Invoke(editor, "ObserveAutoRerollLoops", 1L);
        CollectionAssert.AreEqual(new long[] { 4, 4 }, Counts());
        Invoke(editor, "StopAutoRerollPlayback", true);
        CollectionAssert.AreEqual(new long[] { 0, 0 }, Counts());
    }

    private static void Drain(Task task)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(15);
        while (!task.IsCompleted && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(5); }
        Assert.IsTrue(task.IsCompleted, "Autosave must finish."); task.GetAwaiter().GetResult();
    }

    [STATestMethod]
    public void ApplyForAllActivatesIndependentSettingsAndClearAllIsUndoable()
    {
        using var sample = new AudioObj { Data = new float[4800], SampleRate = 48000, Channels = 1 };
        using var editor = Editor(sample);
        Configure(editor, 0, 7, false, false);
        var samples = (List<AudioObj>)Field(editor, "samples");
        var states = (IDictionary)Field(editor, "autoRerolls");
        object options = states[samples[0]]!.GetType().GetField("Options")!.GetValue(states[samples[0]])!;
        Invoke(editor, "ActivateAutoRerollForAll", options);
        Assert.AreEqual(2, states.Count);
        object second = states[samples[1]]!.GetType().GetField("Options")!.GetValue(states[samples[1]])!;
        Assert.AreEqual(true, second.GetType().GetProperty("Enabled")!.GetValue(second));
        Assert.AreEqual(7, second.GetType().GetProperty("Loops")!.GetValue(second));
        Assert.AreNotSame(options, second);
        var notes = (List<BreakbeatPatternNote>)Field(editor, "notes");
        var before = notes.ToArray();
        Invoke(editor, "ClearAllPatternNotes");
        Assert.HasCount(0, notes);
        Invoke(editor, "UndoEditorAction");
        CollectionAssert.AreEqual(before, notes.ToArray());
    }
}
