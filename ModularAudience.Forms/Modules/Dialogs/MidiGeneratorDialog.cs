using ModularAudience.Audio;
using ModularAudience.Audio.Midi;
using ModularAudience.Generators;
using System.Reflection;

namespace ModularAudience.Forms.Modules.Dialogs
{
    public partial class MidiGeneratorDialog : Form
    {
        private readonly MidiFileData sourceMidiFile;
        private readonly List<PresetOption> presetOptions = [];
        private CancellationTokenSource? generationCts;
        private AudioObj? customSample;

        public MidiGeneratorDialog(MidiFileData sourceMidiFile)
        {
            this.sourceMidiFile = sourceMidiFile ?? throw new ArgumentNullException(nameof(sourceMidiFile));
            this.InitializeComponent();
            this.LoadPresetOptions();
            this.comboBox_instrument.Items.AddRange(Enum.GetNames<MidiInstrument>());
            this.comboBox_instrument.SelectedIndex = 0;
            this.numericUpDown_tempo.Value = (decimal) Math.Clamp(this.sourceMidiFile.DefaultBpm, 20.0, 400.0);
            this.numericUpDown_ppq.Value = Math.Clamp(this.sourceMidiFile.TicksPerQuarterNote, 1, 3840);
            this.numericUpDown_pitchFrequency.Value = (decimal) Math.Clamp(this.sourceMidiFile.PitchFrequency, 1.0, 1000.0);
            this.textBox_filePath.Text = this.sourceMidiFile.FilePath;
            this.UpdateCustomSampleState();
        }

        public MidiFileData? GeneratedMidiFileData { get; private set; }

        private void LoadPresetOptions()
        {
            this.comboBox_preset.Items.Clear();
            this.presetOptions.Clear();
            foreach (MidiGenerationPreset preset in Enum.GetValues<MidiGenerationPreset>())
            {
                string methodName = $"Generate{preset}PresetAsync";
                bool methodExists = typeof(MidiGenerator)
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Any(method => method.Name == methodName
                        && method.ReturnType == typeof(Task<MidiFileData>)
                        && method.GetParameters().Length >= 1
                        && method.GetParameters()[0].ParameterType == typeof(MidiGenerationSettings));
                if (!methodExists)
                {
                    continue;
                }

                PresetOption option = new(preset, methodName);
                this.presetOptions.Add(option);
                this.comboBox_preset.Items.Add(option);
            }

            if (this.comboBox_preset.Items.Count > 0)
            {
                this.comboBox_preset.SelectedIndex = 0;
            }
        }

        private MidiGenerationSettings CreateSettings()
        {
            if (this.comboBox_preset.SelectedItem is not PresetOption selectedPreset)
            {
                throw new InvalidOperationException("Please select a MIDI preset.");
            }

            MidiInstrument instrument = (MidiInstrument) Math.Clamp(this.comboBox_instrument.SelectedIndex, 0, Enum.GetValues<MidiInstrument>().Length - 1);
            if (instrument == MidiInstrument.CustomSample && this.customSample == null)
            {
                throw new InvalidOperationException("Please select a loaded audio object as custom sample.");
            }

            return new MidiGenerationSettings
            {
                Preset = selectedPreset.Preset,
                Tempo = (double) this.numericUpDown_tempo.Value,
                Intensity = (double) this.numericUpDown_intensity.Value,
                TimeSignatureNumerator = (int) this.numericUpDown_timeSignatureNumerator.Value,
                TimeSignatureDenominator = (int) this.numericUpDown_timeSignatureDenominator.Value,
                KeySignature = (int) this.numericUpDown_keySignature.Value,
                NumberOfBars = (int) this.numericUpDown_bars.Value,
                NumberOfTracks = (int) this.numericUpDown_tracks.Value,
                TicksPerQuarterNote = (int) this.numericUpDown_ppq.Value,
                Instrument = (int) instrument,
                MidiInstrument = instrument,
                CustomSample = this.customSample,
                PitchFrequency = (double) this.numericUpDown_pitchFrequency.Value,
                Seed = this.checkBox_useSeed.Checked ? (int) this.numericUpDown_seed.Value : null,
                FilePath = this.textBox_filePath.Text.Trim()
            };
        }

        private async void button_generate_Click(object? sender, EventArgs e)
        {
            if (this.generationCts != null)
            {
                return;
            }

            try
            {
                MidiGenerationSettings settings = this.CreateSettings();
                using CancellationTokenSource cts = new();
                this.generationCts = cts;
                this.button_generate.Enabled = false;
                this.button_cancel.Enabled = false;
                this.label_status.Text = "Generating MIDI...";
                ProgressDialog? progressDialog = null;
                Progress<double> progress = new(value => progressDialog?.Report(value));
                progressDialog = new(
                    title: "Generating MIDI...",
                    progress: progress,
                    ct: cts.Token,
                    cancellationSource: cts);
                try
                {
                    progressDialog.Show(this);
                    progressDialog.BringToFront();
                    this.GeneratedMidiFileData = await MidiGenerator.GenerateMidiFileDataAsync(settings, progress, cts.Token);
                    if (this.GeneratedMidiFileData == null)
                    {
                        throw new InvalidOperationException("MIDI generation did not produce a result.");
                    }

                    progressDialog.Complete();
                    this.DialogResult = DialogResult.OK;
                    this.Close();
                }
                finally
                {
                    if (!progressDialog.IsDisposed)
                    {
                        progressDialog.Close();
                    }
                }
            }
            catch (OperationCanceledException) when (this.generationCts?.IsCancellationRequested == true)
            {
                this.label_status.Text = "Generation cancelled.";
            }
            catch (Exception ex)
            {
                LogCollection.Log($"MIDI generation failed: {ex}");
                this.label_status.Text = "Generation failed.";
                MessageBox.Show(this, ex.Message, "MIDI generation failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                this.button_generate.Enabled = true;
                this.button_cancel.Enabled = true;
                this.generationCts = null;
            }
        }

        private void button_selectCustomSample_Click(object? sender, EventArgs e)
        {
            ContextMenuStrip menu = new();
            foreach (AudioObj audio in WindowMain.CollectionViews
                .Where(view => !view.IsDisposed)
                .SelectMany(view => view.AudioC.Audios)
                .DistinctBy(audio => audio.Id))
            {
                AudioObj selected = audio;
                menu.Items.Add(new ToolStripMenuItem(selected.Name, null, (_, _) =>
                {
                    this.customSample = selected;
                    this.textBox_customSample.Text = selected.Name;
                    this.label_status.Text = $"Custom sample: {selected.Name}";
                }));
            }
            if (menu.Items.Count == 0)
            {
                menu.Items.Add(new ToolStripMenuItem("No audio objects are currently open") { Enabled = false });
            }
            menu.Show(this.button_selectCustomSample, 0, this.button_selectCustomSample.Height);
        }

        private void comboBox_preset_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (this.comboBox_preset.SelectedItem is PresetOption option)
            {
                this.label_status.Text = $"Selected: {option.Preset}";
            }
        }

        private void comboBox_instrument_SelectedIndexChanged(object? sender, EventArgs e)
        {
            this.UpdateCustomSampleState();
        }

        private void UpdateCustomSampleState()
        {
            bool customSampleSelected = this.comboBox_instrument.SelectedIndex == (int) MidiInstrument.CustomSample;
            this.label_customSample.Enabled = customSampleSelected;
            this.textBox_customSample.Enabled = customSampleSelected;
            this.button_selectCustomSample.Enabled = customSampleSelected;
            if (!customSampleSelected)
            {
                this.customSample = null;
                this.textBox_customSample.Clear();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (this.generationCts != null && !this.generationCts.IsCancellationRequested)
            {
                this.generationCts.Cancel();
            }
            base.OnFormClosing(e);
        }

        private sealed record PresetOption(MidiGenerationPreset Preset, string MethodName)
        {
            public override string ToString() => this.Preset.ToString();
        }
    }
}