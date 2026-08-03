using ModularAudience.Audio.Midi;
using ModularAudience.Generators;

namespace ModularAudience.Forms.Modules.Dialogs
{
    public partial class MidiRemixDialog : Form
    {
        private readonly MidiFileData sourceMidiFile;

        public MidiRemixDialog(MidiFileData sourceMidiFile, int defaultTrackIndex = 0)
        {
            this.sourceMidiFile = sourceMidiFile ?? throw new ArgumentNullException(nameof(sourceMidiFile));
            this.InitializeComponent();
            foreach (MidiTrackData track in this.sourceMidiFile.Tracks)
            {
                this.comboBox_track.Items.Add($"{track.Index}: {track.Name}");
            }

            int selected = this.sourceMidiFile.Tracks.ToList().FindIndex(track => track.Index == defaultTrackIndex);
            this.comboBox_track.SelectedIndex = selected >= 0 ? selected : 0;
            MidiRemixSettings defaults = new();
            this.numericUpDown_denoise.Value = (decimal) defaults.DenoiseFactor;
            this.numericUpDown_frequency.Value = (decimal) defaults.FrequencyShift;
            this.numericUpDown_tempo.Value = (decimal) defaults.TempoShift;
            this.numericUpDown_derivation.Value = (decimal) defaults.PatternDerivationFactor;
            this.numericUpDown_rearrangement.Value = (decimal) defaults.PatternRearrangementFactor;
            this.numericUpDown_minLength.Value = defaults.PatternMinLength;
            this.numericUpDown_maxLength.Value = defaults.PatternMaxLength;
            this.numericUpDown_poolSize.Value = defaults.DerivedPatternsPoolSize;
        }

        public MidiRemixSettings Settings { get; private set; } = new();
        public int TrackIndex { get; private set; }

        private void button_ok_Click(object? sender, EventArgs e)
        {
            if (this.numericUpDown_minLength.Value > this.numericUpDown_maxLength.Value)
            {
                MessageBox.Show(this, "Pattern minimum length must not exceed the maximum length.", "Invalid remix settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                this.DialogResult = DialogResult.None;
                return;
            }

            MidiTrackData selectedTrack = this.sourceMidiFile.Tracks[this.comboBox_track.SelectedIndex];
            this.TrackIndex = selectedTrack.Index;
            this.Settings = new MidiRemixSettings
            {
                DenoiseFactor = (float) this.numericUpDown_denoise.Value,
                FrequencyShift = (float) this.numericUpDown_frequency.Value,
                TempoShift = (float) this.numericUpDown_tempo.Value,
                PatternDerivationFactor = (float) this.numericUpDown_derivation.Value,
                PatternRearrangementFactor = (float) this.numericUpDown_rearrangement.Value,
                PatternMinLength = (int) this.numericUpDown_minLength.Value,
                PatternMaxLength = (int) this.numericUpDown_maxLength.Value,
                DerivedPatternsPoolSize = (int) this.numericUpDown_poolSize.Value
            };
        }
    }
}