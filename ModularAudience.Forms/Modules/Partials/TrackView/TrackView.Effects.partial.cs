using ModularAudience.Audio;
using ModularAudience.Audio.Effects;
using ModularAudience.Audio.Effects.AutoTune;
using ModularAudience.Forms.Effects;
using ModularAudience.Forms.Modules.Dialogs;
using System.ComponentModel;

namespace ModularAudience.Forms.Modules;

public partial class TrackView
{
    private AutoTuneDialog? autoTuneDialog;
    private readonly Dictionary<AudioEffectKind, Form> effectDialogs = [];
    private readonly CancellationTokenSource effectsLifetime = new();
    private bool effectActionBusy, effectWindowsClosed;
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<AutoTuneDialogContext, AutoTuneDialog> CreateAutoTuneDialog { get; set; } = context => new AutoTuneDialog(context);

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<EffectDialogContext, Form> CreateEffectDialog { get; set; } = EffectDialogHost.Create;

    private void InitializeEffectsMenu()
    {
        ToolStripMenuItem effects = EffectMenuFactory.CreateApplyMenu(this.OpenEffect);
        this.contextMenu_waveform.Items.Insert(Math.Min(5, this.contextMenu_waveform.Items.Count), effects);
        this.contextMenu_waveform.Opening += (_, _) => effects.Enabled = !this.closeCleanupStarted && !this.effectActionBusy &&
            this.OriginalAudio.Data.Length > 0 && this.OriginalAudio.SampleRate > 0 && this.OriginalAudio.Channels > 0;
        this.Disposed += (_, _) => this.CloseEffectWindows();
    }

    private async void OpenEffect(AudioEffectKind effect)
    {
        if (effect == AudioEffectKind.AutoTune) { this.OpenAutoTune(this, EventArgs.Empty); return; }
        if (this.closeCleanupStarted || this.IsDisposed || this.effectActionBusy) return;
        if (this.effectDialogs.TryGetValue(effect, out Form? existing) && !existing.IsDisposed) { existing.Activate(); return; }
        try
        {
            AudioEffectEditSession session = AudioEffectEditSession.Capture(this.OriginalAudio);
            AudioEffectDefinition definition = AudioEffectCatalog.Get(effect);
            if (!definition.OpensOptions)
            {
                this.effectActionBusy = true;
                try
                {
                    CancellationToken token = this.effectsLifetime.Token;
                    AudioEffectResult result = await AudioEffectProcessor.ProcessAsync(session.Input, session.SampleRate, session.Channels,
                        definition.CreateDefaults(), cancellationToken: token);
                    await this.ApplyEffectResultAsync(session, result, definition.Name, token);
                }
                finally { this.effectActionBusy = false; }
                return;
            }
            var context = new EffectDialogContext(session, this.OriginalAudio.Name, definition.CreateDefaults(),
                (result, token) => this.ApplyEffectResultAsync(session, result, definition.Name, token),
                () => this.StopPlaybackAsync(preserveManualRate: true), () => (this.OriginalAudio.Bpm, this.OriginalAudio.ScannedBpm));
            Form dialog = this.CreateEffectDialog(context);
            this.effectDialogs[effect] = dialog;
            dialog.FormClosed += (_, _) => { if (this.effectDialogs.GetValueOrDefault(effect) == dialog) this.effectDialogs.Remove(effect); };
            dialog.Show(this);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (EffectOptionsEditor.IsEffectError(error))
        {
            LogManager.Log("Opening/applying effect failed.", error);
            if (!this.closeCleanupStarted && !this.IsDisposed)
                this.toolTip_trackView.Show(error.Message, this.pictureBox_waveform, 0, this.pictureBox_waveform.Height, 6000);
        }
    }

    private async Task ApplyEffectResultAsync(AudioEffectEditSession session, AudioEffectResult result, string name, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (this.closeCleanupStarted || this.IsDisposed) throw new OperationCanceledException(token);
        long position = this.OriginalAudio.Position, offset = this.offsetFrames, click = this.lastClickFrame;
        await this.StopPlaybackAsync(preserveManualRate: true);
        token.ThrowIfCancellationRequested();
        if (this.closeCleanupStarted || this.IsDisposed) throw new OperationCanceledException(token);
        this.CancelPendingRender();
        // Stop resets the playback cursor. Restore it before snapshot/commit so
        // the ripple map and undo history both see the user's actual position.
        this.OriginalAudio.SetPosition(position);
        if (!session.Apply(this.OriginalAudio, result)) return;
        int renderedFrames = result.Samples.Length / result.Channels;
        this.offsetFrames = session.MapFrame(offset, renderedFrames); this.lastClickFrame = session.MapFrame(click, renderedFrames);
        this.SynchronizeEffectSelectionMarkers();
        this.RecalculateLoopFraction(); this.ApplyLoopFractionToAudio();
        this.UpdateOffsetScrollbar(); this.UpdateTimeDisplay(); this.RequestWaveformRender();
        WindowMain.LoopControlWindow?.RefreshAudioTiming(this.OriginalAudio);
        WindowMain.Instance?.UpdateTrackDependentUI();
        LogManager.Log($"{name} applied to {(session.IsSelection ? "selection" : "track")} in '{this.OriginalAudio.Name}' (Ctrl+Z / Ctrl+Y available).");
    }

    private void SynchronizeEffectSelectionMarkers()
    {
        int channels = Math.Max(1, this.OriginalAudio.Channels);
        this.selectStartFrame = this.OriginalAudio.SelectionStart < 0 ? -1 : this.OriginalAudio.SelectionStart / channels;
        this.selectEndFrame = this.OriginalAudio.SelectionEnd < 0 ? -1 : this.OriginalAudio.SelectionEnd / channels;
        this.pendingSelect = this.dragSelecting = false;
    }

    private void CloseEffectWindows()
    {
        if (this.effectWindowsClosed) return;
        this.effectWindowsClosed = true; this.effectsLifetime.Cancel();
        this.autoTuneDialog?.Close();
        foreach (Form dialog in this.effectDialogs.Values.ToArray()) dialog.Close();
        this.effectDialogs.Clear();
        this.effectsLifetime.Dispose();
    }

    private void OpenAutoTune(object? sender, EventArgs e)
    {
        if (this.closeCleanupStarted || this.IsDisposed) return;
        if (this.autoTuneDialog is { IsDisposed: false }) { this.autoTuneDialog.Activate(); return; }
        try
        {
            AudioEffectEditSession session = AudioEffectEditSession.Capture(this.OriginalAudio);
            var defaults = new AutoTuneSettings
            {
                TempoBpm = AutoTuneTempoResolver.Resolve(this.OriginalAudio.Bpm, this.OriginalAudio.ScannedBpm),
                MaximumFrequencyHz = Math.Min(1200, session.SampleRate * 0.4)
            };
            var context = new AutoTuneDialogContext(session, this.OriginalAudio.Name, defaults,
                (samples, token) => this.ApplyEffectResultAsync(session, new AudioEffectResult(samples, session.Channels), "Auto Tune", token),
                () => this.StopPlaybackAsync(preserveManualRate: true),
                ReadTrackTempo: () => (this.OriginalAudio.Bpm, this.OriginalAudio.ScannedBpm));
            var dialog = this.CreateAutoTuneDialog(context);
            this.autoTuneDialog = dialog;
            dialog.FormClosed += (_, _) => { if (ReferenceEquals(this.autoTuneDialog, dialog)) this.autoTuneDialog = null; };
            dialog.Show(this); // Parameter effects are intentionally MODELESS.
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException)
        {
            LogManager.Log("Opening Auto Tune failed.", error);
            this.toolTip_trackView.Show(error.Message, this.pictureBox_waveform, 0, this.pictureBox_waveform.Height, 6000);
        }
    }
}
