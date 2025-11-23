namespace ModularAudience.Forms.Modules.Dialogs
{
    partial class AutoSamplesDialog
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
            this.button_cut = new Button();
            this.button_cancel = new Button();
            this.progressBar_cutting = new ProgressBar();
            this.numericUpDown_minDuration = new NumericUpDown();
            this.label_info_minDuration = new Label();
            this.label_info_maxDuration = new Label();
            this.numericUpDown_maxDuration = new NumericUpDown();
            this.label_info_silenceDuration = new Label();
            this.numericUpDown_silenceDuration = new NumericUpDown();
            this.label_status = new Label();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_minDuration).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_maxDuration).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_silenceDuration).BeginInit();
            this.SuspendLayout();
            // 
            // button_cut
            // 
            this.button_cut.BackColor = SystemColors.Info;
            this.button_cut.Location = new Point(257, 166);
            this.button_cut.Name = "button_cut";
            this.button_cut.Size = new Size(75, 23);
            this.button_cut.TabIndex = 0;
            this.button_cut.Text = "Cut";
            this.button_cut.UseVisualStyleBackColor = false;
            this.button_cut.Click += this.button_cut_Click;
            // 
            // button_cancel
            // 
            this.button_cancel.Location = new Point(196, 166);
            this.button_cancel.Name = "button_cancel";
            this.button_cancel.Size = new Size(55, 23);
            this.button_cancel.TabIndex = 1;
            this.button_cancel.Text = "Cancel";
            this.button_cancel.UseVisualStyleBackColor = true;
            this.button_cancel.Click += this.button_cancel_Click;
            // 
            // progressBar_cutting
            // 
            this.progressBar_cutting.Location = new Point(12, 166);
            this.progressBar_cutting.Name = "progressBar_cutting";
            this.progressBar_cutting.Size = new Size(178, 23);
            this.progressBar_cutting.TabIndex = 2;
            // 
            // label_status
            // 
            this.label_status.AutoSize = true;
            this.label_status.Location = new Point(12, 196);
            this.label_status.Name = "label_status";
            this.label_status.Size = new Size(120, 15);
            this.label_status.TabIndex = 9;
            this.label_status.Text = "Ready to cut samples";
            // 
            // numericUpDown_minDuration
            // 
            this.numericUpDown_minDuration.Increment = new decimal(new int[] { 5, 0, 0, 0 });
            this.numericUpDown_minDuration.Location = new Point(12, 114);
            this.numericUpDown_minDuration.Maximum = new decimal(new int[] { 1000, 0, 0, 0 });
            this.numericUpDown_minDuration.Minimum = new decimal(new int[] { 25, 0, 0, 0 });
            this.numericUpDown_minDuration.Name = "numericUpDown_minDuration";
            this.numericUpDown_minDuration.Size = new Size(60, 23);
            this.numericUpDown_minDuration.TabIndex = 3;
            this.numericUpDown_minDuration.Value = new decimal(new int[] { 175, 0, 0, 0 });
            // 
            // label_info_minDuration
            // 
            this.label_info_minDuration.AutoSize = true;
            this.label_info_minDuration.Location = new Point(12, 81);
            this.label_info_minDuration.Name = "label_info_minDuration";
            this.label_info_minDuration.Size = new Size(60, 30);
            this.label_info_minDuration.TabIndex = 4;
            this.label_info_minDuration.Text = "Minimum\r\nDuration";
            // 
            // label_info_maxDuration
            // 
            this.label_info_maxDuration.AutoSize = true;
            this.label_info_maxDuration.Location = new Point(78, 81);
            this.label_info_maxDuration.Name = "label_info_maxDuration";
            this.label_info_maxDuration.Size = new Size(61, 30);
            this.label_info_maxDuration.TabIndex = 6;
            this.label_info_maxDuration.Text = "Maximum\r\nDuration";
            // 
            // numericUpDown_maxDuration
            // 
            this.numericUpDown_maxDuration.Increment = new decimal(new int[] { 10, 0, 0, 0 });
            this.numericUpDown_maxDuration.Location = new Point(78, 114);
            this.numericUpDown_maxDuration.Maximum = new decimal(new int[] { 3600, 0, 0, 0 });
            this.numericUpDown_maxDuration.Minimum = new decimal(new int[] { 50, 0, 0, 0 });
            this.numericUpDown_maxDuration.Name = "numericUpDown_maxDuration";
            this.numericUpDown_maxDuration.Size = new Size(60, 23);
            this.numericUpDown_maxDuration.TabIndex = 5;
            this.numericUpDown_maxDuration.Value = new decimal(new int[] { 175, 0, 0, 0 });
            // 
            // label_info_silenceDuration
            // 
            this.label_info_silenceDuration.AutoSize = true;
            this.label_info_silenceDuration.Location = new Point(196, 81);
            this.label_info_silenceDuration.Name = "label_info_silenceDuration";
            this.label_info_silenceDuration.Size = new Size(53, 30);
            this.label_info_silenceDuration.TabIndex = 8;
            this.label_info_silenceDuration.Text = "Silence\r\nDuration";
            // 
            // numericUpDown_silenceDuration
            // 
            this.numericUpDown_silenceDuration.Increment = new decimal(new int[] { 5, 0, 0, 0 });
            this.numericUpDown_silenceDuration.Location = new Point(196, 114);
            this.numericUpDown_silenceDuration.Maximum = new decimal(new int[] { 2000, 0, 0, 0 });
            this.numericUpDown_silenceDuration.Minimum = new decimal(new int[] { 25, 0, 0, 0 });
            this.numericUpDown_silenceDuration.Name = "numericUpDown_silenceDuration";
            this.numericUpDown_silenceDuration.Size = new Size(60, 23);
            this.numericUpDown_silenceDuration.TabIndex = 7;
            this.numericUpDown_silenceDuration.Value = new decimal(new int[] { 60, 0, 0, 0 });
            // 
            // AutoSamplesDialog
            // 
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(344, 221);
            this.Controls.Add(this.label_status);
            this.Controls.Add(this.label_info_silenceDuration);
            this.Controls.Add(this.numericUpDown_silenceDuration);
            this.Controls.Add(this.label_info_maxDuration);
            this.Controls.Add(this.numericUpDown_maxDuration);
            this.Controls.Add(this.label_info_minDuration);
            this.Controls.Add(this.numericUpDown_minDuration);
            this.Controls.Add(this.progressBar_cutting);
            this.Controls.Add(this.button_cancel);
            this.Controls.Add(this.button_cut);
            this.Name = "AutoSamplesDialog";
            this.Text = "AutoSamplesDialog";
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_minDuration).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_maxDuration).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_silenceDuration).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private Button button_cut;
        private Button button_cancel;
        private ProgressBar progressBar_cutting;
        private NumericUpDown numericUpDown_minDuration;
        private Label label_info_minDuration;
        private Label label_info_maxDuration;
        private NumericUpDown numericUpDown_maxDuration;
        private Label label_info_silenceDuration;
        private NumericUpDown numericUpDown_silenceDuration;
        private Label label_status;
    }
}