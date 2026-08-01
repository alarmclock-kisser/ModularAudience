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
            this.hScrollBar_editor = new HScrollBar();
            this.label_noteGranularity = new Label();
            this.domainUpDown_noteGranularity = new DomainUpDown();
            this.label_pitchFrequency = new Label();
            this.numericUpDown_pitchFrequency = new NumericUpDown();
            this.button_import = new Button();
            this.timer_previewCaret = new System.Windows.Forms.Timer();
            ((System.ComponentModel.ISupportInitialize) this.pictureBox_editor).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_pitchFrequency).BeginInit();
            this.SuspendLayout();
            // 
            // pictureBox_editor
            // 
            this.pictureBox_editor.Anchor =  AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            this.pictureBox_editor.BackColor = Color.Black;
            this.pictureBox_editor.Location = new Point(12, 12);
            this.pictureBox_editor.Name = "pictureBox_editor";
            this.pictureBox_editor.Size = new Size(776, 380);
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
            this.button_save.Anchor =  AnchorStyles.Bottom | AnchorStyles.Right;
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
            this.checkBox_preview.Anchor =  AnchorStyles.Bottom | AnchorStyles.Left;
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
            this.button_play.Anchor =  AnchorStyles.Bottom | AnchorStyles.Left;
            this.button_play.Location = new Point(12, 415);
            this.button_play.Name = "button_play";
            this.button_play.Size = new Size(75, 23);
            this.button_play.TabIndex = 3;
            this.button_play.Text = "Play";
            this.button_play.UseVisualStyleBackColor = true;
            this.button_play.Click += this.button_play_Click;
            // 
            // hScrollBar_editor
            // 
            this.hScrollBar_editor.Anchor =  AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            this.hScrollBar_editor.LargeChange = 100;
            this.hScrollBar_editor.Location = new Point(12, 392);
            this.hScrollBar_editor.Maximum = 1000;
            this.hScrollBar_editor.Name = "hScrollBar_editor";
            this.hScrollBar_editor.Size = new Size(776, 17);
            this.hScrollBar_editor.TabIndex = 4;
            this.hScrollBar_editor.Visible = false;
            this.hScrollBar_editor.Scroll += this.hScrollBar_editor_Scroll;
            // 
            // label_noteGranularity
            // 
            this.label_noteGranularity.Anchor =  AnchorStyles.Bottom | AnchorStyles.Left;
            this.label_noteGranularity.AutoSize = true;
            this.label_noteGranularity.Location = new Point(280, 419);
            this.label_noteGranularity.Name = "label_noteGranularity";
            this.label_noteGranularity.Size = new Size(61, 15);
            this.label_noteGranularity.TabIndex = 5;
            this.label_noteGranularity.Text = "Grid notes";
            // 
            // domainUpDown_noteGranularity
            // 
            this.domainUpDown_noteGranularity.Anchor =  AnchorStyles.Bottom | AnchorStyles.Left;
            this.domainUpDown_noteGranularity.Items.Add("1");
            this.domainUpDown_noteGranularity.Items.Add("2");
            this.domainUpDown_noteGranularity.Items.Add("3");
            this.domainUpDown_noteGranularity.Items.Add("4");
            this.domainUpDown_noteGranularity.Items.Add("5");
            this.domainUpDown_noteGranularity.Items.Add("6");
            this.domainUpDown_noteGranularity.Items.Add("8");
            this.domainUpDown_noteGranularity.Items.Add("12");
            this.domainUpDown_noteGranularity.Items.Add("16");
            this.domainUpDown_noteGranularity.Location = new Point(348, 415);
            this.domainUpDown_noteGranularity.Name = "domainUpDown_noteGranularity";
            this.domainUpDown_noteGranularity.ReadOnly = true;
            this.domainUpDown_noteGranularity.SelectedIndex = 3;
            this.domainUpDown_noteGranularity.Size = new Size(50, 23);
            this.domainUpDown_noteGranularity.TabIndex = 6;
            this.domainUpDown_noteGranularity.Text = "4";
            this.domainUpDown_noteGranularity.SelectedItemChanged += this.domainUpDown_noteGranularity_SelectedItemChanged;
            // 
            // label_pitchFrequency
            // 
            this.label_pitchFrequency.Anchor =  AnchorStyles.Bottom | AnchorStyles.Left;
            this.label_pitchFrequency.AutoSize = true;
            this.label_pitchFrequency.Location = new Point(414, 419);
            this.label_pitchFrequency.Name = "label_pitchFrequency";
            this.label_pitchFrequency.Size = new Size(78, 15);
            this.label_pitchFrequency.TabIndex = 7;
            this.label_pitchFrequency.Text = "A4 frequency";
            // 
            // numericUpDown_pitchFrequency
            // 
            this.numericUpDown_pitchFrequency.Anchor =  AnchorStyles.Bottom | AnchorStyles.Left;
            this.numericUpDown_pitchFrequency.DecimalPlaces = 1;
            this.numericUpDown_pitchFrequency.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
            this.numericUpDown_pitchFrequency.Location = new Point(498, 415);
            this.numericUpDown_pitchFrequency.Maximum = new decimal(new int[] { 1000, 0, 0, 0 });
            this.numericUpDown_pitchFrequency.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numericUpDown_pitchFrequency.Name = "numericUpDown_pitchFrequency";
            this.numericUpDown_pitchFrequency.Size = new Size(70, 23);
            this.numericUpDown_pitchFrequency.TabIndex = 8;
            this.numericUpDown_pitchFrequency.Value = new decimal(new int[] { 440, 0, 0, 0 });
            // 
            // button_import
            // 
            this.button_import.Location = new Point(632, 415);
            this.button_import.Name = "button_import";
            this.button_import.Size = new Size(75, 23);
            this.button_import.TabIndex = 7;
            this.button_import.Text = "Import";
            this.button_import.UseVisualStyleBackColor = true;
            this.button_import.Click += this.button_import_Click;
            // 
            // MidiEditor
            // 
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(800, 450);
            this.Controls.Add(this.button_import);
            this.Controls.Add(this.button_play);
            this.Controls.Add(this.checkBox_preview);
            this.Controls.Add(this.button_save);
            this.Controls.Add(this.hScrollBar_editor);
            this.Controls.Add(this.domainUpDown_noteGranularity);
            this.Controls.Add(this.label_noteGranularity);
            this.Controls.Add(this.numericUpDown_pitchFrequency);
            this.Controls.Add(this.label_pitchFrequency);
            this.Controls.Add(this.pictureBox_editor);
            this.MinimumSize = new Size(400, 250);
            this.Name = "MidiEditor";
            this.Text = "MidiEditor";
            this.FormClosing += this.MidiEditor_FormClosing;
            this.Resize += this.MidiEditor_Resize;
            // 
            // timer_previewCaret
            // 
            this.timer_previewCaret.Interval = 30;
            this.timer_previewCaret.Tick += this.timer_previewCaret_Tick;
            ((System.ComponentModel.ISupportInitialize) this.pictureBox_editor).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_pitchFrequency).EndInit();
        }

        #endregion

        private PictureBox pictureBox_editor;
        private Button button_save;
        private CheckBox checkBox_preview;
        private Button button_play;
        private HScrollBar hScrollBar_editor;
        private Label label_noteGranularity;
        private DomainUpDown domainUpDown_noteGranularity;
        private Label label_pitchFrequency;
        private NumericUpDown numericUpDown_pitchFrequency;
        private Button button_import;
        private System.Windows.Forms.Timer timer_previewCaret;
    }
}