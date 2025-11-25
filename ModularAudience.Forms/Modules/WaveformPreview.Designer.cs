namespace ModularAudience.Forms.Modules
{
    partial class WaveformPreview
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
            this.pictureBox_waveform = new PictureBox();
            ((System.ComponentModel.ISupportInitialize) this.pictureBox_waveform).BeginInit();
            this.SuspendLayout();
            // 
            // pictureBox_waveform
            // 
            this.pictureBox_waveform.Location = new Point(0, 0);
            this.pictureBox_waveform.Name = "pictureBox_waveform";
            this.pictureBox_waveform.Size = new Size(160, 160);
            this.pictureBox_waveform.TabIndex = 0;
            this.pictureBox_waveform.TabStop = false;
            this.pictureBox_waveform.SizeMode = PictureBoxSizeMode.StretchImage;
            // 
            // WaveformPreview
            // 
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(160, 160);
            this.Controls.Add(this.pictureBox_waveform);
            this.Name = "WaveformPreview";
            this.Text = "WaveformPreview";
            this.FormBorderStyle = FormBorderStyle.None;
            this.BackColor = Color.White;
            ((System.ComponentModel.ISupportInitialize) this.pictureBox_waveform).EndInit();
            this.ResumeLayout(false);
        }

        #endregion

        private PictureBox pictureBox_waveform;
    }
}