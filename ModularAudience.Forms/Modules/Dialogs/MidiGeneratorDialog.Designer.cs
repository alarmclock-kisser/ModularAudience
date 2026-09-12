namespace ModularAudience.Forms.Modules.Dialogs
{
    partial class MidiGeneratorDialog
    {
        private System.ComponentModel.IContainer components = null;
        private Label label_preset;
        private ComboBox comboBox_preset;
        private Label label_tempo;
        private NumericUpDown numericUpDown_tempo;
        private Label label_intensity;
        private NumericUpDown numericUpDown_intensity;
        private Label label_timeSignature;
        private NumericUpDown numericUpDown_timeSignatureNumerator;
        private NumericUpDown numericUpDown_timeSignatureDenominator;
        private Label label_keySignature;
        private NumericUpDown numericUpDown_keySignature;
        private Label label_bars;
        private NumericUpDown numericUpDown_bars;
        private Label label_tracks;
        private NumericUpDown numericUpDown_tracks;
        private Label label_ppq;
        private NumericUpDown numericUpDown_ppq;
        private Label label_instrument;
        private ComboBox comboBox_instrument;
        private Label label_pitchFrequency;
        private NumericUpDown numericUpDown_pitchFrequency;
        private Label label_seed;
        private NumericUpDown numericUpDown_seed;
        private CheckBox checkBox_useSeed;
        private Label label_filePath;
        private TextBox textBox_filePath;
        private Label label_customSample;
        private TextBox textBox_customSample;
        private Button button_selectCustomSample;
        private Button button_generate;
        private Button button_cancel;
        private Label label_status;

        protected override void Dispose(bool disposing)
        {
            if (disposing && this.components != null)
            {
                this.components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.label_preset = new Label();
            this.comboBox_preset = new ComboBox();
            this.label_tempo = new Label();
            this.numericUpDown_tempo = new NumericUpDown();
            this.label_intensity = new Label();
            this.numericUpDown_intensity = new NumericUpDown();
            this.label_timeSignature = new Label();
            this.numericUpDown_timeSignatureNumerator = new NumericUpDown();
            this.numericUpDown_timeSignatureDenominator = new NumericUpDown();
            this.label_keySignature = new Label();
            this.numericUpDown_keySignature = new NumericUpDown();
            this.label_bars = new Label();
            this.numericUpDown_bars = new NumericUpDown();
            this.label_tracks = new Label();
            this.numericUpDown_tracks = new NumericUpDown();
            this.label_ppq = new Label();
            this.numericUpDown_ppq = new NumericUpDown();
            this.label_instrument = new Label();
            this.comboBox_instrument = new ComboBox();
            this.label_pitchFrequency = new Label();
            this.numericUpDown_pitchFrequency = new NumericUpDown();
            this.label_seed = new Label();
            this.numericUpDown_seed = new NumericUpDown();
            this.checkBox_useSeed = new CheckBox();
            this.label_filePath = new Label();
            this.textBox_filePath = new TextBox();
            this.label_customSample = new Label();
            this.textBox_customSample = new TextBox();
            this.button_selectCustomSample = new Button();
            this.button_generate = new Button();
            this.button_cancel = new Button();
            this.label_status = new Label();
            this.button_llm = new Button();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_tempo).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_intensity).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_timeSignatureNumerator).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_timeSignatureDenominator).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_keySignature).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_bars).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_tracks).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_ppq).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_pitchFrequency).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_seed).BeginInit();
            this.SuspendLayout();
            // 
            // label_preset
            // 
            this.label_preset.AutoSize = true;
            this.label_preset.Location = new Point(12, 15);
            this.label_preset.Name = "label_preset";
            this.label_preset.Size = new Size(39, 15);
            this.label_preset.TabIndex = 0;
            this.label_preset.Text = "Preset";
            // 
            // comboBox_preset
            // 
            this.comboBox_preset.DropDownStyle = ComboBoxStyle.DropDownList;
            this.comboBox_preset.Location = new Point(150, 12);
            this.comboBox_preset.Name = "comboBox_preset";
            this.comboBox_preset.Size = new Size(230, 23);
            this.comboBox_preset.TabIndex = 1;
            this.comboBox_preset.SelectedIndexChanged += this.comboBox_preset_SelectedIndexChanged;
            // 
            // label_tempo
            // 
            this.label_tempo.AutoSize = true;
            this.label_tempo.Location = new Point(12, 50);
            this.label_tempo.Name = "label_tempo";
            this.label_tempo.Size = new Size(80, 15);
            this.label_tempo.TabIndex = 2;
            this.label_tempo.Text = "Tempo (BPM)";
            // 
            // numericUpDown_tempo
            // 
            this.numericUpDown_tempo.DecimalPlaces = 1;
            this.numericUpDown_tempo.Location = new Point(150, 47);
            this.numericUpDown_tempo.Maximum = new decimal(new int[] { 400, 0, 0, 0 });
            this.numericUpDown_tempo.Minimum = new decimal(new int[] { 20, 0, 0, 0 });
            this.numericUpDown_tempo.Name = "numericUpDown_tempo";
            this.numericUpDown_tempo.Size = new Size(120, 23);
            this.numericUpDown_tempo.TabIndex = 3;
            this.numericUpDown_tempo.Value = new decimal(new int[] { 120, 0, 0, 0 });
            // 
            // label_intensity
            // 
            this.label_intensity.AutoSize = true;
            this.label_intensity.Location = new Point(12, 85);
            this.label_intensity.Name = "label_intensity";
            this.label_intensity.Size = new Size(73, 15);
            this.label_intensity.TabIndex = 4;
            this.label_intensity.Text = "Intensity (%)";
            // 
            // numericUpDown_intensity
            // 
            this.numericUpDown_intensity.DecimalPlaces = 1;
            this.numericUpDown_intensity.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
            this.numericUpDown_intensity.Location = new Point(150, 82);
            this.numericUpDown_intensity.Name = "numericUpDown_intensity";
            this.numericUpDown_intensity.Size = new Size(120, 23);
            this.numericUpDown_intensity.TabIndex = 5;
            this.numericUpDown_intensity.Value = new decimal(new int[] { 100, 0, 0, 0 });
            // 
            // label_timeSignature
            // 
            this.label_timeSignature.AutoSize = true;
            this.label_timeSignature.Location = new Point(12, 120);
            this.label_timeSignature.Name = "label_timeSignature";
            this.label_timeSignature.Size = new Size(86, 15);
            this.label_timeSignature.TabIndex = 6;
            this.label_timeSignature.Text = "Time signature";
            // 
            // numericUpDown_timeSignatureNumerator
            // 
            this.numericUpDown_timeSignatureNumerator.Location = new Point(150, 117);
            this.numericUpDown_timeSignatureNumerator.Maximum = new decimal(new int[] { 32, 0, 0, 0 });
            this.numericUpDown_timeSignatureNumerator.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numericUpDown_timeSignatureNumerator.Name = "numericUpDown_timeSignatureNumerator";
            this.numericUpDown_timeSignatureNumerator.Size = new Size(80, 23);
            this.numericUpDown_timeSignatureNumerator.TabIndex = 7;
            this.numericUpDown_timeSignatureNumerator.Value = new decimal(new int[] { 4, 0, 0, 0 });
            // 
            // numericUpDown_timeSignatureDenominator
            // 
            this.numericUpDown_timeSignatureDenominator.Location = new Point(240, 117);
            this.numericUpDown_timeSignatureDenominator.Maximum = new decimal(new int[] { 32, 0, 0, 0 });
            this.numericUpDown_timeSignatureDenominator.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numericUpDown_timeSignatureDenominator.Name = "numericUpDown_timeSignatureDenominator";
            this.numericUpDown_timeSignatureDenominator.Size = new Size(80, 23);
            this.numericUpDown_timeSignatureDenominator.TabIndex = 8;
            this.numericUpDown_timeSignatureDenominator.Value = new decimal(new int[] { 4, 0, 0, 0 });
            // 
            // label_keySignature
            // 
            this.label_keySignature.AutoSize = true;
            this.label_keySignature.Location = new Point(12, 155);
            this.label_keySignature.Name = "label_keySignature";
            this.label_keySignature.Size = new Size(111, 15);
            this.label_keySignature.TabIndex = 9;
            this.label_keySignature.Text = "Key signature offset";
            // 
            // numericUpDown_keySignature
            // 
            this.numericUpDown_keySignature.Location = new Point(150, 152);
            this.numericUpDown_keySignature.Maximum = new decimal(new int[] { 12, 0, 0, 0 });
            this.numericUpDown_keySignature.Minimum = new decimal(new int[] { 12, 0, 0, int.MinValue });
            this.numericUpDown_keySignature.Name = "numericUpDown_keySignature";
            this.numericUpDown_keySignature.Size = new Size(120, 23);
            this.numericUpDown_keySignature.TabIndex = 10;
            // 
            // label_bars
            // 
            this.label_bars.AutoSize = true;
            this.label_bars.Location = new Point(12, 190);
            this.label_bars.Name = "label_bars";
            this.label_bars.Size = new Size(90, 15);
            this.label_bars.TabIndex = 11;
            this.label_bars.Text = "Number of bars";
            // 
            // numericUpDown_bars
            // 
            this.numericUpDown_bars.Location = new Point(150, 187);
            this.numericUpDown_bars.Maximum = new decimal(new int[] { 1024, 0, 0, 0 });
            this.numericUpDown_bars.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numericUpDown_bars.Name = "numericUpDown_bars";
            this.numericUpDown_bars.Size = new Size(120, 23);
            this.numericUpDown_bars.TabIndex = 12;
            this.numericUpDown_bars.Value = new decimal(new int[] { 16, 0, 0, 0 });
            // 
            // label_tracks
            // 
            this.label_tracks.AutoSize = true;
            this.label_tracks.Location = new Point(12, 225);
            this.label_tracks.Name = "label_tracks";
            this.label_tracks.Size = new Size(99, 15);
            this.label_tracks.TabIndex = 13;
            this.label_tracks.Text = "Number of tracks";
            // 
            // numericUpDown_tracks
            // 
            this.numericUpDown_tracks.Location = new Point(150, 222);
            this.numericUpDown_tracks.Maximum = new decimal(new int[] { 64, 0, 0, 0 });
            this.numericUpDown_tracks.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numericUpDown_tracks.Name = "numericUpDown_tracks";
            this.numericUpDown_tracks.Size = new Size(120, 23);
            this.numericUpDown_tracks.TabIndex = 14;
            this.numericUpDown_tracks.Value = new decimal(new int[] { 1, 0, 0, 0 });
            // 
            // label_ppq
            // 
            this.label_ppq.AutoSize = true;
            this.label_ppq.Location = new Point(12, 260);
            this.label_ppq.Name = "label_ppq";
            this.label_ppq.Size = new Size(122, 15);
            this.label_ppq.TabIndex = 15;
            this.label_ppq.Text = "Ticks per quarter note";
            // 
            // numericUpDown_ppq
            // 
            this.numericUpDown_ppq.Location = new Point(150, 257);
            this.numericUpDown_ppq.Maximum = new decimal(new int[] { 3840, 0, 0, 0 });
            this.numericUpDown_ppq.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numericUpDown_ppq.Name = "numericUpDown_ppq";
            this.numericUpDown_ppq.Size = new Size(120, 23);
            this.numericUpDown_ppq.TabIndex = 16;
            this.numericUpDown_ppq.Value = new decimal(new int[] { 960, 0, 0, 0 });
            // 
            // label_instrument
            // 
            this.label_instrument.AutoSize = true;
            this.label_instrument.Location = new Point(12, 295);
            this.label_instrument.Name = "label_instrument";
            this.label_instrument.Size = new Size(65, 15);
            this.label_instrument.TabIndex = 17;
            this.label_instrument.Text = "Instrument";
            // 
            // comboBox_instrument
            // 
            this.comboBox_instrument.DropDownStyle = ComboBoxStyle.DropDownList;
            this.comboBox_instrument.Location = new Point(150, 292);
            this.comboBox_instrument.Name = "comboBox_instrument";
            this.comboBox_instrument.Size = new Size(230, 23);
            this.comboBox_instrument.TabIndex = 18;
            this.comboBox_instrument.SelectedIndexChanged += this.comboBox_instrument_SelectedIndexChanged;
            // 
            // label_pitchFrequency
            // 
            this.label_pitchFrequency.AutoSize = true;
            this.label_pitchFrequency.Location = new Point(12, 330);
            this.label_pitchFrequency.Name = "label_pitchFrequency";
            this.label_pitchFrequency.Size = new Size(111, 15);
            this.label_pitchFrequency.TabIndex = 19;
            this.label_pitchFrequency.Text = "Pitch reference (Hz)";
            // 
            // numericUpDown_pitchFrequency
            // 
            this.numericUpDown_pitchFrequency.DecimalPlaces = 1;
            this.numericUpDown_pitchFrequency.Location = new Point(150, 327);
            this.numericUpDown_pitchFrequency.Maximum = new decimal(new int[] { 1000, 0, 0, 0 });
            this.numericUpDown_pitchFrequency.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numericUpDown_pitchFrequency.Name = "numericUpDown_pitchFrequency";
            this.numericUpDown_pitchFrequency.Size = new Size(120, 23);
            this.numericUpDown_pitchFrequency.TabIndex = 20;
            this.numericUpDown_pitchFrequency.Value = new decimal(new int[] { 440, 0, 0, 0 });
            // 
            // label_seed
            // 
            this.label_seed.AutoSize = true;
            this.label_seed.Location = new Point(12, 365);
            this.label_seed.Name = "label_seed";
            this.label_seed.Size = new Size(79, 15);
            this.label_seed.TabIndex = 21;
            this.label_seed.Text = "Random seed";
            // 
            // numericUpDown_seed
            // 
            this.numericUpDown_seed.Location = new Point(150, 362);
            this.numericUpDown_seed.Maximum = new decimal(new int[] { int.MaxValue, 0, 0, 0 });
            this.numericUpDown_seed.Minimum = new decimal(new int[] { int.MinValue, 0, 0, int.MinValue });
            this.numericUpDown_seed.Name = "numericUpDown_seed";
            this.numericUpDown_seed.Size = new Size(120, 23);
            this.numericUpDown_seed.TabIndex = 22;
            // 
            // checkBox_useSeed
            // 
            this.checkBox_useSeed.AutoSize = true;
            this.checkBox_useSeed.Location = new Point(275, 364);
            this.checkBox_useSeed.Name = "checkBox_useSeed";
            this.checkBox_useSeed.Size = new Size(72, 19);
            this.checkBox_useSeed.TabIndex = 23;
            this.checkBox_useSeed.Text = "Use seed";
            // 
            // label_filePath
            // 
            this.label_filePath.AutoSize = true;
            this.label_filePath.Location = new Point(12, 400);
            this.label_filePath.Name = "label_filePath";
            this.label_filePath.Size = new Size(78, 15);
            this.label_filePath.TabIndex = 24;
            this.label_filePath.Text = "MIDI file path";
            // 
            // textBox_filePath
            // 
            this.textBox_filePath.Location = new Point(150, 397);
            this.textBox_filePath.Name = "textBox_filePath";
            this.textBox_filePath.Size = new Size(330, 23);
            this.textBox_filePath.TabIndex = 25;
            // 
            // label_customSample
            // 
            this.label_customSample.AutoSize = true;
            this.label_customSample.Location = new Point(12, 435);
            this.label_customSample.Name = "label_customSample";
            this.label_customSample.Size = new Size(90, 15);
            this.label_customSample.TabIndex = 26;
            this.label_customSample.Text = "Custom sample";
            // 
            // textBox_customSample
            // 
            this.textBox_customSample.Location = new Point(150, 432);
            this.textBox_customSample.Name = "textBox_customSample";
            this.textBox_customSample.ReadOnly = true;
            this.textBox_customSample.Size = new Size(250, 23);
            this.textBox_customSample.TabIndex = 27;
            // 
            // button_selectCustomSample
            // 
            this.button_selectCustomSample.Location = new Point(405, 432);
            this.button_selectCustomSample.Name = "button_selectCustomSample";
            this.button_selectCustomSample.Size = new Size(75, 23);
            this.button_selectCustomSample.TabIndex = 28;
            this.button_selectCustomSample.Text = "Select...";
            this.button_selectCustomSample.Click += this.button_selectCustomSample_Click;
            // 
            // button_generate
            // 
            this.button_generate.Location = new Point(300, 480);
            this.button_generate.Name = "button_generate";
            this.button_generate.Size = new Size(100, 30);
            this.button_generate.TabIndex = 29;
            this.button_generate.Text = "Generate";
            this.button_generate.Click += this.button_generate_Click;
            // 
            // button_cancel
            // 
            this.button_cancel.DialogResult = DialogResult.Cancel;
            this.button_cancel.Location = new Point(410, 480);
            this.button_cancel.Name = "button_cancel";
            this.button_cancel.Size = new Size(100, 30);
            this.button_cancel.TabIndex = 30;
            this.button_cancel.Text = "Cancel";
            // 
            // label_status
            // 
            this.label_status.AutoSize = true;
            this.label_status.Location = new Point(12, 488);
            this.label_status.Name = "label_status";
            this.label_status.Size = new Size(39, 15);
            this.label_status.TabIndex = 31;
            this.label_status.Text = "Ready";
            // 
            // button_llm
            // 
            this.button_llm.Location = new Point(150, 480);
            this.button_llm.Name = "button_llm";
            this.button_llm.Size = new Size(75, 23);
            this.button_llm.TabIndex = 32;
            this.button_llm.Text = "LLM";
            this.button_llm.UseVisualStyleBackColor = true;
            this.button_llm.Click += this.button_llm_Click;
            // 
            // MidiGeneratorDialog
            // 
            this.AcceptButton = this.button_generate;
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.CancelButton = this.button_cancel;
            this.ClientSize = new Size(525, 525);
            this.Controls.Add(this.button_llm);
            this.Controls.Add(this.label_preset);
            this.Controls.Add(this.comboBox_preset);
            this.Controls.Add(this.label_tempo);
            this.Controls.Add(this.numericUpDown_tempo);
            this.Controls.Add(this.label_intensity);
            this.Controls.Add(this.numericUpDown_intensity);
            this.Controls.Add(this.label_timeSignature);
            this.Controls.Add(this.numericUpDown_timeSignatureNumerator);
            this.Controls.Add(this.numericUpDown_timeSignatureDenominator);
            this.Controls.Add(this.label_keySignature);
            this.Controls.Add(this.numericUpDown_keySignature);
            this.Controls.Add(this.label_bars);
            this.Controls.Add(this.numericUpDown_bars);
            this.Controls.Add(this.label_tracks);
            this.Controls.Add(this.numericUpDown_tracks);
            this.Controls.Add(this.label_ppq);
            this.Controls.Add(this.numericUpDown_ppq);
            this.Controls.Add(this.label_instrument);
            this.Controls.Add(this.comboBox_instrument);
            this.Controls.Add(this.label_pitchFrequency);
            this.Controls.Add(this.numericUpDown_pitchFrequency);
            this.Controls.Add(this.label_seed);
            this.Controls.Add(this.numericUpDown_seed);
            this.Controls.Add(this.checkBox_useSeed);
            this.Controls.Add(this.label_filePath);
            this.Controls.Add(this.textBox_filePath);
            this.Controls.Add(this.label_customSample);
            this.Controls.Add(this.textBox_customSample);
            this.Controls.Add(this.button_selectCustomSample);
            this.Controls.Add(this.button_generate);
            this.Controls.Add(this.button_cancel);
            this.Controls.Add(this.label_status);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "MidiGeneratorDialog";
            this.StartPosition = FormStartPosition.CenterParent;
            this.Text = "MIDI Generator";
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_tempo).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_intensity).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_timeSignatureNumerator).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_timeSignatureDenominator).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_keySignature).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_bars).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_tracks).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_ppq).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_pitchFrequency).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_seed).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private Button button_llm;
    }
}