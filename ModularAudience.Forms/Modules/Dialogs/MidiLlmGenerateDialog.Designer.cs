namespace ModularAudience.Forms.Modules.Dialogs
{
    partial class MidiLlmGenerateDialog
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.label_apiUrl = new Label();
            this.textBox_apiUrl = new TextBox();
            this.button_connect = new Button();
            this.label_model = new Label();
            this.label_prompt = new Label();
            this.textBox_prompt = new TextBox();
            this.label_bpm = new Label();
            this.numericUpDown_bpm = new NumericUpDown();
            this.label_bars = new Label();
            this.numericUpDown_bars = new NumericUpDown();
            this.label_ppq = new Label();
            this.numericUpDown_ppq = new NumericUpDown();
            this.button_generate = new Button();
            this.button_openMidi = new Button();
            this.button_exportMidi = new Button();
            this.label_status = new Label();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_bpm).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_bars).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_ppq).BeginInit();
            this.SuspendLayout();
            // 
            // label_apiUrl
            // 
            this.label_apiUrl.AutoSize = true;
            this.label_apiUrl.Location = new Point(12, 15);
            this.label_apiUrl.Name = "label_apiUrl";
            this.label_apiUrl.Size = new Size(96, 15);
            this.label_apiUrl.TabIndex = 0;
            this.label_apiUrl.Text = "LLM API URL";
            // 
            // textBox_apiUrl
            // 
            this.textBox_apiUrl.Location = new Point(150, 12);
            this.textBox_apiUrl.Name = "textBox_apiUrl";
            this.textBox_apiUrl.PlaceholderText = "OpenAI compatible API Url...";
            this.textBox_apiUrl.Size = new Size(200, 23);
            this.textBox_apiUrl.TabIndex = 1;
            this.textBox_apiUrl.Text = "http://127.0.0.1:8080";
            // 
            // button_connect
            // 
            this.button_connect.Location = new Point(356, 12);
            this.button_connect.Name = "button_connect";
            this.button_connect.Size = new Size(124, 23);
            this.button_connect.TabIndex = 2;
            this.button_connect.Text = "Connect / Test";
            this.button_connect.UseVisualStyleBackColor = true;
            this.button_connect.Click += this.button_connect_Click;
            // 
            // label_model
            // 
            this.label_model.AutoSize = true;
            this.label_model.Location = new Point(150, 40);
            this.label_model.Name = "label_model";
            this.label_model.Size = new Size(110, 15);
            this.label_model.TabIndex = 3;
            this.label_model.Text = "Model: not connected";
            // 
            // label_prompt
            // 
            this.label_prompt.AutoSize = true;
            this.label_prompt.Location = new Point(12, 65);
            this.label_prompt.Name = "label_prompt";
            this.label_prompt.Size = new Size(132, 15);
            this.label_prompt.TabIndex = 4;
            this.label_prompt.Text = "MIDI Prompt";
            // 
            // textBox_prompt
            // 
            this.textBox_prompt.Location = new Point(150, 62);
            this.textBox_prompt.Multiline = true;
            this.textBox_prompt.Name = "textBox_prompt";
            this.textBox_prompt.PlaceholderText = "Describe the MIDI pattern to generate...";
            this.textBox_prompt.Size = new Size(330, 110);
            this.textBox_prompt.TabIndex = 5;
            // 
            // label_bpm
            // 
            this.label_bpm.AutoSize = true;
            this.label_bpm.Location = new Point(12, 185);
            this.label_bpm.Name = "label_bpm";
            this.label_bpm.Size = new Size(80, 15);
            this.label_bpm.TabIndex = 6;
            this.label_bpm.Text = "Tempo (BPM)";
            // 
            // numericUpDown_bpm
            // 
            this.numericUpDown_bpm.DecimalPlaces = 1;
            this.numericUpDown_bpm.Location = new Point(150, 182);
            this.numericUpDown_bpm.Maximum = new decimal(new int[] { 400, 0, 0, 0 });
            this.numericUpDown_bpm.Minimum = new decimal(new int[] { 20, 0, 0, 0 });
            this.numericUpDown_bpm.Name = "numericUpDown_bpm";
            this.numericUpDown_bpm.Size = new Size(120, 23);
            this.numericUpDown_bpm.TabIndex = 7;
            this.numericUpDown_bpm.Value = new decimal(new int[] { 120, 0, 0, 0 });
            // 
            // label_bars
            // 
            this.label_bars.AutoSize = true;
            this.label_bars.Location = new Point(12, 220);
            this.label_bars.Name = "label_bars";
            this.label_bars.Size = new Size(90, 15);
            this.label_bars.TabIndex = 8;
            this.label_bars.Text = "Number of bars";
            // 
            // numericUpDown_bars
            // 
            this.numericUpDown_bars.Location = new Point(150, 217);
            this.numericUpDown_bars.Maximum = new decimal(new int[] { 512, 0, 0, 0 });
            this.numericUpDown_bars.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numericUpDown_bars.Name = "numericUpDown_bars";
            this.numericUpDown_bars.Size = new Size(120, 23);
            this.numericUpDown_bars.TabIndex = 9;
            this.numericUpDown_bars.Value = new decimal(new int[] { 4, 0, 0, 0 });
            // 
            // label_ppq
            // 
            this.label_ppq.AutoSize = true;
            this.label_ppq.Location = new Point(12, 255);
            this.label_ppq.Name = "label_ppq";
            this.label_ppq.Size = new Size(122, 15);
            this.label_ppq.TabIndex = 10;
            this.label_ppq.Text = "Ticks per quarter note";
            // 
            // numericUpDown_ppq
            // 
            this.numericUpDown_ppq.Location = new Point(150, 252);
            this.numericUpDown_ppq.Maximum = new decimal(new int[] { 3840, 0, 0, 0 });
            this.numericUpDown_ppq.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numericUpDown_ppq.Name = "numericUpDown_ppq";
            this.numericUpDown_ppq.Size = new Size(120, 23);
            this.numericUpDown_ppq.TabIndex = 11;
            this.numericUpDown_ppq.Value = new decimal(new int[] { 960, 0, 0, 0 });
            // 
            // button_generate
            // 
            this.button_generate.Location = new Point(150, 290);
            this.button_generate.Name = "button_generate";
            this.button_generate.Size = new Size(120, 28);
            this.button_generate.TabIndex = 12;
            this.button_generate.Text = "Generate via LLM";
            this.button_generate.UseVisualStyleBackColor = true;
            this.button_generate.Click += this.button_generate_Click;
            // 
            // button_openMidi
            // 
            this.button_openMidi.Enabled = false;
            this.button_openMidi.Location = new Point(276, 290);
            this.button_openMidi.Name = "button_openMidi";
            this.button_openMidi.Size = new Size(105, 28);
            this.button_openMidi.TabIndex = 13;
            this.button_openMidi.Text = "Open in MIDI";
            this.button_openMidi.UseVisualStyleBackColor = true;
            this.button_openMidi.Click += this.button_openMidi_Click;
            // 
            // button_exportMidi
            // 
            this.button_exportMidi.Enabled = false;
            this.button_exportMidi.Location = new Point(387, 290);
            this.button_exportMidi.Name = "button_exportMidi";
            this.button_exportMidi.Size = new Size(93, 28);
            this.button_exportMidi.TabIndex = 14;
            this.button_exportMidi.Text = "Export .mid";
            this.button_exportMidi.UseVisualStyleBackColor = true;
            this.button_exportMidi.Click += this.button_exportMidi_Click;
            // 
            // label_status
            // 
            this.label_status.AutoSize = true;
            this.label_status.Location = new Point(150, 325);
            this.label_status.Name = "label_status";
            this.label_status.Size = new Size(10, 15);
            this.label_status.TabIndex = 15;
            this.label_status.Text = "-";
            // 
            // MidiLlmGenerateDialog
            // 
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(500, 350);
            this.Controls.Add(this.label_status);
            this.Controls.Add(this.button_exportMidi);
            this.Controls.Add(this.button_openMidi);
            this.Controls.Add(this.button_generate);
            this.Controls.Add(this.numericUpDown_ppq);
            this.Controls.Add(this.label_ppq);
            this.Controls.Add(this.numericUpDown_bars);
            this.Controls.Add(this.label_bars);
            this.Controls.Add(this.numericUpDown_bpm);
            this.Controls.Add(this.label_bpm);
            this.Controls.Add(this.textBox_prompt);
            this.Controls.Add(this.label_prompt);
            this.Controls.Add(this.label_model);
            this.Controls.Add(this.button_connect);
            this.Controls.Add(this.textBox_apiUrl);
            this.Controls.Add(this.label_apiUrl);
            this.MinimumSize = new Size(516, 389);
            this.Name = "MidiLlmGenerateDialog";
            this.Text = "MIDI LLM Generate";
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_bpm).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_bars).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_ppq).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private Label label_apiUrl;
        private TextBox textBox_apiUrl;
        private Button button_connect;
        private Label label_model;
        private Label label_prompt;
        private TextBox textBox_prompt;
        private Label label_bpm;
        private NumericUpDown numericUpDown_bpm;
        private Label label_bars;
        private NumericUpDown numericUpDown_bars;
        private Label label_ppq;
        private NumericUpDown numericUpDown_ppq;
        private Button button_generate;
        private Button button_openMidi;
        private Button button_exportMidi;
        private Label label_status;
    }
}