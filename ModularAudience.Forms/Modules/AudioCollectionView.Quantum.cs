using ModularAudience.Forms.Modules.Dialogs;

namespace ModularAudience.Forms;

public partial class AudioCollectionView
{
    private QuantumBeatEngineDialog? quantumBeatEngineDialog;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    [System.ComponentModel.Browsable(false)]
    internal Func<IReadOnlyList<ModularAudience.Audio.AudioObj>, QuantumBeatEngineDialog>? QuantumDialogFactory { get; set; }

    private void quantumBeatEngineToolStripMenuItem_Click(object? sender, EventArgs e)
    {
        if (this.quantumBeatEngineDialog is { IsDisposed: false })
        { this.quantumBeatEngineDialog.Show(); this.quantumBeatEngineDialog.BringToFront(); return; }
        var selected = this.GetContextAudios();
        if (selected.Count < 2) return;
        if (selected.Count > 32 || selected.Sum(audio => (long)audio.Data.Length) > 67_108_864)
        {
            QuantumHelpDialog.ShowMessage(this, "Quantum sample selection", "Select 2 to 32 samples containing no more than 256 MiB of combined source PCM. Use shorter slices for large loops.");
            return;
        }
        try
        {
            this.quantumBeatEngineDialog = this.QuantumDialogFactory?.Invoke(selected) ?? new QuantumBeatEngineDialog(selected);
            this.quantumBeatEngineDialog.Show(this); // Modeless: other collections, editors and playback remain interactive.
        }
        catch (Exception error) { QuantumHelpDialog.ShowMessage(this, "Quantum Beat Engine", QuantumBeatEngineDialog.EnglishError(error)); }
    }
}
