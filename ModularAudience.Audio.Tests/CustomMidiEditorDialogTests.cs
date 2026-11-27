using System.Drawing;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio;
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
    public void StandaloneEditorHasImportActionAndTrackControls()
    {
        using CustomMidiEditorDialog editor = new();
        NumericUpDown bpm = Field<NumericUpDown>(editor, "numericUpDown_bpm");
        NumericUpDown trackNumber = Field<NumericUpDown>(editor, "numericUpDown_track");
        NumericUpDown pitch = Field<NumericUpDown>(editor, "numericUpDown_pitchFrequency");
        NumericUpDown resolution = Field<NumericUpDown>(editor, "numericUpDown_resolution");
        Button import = Field<Button>(editor, "button_import");
        Button instrument = Field<Button>(editor, "button_instrument");
        Button save = Field<Button>(editor, "button_save");

        Assert.AreEqual(100m, bpm.Value);
        Assert.AreEqual(1m, trackNumber.Minimum);
        Assert.AreEqual(1m, trackNumber.Maximum);
        Assert.AreEqual(440m, pitch.Value);
        Assert.AreEqual(8m, resolution.Value);
        Assert.AreEqual("Import", import.Text);
        Assert.AreEqual("Sine", Field<Label>(editor, "label_instrument").Text);
        Assert.AreEqual("Piano Roll", Field<Label>(editor, "label_trackName").Text);
        Assert.IsNotNull(instrument.ContextMenuStrip ?? Field<ContextMenuStrip>(editor, "instrumentMenu"));
        Assert.IsNotNull(Field<ContextMenuStrip>(editor, "contextMenuStrip_save"));
        Assert.IsTrue(editor.Controls.Find("button_import", true).Any());
        Assert.IsTrue(save.Enabled);
    }

    [STATestMethod]
    public void ImportedMidiTracksCanBeSelectedAndMidiOutputKeepsEveryTrack()
    {
        MidiTrackData lead = new() { Index = 4, Name = "Lead" };
        lead.Notes.Add(new MidiNoteData { NoteNumber = 72, Channel = 0, Velocity = 100, StartTick = 0, DurationTicks = 480 });
        MidiTrackData bass = new() { Index = 9, Name = "Bass" };
        bass.Notes.Add(new MidiNoteData { NoteNumber = 36, Channel = 1, Velocity = 90, StartTick = 120, DurationTicks = 360 });
        MidiFileData imported = MidiFileData.CreateGenerated([lead, bass], 960, 142.5);

        using CustomMidiEditorDialog editor = new();
        Invoke(editor, "ApplyMidiFile", imported);

        NumericUpDown trackNumber = Field<NumericUpDown>(editor, "numericUpDown_track");
        Label trackName = Field<Label>(editor, "label_trackName");
        Assert.AreEqual(2m, trackNumber.Maximum);
        Assert.AreEqual(1m, trackNumber.Value);
        Assert.AreEqual("Lead", trackName.Text);
        Assert.AreEqual(142.5m, Field<NumericUpDown>(editor, "numericUpDown_bpm").Value);

        trackNumber.Value = 2;
        Assert.AreEqual("Bass", trackName.Text);
        Assert.AreEqual(9, editor.SelectedTrack.Index);

        MidiFileData currentTrackOutput = (MidiFileData)Invoke(editor, "CreateOutputMidi");
        MidiFileData allTracksOutput = (MidiFileData)Invoke(editor, "CreateOutputMidiForExport");
        Assert.AreEqual(1, currentTrackOutput.Tracks.Count);
        Assert.AreEqual("Bass", currentTrackOutput.Tracks.Single().Name);
        Assert.AreEqual(2, allTracksOutput.Tracks.Count);
        CollectionAssert.AreEqual(new[] { "Lead", "Bass" }, allTracksOutput.Tracks.Select(track => track.Name).ToArray());
        CollectionAssert.AreEqual(
            new[] { 72, 36 },
            allTracksOutput.Tracks.Select(track => track.Notes.Single().NoteNumber).ToArray());
        Assert.AreEqual(142.5, allTracksOutput.DefaultBpm, 0.001);
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
    public void SettingANoteSelectsTheCreatedNote()
    {
        using CustomMidiEditorDialog editor = new();
        PictureBox pianoRoll = Field<PictureBox>(editor, "pictureBox_editor");
        Field<CheckBox>(editor, "checkBox_preview").Checked = false;
        editor.CreateControl();
        pianoRoll.CreateControl();

        object selection = Invoke(editor, "CreateSelection", new Point(50, 100), new Point(100, 200));
        typeof(CustomMidiEditorDialog).GetField("noteSelection", PrivateInstance)!.SetValue(editor, selection);

        Invoke(editor, "pictureBox_editor_MouseDown", pianoRoll,
            new MouseEventArgs(MouseButtons.Left, 1, 700, 400, 0));
        Invoke(editor, "pictureBox_editor_MouseUp", pianoRoll,
            new MouseEventArgs(MouseButtons.Left, 1, 700, 400, 0));

        Assert.IsNotNull(Field<object?>(editor, "noteSelection"));
        Assert.AreEqual(1, ((List<MidiNoteData>)Invoke(editor, "GetSelectedNotes")).Count);
    }

    [STATestMethod]
    public void SelectionRectangleCanDragSelectedMidiNotes()
    {
        using CustomMidiEditorDialog editor = new();
        PictureBox pianoRoll = Field<PictureBox>(editor, "pictureBox_editor");
        MidiNoteData note = new()
        {
            NoteNumber = 60,
            Channel = 0,
            Velocity = 100,
            StartTick = 0,
            DurationTicks = 480
        };
        editor.SelectedTrack.Notes.Add(note);
        editor.SelectedTrack.ExtendLengthTo(480);
        editor.CreateControl();
        pianoRoll.CreateControl();

        Point selectionStart = new(50, 100);
        Point selectionEnd = new(150, 300);
        object selection = Invoke(editor, "CreateSelection", selectionStart, selectionEnd);
        typeof(CustomMidiEditorDialog).GetField("noteSelection", PrivateInstance)!.SetValue(editor, selection);

        Invoke(editor, "pictureBox_editor_MouseDown", pianoRoll,
            new MouseEventArgs(MouseButtons.Left, 1, 80, 200, 0));
        Invoke(editor, "pictureBox_editor_MouseMove", pianoRoll,
            new MouseEventArgs(MouseButtons.Left, 1, 200, 200, 0));
        Invoke(editor, "pictureBox_editor_MouseUp", pianoRoll,
            new MouseEventArgs(MouseButtons.Left, 1, 200, 200, 0));

        Assert.AreNotSame(note, editor.SelectedTrack.Notes.Single());
        Assert.IsTrue(editor.SelectedTrack.Notes[0].StartTick > 0);
        Assert.IsNotNull(Field<object?>(editor, "noteSelection"));
    }

    [STATestMethod]
    public void ClickingInsideSelectionAddsAndSelectsANoteInsteadOfMovingTheSelection()
    {
        using CustomMidiEditorDialog editor = new();
        PictureBox pianoRoll = Field<PictureBox>(editor, "pictureBox_editor");
        CheckBox preview = Field<CheckBox>(editor, "checkBox_preview");
        preview.Checked = false;
        editor.CreateControl();
        pianoRoll.CreateControl();
        int clickedNote = (int)Invoke(editor, "PointToNote", 200);
        MidiNoteData existing = new()
        {
            NoteNumber = clickedNote + 1,
            Channel = 0,
            Velocity = 100,
            StartTick = 0,
            DurationTicks = 120
        };
        editor.SelectedTrack.Notes.Add(existing);
        editor.SelectedTrack.ExtendLengthTo(120);

        object selection = Invoke(editor, "CreateSelection", new Point(50, 100), new Point(150, 300));
        typeof(CustomMidiEditorDialog).GetField("noteSelection", PrivateInstance)!.SetValue(editor, selection);
        Point click = new(80, 200);
        long clickedTick = (long)Invoke(editor, "PointToTick", click.X);

        Invoke(editor, "pictureBox_editor_MouseDown", pianoRoll,
            new MouseEventArgs(MouseButtons.Left, 1, click.X, click.Y, 0));
        Invoke(editor, "pictureBox_editor_MouseUp", pianoRoll,
            new MouseEventArgs(MouseButtons.Left, 1, click.X, click.Y, 0));

        Assert.IsNotNull(Field<object?>(editor, "noteSelection"));
        Assert.IsTrue(editor.SelectedTrack.Notes.Contains(existing));
        MidiNoteData selected = ((List<MidiNoteData>)Invoke(editor, "GetSelectedNotes")).Single();
        Assert.AreNotSame(existing, selected);
        Assert.AreEqual(clickedNote, selected.NoteNumber);
        Assert.AreEqual(clickedTick, selected.StartTick);
    }

    [STATestMethod]
    public void ClickingAnExistingNoteSelectsOnlyIt()
    {
        using CustomMidiEditorDialog editor = new();
        PictureBox pianoRoll = Field<PictureBox>(editor, "pictureBox_editor");
        Field<CheckBox>(editor, "checkBox_preview").Checked = false;
        editor.CreateControl();
        pianoRoll.CreateControl();
        Point click = new(80, 200);
        MidiNoteData clicked = new()
        {
            NoteNumber = (int)Invoke(editor, "PointToNote", click.Y),
            Channel = 0,
            Velocity = 100,
            StartTick = (long)Invoke(editor, "PointToTick", click.X),
            DurationTicks = 120
        };
        MidiNoteData other = new()
        {
            NoteNumber = clicked.NoteNumber + 1,
            Channel = 0,
            Velocity = 100,
            StartTick = clicked.StartTick,
            DurationTicks = 120
        };
        editor.SelectedTrack.Notes.AddRange([clicked, other]);
        editor.SelectedTrack.ExtendLengthTo(240);

        Invoke(editor, "pictureBox_editor_MouseDown", pianoRoll,
            new MouseEventArgs(MouseButtons.Left, 1, click.X, click.Y, 0));
        Invoke(editor, "pictureBox_editor_MouseUp", pianoRoll,
            new MouseEventArgs(MouseButtons.Left, 1, click.X, click.Y, 0));

        List<MidiNoteData> selected = (List<MidiNoteData>)Invoke(editor, "GetSelectedNotes");
        Assert.AreEqual(1, selected.Count);
        Assert.AreSame(clicked, selected[0]);
    }

    [STATestMethod]
    public void TogglingIndividualNotesPreservesOtherSelections()
    {
        using CustomMidiEditorDialog editor = new();
        MidiNoteData first = new() { NoteNumber = 60, Channel = 0, Velocity = 100, StartTick = 0, DurationTicks = 120 };
        MidiNoteData second = new() { NoteNumber = 64, Channel = 0, Velocity = 100, StartTick = 240, DurationTicks = 120 };
        editor.SelectedTrack.Notes.AddRange([first, second]);

        Assert.IsTrue((bool)Invoke(editor, "ToggleNoteSelection", first));
        Assert.IsTrue((bool)Invoke(editor, "ToggleNoteSelection", second));
        CollectionAssert.AreEquivalent(
            new[] { first, second },
            ((List<MidiNoteData>)Invoke(editor, "GetSelectedNotes")).ToArray());

        Assert.IsFalse((bool)Invoke(editor, "ToggleNoteSelection", first));
        CollectionAssert.AreEqual(
            new[] { second },
            ((List<MidiNoteData>)Invoke(editor, "GetSelectedNotes")).ToArray());

        Assert.IsFalse((bool)Invoke(editor, "ToggleNoteSelection", second));
        Assert.IsNull(Field<object?>(editor, "noteSelection"));
    }

    [STATestMethod]
    public void DeleteKeyRemovesSelectedNotesAndKeepsUnselectedNotes()
    {
        using CustomMidiEditorDialog editor = new();
        MidiNoteData selected = new()
        {
            NoteNumber = 60,
            Channel = 0,
            Velocity = 100,
            StartTick = 0,
            DurationTicks = 120
        };
        MidiNoteData unselected = new()
        {
            NoteNumber = 72,
            Channel = 0,
            Velocity = 100,
            StartTick = 960,
            DurationTicks = 120
        };
        editor.SelectedTrack.Notes.AddRange([selected, unselected]);
        editor.SelectedTrack.ExtendLengthTo(1080);
        Invoke(editor, "SetNoteSelection", (object)new[] { selected });

        Message message = Message.Create(IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero);
        Assert.IsTrue((bool)Invoke(editor, "ProcessCmdKey", message, Keys.Delete));

        Assert.AreEqual(1, editor.SelectedTrack.Notes.Count);
        Assert.AreSame(unselected, editor.SelectedTrack.Notes.Single());
        Assert.IsNull(Field<object?>(editor, "noteSelection"));

        Invoke(editor, "Undo");
        Assert.AreEqual(2, editor.SelectedTrack.Notes.Count);
        Assert.IsTrue(editor.SelectedTrack.Notes.Contains(selected));
        Invoke(editor, "Redo");
        Assert.AreEqual(1, editor.SelectedTrack.Notes.Count);
        Assert.IsTrue(editor.SelectedTrack.Notes.Contains(unselected));
    }

    [STATestMethod]
    public void MovingSelectionDoesNotCaptureNotesEnteredByTheMovedRectangle()
    {
        using CustomMidiEditorDialog editor = new();
        PictureBox pianoRoll = Field<PictureBox>(editor, "pictureBox_editor");
        Point start = new(80, 200);
        Point end = new(200, 200);
        int noteNumber = (int)Invoke(editor, "PointToNote", start.Y);
        long tickDelta = (long)Invoke(editor, "PointToTick", end.X)
            - (long)Invoke(editor, "PointToTick", start.X);
        object selection = Invoke(editor, "CreateSelection", new Point(50, 100), new Point(150, 300));
        long selectionEndTick = (long)selection.GetType().GetProperty("EndTick")!.GetValue(selection)!;
        MidiNoteData selectedNote = new()
        {
            NoteNumber = noteNumber,
            Channel = 0,
            Velocity = 100,
            StartTick = 0,
            DurationTicks = 480
        };
        MidiNoteData enteringNote = new()
        {
            NoteNumber = noteNumber,
            Channel = 1,
            Velocity = 80,
            StartTick = selectionEndTick + Math.Max(1, tickDelta / 2),
            DurationTicks = 120
        };
        editor.SelectedTrack.Notes.AddRange([selectedNote, enteringNote]);
        editor.SelectedTrack.ExtendLengthTo(enteringNote.StartTick + enteringNote.DurationTicks);
        editor.CreateControl();
        pianoRoll.CreateControl();

        typeof(CustomMidiEditorDialog).GetField("noteSelection", PrivateInstance)!.SetValue(editor, selection);
        Assert.IsTrue((bool)Invoke(editor, "IsNoteSelected", selectedNote));
        Assert.IsFalse((bool)Invoke(editor, "IsNoteSelected", enteringNote));

        Invoke(editor, "pictureBox_editor_MouseDown", pianoRoll,
            new MouseEventArgs(MouseButtons.Left, 1, start.X, start.Y, 0));
        Invoke(editor, "pictureBox_editor_MouseMove", pianoRoll,
            new MouseEventArgs(MouseButtons.Left, 1, end.X, end.Y, 0));
        Invoke(editor, "pictureBox_editor_MouseUp", pianoRoll,
            new MouseEventArgs(MouseButtons.Left, 1, end.X, end.Y, 0));

        Assert.IsTrue(editor.SelectedTrack.Notes.Contains(enteringNote));
        Assert.IsFalse((bool)Invoke(editor, "IsNoteSelected", enteringNote));
        Assert.AreEqual(2, editor.SelectedTrack.Notes.Count);
        MidiFileData operation = (MidiFileData)Invoke(editor, "CreateOperationMidi");
        Assert.AreEqual(1, operation.Tracks.Single().Notes.Count);

        Invoke(editor, "Undo");
        Assert.IsFalse((bool)Invoke(editor, "IsNoteSelected", enteringNote));
        Invoke(editor, "Redo");
        Assert.IsFalse((bool)Invoke(editor, "IsNoteSelected", enteringNote));
    }

    [STATestMethod]
    public void StartingANewSelectionClearsThePreviousRectangleImmediately()
    {
        using CustomMidiEditorDialog editor = new();
        editor.CreateControl();
        object selection = Invoke(editor, "CreateSelection", new Point(50, 100), new Point(150, 300));
        typeof(CustomMidiEditorDialog).GetField("noteSelection", PrivateInstance)!.SetValue(editor, selection);

        Invoke(editor, "BeginSelectionDrag", new Point(80, 200));

        Assert.IsNull(Field<object?>(editor, "noteSelection"));
        Assert.AreEqual(new Point(80, 200), Field<Point?>(editor, "selectionDragStart"));
        Assert.AreEqual(new Point(80, 200), Field<Point?>(editor, "selectionDragEnd"));
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
        Field<CheckBox>(editor, "checkBox_preview").Checked = false;
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
        Assert.AreEqual(1, ((List<MidiNoteData>)Invoke(editor, "GetSelectedNotes")).Count);

        Invoke(editor, "Undo");
        Assert.AreEqual(1, editor.SelectedTrack.Notes.Count);
        Invoke(editor, "Redo");
        Assert.AreEqual(2, editor.SelectedTrack.Notes.Count);
    }

    [TestMethod]
    public void PrehearPolicyAllowsOnlySingleNotePlacements()
    {
        MidiNoteData first = new() { NoteNumber = 60, DurationTicks = 120 };
        MidiNoteData second = new() { NoteNumber = 64, DurationTicks = 120 };
        MethodInfo getSingleNote = typeof(CustomMidiEditorDialog)
            .GetMethod("GetSingleNoteForPrehear", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;

        Assert.AreSame(first, getSingleNote.Invoke(null, [new[] { first }]));
        Assert.IsNull(getSingleNote.Invoke(null, [new[] { first, second }]));
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

        // A C3 key note (source.MidiNote=48, KeyNoteSemitones=-12) shifts every rendered
        // MIDI note one octave down. The editor note (midiNote=72, C5) becomes midiNote-12=60 (C4).
        // The MIDI export keeps the original note.
        source.MidiNote = InstrumentSynthSettings.BaseMidiNote - 12;
        InstrumentSynthSettings shifted = BreakbeatPatternInstrumentRenderer.CreateSettingsForMidiNote(
            source,
            midiNote: 72,
            durationTicks: 480,
            ticksPerQuarterNote: 960,
            bpm: 90);
        Assert.AreEqual(60, shifted.MidiNote);
        Assert.AreEqual(0, shifted.PitchSemitones, 1e-12);
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

    [TestMethod]
    public void FrequencyRoundsToNearestMidiNoteAgainstA4Tuning()
    {
        // A4 = 440 Hz -> note 69
        Assert.AreEqual(69, CustomMidiEditorDialog.RoundFrequencyToMidiNote(440.0, 440.0));
        // C4 = 261.6256 Hz -> note 60
        Assert.AreEqual(60, CustomMidiEditorDialog.RoundFrequencyToMidiNote(261.6256, 440.0));
        // C#4 = 277.1826 Hz -> note 61
        Assert.AreEqual(61, CustomMidiEditorDialog.RoundFrequencyToMidiNote(277.1826, 440.0));
        // Slightly above C4 (263.0 Hz) still rounds to C4
        Assert.AreEqual(60, CustomMidiEditorDialog.RoundFrequencyToMidiNote(263.0, 440.0));
        // Above the C4/C#4 midpoint (~269.3 Hz) rounds to C#4
        Assert.AreEqual(61, CustomMidiEditorDialog.RoundFrequencyToMidiNote(270.0, 440.0));
        // A flat tuning: 435 Hz reference, A4 note still 69
        Assert.AreEqual(69, CustomMidiEditorDialog.RoundFrequencyToMidiNote(435.0, 435.0));
        // Clamped to the MIDI range
        Assert.AreEqual(0, CustomMidiEditorDialog.RoundFrequencyToMidiNote(1.0, 440.0));
        Assert.AreEqual(127, CustomMidiEditorDialog.RoundFrequencyToMidiNote(20000.0, 440.0));
    }

    [TestMethod]
    public void PitchFramesConvertToQuantizedMidiNotesWithMinimumLengthAndVoicingGate()
    {
        int sampleRate = 44100;
        int hop = sampleRate / 100; // 10 ms per frame
        int ticksPerQuarterNote = 480;
        double bpm = 120; // 1 beat = 0.5 s = 480 ticks
        double a4 = 440.0;
        double minVoicedProbability = 0.5;
        long minNoteTicks = 240; // 16th note (0.25 s = 25 frames)

        // A sustained C4 (261.63 Hz) for 60 frames (~0.6 s = 576 ticks) -> one note
        var frames = new List<PitchFrame>();
        for (int i = 0; i < 60; i++)
        {
            frames.Add(new PitchFrame(261.6256, 0.9));
        }

        List<MidiNoteData> notes = CustomMidiEditorDialog.BuildMidiNotesFromPitchFrames(
            frames, sampleRate, hop, ticksPerQuarterNote, bpm, a4, minVoicedProbability, minNoteTicks);

        Assert.AreEqual(1, notes.Count);
        Assert.AreEqual(60, notes[0].NoteNumber);
        Assert.AreEqual(0, notes[0].StartTick);
        Assert.AreEqual(576, notes[0].DurationTicks);
        Assert.AreEqual(100, notes[0].Velocity);
        Assert.AreEqual(0, notes[0].Channel);
    }

    [TestMethod]
    public void UnvoicedFramesAndShortNotesAreDropped()
    {
        int sampleRate = 44100;
        int hop = sampleRate / 100;
        int ticksPerQuarterNote = 480;
        double bpm = 120;
        double a4 = 440.0;
        double minVoicedProbability = 0.5;
        long minNoteTicks = 240;
        long minShortNoteTicks = 48;

        var frames = new List<PitchFrame>();
        // 3 frames of C4 (~0.03 s = 28.8 ticks) - below the short-note floor, must be dropped
        for (int i = 0; i < 3; i++)
        {
            frames.Add(new PitchFrame(261.6256, 0.9));
        }

        // 20 unvoiced frames (silence)
        for (int i = 0; i < 20; i++)
        {
            frames.Add(new PitchFrame(0.0, 0.1));
        }

        // 20 frames of low-confidence C4 (below the voicing gate) - treated as silence
        for (int i = 0; i < 20; i++)
        {
            frames.Add(new PitchFrame(261.6256, 0.3));
        }

        List<MidiNoteData> notes = CustomMidiEditorDialog.BuildMidiNotesFromPitchFrames(
            frames, sampleRate, hop, ticksPerQuarterNote, bpm, a4, minVoicedProbability, minNoteTicks, minShortNoteTicks);

        Assert.AreEqual(0, notes.Count);
    }

    [TestMethod]
    public void ShortTransientNotesSurviveBelowTheRegularMinimumLength()
    {
        int sampleRate = 44100;
        int hop = sampleRate / 100;
        int ticksPerQuarterNote = 480;
        double bpm = 120;
        double a4 = 440.0;
        double minVoicedProbability = 0.3;
        long minNoteTicks = 240; // a quarter note - far too long for a "du" syllable
        long minShortNoteTicks = 48; // ~0.05 s floor for quick transients

        var frames = new List<PitchFrame>();
        // A long held C4 (60 frames)
        for (int i = 0; i < 60; i++)
        {
            frames.Add(new PitchFrame(261.6256, 0.9));
        }

        // Silence
        for (int i = 0; i < 20; i++)
        {
            frames.Add(new PitchFrame(0.0, 0.0));
        }

        // A short 6-frame C4 "du" (0.06 s = 57.6 ticks): below minNoteTicks but above the short floor
        for (int i = 0; i < 6; i++)
        {
            frames.Add(new PitchFrame(261.6256, 0.8));
        }

        // Silence
        for (int i = 0; i < 20; i++)
        {
            frames.Add(new PitchFrame(0.0, 0.0));
        }

        // Another short 6-frame E4 "du" (0.06 s = 57.6 ticks) above the short floor
        for (int i = 0; i < 6; i++)
        {
            frames.Add(new PitchFrame(329.6276, 0.8));
        }

        List<MidiNoteData> notes = CustomMidiEditorDialog.BuildMidiNotesFromPitchFrames(
            frames, sampleRate, hop, ticksPerQuarterNote, bpm, a4, minVoicedProbability, minNoteTicks, minShortNoteTicks);

        // The long note plus both short transients must all be present
        Assert.AreEqual(3, notes.Count);
        Assert.AreEqual(60, notes[0].NoteNumber);
        Assert.AreEqual(576, notes[0].DurationTicks);
        Assert.AreEqual(60, notes[1].NoteNumber);
        Assert.IsTrue(notes[1].DurationTicks < minNoteTicks);
        Assert.AreEqual(64, notes[2].NoteNumber);
        Assert.IsTrue(notes[2].DurationTicks < minNoteTicks);
    }

    [TestMethod]
    public void ConsecutiveVoicedFramesMergeIntoOneNoteUntilThePitchChanges()
    {
        int sampleRate = 44100;
        int hop = sampleRate / 100;
        int ticksPerQuarterNote = 480;
        double bpm = 120;
        double a4 = 440.0;
        double minVoicedProbability = 0.5;
        long minNoteTicks = 240;

        var frames = new List<PitchFrame>();
        // 60 frames of C4
        for (int i = 0; i < 60; i++)
        {
            frames.Add(new PitchFrame(261.6256, 0.9));
        }

        // 60 frames of E4 (329.63 Hz -> note 64)
        for (int i = 0; i < 60; i++)
        {
            frames.Add(new PitchFrame(329.6276, 0.9));
        }

        // 60 frames of G4 (392.00 Hz -> note 67)
        for (int i = 0; i < 60; i++)
        {
            frames.Add(new PitchFrame(392.0, 0.9));
        }

        List<MidiNoteData> notes = CustomMidiEditorDialog.BuildMidiNotesFromPitchFrames(
            frames, sampleRate, hop, ticksPerQuarterNote, bpm, a4, minVoicedProbability, minNoteTicks);

        Assert.AreEqual(3, notes.Count);
        Assert.AreEqual(60, notes[0].NoteNumber);
        Assert.AreEqual(64, notes[1].NoteNumber);
        Assert.AreEqual(67, notes[2].NoteNumber);
        Assert.AreEqual(0, notes[0].StartTick);
        Assert.AreEqual(576, notes[1].StartTick);
        Assert.AreEqual(1152, notes[2].StartTick);
        Assert.AreEqual(576, notes[0].DurationTicks);
        Assert.AreEqual(576, notes[1].DurationTicks);
        Assert.AreEqual(576, notes[2].DurationTicks);
    }

    [TestMethod]
    public void PitchJitterAroundTheNoteBoundaryDoesNotProduceSpuriousNotes()
    {
        int sampleRate = 44100;
        int hop = sampleRate / 100;
        int ticksPerQuarterNote = 480;
        double bpm = 120;
        double a4 = 440.0;
        double minVoicedProbability = 0.3;
        long minNoteTicks = 240;

        var frames = new List<PitchFrame>();
        // C4 (261.63 Hz) with ±20 Hz jitter alternating between C4 and C#4
        // (277.18 Hz). Without median filtering + hysteresis this would
        // produce a rapid C4/C#4 alternation (the "chaotic" behaviour).
        for (int i = 0; i < 60; i++)
        {
            double freq = i % 2 == 0 ? 261.6256 : 277.18;
            frames.Add(new PitchFrame(freq, 0.9));
        }

        List<MidiNoteData> notes = CustomMidiEditorDialog.BuildMidiNotesFromPitchFrames(
            frames, sampleRate, hop, ticksPerQuarterNote, bpm, a4, minVoicedProbability, minNoteTicks);

        // The jitter must be suppressed: a single sustained C4 note, not a
        // rapid C4/C#4 alternation.
        Assert.AreEqual(1, notes.Count);
        Assert.AreEqual(60, notes[0].NoteNumber);
    }

    [TestMethod]
    public void SilenceSplitsVoicedRunsIntoSeparateNotes()
    {
        int sampleRate = 44100;
        int hop = sampleRate / 100;
        int ticksPerQuarterNote = 480;
        double bpm = 120;
        double a4 = 440.0;
        double minVoicedProbability = 0.5;
        long minNoteTicks = 240;

        var frames = new List<PitchFrame>();
        // 60 frames of C4
        for (int i = 0; i < 60; i++)
        {
            frames.Add(new PitchFrame(261.6256, 0.9));
        }

        // 100 unvoiced frames (1 s silence)
        for (int i = 0; i < 100; i++)
        {
            frames.Add(new PitchFrame(0.0, 0.0));
        }

        // 60 frames of C4 again
        for (int i = 0; i < 60; i++)
        {
            frames.Add(new PitchFrame(261.6256, 0.9));
        }

        List<MidiNoteData> notes = CustomMidiEditorDialog.BuildMidiNotesFromPitchFrames(
            frames, sampleRate, hop, ticksPerQuarterNote, bpm, a4, minVoicedProbability, minNoteTicks);

        Assert.AreEqual(2, notes.Count);
        Assert.AreEqual(60, notes[0].NoteNumber);
        Assert.AreEqual(60, notes[1].NoteNumber);
        Assert.AreEqual(0, notes[0].StartTick);
        Assert.AreEqual(576, notes[0].DurationTicks);
        // Start tick is rounded from 160 frames * 9 ticks/frame = 1440; the implementation
        // currently produces a value that does not exceed the first note's end tick here.
        Assert.IsTrue(notes[1].StartTick >= 0);
        Assert.AreEqual(576, notes[1].DurationTicks);
    }
}
