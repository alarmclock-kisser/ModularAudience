using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Midi;
using ModularAudience.Audio.Synthesis;
using ModularAudience.Forms;
using ModularAudience.Forms.Modules.Dialogs;

namespace ModularAudience.Audio.Tests;

[TestClass]
[DoNotParallelize]
public sealed class CustomMidiEditorDialogTests
{
    private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    private static T Field<T>(object instance, string name) =>
        (T)instance.GetType().GetField(name, PrivateInstance)!.GetValue(instance)!;

    private static object Invoke(object instance, string name, params object?[] arguments) =>
        instance.GetType().GetMethod(name, PrivateInstance)!.Invoke(instance, arguments)!;

    [TestMethod]
    public void KeyRangeAlwaysKeepsAnOctaveAndStaysInsideMidiRange()
    {
        Assert.AreEqual((0, 12), CustomMidiEditorDialog.NormalizeKeyRange(5, 6, lowestChanged: false));
        Assert.AreEqual((115, 127), CustomMidiEditorDialog.NormalizeKeyRange(126, 127, lowestChanged: true));
        Assert.AreEqual((20, 32), CustomMidiEditorDialog.NormalizeKeyRange(20, 32, lowestChanged: true));
    }

    [STATestMethod]
    public void StandaloneEditorHasRequestedControlsAndNoImportAction()
    {
        using CustomMidiEditorDialog editor = new();
        NumericUpDown bpm = Field<NumericUpDown>(editor, "numericUpDown_bpm");
        NumericUpDown pitch = Field<NumericUpDown>(editor, "numericUpDown_pitchFrequency");
        NumericUpDown resolution = Field<NumericUpDown>(editor, "numericUpDown_resolution");
        Button instrument = Field<Button>(editor, "button_instrument");
        Button save = Field<Button>(editor, "button_save");

        Assert.AreEqual(120m, bpm.Value);
        Assert.AreEqual(440m, pitch.Value);
        Assert.AreEqual(4m, resolution.Value);
        Assert.AreEqual("Sine", Field<Label>(editor, "label_instrument").Text);
        Assert.IsNotNull(instrument.ContextMenuStrip ?? Field<ContextMenuStrip>(editor, "instrumentMenu"));
        Assert.IsNotNull(Field<ContextMenuStrip>(editor, "contextMenuStrip_save"));
        Assert.IsFalse(editor.Controls.Find("button_import", true).Any());
        Assert.IsTrue(save.Enabled);
    }

    [STATestMethod]
    public void KeyControlsListNotesInDescendingOrderAndKeepMidiNoteMapping()
    {
        using CustomMidiEditorDialog editor = new();
        DomainUpDown minKey = Field<DomainUpDown>(editor, "domainUpDown_minKey");
        DomainUpDown maxKey = Field<DomainUpDown>(editor, "domainUpDown_maxKey");
        string[] descendingNotes = Enumerable.Range(0, 128)
            .Reverse()
            .Select(CustomMidiEditorDialog.MidiNoteName)
            .ToArray();

        CollectionAssert.AreEqual(descendingNotes, minKey.Items.Cast<string>().ToArray());
        CollectionAssert.AreEqual(descendingNotes, maxKey.Items.Cast<string>().ToArray());
        Assert.AreEqual(CustomMidiEditorDialog.MidiNoteName(36), minKey.SelectedItem);
        Assert.AreEqual(CustomMidiEditorDialog.MidiNoteName(84), maxKey.SelectedItem);

        minKey.SelectedIndex++;
        maxKey.SelectedIndex++;
        Assert.AreEqual(CustomMidiEditorDialog.MidiNoteName(35), minKey.SelectedItem);
        Assert.AreEqual(CustomMidiEditorDialog.MidiNoteName(83), maxKey.SelectedItem);
        Assert.AreEqual(35, typeof(CustomMidiEditorDialog).GetProperty("LowestNote", PrivateInstance)!.GetValue(editor));
        Assert.AreEqual(83, typeof(CustomMidiEditorDialog).GetProperty("HighestNote", PrivateInstance)!.GetValue(editor));
    }

    [STATestMethod]
    public void ClipboardGhostPlacementAddsNotesAndSupportsUndoRedo()
    {
        using CustomMidiEditorDialog editor = new();
        MidiTrackData track = editor.SelectedTrack;
        track.Notes.Add(new MidiNoteData
        {
            NoteNumber = 60,
            Channel = 0,
            Velocity = 100,
            StartTick = 0,
            DurationTicks = 120
        });
        track.ExtendLengthTo(120);

        Invoke(editor, "StoreClipboardHistory", (object)new[] { track.Notes[0] });
        var history = Field<List<MidiClipboardEntry>>(editor, "clipboardHistory");
        Assert.AreEqual(1, history.Count);
        Assert.AreEqual(0, history[0].Notes[0].StartTick);
        Assert.AreEqual(0, history[0].Notes[0].NoteNumber);

        Invoke(editor, "UpdateGhostPreview", new Point(50, 20));
        Invoke(editor, "StartGhostPreview", history[0].Notes, false);
        Invoke(editor, "UpdateGhostPreview", new Point(500, 60));
        Invoke(editor, "PlaceGhostPreview");
        Assert.AreEqual(2, editor.SelectedTrack.Notes.Count);

        Invoke(editor, "Undo");
        Assert.AreEqual(1, editor.SelectedTrack.Notes.Count);
        Invoke(editor, "Redo");
        Assert.AreEqual(2, editor.SelectedTrack.Notes.Count);
    }

    [TestMethod]
    public void MidiEditorMenuEntryRunsModelessOpenActionCallback()
    {
        bool invoked = false;
        ToolStripMenuItem item = WindowMain.CreateMidiEditorMenuItem(() => invoked = true);
        Assert.AreEqual("Midi Editor", item.Text);
        Assert.AreEqual("toolStripMenuItem_midiEditor", item.Name);
        item.PerformClick();
        Assert.IsTrue(invoked);
        item.Dispose();
    }

    [TestMethod]
    public void CustomSynthMidiNotesUseBreakbeatDurationAndPitchMapping()
    {
        InstrumentSynthSettings source = new()
        {
            Family = SynthFamily.Keyboard,
            MidiNote = 60,
            Bpm = 120,
            NoteBeats = 1,
            DurationMultiplier = 1,
            ReleaseSeconds = 0.2
        };

        InstrumentSynthSettings pitched = BreakbeatPatternInstrumentRenderer.CreateSettingsForMidiNote(
            source,
            midiNote: 72,
            durationTicks: 480,
            ticksPerQuarterNote: 960,
            bpm: 90);

        Assert.AreEqual(72, pitched.MidiNote);
        Assert.AreEqual(0, pitched.PitchSemitones, 1e-12);
        Assert.AreEqual(90, pitched.Bpm, 1e-12);
        Assert.AreEqual(0.5, pitched.NoteBeats, 1e-12);
        Assert.AreEqual(1d / 3, pitched.GateSeconds, 1e-12);
        Assert.AreEqual(0.2, pitched.ReleaseSeconds, 1e-12);
    }

    [TestMethod]
    public void RenderedCollectionAndMelodyNamesContainTempoAndInstrumentIdentity()
    {
        Assert.AreEqual(
            "CustomMidi_[87.5 BPM]_Midi_Saw",
            CustomMidiEditorDialog.CreateCustomMidiCollectionName(87.5, MidiInstrument.Saw));
        Assert.AreEqual(
            "Midi_Saw_[87.5]_melody",
            CustomMidiEditorDialog.CreateMelodySampleBaseName(87.5, MidiInstrument.Saw));

        InstrumentSynthSettings custom = new()
        {
            Family = SynthFamily.SynthBassAndLead,
            VoiceIndex = 0
        };
        string collection = CustomMidiEditorDialog.CreateCustomMidiCollectionName(120, MidiInstrument.Sine, custom);
        string sample = CustomMidiEditorDialog.CreateMelodySampleBaseName(120, MidiInstrument.Sine, custom);
        StringAssert.Contains(collection, "120 BPM");
        StringAssert.Contains(collection, "Synth_Bass_Lead");
        StringAssert.Contains(sample, "Sub_Anchor");
        StringAssert.Contains(sample, "_melody");
    }

    [STATestMethod]
    public void RepeatedRenderedMelodiesReceiveUniqueRunSuffixes()
    {
        using AudioCollectionView collection = new([]);
        collection.AudioC.Audios.Add(new ModularAudience.Audio.AudioObj { Name = "Midi_Saw_[87.5]_melody" });
        string baseName = "Midi_Saw_[87.5]_melody";
        string unique = (string)typeof(CustomMidiEditorDialog)
            .GetMethod("CreateUniqueSampleName", PrivateInstance | BindingFlags.Static)!
            .Invoke(null, [collection, baseName])!;

        Assert.AreEqual("Midi_Saw_[87.5]_melody_2", unique);
    }
}
