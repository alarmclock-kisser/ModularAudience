namespace ModularAudience.Forms.Modules.Dialogs
{
    partial class MidiWindow
    {
        private System.ComponentModel.IContainer components = null;
        private PictureBox pictureBox_midi;
        private Label label_track;
        private NumericUpDown numericUpDown_track;
        private Label label_bpm;
        private NumericUpDown numericUpDown_bpm;
        private Label label_instrument;
        private ComboBox comboBox_instrument;
        private Button button_customInstrument;
        private Button button_preview;
        private Button button_save;
        private Label label_status;
        private System.Windows.Forms.Timer timer_previewCaret;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.pictureBox_midi = new PictureBox();
            this.label_track = new Label();
            this.numericUpDown_track = new NumericUpDown();
            this.label_bpm = new Label();
            this.numericUpDown_bpm = new NumericUpDown();
            this.label_instrument = new Label();
            this.comboBox_instrument = new ComboBox();
            this.button_customInstrument = new Button();
            this.button_preview = new Button();
            this.button_save = new Button();
            this.label_status = new Label();
            this.timer_previewCaret = new System.Windows.Forms.Timer(this.components);
            ((System.ComponentModel.ISupportInitialize) this.pictureBox_midi).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_track).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_bpm).BeginInit();
            this.SuspendLayout();
            // 
            // pictureBox_midi
            // 
            this.pictureBox_midi.BackColor = Color.FromArgb(  24,   24,   28);
            this.pictureBox_midi.BorderStyle = BorderStyle.FixedSingle;
            this.pictureBox_midi.Dock = DockStyle.Top;
            this.pictureBox_midi.Location = new Point(0, 0);
            this.pictureBox_midi.Name = "pictureBox_midi";
            this.pictureBox_midi.Size = new Size(824, 500);
            this.pictureBox_midi.TabIndex = 0;
            this.pictureBox_midi.TabStop = false;
            this.pictureBox_midi.MouseDown += this.pictureBox_midi_MouseDown;
            this.pictureBox_midi.MouseEnter += this.pictureBox_midi_MouseEnter;
            this.pictureBox_midi.MouseLeave += this.pictureBox_midi_MouseLeave;
            this.pictureBox_midi.MouseMove += this.pictureBox_midi_MouseMove;
            this.pictureBox_midi.MouseUp += this.pictureBox_midi_MouseUp;
            this.pictureBox_midi.Paint += this.pictureBox_midi_Paint;
            // 
            // label_track
            // 
            this.label_track.AutoSize = true;
            this.label_track.Location = new Point(12, 515);
            this.label_track.Name = "label_track";
            this.label_track.Size = new Size(38, 15);
            this.label_track.TabIndex = 1;
            this.label_track.Text = "Track:";
            // 
            // numericUpDown_track
            // 
            this.numericUpDown_track.Location = new Point(52, 512);
            this.numericUpDown_track.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numericUpDown_track.Name = "numericUpDown_track";
            this.numericUpDown_track.Size = new Size(58, 23);
            this.numericUpDown_track.TabIndex = 2;
            this.numericUpDown_track.Value = new decimal(new int[] { 1, 0, 0, 0 });
            this.numericUpDown_track.ValueChanged += this.numericUpDown_track_ValueChanged;
            // 
            // label_bpm
            // 
            this.label_bpm.AutoSize = true;
            this.label_bpm.Location = new Point(130, 515);
            this.label_bpm.Name = "label_bpm";
            this.label_bpm.Size = new Size(35, 15);
            this.label_bpm.TabIndex = 3;
            this.label_bpm.Text = "BPM:";
            // 
            // numericUpDown_bpm
            // 
            this.numericUpDown_bpm.DecimalPlaces = 2;
            this.numericUpDown_bpm.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
            this.numericUpDown_bpm.Location = new Point(168, 512);
            this.numericUpDown_bpm.Maximum = new decimal(new int[] { 400, 0, 0, 0 });
            this.numericUpDown_bpm.Minimum = new decimal(new int[] { 20, 0, 0, 0 });
            this.numericUpDown_bpm.Name = "numericUpDown_bpm";
            this.numericUpDown_bpm.Size = new Size(82, 23);
            this.numericUpDown_bpm.TabIndex = 4;
            this.numericUpDown_bpm.Value = new decimal(new int[] { 20, 0, 0, 0 });
            this.numericUpDown_bpm.ValueChanged += this.numericUpDown_bpm_ValueChanged;
            // 
            // label_instrument
            // 
            this.label_instrument.AutoSize = true;
            this.label_instrument.Location = new Point(270, 515);
            this.label_instrument.Name = "label_instrument";
            this.label_instrument.Size = new Size(68, 15);
            this.label_instrument.TabIndex = 5;
            this.label_instrument.Text = "Instrument:";
            // 
            // comboBox_instrument
            // 
            this.comboBox_instrument.DropDownStyle = ComboBoxStyle.DropDownList;
            this.comboBox_instrument.Location = new Point(344, 512);
            this.comboBox_instrument.Name = "comboBox_instrument";
            this.comboBox_instrument.Size = new Size(145, 23);
            this.comboBox_instrument.TabIndex = 6;
            this.comboBox_instrument.SelectedIndexChanged += this.comboBox_instrument_SelectedIndexChanged;
            // 
            // button_customInstrument
            // 
            this.button_customInstrument.Location = new Point(495, 512);
            this.button_customInstrument.Name = "button_customInstrument";
            this.button_customInstrument.Size = new Size(105, 23);
            this.button_customInstrument.TabIndex = 7;
            this.button_customInstrument.Text = "Custom...";
            this.button_customInstrument.Click += this.button_customInstrument_Click;
            // 
            // button_preview
            // 
            this.button_preview.Anchor =  AnchorStyles.Bottom | AnchorStyles.Left;
            this.button_preview.Location = new Point(12, 582);
            this.button_preview.Name = "button_preview";
            this.button_preview.Size = new Size(130, 30);
            this.button_preview.TabIndex = 8;
            this.button_preview.Text = "Preview";
            this.button_preview.Click += this.button_preview_Click;
            // 
            // button_save
            // 
            this.button_save.Anchor =  AnchorStyles.Bottom | AnchorStyles.Right;
            this.button_save.Location = new Point(682, 582);
            this.button_save.Name = "button_save";
            this.button_save.Size = new Size(130, 30);
            this.button_save.TabIndex = 9;
            this.button_save.Text = "Save";
            this.button_save.Click += this.button_save_Click;
            // 
            // label_status
            // 
            this.label_status.Anchor =  AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            this.label_status.AutoEllipsis = true;
            this.label_status.Location = new Point(155, 588);
            this.label_status.Name = "label_status";
            this.label_status.Size = new Size(510, 23);
            this.label_status.TabIndex = 10;
            // 
            // timer_previewCaret
            // 
            this.timer_previewCaret.Interval = 30;
            this.timer_previewCaret.Tick += this.timer_previewCaret_Tick;
            // 
            // MidiWindow
            // 
            this.ClientSize = new Size(824, 624);
            this.Controls.Add(this.pictureBox_midi);
            this.Controls.Add(this.label_track);
            this.Controls.Add(this.numericUpDown_track);
            this.Controls.Add(this.label_bpm);
            this.Controls.Add(this.numericUpDown_bpm);
            this.Controls.Add(this.label_instrument);
            this.Controls.Add(this.comboBox_instrument);
            this.Controls.Add(this.button_customInstrument);
            this.Controls.Add(this.button_preview);
            this.Controls.Add(this.button_save);
            this.Controls.Add(this.label_status);
            this.MinimumSize = new Size(700, 500);
            this.Name = "MidiWindow";
            this.StartPosition = FormStartPosition.CenterParent;
            this.Text = "MIDI Renderer";
            this.FormClosing += this.MidiWindow_FormClosing;
            this.Resize += this.MidiWindow_Resize;
            ((System.ComponentModel.ISupportInitialize) this.pictureBox_midi).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_track).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_bpm).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}