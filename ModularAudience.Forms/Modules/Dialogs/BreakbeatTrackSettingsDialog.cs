using ModularAudience.Generators;

namespace ModularAudience.Forms.Modules.Dialogs
{
    public partial class BreakbeatTrackSettingsDialog : Form
    {
        public BreakbeatTrackSettingsDialog(string sampleName, BreakbeatTrackSettings settings)
        {
            this.InitializeComponent();
            this.Text = $"Track Settings: {sampleName}";
            this.comboBox_defaultPlaybackMode.DataSource = Enum.GetValues<BreakbeatPlaybackMode>();
            this.numericUpDown_volume.Value = (decimal)Math.Clamp(settings.DefaultVolumePercent, 0f, 250f);
            this.numericUpDown_pitch.Value = (decimal)Math.Clamp(settings.DefaultPitchSemitones, -24f, 24f);
            this.comboBox_defaultPlaybackMode.SelectedItem = settings.DefaultPlaybackMode;
        }

        public BreakbeatTrackSettings Settings => new(
            (float)this.numericUpDown_volume.Value,
            (float)this.numericUpDown_pitch.Value,
            this.comboBox_defaultPlaybackMode.SelectedItem is BreakbeatPlaybackMode mode
                ? mode
                : BreakbeatPlaybackMode.TimeStretch);

        private void button_reset_Click(object? sender, EventArgs e)
        {
            this.numericUpDown_volume.Value = 100m;
            this.numericUpDown_pitch.Value = 0m;
            this.comboBox_defaultPlaybackMode.SelectedItem = BreakbeatPlaybackMode.TimeStretch;
        }
    }
}