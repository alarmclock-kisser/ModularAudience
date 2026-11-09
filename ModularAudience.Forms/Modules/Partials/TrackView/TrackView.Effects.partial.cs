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
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<AutoTuneDialogContext, AutoTuneDialog> CreateAutoTuneDialog { get; set; } = context => new AutoTuneDialog(context);

    private void InitializeEffectsMenu()
    {
        var effects = new ToolStripMenuItem("Apply Effects ...") { Name = "menuItem_applyEffects" };
        ToolStripMenuItem autoTune = EffectMenuFactory.Create("Auto Tune", opensParameters: true, this.OpenAutoTune);
        autoTune.Name = "menuItem_autoTune";
        effects.DropDownItems.Add(autoTune);
        this.contextMenu_waveform.Items.Insert(Math.Min(5, this.contextMenu_waveform.Items.Count), effects);
        this.contextMenu_waveform.Opening += (_, _) => effects.Enabled = !this.closeCleanupStarted &&
            this.OriginalAudio.Data.Length > 0 && this.OriginalAudio.SampleRate > 0 && this.OriginalAudio.Channels > 0;
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
                async (samples, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    if (this.closeCleanupStarted || this.IsDisposed) throw new OperationCanceledException(token);
                    await this.StopPlaybackAsync(preserveManualRate: true);
                    token.ThrowIfCancellationRequested();
                    if (this.closeCleanupStarted || this.IsDisposed) throw new OperationCanceledException(token);
                    this.CancelPendingRender();
                    bool changed = session.Apply(this.OriginalAudio, samples);
                    if (changed)
                    {
                        this.RecalculateLoopFraction(); this.ApplyLoopFractionToAudio();
                        this.UpdateOffsetScrollbar(); this.RequestWaveformRender();
                        WindowMain.Instance?.UpdateTrackDependentUI();
                        LogManager.Log($"Auto Tune applied to {(session.IsSelection ? "selection" : "track")} in '{this.OriginalAudio.Name}' (Ctrl+Z / Ctrl+Y available).");
                    }
                },
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
