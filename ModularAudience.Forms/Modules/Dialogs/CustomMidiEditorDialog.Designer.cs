namespace ModularAudience.Forms.Modules.Dialogs
{
    partial class CustomMidiEditorDialog
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
            this.components = new System.ComponentModel.Container();
            this.tableLayoutPanel_root = new TableLayoutPanel();
            this.panel_top = new Panel();
            this.flowLayoutPanel_top = new FlowLayoutPanel();
            this.flowLayoutPanel_tracks = new FlowLayoutPanel();
            this.button_import = new Button();
            this.label_track = new Label();
            this.numericUpDown_track = new NumericUpDown();
            this.label_trackName = new Label();
            this.button_instrument = new Button();
            this.label_instrument = new Label();
            this.label_resolution = new Label();
            this.numericUpDown_resolution = new NumericUpDown();
            this.label_bpm = new Label();
            this.numericUpDown_bpm = new NumericUpDown();
            this.label_minKey = new Label();
            this.domainUpDown_minKey = new DomainUpDown();
            this.label_maxKey = new Label();
            this.domainUpDown_maxKey = new DomainUpDown();
            this.pictureBox_editor = new PictureBox();
            this.panel_bottom = new Panel();
            this.hScrollBar_editor = new HScrollBar();
            this.panel_actions = new Panel();
            this.flowLayoutPanel_left = new FlowLayoutPanel();
            this.button_play = new Button();
            this.checkBox_preview = new CheckBox();
            this.label_pitchFrequency = new Label();
            this.numericUpDown_pitchFrequency = new NumericUpDown();
            this.flowLayoutPanel_right = new FlowLayoutPanel();
            this.button_save = new Button();
            this.button_remix = new Button();
            this.button_generate = new Button();
            this.button_record = new Button();
            this.toolTip_editor = new ToolTip(this.components);
            this.timer_previewCaret = new System.Windows.Forms.Timer(this.components);
            this.timer_recording = new System.Windows.Forms.Timer(this.components);
            this.comboBox_inputDevice = new ComboBox();
            this.label_recordStatus = new Label();
            this.progressBar_inputLevel = new ProgressBar();
            this.contextMenuStrip_playFromHere = new ContextMenuStrip(this.components);
            this.toolStripMenuItem_playFromHere = new ToolStripMenuItem();
            this.contextMenuStrip_save = new ContextMenuStrip(this.components);
            this.toolStripMenuItem_saveMidi = new ToolStripMenuItem();
            this.tableLayoutPanel_root.SuspendLayout();
            this.panel_top.SuspendLayout();
            this.flowLayoutPanel_top.SuspendLayout();
            this.flowLayoutPanel_tracks.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)this.numericUpDown_track).BeginInit();
            ((System.ComponentModel.ISupportInitialize)this.numericUpDown_resolution).BeginInit();
            ((System.ComponentModel.ISupportInitialize)this.numericUpDown_bpm).BeginInit();
            ((System.ComponentModel.ISupportInitialize)this.pictureBox_editor).BeginInit();
            this.panel_bottom.SuspendLayout();
            this.panel_actions.SuspendLayout();
            this.flowLayoutPanel_left.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)this.numericUpDown_pitchFrequency).BeginInit();
            this.flowLayoutPanel_right.SuspendLayout();
            this.contextMenuStrip_playFromHere.SuspendLayout();
            this.contextMenuStrip_save.SuspendLayout();
            this.SuspendLayout();
            // 
            // tableLayoutPanel_root
            // 
            this.tableLayoutPanel_root.ColumnCount = 1;
            this.tableLayoutPanel_root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            this.tableLayoutPanel_root.Controls.Add(this.panel_top, 0, 0);
            this.tableLayoutPanel_root.Controls.Add(this.pictureBox_editor, 0, 1);
            this.tableLayoutPanel_root.Controls.Add(this.panel_bottom, 0, 2);
            this.tableLayoutPanel_root.Dock = DockStyle.Fill;
            this.tableLayoutPanel_root.Location = new Point(0, 0);
            this.tableLayoutPanel_root.Margin = new Padding(0);
            this.tableLayoutPanel_root.Name = "tableLayoutPanel_root";
            this.tableLayoutPanel_root.RowCount = 3;
            this.tableLayoutPanel_root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66F));
            this.tableLayoutPanel_root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            this.tableLayoutPanel_root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64F));
            this.tableLayoutPanel_root.Size = new Size(900, 540);
            this.tableLayoutPanel_root.TabIndex = 0;
            // 
            // panel_top
            // 
            this.panel_top.Controls.Add(this.flowLayoutPanel_tracks);
            this.panel_top.Controls.Add(this.flowLayoutPanel_top);
            this.panel_top.Dock = DockStyle.Fill;
            this.panel_top.Location = new Point(0, 0);
            this.panel_top.Margin = new Padding(0);
            this.panel_top.Name = "panel_top";
            this.panel_top.Padding = new Padding(8, 4, 8, 2);
            this.panel_top.Size = new Size(900, 66);
            this.panel_top.TabIndex = 0;
            // 
            // flowLayoutPanel_top
            // 
            this.flowLayoutPanel_top.AutoScroll = true;
            this.flowLayoutPanel_top.Controls.Add(this.button_instrument);
            this.flowLayoutPanel_top.Controls.Add(this.label_instrument);
            this.flowLayoutPanel_top.Controls.Add(this.label_resolution);
            this.flowLayoutPanel_top.Controls.Add(this.numericUpDown_resolution);
            this.flowLayoutPanel_top.Controls.Add(this.label_bpm);
            this.flowLayoutPanel_top.Controls.Add(this.numericUpDown_bpm);
            this.flowLayoutPanel_top.Controls.Add(this.label_minKey);
            this.flowLayoutPanel_top.Controls.Add(this.domainUpDown_minKey);
            this.flowLayoutPanel_top.Controls.Add(this.label_maxKey);
            this.flowLayoutPanel_top.Controls.Add(this.domainUpDown_maxKey);
            this.flowLayoutPanel_top.Dock = DockStyle.Top;
            this.flowLayoutPanel_top.FlowDirection = FlowDirection.LeftToRight;
            this.flowLayoutPanel_top.Location = new Point(8, 4);
            this.flowLayoutPanel_top.Margin = new Padding(0);
            this.flowLayoutPanel_top.Name = "flowLayoutPanel_top";
            this.flowLayoutPanel_top.Size = new Size(884, 28);
            this.flowLayoutPanel_top.TabIndex = 0;
            this.flowLayoutPanel_top.WrapContents = false;
            //
            // flowLayoutPanel_tracks
            //
            this.flowLayoutPanel_tracks.Controls.Add(this.button_import);
            this.flowLayoutPanel_tracks.Controls.Add(this.label_track);
            this.flowLayoutPanel_tracks.Controls.Add(this.numericUpDown_track);
            this.flowLayoutPanel_tracks.Controls.Add(this.label_trackName);
            this.flowLayoutPanel_tracks.Dock = DockStyle.Bottom;
            this.flowLayoutPanel_tracks.FlowDirection = FlowDirection.LeftToRight;
            this.flowLayoutPanel_tracks.Location = new Point(8, 34);
            this.flowLayoutPanel_tracks.Margin = new Padding(0);
            this.flowLayoutPanel_tracks.Name = "flowLayoutPanel_tracks";
            this.flowLayoutPanel_tracks.Size = new Size(884, 28);
            this.flowLayoutPanel_tracks.TabIndex = 1;
            this.flowLayoutPanel_tracks.WrapContents = false;
            //
            // button_import
            //
            this.button_import.Location = new Point(3, 3);
            this.button_import.Name = "button_import";
            this.button_import.Size = new Size(72, 23);
            this.button_import.TabIndex = 0;
            this.button_import.Text = "Import";
            this.button_import.UseVisualStyleBackColor = true;
            this.button_import.Click += this.button_import_Click;
            //
            // label_track
            //
            this.label_track.AutoSize = true;
            this.label_track.Location = new Point(81, 0);
            this.label_track.Margin = new Padding(3, 8, 3, 0);
            this.label_track.Name = "label_track";
            this.label_track.Size = new Size(34, 15);
            this.label_track.TabIndex = 1;
            this.label_track.Text = "Track";
            //
            // numericUpDown_track
            //
            this.numericUpDown_track.Location = new Point(118, 3);
            this.numericUpDown_track.Maximum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numericUpDown_track.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numericUpDown_track.Name = "numericUpDown_track";
            this.numericUpDown_track.Size = new Size(55, 23);
            this.numericUpDown_track.TabIndex = 2;
            this.numericUpDown_track.Value = new decimal(new int[] { 1, 0, 0, 0 });
            this.numericUpDown_track.ValueChanged += this.numericUpDown_track_ValueChanged;
            //
            // label_trackName
            //
            this.label_trackName.AutoEllipsis = true;
            this.label_trackName.Location = new Point(176, 3);
            this.label_trackName.Margin = new Padding(3, 5, 3, 0);
            this.label_trackName.Name = "label_trackName";
            this.label_trackName.Size = new Size(400, 23);
            this.label_trackName.TabIndex = 3;
            this.label_trackName.Text = "Piano Roll";
            this.label_trackName.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // button_instrument
            // 
            this.button_instrument.Location = new Point(3, 3);
            this.button_instrument.Name = "button_instrument";
            this.button_instrument.Size = new Size(82, 23);
            this.button_instrument.TabIndex = 0;
            this.button_instrument.Text = "Instrument";
            this.button_instrument.UseVisualStyleBackColor = true;
            this.button_instrument.Click += this.button_instrument_Click;
            // 
            // label_instrument
            // 
            this.label_instrument.AutoSize = true;
            this.label_instrument.Location = new Point(91, 0);
            this.label_instrument.Margin = new Padding(3, 8, 14, 0);
            this.label_instrument.Name = "label_instrument";
            this.label_instrument.Size = new Size(31, 15);
            this.label_instrument.TabIndex = 1;
            this.label_instrument.Text = "Sine";
            // 
            // label_resolution
            // 
            this.label_resolution.AutoSize = true;
            this.label_resolution.Location = new Point(139, 0);
            this.label_resolution.Margin = new Padding(3, 8, 3, 0);
            this.label_resolution.Name = "label_resolution";
            this.label_resolution.Size = new Size(60, 15);
            this.label_resolution.TabIndex = 2;
            this.label_resolution.Text = "Resolution";
            // 
            // numericUpDown_resolution
            // 
            this.numericUpDown_resolution.Location = new Point(205, 3);
            this.numericUpDown_resolution.Maximum = new decimal(new int[] { 16, 0, 0, 0 });
            this.numericUpDown_resolution.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numericUpDown_resolution.Name = "numericUpDown_resolution";
            this.numericUpDown_resolution.Size = new Size(55, 23);
            this.numericUpDown_resolution.TabIndex = 3;
            this.numericUpDown_resolution.Value = new decimal(new int[] { 4, 0, 0, 0 });
            this.numericUpDown_resolution.ValueChanged += this.numericUpDown_resolution_ValueChanged;
            // 
            // label_bpm
            // 
            this.label_bpm.AutoSize = true;
            this.label_bpm.Location = new Point(266, 0);
            this.label_bpm.Margin = new Padding(3, 8, 3, 0);
            this.label_bpm.Name = "label_bpm";
            this.label_bpm.Size = new Size(31, 15);
            this.label_bpm.TabIndex = 4;
            this.label_bpm.Text = "BPM";
            // 
            // numericUpDown_bpm
            // 
            this.numericUpDown_bpm.DecimalPlaces = 2;
            this.numericUpDown_bpm.Increment = new decimal(new int[] { 5, 0, 0, 65536 });
            this.numericUpDown_bpm.Location = new Point(303, 3);
            this.numericUpDown_bpm.Maximum = new decimal(new int[] { 400, 0, 0, 0 });
            this.numericUpDown_bpm.Minimum = new decimal(new int[] { 20, 0, 0, 0 });
            this.numericUpDown_bpm.Name = "numericUpDown_bpm";
            this.numericUpDown_bpm.Size = new Size(70, 23);
            this.numericUpDown_bpm.TabIndex = 5;
            this.numericUpDown_bpm.Value = new decimal(new int[] { 100, 0, 0, 0 });
            this.numericUpDown_bpm.ValueChanged += this.numericUpDown_bpm_ValueChanged;
            // 
            // label_minKey
            // 
            this.label_minKey.AutoSize = true;
            this.label_minKey.Location = new Point(379, 0);
            this.label_minKey.Margin = new Padding(3, 8, 3, 0);
            this.label_minKey.Name = "label_minKey";
            this.label_minKey.Size = new Size(48, 15);
            this.label_minKey.TabIndex = 6;
            this.label_minKey.Text = "Min key";
            // 
            // domainUpDown_minKey
            // 
            this.domainUpDown_minKey.Location = new Point(433, 3);
            this.domainUpDown_minKey.Name = "domainUpDown_minKey";
            this.domainUpDown_minKey.ReadOnly = true;
            this.domainUpDown_minKey.Size = new Size(65, 23);
            this.domainUpDown_minKey.TabIndex = 7;
            this.domainUpDown_minKey.Wrap = false;
            this.domainUpDown_minKey.SelectedItemChanged += this.domainUpDown_minKey_SelectedItemChanged;
            // 
            // label_maxKey
            // 
            this.label_maxKey.AutoSize = true;
            this.label_maxKey.Location = new Point(504, 0);
            this.label_maxKey.Margin = new Padding(3, 8, 3, 0);
            this.label_maxKey.Name = "label_maxKey";
            this.label_maxKey.Size = new Size(52, 15);
            this.label_maxKey.TabIndex = 8;
            this.label_maxKey.Text = "Max key";
            // 
            // domainUpDown_maxKey
            // 
            this.domainUpDown_maxKey.Location = new Point(562, 3);
            this.domainUpDown_maxKey.Name = "domainUpDown_maxKey";
            this.domainUpDown_maxKey.ReadOnly = true;
            this.domainUpDown_maxKey.Size = new Size(65, 23);
            this.domainUpDown_maxKey.TabIndex = 9;
            this.domainUpDown_maxKey.Wrap = false;
            this.domainUpDown_maxKey.SelectedItemChanged += this.domainUpDown_maxKey_SelectedItemChanged;
            // 
            // pictureBox_editor
            // 
            this.pictureBox_editor.BackColor = Color.Black;
            this.pictureBox_editor.Dock = DockStyle.Fill;
            this.pictureBox_editor.Location = new Point(8, 41);
            this.pictureBox_editor.Margin = new Padding(8, 3, 8, 3);
            this.pictureBox_editor.Name = "pictureBox_editor";
            this.pictureBox_editor.Size = new Size(884, 436);
            this.pictureBox_editor.TabIndex = 1;
            this.pictureBox_editor.TabStop = false;
            this.pictureBox_editor.Paint += this.pictureBox_editor_Paint;
            this.pictureBox_editor.MouseDown += this.pictureBox_editor_MouseDown;
            this.pictureBox_editor.MouseMove += this.pictureBox_editor_MouseMove;
            this.pictureBox_editor.MouseUp += this.pictureBox_editor_MouseUp;
            this.pictureBox_editor.MouseWheel += this.pictureBox_editor_MouseWheel;
            // 
            // panel_bottom
            // 
            this.panel_bottom.Controls.Add(this.panel_actions);
            this.panel_bottom.Controls.Add(this.hScrollBar_editor);
            this.panel_bottom.Dock = DockStyle.Fill;
            this.panel_bottom.Location = new Point(0, 480);
            this.panel_bottom.Margin = new Padding(0);
            this.panel_bottom.Name = "panel_bottom";
            this.panel_bottom.Padding = new Padding(8, 0, 8, 4);
            this.panel_bottom.Size = new Size(900, 60);
            this.panel_bottom.TabIndex = 2;
            // 
            // hScrollBar_editor
            // 
            this.hScrollBar_editor.Dock = DockStyle.Top;
            this.hScrollBar_editor.LargeChange = 100;
            this.hScrollBar_editor.Location = new Point(8, 0);
            this.hScrollBar_editor.Maximum = 1000;
            this.hScrollBar_editor.Name = "hScrollBar_editor";
            this.hScrollBar_editor.Size = new Size(884, 17);
            this.hScrollBar_editor.TabIndex = 0;
            this.hScrollBar_editor.Visible = false;
            this.hScrollBar_editor.Scroll += this.hScrollBar_editor_Scroll;
            // 
            // panel_actions
            // 
            this.panel_actions.Controls.Add(this.flowLayoutPanel_left);
            this.panel_actions.Controls.Add(this.flowLayoutPanel_right);
            this.panel_actions.Dock = DockStyle.Fill;
            this.panel_actions.Location = new Point(8, 17);
            this.panel_actions.Margin = new Padding(0);
            this.panel_actions.Name = "panel_actions";
            this.panel_actions.Size = new Size(884, 39);
            this.panel_actions.TabIndex = 1;
            // 
            // flowLayoutPanel_left
            // 
            this.flowLayoutPanel_left.Controls.Add(this.button_play);
            this.flowLayoutPanel_left.Controls.Add(this.checkBox_preview);
            this.flowLayoutPanel_left.Controls.Add(this.label_pitchFrequency);
            this.flowLayoutPanel_left.Controls.Add(this.numericUpDown_pitchFrequency);
            this.flowLayoutPanel_left.Controls.Add(this.comboBox_inputDevice);
            this.flowLayoutPanel_left.Controls.Add(this.label_recordStatus);
            this.flowLayoutPanel_left.Controls.Add(this.progressBar_inputLevel);
            this.flowLayoutPanel_left.Dock = DockStyle.Left;
            this.flowLayoutPanel_left.Location = new Point(0, 0);
            this.flowLayoutPanel_left.Margin = new Padding(0);
            this.flowLayoutPanel_left.Name = "flowLayoutPanel_left";
            this.flowLayoutPanel_left.Padding = new Padding(0, 8, 0, 0);
            this.flowLayoutPanel_left.Size = new Size(600, 39);
            this.flowLayoutPanel_left.TabIndex = 0;
            this.flowLayoutPanel_left.WrapContents = false;
            //
            // comboBox_inputDevice
            //
            this.comboBox_inputDevice.DropDownStyle = ComboBoxStyle.DropDownList;
            this.comboBox_inputDevice.Enabled = false;
            this.comboBox_inputDevice.Location = new Point(391, 11);
            this.comboBox_inputDevice.Name = "comboBox_inputDevice";
            this.comboBox_inputDevice.Size = new Size(120, 23);
            this.comboBox_inputDevice.TabIndex = 4;
            this.comboBox_inputDevice.Visible = false;
            this.comboBox_inputDevice.SelectedIndexChanged += this.comboBox_inputDevice_SelectedIndexChanged;
            //
            // label_recordStatus
            //
            this.label_recordStatus.AutoEllipsis = true;
            this.label_recordStatus.Enabled = false;
            this.label_recordStatus.Location = new Point(517, 11);
            this.label_recordStatus.Margin = new Padding(3, 8, 3, 0);
            this.label_recordStatus.Name = "label_recordStatus";
            this.label_recordStatus.Size = new Size(90, 15);
            this.label_recordStatus.TabIndex = 5;
            this.label_recordStatus.Visible = false;
            //
            // progressBar_inputLevel
            //
            this.progressBar_inputLevel.Enabled = false;
            this.progressBar_inputLevel.Location = new Point(613, 11);
            this.progressBar_inputLevel.Name = "progressBar_inputLevel";
            this.progressBar_inputLevel.Size = new Size(80, 23);
            this.progressBar_inputLevel.TabIndex = 6;
            this.progressBar_inputLevel.Visible = false;

            // 
            // button_play
            // 
            this.button_play.Location = new Point(3, 11);
            this.button_play.Name = "button_play";
            this.button_play.Size = new Size(75, 23);
            this.button_play.TabIndex = 0;
            this.button_play.Text = "Play";
            this.button_play.UseVisualStyleBackColor = true;
            this.button_play.Click += this.button_play_Click;
            // 
            // checkBox_preview
            // 
            this.checkBox_preview.AutoSize = true;
            this.checkBox_preview.Checked = true;
            this.checkBox_preview.CheckState = CheckState.Checked;
            this.checkBox_preview.Location = new Point(84, 14);
            this.checkBox_preview.Margin = new Padding(3, 3, 12, 3);
            this.checkBox_preview.Name = "checkBox_preview";
            this.checkBox_preview.Size = new Size(121, 19);
            this.checkBox_preview.TabIndex = 1;
            this.checkBox_preview.Text = "Prehear set note";
            this.checkBox_preview.UseVisualStyleBackColor = true;
            // 
            // label_pitchFrequency
            // 
            this.label_pitchFrequency.AutoSize = true;
            this.label_pitchFrequency.Location = new Point(220, 11);
            this.label_pitchFrequency.Margin = new Padding(3, 8, 3, 0);
            this.label_pitchFrequency.Name = "label_pitchFrequency";
            this.label_pitchFrequency.Size = new Size(77, 15);
            this.label_pitchFrequency.TabIndex = 2;
            this.label_pitchFrequency.Text = "A4 frequency";
            // 
            // numericUpDown_pitchFrequency
            // 
            this.numericUpDown_pitchFrequency.DecimalPlaces = 1;
            this.numericUpDown_pitchFrequency.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
            this.numericUpDown_pitchFrequency.Location = new Point(303, 11);
            this.numericUpDown_pitchFrequency.Maximum = new decimal(new int[] { 1000, 0, 0, 0 });
            this.numericUpDown_pitchFrequency.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numericUpDown_pitchFrequency.Name = "numericUpDown_pitchFrequency";
            this.numericUpDown_pitchFrequency.Size = new Size(70, 23);
            this.numericUpDown_pitchFrequency.TabIndex = 3;
            this.numericUpDown_pitchFrequency.Value = new decimal(new int[] { 440, 0, 0, 0 });
            this.numericUpDown_pitchFrequency.ValueChanged += this.numericUpDown_pitchFrequency_ValueChanged;
            // 
            // flowLayoutPanel_right
            // 
            this.flowLayoutPanel_right.Controls.Add(this.button_save);
            this.flowLayoutPanel_right.Controls.Add(this.button_remix);
            this.flowLayoutPanel_right.Controls.Add(this.button_generate);
            this.flowLayoutPanel_right.Controls.Add(this.button_record);
            this.flowLayoutPanel_right.Dock = DockStyle.Right;
            this.flowLayoutPanel_right.FlowDirection = FlowDirection.RightToLeft;
            this.flowLayoutPanel_right.Location = new Point(614, 0);
            this.flowLayoutPanel_right.Margin = new Padding(0);
            this.flowLayoutPanel_right.Name = "flowLayoutPanel_right";
            this.flowLayoutPanel_right.Padding = new Padding(0, 8, 0, 0);
            this.flowLayoutPanel_right.Size = new Size(360, 39);
            this.flowLayoutPanel_right.TabIndex = 1;
            this.flowLayoutPanel_right.WrapContents = false;
            //
            // button_record
            //
            this.button_record.Location = new Point(3, 11);
            this.button_record.Name = "button_record";
            this.button_record.Size = new Size(60, 23);
            this.button_record.TabIndex = 3;
            this.button_record.Text = "Record";
            this.button_record.UseVisualStyleBackColor = true;
            this.button_record.Click += this.button_record_Click;
            // 
            // button_save
            // 
            this.button_save.Location = new Point(203, 11);
            this.button_save.Name = "button_save";
            this.button_save.Size = new Size(64, 23);
            this.button_save.TabIndex = 0;
            this.button_save.Text = "Save";
            this.button_save.ContextMenuStrip = this.contextMenuStrip_save;
            this.button_save.UseVisualStyleBackColor = true;
            this.button_save.Click += this.button_save_Click;
            // 
            // button_remix
            // 
            this.button_remix.Location = new Point(139, 11);
            this.button_remix.Name = "button_remix";
            this.button_remix.Size = new Size(58, 23);
            this.button_remix.TabIndex = 1;
            this.button_remix.Text = "Remix";
            this.button_remix.UseVisualStyleBackColor = true;
            this.button_remix.Click += this.button_remix_Click;
            // 
            // button_generate
            // 
            this.button_generate.Location = new Point(58, 11);
            this.button_generate.Name = "button_generate";
            this.button_generate.Size = new Size(75, 23);
            this.button_generate.TabIndex = 2;
            this.button_generate.Text = "Generate";
            this.button_generate.UseVisualStyleBackColor = true;
            this.button_generate.Click += this.button_generate_Click;
            //
            // toolTip_editor
            //
            this.toolTip_editor.SetToolTip(this.pictureBox_editor, "Left mouse button: Add notes; right-click a note: Delete it; right-click empty space: Play from Here\r\nShift + left-drag: Select a note/time range for Remix and Generate\r\nCtrl + drag: Pan; Ctrl + mouse wheel: Zoom\r\nCtrl+C/X: Copy/Cut selection; Ctrl+V: Paste ghost (hold for clipboard history)\r\nClick to place a ghost; Esc or right-click cancels");
            this.toolTip_editor.SetToolTip(this.button_record, "Toggle microphone recording: picks up your voice, converts the pitch to MIDI notes via pYIN, stores the recording as a sample in the CustomMidi collection and creates a ghost preview to place. Click again (Stop) to finish.");
            //
            // timer_previewCaret
            //
            this.timer_previewCaret.Interval = 30;
            this.timer_previewCaret.Tick += this.timer_previewCaret_Tick;
            //
            // timer_recording
            //
            this.timer_recording.Interval = 100;
            this.timer_recording.Tick += this.timer_recording_Tick;
            //
            // contextMenuStrip_playFromHere
            //
            this.contextMenuStrip_playFromHere.Items.AddRange(new ToolStripItem[] { this.toolStripMenuItem_playFromHere });
            this.contextMenuStrip_playFromHere.Name = "contextMenuStrip_playFromHere";
            this.contextMenuStrip_playFromHere.Size = new Size(154, 26);
            //
            // toolStripMenuItem_playFromHere
            //
            this.toolStripMenuItem_playFromHere.Name = "toolStripMenuItem_playFromHere";
            this.toolStripMenuItem_playFromHere.Size = new Size(153, 22);
            this.toolStripMenuItem_playFromHere.Text = "Play from Here";
            this.toolStripMenuItem_playFromHere.Click += this.toolStripMenuItem_playFromHere_Click;
            //
            // contextMenuStrip_save
            // 
            this.contextMenuStrip_save.Items.AddRange(new ToolStripItem[] { this.toolStripMenuItem_saveMidi });
            this.contextMenuStrip_save.Name = "contextMenuStrip_save";
            this.contextMenuStrip_save.Size = new Size(181, 26);
            // 
            // toolStripMenuItem_saveMidi
            // 
            this.toolStripMenuItem_saveMidi.Name = "toolStripMenuItem_saveMidi";
            this.toolStripMenuItem_saveMidi.Size = new Size(180, 22);
            this.toolStripMenuItem_saveMidi.Text = "Save as MIDI file...";
            this.toolStripMenuItem_saveMidi.Click += this.toolStripMenuItem_saveMidi_Click;
            // 
            // CustomMidiEditorDialog
            // 
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(900, 540);
            this.Controls.Add(this.tableLayoutPanel_root);
            this.MinimumSize = new Size(760, 420);
            this.Name = "CustomMidiEditorDialog";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Text = "MIDI Editor";
            this.FormClosing += this.CustomMidiEditorDialog_FormClosing;
            this.Resize += this.CustomMidiEditorDialog_Resize;
            this.tableLayoutPanel_root.ResumeLayout(false);
            this.panel_top.ResumeLayout(false);
            this.flowLayoutPanel_top.ResumeLayout(false);
            this.flowLayoutPanel_top.PerformLayout();
            this.flowLayoutPanel_tracks.ResumeLayout(false);
            this.flowLayoutPanel_tracks.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)this.numericUpDown_track).EndInit();
            ((System.ComponentModel.ISupportInitialize)this.numericUpDown_resolution).EndInit();
            ((System.ComponentModel.ISupportInitialize)this.numericUpDown_bpm).EndInit();
            ((System.ComponentModel.ISupportInitialize)this.pictureBox_editor).EndInit();
            this.panel_bottom.ResumeLayout(false);
            this.panel_actions.ResumeLayout(false);
            this.flowLayoutPanel_left.ResumeLayout(false);
            this.flowLayoutPanel_left.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)this.numericUpDown_pitchFrequency).EndInit();
            this.flowLayoutPanel_right.ResumeLayout(false);
            this.contextMenuStrip_playFromHere.ResumeLayout(false);
            this.contextMenuStrip_save.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private TableLayoutPanel tableLayoutPanel_root;
        private Panel panel_top;
        private FlowLayoutPanel flowLayoutPanel_top;
        private FlowLayoutPanel flowLayoutPanel_tracks;
        private Button button_import;
        private Label label_track;
        private NumericUpDown numericUpDown_track;
        private Label label_trackName;
        private Button button_instrument;
        private Label label_instrument;
        private Label label_resolution;
        private NumericUpDown numericUpDown_resolution;
        private Label label_bpm;
        private NumericUpDown numericUpDown_bpm;
        private Label label_minKey;
        private DomainUpDown domainUpDown_minKey;
        private Label label_maxKey;
        private DomainUpDown domainUpDown_maxKey;
        private PictureBox pictureBox_editor;
        private Panel panel_bottom;
        private HScrollBar hScrollBar_editor;
        private Panel panel_actions;
        private FlowLayoutPanel flowLayoutPanel_left;
        private Button button_play;
        private CheckBox checkBox_preview;
        private Label label_pitchFrequency;
        private NumericUpDown numericUpDown_pitchFrequency;
        private FlowLayoutPanel flowLayoutPanel_right;
        private Button button_save;
        private Button button_remix;
        private Button button_generate;
        private Button button_record;
        private ToolTip toolTip_editor;
        private System.Windows.Forms.Timer timer_previewCaret;
        private System.Windows.Forms.Timer timer_recording;
        private ComboBox comboBox_inputDevice;
        private Label label_recordStatus;
        private ProgressBar progressBar_inputLevel;
        private ContextMenuStrip contextMenuStrip_playFromHere;
        private ToolStripMenuItem toolStripMenuItem_playFromHere;
        private ContextMenuStrip contextMenuStrip_save;
        private ToolStripMenuItem toolStripMenuItem_saveMidi;
    }
}
