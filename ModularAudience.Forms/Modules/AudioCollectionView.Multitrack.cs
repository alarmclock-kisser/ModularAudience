using ModularAudience.Audio;
using ModularAudience.Forms.Modules;

namespace ModularAudience.Forms;

public partial class AudioCollectionView
{
    private bool openingMultitrack;

    private async void menuToolStripItem_openMultitrack_Click(object? sender, EventArgs e)
    {
        List<AudioObj> selected = this.GetContextAudios();
        if (this.openingMultitrack || selected.Count == 0) { return; }
        this.openingMultitrack = true;
        this.menuToolStripItem_openMultitrack.Enabled = false;
        MixTrackView? mix = null;
        try
        {
            mix = new MixTrackView();
            await mix.JoinCollectionSelectionAsync(selected);
        }
        catch (Exception error)
        {
            // A completed first join is useful even if a later track cannot be imported.
            if (mix is { IsDisposed: false } && mix.Arrangement.Lanes.Count == 0) { mix.Dispose(); }
            LogManager.Log(error);
            if (!this.IsDisposed && !this.Disposing)
            {
                MessageBox.Show(this, "Could not open the selected tracks as a multitrack view: " + error.Message,
                    "Open as Multitrack View", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        finally
        {
            this.openingMultitrack = false;
            if (!this.IsDisposed && !this.Disposing) { this.UpdateContextMenuState(); }
        }
    }
}
