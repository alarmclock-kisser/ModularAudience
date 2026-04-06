namespace ModularAudience.Forms.Modules
{
    partial class DrumRollEditor
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
            this.panel_pattern = new BufferedPatternPanel();
            this.button_hit = new Button();
            this.button_playback = new Button();
            this.numericUpDown_bpm = new NumericUpDown();
            this.domainUpDown_hits = new DomainUpDown();
            this.label_info_bpm = new Label();
            this.label_info_hits = new Label();
            this.label_info_dragndrop = new Label();
            this.numericUpDown_volume = new NumericUpDown();
            this.label_info_volume = new Label();
            this.button_export = new Button();
            this.numericUpDown_rerollInterval = new NumericUpDown();
            this.label_info_randomDensity = new Label();
            this.numericUpDown_randomDensity = new NumericUpDown();
            this.label_info_randomAccent = new Label();
            this.numericUpDown_randomAccent = new NumericUpDown();
            this.label_info_randomStreak = new Label();
            this.numericUpDown_randomStreak = new NumericUpDown();
            this.label_info_randomVariation = new Label();
            this.numericUpDown_randomVariation = new NumericUpDown();
            this.button_randomize = new Button();
            this.label_info_reroll = new Label();
            this.checkBox_interleaved = new CheckBox();
            this.panel_pattern.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_bpm).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_volume).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_rerollInterval).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_randomDensity).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_randomAccent).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_randomStreak).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_randomVariation).BeginInit();
            this.SuspendLayout();
            // 
            // panel_pattern
            // 
            this.panel_pattern.BackColor = SystemColors.ControlLight;
            this.panel_pattern.Controls.Add(this.button_hit);
            this.panel_pattern.Location = new Point(12, 56);
            this.panel_pattern.Name = "panel_pattern";
            this.panel_pattern.Size = new Size(680, 50);
            this.panel_pattern.TabIndex = 0;
            this.panel_pattern.Paint += this.panel_pattern_Paint;
            this.panel_pattern.MouseClick += this.panel_pattern_MouseClick;
            // 
            // button_hit
            // 
            this.button_hit.Location = new Point(3, 3);
            this.button_hit.Name = "button_hit";
            this.button_hit.Size = new Size(35, 44);
            this.button_hit.TabIndex = 1;
            this.button_hit.Text = "0";
            this.button_hit.UseVisualStyleBackColor = true;
            // 
            // button_playback
            // 
            this.button_playback.Location = new Point(11, 11);
            this.button_playback.Margin = new Padding(2);
            this.button_playback.Name = "button_playback";
            this.button_playback.Size = new Size(23, 23);
            this.button_playback.TabIndex = 5;
            this.button_playback.TabStop = false;
            this.button_playback.Tag = "■";
            this.button_playback.Text = "▶";
            this.button_playback.UseVisualStyleBackColor = true;
            this.button_playback.Click += this.button_playback_Click;
            // 
            // numericUpDown_bpm
            // 
            this.numericUpDown_bpm.DecimalPlaces = 3;
            this.numericUpDown_bpm.Increment = new decimal(new int[] { 5, 0, 0, 65536 });
            this.numericUpDown_bpm.Location = new Point(39, 12);
            this.numericUpDown_bpm.Maximum = new decimal(new int[] { 360, 0, 0, 0 });
            this.numericUpDown_bpm.Minimum = new decimal(new int[] { 15, 0, 0, 0 });
            this.numericUpDown_bpm.Name = "numericUpDown_bpm";
            this.numericUpDown_bpm.ReadOnly = true;
            this.numericUpDown_bpm.Size = new Size(75, 23);
            this.numericUpDown_bpm.TabIndex = 6;
            this.numericUpDown_bpm.TabStop = false;
            this.numericUpDown_bpm.Value = new decimal(new int[] { 90, 0, 0, 0 });
            // 
            // domainUpDown_hits
            // 
            this.domainUpDown_hits.Items.Add("32");
            this.domainUpDown_hits.Items.Add("28");
            this.domainUpDown_hits.Items.Add("24");
            this.domainUpDown_hits.Items.Add("20");
            this.domainUpDown_hits.Items.Add("16");
            this.domainUpDown_hits.Items.Add("12");
            this.domainUpDown_hits.Items.Add("8");
            this.domainUpDown_hits.Items.Add("4");
            this.domainUpDown_hits.Location = new Point(120, 12);
            this.domainUpDown_hits.Name = "domainUpDown_hits";
            this.domainUpDown_hits.ReadOnly = true;
            this.domainUpDown_hits.Size = new Size(75, 23);
            this.domainUpDown_hits.TabIndex = 7;
            this.domainUpDown_hits.TabStop = false;
            this.domainUpDown_hits.Text = "Hits";
            this.domainUpDown_hits.SelectedItemChanged += this.domainUpDown_hits_SelectedItemChanged;
            // 
            // label_info_bpm
            // 
            this.label_info_bpm.AutoSize = true;
            this.label_info_bpm.Location = new Point(39, 38);
            this.label_info_bpm.Name = "label_info_bpm";
            this.label_info_bpm.Size = new Size(32, 15);
            this.label_info_bpm.TabIndex = 8;
            this.label_info_bpm.Text = "BPM";
            // 
            // label_info_hits
            // 
            this.label_info_hits.AutoSize = true;
            this.label_info_hits.Location = new Point(120, 38);
            this.label_info_hits.Name = "label_info_hits";
            this.label_info_hits.Size = new Size(28, 15);
            this.label_info_hits.TabIndex = 9;
            this.label_info_hits.Text = "Hits";
            // 
            // label_info_dragndrop
            // 
            this.label_info_dragndrop.AutoSize = true;
            this.label_info_dragndrop.Location = new Point(254, 54);
            this.label_info_dragndrop.Name = "label_info_dragndrop";
            this.label_info_dragndrop.Size = new Size(138, 15);
            this.label_info_dragndrop.TabIndex = 10;
            this.label_info_dragndrop.Text = "Drop Sample here to add";
            // 
            // numericUpDown_volume
            // 
            this.numericUpDown_volume.Location = new Point(201, 27);
            this.numericUpDown_volume.Name = "numericUpDown_volume";
            this.numericUpDown_volume.ReadOnly = true;
            this.numericUpDown_volume.Size = new Size(55, 23);
            this.numericUpDown_volume.TabIndex = 11;
            this.numericUpDown_volume.TabStop = false;
            this.numericUpDown_volume.Value = new decimal(new int[] { 80, 0, 0, 0 });
            // 
            // label_info_volume
            // 
            this.label_info_volume.AutoSize = true;
            this.label_info_volume.Location = new Point(201, 9);
            this.label_info_volume.Name = "label_info_volume";
            this.label_info_volume.Size = new Size(47, 15);
            this.label_info_volume.TabIndex = 12;
            this.label_info_volume.Text = "Volume";
            // 
            // button_export
            // 
            this.button_export.BackColor = Color.FromArgb(  192,   255,   255);
            this.button_export.Location = new Point(617, 12);
            this.button_export.Name = "button_export";
            this.button_export.Size = new Size(75, 23);
            this.button_export.TabIndex = 13;
            this.button_export.TabStop = false;
            this.button_export.Text = "Export";
            this.button_export.UseVisualStyleBackColor = false;
            this.button_export.Click += this.button_export_Click;
            // 
            // numericUpDown_rerollInterval
            // 
            this.numericUpDown_rerollInterval.Location = new Point(551, 12);
            this.numericUpDown_rerollInterval.Maximum = new decimal(new int[] { 32, 0, 0, 0 });
            this.numericUpDown_rerollInterval.Name = "numericUpDown_rerollInterval";
            this.numericUpDown_rerollInterval.Size = new Size(60, 23);
            this.numericUpDown_rerollInterval.TabIndex = 14;
            // 
            // label_info_randomDensity
            // 
            this.label_info_randomDensity.AutoSize = true;
            this.label_info_randomDensity.Location = new Point(262, 9);
            this.label_info_randomDensity.Name = "label_info_randomDensity";
            this.label_info_randomDensity.Size = new Size(43, 15);
            this.label_info_randomDensity.TabIndex = 15;
            this.label_info_randomDensity.Text = "Dense";
            // 
            // numericUpDown_randomDensity
            // 
            this.numericUpDown_randomDensity.Location = new Point(262, 27);
            this.numericUpDown_randomDensity.Maximum = new decimal(new int[] { 100, 0, 0, 0 });
            this.numericUpDown_randomDensity.Name = "numericUpDown_randomDensity";
            this.numericUpDown_randomDensity.Size = new Size(50, 23);
            this.numericUpDown_randomDensity.TabIndex = 16;
            this.numericUpDown_randomDensity.Value = new decimal(new int[] { 45, 0, 0, 0 });
            // 
            // label_info_randomAccent
            // 
            this.label_info_randomAccent.AutoSize = true;
            this.label_info_randomAccent.Location = new Point(318, 9);
            this.label_info_randomAccent.Name = "label_info_randomAccent";
            this.label_info_randomAccent.Size = new Size(43, 15);
            this.label_info_randomAccent.TabIndex = 17;
            this.label_info_randomAccent.Text = "Accent";
            // 
            // numericUpDown_randomAccent
            // 
            this.numericUpDown_randomAccent.Location = new Point(318, 27);
            this.numericUpDown_randomAccent.Maximum = new decimal(new int[] { 100, 0, 0, 0 });
            this.numericUpDown_randomAccent.Name = "numericUpDown_randomAccent";
            this.numericUpDown_randomAccent.Size = new Size(50, 23);
            this.numericUpDown_randomAccent.TabIndex = 18;
            this.numericUpDown_randomAccent.Value = new decimal(new int[] { 30, 0, 0, 0 });
            // 
            // label_info_randomStreak
            // 
            this.label_info_randomStreak.AutoSize = true;
            this.label_info_randomStreak.Location = new Point(374, 9);
            this.label_info_randomStreak.Name = "label_info_randomStreak";
            this.label_info_randomStreak.Size = new Size(37, 15);
            this.label_info_randomStreak.TabIndex = 19;
            this.label_info_randomStreak.Text = "Burst";
            // 
            // numericUpDown_randomStreak
            // 
            this.numericUpDown_randomStreak.Location = new Point(374, 27);
            this.numericUpDown_randomStreak.Maximum = new decimal(new int[] { 100, 0, 0, 0 });
            this.numericUpDown_randomStreak.Name = "numericUpDown_randomStreak";
            this.numericUpDown_randomStreak.Size = new Size(50, 23);
            this.numericUpDown_randomStreak.TabIndex = 20;
            this.numericUpDown_randomStreak.Value = new decimal(new int[] { 15, 0, 0, 0 });
            // 
            // label_info_randomVariation
            // 
            this.label_info_randomVariation.AutoSize = true;
            this.label_info_randomVariation.Location = new Point(430, 9);
            this.label_info_randomVariation.Name = "label_info_randomVariation";
            this.label_info_randomVariation.Size = new Size(25, 15);
            this.label_info_randomVariation.TabIndex = 21;
            this.label_info_randomVariation.Text = "Var";
            // 
            // numericUpDown_randomVariation
            // 
            this.numericUpDown_randomVariation.Location = new Point(430, 27);
            this.numericUpDown_randomVariation.Maximum = new decimal(new int[] { 100, 0, 0, 0 });
            this.numericUpDown_randomVariation.Name = "numericUpDown_randomVariation";
            this.numericUpDown_randomVariation.Size = new Size(50, 23);
            this.numericUpDown_randomVariation.TabIndex = 22;
            this.numericUpDown_randomVariation.Value = new decimal(new int[] { 25, 0, 0, 0 });
            // 
            // button_randomize
            // 
            this.button_randomize.Location = new Point(486, 27);
            this.button_randomize.Name = "button_randomize";
            this.button_randomize.Size = new Size(59, 23);
            this.button_randomize.TabIndex = 23;
            this.button_randomize.Text = "Random";
            this.button_randomize.UseVisualStyleBackColor = true;
            this.button_randomize.Click += this.button_randomize_Click;
            // 
            // label_info_reroll
            // 
            this.label_info_reroll.AutoSize = true;
            this.label_info_reroll.Location = new Point(551, 9);
            this.label_info_reroll.Name = "label_info_reroll";
            this.label_info_reroll.Size = new Size(40, 15);
            this.label_info_reroll.TabIndex = 24;
            this.label_info_reroll.Text = "Reroll";
            // 
            // checkBox_interleaved
            // 
            this.checkBox_interleaved.AutoSize = true;
            this.checkBox_interleaved.Location = new Point(486, 8);
            this.checkBox_interleaved.Name = "checkBox_interleaved";
            this.checkBox_interleaved.Size = new Size(83, 19);
            this.checkBox_interleaved.TabIndex = 25;
            this.checkBox_interleaved.Text = "Interleaved";
            this.checkBox_interleaved.UseVisualStyleBackColor = true;
            this.checkBox_interleaved.CheckedChanged += this.checkBox_interleaved_CheckedChanged;
            // 
            // DrumRollEditor
            // 
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(704, 441);
            this.Controls.Add(this.checkBox_interleaved);
            this.Controls.Add(this.label_info_reroll);
            this.Controls.Add(this.button_randomize);
            this.Controls.Add(this.numericUpDown_randomVariation);
            this.Controls.Add(this.label_info_randomVariation);
            this.Controls.Add(this.numericUpDown_randomStreak);
            this.Controls.Add(this.label_info_randomStreak);
            this.Controls.Add(this.numericUpDown_randomAccent);
            this.Controls.Add(this.label_info_randomAccent);
            this.Controls.Add(this.numericUpDown_randomDensity);
            this.Controls.Add(this.label_info_randomDensity);
            this.Controls.Add(this.numericUpDown_rerollInterval);
            this.Controls.Add(this.button_export);
            this.Controls.Add(this.label_info_volume);
            this.Controls.Add(this.numericUpDown_volume);
            this.Controls.Add(this.label_info_dragndrop);
            this.Controls.Add(this.label_info_hits);
            this.Controls.Add(this.label_info_bpm);
            this.Controls.Add(this.domainUpDown_hits);
            this.Controls.Add(this.numericUpDown_bpm);
            this.Controls.Add(this.button_playback);
            this.Controls.Add(this.panel_pattern);
            this.KeyPreview = true;
            this.MaximizeBox = false;
            this.MaximumSize = new Size(1280, 480);
            this.MinimumSize = new Size(720, 480);
            this.Name = "DrumRollEditor";
            this.Text = "Drum Roll Editor";
            this.panel_pattern.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_bpm).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_volume).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_rerollInterval).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_randomDensity).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_randomAccent).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_randomStreak).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_randomVariation).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private BufferedPatternPanel panel_pattern;
        private Button button_hit;
        private Button button_playback;
        private NumericUpDown numericUpDown_bpm;
        private DomainUpDown domainUpDown_hits;
        private Label label_info_bpm;
        private Label label_info_hits;
        private Label label_info_dragndrop;
        private NumericUpDown numericUpDown_volume;
        private Label label_info_volume;
        private Button button_export;
        private NumericUpDown numericUpDown_rerollInterval;
        private Label label_info_randomDensity;
        private NumericUpDown numericUpDown_randomDensity;
        private Label label_info_randomAccent;
        private NumericUpDown numericUpDown_randomAccent;
        private Label label_info_randomStreak;
        private NumericUpDown numericUpDown_randomStreak;
        private Label label_info_randomVariation;
        private NumericUpDown numericUpDown_randomVariation;
        private Button button_randomize;
        private Label label_info_reroll;
        private CheckBox checkBox_interleaved;
    }
}