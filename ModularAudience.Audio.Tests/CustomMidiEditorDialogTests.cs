using System.Drawing;
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

        Assert.AreEqual(100m, bpm.Value);
        Assert.AreEqual(440m, pitch.Value);
        Assert.AreEqual(4m, resolution.Value);
        Assert.AreEqual("Sine", Field<Label>(editor, "label_instrument").Text);
        Assert.IsNotNull(instrument.ContextMenuStrip ?? Field<ContextMenuStrip>(editor, "instrumentMenu"));
        Assert.IsNotNull(Field<ContextMenuStrip>(editor, "contextMenuStrip_save"));
        Assert.IsFalse(editor.Controls.Find("button_import", true).Any());
        Assert.IsTrue(save.Enabled);
    }

    [TestMethod]
    public void RulerLabelSpacingAdaptsToZoomAndFormatsElapsedTime()
    {
        Assert.AreEqual(1, CustomMidiEditorDialog.GetBarLabelStride(200));
        Assert.AreEqual(4, CustomMidiEditorDialog.GetBarLabelStride(16));
        Assert.AreEqual(8, CustomMidiEditorDialog.GetBarLabelStride(8));
        Assert.AreEqual(1, CustomMidiEditorDialog.GetTimeLabelIntervalSeconds(160));
        Assert.AreEqual(200, CustomMidiEditorDialog.GetTimeLabelIntervalSeconds(0.64));
        Assert.AreEqual("62:05", CustomMidiEditorDialog.FormatTimeRulerLabel(3725));
    }

    [STATestMethod]
    public void EditorUsesAnExplicitInitialWorkingBpm()
    {
        using CustomMidiEditorDialog editor = new(initialBpm: 142);
        Assert.AreEqual(142m, Field<NumericUpDown>(editor, "numericUpDown_bpm").Value);
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

    [TestMethod]
    public void PlayFromTickPreviewClipsCrossingNotesAndShiftsLaterNotes()
    {
        MidiTrackData track = new() { Index = 2, Name = "Piano Roll" };
        track.Notes.AddRange(
        [
            new MidiNoteData { NoteNumber = 55, Channel = 0, Velocity = 90, StartTick = 20, DurationTicks = 60 },
            new MidiNoteData { NoteNumber = 60, Channel = 1, Velocity = 100, StartTick = 80, DurationTicks = 60 },
            new MidiNoteData { NoteNumber = 64, Channel = 2, Velocity = 80, StartTick = 160, DurationTicks = 20 }
        ]);
        MidiFileData source = MidiFileData.CreateGenerated([track], 960, 120, timeSignatureNumerator: 3, timeSignatureDenominator: 4);

        MidiFileData preview = CustomMidiEditorDialog.CreateMidiPreviewFromTick(source, track.Index, 100);
        MidiTrackData previewTrack = preview.Tracks.Single();

        Assert.AreEqual(2, previewTrack.Notes.Count);
        Assert.AreEqual((60, 1, 100, 0L, 40L), (
            previewTrack.Notes[0].NoteNumber,
            previewTrack.Notes[0].Channel,
            previewTrack.Notes[0].Velocity,
            previewTrack.Notes[0].StartTick,
            previewTrack.Notes[0].DurationTicks));
        Assert.AreEqual((64, 2, 80, 60L, 20L), (
            previewTrack.Notes[1].NoteNumber,
            previewTrack.Notes[1].Channel,
            previewTrack.Notes[1].Velocity,
            previewTrack.Notes[1].StartTick,
            previewTrack.Notes[1].DurationTicks));
        Assert.AreEqual(80, source.Tracks.Single().Notes[1].StartTick);
        Assert.AreEqual(960, preview.TicksPerQuarterNote);
        Assert.AreEqual(3, preview.TimeSignatureNumerator);
        Assert.AreEqual(4, preview.TimeSignatureDenominator);
    }

    [STATestMethod]
    public void RightClickOnEmptyPianoRollSpaceOpensPlayFromHereMenu()
    {
        using CustomMidiEditorDialog editor = new();
        PictureBox pianoRoll = Field<PictureBox>(editor, "pictureBox_editor");
        ContextMenuStrip menu = Field<ContextMenuStrip>(editor, "contextMenuStrip_playFromHere");
        Point click = new(80, 100);
        editor.CreateControl();
        pianoRoll.CreateControl();

        Invoke(editor, "pictureBox_editor_MouseDown", pianoRoll, new MouseEventArgs(MouseButtons.Right, 1, click.X, click.Y, 0));
        Invoke(editor, "pictureBox_editor_MouseUp", pianoRoll, new MouseEventArgs(MouseButtons.Right, 1, click.X, click.Y, 0));

        Assert.IsTrue(menu.Visible);
        Assert.AreEqual("Play from Here", menu.Items[0].Text);
        Assert.AreEqual((long)Invoke(editor, "PointToTick", click.X), Field<long>(editor, "playFromHereStartTick"));
        menu.Close();
    }

    [STATestMethod]
    public void RightClickOnNoteDeletesOnlyThatNoteWithoutOpeningMenu()
    {
        using CustomMidiEditorDialog editor = new();
        PictureBox pianoRoll = Field<PictureBox>(editor, "pictureBox_editor");
        ContextMenuStrip menu = Field<ContextMenuStrip>(editor, "contextMenuStrip_playFromHere");
        Point click = new(50, 150);
        int noteNumber = (int)Invoke(editor, "PointToNote", click.Y);
        MidiNoteData clickedNote = new()
        {
            NoteNumber = noteNumber,
            Channel = 0,
            Velocity = 100,
            StartTick = 0,
            DurationTicks = 480
        };
        MidiNoteData overlappingNote = new()
        {
            NoteNumber = noteNumber,
            Channel = 0,
            Velocity = 80,
            StartTick = 0,
            DurationTicks = 240
        };
        editor.SelectedTrack.Notes.AddRange([clickedNote, overlappingNote]);
        editor.SelectedTrack.ExtendLengthTo(480);
        editor.CreateControl();
        pianoRoll.CreateControl();

        Invoke(editor, "pictureBox_editor_MouseDown", pianoRoll, new MouseEventArgs(MouseButtons.Right, 1, click.X, click.Y, 0));
        Invoke(editor, "pictureBox_editor_MouseUp", pianoRoll, new MouseEventArgs(MouseButtons.Right, 1, click.X, click.Y, 0));

        Assert.AreEqual(1, editor.SelectedTrack.Notes.Count);
        Assert.AreSame(overlappingNote, editor.SelectedTrack.Notes.Single());
        Assert.IsFalse(menu.Visible);
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
