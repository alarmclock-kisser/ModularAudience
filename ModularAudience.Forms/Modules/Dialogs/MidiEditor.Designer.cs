namespace ModularAudience.Forms.Modules.Dialogs
{
    partial class MidiEditor
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
            this.pictureBox_editor = new PictureBox();
            this.button_save = new Button();
            this.checkBox_preview = new CheckBox();
            this.button_play = new Button();
            ((System.ComponentModel.ISupportInitialize) this.pictureBox_editor).BeginInit();
            this.SuspendLayout();
            // 
            // pictureBox_editor
            // 
            this.pictureBox_editor.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            this.pictureBox_editor.BackColor = Color.Black;
            this.pictureBox_editor.Location = new Point(12, 12);
            this.pictureBox_editor.Name = "pictureBox_editor";
            this.pictureBox_editor.Size = new Size(776, 397);
            this.pictureBox_editor.TabIndex = 0;
            this.pictureBox_editor.TabStop = false;
            this.pictureBox_editor.Paint += this.pictureBox_editor_Paint;
            this.pictureBox_editor.MouseDown += this.pictureBox_editor_MouseDown;
            this.pictureBox_editor.MouseMove += this.pictureBox_editor_MouseMove;
            this.pictureBox_editor.MouseUp += this.pictureBox_editor_MouseUp;
            this.pictureBox_editor.MouseWheel += this.pictureBox_editor_MouseWheel;
            // 
            // button_save
            // 
            this.button_save.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            this.button_save.Location = new Point(713, 415);
            this.button_save.Name = "button_save";
            this.button_save.Size = new Size(75, 23);
            this.button_save.TabIndex = 1;
            this.button_save.Text = "Save";
            this.button_save.UseVisualStyleBackColor = true;
            this.button_save.Click += this.button_save_Click;
            // 
            // checkBox_preview
            // 
            this.checkBox_preview.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            this.checkBox_preview.AutoSize = true;
            this.checkBox_preview.Checked = true;
            this.checkBox_preview.CheckState = CheckState.Checked;
            this.checkBox_preview.Location = new Point(138, 419);
            this.checkBox_preview.Name = "checkBox_preview";
            this.checkBox_preview.Size = new Size(122, 19);
            this.checkBox_preview.TabIndex = 2;
            this.checkBox_preview.Text = "Preview MIDI note";
            this.checkBox_preview.UseVisualStyleBackColor = true;
            // 
            // button_play
            // 
            this.button_play.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            this.button_play.Location = new Point(12, 415);
            this.button_play.Name = "button_play";
            this.button_play.Size = new Size(75, 23);
            this.button_play.TabIndex = 3;
            this.button_play.Text = "Play";
            this.button_play.UseVisualStyleBackColor = true;
            this.button_play.Click += this.button_play_Click;
            // 
            // MidiEditor
            // 
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(800, 450);
            this.MinimumSize = new Size(400, 250);
            this.Controls.Add(this.button_play);
            this.Controls.Add(this.checkBox_preview);
            this.Controls.Add(this.button_save);
            this.Controls.Add(this.pictureBox_editor);
            this.Name = "MidiEditor";
            this.Text = "MidiEditor";
            this.Resize += this.MidiEditor_Resize;
            this.FormClosing += this.MidiEditor_FormClosing;
            ((System.ComponentModel.ISupportInitialize) this.pictureBox_editor).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private PictureBox pictureBox_editor;
        private Button button_save;
        private CheckBox checkBox_preview;
        private Button button_play;
    }
}