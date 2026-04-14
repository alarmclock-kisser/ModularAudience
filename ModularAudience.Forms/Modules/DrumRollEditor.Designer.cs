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
            panel_pattern = new BufferedPatternPanel();
            button_hit = new Button();
            label_info_dragndrop = new Label();
            button_playback = new Button();
            numericUpDown_bpm = new NumericUpDown();
            domainUpDown_hits = new DomainUpDown();
            label_info_bpm = new Label();
            label_info_hits = new Label();
            numericUpDown_volume = new NumericUpDown();
            label_info_volume = new Label();
            button_export = new Button();
            numericUpDown_rerollInterval = new NumericUpDown();
            label_info_randomDensity = new Label();
            numericUpDown_randomDensity = new NumericUpDown();
            label_info_randomAccent = new Label();
            numericUpDown_randomAccent = new NumericUpDown();
            label_info_randomStreak = new Label();
            numericUpDown_randomStreak = new NumericUpDown();
            label_info_randomVariation = new Label();
            numericUpDown_randomVariation = new NumericUpDown();
            button_randomize = new Button();
            label_info_reroll = new Label();
            checkBox_interleaved = new CheckBox();
            checkBox_launchpad = new CheckBox();
            panel_pattern.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)numericUpDown_bpm).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDown_volume).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDown_rerollInterval).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDown_randomDensity).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDown_randomAccent).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDown_randomStreak).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDown_randomVariation).BeginInit();
            SuspendLayout();
            // 
            // panel_pattern
            // 
            panel_pattern.AllowDrop = true;
            panel_pattern.BackColor = SystemColors.ControlLight;
            panel_pattern.Controls.Add(button_hit);
            panel_pattern.Location = new Point(12, 66);
            panel_pattern.Name = "panel_pattern";
            panel_pattern.Size = new Size(680, 50);
            panel_pattern.TabIndex = 0;
            panel_pattern.DragDrop += DrumRollEditor_DragDrop;
            panel_pattern.DragEnter += DrumRollEditor_DragEnter;
            panel_pattern.Paint += panel_pattern_Paint;
            panel_pattern.MouseClick += panel_pattern_MouseClick;
            // 
            // button_hit
            // 
            button_hit.Location = new Point(3, 3);
            button_hit.Name = "button_hit";
            button_hit.Size = new Size(35, 44);
            button_hit.TabIndex = 1;
            button_hit.Text = "0";
            button_hit.UseVisualStyleBackColor = true;
            // 
            // label_info_dragndrop
            // 
            label_info_dragndrop.AllowDrop = true;
            label_info_dragndrop.AutoSize = true;
            label_info_dragndrop.Location = new Point(270, 53);
            label_info_dragndrop.Name = "label_info_dragndrop";
            label_info_dragndrop.Size = new Size(138, 15);
            label_info_dragndrop.TabIndex = 10;
            label_info_dragndrop.Text = "Drop Sample here to add";
            label_info_dragndrop.DragDrop += DrumRollEditor_DragDrop;
            label_info_dragndrop.DragEnter += DrumRollEditor_DragEnter;
            // 
            // button_playback
            // 
            button_playback.Location = new Point(11, 28);
            button_playback.Margin = new Padding(2);
            button_playback.Name = "button_playback";
            button_playback.Size = new Size(23, 23);
            button_playback.TabIndex = 5;
            button_playback.TabStop = false;
            button_playback.Tag = "■";
            button_playback.Text = "▶";
            button_playback.UseVisualStyleBackColor = true;
            button_playback.Click += button_playback_Click;
            // 
            // numericUpDown_bpm
            // 
            numericUpDown_bpm.DecimalPlaces = 3;
            numericUpDown_bpm.Increment = new decimal(new int[] { 5, 0, 0, 65536 });
            numericUpDown_bpm.Location = new Point(39, 27);
            numericUpDown_bpm.Maximum = new decimal(new int[] { 360, 0, 0, 0 });
            numericUpDown_bpm.Minimum = new decimal(new int[] { 15, 0, 0, 0 });
            numericUpDown_bpm.Name = "numericUpDown_bpm";
            numericUpDown_bpm.ReadOnly = true;
            numericUpDown_bpm.Size = new Size(90, 23);
            numericUpDown_bpm.TabIndex = 6;
            numericUpDown_bpm.TabStop = false;
            numericUpDown_bpm.Value = new decimal(new int[] { 90, 0, 0, 0 });
            // 
            // domainUpDown_hits
            // 
            domainUpDown_hits.Items.Add("32");
            domainUpDown_hits.Items.Add("28");
            domainUpDown_hits.Items.Add("24");
            domainUpDown_hits.Items.Add("20");
            domainUpDown_hits.Items.Add("16");
            domainUpDown_hits.Items.Add("12");
            domainUpDown_hits.Items.Add("8");
            domainUpDown_hits.Items.Add("4");
            domainUpDown_hits.Location = new Point(140, 27);
            domainUpDown_hits.Name = "domainUpDown_hits";
            domainUpDown_hits.ReadOnly = true;
            domainUpDown_hits.Size = new Size(55, 23);
            domainUpDown_hits.TabIndex = 7;
            domainUpDown_hits.TabStop = false;
            domainUpDown_hits.Text = "Hits";
            domainUpDown_hits.SelectedItemChanged += domainUpDown_hits_SelectedItemChanged;
            // 
            // label_info_bpm
            // 
            label_info_bpm.AutoSize = true;
            label_info_bpm.Location = new Point(39, 9);
            label_info_bpm.Name = "label_info_bpm";
            label_info_bpm.Size = new Size(32, 15);
            label_info_bpm.TabIndex = 8;
            label_info_bpm.Text = "BPM";
            // 
            // label_info_hits
            // 
            label_info_hits.AutoSize = true;
            label_info_hits.Location = new Point(140, 9);
            label_info_hits.Name = "label_info_hits";
            label_info_hits.Size = new Size(28, 15);
            label_info_hits.TabIndex = 9;
            label_info_hits.Text = "Hits";
            // 
            // numericUpDown_volume
            // 
            numericUpDown_volume.Location = new Point(201, 27);
            numericUpDown_volume.Name = "numericUpDown_volume";
            numericUpDown_volume.ReadOnly = true;
            numericUpDown_volume.Size = new Size(55, 23);
            numericUpDown_volume.TabIndex = 11;
            numericUpDown_volume.TabStop = false;
            numericUpDown_volume.Value = new decimal(new int[] { 80, 0, 0, 0 });
            // 
            // label_info_volume
            // 
            label_info_volume.AutoSize = true;
            label_info_volume.Location = new Point(201, 9);
            label_info_volume.Name = "label_info_volume";
            label_info_volume.Size = new Size(47, 15);
            label_info_volume.TabIndex = 12;
            label_info_volume.Text = "Volume";
            // 
            // button_export
            // 
            button_export.BackColor = Color.FromArgb(192, 255, 255);
            button_export.Location = new Point(617, 12);
            button_export.Name = "button_export";
            button_export.Size = new Size(75, 23);
            button_export.TabIndex = 13;
            button_export.TabStop = false;
            button_export.Text = "Export";
            button_export.UseVisualStyleBackColor = false;
            button_export.Click += button_export_Click;
            // 
            // numericUpDown_rerollInterval
            // 
            numericUpDown_rerollInterval.Location = new Point(571, 27);
            numericUpDown_rerollInterval.Maximum = new decimal(new int[] { 32, 0, 0, 0 });
            numericUpDown_rerollInterval.Name = "numericUpDown_rerollInterval";
            numericUpDown_rerollInterval.Size = new Size(40, 23);
            numericUpDown_rerollInterval.TabIndex = 14;
            // 
            // label_info_randomDensity
            // 
            label_info_randomDensity.AutoSize = true;
            label_info_randomDensity.Location = new Point(262, 9);
            label_info_randomDensity.Name = "label_info_randomDensity";
            label_info_randomDensity.Size = new Size(39, 15);
            label_info_randomDensity.TabIndex = 15;
            label_info_randomDensity.Text = "Dense";
            // 
            // numericUpDown_randomDensity
            // 
            numericUpDown_randomDensity.Location = new Point(262, 27);
            numericUpDown_randomDensity.Name = "numericUpDown_randomDensity";
            numericUpDown_randomDensity.Size = new Size(50, 23);
            numericUpDown_randomDensity.TabIndex = 16;
            numericUpDown_randomDensity.Value = new decimal(new int[] { 45, 0, 0, 0 });
            // 
            // label_info_randomAccent
            // 
            label_info_randomAccent.AutoSize = true;
            label_info_randomAccent.Location = new Point(318, 9);
            label_info_randomAccent.Name = "label_info_randomAccent";
            label_info_randomAccent.Size = new Size(44, 15);
            label_info_randomAccent.TabIndex = 17;
            label_info_randomAccent.Text = "Accent";
            // 
            // numericUpDown_randomAccent
            // 
            numericUpDown_randomAccent.Location = new Point(318, 27);
            numericUpDown_randomAccent.Name = "numericUpDown_randomAccent";
            numericUpDown_randomAccent.Size = new Size(50, 23);
            numericUpDown_randomAccent.TabIndex = 18;
            numericUpDown_randomAccent.Value = new decimal(new int[] { 30, 0, 0, 0 });
            // 
            // label_info_randomStreak
            // 
            label_info_randomStreak.AutoSize = true;
            label_info_randomStreak.Location = new Point(374, 9);
            label_info_randomStreak.Name = "label_info_randomStreak";
            label_info_randomStreak.Size = new Size(34, 15);
            label_info_randomStreak.TabIndex = 19;
            label_info_randomStreak.Text = "Burst";
            // 
            // numericUpDown_randomStreak
            // 
            numericUpDown_randomStreak.Location = new Point(374, 27);
            numericUpDown_randomStreak.Name = "numericUpDown_randomStreak";
            numericUpDown_randomStreak.Size = new Size(50, 23);
            numericUpDown_randomStreak.TabIndex = 20;
            numericUpDown_randomStreak.Value = new decimal(new int[] { 15, 0, 0, 0 });
            // 
            // label_info_randomVariation
            // 
            label_info_randomVariation.AutoSize = true;
            label_info_randomVariation.Location = new Point(430, 9);
            label_info_randomVariation.Name = "label_info_randomVariation";
            label_info_randomVariation.Size = new Size(23, 15);
            label_info_randomVariation.TabIndex = 21;
            label_info_randomVariation.Text = "Var";
            // 
            // numericUpDown_randomVariation
            // 
            numericUpDown_randomVariation.Location = new Point(430, 27);
            numericUpDown_randomVariation.Name = "numericUpDown_randomVariation";
            numericUpDown_randomVariation.Size = new Size(50, 23);
            numericUpDown_randomVariation.TabIndex = 22;
            numericUpDown_randomVariation.Value = new decimal(new int[] { 25, 0, 0, 0 });
            // 
            // button_randomize
            // 
            button_randomize.Location = new Point(490, 27);
            button_randomize.Name = "button_randomize";
            button_randomize.Size = new Size(75, 23);
            button_randomize.TabIndex = 23;
            button_randomize.Text = "Random";
            button_randomize.UseVisualStyleBackColor = true;
            button_randomize.Click += button_randomize_Click;
            // 
            // label_info_reroll
            // 
            label_info_reroll.AutoSize = true;
            label_info_reroll.Location = new Point(571, 9);
            label_info_reroll.Name = "label_info_reroll";
            label_info_reroll.Size = new Size(37, 15);
            label_info_reroll.TabIndex = 24;
            label_info_reroll.Text = "Reroll";
            // 
            // checkBox_interleaved
            // 
            checkBox_interleaved.AutoSize = true;
            checkBox_interleaved.Location = new Point(481, 2);
            checkBox_interleaved.Name = "checkBox_interleaved";
            checkBox_interleaved.Size = new Size(84, 19);
            checkBox_interleaved.TabIndex = 25;
            checkBox_interleaved.Text = "Interleaved";
            checkBox_interleaved.UseVisualStyleBackColor = true;
            checkBox_interleaved.CheckedChanged += checkBox_interleaved_CheckedChanged;
            // 
            // checkBox_launchpad
            // 
            checkBox_launchpad.AutoSize = true;
            checkBox_launchpad.Location = new Point(371, 2);
            checkBox_launchpad.Name = "checkBox_launchpad";
            checkBox_launchpad.Size = new Size(104, 19);
            checkBox_launchpad.TabIndex = 26;
            checkBox_launchpad.Text = "Launchpad";
            checkBox_launchpad.UseVisualStyleBackColor = true;
            // 
            // DrumRollEditor
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(704, 441);
            Controls.Add(checkBox_launchpad);
            Controls.Add(checkBox_interleaved);
            Controls.Add(label_info_dragndrop);
            Controls.Add(label_info_reroll);
            Controls.Add(button_randomize);
            Controls.Add(numericUpDown_randomVariation);
            Controls.Add(label_info_randomVariation);
            Controls.Add(numericUpDown_randomStreak);
            Controls.Add(label_info_randomStreak);
            Controls.Add(numericUpDown_randomAccent);
            Controls.Add(label_info_randomAccent);
            Controls.Add(numericUpDown_randomDensity);
            Controls.Add(label_info_randomDensity);
            Controls.Add(numericUpDown_rerollInterval);
            Controls.Add(button_export);
            Controls.Add(label_info_volume);
            Controls.Add(numericUpDown_volume);
            Controls.Add(label_info_hits);
            Controls.Add(label_info_bpm);
            Controls.Add(domainUpDown_hits);
            Controls.Add(numericUpDown_bpm);
            Controls.Add(button_playback);
            Controls.Add(panel_pattern);
            KeyPreview = true;
            MaximizeBox = false;
            MaximumSize = new Size(1280, 480);
            MinimumSize = new Size(720, 480);
            Name = "DrumRollEditor";
            Text = "Drum Roll Editor";
            panel_pattern.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)numericUpDown_bpm).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDown_volume).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDown_rerollInterval).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDown_randomDensity).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDown_randomAccent).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDown_randomStreak).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDown_randomVariation).EndInit();
            ResumeLayout(false);
            PerformLayout();
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
        private CheckBox checkBox_launchpad;
    }
}