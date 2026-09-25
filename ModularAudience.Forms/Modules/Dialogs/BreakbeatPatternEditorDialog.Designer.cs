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

            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.pictureBox_pattern = new BufferedPictureBox();
            this.panel_controls = new Panel();
            this.label_bpm = new Label();
            this.numericUpDown_bpm = new NumericUpDown();
            this.label_resolution = new Label();
            this.numericUpDown_resolution = new NumericUpDown();
            this.button_hear = new Button();
            this.checkBox_preHear = new CheckBox();
            this.button_addBar = new Button();
            this.button_removeBar = new Button();
            this.button_cancel = new Button();
            this.button_save = new Button();
            this.timer_previewCaret = new System.Windows.Forms.Timer(this.components);
            this.toolTip_pattern = new ToolTip(this.components);
            ((System.ComponentModel.ISupportInitialize)this.pictureBox_pattern).BeginInit();
            this.panel_controls.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)this.numericUpDown_bpm).BeginInit();
            ((System.ComponentModel.ISupportInitialize)this.numericUpDown_resolution).BeginInit();
            this.SuspendLayout();
            // 
            // pictureBox_pattern
            // 
            this.pictureBox_pattern.BackColor = Color.FromArgb(28, 30, 34);
            this.pictureBox_pattern.Dock = DockStyle.Fill;
            this.pictureBox_pattern.Location = new Point(0, 0);
            this.pictureBox_pattern.Name = "pictureBox_pattern";
            this.pictureBox_pattern.Size = new Size(884, 461);
            this.pictureBox_pattern.TabIndex = 0;
            this.pictureBox_pattern.TabStop = false;
            this.toolTip_pattern.SetToolTip(this.pictureBox_pattern, "Left-click or drag to add hits. Right-click or drag to delete hits.");
            this.pictureBox_pattern.Paint += this.pictureBox_pattern_Paint;
            this.pictureBox_pattern.MouseDown += this.pictureBox_pattern_MouseDown;
            this.pictureBox_pattern.MouseMove += this.pictureBox_pattern_MouseMove;
            this.pictureBox_pattern.MouseUp += this.pictureBox_pattern_MouseUp;
            this.pictureBox_pattern.Resize += this.pictureBox_pattern_Resize;
            // 
            // panel_controls
            // 
            this.panel_controls.Controls.Add(this.button_save);
            this.panel_controls.Controls.Add(this.button_cancel);
            this.panel_controls.Controls.Add(this.button_removeBar);
            this.panel_controls.Controls.Add(this.button_addBar);
            this.panel_controls.Controls.Add(this.checkBox_preHear);
            this.panel_controls.Controls.Add(this.button_hear);
            this.panel_controls.Controls.Add(this.numericUpDown_bpm);
            this.panel_controls.Controls.Add(this.label_bpm);
            this.panel_controls.Controls.Add(this.numericUpDown_resolution);
            this.panel_controls.Controls.Add(this.label_resolution);
            this.panel_controls.Dock = DockStyle.Bottom;
            this.panel_controls.Location = new Point(0, 461);
            this.panel_controls.Name = "panel_controls";
            this.panel_controls.Size = new Size(884, 52);
            this.panel_controls.TabIndex = 1;
            // 
            // label_bpm
            // 
            this.label_bpm.AutoSize = true;
            this.label_bpm.Location = new Point(14, 19);
            this.label_bpm.Name = "label_bpm";
            this.label_bpm.Size = new Size(32, 15);
            this.label_bpm.TabIndex = 0;
            this.label_bpm.Text = "BPM";
            // 
            // numericUpDown_bpm
            // 
            this.numericUpDown_bpm.DecimalPlaces = 1;
            this.numericUpDown_bpm.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
            this.numericUpDown_bpm.Location = new Point(52, 15);
            this.numericUpDown_bpm.Maximum = new decimal(new int[] { 300, 0, 0, 0 });
            this.numericUpDown_bpm.Minimum = new decimal(new int[] { 10, 0, 0, 0 });
            this.numericUpDown_bpm.Name = "numericUpDown_bpm";
            this.numericUpDown_bpm.Size = new Size(72, 23);
            this.numericUpDown_bpm.TabIndex = 1;
            this.numericUpDown_bpm.Value = new decimal(new int[] { 120, 0, 0, 0 });
            this.numericUpDown_bpm.ValueChanged += this.numericUpDown_bpm_ValueChanged;
            // 
            // label_resolution
            // 
            this.label_resolution.AutoSize = true;
            this.label_resolution.Location = new Point(322, 19);
            this.label_resolution.Name = "label_resolution";
            this.label_resolution.Size = new Size(62, 15);
            this.label_resolution.TabIndex = 5;
            this.label_resolution.Text = "Steps / bar";
            // 
            // numericUpDown_resolution
            // 
            this.numericUpDown_resolution.Location = new Point(390, 15);
            this.numericUpDown_resolution.Maximum = new decimal(new int[] { 64, 0, 0, 0 });
            this.numericUpDown_resolution.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numericUpDown_resolution.Name = "numericUpDown_resolution";
            this.numericUpDown_resolution.Size = new Size(58, 23);
            this.numericUpDown_resolution.TabIndex = 6;
            this.numericUpDown_resolution.Value = new decimal(new int[] { 4, 0, 0, 0 });
            this.numericUpDown_resolution.ValueChanged += this.numericUpDown_resolution_ValueChanged;
            // 
            // button_hear
            // 
            this.button_hear.Location = new Point(140, 14);
            this.button_hear.Name = "button_hear";
            this.button_hear.Size = new Size(75, 25);
            this.button_hear.TabIndex = 2;
            this.button_hear.Text = "Hear";
            this.button_hear.UseVisualStyleBackColor = true;
            this.button_hear.Click += this.button_hear_Click;
            // 
            // checkBox_preHear
            // 
            this.checkBox_preHear.AutoSize = true;
            this.checkBox_preHear.Location = new Point(225, 18);
            this.checkBox_preHear.Name = "checkBox_preHear";
            this.checkBox_preHear.Size = new Size(74, 19);
            this.checkBox_preHear.TabIndex = 3;
            this.checkBox_preHear.Text = "Pre-hear";
            this.checkBox_preHear.UseVisualStyleBackColor = true;
            this.toolTip_pattern.SetToolTip(this.checkBox_preHear, "Play each newly placed note once.");
            // 
            // button_addBar
            // 
            this.button_addBar.Location = new Point(462, 14);
            this.button_addBar.Name = "button_addBar";
            this.button_addBar.Size = new Size(26, 25);
            this.button_addBar.TabIndex = 7;
            this.button_addBar.Text = "+";
            this.button_addBar.UseVisualStyleBackColor = true;
            this.button_addBar.Click += this.button_addBar_Click;
            this.toolTip_pattern.SetToolTip(this.button_addBar, "Append one bar");
            // 
            // button_removeBar
            // 
            this.button_removeBar.Location = new Point(494, 14);
            this.button_removeBar.Name = "button_removeBar";
            this.button_removeBar.Size = new Size(26, 25);
            this.button_removeBar.TabIndex = 8;
            this.button_removeBar.Text = "-";
            this.button_removeBar.UseVisualStyleBackColor = true;
            this.button_removeBar.Click += this.button_removeBar_Click;
            this.toolTip_pattern.SetToolTip(this.button_removeBar, "Remove the last bar");
            // 
            // button_cancel
            // 
            this.button_cancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            this.button_cancel.DialogResult = DialogResult.Cancel;
            this.button_cancel.Location = new Point(713, 14);
            this.button_cancel.Name = "button_cancel";
            this.button_cancel.Size = new Size(75, 25);
            this.button_cancel.TabIndex = 3;
            this.button_cancel.Text = "Cancel";
            this.button_cancel.UseVisualStyleBackColor = true;
            // 
            // button_save
            // 
            this.button_save.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            this.button_save.Location = new Point(797, 14);
            this.button_save.Name = "button_save";
            this.button_save.Size = new Size(75, 25);
            this.button_save.TabIndex = 4;
            this.button_save.Text = "Save";
            this.button_save.UseVisualStyleBackColor = true;
            this.button_save.Click += this.button_save_Click;
            // 
            // timer_previewCaret
            // 
            this.timer_previewCaret.Interval = 30;
            this.timer_previewCaret.Tick += this.timer_previewCaret_Tick;
            // 
            // BreakbeatPatternEditorDialog
            // 
            this.AcceptButton = this.button_save;
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(884, 513);
            this.Controls.Add(this.pictureBox_pattern);
            this.Controls.Add(this.panel_controls);
            this.MinimumSize = new Size(760, 360);
            this.Name = "BreakbeatPatternEditorDialog";
            this.StartPosition = FormStartPosition.CenterParent;
            this.Text = "Breakbeat Pattern Editor";
            this.CancelButton = this.button_cancel;
            ((System.ComponentModel.ISupportInitialize)this.pictureBox_pattern).EndInit();
            this.panel_controls.ResumeLayout(false);
            this.panel_controls.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)this.numericUpDown_bpm).EndInit();
            ((System.ComponentModel.ISupportInitialize)this.numericUpDown_resolution).EndInit();
            this.ResumeLayout(false);
        }

        private BufferedPictureBox pictureBox_pattern;
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
        private Button button_save;
        private System.Windows.Forms.Timer timer_previewCaret;
        private ToolTip toolTip_pattern;
    }
}