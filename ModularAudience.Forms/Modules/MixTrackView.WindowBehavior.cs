using System.ComponentModel;
using ModularAudience.Audio;
using ModularAudience.Forms.Modules.Dialogs;

namespace ModularAudience.Forms.Modules;

public sealed partial class MixTrackView
{
    private readonly Dictionary<Guid, (AudioCollectionView? Collection, Guid Audio)> joinedOrigins = [];

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<Form, Task<DialogResult>>? DialogPresenter { get; set; }
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<FileDialog, Task<DialogResult>>? FileDialogPresenter { get; set; }
    internal bool HasLoadedProject => this.projectPath is not null;
    internal MixWindowGroup WindowGroup => this.windowGroup;

    private Task<DialogResult> ShowMixDialogAsync(Form dialog) => this.DialogPresenter is not null
        ? this.DialogPresenter(dialog) : this.windowGroup.ShowAsync(dialog);
    private async Task<DialogResult> ShowMixFileDialogAsync(FileDialog dialog, string title)
    {
        try { return await (this.FileDialogPresenter is not null ? this.FileDialogPresenter(dialog) : this.windowGroup.ShowNativeAsync(title, owner => dialog.ShowDialog(owner))); }
        catch (OperationCanceledException) { return DialogResult.Cancel; }
    }
    private async Task<Color?> PickMixColorAsync(Color initial)
    {
        try
        {
            Form? parent = this.windowGroup.Windows.LastOrDefault();
            return await this.windowGroup.ShowNativeAsync<Color?>("Choose mix color", owner =>
            {
                using var dialog = new ColorDialog { FullOpen = true, Color = initial };
                return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.Color : null;
            }, parent);
        }
        catch (OperationCanceledException) { return null; }
    }

    private void RefreshAfterHistory()
    {
        this.tempoDebounce.Stop(); this.ClearAudioEffectSelection(); this.SetTempoControl(Arrangement.Bpm);
        this.committed = ModularAudience.Audio.Arrangement.MixAudioHistory.Capture(Arrangement);
        this.committedAudioKey = ModularAudience.Audio.Arrangement.MixAudioHistory.Key(Arrangement);
        this.Changed(); // Baseline must be updated without generating a second history entry.
    }
    private void UpdateTransportLoop()
    {
        if (playback is not null)
        {
            if (Arrangement.Loop) this.ApplyPlaybackLoop(playback);
            else if (playback.LoopEnabled) playback.UpdateLoopFraction(0, playback.Data.LongLength, playback.Data.LongLength, false, false);
        }
        canvas.Invalidate();
    }
    private void UpdateTimelineScrollBounds()
    {
        double earliest = Arrangement.Lanes.SelectMany(lane => lane.Clips).Select(clip => clip.Start).DefaultIfEmpty(0).Min();
        double margin = 480 / Arrangement.Bpm;
        scroll.Minimum = (int)Math.Clamp(Math.Floor(Math.Min(0, earliest) * 100 - margin * 100), int.MinValue + 200, -1);
        scroll.Maximum = Math.Max(1000, (int)Math.Min(int.MaxValue - 200, Math.Ceiling(Arrangement.Duration * 100)) + 100);
    }
    private void AutoPanClipDrag(int x)
    {
        if (this.dragMode is not (1 or 2 or 3)) return;
        int direction = x < Header + 8 ? -1 : x > canvas.Width - 12 ? 1 : 0;
        if (direction == 0) return;
        int amount = Math.Max(1, (int)Math.Round(600 / pixelsPerSecond));
        if (direction < 0 && scroll.Value - amount <= scroll.Minimum) scroll.Minimum = Math.Max(int.MinValue + 200, scroll.Minimum - amount * 8);
        if (direction > 0 && scroll.Value + amount >= scroll.Maximum - scroll.LargeChange) scroll.Maximum = Math.Min(int.MaxValue - 200, scroll.Maximum + amount * 8);
        scroll.Value = Math.Clamp(scroll.Value + direction * amount, scroll.Minimum, scroll.Maximum - scroll.LargeChange + 1);
    }

    private void RememberJoinedOrigin(Guid lane, TrackView view)
    {
        AudioCollection? source = view.SourceCollection;
        AudioCollectionView? collection = WindowMain.CollectionViews.FirstOrDefault(candidate => !candidate.IsDisposed && !candidate.Disposing && ReferenceEquals(candidate.AudioC, source));
        this.joinedOrigins[lane] = (collection, view.SourceAudioId);
    }
    private void RestoreJoinedWindowsOnClose()
    {
        foreach (var pair in this.joined.ToArray())
        {
            TrackView view = pair.Value;
            if (view.IsDisposed || view.Disposing || !ReferenceEquals(view.JoinedMix, this)) continue;
            view.SetJoinedMix(null);
            bool alive = this.joinedOrigins.TryGetValue(pair.Key, out var origin) && origin.Collection is { IsDisposed: false, Disposing: false } collection &&
                WindowMain.CollectionViews.Contains(collection) && collection.AudioC.Audios.Any(audio => audio.Id == origin.Audio);
            if (alive) view.Show();
            else { view.Hide(); view.Close(); }
        }
    }
}
