using ModularAudience.Forms.Modules.Dialogs;

namespace ModularAudience.Forms;

public partial class AudioCollectionView
{
    private readonly ToolStripMenuItem sampleModellerItem = new("Waveform Modeller") { Name = "menuToolStripItem_waveformModeller" };
    private void InitializeSampleModeller()
    {
        int index = this.contextMenuStrip_audios.Items.IndexOf(this.menuToolStripItem_timeStretchSelected);
        this.contextMenuStrip_audios.Items.Insert(index + 1, sampleModellerItem);
        sampleModellerItem.Click += (_, _) =>
        {
            var audio = this.GetSingleContextAudio();
            if (audio == null) return;
            new SampleWaveformModeller(this, audio).Show(this);
        };
    }
}
