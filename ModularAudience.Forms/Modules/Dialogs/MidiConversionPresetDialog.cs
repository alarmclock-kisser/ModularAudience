using ModularAudience.Audio.Midi;

namespace ModularAudience.Forms.Modules.Dialogs
{
    public partial class MidiConversionPresetDialog : Form
    {
        public MidiConversionPreset SelectedPreset => this.comboBox_preset.SelectedIndex == 1
            ? MidiConversionPreset.Guitar
            : MidiConversionPreset.Synth;

        public MidiConversionPresetDialog()
        {
            this.InitializeComponent();
            this.comboBox_preset.SelectedIndex = 0;
        }
    }
}
