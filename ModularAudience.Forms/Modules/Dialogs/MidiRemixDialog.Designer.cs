namespace ModularAudience.Forms.Modules.Dialogs
{
    partial class MidiRemixDialog
    {
        private System.ComponentModel.IContainer components = null;
        private Label label_track;
        private ComboBox comboBox_track;
        private Label label_denoise;
        private NumericUpDown numericUpDown_denoise;
        private Label label_frequency;
        private NumericUpDown numericUpDown_frequency;
        private Label label_tempo;
        private NumericUpDown numericUpDown_tempo;
        private Label label_derivation;
        private NumericUpDown numericUpDown_derivation;
        private Label label_rearrangement;
        private NumericUpDown numericUpDown_rearrangement;
        private Label label_minLength;
        private NumericUpDown numericUpDown_minLength;
        private Label label_maxLength;
        private NumericUpDown numericUpDown_maxLength;
        private Label label_poolSize;
        private NumericUpDown numericUpDown_poolSize;
        private Button button_ok;
        private Button button_cancel;

        protected override void Dispose(bool disposing)
        {
            if (disposing) this.components?.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.label_track = new Label();
            this.comboBox_track = new ComboBox();
            this.label_denoise = new Label();
            this.numericUpDown_denoise = CreateDecimalControl();
            this.label_frequency = new Label();
            this.numericUpDown_frequency = CreateDecimalControl();
            this.label_tempo = new Label();
            this.numericUpDown_tempo = CreateDecimalControl();
            this.label_derivation = new Label();
            this.numericUpDown_derivation = CreateDecimalControl();
            this.label_rearrangement = new Label();
            this.numericUpDown_rearrangement = CreateDecimalControl();
            this.label_minLength = new Label();
            this.numericUpDown_minLength = CreateIntegerControl();
            this.label_maxLength = new Label();
            this.numericUpDown_maxLength = CreateIntegerControl();
            this.label_poolSize = new Label();
            this.numericUpDown_poolSize = CreateIntegerControl();
            this.button_ok = new Button();
            this.button_cancel = new Button();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_denoise).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_frequency).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_tempo).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_derivation).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_rearrangement).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_minLength).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_maxLength).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_poolSize).BeginInit();
            this.SuspendLayout();
            Label[] labels = [this.label_track, this.label_denoise, this.label_frequency, this.label_tempo, this.label_derivation, this.label_rearrangement, this.label_minLength, this.label_maxLength, this.label_poolSize];
            string[] texts = ["Track ID", "Denoise factor", "Frequency shift", "Tempo shift", "Pattern derivation", "Pattern rearrangement", "Pattern minimum length", "Pattern maximum length", "Derived pattern pool size"];
            for (int i = 0; i < labels.Length; i++)
            {
                labels[i].AutoSize = true;
                labels[i].Location = new Point(12, 15 + i * 34);
                labels[i].Text = texts[i];
            }
            this.comboBox_track.DropDownStyle = ComboBoxStyle.DropDownList;
            this.comboBox_track.Location = new Point(210, 12);
            this.comboBox_track.Size = new Size(170, 23);
            NumericUpDown[] decimals = [this.numericUpDown_denoise, this.numericUpDown_frequency, this.numericUpDown_tempo, this.numericUpDown_derivation, this.numericUpDown_rearrangement];
            for (int i = 0; i < decimals.Length; i++)
            {
                decimals[i].Location = new Point(210, 46 + i * 34);
                decimals[i].Size = new Size(90, 23);
                decimals[i].DecimalPlaces = 2;
                decimals[i].Increment = 0.05M;
                decimals[i].Minimum = -4;
                decimals[i].Maximum = 4;
            }
            this.numericUpDown_denoise.Minimum = 0; this.numericUpDown_denoise.Maximum = 1;
            this.numericUpDown_derivation.Minimum = 0; this.numericUpDown_derivation.Maximum = 1;
            this.numericUpDown_rearrangement.Minimum = 0; this.numericUpDown_rearrangement.Maximum = 1;
            NumericUpDown[] integers = [this.numericUpDown_minLength, this.numericUpDown_maxLength, this.numericUpDown_poolSize];
            for (int i = 0; i < integers.Length; i++)
            {
                integers[i].Location = new Point(210, 216 + i * 34);
                integers[i].Size = new Size(90, 23);
                integers[i].Minimum = 1;
                integers[i].Maximum = 256;
            }
            this.button_ok.Text = "OK"; this.button_ok.DialogResult = DialogResult.OK; this.button_ok.Location = new Point(224, 332); this.button_ok.Click += this.button_ok_Click;
            this.button_cancel.Text = "Cancel"; this.button_cancel.DialogResult = DialogResult.Cancel; this.button_cancel.Location = new Point(305, 332);
            this.AcceptButton = this.button_ok; this.CancelButton = this.button_cancel;
            this.ClientSize = new Size(400, 375);
            this.Controls.AddRange([.. labels, this.comboBox_track, .. decimals, .. integers, this.button_ok, this.button_cancel]);
            this.Text = "MIDI Remix Settings";
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_denoise).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_frequency).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_tempo).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_derivation).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_rearrangement).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_minLength).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_maxLength).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_poolSize).EndInit();
            this.ResumeLayout(false); this.PerformLayout();
        }

        private static NumericUpDown CreateDecimalControl() => new() { DecimalPlaces = 2 };
        private static NumericUpDown CreateIntegerControl() => new();
    }
}