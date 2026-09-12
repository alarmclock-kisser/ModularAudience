namespace ModularAudience.Forms.Modules
{
    public partial class LoopControl
    {
        private void checkedListBox_playlistTracks_SelectedIndexChanged(object? sender, EventArgs e)
        {
            this.SelectPlaylistTrackView();
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

        private void FocusSelectedPlaylistTrackView()
        {
            TrackView? trackView = this.SelectPlaylistTrackView();
            if (trackView == null)
            {
                return;
            }

            if (trackView.WindowState == FormWindowState.Minimized)
            {
                trackView.WindowState = FormWindowState.Normal;
            }
            trackView.Activate();
        }

        internal void SynchronizeTrackSelection()
        {
            if (this.IsDisposed || this.Disposing)
            {
                return;
            }
            if (this.InvokeRequired)
            {
                this.BeginInvoke((Action) this.SynchronizeTrackSelection);
                return;
            }

            bool wasSuppressed = this.suppressPlaylistChecklistEvents;
            this.suppressPlaylistChecklistEvents = true;
            try
            {
                this.checkedListBox_playlistTracks.SelectedIndex =
                    this.FindPlaylistTrackIndex(this.CurrentTrackView?.OriginalAudio.Id);
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
