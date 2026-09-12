using ModularAudience.Audio;

namespace ModularAudience.Forms.Modules
{
    public partial class LoopControl
    {
        private sealed class LoopTargetState
        {
            public long StartSamples { get; set; } = -1;
            public long EndSamples { get; set; } = -1;
            public float Fraction { get; set; }
        }

        private readonly Dictionary<Guid, LoopTargetState> loopTargetStates = [];
        private AudioObj? FocusedPlaylistAudio =>
            (this.checkedListBox_playlistTracks.SelectedItem as PlaylistTargetItem)?.Audio;
        private static TrackView? SelectedTrackView => WindowMain.LastSelectedTrackView is { IsDisposed: false, Disposing: false } view
            ? view : null;
        private TrackView? CurrentTrackView => this.FocusedPlaylistAudio is AudioObj audio
            ? FindTrackView(audio)
            : this.checkedListBox_playlistTracks.ContainsFocus ? null : SelectedTrackView;
        private AudioObj? OriginalAudio => this.FocusedPlaylistAudio ?? this.CurrentTrackView?.OriginalAudio;
        private IReadOnlyList<AudioObj> PlaylistAudios => WindowMain.Instance?.GetActivePlaylistAudios() ?? [];

        private static TrackView? FindTrackView(AudioObj audio) => WindowMain.TrackViews.FirstOrDefault(view =>
            !view.IsDisposed && !view.Disposing && view.OriginalAudio.Id == audio.Id);

        private List<AudioObj> GetActiveTargetAudios() => this.PlaylistAudios
            .Concat(WindowMain.TrackViews.Where(view => !view.IsDisposed && !view.Disposing && view.OriginalAudio.PlayerPlaying)
                .Select(view => view.OriginalAudio))
            .DistinctBy(audio => audio.Id)
            .ToList();

        private IReadOnlyList<AudioObj> GetActionTargets(bool checkedGroup)
        {
            if (!checkedGroup)
            {
                return this.OriginalAudio is AudioObj audio ? [audio] : [];
            }

            HashSet<Guid> checkedIds = this.checkedListBox_playlistTracks.CheckedItems
                .OfType<PlaylistTargetItem>().Select(item => item.Audio.Id).ToHashSet();
            return this.GetActiveTargetAudios().Where(audio => checkedIds.Contains(audio.Id)).ToArray();
        }

        private static float GetAudioBpm(AudioObj audio) =>
            audio.Bpm > 0 ? audio.Bpm : audio.ScannedBpm > 0 ? audio.ScannedBpm : 120f;

        private static float GetUiLoopFraction(AudioObj audio)
        {
            if (!audio.LoopEnabled)
            {
                return 0f;
            }
            return audio.Metrics.TryGetValue("loop.ui.fraction", out double fraction)
                ? (float) fraction : audio.LoopFraction;
        }

        private LoopTargetState GetLoopTargetState(AudioObj audio)
        {
            if (!this.loopTargetStates.TryGetValue(audio.Id, out LoopTargetState? state) || !audio.LoopEnabled)
            {
                state = new LoopTargetState();
                this.loopTargetStates[audio.Id] = state;
            }
            return state;
        }

        private void ToggleTargetLoops(IReadOnlyList<AudioObj> targets, float fraction)
        {
            bool turnOff = targets.Count > 0 && targets.All(audio =>
                audio.LoopEnabled && Math.Abs(GetUiLoopFraction(audio) - fraction) < 0.0001f);
            foreach (AudioObj audio in targets)
            {
                this.SetLoopRange(audio, turnOff ? 0f : fraction, audio.LoopEnabled);
            }
            this.UpdateLoopButtonsState();
        }

        private void UpdateTargetLabel()
        {
            int count = this.checkedListBox_playlistTracks.Items.Count;
            int checkedCount = this.selectedPlaylistTrackIds.Count;
            AudioObj? audio = this.OriginalAudio;
            this.label_targetMode.Text = audio != null
                ? $"Target: {audio.OriginalName ?? audio.Name} | Ctrl: {checkedCount}/{count} checked"
                : count == 0 ? "Target: no active playlist tracks"
                : $"Target: no focused row | Ctrl: {checkedCount}/{count} checked";
        }

        private static void RefreshTargetWaveform(AudioObj audio)
        {
            FindTrackView(audio)?.RequestWaveformRender();
        }
    }
}
