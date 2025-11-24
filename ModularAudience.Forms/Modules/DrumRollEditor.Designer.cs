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
            this.panel_pattern = new Panel();
            this.button_hit = new Button();
            this.button_playback = new Button();
            this.numericUpDown_bpm = new NumericUpDown();
            this.domainUpDown_hits = new DomainUpDown();
            this.label_info_bpm = new Label();
            this.label_info_hits = new Label();
            this.label_info_dragndrop = new Label();
            this.panel_pattern.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_bpm).BeginInit();
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
            this.numericUpDown_bpm.Size = new Size(75, 23);
            this.numericUpDown_bpm.TabIndex = 6;
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
            this.domainUpDown_hits.Size = new Size(75, 23);
            this.domainUpDown_hits.TabIndex = 7;
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
            this.label_info_dragndrop.Location = new Point(554, 9);
            this.label_info_dragndrop.Name = "label_info_dragndrop";
            this.label_info_dragndrop.Size = new Size(138, 15);
            this.label_info_dragndrop.TabIndex = 10;
            this.label_info_dragndrop.Text = "Drop Sample here to add";
            // 
            // DrumRollEditor
            // 
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(704, 441);
            this.Controls.Add(this.label_info_dragndrop);
            this.Controls.Add(this.label_info_hits);
            this.Controls.Add(this.label_info_bpm);
            this.Controls.Add(this.domainUpDown_hits);
            this.Controls.Add(this.numericUpDown_bpm);
            this.Controls.Add(this.button_playback);
            this.Controls.Add(this.panel_pattern);
            this.MaximizeBox = false;
            this.MaximumSize = new Size(720, 480);
            this.MinimumSize = new Size(720, 480);
            this.Name = "DrumRollEditor";
            this.Text = "Drum Roll Editor";
            this.panel_pattern.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_bpm).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private Panel panel_pattern;
        private Button button_hit;
        private Button button_playback;
        private NumericUpDown numericUpDown_bpm;
        private DomainUpDown domainUpDown_hits;
        private Label label_info_bpm;
        private Label label_info_hits;
        private Label label_info_dragndrop;
    }
}