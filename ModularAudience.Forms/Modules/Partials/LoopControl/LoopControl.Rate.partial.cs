using ModularAudience.Audio;
using ModularAudience.Audio.Processing;
using ModularAudience.Forms.Controls;
using System.Globalization;

namespace ModularAudience.Forms.Modules
{
    public partial class LoopControl
    {
        private async void checkedListBox_playlistTracks_RatePositionChanged(object? sender, PlaylistTrackRateChangedEventArgs e)
        {
            await this.ApplyPlaylistRateAsync(e.RowIndex, e.Position);
        }

        private async void toolStripMenuItem_resetPlaylistRate_Click(object? sender, EventArgs e)
        {
            await this.ApplyPlaylistRateAsync(this.checkedListBox_playlistTracks.SelectedIndex, 0);
        }

        private async Task ApplyPlaylistRateAsync(int rowIndex, int position)
        {
            if (this.suppressPlaylistChecklistEvents || rowIndex < 0 || rowIndex >= this.checkedListBox_playlistTracks.Items.Count
                || this.checkedListBox_playlistTracks.Items[rowIndex] is not PlaylistTargetItem item)
            {
                return;
            }
            try
            {
                TrackView? view = FindTrackView(item.Audio);
                Task update = Task.CompletedTask;
                if (view != null)
                {
                    view.SetPlaybackRateSynced(position, broadcast: false);
                }
                else
                {
                    item.Audio.ManualSampleRateFactor = PlaybackRateMapping.MapFactor(position);
                    update = item.Audio.ApplyCombinedSampleRateAsync();
                }
                this.RefreshPlaylistRowText(rowIndex, item);
                await update;
            }
            catch (Exception ex)
            {
                LogCollection.Log(ex);
            }
        }

        private void RefreshPlaylistRowText(int rowIndex, PlaylistTargetItem item)
        {
            string display = BuildPlaylistDisplayText(item.Audio);
            if (string.Equals(item.DisplayText, display, StringComparison.Ordinal))
            {
                return;
            }
            item.DisplayText = display;
            this.checkedListBox_playlistTracks.Invalidate(this.checkedListBox_playlistTracks.GetItemRectangle(rowIndex));
        }

        private static string BuildPlaylistRateText(AudioObj audio)
        {
            double effectiveBpm = GetAudioBpm(audio) * audio.StretchFactor * audio.SampleRateFactor;
            string bpm = effectiveBpm.ToString("F1", CultureInfo.InvariantCulture);
            string rate = ((audio.ManualSampleRateFactor - 1.0) * 100.0)
                .ToString("+0.0;-0.0;+0.0", CultureInfo.InvariantCulture);
            return $"[{bpm} BPM] {{{rate}%}}";
        }
    }
}
