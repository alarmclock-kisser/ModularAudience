using ModularAudience.Audio;
using ModularAudience.Forms.Modules.Dialogs;

namespace ModularAudience.Forms;

public partial class AudioCollectionView
{
    private void menuToolStripItem_decompile_Click(object? sender, EventArgs e)
    {
        AudioObj? source = this.GetSingleContextAudio();
        if (source == null)
        {
            return;
        }

        this.contextMenuStrip_audios.Close();
        try
        {
            var dialog = new DecompileConfigDialog(source);
            dialog.ShowIndependent();
        }
        catch (Exception error)
        {
            LogManager.Log($"Audio decompile configuration failed for '{source.Name}': {error}");
            MessageBox.Show(
                this,
                "The Audio Decompile window could not be opened. Error details were logged.",
                "Audio Decompile",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
