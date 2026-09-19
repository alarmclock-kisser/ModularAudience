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
        private readonly Dictionary<Guid, double> _jumpBaseMsByAudioId = new();

        private static double GetJumpRateFactor(AudioObj audio) =>
            Math.Clamp(audio.SampleRateFactor, 0.01, 10.0);

        private double GetJumpBaseMs(AudioObj audio)
        {
            if (!this._jumpBaseMsByAudioId.TryGetValue(audio.Id, out double baseMs))
            {
                baseMs = 60000.0
                    / (GetAudioBpm(audio) * Math.Max(0.01, audio.StretchFactor))
                    / 4.0;
                this._jumpBaseMsByAudioId[audio.Id] = baseMs;
            }
            return baseMs;
        }

        private void SetJumpBaseMs(AudioObj? audio, double baseMs)
        {
            if (audio != null)
            {
                this._jumpBaseMsByAudioId[audio.Id] = Math.Max(0.01, baseMs);
            }
        }

        internal void UpdateJumpDistanceFromTrackView(AudioObj audio)
        {
            if (this.IsDisposed || this.Disposing || this.OriginalAudio?.Id != audio.Id)
            {
                return;
            }

            this.UpdateJumpDistanceForRateImmediately(audio,
                Math.Clamp(audio.ManualSampleRateFactor * audio.SyncNudgeSampleRateFactor, 0.01, 10.0));
        }

        private async void checkedListBox_playlistTracks_RatePositionChanged(object? sender, PlaylistTrackRateChangedEventArgs e)
        {
            await this.ApplyPlaylistRateAsync(e.RowIndex, e.Position,
                reset: e.Position == 0, resetAll: e.ResetAll, shiftAll: e.ShiftAll);
        }

        private async void toolStripMenuItem_resetPlaylistRate_Click(object? sender, EventArgs e)
        {
            bool resetAll = this.resetPlaylistRateForAllTracks;
            this.resetPlaylistRateForAllTracks = false;
            await this.ApplyPlaylistRateAsync(this.checkedListBox_playlistTracks.SelectedIndex,
                0, reset: true, resetAll: resetAll);
        }

        private async Task ApplyPlaylistRateAsync(int rowIndex, int position, bool reset = false, bool resetAll = false,
            bool shiftAll = false)
        {
            if (this.suppressPlaylistChecklistEvents || rowIndex < 0 || rowIndex >= this.checkedListBox_playlistTracks.Items.Count
                || this.checkedListBox_playlistTracks.Items[rowIndex] is not PlaylistTargetItem item)
            {
                return;
            }
            try
            {
                if (shiftAll && !reset && !resetAll)
                {
                    await this.ApplyPlaylistRateToAllAudiosAsync(position);
                    return;
                }

                IReadOnlyList<AudioObj> targets = resetAll
                    ? this.checkedListBox_playlistTracks.Items
                        .OfType<PlaylistTargetItem>()
                        .Select(item => item.Audio)
                        .DistinctBy(audio => audio.Id)
                        .ToArray()
                    : reset
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

        private async Task ApplyPlaylistRateToAllAudiosAsync(int position)
        {
            AudioObj[] targets = this.checkedListBox_playlistTracks.Items
                .OfType<PlaylistTargetItem>()
                .Select(item => item.Audio)
                .DistinctBy(audio => audio.Id)
                .ToArray();
            if (targets.Length == 0)
            {
                return;
            }

            double minimumLogPosition = 500.0 * Math.Log2(0.01);
            double maximumLogPosition = 500.0 * Math.Log2(10.0);
            double anchorOffset = position > 0
                ? targets.Max(audio => 500.0 * Math.Log2(Math.Clamp(audio.ManualSampleRateFactor, 0.01f, 10f)))
                : targets.Min(audio => 500.0 * Math.Log2(Math.Clamp(audio.ManualSampleRateFactor, 0.01f, 10f)));
            double newOffset = Math.Clamp(anchorOffset + position, minimumLogPosition, maximumLogPosition);

            foreach (AudioObj audio in targets)
            {
                await this.ApplyPlaylistRateAtOffsetAsync(audio, newOffset);
            }
        }

        private async Task ApplyPlaylistRateToAudioAsync(AudioObj audio, int position, bool reset)
        {
            Guid audioId = audio.Id;

            if (!audio.Playing && !audio.Paused && Math.Abs(audio.ManualSampleRateFactor - 1.0) < 0.000001)
            {
                this._audioRateDragOffset.Remove(audioId);
            }

            double minimumLogPosition = 500.0 * Math.Log2(0.01);
            double maximumLogPosition = 500.0 * Math.Log2(10.0);
            double currentAudioOffset = 500.0 * Math.Log2(Math.Clamp(audio.ManualSampleRateFactor, 0.01f, 10f));
            if (!_audioRateDragOffset.TryGetValue(audioId, out double dragOffset)
                || Math.Abs(dragOffset - currentAudioOffset) > 0.000001)
            {
                _audioRateDragOffset[audioId] = currentAudioOffset;
            }

            double candidateOffset = reset ? 0.0 : _audioRateDragOffset[audioId] + position;
            double newOffset = Math.Clamp(candidateOffset, minimumLogPosition, maximumLogPosition);
            _audioRateDragOffset[audioId] = newOffset;

            await this.ApplyPlaylistRateAtOffsetAsync(audio, newOffset);
        }

        private async Task ApplyPlaylistRateAtOffsetAsync(AudioObj audio, double newOffset)
        {
            _audioRateDragOffset[audio.Id] = newOffset;

            float currentFactor = (float)Math.Clamp(Math.Pow(2.0, newOffset / 500.0), 0.01, 10.0);

            audio.ManualSampleRateFactor = currentFactor;
            int rowIndex = this.FindPlaylistTrackIndex(audio.Id);
            if (rowIndex >= 0 && this.checkedListBox_playlistTracks.Items[rowIndex] is PlaylistTargetItem item)
            {
                this.RefreshPlaylistRowText(rowIndex, item);
            }
            await audio.ApplyCombinedSampleRateAsync();
            foreach (TrackView trackView in WindowMain.TrackViews
                .Where(view => !view.IsDisposed && !view.Disposing && view.MatchesRateAudio(audio)))
            {
                trackView.SyncRateAnchorFromLoopControl();
            }
            if (rowIndex >= 0 && this.checkedListBox_playlistTracks.Items[rowIndex] is PlaylistTargetItem refreshedItem)
            {
                this.RefreshPlaylistRowText(rowIndex, refreshedItem);
            }
            // Update the jump distance to reflect the new rate
            this.UpdateJumpDistanceForRate(audio);
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
            // SampleRateFactor is already the combined live varispeed rate.
            double effectiveBpm = GetAudioBpm(audio) * audio.StretchFactor * audio.SampleRateFactor;
            string bpm = effectiveBpm.ToString("F1", CultureInfo.InvariantCulture);
            string rate = ((audio.ManualSampleRateFactor - 1.0) * 100.0)
                .ToString("+0.0;-0.0;+0.0", CultureInfo.InvariantCulture);
            return $"[{bpm} BPM] {{{rate}%}}";
        }
    }
}
