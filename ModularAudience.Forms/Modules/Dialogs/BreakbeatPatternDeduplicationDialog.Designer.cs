namespace ModularAudience.Forms.Modules.Dialogs
{
    partial class BreakbeatPatternDeduplicationDialog
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (this.components != null))
            {
                this.components.Dispose();
            }

            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.label_description = new Label();
            this.label_candidateTracks = new Label();
            this.label_similarityThreshold = new Label();
            this.numericUpDown_similarityThreshold = new NumericUpDown();
            this.label_thresholdHint = new Label();
            this.button_deduplicate = new Button();
            this.button_cancel = new Button();
            ((System.ComponentModel.ISupportInitialize)this.numericUpDown_similarityThreshold).BeginInit();
            this.SuspendLayout();
            //
            // label_description
            //
            this.label_description.AutoSize = false;
            this.label_description.Location = new Point(14, 14);
            this.label_description.Name = "label_description";
            this.label_description.Size = new Size(452, 60);
            this.label_description.TabIndex = 0;
            this.label_description.TextAlign = ContentAlignment.MiddleLeft;
            this.label_description.Text =
                "Similar sample tracks are merged into one lane and their notes are combined. " +
                "Exact duplicate note events are removed. Instrument tracks, Auto-Reroll tracks, " +
                "and tracks with different playback settings are not merged.";
            //
            // label_candidateTracks
            //
            this.label_candidateTracks.AutoSize = true;
            this.label_candidateTracks.Location = new Point(14, 82);
            this.label_candidateTracks.Name = "label_candidateTracks";
            this.label_candidateTracks.Size = new Size(152, 15);
            this.label_candidateTracks.TabIndex = 1;
            this.label_candidateTracks.Text = "Compatible sample tracks";
            //
            // label_similarityThreshold
            //
            this.label_similarityThreshold.AutoSize = true;
            this.label_similarityThreshold.Location = new Point(14, 115);
            this.label_similarityThreshold.Name = "label_similarityThreshold";
            this.label_similarityThreshold.Size = new Size(116, 15);
            this.label_similarityThreshold.TabIndex = 2;
            this.label_similarityThreshold.Text = "Similarity threshold";
            //
            // numericUpDown_similarityThreshold
            //
            this.numericUpDown_similarityThreshold.DecimalPlaces = 3;
            this.numericUpDown_similarityThreshold.Increment = 0.001m;
            this.numericUpDown_similarityThreshold.Location = new Point(140, 111);
            this.numericUpDown_similarityThreshold.Maximum = 0.999m;
            this.numericUpDown_similarityThreshold.Minimum = 0.800m;
            this.numericUpDown_similarityThreshold.Name = "numericUpDown_similarityThreshold";
            this.numericUpDown_similarityThreshold.Size = new Size(82, 23);
            this.numericUpDown_similarityThreshold.TabIndex = 3;
            this.numericUpDown_similarityThreshold.Value = 0.940m;
            //
            // label_thresholdHint
            //
            this.label_thresholdHint.AutoSize = true;
            this.label_thresholdHint.Location = new Point(14, 145);
            this.label_thresholdHint.Name = "label_thresholdHint";
            this.label_thresholdHint.Size = new Size(287, 15);
            this.label_thresholdHint.TabIndex = 4;
            this.label_thresholdHint.Text = "Higher values only merge more similar audio.";
            //
            // button_deduplicate
            //
            this.button_deduplicate.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            this.button_deduplicate.DialogResult = DialogResult.OK;
            this.button_deduplicate.Location = new Point(292, 184);
            this.button_deduplicate.Name = "button_deduplicate";
            this.button_deduplicate.Size = new Size(84, 26);
            this.button_deduplicate.TabIndex = 5;
            this.button_deduplicate.Text = "Dedup";
            this.button_deduplicate.UseVisualStyleBackColor = true;
            //
            // button_cancel
            //
            this.button_cancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            this.button_cancel.DialogResult = DialogResult.Cancel;
            this.button_cancel.Location = new Point(382, 184);
            this.button_cancel.Name = "button_cancel";
            this.button_cancel.Size = new Size(84, 26);
            this.button_cancel.TabIndex = 6;
            this.button_cancel.Text = "Cancel";
            this.button_cancel.UseVisualStyleBackColor = true;
            //
            // BreakbeatPatternDeduplicationDialog
            //
            this.AcceptButton = this.button_deduplicate;
            this.CancelButton = this.button_cancel;
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(480, 224);
            this.Controls.Add(this.button_cancel);
            this.Controls.Add(this.button_deduplicate);
            this.Controls.Add(this.label_thresholdHint);
            this.Controls.Add(this.numericUpDown_similarityThreshold);
            this.Controls.Add(this.label_similarityThreshold);
            this.Controls.Add(this.label_candidateTracks);
            this.Controls.Add(this.label_description);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "BreakbeatPatternDeduplicationDialog";
            this.StartPosition = FormStartPosition.CenterParent;
            this.Text = "Deduplicate Pattern Tracks";
            ((System.ComponentModel.ISupportInitialize)this.numericUpDown_similarityThreshold).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private Label label_description = null!;
        private Label label_candidateTracks = null!;
        private Label label_similarityThreshold = null!;
        private NumericUpDown numericUpDown_similarityThreshold = null!;
        private Label label_thresholdHint = null!;
        private Button button_deduplicate = null!;
        private Button button_cancel = null!;
    }
}
