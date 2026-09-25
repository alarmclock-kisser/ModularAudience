namespace ModularAudience.Forms.Modules.Dialogs
{
    partial class BreakbeatTrackSettingsDialog
    {
        private System.ComponentModel.IContainer components = null!;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.components?.Dispose();
            }

            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.label_defaults = new Label();
            this.label_volume = new Label();
            this.numericUpDown_volume = new NumericUpDown();
            this.label_pitch = new Label();
            this.numericUpDown_pitch = new NumericUpDown();
            this.label_defaultPlaybackMode = new Label();
            this.comboBox_defaultPlaybackMode = new ComboBox();
            this.button_reset = new Button();
            this.button_apply = new Button();
            this.button_cancel = new Button();
            ((System.ComponentModel.ISupportInitialize)this.numericUpDown_volume).BeginInit();
            ((System.ComponentModel.ISupportInitialize)this.numericUpDown_pitch).BeginInit();
            this.SuspendLayout();
            // 
            // label_defaults
            // 
            this.label_defaults.AutoSize = true;
            this.label_defaults.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            this.label_defaults.Location = new Point(16, 16);
            this.label_defaults.Name = "label_defaults";
            this.label_defaults.Size = new Size(124, 15);
            this.label_defaults.TabIndex = 0;
            this.label_defaults.Text = "Defaults for new notes";
            // 
            // label_volume
            // 
            this.label_volume.AutoSize = true;
            this.label_volume.Location = new Point(18, 52);
            this.label_volume.Name = "label_volume";
            this.label_volume.Size = new Size(74, 15);
            this.label_volume.TabIndex = 1;
            this.label_volume.Text = "Volume (%)";
            // 
            // numericUpDown_volume
            // 
            this.numericUpDown_volume.Increment = 5m;
            this.numericUpDown_volume.Location = new Point(190, 49);
            this.numericUpDown_volume.Maximum = 250m;
            this.numericUpDown_volume.Name = "numericUpDown_volume";
            this.numericUpDown_volume.Size = new Size(132, 23);
            this.numericUpDown_volume.TabIndex = 2;
            this.numericUpDown_volume.Value = 100m;
            // 
            // label_pitch
            // 
            this.label_pitch.AutoSize = true;
            this.label_pitch.Location = new Point(18, 87);
            this.label_pitch.Name = "label_pitch";
            this.label_pitch.Size = new Size(103, 15);
            this.label_pitch.TabIndex = 3;
            this.label_pitch.Text = "Pitch (semitones)";
            // 
            // numericUpDown_pitch
            // 
            this.numericUpDown_pitch.DecimalPlaces = 2;
            this.numericUpDown_pitch.Increment = 0.25m;
            this.numericUpDown_pitch.Location = new Point(190, 84);
            this.numericUpDown_pitch.Maximum = 24m;
            this.numericUpDown_pitch.Minimum = -24m;
            this.numericUpDown_pitch.Name = "numericUpDown_pitch";
            this.numericUpDown_pitch.Size = new Size(132, 23);
            this.numericUpDown_pitch.TabIndex = 4;
            // 
            // label_defaultPlaybackMode
            // 
            this.label_defaultPlaybackMode.AutoSize = true;
            this.label_defaultPlaybackMode.Location = new Point(18, 122);
            this.label_defaultPlaybackMode.Name = "label_defaultPlaybackMode";
            this.label_defaultPlaybackMode.Size = new Size(77, 15);
            this.label_defaultPlaybackMode.TabIndex = 5;
            this.label_defaultPlaybackMode.Text = "Resize mode";
            // 
            // comboBox_defaultPlaybackMode
            // 
            this.comboBox_defaultPlaybackMode.DropDownStyle = ComboBoxStyle.DropDownList;
            this.comboBox_defaultPlaybackMode.FormattingEnabled = true;
            this.comboBox_defaultPlaybackMode.Location = new Point(190, 119);
            this.comboBox_defaultPlaybackMode.Name = "comboBox_defaultPlaybackMode";
            this.comboBox_defaultPlaybackMode.Size = new Size(132, 23);
            this.comboBox_defaultPlaybackMode.TabIndex = 6;
            // 
            // button_reset
            //
            this.button_reset.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            this.button_reset.Location = new Point(12, 165);
            this.button_reset.Name = "button_reset";
            this.button_reset.Size = new Size(120, 25);
            this.button_reset.TabIndex = 7;
            this.button_reset.Text = "Reset";
            this.button_reset.UseVisualStyleBackColor = true;
            this.button_reset.Click += this.button_reset_Click;
            // 
            // button_apply
            //
            this.button_apply.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            this.button_apply.DialogResult = DialogResult.OK;
            this.button_apply.Location = new Point(166, 165);
            this.button_apply.Name = "button_apply";
            this.button_apply.Size = new Size(75, 25);
            this.button_apply.TabIndex = 8;
            this.button_apply.Text = "Apply";
            this.button_apply.UseVisualStyleBackColor = true;
            // 
            // button_cancel
            // 
            this.button_cancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            this.button_cancel.DialogResult = DialogResult.Cancel;
            this.button_cancel.Location = new Point(247, 165);
            this.button_cancel.Name = "button_cancel";
            this.button_cancel.Size = new Size(75, 25);
            this.button_cancel.TabIndex = 9;
            this.button_cancel.Text = "Cancel";
            this.button_cancel.UseVisualStyleBackColor = true;
            // 
            // BreakbeatTrackSettingsDialog
            // 
            this.AcceptButton = this.button_apply;
            this.CancelButton = this.button_cancel;
            this.ClientSize = new Size(340, 206);
            this.Controls.Add(this.button_cancel);
            this.Controls.Add(this.button_apply);
            this.Controls.Add(this.button_reset);
            this.Controls.Add(this.comboBox_defaultPlaybackMode);
            this.Controls.Add(this.label_defaultPlaybackMode);
            this.Controls.Add(this.numericUpDown_pitch);
            this.Controls.Add(this.label_pitch);
            this.Controls.Add(this.numericUpDown_volume);
            this.Controls.Add(this.label_volume);
            this.Controls.Add(this.label_defaults);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "BreakbeatTrackSettingsDialog";
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.Text = "Track Settings";
            ((System.ComponentModel.ISupportInitialize)this.numericUpDown_volume).EndInit();
            ((System.ComponentModel.ISupportInitialize)this.numericUpDown_pitch).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private Label label_defaults;
        private Label label_volume;
        private NumericUpDown numericUpDown_volume;
        private Label label_pitch;
        private NumericUpDown numericUpDown_pitch;
        private Label label_defaultPlaybackMode;
        private ComboBox comboBox_defaultPlaybackMode;
        private Button button_reset;
        private Button button_apply;
        private Button button_cancel;
    }
}