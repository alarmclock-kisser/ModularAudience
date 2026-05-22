namespace ModularAudience.Forms
{
    partial class WindowMain
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
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
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.button_import = new Button();
            this.button_browse = new Button();
            this.contextMenuStrip_playlist = new ContextMenuStrip(this.components);
            this.toolStripMenuItem_playPause = new ToolStripMenuItem();
            this.toolStripMenuItem_prev = new ToolStripMenuItem();
            this.toolStripMenuItem_skip = new ToolStripMenuItem();
            this.toolStripMenuItem_crossfade = new ToolStripMenuItem();
            this.toolStripMenuItem_crossSyncDuration = new ToolStripMenuItem();
            this.toolStripMenuItem_shuffle = new ToolStripMenuItem();
            this.toolStripMenuItem_clear = new ToolStripMenuItem();
            this.toolStripMenuItem_timestretchEach = new ToolStripMenuItem();
            this.toolStripMenuItem_countdown = new ToolStripMenuItem();
            this.toolTip_playlist = new ToolTip(this.components);
            this.button_scanBpm = new Button();
            this.textBox_scanBpmResult = new TextBox();
            this.textBox_scanTimingResult = new TextBox();
            this.button_scanTiming = new Button();
            this.textBox_scanKeyResult = new TextBox();
            this.button_scanKey = new Button();
            this.button_timeStretch = new Button();
            this.button_export = new Button();
            this.comboBox_exportFormat = new ComboBox();
            this.comboBox_exportBits = new ComboBox();
            this.button_autoSamples = new Button();
            this.button_newBag = new Button();
            this.button_drumRoll = new Button();
            this.button_pianoRoll = new Button();
            this.textBox_info = new TextBox();
            this.button_record = new Button();
            this.textBox_recordingTime = new TextBox();
            this.label_stopRecordInfo = new Label();
            this.button_newTrack = new Button();
            this.button_copyLog = new Button();
            this.listBox_log = new ListBox();
            this.button_breakbeatArchitect = new Button();
            this.button_pitchShift = new Button();
            this.button_applyCloseAll = new Button();
            this.checkBox_structure = new CheckBox();
            this.button_loopControl = new Button();
            this.button_devMode = new Button();
            this.button_cuda = new Button();
            this.checkBox_oneBag = new CheckBox();
            this.button_bringAllToFront = new Button();
            this.button_playlist = new Button();
            this.label_currentlyEnqueued = new Label();
            this.toolStripMenuItem_autoEnqueueOne = new ToolStripMenuItem();
            this.contextMenuStrip_playlist.SuspendLayout();
            this.SuspendLayout();
            // 
            // button_import
            // 
            this.button_import.BackColor = Color.FromArgb(  255,   255,   192);
            this.button_import.Location = new Point(12, 12);
            this.button_import.Name = "button_import";
            this.button_import.Size = new Size(75, 23);
            this.button_import.TabIndex = 0;
            this.button_import.TabStop = false;
            this.button_import.Text = "Import";
            this.button_import.UseVisualStyleBackColor = false;
            this.button_import.Click += this.button_import_Click;
            // 
            // button_browse
            // 
            this.button_browse.Font = new Font("Bahnschrift", 9F, FontStyle.Regular, GraphicsUnit.Point,  0);
            this.button_browse.Location = new Point(93, 12);
            this.button_browse.Name = "button_browse";
            this.button_browse.Size = new Size(32, 23);
            this.button_browse.TabIndex = 1;
            this.button_browse.TabStop = false;
            this.button_browse.Text = "[...]";
            this.button_browse.UseVisualStyleBackColor = true;
            this.button_browse.Click += this.button_browse_Click;
            // 
            // contextMenuStrip_playlist
            // 
            this.contextMenuStrip_playlist.Items.AddRange(new ToolStripItem[] { this.toolStripMenuItem_playPause, this.toolStripMenuItem_prev, this.toolStripMenuItem_skip, this.toolStripMenuItem_crossfade, this.toolStripMenuItem_crossSyncDuration, this.toolStripMenuItem_shuffle, this.toolStripMenuItem_clear, this.toolStripMenuItem_timestretchEach, this.toolStripMenuItem_countdown });
            this.contextMenuStrip_playlist.Name = "contextMenuStrip_playlist";
            this.contextMenuStrip_playlist.Size = new Size(201, 202);
            // 
            // toolStripMenuItem_playPause
            // 
            this.toolStripMenuItem_playPause.Name = "toolStripMenuItem_playPause";
            this.toolStripMenuItem_playPause.Size = new Size(200, 22);
            this.toolStripMenuItem_playPause.Text = "Import Tracks ...";
            this.toolStripMenuItem_playPause.Click += this.playlistMenu_ImportTracks_Click;
            // 
            // toolStripMenuItem_prev
            // 
            this.toolStripMenuItem_prev.Name = "toolStripMenuItem_prev";
            this.toolStripMenuItem_prev.Size = new Size(200, 22);
            this.toolStripMenuItem_prev.Text = "⏮ Rewind / Previous";
            this.toolStripMenuItem_prev.Click += this.playlistMenu_Prev_Click;
            // 
            // toolStripMenuItem_skip
            // 
            this.toolStripMenuItem_skip.Name = "toolStripMenuItem_skip";
            this.toolStripMenuItem_skip.Size = new Size(200, 22);
            this.toolStripMenuItem_skip.Text = "⏭ Skip Track";
            this.toolStripMenuItem_skip.Click += this.playlistMenu_Skip_Click;
            // 
            // toolStripMenuItem_crossfade
            // 
            this.toolStripMenuItem_crossfade.Name = "toolStripMenuItem_crossfade";
            this.toolStripMenuItem_crossfade.Size = new Size(200, 22);
            this.toolStripMenuItem_crossfade.Text = "≋ Crossfade...";
            this.toolStripMenuItem_crossfade.Click += this.toolStripMenuItem_crossfade_Click;
            // 
            // toolStripMenuItem_crossSyncDuration
            // 
            this.toolStripMenuItem_crossSyncDuration.Name = "toolStripMenuItem_crossSyncDuration";
            this.toolStripMenuItem_crossSyncDuration.Size = new Size(200, 22);
            this.toolStripMenuItem_crossSyncDuration.Text = "≋ Cross Sync Duration...";
            this.toolStripMenuItem_crossSyncDuration.Click += this.toolStripMenuItem_crossSyncDuration_Click;
            // 
            // toolStripMenuItem_shuffle
            // 
            this.toolStripMenuItem_shuffle.Name = "toolStripMenuItem_shuffle";
            this.toolStripMenuItem_shuffle.Size = new Size(200, 22);
            this.toolStripMenuItem_shuffle.Text = "🔀 Shuffle Remaining";
            this.toolStripMenuItem_shuffle.Click += this.playlistMenu_Shuffle_Click;
            // 
            // toolStripMenuItem_clear
            // 
            this.toolStripMenuItem_clear.Name = "toolStripMenuItem_clear";
            this.toolStripMenuItem_clear.Size = new Size(200, 22);
            this.toolStripMenuItem_clear.Text = "✖ Clear Playlist";
            this.toolStripMenuItem_clear.Click += this.playlistMenu_Clear_Click;
            // 
            // toolStripMenuItem_timestretchEach
            // 
            this.toolStripMenuItem_timestretchEach.DoubleClickEnabled = true;
            this.toolStripMenuItem_timestretchEach.Name = "toolStripMenuItem_timestretchEach";
            this.toolStripMenuItem_timestretchEach.Size = new Size(200, 22);
            this.toolStripMenuItem_timestretchEach.Text = "⏱ Timestretch each...";
            this.toolStripMenuItem_timestretchEach.Click += this.playlistMenu_TimestretchEach_Click;
            this.toolStripMenuItem_timestretchEach.DoubleClick += this.toolStripMenuItem_timestretchEach_DoubleClick;
            // 
            // toolStripMenuItem_countdown
            // 
            this.toolStripMenuItem_countdown.Checked = true;
            this.toolStripMenuItem_countdown.CheckOnClick = true;
            this.toolStripMenuItem_countdown.CheckState = CheckState.Checked;
            this.toolStripMenuItem_countdown.Name = "toolStripMenuItem_countdown";
            this.toolStripMenuItem_countdown.Size = new Size(200, 22);
            this.toolStripMenuItem_countdown.Text = "Countdown before play";
            this.toolStripMenuItem_countdown.Click += this.playlistMenu_Countdown_Click;
            // 
            // button_scanBpm
            // 
            this.button_scanBpm.Location = new Point(384, 291);
            this.button_scanBpm.Name = "button_scanBpm";
            this.button_scanBpm.Size = new Size(75, 23);
            this.button_scanBpm.TabIndex = 3;
            this.button_scanBpm.TabStop = false;
            this.button_scanBpm.Text = "Scan BPM";
            this.button_scanBpm.UseVisualStyleBackColor = true;
            this.button_scanBpm.Click += this.button_scanBpm_Click;
            // 
            // textBox_scanBpmResult
            // 
            this.textBox_scanBpmResult.Location = new Point(465, 291);
            this.textBox_scanBpmResult.Name = "textBox_scanBpmResult";
            this.textBox_scanBpmResult.PlaceholderText = "0.000 BPM";
            this.textBox_scanBpmResult.ReadOnly = true;
            this.textBox_scanBpmResult.Size = new Size(100, 23);
            this.textBox_scanBpmResult.TabIndex = 4;
            this.textBox_scanBpmResult.TabStop = false;
            // 
            // textBox_scanTimingResult
            // 
            this.textBox_scanTimingResult.Location = new Point(465, 262);
            this.textBox_scanTimingResult.Name = "textBox_scanTimingResult";
            this.textBox_scanTimingResult.PlaceholderText = "1 / 1 Timing";
            this.textBox_scanTimingResult.ReadOnly = true;
            this.textBox_scanTimingResult.Size = new Size(100, 23);
            this.textBox_scanTimingResult.TabIndex = 6;
            this.textBox_scanTimingResult.TabStop = false;
            // 
            // button_scanTiming
            // 
            this.button_scanTiming.Location = new Point(384, 262);
            this.button_scanTiming.Name = "button_scanTiming";
            this.button_scanTiming.Size = new Size(75, 23);
            this.button_scanTiming.TabIndex = 5;
            this.button_scanTiming.TabStop = false;
            this.button_scanTiming.Text = "Scan Time";
            this.button_scanTiming.UseVisualStyleBackColor = true;
            this.button_scanTiming.Click += this.button_scanTiming_Click;
            // 
            // textBox_scanKeyResult
            // 
            this.textBox_scanKeyResult.Location = new Point(465, 233);
            this.textBox_scanKeyResult.Name = "textBox_scanKeyResult";
            this.textBox_scanKeyResult.PlaceholderText = "No key scanned";
            this.textBox_scanKeyResult.ReadOnly = true;
            this.textBox_scanKeyResult.Size = new Size(100, 23);
            this.textBox_scanKeyResult.TabIndex = 8;
            this.textBox_scanKeyResult.TabStop = false;
            // 
            // button_scanKey
            // 
            this.button_scanKey.Location = new Point(384, 233);
            this.button_scanKey.Name = "button_scanKey";
            this.button_scanKey.Size = new Size(75, 23);
            this.button_scanKey.TabIndex = 7;
            this.button_scanKey.TabStop = false;
            this.button_scanKey.Text = "Scan Key";
            this.button_scanKey.UseVisualStyleBackColor = true;
            this.button_scanKey.Click += this.button_scanKey_Click;
            // 
            // button_timeStretch
            // 
            this.button_timeStretch.Location = new Point(192, 233);
            this.button_timeStretch.Name = "button_timeStretch";
            this.button_timeStretch.Size = new Size(90, 23);
            this.button_timeStretch.TabIndex = 9;
            this.button_timeStretch.TabStop = false;
            this.button_timeStretch.Text = "Time Stretch";
            this.button_timeStretch.UseVisualStyleBackColor = true;
            this.button_timeStretch.Click += this.button_timeStretch_Click;
            // 
            // button_export
            // 
            this.button_export.BackColor = Color.FromArgb(  192,   255,   255);
            this.button_export.Location = new Point(12, 228);
            this.button_export.Name = "button_export";
            this.button_export.Size = new Size(51, 23);
            this.button_export.TabIndex = 10;
            this.button_export.TabStop = false;
            this.button_export.Text = "Export";
            this.button_export.UseVisualStyleBackColor = false;
            this.button_export.Click += this.button_export_Click;
            // 
            // comboBox_exportFormat
            // 
            this.comboBox_exportFormat.DropDownStyle = ComboBoxStyle.DropDownList;
            this.comboBox_exportFormat.FormattingEnabled = true;
            this.comboBox_exportFormat.Location = new Point(69, 229);
            this.comboBox_exportFormat.Name = "comboBox_exportFormat";
            this.comboBox_exportFormat.Size = new Size(56, 23);
            this.comboBox_exportFormat.TabIndex = 11;
            this.comboBox_exportFormat.TabStop = false;
            this.comboBox_exportFormat.SelectedIndexChanged += this.comboBox_exportFormat_SelectedIndexChanged;
            // 
            // comboBox_exportBits
            // 
            this.comboBox_exportBits.DropDownStyle = ComboBoxStyle.DropDownList;
            this.comboBox_exportBits.FormattingEnabled = true;
            this.comboBox_exportBits.Location = new Point(131, 229);
            this.comboBox_exportBits.Name = "comboBox_exportBits";
            this.comboBox_exportBits.Size = new Size(55, 23);
            this.comboBox_exportBits.TabIndex = 12;
            this.comboBox_exportBits.TabStop = false;
            this.comboBox_exportBits.SelectedIndexChanged += this.comboBox_exportBits_SelectedIndexChanged;
            // 
            // button_autoSamples
            // 
            this.button_autoSamples.Location = new Point(288, 261);
            this.button_autoSamples.Name = "button_autoSamples";
            this.button_autoSamples.Size = new Size(90, 23);
            this.button_autoSamples.TabIndex = 13;
            this.button_autoSamples.TabStop = false;
            this.button_autoSamples.Text = "Auto Samples";
            this.button_autoSamples.UseVisualStyleBackColor = true;
            this.button_autoSamples.Click += this.button_autoSamples_Click;
            // 
            // button_newBag
            // 
            this.button_newBag.BackColor = Color.FromArgb(  192,   255,   192);
            this.button_newBag.Font = new Font("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point,  0);
            this.button_newBag.Location = new Point(69, 90);
            this.button_newBag.Name = "button_newBag";
            this.button_newBag.Size = new Size(51, 23);
            this.button_newBag.TabIndex = 14;
            this.button_newBag.TabStop = false;
            this.button_newBag.Text = "+ Bag";
            this.button_newBag.UseVisualStyleBackColor = false;
            this.button_newBag.Click += this.button_newBag_Click;
            // 
            // button_drumRoll
            // 
            this.button_drumRoll.Enabled = false;
            this.button_drumRoll.Location = new Point(192, 291);
            this.button_drumRoll.Name = "button_drumRoll";
            this.button_drumRoll.Size = new Size(90, 23);
            this.button_drumRoll.TabIndex = 15;
            this.button_drumRoll.TabStop = false;
            this.button_drumRoll.Text = "Drum Roll";
            this.button_drumRoll.UseVisualStyleBackColor = true;
            this.button_drumRoll.Click += this.button_drumRoll_Click;
            // 
            // button_pianoRoll
            // 
            this.button_pianoRoll.Location = new Point(149, 99);
            this.button_pianoRoll.Name = "button_pianoRoll";
            this.button_pianoRoll.Size = new Size(90, 23);
            this.button_pianoRoll.TabIndex = 16;
            this.button_pianoRoll.TabStop = false;
            this.button_pianoRoll.Text = "Piano Roll";
            this.button_pianoRoll.UseVisualStyleBackColor = true;
            this.button_pianoRoll.Click += this.button_pianoRoll_Click;
            // 
            // textBox_info
            // 
            this.textBox_info.Font = new Font("Bahnschrift", 9F, FontStyle.Regular, GraphicsUnit.Point,  0);
            this.textBox_info.Location = new Point(384, 17);
            this.textBox_info.Multiline = true;
            this.textBox_info.Name = "textBox_info";
            this.textBox_info.PlaceholderText = "No track currently selected.";
            this.textBox_info.ReadOnly = true;
            this.textBox_info.Size = new Size(181, 210);
            this.textBox_info.TabIndex = 17;
            this.textBox_info.TabStop = false;
            // 
            // button_record
            // 
            this.button_record.ForeColor = Color.Black;
            this.button_record.Location = new Point(12, 181);
            this.button_record.Name = "button_record";
            this.button_record.Size = new Size(23, 23);
            this.button_record.TabIndex = 18;
            this.button_record.TabStop = false;
            this.button_record.Text = "●";
            this.button_record.UseVisualStyleBackColor = true;
            this.button_record.Click += this.button_record_Click;
            // 
            // textBox_recordingTime
            // 
            this.textBox_recordingTime.Location = new Point(45, 181);
            this.textBox_recordingTime.Name = "textBox_recordingTime";
            this.textBox_recordingTime.PlaceholderText = "Not recording";
            this.textBox_recordingTime.Size = new Size(80, 23);
            this.textBox_recordingTime.TabIndex = 19;
            this.textBox_recordingTime.TabStop = false;
            // 
            // label_stopRecordInfo
            // 
            this.label_stopRecordInfo.AutoSize = true;
            this.label_stopRecordInfo.Font = new Font("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point,  0);
            this.label_stopRecordInfo.ForeColor = Color.Red;
            this.label_stopRecordInfo.Location = new Point(12, 207);
            this.label_stopRecordInfo.Name = "label_stopRecordInfo";
            this.label_stopRecordInfo.Size = new Size(114, 13);
            this.label_stopRecordInfo.TabIndex = 20;
            this.label_stopRecordInfo.Text = "Ctrl-Click to stop rec.";
            this.label_stopRecordInfo.Visible = false;
            // 
            // button_newTrack
            // 
            this.button_newTrack.BackColor = Color.FromArgb(  192,   192,   255);
            this.button_newTrack.Font = new Font("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point,  0);
            this.button_newTrack.Location = new Point(12, 90);
            this.button_newTrack.Name = "button_newTrack";
            this.button_newTrack.Size = new Size(51, 23);
            this.button_newTrack.TabIndex = 21;
            this.button_newTrack.Text = "+ Track";
            this.button_newTrack.UseVisualStyleBackColor = false;
            this.button_newTrack.Click += this.button_newTrack_Click;
            // 
            // button_copyLog
            // 
            this.button_copyLog.Font = new Font("Bahnschrift SemiLight Condensed", 8.25F, FontStyle.Regular, GraphicsUnit.Point,  0);
            this.button_copyLog.Location = new Point(74, 152);
            this.button_copyLog.Name = "button_copyLog";
            this.button_copyLog.Size = new Size(51, 23);
            this.button_copyLog.TabIndex = 34;
            this.button_copyLog.TabStop = false;
            this.button_copyLog.Text = "Copy Log";
            this.button_copyLog.UseVisualStyleBackColor = true;
            this.button_copyLog.Click += this.button_copyLog_Click;
            // 
            // listBox_log
            // 
            this.listBox_log.Font = new Font("Bahnschrift Light SemiCondensed", 8.25F, FontStyle.Regular, GraphicsUnit.Point,  0);
            this.listBox_log.FormattingEnabled = true;
            this.listBox_log.HorizontalScrollbar = true;
            this.listBox_log.Location = new Point(131, 9);
            this.listBox_log.Name = "listBox_log";
            this.listBox_log.Size = new Size(247, 212);
            this.listBox_log.TabIndex = 22;
            this.listBox_log.TabStop = false;
            this.listBox_log.MouseClick += this.listBox_log_Click;
            this.listBox_log.DoubleClick += this.listBox_log_DoubleClick;
            // 
            // button_breakbeatArchitect
            // 
            this.button_breakbeatArchitect.Location = new Point(288, 291);
            this.button_breakbeatArchitect.Name = "button_breakbeatArchitect";
            this.button_breakbeatArchitect.Size = new Size(90, 23);
            this.button_breakbeatArchitect.TabIndex = 23;
            this.button_breakbeatArchitect.TabStop = false;
            this.button_breakbeatArchitect.Text = "Break Beat";
            this.button_breakbeatArchitect.UseVisualStyleBackColor = true;
            this.button_breakbeatArchitect.Click += this.button_breakbeatArchitect_Click;
            // 
            // button_pitchShift
            // 
            this.button_pitchShift.Location = new Point(288, 234);
            this.button_pitchShift.Name = "button_pitchShift";
            this.button_pitchShift.Size = new Size(90, 23);
            this.button_pitchShift.TabIndex = 24;
            this.button_pitchShift.TabStop = false;
            this.button_pitchShift.Text = "Pitch Shift";
            this.button_pitchShift.UseVisualStyleBackColor = true;
            this.button_pitchShift.Click += this.button_pitchShift_Click;
            // 
            // button_applyCloseAll
            // 
            this.button_applyCloseAll.BackColor = Color.FromArgb(  255,   192,   255);
            this.button_applyCloseAll.Location = new Point(12, 286);
            this.button_applyCloseAll.Name = "button_applyCloseAll";
            this.button_applyCloseAll.Size = new Size(113, 23);
            this.button_applyCloseAll.TabIndex = 25;
            this.button_applyCloseAll.TabStop = false;
            this.button_applyCloseAll.Text = "Apply + Close All";
            this.button_applyCloseAll.UseVisualStyleBackColor = false;
            this.button_applyCloseAll.Click += this.button_applyCloseAll_Click;
            // 
            // checkBox_structure
            // 
            this.checkBox_structure.AutoSize = true;
            this.checkBox_structure.Checked = true;
            this.checkBox_structure.CheckState = CheckState.Checked;
            this.checkBox_structure.Location = new Point(12, 41);
            this.checkBox_structure.Name = "checkBox_structure";
            this.checkBox_structure.Size = new Size(103, 19);
            this.checkBox_structure.TabIndex = 26;
            this.checkBox_structure.Text = "Keep Structure";
            this.checkBox_structure.UseVisualStyleBackColor = true;
            // 
            // button_loopControl
            // 
            this.button_loopControl.Location = new Point(192, 261);
            this.button_loopControl.Name = "button_loopControl";
            this.button_loopControl.Size = new Size(90, 23);
            this.button_loopControl.TabIndex = 27;
            this.button_loopControl.Text = "Loop Control";
            this.button_loopControl.UseVisualStyleBackColor = true;
            this.button_loopControl.Click += this.button_loopControl_Click;
            // 
            // button_devMode
            // 
            this.button_devMode.BackColor = Color.DarkGray;
            this.button_devMode.Location = new Point(131, 286);
            this.button_devMode.Name = "button_devMode";
            this.button_devMode.Size = new Size(55, 23);
            this.button_devMode.TabIndex = 28;
            this.button_devMode.Text = "DEV";
            this.button_devMode.UseVisualStyleBackColor = false;
            this.button_devMode.Click += this.button_devMode_Click;
            // 
            // button_cuda
            // 
            this.button_cuda.Location = new Point(131, 258);
            this.button_cuda.Name = "button_cuda";
            this.button_cuda.Size = new Size(55, 23);
            this.button_cuda.TabIndex = 29;
            this.button_cuda.Text = "CUDA";
            this.button_cuda.UseVisualStyleBackColor = true;
            this.button_cuda.Click += this.button_cuda_Click;
            // 
            // checkBox_oneBag
            // 
            this.checkBox_oneBag.AutoSize = true;
            this.checkBox_oneBag.Location = new Point(12, 66);
            this.checkBox_oneBag.Name = "checkBox_oneBag";
            this.checkBox_oneBag.Size = new Size(103, 19);
            this.checkBox_oneBag.TabIndex = 30;
            this.checkBox_oneBag.Text = "All-in-one bag";
            this.checkBox_oneBag.UseVisualStyleBackColor = true;
            this.checkBox_oneBag.CheckedChanged += this.checkBox_oneBag_CheckedChanged;
            // 
            // button_bringAllToFront
            // 
            this.button_bringAllToFront.BackColor = Color.CornflowerBlue;
            this.button_bringAllToFront.Location = new Point(12, 258);
            this.button_bringAllToFront.Name = "button_bringAllToFront";
            this.button_bringAllToFront.Size = new Size(113, 23);
            this.button_bringAllToFront.TabIndex = 31;
            this.button_bringAllToFront.TabStop = false;
            this.button_bringAllToFront.Text = "Bring all to Front";
            this.button_bringAllToFront.UseVisualStyleBackColor = false;
            this.button_bringAllToFront.Click += this.button_bringAllToFront_Click;
            // 
            // button_playlist
            // 
            this.button_playlist.BackColor = Color.FromArgb(  255,   224,   192);
            this.button_playlist.ContextMenuStrip = this.contextMenuStrip_playlist;
            this.button_playlist.Font = new Font("Segoe UI Semilight", 8.25F, FontStyle.Regular, GraphicsUnit.Point,  0);
            this.button_playlist.Location = new Point(12, 119);
            this.button_playlist.Name = "button_playlist";
            this.button_playlist.Size = new Size(51, 23);
            this.button_playlist.TabIndex = 32;
            this.button_playlist.TabStop = false;
            this.button_playlist.Text = "▶ List";
            this.button_playlist.UseVisualStyleBackColor = false;
            this.button_playlist.Click += this.button_playlist_TogglePlayPause_Click;
            this.button_playlist.MouseHover += this.button_playlist_MouseHover;
            // 
            // label_currentlyEnqueued
            // 
            this.label_currentlyEnqueued.AutoSize = true;
            this.label_currentlyEnqueued.Font = new Font("Bahnschrift Light SemiCondensed", 8.25F, FontStyle.Regular, GraphicsUnit.Point,  0);
            this.label_currentlyEnqueued.Location = new Point(-1, 312);
            this.label_currentlyEnqueued.Name = "label_currentlyEnqueued";
            this.label_currentlyEnqueued.Size = new Size(174, 13);
            this.label_currentlyEnqueued.TabIndex = 33;
            this.label_currentlyEnqueued.Text = "No track currently enqueued in playlist.";
            // 
            // toolStripMenuItem_autoEnqueueOne
            // 
            this.toolStripMenuItem_autoEnqueueOne.Name = "toolStripMenuItem_autoEnqueueOne";
            this.toolStripMenuItem_autoEnqueueOne.Size = new Size(200, 22);
            this.toolStripMenuItem_autoEnqueueOne.Text = "Auto enqueue one";
            this.toolStripMenuItem_autoEnqueueOne.Click += this.playlistMenu_AutoEnqueueOne_Click;
            // 
            // WindowMain
            // 
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(584, 326);
            this.Controls.Add(this.button_copyLog);
            this.Controls.Add(this.label_currentlyEnqueued);
            this.Controls.Add(this.button_playlist);
            this.Controls.Add(this.button_bringAllToFront);
            this.Controls.Add(this.checkBox_oneBag);
            this.Controls.Add(this.button_cuda);
            this.Controls.Add(this.button_devMode);
            this.Controls.Add(this.button_loopControl);
            this.Controls.Add(this.checkBox_structure);
            this.Controls.Add(this.button_applyCloseAll);
            this.Controls.Add(this.button_pitchShift);
            this.Controls.Add(this.button_breakbeatArchitect);
            this.Controls.Add(this.listBox_log);
            this.Controls.Add(this.button_newTrack);
            this.Controls.Add(this.label_stopRecordInfo);
            this.Controls.Add(this.textBox_recordingTime);
            this.Controls.Add(this.button_record);
            this.Controls.Add(this.textBox_info);
            this.Controls.Add(this.button_drumRoll);
            this.Controls.Add(this.button_newBag);
            this.Controls.Add(this.button_autoSamples);
            this.Controls.Add(this.comboBox_exportBits);
            this.Controls.Add(this.comboBox_exportFormat);
            this.Controls.Add(this.button_export);
            this.Controls.Add(this.button_timeStretch);
            this.Controls.Add(this.textBox_scanKeyResult);
            this.Controls.Add(this.button_scanKey);
            this.Controls.Add(this.textBox_scanTimingResult);
            this.Controls.Add(this.button_scanTiming);
            this.Controls.Add(this.textBox_scanBpmResult);
            this.Controls.Add(this.button_scanBpm);
            this.Controls.Add(this.button_browse);
            this.Controls.Add(this.button_import);
            this.MaximumSize = new Size(600, 365);
            this.MinimumSize = new Size(600, 365);
            this.Name = "WindowMain";
            this.Text = "ModularAudience (Main Control)";
            this.contextMenuStrip_playlist.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private Button button_import;
        private Button button_browse;
        private Button button_scanBpm;
        private TextBox textBox_scanBpmResult;
        private TextBox textBox_scanTimingResult;
        private Button button_scanTiming;
        private TextBox textBox_scanKeyResult;
        private Button button_scanKey;
        private Button button_timeStretch;
        private Button button_export;
        private ComboBox comboBox_exportFormat;
        private ComboBox comboBox_exportBits;
        private Button button_autoSamples;
        private Button button_newBag;
        private Button button_drumRoll;
        private Button button_pianoRoll;
        private TextBox textBox_info;
        private Button button_record;
        private TextBox textBox_recordingTime;
        private Label label_stopRecordInfo;
        private Button button_newTrack;
        private Button button_copyLog;
        private ListBox listBox_log;
        private Button button_breakbeatArchitect;
        private Button button_pitchShift;
		private Button button_applyCloseAll;
		private CheckBox checkBox_structure;
        private Button button_loopControl;
        private Button button_devMode;
		private Button button_cuda;
        private CheckBox checkBox_oneBag;
        private Button button_bringAllToFront;
        private Button button_playlist;
        private Label label_currentlyEnqueued;
        private ContextMenuStrip contextMenuStrip_playlist;
        private ToolStripMenuItem toolStripMenuItem_autoEnqueueOne;
        private ToolStripMenuItem toolStripMenuItem_countdown;
        private ToolStripMenuItem toolStripMenuItem_playPause;
        private ToolStripMenuItem toolStripMenuItem_prev;
        private ToolStripMenuItem toolStripMenuItem_skip;
        private ToolStripMenuItem toolStripMenuItem_shuffle;
        private ToolStripMenuItem toolStripMenuItem_clear;
        private ToolStripMenuItem toolStripMenuItem_timestretchEach;
        private ToolTip toolTip_playlist;
        private ToolStripMenuItem toolStripMenuItem_crossfade;
        private ToolStripMenuItem toolStripMenuItem_crossSyncDuration;
    }
}
