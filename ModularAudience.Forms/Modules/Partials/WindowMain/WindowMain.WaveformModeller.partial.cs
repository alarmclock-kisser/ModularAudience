using ModularAudience.Forms.Modules.Dialogs;

namespace ModularAudience.Forms;

public partial class WindowMain
{
    private WaveformModeller? waveformModeller;
    private void ModelWaveform_Click(object? sender, EventArgs e)
    {
        if (waveformModeller == null || waveformModeller.IsDisposed) waveformModeller = new WaveformModeller();
        waveformModeller.Show(); waveformModeller.Activate();
    }
}
