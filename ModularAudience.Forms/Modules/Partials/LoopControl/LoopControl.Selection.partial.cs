namespace ModularAudience.Forms.Modules
{
    public partial class LoopControl
    {
        private void checkedListBox_playlistTracks_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (this.suppressPlaylistChecklistEvents)
            {
                return;
            }
            this.SelectPlaylistTrackView();
            this.UpdateLoopButtonsState();
        }

        private TrackView? SelectPlaylistTrackView()
        {
            if (this.suppressPlaylistChecklistEvents ||
                this.checkedListBox_playlistTracks.SelectedItem is not PlaylistTargetItem item)
            {
                return null;
            }

            TrackView? trackView = WindowMain.TrackViews.FirstOrDefault(view =>
                !view.IsDisposed && !view.Disposing && view.Visible && view.OriginalAudio.Id == item.Audio.Id);
            if (trackView != null)
            {
                WindowMain.LastSelectedTrackView = trackView;
            }

            return trackView;
        }

        private void RestorePlaylistSelection(Guid? audioId, int previousIndex)
        {
            var listBox = this.checkedListBox_playlistTracks;
            int index = this.FindPlaylistTrackIndex(audioId);
            if (index < 0 && previousIndex >= 0 && listBox.Items.Count > 0)
            {
                index = Math.Min(previousIndex, listBox.Items.Count - 1);
            }
            if (index < 0)
            {
                index = this.FindPlaylistTrackIndex(SelectedTrackView?.OriginalAudio.Id);
            }
            if (index < 0 && listBox.Items.Count > 0 && (SelectedTrackView == null || listBox.ContainsFocus))
            {
                index = 0;
            }
            if (listBox.SelectedIndex != index)
            {
                listBox.SelectedIndex = index;
            }
        }

        internal void SynchronizeTrackSelection()
        {
            if (this.IsDisposed || this.Disposing)
            {
                return;
            }
            if (this.InvokeRequired)
            {
                this.BeginInvoke((Action)this.SynchronizeTrackSelection);
                return;
            }
            if (this.suppressPlaylistChecklistEvents || this.checkedListBox_playlistTracks.IsInteracting ||
                this.checkedListBox_playlistTracks.ContainsFocus || this.contextMenuStrip_playlistItem.Visible)
            {
                return;
            }

            bool wasSuppressed = this.suppressPlaylistChecklistEvents;
            this.suppressPlaylistChecklistEvents = true;
            try
            {
                int index = this.FindPlaylistTrackIndex(SelectedTrackView?.OriginalAudio.Id);
                if (index >= 0)
                {
                    this.checkedListBox_playlistTracks.SelectedIndex = index;
                }

                // Also sync the loop buttons to reflect the currently selected track
                this.UpdateLoopButtonsState();
            }
            finally
            {
                this.suppressPlaylistChecklistEvents = wasSuppressed;
            }
        }

        private int FindPlaylistTrackIndex(Guid? audioId)
        {
            for (int i = 0; i < this.checkedListBox_playlistTracks.Items.Count; i++)
            {
                if (this.checkedListBox_playlistTracks.Items[i] is PlaylistTargetItem item && item.Audio.Id == audioId)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
