using System.Collections;
using System.Reflection;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Forms.Modules.Dialogs;
using ModularAudience.Generators;

namespace ModularAudience.Audio.Tests;

[TestClass]
[DoNotParallelize]
public sealed class BreakbeatTrackPresetTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Assembly Forms = typeof(BreakbeatPatternEditorDialog).Assembly;
    private static Type TypeOf(string name) => Forms.GetType("ModularAudience.Forms.Modules.Dialogs." + name)!;
    private static object Field(object target, string name) => target.GetType().GetField(name, Private)!.GetValue(target)!;
    private static object? Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private)!.Invoke(target, args);
    private static object Get(object target, string name) => target.GetType().GetProperty(name)!.GetValue(target)!;
    private static void Set(object target, string name, object value) => target.GetType().GetProperty(name)!.SetValue(target, value);
    private static object[] Presets => ((IEnumerable)TypeOf("BreakbeatTrackPresets").GetProperty("All")!.GetValue(null)!).Cast<object>().ToArray();
    private static object Options() => Activator.CreateInstance(TypeOf("BreakbeatGenerateOptions"))!;
    private static object Defaults(object preset) => preset.GetType().GetMethod("Defaults")!.Invoke(preset, [Options()])!;
    private static object[] Hits(object preset, object options, int resolution = 32) =>
        ((IEnumerable)TypeOf("BreakbeatTrackPresets").GetMethod("Generate")!.Invoke(null,
            [preset, options, 0, (int)Get(options, "PhraseBars") * resolution - 1, resolution, new Random(87)])!).Cast<object>().ToArray();

    [TestMethod]
    public void PresetsHaveDistinctRhythmsReproducibleSeedsAndBoundedOptions()
    {
        Assert.IsTrue(Presets.Length >= 12);
        var signatures = new HashSet<string>();
        foreach (object preset in Presets)
        {
            object options = Defaults(preset);
            var hits = Hits(preset, options);
            Assert.IsTrue(hits.Length > 0, (string)Get(preset, "Name"));
            string signature = string.Join(",", hits.Select(h => Get(h, "Column")));
            Assert.IsTrue(signatures.Add(signature), "Presets must generate genuinely distinct rhythms.");
            Assert.AreEqual(signature, string.Join(",", Hits(preset, options).Select(h => Get(h, "Column"))));
            foreach (var hit in hits)
            {
                Assert.IsTrue((int)Get(hit, "Length") >= 1);
                Assert.IsTrue((int)Get(hit, "Length") <= (int)Get(options, "MaxNoteLengthSteps"));
                Assert.IsTrue(Math.Abs((float)Get(hit, "Pitch")) <= (float)Get(options, "PitchRangeSemitones"));
                Assert.IsTrue((float)Get(hit, "Volume") >= (float)Get(options, "VolumeMinPercent"));
                Assert.IsTrue((float)Get(hit, "Volume") <= (float)Get(options, "VolumeMaxPercent"));
                object kinds = Get(hit, "Kinds");
                foreach (string flag in new[] { "AllowTruncated", "AllowTimeStretch", "AllowVarispeed" })
                    if (!(bool)Get(options, flag)) Assert.IsFalse((bool)Get(kinds, flag));
            }
            Set(options, "DensityPercent", 0);
            Assert.HasCount(0, Hits(preset, options));
        }
    }

    [TestMethod]
    public void PitchPermissionsAndPhraseEndAnswersAreEffective()
    {
        foreach (object preset in Presets)
        {
            object options = Defaults(preset);
            Set(options, "AllowPitchedNotes", false);
            Assert.IsTrue(Hits(preset, options).All(h => (float)Get(h, "Pitch") == 0));
            Set(options, "AllowPitchedNotes", true); Set(options, "PitchChancePercent", 0);
            Assert.IsTrue(Hits(preset, options).All(h => (float)Get(h, "Pitch") == 0));
        }
        object roller = Presets.Single(p => (string)Get(p, "Name") == "JungleRoller");
        object rollerOptions = Defaults(roller);
        Set(rollerOptions, "DensityPercent", 100); Set(rollerOptions, "AmenFactorPercent", 0);
        Set(rollerOptions, "MotifRepeatPercent", 0);
        var columns = Hits(roller, rollerOptions).Select(h => (int)Get(h, "Column")).ToArray();
        Assert.IsTrue(columns.Count(c => c >= 120) > columns.Count(c => c >= 24 && c < 32), "Last-bar roll must use finer retriggers.");
        object tape = Presets.Single(p => (string)Get(p, "Name") == "TapeDive");
        var tapeHits = Hits(tape, Defaults(tape));
        Assert.IsTrue(tapeHits.All(h => (float)Get(h, "Pitch") <= 0));
        Assert.IsTrue((float)Get(tapeHits.Last(), "Pitch") < (float)Get(tapeHits.First(), "Pitch"));
    }

    [STATestMethod]
    public void NoneAndZeroIntensityUseExactlyTheLegacyGeneration()
    {
        using var sample = new AudioObj { Data = new float[480], SampleRate = 48000, Channels = 1 };
        using var editor = Editor(sample);
        var notes = (List<BreakbeatPatternNote>)Field(editor, "notes");
        object options = Options(); Set(options, "Seed", 123); Set(options, "AllowTruncated", true);
        Call(editor, "GeneratePatternInArea", 0, 0, 0, 63, options);
        var legacy = notes.ToArray(); notes.Clear();
        Set(options, "Preset", "JungleRoller"); Set(options, "PresetIntensityPercent", 0);
        Call(editor, "GeneratePatternInArea", 0, 0, 0, 63, options);
        CollectionAssert.AreEqual(legacy, notes.ToArray());
    }

    [STATestMethod]
    public void LegacyGenerationMixesAllEnabledKindsInsteadOfAlwaysHardCutting()
    {
        using var sample = new AudioObj { Data = new float[48000], SampleRate = 48000, Channels = 1 };
        using var editor = Editor(sample);
        var notes = (List<BreakbeatPatternNote>)Field(editor, "notes");
        object options = Options();
        Set(options, "Seed", 731); Set(options, "DensityPercent", 100); Set(options, "AmenFactorPercent", 0);
        Set(options, "MinNoteLengthSteps", 1); Set(options, "MaxNoteLengthSteps", 1);
        Set(options, "AllowTruncated", true); Set(options, "AllowTimeStretch", true); Set(options, "AllowVarispeed", true);
        Call(editor, "GeneratePatternInArea", 0, 0, 0, 63, options);
        Assert.IsTrue(notes.Any(n => n.Truncated), "Hard-cut notes must remain eligible.");
        Assert.IsTrue(notes.Any(n => n.TimeStretch), "Short blue notes must not be hidden by hard-cut priority.");
        Assert.IsTrue(notes.Any(n => n.Varispeed), "Short yellow notes must not be hidden by the track default.");
        Assert.IsTrue(notes.All(n => n.DurationTicks == 64));
        var snapshot = notes.ToArray(); notes.Clear();
        Call(editor, "GeneratePatternInArea", 0, 0, 0, 63, options);
        CollectionAssert.AreEqual(snapshot, notes.ToArray(), "Kind selection must respect the fixed seed.");

        Set(options, "AllowVarispeed", false); notes.Clear();
        Call(editor, "GeneratePatternInArea", 0, 0, 0, 63, options);
        Assert.IsFalse(notes.Any(n => n.Varispeed));
        Assert.IsTrue(notes.Any(n => n.TimeStretch));
        Set(options, "AllowTimeStretch", false); notes.Clear();
        Call(editor, "GeneratePatternInArea", 0, 0, 0, 63, options);
        Assert.IsTrue(notes.All(n => n.Truncated && !n.TimeStretch && !n.Varispeed));
    }

    [STATestMethod]
    public void RealEditorPresetsRespectAreaPermissionsAndFixedSeeds()
    {
        using var sample = new AudioObj { Data = new float[12000], SampleRate = 48000, Channels = 1 };
        using var editor = Editor(sample);
        var notes = (List<BreakbeatPatternNote>)Field(editor, "notes");
        foreach (object preset in Presets)
        {
            object options = Defaults(preset); Set(options, "Seed", 319);
            notes.Clear(); Call(editor, "GeneratePatternInArea", 0, 0, 4, 59, options);
            Assert.IsTrue(notes.Count > 0, (string)Get(preset, "Name"));
            Assert.IsTrue(notes.All(n => n.StartTick >= 256 && n.StartTick + n.DurationTicks <= 3840));
            if (!(bool)Get(options, "AllowPitchedNotes")) Assert.IsTrue(notes.All(n => n.PitchSemitones == 0));
            if (!(bool)Get(options, "AllowVarispeed")) Assert.IsTrue(notes.All(n => !n.Varispeed));
            if (!(bool)Get(options, "AllowTimeStretch")) Assert.IsTrue(notes.All(n => !n.TimeStretch));
            if (!(bool)Get(options, "AllowTruncated")) Assert.IsTrue(notes.All(n => !n.Truncated));
            var snapshot = notes.ToArray(); notes.Clear();
            Call(editor, "GeneratePatternInArea", 0, 0, 4, 59, options);
            CollectionAssert.AreEqual(snapshot, notes.ToArray());
        }
    }

    [STATestMethod]
    public void DialogSelectionLoadsDefaultsAllowsEditsAndCopiesPresetSettings()
    {
        object options = Options();
        using var dialog = (Form)Activator.CreateInstance(TypeOf("BreakbeatPatternGenerateDialog"), [options, null])!;
        var combo = (ComboBox)Field(dialog, "combo_preset");
        var intensity = (NumericUpDown)Field(dialog, "numeric_presetIntensity");
        Assert.AreEqual("- None -", combo.SelectedItem);
        Assert.IsFalse(intensity.Enabled);
        combo.SelectedIndex = 1;
        Assert.IsTrue(intensity.Enabled);
        Assert.AreEqual("AmenSnareLadder", Get(Get(dialog, "Options"), "Preset"));
        intensity.Value = 65;
        ((NumericUpDown)Field(dialog, "numeric_density")).Value = 23;
        object edited = Get(dialog, "Options");
        Assert.AreEqual(65, Get(edited, "PresetIntensityPercent"));
        Assert.AreEqual(23, Get(edited, "DensityPercent"));
        string json = JsonSerializer.Serialize(edited, edited.GetType());
        object restored = JsonSerializer.Deserialize(json, edited.GetType())!;
        Call(dialog, "ApplyOptions", restored);
        Assert.AreEqual(65m, intensity.Value);
        Assert.AreEqual(23m, ((NumericUpDown)Field(dialog, "numeric_density")).Value);
        combo.SelectedIndex = 0;
        Assert.IsFalse(intensity.Enabled);
        Assert.AreEqual(23, Get(Get(dialog, "Options"), "DensityPercent"));
    }

    [STATestMethod]
    public void PerTrackDialogDisablesOnlyItsOwnerAndCancelReenablesIt()
    {
        using var sample = new AudioObj { Data = new float[480], SampleRate = 48000, Channels = 1 };
        using var editor = Editor(sample);
        using var other = new Form();
        editor.Show(); other.Show();
        var bounds = (Rectangle)Call(editor, "AutoToggleBounds", 0)!;
        Call(editor, "TryClickAutoReroll", new MouseEventArgs(MouseButtons.Right, 1, bounds.X + 2, bounds.Y + 2, 0));
        var dialog = (Form)Field(editor, "trackGenerateDialog");
        Assert.AreSame(editor, dialog.Owner);
        Assert.IsFalse(dialog.Modal);
        Assert.IsFalse(editor.Enabled);
        Assert.IsTrue(other.Enabled);
        Assert.IsTrue(dialog.Enabled);
        dialog.DialogResult = DialogResult.Cancel; dialog.Close();
        Assert.IsTrue(editor.Enabled);
        Assert.IsTrue(other.Enabled);
        Assert.IsTrue(dialog.IsDisposed);

        Call(editor, "TryClickAutoReroll", new MouseEventArgs(MouseButtons.Right, 1, bounds.X + 2, bounds.Y + 2, 0));
        dialog = (Form)Field(editor, "trackGenerateDialog");
        ((ComboBox)Field(dialog, "combo_preset")).SelectedIndex = 2;
        dialog.DialogResult = DialogResult.OK; dialog.Close();
        Assert.IsTrue(editor.Enabled);
        var states = (IDictionary)Field(editor, "autoRerolls");
        var state = states.Values.Cast<object>().Single();
        object storedOptions = state.GetType().GetField("Options")!.GetValue(state)!;
        Assert.AreEqual("JungleRoller", Get(Get(storedOptions, "Generate"), "Preset"));
        Assert.IsTrue(((List<BreakbeatPatternNote>)Field(editor, "notes")).Count > 0);

        Call(editor, "TryClickAutoReroll", new MouseEventArgs(MouseButtons.Right, 1, bounds.X + 2, bounds.Y + 2, 0));
        dialog = (Form)Field(editor, "trackGenerateDialog");
        var oldNotes = ((List<BreakbeatPatternNote>)Field(editor, "notes")).ToArray();
        ((NumericUpDown)Field(dialog, "numeric_density")).Value = 0;
        dialog.DialogResult = DialogResult.OK; dialog.Close();
        Assert.HasCount(0, (List<BreakbeatPatternNote>)Field(editor, "notes"), "OK must regenerate even a populated track.");
        Call(editor, "UndoEditorAction");
        CollectionAssert.AreEqual(oldNotes, ((List<BreakbeatPatternNote>)Field(editor, "notes")).ToArray());

        Call(editor, "TryClickAutoReroll", new MouseEventArgs(MouseButtons.Right, 1, bounds.X + 2, bounds.Y + 2, 0));
        dialog = (Form)Field(editor, "trackGenerateDialog");
        editor.Close();
        Assert.IsTrue(dialog.IsDisposed, "Closing the editor must close its options window.");
        Assert.IsTrue(other.Enabled);
    }

    private static BreakbeatPatternEditorDialog Editor(AudioObj sample) => new(
        [new bool[64]], [sample], ["Test"], 4, 16, 0, 120, [], initialGridResolution: 16);
}
