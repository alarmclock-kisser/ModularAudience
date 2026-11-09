using ModularAudience.Audio.Midi;

namespace ModularAudience.Forms.Modules.Dialogs;

public partial class MidiWindow
{
    // Keep originals separate from the derived page so ensemble output never doubles notes.
    private readonly HashSet<int> mergedTrackIndices = [];
    private MidiTrackData? mergedTrack;

    private void button_merge_Click(object? sender, EventArgs e)
    {
        if (this.tracks.Count == 0 || ReferenceEquals(this.SelectedTrack, this.mergedTrack)) return;
        int trackIndex = this.SelectedTrack.Index;
        if (!this.mergedTrackIndices.Add(trackIndex)) this.mergedTrackIndices.Remove(trackIndex);
        this.previewCts?.Cancel();
        this.RefreshMidiPages((int)this.numericUpDown_track.Value);
    }

    private void RefreshMidiPages(int selectedPage)
    {
        this.tracks = this.midiFile.Tracks.ToList();
        this.mergedTrackIndices.IntersectWith(this.tracks.Select(track => track.Index));
        this.mergedTrack = null;
        if (this.mergedTrackIndices.Count > 0)
        {
            int mergedIndex = this.tracks.Max(track => track.Index) + 1;
            this.mergedTrack = this.midiFile.SelectTracks(this.mergedTrackIndices)
                .MergeTracks(trackIndex: mergedIndex).Tracks[0];
            this.tracks.Add(this.mergedTrack);
        }

        this.numericUpDown_track.Maximum = Math.Max(1, this.tracks.Count);
        this.numericUpDown_track.Value = Math.Clamp(selectedPage, 1, (int)this.numericUpDown_track.Maximum);
        this.numericUpDown_track.Enabled = this.tracks.Count > 1;
        this.UpdateMergeButton();
        this.label_status.Text = $"Tracks: {this.midiFile.Tracks.Count}, Notes: {this.midiFile.Tracks.Sum(track => track.Notes.Count)}"
            + (this.mergedTrack == null ? string.Empty : $", Merged: {this.mergedTrackIndices.Count} pages");
        this.pictureBox_midi.Invalidate();
    }

    private void UpdateMergeButton()
    {
        bool originalPage = this.tracks.Count > 0 && !ReferenceEquals(this.SelectedTrack, this.mergedTrack);
        bool included = originalPage && this.mergedTrackIndices.Contains(this.SelectedTrack.Index);
        this.button_merge.Enabled = originalPage;
        this.button_merge.Text = included ? "Un-merge" : "Merge";
        this.toolTip_midi.SetToolTip(this.button_merge, originalPage
            ? included ? "Remove this MIDI page from Merged." : "Add this MIDI page to Merged. Original pages are kept."
            : "Merged is generated from the original pages. Select an original page to merge or un-merge it.");
    }

    internal MidiFileData CreateOutputMidi(bool currentPageOnly, bool merge = false)
    {
        if (!currentPageOnly) return merge ? this.midiFile.MergeTracks() : this.midiFile;

        // The selected page may be the derived Merged page, which is not in midiFile.
        MidiFileData page = MidiFileData.CreateGenerated([this.SelectedTrack], this.midiFile.TicksPerQuarterNote,
            this.midiFile.DefaultBpm, this.midiFile.FilePath, this.midiFile.PitchFrequency);
        return page.SelectTracks([this.SelectedTrack.Index]);
    }
}
