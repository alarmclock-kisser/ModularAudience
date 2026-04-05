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
            listBox_audios = new ListBox();
            checkBox_autoPlay = new CheckBox();
            button_export = new Button();
            checkBox_preview = new CheckBox();
            SuspendLayout();
            // 
            // listBox_audios
            // 
            listBox_audios.FormattingEnabled = true;
            listBox_audios.Location = new Point(12, 27);
            listBox_audios.Name = "listBox_audios";
            listBox_audios.SelectionMode = SelectionMode.MultiExtended;
            listBox_audios.Size = new Size(220, 289);
            listBox_audios.TabIndex = 0;
            listBox_audios.SelectedIndexChanged += listBox_audios_SelectedIndexChanged;
            // 
            // checkBox_autoPlay
            // 
            checkBox_autoPlay.AutoSize = true;
            checkBox_autoPlay.Location = new Point(155, 2);
            checkBox_autoPlay.Name = "checkBox_autoPlay";
            checkBox_autoPlay.Size = new Size(77, 19);
            checkBox_autoPlay.TabIndex = 1;
            checkBox_autoPlay.Text = "Auto Play";
            checkBox_autoPlay.UseVisualStyleBackColor = true;
            // 
            // button_export
            // 
            button_export.BackColor = Color.FromArgb(192, 255, 255);
            button_export.Location = new Point(12, 2);
            button_export.Name = "button_export";
            button_export.Size = new Size(60, 23);
            button_export.TabIndex = 2;
            button_export.Text = "Export";
            button_export.UseVisualStyleBackColor = false;
            button_export.Click += button_export_Click;
            // 
            // checkBox_preview
            // 
            checkBox_preview.AutoSize = true;
            checkBox_preview.Location = new Point(82, 2);
            checkBox_preview.Name = "checkBox_preview";
            checkBox_preview.Size = new Size(67, 19);
            checkBox_preview.TabIndex = 3;
            checkBox_preview.Text = "Preview";
            checkBox_preview.UseVisualStyleBackColor = true;
            // 
            // AudioCollectionView
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(244, 321);
            Controls.Add(checkBox_preview);
            Controls.Add(button_export);
            Controls.Add(checkBox_autoPlay);
            Controls.Add(listBox_audios);
            MaximizeBox = false;
            MaximumSize = new Size(480, 8192);
            MinimizeBox = false;
            MinimumSize = new Size(200, 100);
            Name = "AudioCollectionView";
            Text = "Audio Collection #00";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private ListBox listBox_audios;
        private CheckBox checkBox_autoPlay;
        private Button button_export;
        private CheckBox checkBox_preview;
    }
}