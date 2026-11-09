using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Midi;
using ModularAudience.Forms.Modules.Dialogs;

namespace ModularAudience.Audio.Tests;

[TestClass]
[DoNotParallelize]
public sealed class MidiWindowPageTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [STATestMethod]
    public void MergeButtonTracksMembershipAndRemovesTheLastEmptyMergedPage()
    {
        MidiFileData source = MidiPageOperationsTests.CreateMidi();
        using var window = new MidiWindow(null, source);
        window.Show();
        var page = Field<NumericUpDown>(window, "numericUpDown_track");
        var merge = Field<Button>(window, "button_merge");

        Assert.AreEqual(3m, page.Maximum);
        Assert.AreEqual("Merge", merge.Text);
        merge.PerformClick();
        Assert.AreEqual(1m, page.Value, "Adding a page must not prevent adding the next original page.");
        Assert.AreEqual(4m, page.Maximum);
        Assert.AreEqual("Un-merge", merge.Text);

        page.Value = 2;
        Assert.AreEqual("Merge", merge.Text);
        merge.PerformClick();
        Assert.AreEqual("Un-merge", merge.Text);
        Assert.AreEqual(4m, page.Maximum, "There is only one derived page, not one per merge action.");
        page.Value = 4;
        Assert.IsFalse(merge.Enabled, "Merged must never merge itself.");
        MidiFileData mergedPage = window.CreateOutputMidi(currentPageOnly: true);
        Assert.AreEqual("Merged", mergedPage.Tracks[0].Name);
        Assert.AreEqual(12, mergedPage.Tracks[0].Index);
        CollectionAssert.AreEqual(MidiPageOperationsTests.AllNoteSignatures(source.SelectTracks([2, 7])),
            MidiPageOperationsTests.AllNoteSignatures(mergedPage));

        Assert.AreEqual(3, window.CreateOutputMidi(currentPageOnly: false).Tracks.Count,
            "Normal export must retain originals without adding duplicate merged notes.");
        MidiFileData ensemble = window.CreateOutputMidi(currentPageOnly: false, merge: true);
        Assert.AreEqual(1, ensemble.Tracks.Count);
        CollectionAssert.AreEqual(MidiPageOperationsTests.AllNoteSignatures(source), MidiPageOperationsTests.AllNoteSignatures(ensemble));

        page.Value = 2;
        merge.PerformClick();
        Assert.AreEqual("Merge", merge.Text);
        page.Value = 4;
        Assert.AreEqual(1, window.CreateOutputMidi(currentPageOnly: true).Tracks[0].Notes.Count);
        Assert.AreEqual(60, window.CreateOutputMidi(currentPageOnly: true).Tracks[0].Notes[0].NoteNumber);
        page.Value = 1;
        Assert.AreEqual("Un-merge", merge.Text);
        merge.PerformClick();
        Assert.AreEqual("Merge", merge.Text);
        Assert.AreEqual(3m, page.Maximum);
        Assert.AreEqual(3, source.Tracks.Sum(track => track.Notes.Count));

        // Recreate the page after its removal to catch stale membership or stale notes.
        page.Value = 3;
        merge.PerformClick();
        page.Value = 4;
        Assert.AreEqual(67, window.CreateOutputMidi(currentPageOnly: true).Tracks[0].Notes.Single().NoteNumber);
    }

    [STATestMethod]
    public void CurrentPageOutputUsesPageNumberNotSparseTrackIndexAndPreservesSettings()
    {
        MidiFileData source = MidiPageOperationsTests.CreateMidi();
        using var window = new MidiWindow(null, source);
        Field<NumericUpDown>(window, "numericUpDown_track").Value = 2;
        MidiFileData selected = window.CreateOutputMidi(currentPageOnly: true, merge: true);

        Assert.AreEqual(1, selected.Tracks.Count);
        Assert.AreEqual(7, selected.Tracks[0].Index);
        Assert.AreEqual("Bass", selected.Tracks[0].Name);
        Assert.AreEqual(432, selected.PitchFrequency);
        Assert.AreEqual(source.Tracks[1].LengthTicks, selected.LengthTicks);
        CollectionAssert.AreEqual(MidiPageOperationsTests.AllNoteSignatures(source.SelectTracks([7])),
            MidiPageOperationsTests.AllNoteSignatures(selected));
        selected.Tracks[0].Notes.Clear();
        Assert.AreEqual(1, source.Tracks[1].Notes.Count);
    }

    [STATestMethod]
    public void EditingAnOriginalPageRebuildsMergedAndKeepsTheSelectedPage()
    {
        MidiFileData source = MidiPageOperationsTests.CreateMidi();
        using var window = new MidiWindow(null, source);
        window.Show();
        var page = Field<NumericUpDown>(window, "numericUpDown_track");
        var merge = Field<Button>(window, "button_merge");
        page.Value = 2;
        merge.PerformClick();
        page.Value = 4;

        MidiEditSelection selection = MidiFileData.CreateEditSelection(source, 7, 0, 2880, 0, 127);
        selection.MidiFile.Tracks[0].Notes.Clear();
        selection.MidiFile.Tracks[0].Notes.Add(new() { NoteNumber = 55, Channel = 3, Velocity = 88, StartTick = 480, DurationTicks = 240 });
        window.ApplyEdit(source.ReplaceSelection(selection, selection.MidiFile));

        Assert.AreEqual(4m, page.Value);
        Assert.AreEqual("Merged", window.CreateOutputMidi(currentPageOnly: true).Tracks[0].Name);
        Assert.AreEqual(55, window.CreateOutputMidi(currentPageOnly: true).Tracks[0].Notes.Single().NoteNumber);
        page.Value = 2;
        Assert.AreEqual("Un-merge", merge.Text);
        Assert.AreEqual(55, window.CreateOutputMidi(currentPageOnly: true).Tracks[0].Notes.Single().NoteNumber);
    }

    [STATestMethod]
    public void ReplacingPagesPrunesMissingMembershipAndClampsThePageSelector()
    {
        MidiFileData source = MidiPageOperationsTests.CreateMidi();
        using var window = new MidiWindow(null, source);
        window.Show();
        var page = Field<NumericUpDown>(window, "numericUpDown_track");
        Field<Button>(window, "button_merge").PerformClick();
        page.Value = 4;
        window.ApplyEdit(source.SelectTracks([7, 11]));

        Assert.AreEqual(2m, page.Maximum);
        Assert.AreEqual(2m, page.Value);
        Assert.AreEqual("Harmony", window.CreateOutputMidi(currentPageOnly: true).Tracks[0].Name);
        Assert.AreEqual("Merge", Field<Button>(window, "button_merge").Text);
    }

    [STATestMethod]
    public void SaveAndExportOwnTheRequestedContextMenusUntilWindowDisposal()
    {
        using var window = new MidiWindow(null, MidiPageOperationsTests.CreateMidi());
        ContextMenuStrip saveMenu = Field<Button>(window, "button_save").ContextMenuStrip!;
        ContextMenuStrip exportMenu = Field<Button>(window, "button_export").ContextMenuStrip!;
        Assert.IsNotNull(saveMenu);
        Assert.IsNotNull(exportMenu);
        Assert.AreEqual("Render this MIDI page only", saveMenu.Items[0].Text);
        Assert.AreEqual(1, saveMenu.Items.Count);
        Assert.AreEqual("Export this MIDI page only", exportMenu.Items[0].Text);
        Assert.AreEqual("Export as single MIDI page (merge)", exportMenu.Items[1].Text);
        Assert.AreEqual(2, exportMenu.Items.Count);
        window.Dispose();
        Assert.IsTrue(saveMenu.IsDisposed);
        Assert.IsTrue(exportMenu.IsDisposed);
    }

    private static T Field<T>(MidiWindow window, string name) =>
        (T)typeof(MidiWindow).GetField(name, PrivateInstance)!.GetValue(window)!;
}
