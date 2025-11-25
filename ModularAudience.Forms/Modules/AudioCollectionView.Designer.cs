namespace ModularAudience.Forms
{
    partial class AudioCollectionView
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
            this.listBox_audios = new ListBox();
            this.checkBox_autoPlay = new CheckBox();
            this.button_export = new Button();
            this.SuspendLayout();
            // 
            // listBox_audios
            // 
            this.listBox_audios.FormattingEnabled = true;
            this.listBox_audios.Location = new Point(12, 27);
            this.listBox_audios.Name = "listBox_audios";
            this.listBox_audios.SelectionMode = SelectionMode.MultiExtended;
            this.listBox_audios.Size = new Size(220, 289);
            this.listBox_audios.TabIndex = 0;
            this.listBox_audios.SelectedIndexChanged += this.listBox_audios_SelectedIndexChanged;
            // 
            // checkBox_autoPlay
            // 
            this.checkBox_autoPlay.AutoSize = true;
            this.checkBox_autoPlay.Location = new Point(155, 2);
            this.checkBox_autoPlay.Name = "checkBox_autoPlay";
            this.checkBox_autoPlay.Size = new Size(77, 19);
            this.checkBox_autoPlay.TabIndex = 1;
            this.checkBox_autoPlay.Text = "Auto Play";
            this.checkBox_autoPlay.UseVisualStyleBackColor = true;
            // 
            // button_export
            // 
            this.button_export.BackColor = Color.FromArgb(  192,   255,   255);
            this.button_export.Location = new Point(12, 2);
            this.button_export.Name = "button_export";
            this.button_export.Size = new Size(75, 23);
            this.button_export.TabIndex = 2;
            this.button_export.Text = "Export";
            this.button_export.UseVisualStyleBackColor = false;
            this.button_export.Click += this.button_export_Click;
            // 
            // AudioCollectionView
            // 
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(244, 321);
            this.Controls.Add(this.button_export);
            this.Controls.Add(this.checkBox_autoPlay);
            this.Controls.Add(this.listBox_audios);
            this.MaximizeBox = false;
            this.MaximumSize = new Size(480, 8192);
            this.MinimizeBox = false;
            this.MinimumSize = new Size(200, 100);
            this.Name = "AudioCollectionView";
            this.Text = "Audio Collection #00";
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private ListBox listBox_audios;
        private CheckBox checkBox_autoPlay;
        private Button button_export;
    }
}