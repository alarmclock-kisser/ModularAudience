using ModularAudience.Audio;
using System;
using System.Windows.Forms;

namespace ModularAudience.Forms
{
    public partial class AudioCollectionView
    {
        private void menuToolStripItem_createVisualizerVideo_Click(object sender, EventArgs e)
        {
            AudioObj? selectedAudio = this.GetSingleContextAudio();
            if (selectedAudio == null)
            {
                return;
            }

            using var dialog = new VisualizerDialog(selectedAudio);
            this.ShowDialog(dialog);
        }
    }
}
