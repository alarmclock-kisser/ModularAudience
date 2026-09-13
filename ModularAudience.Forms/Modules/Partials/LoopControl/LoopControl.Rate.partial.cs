using ModularAudience.Audio;
using ModularAudience.Audio.Processing;
using ModularAudience.Forms.Controls;
using System.Globalization;

namespace ModularAudience.Forms.Modules
{
    public partial class LoopControl
    {
        // Cumulative rate drag offset per audio (guid -> offset in "log position units")
        private readonly Dictionary<Guid, float> _audioRateDragOffset = new();

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
                Guid audioId = item.Audio.Id;

                // Initialize drag offset on first interaction for this audio
                if (!_audioRateDragOffset.ContainsKey(audioId))
                {
                    _audioRateDragOffset[audioId] = (float)PlaybackRateMapping.MapFactor(position);
                }

                // Cumulative: add delta from previous offset to maintain relative dragging
                float prevOffset = _audioRateDragOffset[audioId];
                float delta = (float)PlaybackRateMapping.MapFactor(position) - prevOffset;
                float newOffset = prevOffset + delta;
                _audioRateDragOffset[audioId] = newOffset;

                float currentFactor = Math.Clamp(newOffset, 0.001f, 10f); // 1%..1000% range

                if (view != null)
                {
                    view.SetPlaybackRateSynced((int)Math.Round(500.0 * Math.Log2(currentFactor)), broadcast: false);
                }
                else
                {
                    item.Audio.ManualSampleRateFactor = currentFactor;
                    var updateTask = item.Audio.ApplyCombinedSampleRateAsync();
                    this.RefreshPlaylistRowText(rowIndex, item);
                    await updateTask;
                }
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
