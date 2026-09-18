using ModularAudience.Audio;
using ModularAudience.Audio.Processing;
using ModularAudience.Forms.Controls;
using System.Globalization;

namespace ModularAudience.Forms.Modules
{
    public partial class LoopControl
    {
        // Absolute rate position per audio in logarithmic scrollbar units.
        private readonly Dictionary<Guid, double> _audioRateDragOffset = new();

        private async void checkedListBox_playlistTracks_RatePositionChanged(object? sender, PlaylistTrackRateChangedEventArgs e)
        {
            await this.ApplyPlaylistRateAsync(e.RowIndex, e.Position, reset: e.Position == 0);
        }

        private async void toolStripMenuItem_resetPlaylistRate_Click(object? sender, EventArgs e)
        {
            await this.ApplyPlaylistRateAsync(this.checkedListBox_playlistTracks.SelectedIndex, 0, reset: true);
        }

        private async Task ApplyPlaylistRateAsync(int rowIndex, int position, bool reset = false)
        {
            if (this.suppressPlaylistChecklistEvents || rowIndex < 0 || rowIndex >= this.checkedListBox_playlistTracks.Items.Count
                || this.checkedListBox_playlistTracks.Items[rowIndex] is not PlaylistTargetItem item)
            {
                return;
            }
            try
            {
                IReadOnlyList<AudioObj> targets = reset
                    ? [item.Audio]
                    : ModifierKeys.HasFlag(Keys.Control)
                        ? this.GetActionTargets(checkedGroup: true)
                        : [item.Audio];
                foreach (AudioObj audio in targets.DistinctBy(audio => audio.Id))
                {
                    await this.ApplyPlaylistRateToAudioAsync(audio, position, reset);
                }
            }
            catch (Exception ex)
            {
                LogCollection.Log(ex);
            }
        }

        private async Task ApplyPlaylistRateToAudioAsync(AudioObj audio, int position, bool reset)
        {
            Guid audioId = audio.Id;

            double minimumLogPosition = 500.0 * Math.Log2(0.01);
            double maximumLogPosition = 500.0 * Math.Log2(10.0);
            if (!_audioRateDragOffset.ContainsKey(audioId))
            {
                _audioRateDragOffset[audioId] = 500.0 * Math.Log2(Math.Clamp(audio.ManualSampleRateFactor, 0.01f, 10f));
            }

            double candidateOffset = reset ? 0.0 : _audioRateDragOffset[audioId] + position;
            double newOffset = Math.Clamp(candidateOffset, minimumLogPosition, maximumLogPosition);
            _audioRateDragOffset[audioId] = newOffset;

            float currentFactor = (float)Math.Clamp(Math.Pow(2.0, newOffset / 500.0), 0.01, 10.0);

            audio.ManualSampleRateFactor = currentFactor;
            int rowIndex = this.FindPlaylistTrackIndex(audio.Id);
            if (rowIndex >= 0 && this.checkedListBox_playlistTracks.Items[rowIndex] is PlaylistTargetItem item)
            {
                this.RefreshPlaylistRowText(rowIndex, item);
            }
            await audio.ApplyCombinedSampleRateAsync();
            if (rowIndex >= 0 && this.checkedListBox_playlistTracks.Items[rowIndex] is PlaylistTargetItem refreshedItem)
            {
                this.RefreshPlaylistRowText(rowIndex, refreshedItem);
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
