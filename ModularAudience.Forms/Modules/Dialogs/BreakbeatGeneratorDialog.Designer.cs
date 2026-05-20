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
            this.components = new System.ComponentModel.Container();
            this.listBox_samples = new ListBox();
            this.comboBox_drumset = new ComboBox();
            this.checkBox_autoPlay = new CheckBox();
            this.button_autoMap = new Button();
            this.button_edit = new Button();
            this.numericUpDown_bars = new NumericUpDown();
            this.label_info_bars = new Label();
            this.label_info_resolution = new Label();
            this.numericUpDown_resolution = new NumericUpDown();
            this.label_info_swing = new Label();
            this.numericUpDown_swing = new NumericUpDown();
            this.label_info_complexity = new Label();
            this.numericUpDown_complexity = new NumericUpDown();
            this.label_info_density = new Label();
            this.numericUpDown_density = new NumericUpDown();
            this.numericUpDown_seed = new NumericUpDown();
            this.label_info_seed = new Label();
            this.button_go = new Button();
            this.label_info_bpm = new Label();
            this.numericUpDown_bpm = new NumericUpDown();
            this.checkBox_interleaved = new CheckBox();
            this.contextMenuStrip_samples = new ContextMenuStrip(this.components);
            this.removeToolStripMenuItem = new ToolStripMenuItem();
            this.editToolStripMenuItem = new ToolStripMenuItem();
            this.comboBox_preset = new ComboBox();
            this.textBox_prompt = new TextBox();
            this.button_llm = new Button();
            this.textBox_apiUrl = new TextBox();
            this.pictureBox_beatMap = new PictureBox();
            this.button_bot = new Button();
            this.label_info_reroll = new Label();
            this.numericUpDown_reroll = new NumericUpDown();
            this.colorDialog1 = new ColorDialog();
            this.checkBox_autoExport = new CheckBox();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_bars).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_resolution).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_swing).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_complexity).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_density).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_seed).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_bpm).BeginInit();
            this.contextMenuStrip_samples.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize) this.pictureBox_beatMap).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_reroll).BeginInit();
            this.SuspendLayout();
            // 
            // listBox_samples
            // 
            this.listBox_samples.FormattingEnabled = true;
            this.listBox_samples.Location = new Point(12, 41);
            this.listBox_samples.Name = "listBox_samples";
            this.listBox_samples.Size = new Size(140, 244);
            this.listBox_samples.TabIndex = 0;
            this.listBox_samples.SelectedIndexChanged += this.listBox_samples_SelectedIndexChanged;
            // 
            // comboBox_drumset
            // 
            this.comboBox_drumset.FormattingEnabled = true;
            this.comboBox_drumset.Location = new Point(12, 12);
            this.comboBox_drumset.Name = "comboBox_drumset";
            this.comboBox_drumset.Size = new Size(140, 23);
            this.comboBox_drumset.TabIndex = 1;
            this.comboBox_drumset.Text = "Select drum ...";
            this.comboBox_drumset.SelectedIndexChanged += this.comboBox_drumset_SelectedIndexChanged;
            // 
            // checkBox_autoPlay
            // 
            this.checkBox_autoPlay.AutoSize = true;
            this.checkBox_autoPlay.Location = new Point(12, 291);
            this.checkBox_autoPlay.Name = "checkBox_autoPlay";
            this.checkBox_autoPlay.Size = new Size(77, 19);
            this.checkBox_autoPlay.TabIndex = 2;
            this.checkBox_autoPlay.Text = "Auto Play";
            this.checkBox_autoPlay.UseVisualStyleBackColor = true;
            // 
            // button_autoMap
            // 
            this.button_autoMap.BackColor = Color.FromArgb(  255,   224,   192);
            this.button_autoMap.Location = new Point(12, 316);
            this.button_autoMap.Name = "button_autoMap";
            this.button_autoMap.Size = new Size(75, 23);
            this.button_autoMap.TabIndex = 3;
            this.button_autoMap.Text = "Auto Map";
            this.button_autoMap.UseVisualStyleBackColor = false;
            this.button_autoMap.Click += this.button_autoMap_Click;
            // 
            // button_edit
            // 
            this.button_edit.BackColor = Color.FromArgb(  192,   255,   255);
            this.button_edit.Location = new Point(93, 316);
            this.button_edit.Name = "button_edit";
            this.button_edit.Size = new Size(59, 23);
            this.button_edit.TabIndex = 4;
            this.button_edit.Text = "Edit";
            this.button_edit.UseVisualStyleBackColor = false;
            this.button_edit.Click += this.button_edit_Click;
            // 
            // numericUpDown_bars
            // 
            this.numericUpDown_bars.Location = new Point(12, 406);
            this.numericUpDown_bars.Maximum = new decimal(new int[] { 64, 0, 0, 0 });
            this.numericUpDown_bars.Name = "numericUpDown_bars";
            this.numericUpDown_bars.Size = new Size(50, 23);
            this.numericUpDown_bars.TabIndex = 5;
            this.numericUpDown_bars.Value = new decimal(new int[] { 2, 0, 0, 0 });
            // 
            // label_info_bars
            // 
            this.label_info_bars.AutoSize = true;
            this.label_info_bars.Location = new Point(12, 388);
            this.label_info_bars.Name = "label_info_bars";
            this.label_info_bars.Size = new Size(29, 15);
            this.label_info_bars.TabIndex = 6;
            this.label_info_bars.Text = "Bars";
            // 
            // label_info_resolution
            // 
            this.label_info_resolution.AutoSize = true;
            this.label_info_resolution.Location = new Point(124, 388);
            this.label_info_resolution.Name = "label_info_resolution";
            this.label_info_resolution.Size = new Size(28, 15);
            this.label_info_resolution.TabIndex = 8;
            this.label_info_resolution.Text = "Hits";
            // 
            // numericUpDown_resolution
            // 
            this.numericUpDown_resolution.Location = new Point(124, 406);
            this.numericUpDown_resolution.Maximum = new decimal(new int[] { 128, 0, 0, 0 });
            this.numericUpDown_resolution.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numericUpDown_resolution.Name = "numericUpDown_resolution";
            this.numericUpDown_resolution.Size = new Size(50, 23);
            this.numericUpDown_resolution.TabIndex = 7;
            this.numericUpDown_resolution.Value = new decimal(new int[] { 16, 0, 0, 0 });
            // 
            // label_info_swing
            // 
            this.label_info_swing.AutoSize = true;
            this.label_info_swing.Location = new Point(180, 388);
            this.label_info_swing.Name = "label_info_swing";
            this.label_info_swing.Size = new Size(39, 15);
            this.label_info_swing.TabIndex = 10;
            this.label_info_swing.Text = "Swing";
            // 
            // numericUpDown_swing
            // 
            this.numericUpDown_swing.DecimalPlaces = 3;
            this.numericUpDown_swing.Increment = new decimal(new int[] { 1, 0, 0, 196608 });
            this.numericUpDown_swing.Location = new Point(180, 406);
            this.numericUpDown_swing.Maximum = new decimal(new int[] { 5, 0, 0, 0 });
            this.numericUpDown_swing.Name = "numericUpDown_swing";
            this.numericUpDown_swing.Size = new Size(50, 23);
            this.numericUpDown_swing.TabIndex = 9;
            // 
            // label_info_complexity
            // 
            this.label_info_complexity.AutoSize = true;
            this.label_info_complexity.Location = new Point(236, 388);
            this.label_info_complexity.Name = "label_info_complexity";
            this.label_info_complexity.Size = new Size(49, 15);
            this.label_info_complexity.TabIndex = 12;
            this.label_info_complexity.Text = "Amen??";
            // 
            // numericUpDown_complexity
            // 
            this.numericUpDown_complexity.DecimalPlaces = 3;
            this.numericUpDown_complexity.Increment = new decimal(new int[] { 5, 0, 0, 196608 });
            this.numericUpDown_complexity.Location = new Point(236, 406);
            this.numericUpDown_complexity.Maximum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numericUpDown_complexity.Name = "numericUpDown_complexity";
            this.numericUpDown_complexity.Size = new Size(50, 23);
            this.numericUpDown_complexity.TabIndex = 11;
            this.numericUpDown_complexity.Value = new decimal(new int[] { 666, 0, 0, 196608 });
            // 
            // label_info_density
            // 
            this.label_info_density.AutoSize = true;
            this.label_info_density.Location = new Point(68, 388);
            this.label_info_density.Name = "label_info_density";
            this.label_info_density.Size = new Size(46, 15);
            this.label_info_density.TabIndex = 14;
            this.label_info_density.Text = "Density";
            // 
            // numericUpDown_density
            // 
            this.numericUpDown_density.DecimalPlaces = 3;
            this.numericUpDown_density.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
            this.numericUpDown_density.Location = new Point(68, 406);
            this.numericUpDown_density.Maximum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numericUpDown_density.Name = "numericUpDown_density";
            this.numericUpDown_density.Size = new Size(50, 23);
            this.numericUpDown_density.TabIndex = 13;
            this.numericUpDown_density.Value = new decimal(new int[] { 34, 0, 0, 131072 });
            // 
            // numericUpDown_seed
            // 
            this.numericUpDown_seed.Location = new Point(292, 406);
            this.numericUpDown_seed.Maximum = new decimal(new int[] { 999999999, 0, 0, 0 });
            this.numericUpDown_seed.Name = "numericUpDown_seed";
            this.numericUpDown_seed.Size = new Size(93, 23);
            this.numericUpDown_seed.TabIndex = 15;
            this.numericUpDown_seed.Value = new decimal(new int[] { 987654321, 0, 0, 0 });
            this.numericUpDown_seed.ValueChanged += this.numericUpDown_seed_ValueChanged;
            // 
            // label_info_seed
            // 
            this.label_info_seed.AutoSize = true;
            this.label_info_seed.Location = new Point(292, 388);
            this.label_info_seed.Name = "label_info_seed";
            this.label_info_seed.Size = new Size(32, 15);
            this.label_info_seed.TabIndex = 16;
            this.label_info_seed.Text = "Seed";
            // 
            // button_go
            // 
            this.button_go.BackColor = SystemColors.Info;
            this.button_go.Location = new Point(391, 406);
            this.button_go.Name = "button_go";
            this.button_go.Size = new Size(61, 23);
            this.button_go.TabIndex = 17;
            this.button_go.Text = "Break";
            this.button_go.UseVisualStyleBackColor = false;
            this.button_go.Click += this.button_go_Click;
            // 
            // label_info_bpm
            // 
            this.label_info_bpm.AutoSize = true;
            this.label_info_bpm.Location = new Point(12, 344);
            this.label_info_bpm.Name = "label_info_bpm";
            this.label_info_bpm.Size = new Size(32, 15);
            this.label_info_bpm.TabIndex = 19;
            this.label_info_bpm.Text = "BPM";
            // 
            // numericUpDown_bpm
            // 
            this.numericUpDown_bpm.DecimalPlaces = 3;
            this.numericUpDown_bpm.Increment = new decimal(new int[] { 5, 0, 0, 65536 });
            this.numericUpDown_bpm.Location = new Point(12, 362);
            this.numericUpDown_bpm.Maximum = new decimal(new int[] { 300, 0, 0, 0 });
            this.numericUpDown_bpm.Minimum = new decimal(new int[] { 10, 0, 0, 0 });
            this.numericUpDown_bpm.Name = "numericUpDown_bpm";
            this.numericUpDown_bpm.Size = new Size(65, 23);
            this.numericUpDown_bpm.TabIndex = 18;
            this.numericUpDown_bpm.Value = new decimal(new int[] { 875, 0, 0, 65536 });
            // 
            // checkBox_interleaved
            // 
            this.checkBox_interleaved.AutoSize = true;
            this.checkBox_interleaved.Location = new Point(83, 363);
            this.checkBox_interleaved.Name = "checkBox_interleaved";
            this.checkBox_interleaved.Size = new Size(84, 19);
            this.checkBox_interleaved.TabIndex = 20;
            this.checkBox_interleaved.Text = "Interleaved";
            this.checkBox_interleaved.UseVisualStyleBackColor = true;
            // 
            // contextMenuStrip_samples
            // 
            this.contextMenuStrip_samples.Items.AddRange(new ToolStripItem[] { this.removeToolStripMenuItem, this.editToolStripMenuItem });
            this.contextMenuStrip_samples.Name = "contextMenuStrip_samples";
            this.contextMenuStrip_samples.Size = new Size(118, 48);
            // 
            // removeToolStripMenuItem
            // 
            this.removeToolStripMenuItem.Name = "removeToolStripMenuItem";
            this.removeToolStripMenuItem.Size = new Size(117, 22);
            this.removeToolStripMenuItem.Text = "Remove";
            this.removeToolStripMenuItem.Click += this.removeToolStripMenuItem_Click;
            // 
            // editToolStripMenuItem
            // 
            this.editToolStripMenuItem.Name = "editToolStripMenuItem";
            this.editToolStripMenuItem.Size = new Size(117, 22);
            this.editToolStripMenuItem.Text = "Edit";
            this.editToolStripMenuItem.Click += this.editToolStripMenuItem_Click;
            // 
            // comboBox_preset
            // 
            this.comboBox_preset.FormattingEnabled = true;
            this.comboBox_preset.Location = new Point(225, 316);
            this.comboBox_preset.Name = "comboBox_preset";
            this.comboBox_preset.Size = new Size(160, 23);
            this.comboBox_preset.TabIndex = 21;
            this.comboBox_preset.Text = "Select Preset ...";
            // 
            // textBox_prompt
            // 
            this.textBox_prompt.Location = new Point(158, 170);
            this.textBox_prompt.Multiline = true;
            this.textBox_prompt.Name = "textBox_prompt";
            this.textBox_prompt.PlaceholderText = "Enter descriptive Beat Prompt...";
            this.textBox_prompt.Size = new Size(294, 86);
            this.textBox_prompt.TabIndex = 22;
            // 
            // button_llm
            // 
            this.button_llm.Location = new Point(407, 262);
            this.button_llm.Name = "button_llm";
            this.button_llm.Size = new Size(45, 23);
            this.button_llm.TabIndex = 23;
            this.button_llm.Text = "LLM";
            this.button_llm.UseVisualStyleBackColor = true;
            this.button_llm.Click += this.button_llm_Click;
            // 
            // textBox_apiUrl
            // 
            this.textBox_apiUrl.Location = new Point(158, 262);
            this.textBox_apiUrl.Name = "textBox_apiUrl";
            this.textBox_apiUrl.PlaceholderText = "OpenAI API Url...";
            this.textBox_apiUrl.Size = new Size(243, 23);
            this.textBox_apiUrl.TabIndex = 24;
            this.textBox_apiUrl.Text = "http://127.0.0.1:8080";
            // 
            // pictureBox_beatMap
            // 
            this.pictureBox_beatMap.BackColor = Color.White;
            this.pictureBox_beatMap.Location = new Point(158, 12);
            this.pictureBox_beatMap.Name = "pictureBox_beatMap";
            this.pictureBox_beatMap.Size = new Size(294, 152);
            this.pictureBox_beatMap.TabIndex = 25;
            this.pictureBox_beatMap.TabStop = false;
            // 
            // button_bot
            // 
            this.button_bot.BackColor = SystemColors.Info;
            this.button_bot.Location = new Point(391, 377);
            this.button_bot.Name = "button_bot";
            this.button_bot.Size = new Size(61, 23);
            this.button_bot.TabIndex = 26;
            this.button_bot.Text = "Bot: off";
            this.button_bot.UseVisualStyleBackColor = false;
            this.button_bot.Click += this.button_bot_Click;
            // 
            // label_info_reroll
            // 
            this.label_info_reroll.AutoSize = true;
            this.label_info_reroll.Location = new Point(415, 330);
            this.label_info_reroll.Name = "label_info_reroll";
            this.label_info_reroll.Size = new Size(37, 15);
            this.label_info_reroll.TabIndex = 28;
            this.label_info_reroll.Text = "Reroll";
            // 
            // numericUpDown_reroll
            // 
            this.numericUpDown_reroll.Location = new Point(418, 348);
            this.numericUpDown_reroll.Maximum = new decimal(new int[] { 64, 0, 0, 0 });
            this.numericUpDown_reroll.Name = "numericUpDown_reroll";
            this.numericUpDown_reroll.Size = new Size(34, 23);
            this.numericUpDown_reroll.TabIndex = 27;
            this.numericUpDown_reroll.Value = new decimal(new int[] { 1, 0, 0, 0 });
            // 
            // checkBox_autoExport
            // 
            this.checkBox_autoExport.AutoSize = true;
            this.checkBox_autoExport.Location = new Point(324, 349);
            this.checkBox_autoExport.Name = "checkBox_autoExport";
            this.checkBox_autoExport.Size = new Size(88, 19);
            this.checkBox_autoExport.TabIndex = 29;
            this.checkBox_autoExport.Text = "Auto Export";
            this.checkBox_autoExport.UseVisualStyleBackColor = true;
            // 
            // BreakbeatGeneratorDialog
            // 
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(464, 441);
            this.Controls.Add(this.checkBox_autoExport);
            this.Controls.Add(this.label_info_reroll);
            this.Controls.Add(this.numericUpDown_reroll);
            this.Controls.Add(this.button_bot);
            this.Controls.Add(this.pictureBox_beatMap);
            this.Controls.Add(this.textBox_apiUrl);
            this.Controls.Add(this.button_llm);
            this.Controls.Add(this.textBox_prompt);
            this.Controls.Add(this.comboBox_preset);
            this.Controls.Add(this.checkBox_interleaved);
            this.Controls.Add(this.label_info_bpm);
            this.Controls.Add(this.numericUpDown_bpm);
            this.Controls.Add(this.button_go);
            this.Controls.Add(this.label_info_seed);
            this.Controls.Add(this.numericUpDown_seed);
            this.Controls.Add(this.label_info_density);
            this.Controls.Add(this.numericUpDown_density);
            this.Controls.Add(this.label_info_complexity);
            this.Controls.Add(this.numericUpDown_complexity);
            this.Controls.Add(this.label_info_swing);
            this.Controls.Add(this.numericUpDown_swing);
            this.Controls.Add(this.label_info_resolution);
            this.Controls.Add(this.numericUpDown_resolution);
            this.Controls.Add(this.label_info_bars);
            this.Controls.Add(this.numericUpDown_bars);
            this.Controls.Add(this.button_edit);
            this.Controls.Add(this.button_autoMap);
            this.Controls.Add(this.checkBox_autoPlay);
            this.Controls.Add(this.comboBox_drumset);
            this.Controls.Add(this.listBox_samples);
            this.MaximizeBox = false;
            this.MaximumSize = new Size(480, 480);
            this.MinimumSize = new Size(480, 480);
            this.Name = "BreakbeatGeneratorDialog";
            this.Text = "Breakbeat Generator";
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_bars).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_resolution).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_swing).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_complexity).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_density).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_seed).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_bpm).EndInit();
            this.contextMenuStrip_samples.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize) this.pictureBox_beatMap).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_reroll).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
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
        private Button button_bot;
        private Label label_info_reroll;
        private NumericUpDown numericUpDown_reroll;
        private ColorDialog colorDialog1;
        private CheckBox checkBox_autoExport;
    }
}