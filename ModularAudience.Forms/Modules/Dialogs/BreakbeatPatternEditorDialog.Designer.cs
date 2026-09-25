namespace ModularAudience.Forms.Modules.Dialogs
{
    partial class BreakbeatPatternEditorDialog
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }

            if (disposing && this.samples is not null)
            {
                foreach (ModularAudience.Audio.AudioObj sample in this.samples)
                {
                    sample.Dispose();
                }
            }

            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            pictureBox_pattern = new BufferedPictureBox();
            panel_pattern = new Panel();
            hScrollBar_pattern = new HScrollBar();
            panel_controls = new Panel();
            button_save = new Button();
            button_cancel = new Button();
            button_help = new Button();
            button_removeBar = new Button();
            button_addBar = new Button();
            checkBox_preHear = new CheckBox();
            button_hear = new Button();
            numericUpDown_bpm = new NumericUpDown();
            label_bpm = new Label();
            numericUpDown_resolution = new NumericUpDown();
            label_resolution = new Label();
            timer_previewCaret = new System.Windows.Forms.Timer(components);
            timer_pitchGestureRelease = new System.Windows.Forms.Timer(components);
            timer_pitchTooltip = new System.Windows.Forms.Timer(components);
            toolTip_pattern = new ToolTip(components);
            toolTip_pattern.InitialDelay = 360;
            ((System.ComponentModel.ISupportInitialize)pictureBox_pattern).BeginInit();
            panel_pattern.SuspendLayout();
            panel_controls.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)numericUpDown_bpm).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDown_resolution).BeginInit();
            SuspendLayout();
            // 
            // pictureBox_pattern
            // 
            pictureBox_pattern.BackColor = Color.FromArgb(28, 30, 34);
            pictureBox_pattern.Dock = DockStyle.Fill;
            pictureBox_pattern.Location = new Point(0, 0);
            pictureBox_pattern.Name = "pictureBox_pattern";
            pictureBox_pattern.Size = new Size(884, 461);
            pictureBox_pattern.TabIndex = 0;
            pictureBox_pattern.TabStop = true;
            pictureBox_pattern.Paint += pictureBox_pattern_Paint;
            pictureBox_pattern.MouseDown += pictureBox_pattern_MouseDown;
            pictureBox_pattern.MouseMove += pictureBox_pattern_MouseMove;
            pictureBox_pattern.MouseUp += pictureBox_pattern_MouseUp;
            pictureBox_pattern.MouseEnter += pictureBox_pattern_MouseEnter;
            pictureBox_pattern.MouseLeave += pictureBox_pattern_MouseLeave;
            pictureBox_pattern.MouseWheel += pictureBox_pattern_MouseWheel;
            pictureBox_pattern.Resize += pictureBox_pattern_Resize;
            //
            // panel_pattern
            //
            panel_pattern.Controls.Add(pictureBox_pattern);
            panel_pattern.Controls.Add(hScrollBar_pattern);
            panel_pattern.Dock = DockStyle.Fill;
            panel_pattern.Location = new Point(0, 0);
            panel_pattern.Name = "panel_pattern";
            panel_pattern.Size = new Size(884, 461);
            panel_pattern.TabIndex = 2;
            panel_pattern.MouseWheel += pictureBox_pattern_MouseWheel;
            //
            // hScrollBar_pattern
            //
            hScrollBar_pattern.Dock = DockStyle.Bottom;
            hScrollBar_pattern.Height = SystemInformation.HorizontalScrollBarHeight;
            hScrollBar_pattern.Name = "hScrollBar_pattern";
            hScrollBar_pattern.Visible = false;
            hScrollBar_pattern.ValueChanged += hScrollBar_pattern_ValueChanged;
            hScrollBar_pattern.MouseWheel += pictureBox_pattern_MouseWheel;
            // 
            // panel_controls
            // 
            panel_controls.Controls.Add(button_save);
            panel_controls.Controls.Add(button_cancel);
            panel_controls.Controls.Add(button_help);
            panel_controls.Controls.Add(button_removeBar);
            panel_controls.Controls.Add(button_addBar);
            panel_controls.Controls.Add(checkBox_preHear);
            panel_controls.Controls.Add(button_hear);
            panel_controls.Controls.Add(numericUpDown_bpm);
            panel_controls.Controls.Add(label_bpm);
            panel_controls.Controls.Add(numericUpDown_resolution);
            panel_controls.Controls.Add(label_resolution);
            panel_controls.Dock = DockStyle.Bottom;
            panel_controls.Location = new Point(0, 461);
            panel_controls.Name = "panel_controls";
            panel_controls.Size = new Size(884, 52);
            panel_controls.TabIndex = 1;
            // 
            // button_save
            // 
            button_save.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            button_save.Location = new Point(797, 14);
            button_save.Name = "button_save";
            button_save.Size = new Size(75, 25);
            button_save.TabIndex = 4;
            button_save.Text = "Save";
            button_save.UseVisualStyleBackColor = true;
            button_save.Click += button_save_Click;
            //
            // button_help
            //
            button_help.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            button_help.Location = new Point(626, 14);
            button_help.Name = "button_help";
            button_help.Size = new Size(72, 25);
            button_help.TabIndex = 9;
            button_help.Text = "Help";
            toolTip_pattern.SetToolTip(button_help, "Show pattern editor gestures and shortcuts.");
            button_help.UseVisualStyleBackColor = true;
            button_help.Click += button_help_Click;
            //
            // button_cancel
            //
            button_cancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            button_cancel.DialogResult = DialogResult.Cancel;
            button_cancel.Location = new Point(713, 14);
            button_cancel.Name = "button_cancel";
            button_cancel.Size = new Size(75, 25);
            button_cancel.TabIndex = 3;
            button_cancel.Text = "Cancel";
            button_cancel.UseVisualStyleBackColor = true;
            // 
            // button_removeBar
            // 
            button_removeBar.Location = new Point(494, 14);
            button_removeBar.Name = "button_removeBar";
            button_removeBar.Size = new Size(26, 25);
            button_removeBar.TabIndex = 8;
            button_removeBar.Text = "-";
            toolTip_pattern.SetToolTip(button_removeBar, "Remove the last bar");
            button_removeBar.UseVisualStyleBackColor = true;
            button_removeBar.Click += button_removeBar_Click;
            // 
            // button_addBar
            // 
            button_addBar.Location = new Point(462, 14);
            button_addBar.Name = "button_addBar";
            button_addBar.Size = new Size(26, 25);
            button_addBar.TabIndex = 7;
            button_addBar.Text = "+";
            toolTip_pattern.SetToolTip(button_addBar, "Append one bar");
            button_addBar.UseVisualStyleBackColor = true;
            button_addBar.Click += button_addBar_Click;
            // 
            // checkBox_preHear
            // 
            checkBox_preHear.AutoSize = true;
            checkBox_preHear.Checked = true;
            checkBox_preHear.CheckState = CheckState.Checked;
            checkBox_preHear.Location = new Point(225, 18);
            checkBox_preHear.Name = "checkBox_preHear";
            checkBox_preHear.Size = new Size(71, 19);
            checkBox_preHear.TabIndex = 3;
            checkBox_preHear.Text = "Pre-hear";
            toolTip_pattern.SetToolTip(checkBox_preHear, "Play each newly placed note once.");
            checkBox_preHear.UseVisualStyleBackColor = true;
            // 
            // button_hear
            // 
            button_hear.Location = new Point(140, 14);
            button_hear.Name = "button_hear";
            button_hear.Size = new Size(75, 25);
            button_hear.TabIndex = 2;
            button_hear.Text = "Hear";
            button_hear.UseVisualStyleBackColor = true;
            button_hear.Click += button_hear_Click;
            // 
            // numericUpDown_bpm
            // 
            numericUpDown_bpm.DecimalPlaces = 1;
            numericUpDown_bpm.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
            numericUpDown_bpm.Location = new Point(52, 15);
            numericUpDown_bpm.Maximum = new decimal(new int[] { 300, 0, 0, 0 });
            numericUpDown_bpm.Minimum = new decimal(new int[] { 10, 0, 0, 0 });
            numericUpDown_bpm.Name = "numericUpDown_bpm";
            numericUpDown_bpm.Size = new Size(72, 23);
            numericUpDown_bpm.TabIndex = 1;
            numericUpDown_bpm.Value = new decimal(new int[] { 120, 0, 0, 0 });
            numericUpDown_bpm.ValueChanged += numericUpDown_bpm_ValueChanged;
            // 
            // label_bpm
            // 
            label_bpm.AutoSize = true;
            label_bpm.Location = new Point(14, 19);
            label_bpm.Name = "label_bpm";
            label_bpm.Size = new Size(32, 15);
            label_bpm.TabIndex = 0;
            label_bpm.Text = "BPM";
            // 
            // numericUpDown_resolution
            // 
            numericUpDown_resolution.Location = new Point(390, 15);
            numericUpDown_resolution.Maximum = new decimal(new int[] { 256, 0, 0, 0 });
            numericUpDown_resolution.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            numericUpDown_resolution.Name = "numericUpDown_resolution";
            numericUpDown_resolution.Size = new Size(58, 23);
            numericUpDown_resolution.TabIndex = 6;
            numericUpDown_resolution.Value = new decimal(new int[] { 4, 0, 0, 0 });
            numericUpDown_resolution.ValueChanged += numericUpDown_resolution_ValueChanged;
            // 
            // label_resolution
            // 
            label_resolution.AutoSize = true;
            label_resolution.Location = new Point(322, 19);
            label_resolution.Name = "label_resolution";
            label_resolution.Size = new Size(63, 15);
            label_resolution.TabIndex = 5;
            label_resolution.Text = "Steps / bar";
            // 
            // timer_previewCaret
            // 
            timer_previewCaret.Interval = 30;
            timer_previewCaret.Tick += timer_previewCaret_Tick;
            timer_pitchGestureRelease.Interval = 30;
            timer_pitchGestureRelease.Tick += timer_pitchGestureRelease_Tick;
            timer_pitchTooltip.Interval = 360;
            timer_pitchTooltip.Tick += timer_pitchTooltip_Tick;
            // 
            // BreakbeatPatternEditorDialog
            // 
            AcceptButton = button_save;
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            CancelButton = button_cancel;
            ClientSize = new Size(884, 513);
            Controls.Add(panel_pattern);
            Controls.Add(panel_controls);
            MinimumSize = new Size(840, 360);
            Name = "BreakbeatPatternEditorDialog";
            StartPosition = FormStartPosition.CenterParent;
            Text = "Breakbeat Pattern Editor";
            ((System.ComponentModel.ISupportInitialize)pictureBox_pattern).EndInit();
            panel_pattern.ResumeLayout(false);
            panel_controls.ResumeLayout(false);
            panel_controls.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)numericUpDown_bpm).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDown_resolution).EndInit();
            ResumeLayout(false);
        }

        private BufferedPictureBox pictureBox_pattern;
        private Panel panel_pattern;
        private HScrollBar hScrollBar_pattern;
        private Panel panel_controls;
        private Label label_bpm;
        private NumericUpDown numericUpDown_bpm;
        private Label label_resolution;
        private NumericUpDown numericUpDown_resolution;
        private Button button_hear;
        private CheckBox checkBox_preHear;
        private Button button_addBar;
        private Button button_removeBar;
        private Button button_cancel;
        private Button button_help;
        private Button button_save;
        private System.Windows.Forms.Timer timer_previewCaret;
        private System.Windows.Forms.Timer timer_pitchGestureRelease;
        private System.Windows.Forms.Timer timer_pitchTooltip;
        private ToolTip toolTip_pattern;
    }
}