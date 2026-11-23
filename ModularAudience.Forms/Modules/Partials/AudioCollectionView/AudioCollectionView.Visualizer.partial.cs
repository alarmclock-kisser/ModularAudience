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

            // AudioCollectionView is already visible - show dialog non-modally
            var dialog = new VisualizerDialog(selectedAudio);
            dialog.Show(this);
        }
    }
}
