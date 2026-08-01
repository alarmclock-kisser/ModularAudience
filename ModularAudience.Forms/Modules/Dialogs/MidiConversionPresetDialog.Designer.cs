namespace ModularAudience.Forms.Modules.Dialogs
{
    partial class MidiConversionPresetDialog
    {
        private System.ComponentModel.IContainer components = null;
        private Label label_title;
        private Label label_description;
        private ComboBox comboBox_preset;
        private Button button_ok;
        private Button button_cancel;

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
            this.label_title = new Label();
            this.label_description = new Label();
            this.comboBox_preset = new ComboBox();
            this.button_ok = new Button();
            this.button_cancel = new Button();
            this.SuspendLayout();
            //
            // label_title
            //
            this.label_title.AutoSize = true;
            this.label_title.Location = new Point(12, 12);
            this.label_title.Name = "label_title";
            this.label_title.Size = new Size(125, 15);
            this.label_title.TabIndex = 0;
            this.label_title.Text = "MIDI conversion preset";
            //
            // label_description
            //
            this.label_description.AutoSize = true;
            this.label_description.Location = new Point(12, 39);
            this.label_description.Name = "label_description";
            this.label_description.Size = new Size(318, 30);
            this.label_description.TabIndex = 1;
            this.label_description.Text = "Synth: clear monophonic tones\r\nGuitar: suppresses pluck-related pitch artifacts";
            //
            // comboBox_preset
            //
            this.comboBox_preset.DropDownStyle = ComboBoxStyle.DropDownList;
            this.comboBox_preset.FormattingEnabled = true;
            this.comboBox_preset.Items.AddRange(new object[] { "Synth", "Guitar" });
            this.comboBox_preset.Location = new Point(12, 82);
            this.comboBox_preset.Name = "comboBox_preset";
            this.comboBox_preset.Size = new Size(318, 23);
            this.comboBox_preset.TabIndex = 2;
            //
            // button_ok
            //
            this.button_ok.DialogResult = DialogResult.OK;
            this.button_ok.Location = new Point(174, 121);
            this.button_ok.Name = "button_ok";
            this.button_ok.Size = new Size(75, 23);
            this.button_ok.TabIndex = 3;
            this.button_ok.Text = "Convert";
            this.button_ok.UseVisualStyleBackColor = true;
            //
            // button_cancel
            //
            this.button_cancel.DialogResult = DialogResult.Cancel;
            this.button_cancel.Location = new Point(255, 121);
            this.button_cancel.Name = "button_cancel";
            this.button_cancel.Size = new Size(75, 23);
            this.button_cancel.TabIndex = 4;
            this.button_cancel.Text = "Cancel";
            this.button_cancel.UseVisualStyleBackColor = true;
            //
            // MidiConversionPresetDialog
            //
            this.AcceptButton = this.button_ok;
            this.CancelButton = this.button_cancel;
            this.ClientSize = new Size(342, 156);
            this.Controls.Add(this.button_cancel);
            this.Controls.Add(this.button_ok);
            this.Controls.Add(this.comboBox_preset);
            this.Controls.Add(this.label_description);
            this.Controls.Add(this.label_title);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "MidiConversionPresetDialog";
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.Text = "Convert audio to MIDI";
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}
