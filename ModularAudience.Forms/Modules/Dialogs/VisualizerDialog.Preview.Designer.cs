namespace ModularAudience.Forms
{
    partial class VisualizerDialogPreview
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
            this.pictureBox_preview = new PictureBox();
            ((System.ComponentModel.ISupportInitialize) this.pictureBox_preview).BeginInit();
            this.SuspendLayout();
            // 
            // pictureBox_preview
            // 
            this.pictureBox_preview.Dock = DockStyle.Fill;
            this.pictureBox_preview.Location = new Point(0, 0);
            this.pictureBox_preview.Name = "pictureBox_preview";
            this.pictureBox_preview.Size = new Size(240, 217);
            this.pictureBox_preview.TabIndex = 0;
            this.pictureBox_preview.TabStop = false;
            // 
            // VisualizerDialogPreview
            // 
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(240, 217);
            this.Controls.Add(this.pictureBox_preview);
            this.Name = "VisualizerDialogPreview";
            this.Text = "Visualizer Preview";
            ((System.ComponentModel.ISupportInitialize) this.pictureBox_preview).EndInit();
            this.ResumeLayout(false);
        }

        #endregion

        private PictureBox pictureBox_preview;
    }
}