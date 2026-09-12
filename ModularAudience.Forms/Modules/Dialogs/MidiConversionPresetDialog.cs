using ModularAudience.Audio.Midi;

namespace ModularAudience.Forms.Modules.Dialogs
{
    public partial class MidiConversionPresetDialog : Form
    {
        public MidiConversionPreset SelectedPreset => this.comboBox_preset.SelectedIndex switch
        {
            1 => MidiConversionPreset.Guitar,
            2 => MidiConversionPreset.Polyphonic,
            _ => MidiConversionPreset.Synth
        };

        public MidiConversionPresetDialog()
        {
            this.InitializeComponent();
            this.comboBox_preset.SelectedIndex = 2;
        }
    }
}
