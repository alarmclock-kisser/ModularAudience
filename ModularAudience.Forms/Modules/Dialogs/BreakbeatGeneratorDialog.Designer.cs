namespace ModularAudience.Forms.Modules.Dialogs
{
    partial class BreakbeatGeneratorDialog
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
            components = new System.ComponentModel.Container();
            listBox_samples = new ListBox();
            comboBox_drumset = new ComboBox();
            checkBox_autoPlay = new CheckBox();
            button_autoMap = new Button();
            button_edit = new Button();
            numericUpDown_bars = new NumericUpDown();
            label_info_bars = new Label();
            label_info_resolution = new Label();
            numericUpDown_resolution = new NumericUpDown();
            label_info_swing = new Label();
            numericUpDown_swing = new NumericUpDown();
            label_info_complexity = new Label();
            numericUpDown_complexity = new NumericUpDown();
            label_info_density = new Label();
            numericUpDown_density = new NumericUpDown();
            numericUpDown_seed = new NumericUpDown();
            label_info_seed = new Label();
            button_go = new Button();
            label_info_bpm = new Label();
            numericUpDown_bpm = new NumericUpDown();
            checkBox_interleaved = new CheckBox();
            contextMenuStrip_samples = new ContextMenuStrip(components);
            removeToolStripMenuItem = new ToolStripMenuItem();
            editToolStripMenuItem = new ToolStripMenuItem();
            comboBox_preset = new ComboBox();
            textBox_prompt = new TextBox();
            button_llm = new Button();
            textBox_apiUrl = new TextBox();
            pictureBox_beatMap = new PictureBox();
            ((System.ComponentModel.ISupportInitialize)numericUpDown_bars).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDown_resolution).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDown_swing).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDown_complexity).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDown_density).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDown_seed).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDown_bpm).BeginInit();
            contextMenuStrip_samples.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)pictureBox_beatMap).BeginInit();
            SuspendLayout();
            // 
            // listBox_samples
            // 
            listBox_samples.FormattingEnabled = true;
            listBox_samples.Location = new Point(12, 41);
            listBox_samples.Name = "listBox_samples";
            listBox_samples.Size = new Size(140, 244);
            listBox_samples.TabIndex = 0;
            listBox_samples.SelectedIndexChanged += listBox_samples_SelectedIndexChanged;
            // 
            // comboBox_drumset
            // 
            comboBox_drumset.FormattingEnabled = true;
            comboBox_drumset.Location = new Point(12, 12);
            comboBox_drumset.Name = "comboBox_drumset";
            comboBox_drumset.Size = new Size(140, 23);
            comboBox_drumset.TabIndex = 1;
            comboBox_drumset.Text = "Select drum ...";
            comboBox_drumset.SelectedIndexChanged += comboBox_drumset_SelectedIndexChanged;
            // 
            // checkBox_autoPlay
            // 
            checkBox_autoPlay.AutoSize = true;
            checkBox_autoPlay.Location = new Point(12, 291);
            checkBox_autoPlay.Name = "checkBox_autoPlay";
            checkBox_autoPlay.Size = new Size(77, 19);
            checkBox_autoPlay.TabIndex = 2;
            checkBox_autoPlay.Text = "Auto Play";
            checkBox_autoPlay.UseVisualStyleBackColor = true;
            // 
            // button_autoMap
            // 
            button_autoMap.BackColor = Color.FromArgb(255, 224, 192);
            button_autoMap.Location = new Point(12, 316);
            button_autoMap.Name = "button_autoMap";
            button_autoMap.Size = new Size(75, 23);
            button_autoMap.TabIndex = 3;
            button_autoMap.Text = "Auto Map";
            button_autoMap.UseVisualStyleBackColor = false;
            button_autoMap.Click += button_autoMap_Click;
            // 
            // button_edit
            // 
            button_edit.BackColor = Color.FromArgb(192, 255, 255);
            button_edit.Location = new Point(93, 316);
            button_edit.Name = "button_edit";
            button_edit.Size = new Size(59, 23);
            button_edit.TabIndex = 4;
            button_edit.Text = "Edit";
            button_edit.UseVisualStyleBackColor = false;
            button_edit.Click += button_edit_Click;
            // 
            // numericUpDown_bars
            // 
            numericUpDown_bars.Location = new Point(12, 406);
            numericUpDown_bars.Maximum = new decimal(new int[] { 64, 0, 0, 0 });
            numericUpDown_bars.Name = "numericUpDown_bars";
            numericUpDown_bars.Size = new Size(50, 23);
            numericUpDown_bars.TabIndex = 5;
            numericUpDown_bars.Value = new decimal(new int[] { 2, 0, 0, 0 });
            // 
            // label_info_bars
            // 
            label_info_bars.AutoSize = true;
            label_info_bars.Location = new Point(12, 388);
            label_info_bars.Name = "label_info_bars";
            label_info_bars.Size = new Size(29, 15);
            label_info_bars.TabIndex = 6;
            label_info_bars.Text = "Bars";
            // 
            // label_info_resolution
            // 
            label_info_resolution.AutoSize = true;
            label_info_resolution.Location = new Point(124, 388);
            label_info_resolution.Name = "label_info_resolution";
            label_info_resolution.Size = new Size(28, 15);
            label_info_resolution.TabIndex = 8;
            label_info_resolution.Text = "Hits";
            // 
            // numericUpDown_resolution
            // 
            numericUpDown_resolution.Location = new Point(124, 406);
            numericUpDown_resolution.Maximum = new decimal(new int[] { 128, 0, 0, 0 });
            numericUpDown_resolution.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            numericUpDown_resolution.Name = "numericUpDown_resolution";
            numericUpDown_resolution.Size = new Size(50, 23);
            numericUpDown_resolution.TabIndex = 7;
            numericUpDown_resolution.Value = new decimal(new int[] { 16, 0, 0, 0 });
            // 
            // label_info_swing
            // 
            label_info_swing.AutoSize = true;
            label_info_swing.Location = new Point(180, 388);
            label_info_swing.Name = "label_info_swing";
            label_info_swing.Size = new Size(39, 15);
            label_info_swing.TabIndex = 10;
            label_info_swing.Text = "Swing";
            // 
            // numericUpDown_swing
            // 
            numericUpDown_swing.DecimalPlaces = 3;
            numericUpDown_swing.Increment = new decimal(new int[] { 1, 0, 0, 196608 });
            numericUpDown_swing.Location = new Point(180, 406);
            numericUpDown_swing.Maximum = new decimal(new int[] { 5, 0, 0, 0 });
            numericUpDown_swing.Name = "numericUpDown_swing";
            numericUpDown_swing.Size = new Size(50, 23);
            numericUpDown_swing.TabIndex = 9;
            // 
            // label_info_complexity
            // 
            label_info_complexity.AutoSize = true;
            label_info_complexity.Location = new Point(236, 388);
            label_info_complexity.Name = "label_info_complexity";
            label_info_complexity.Size = new Size(49, 15);
            label_info_complexity.TabIndex = 12;
            label_info_complexity.Text = "Amen??";
            // 
            // numericUpDown_complexity
            // 
            numericUpDown_complexity.DecimalPlaces = 3;
            numericUpDown_complexity.Increment = new decimal(new int[] { 5, 0, 0, 196608 });
            numericUpDown_complexity.Location = new Point(236, 406);
            numericUpDown_complexity.Maximum = new decimal(new int[] { 1, 0, 0, 0 });
            numericUpDown_complexity.Name = "numericUpDown_complexity";
            numericUpDown_complexity.Size = new Size(50, 23);
            numericUpDown_complexity.TabIndex = 11;
            numericUpDown_complexity.Value = new decimal(new int[] { 666, 0, 0, 196608 });
            // 
            // label_info_density
            // 
            label_info_density.AutoSize = true;
            label_info_density.Location = new Point(68, 388);
            label_info_density.Name = "label_info_density";
            label_info_density.Size = new Size(46, 15);
            label_info_density.TabIndex = 14;
            label_info_density.Text = "Density";
            // 
            // numericUpDown_density
            // 
            numericUpDown_density.DecimalPlaces = 3;
            numericUpDown_density.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
            numericUpDown_density.Location = new Point(68, 406);
            numericUpDown_density.Maximum = new decimal(new int[] { 1, 0, 0, 0 });
            numericUpDown_density.Name = "numericUpDown_density";
            numericUpDown_density.Size = new Size(50, 23);
            numericUpDown_density.TabIndex = 13;
            numericUpDown_density.Value = new decimal(new int[] { 34, 0, 0, 131072 });
            // 
            // numericUpDown_seed
            // 
            numericUpDown_seed.Location = new Point(292, 406);
            numericUpDown_seed.Maximum = new decimal(new int[] { 999999999, 0, 0, 0 });
            numericUpDown_seed.Name = "numericUpDown_seed";
            numericUpDown_seed.Size = new Size(93, 23);
            numericUpDown_seed.TabIndex = 15;
            numericUpDown_seed.Value = new decimal(new int[] { 987654321, 0, 0, 0 });
            numericUpDown_seed.ValueChanged += numericUpDown_seed_ValueChanged;
            // 
            // label_info_seed
            // 
            label_info_seed.AutoSize = true;
            label_info_seed.Location = new Point(292, 388);
            label_info_seed.Name = "label_info_seed";
            label_info_seed.Size = new Size(32, 15);
            label_info_seed.TabIndex = 16;
            label_info_seed.Text = "Seed";
            // 
            // button_go
            // 
            button_go.BackColor = SystemColors.Info;
            button_go.Location = new Point(391, 406);
            button_go.Name = "button_go";
            button_go.Size = new Size(61, 23);
            button_go.TabIndex = 17;
            button_go.Text = "Break";
            button_go.UseVisualStyleBackColor = false;
            button_go.Click += button_go_Click;
            // 
            // label_info_bpm
            // 
            label_info_bpm.AutoSize = true;
            label_info_bpm.Location = new Point(12, 344);
            label_info_bpm.Name = "label_info_bpm";
            label_info_bpm.Size = new Size(32, 15);
            label_info_bpm.TabIndex = 19;
            label_info_bpm.Text = "BPM";
            // 
            // numericUpDown_bpm
            // 
            numericUpDown_bpm.DecimalPlaces = 3;
            numericUpDown_bpm.Increment = new decimal(new int[] { 5, 0, 0, 65536 });
            numericUpDown_bpm.Location = new Point(12, 362);
            numericUpDown_bpm.Maximum = new decimal(new int[] { 300, 0, 0, 0 });
            numericUpDown_bpm.Minimum = new decimal(new int[] { 10, 0, 0, 0 });
            numericUpDown_bpm.Name = "numericUpDown_bpm";
            numericUpDown_bpm.Size = new Size(65, 23);
            numericUpDown_bpm.TabIndex = 18;
            numericUpDown_bpm.Value = new decimal(new int[] { 875, 0, 0, 65536 });
            // 
            // checkBox_interleaved
            // 
            checkBox_interleaved.AutoSize = true;
            checkBox_interleaved.Location = new Point(83, 363);
            checkBox_interleaved.Name = "checkBox_interleaved";
            checkBox_interleaved.Size = new Size(84, 19);
            checkBox_interleaved.TabIndex = 20;
            checkBox_interleaved.Text = "Interleaved";
            checkBox_interleaved.UseVisualStyleBackColor = true;
            // 
            // contextMenuStrip_samples
            // 
            contextMenuStrip_samples.Items.AddRange(new ToolStripItem[] { removeToolStripMenuItem, editToolStripMenuItem });
            contextMenuStrip_samples.Name = "contextMenuStrip_samples";
            contextMenuStrip_samples.Size = new Size(118, 48);
            // 
            // removeToolStripMenuItem
            // 
            removeToolStripMenuItem.Name = "removeToolStripMenuItem";
            removeToolStripMenuItem.Size = new Size(117, 22);
            removeToolStripMenuItem.Text = "Remove";
            removeToolStripMenuItem.Click += removeToolStripMenuItem_Click;
            // 
            // editToolStripMenuItem
            // 
            editToolStripMenuItem.Name = "editToolStripMenuItem";
            editToolStripMenuItem.Size = new Size(117, 22);
            editToolStripMenuItem.Text = "Edit";
            editToolStripMenuItem.Click += editToolStripMenuItem_Click;
            // 
            // comboBox_preset
            // 
            comboBox_preset.FormattingEnabled = true;
            comboBox_preset.Location = new Point(292, 316);
            comboBox_preset.Name = "comboBox_preset";
            comboBox_preset.Size = new Size(160, 23);
            comboBox_preset.TabIndex = 21;
            comboBox_preset.Text = "Select Preset ...";
            // 
            // textBox_prompt
            // 
            textBox_prompt.Location = new Point(158, 170);
            textBox_prompt.Multiline = true;
            textBox_prompt.Name = "textBox_prompt";
            textBox_prompt.PlaceholderText = "Enter descriptive Beat Prompt...";
            textBox_prompt.Size = new Size(294, 86);
            textBox_prompt.TabIndex = 22;
            // 
            // button_llm
            // 
            button_llm.Location = new Point(407, 262);
            button_llm.Name = "button_llm";
            button_llm.Size = new Size(45, 23);
            button_llm.TabIndex = 23;
            button_llm.Text = "LLM";
            button_llm.UseVisualStyleBackColor = true;
            button_llm.Click += button_llm_Click;
            // 
            // textBox_apiUrl
            // 
            textBox_apiUrl.Location = new Point(158, 262);
            textBox_apiUrl.Name = "textBox_apiUrl";
            textBox_apiUrl.PlaceholderText = "OpenAI API Url...";
            textBox_apiUrl.Size = new Size(243, 23);
            textBox_apiUrl.TabIndex = 24;
            textBox_apiUrl.Text = "http://127.0.0.1:8080";
            // 
            // pictureBox_beatMap
            // 
            pictureBox_beatMap.BackColor = Color.White;
            pictureBox_beatMap.Location = new Point(158, 12);
            pictureBox_beatMap.Name = "pictureBox_beatMap";
            pictureBox_beatMap.Size = new Size(294, 152);
            pictureBox_beatMap.TabIndex = 25;
            pictureBox_beatMap.TabStop = false;
            // 
            // BreakbeatGeneratorDialog
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(464, 441);
            Controls.Add(pictureBox_beatMap);
            Controls.Add(textBox_apiUrl);
            Controls.Add(button_llm);
            Controls.Add(textBox_prompt);
            Controls.Add(comboBox_preset);
            Controls.Add(checkBox_interleaved);
            Controls.Add(label_info_bpm);
            Controls.Add(numericUpDown_bpm);
            Controls.Add(button_go);
            Controls.Add(label_info_seed);
            Controls.Add(numericUpDown_seed);
            Controls.Add(label_info_density);
            Controls.Add(numericUpDown_density);
            Controls.Add(label_info_complexity);
            Controls.Add(numericUpDown_complexity);
            Controls.Add(label_info_swing);
            Controls.Add(numericUpDown_swing);
            Controls.Add(label_info_resolution);
            Controls.Add(numericUpDown_resolution);
            Controls.Add(label_info_bars);
            Controls.Add(numericUpDown_bars);
            Controls.Add(button_edit);
            Controls.Add(button_autoMap);
            Controls.Add(checkBox_autoPlay);
            Controls.Add(comboBox_drumset);
            Controls.Add(listBox_samples);
            MaximizeBox = false;
            MaximumSize = new Size(480, 480);
            MinimumSize = new Size(480, 480);
            Name = "BreakbeatGeneratorDialog";
            Text = "Breakbeat Generator";
            ((System.ComponentModel.ISupportInitialize)numericUpDown_bars).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDown_resolution).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDown_swing).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDown_complexity).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDown_density).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDown_seed).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDown_bpm).EndInit();
            contextMenuStrip_samples.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)pictureBox_beatMap).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private ListBox listBox_samples;
        private ComboBox comboBox_drumset;
        private CheckBox checkBox_autoPlay;
        private Button button_autoMap;
        private Button button_edit;
        private NumericUpDown numericUpDown_bars;
        private Label label_info_bars;
        private Label label_info_resolution;
        private NumericUpDown numericUpDown_resolution;
        private Label label_info_swing;
        private NumericUpDown numericUpDown_swing;
        private Label label_info_complexity;
        private NumericUpDown numericUpDown_complexity;
        private Label label_info_density;
        private NumericUpDown numericUpDown_density;
        private NumericUpDown numericUpDown_seed;
        private Label label_info_seed;
        private Button button_go;
        private Label label_info_bpm;
        private NumericUpDown numericUpDown_bpm;
        private CheckBox checkBox_interleaved;
		private ContextMenuStrip contextMenuStrip_samples;
		private ToolStripMenuItem removeToolStripMenuItem;
		private ToolStripMenuItem editToolStripMenuItem;
		private ComboBox comboBox_preset;
        private TextBox textBox_prompt;
        private Button button_llm;
        private TextBox textBox_apiUrl;
        private PictureBox pictureBox_beatMap;
    }
}