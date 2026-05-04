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
            button_import = new Button();
            button_browse = new Button();
            button_scanBpm = new Button();
            textBox_scanBpmResult = new TextBox();
            textBox_scanTimingResult = new TextBox();
            button_scanTiming = new Button();
            textBox_scanKeyResult = new TextBox();
            button_scanKey = new Button();
            button_timeStretch = new Button();
            button_export = new Button();
            comboBox_exportFormat = new ComboBox();
            comboBox_exportBits = new ComboBox();
            button_autoSamples = new Button();
            button_newBag = new Button();
            button_drumRoll = new Button();
            button_pianoRoll = new Button();
            textBox_info = new TextBox();
            button_record = new Button();
            textBox_recordingTime = new TextBox();
            label_stopRecordInfo = new Label();
            button_newTrack = new Button();
            listBox_log = new ListBox();
            button_breakbeatArchitect = new Button();
            button_pitchShift = new Button();
            button_applyCloseAll = new Button();
            checkBox_structure = new CheckBox();
            button_loopControl = new Button();
            button_devMode = new Button();
            button_cuda = new Button();
            checkBox_oneBag = new CheckBox();
            button_bringAllToFront = new Button();
            SuspendLayout();
            // 
            // button_import
            // 
            button_import.BackColor = Color.FromArgb(255, 255, 192);
            button_import.Location = new Point(12, 12);
            button_import.Name = "button_import";
            button_import.Size = new Size(75, 23);
            button_import.TabIndex = 0;
            button_import.TabStop = false;
            button_import.Text = "Import";
            button_import.UseVisualStyleBackColor = false;
            button_import.Click += button_import_Click;
            // 
            // button_browse
            // 
            button_browse.Font = new Font("Bahnschrift", 9F, FontStyle.Regular, GraphicsUnit.Point, 0);
            button_browse.Location = new Point(93, 12);
            button_browse.Name = "button_browse";
            button_browse.Size = new Size(32, 23);
            button_browse.TabIndex = 1;
            button_browse.TabStop = false;
            button_browse.Text = "[...]";
            button_browse.UseVisualStyleBackColor = true;
            button_browse.Click += button_browse_Click;
            // 
            // button_scanBpm
            // 
            button_scanBpm.Location = new Point(511, 286);
            button_scanBpm.Name = "button_scanBpm";
            button_scanBpm.Size = new Size(75, 23);
            button_scanBpm.TabIndex = 3;
            button_scanBpm.TabStop = false;
            button_scanBpm.Text = "Scan BPM";
            button_scanBpm.UseVisualStyleBackColor = true;
            button_scanBpm.Click += button_scanBpm_Click;
            // 
            // textBox_scanBpmResult
            // 
            textBox_scanBpmResult.Location = new Point(592, 286);
            textBox_scanBpmResult.Name = "textBox_scanBpmResult";
            textBox_scanBpmResult.PlaceholderText = "0.000 BPM";
            textBox_scanBpmResult.ReadOnly = true;
            textBox_scanBpmResult.Size = new Size(100, 23);
            textBox_scanBpmResult.TabIndex = 4;
            textBox_scanBpmResult.TabStop = false;
            // 
            // textBox_scanTimingResult
            // 
            textBox_scanTimingResult.Location = new Point(592, 257);
            textBox_scanTimingResult.Name = "textBox_scanTimingResult";
            textBox_scanTimingResult.PlaceholderText = "1 / 1 Timing";
            textBox_scanTimingResult.ReadOnly = true;
            textBox_scanTimingResult.Size = new Size(100, 23);
            textBox_scanTimingResult.TabIndex = 6;
            textBox_scanTimingResult.TabStop = false;
            // 
            // button_scanTiming
            // 
            button_scanTiming.Location = new Point(511, 257);
            button_scanTiming.Name = "button_scanTiming";
            button_scanTiming.Size = new Size(75, 23);
            button_scanTiming.TabIndex = 5;
            button_scanTiming.TabStop = false;
            button_scanTiming.Text = "Scan Time";
            button_scanTiming.UseVisualStyleBackColor = true;
            button_scanTiming.Click += button_scanTiming_Click;
            // 
            // textBox_scanKeyResult
            // 
            textBox_scanKeyResult.Location = new Point(592, 228);
            textBox_scanKeyResult.Name = "textBox_scanKeyResult";
            textBox_scanKeyResult.PlaceholderText = "No key scanned";
            textBox_scanKeyResult.ReadOnly = true;
            textBox_scanKeyResult.Size = new Size(100, 23);
            textBox_scanKeyResult.TabIndex = 8;
            textBox_scanKeyResult.TabStop = false;
            // 
            // button_scanKey
            // 
            button_scanKey.Location = new Point(511, 228);
            button_scanKey.Name = "button_scanKey";
            button_scanKey.Size = new Size(75, 23);
            button_scanKey.TabIndex = 7;
            button_scanKey.TabStop = false;
            button_scanKey.Text = "Scan Key";
            button_scanKey.UseVisualStyleBackColor = true;
            button_scanKey.Click += button_scanKey_Click;
            // 
            // button_timeStretch
            // 
            button_timeStretch.Location = new Point(149, 12);
            button_timeStretch.Name = "button_timeStretch";
            button_timeStretch.Size = new Size(90, 23);
            button_timeStretch.TabIndex = 9;
            button_timeStretch.TabStop = false;
            button_timeStretch.Text = "Time Stretch";
            button_timeStretch.UseVisualStyleBackColor = true;
            button_timeStretch.Click += button_timeStretch_Click;
            // 
            // button_export
            // 
            button_export.BackColor = Color.FromArgb(192, 255, 255);
            button_export.Location = new Point(12, 228);
            button_export.Name = "button_export";
            button_export.Size = new Size(75, 23);
            button_export.TabIndex = 10;
            button_export.TabStop = false;
            button_export.Text = "Export";
            button_export.UseVisualStyleBackColor = false;
            button_export.Click += button_export_Click;
            // 
            // comboBox_exportFormat
            // 
            comboBox_exportFormat.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBox_exportFormat.FormattingEnabled = true;
            comboBox_exportFormat.Location = new Point(93, 229);
            comboBox_exportFormat.Name = "comboBox_exportFormat";
            comboBox_exportFormat.Size = new Size(80, 23);
            comboBox_exportFormat.TabIndex = 11;
            comboBox_exportFormat.TabStop = false;
            comboBox_exportFormat.SelectedIndexChanged += comboBox_exportFormat_SelectedIndexChanged;
            // 
            // comboBox_exportBits
            // 
            comboBox_exportBits.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBox_exportBits.FormattingEnabled = true;
            comboBox_exportBits.Location = new Point(179, 229);
            comboBox_exportBits.Name = "comboBox_exportBits";
            comboBox_exportBits.Size = new Size(60, 23);
            comboBox_exportBits.TabIndex = 12;
            comboBox_exportBits.TabStop = false;
            comboBox_exportBits.SelectedIndexChanged += comboBox_exportBits_SelectedIndexChanged;
            // 
            // button_autoSamples
            // 
            button_autoSamples.Location = new Point(149, 41);
            button_autoSamples.Name = "button_autoSamples";
            button_autoSamples.Size = new Size(90, 23);
            button_autoSamples.TabIndex = 13;
            button_autoSamples.TabStop = false;
            button_autoSamples.Text = "Auto Samples";
            button_autoSamples.UseVisualStyleBackColor = true;
            button_autoSamples.Click += button_autoSamples_Click;
            // 
            // button_newBag
            // 
            button_newBag.BackColor = Color.FromArgb(192, 255, 192);
            button_newBag.Location = new Point(12, 120);
            button_newBag.Name = "button_newBag";
            button_newBag.Size = new Size(75, 23);
            button_newBag.TabIndex = 14;
            button_newBag.TabStop = false;
            button_newBag.Text = "New Bag";
            button_newBag.UseVisualStyleBackColor = false;
            button_newBag.Click += button_newBag_Click;
            // 
            // button_drumRoll
            // 
            button_drumRoll.Enabled = false;
            button_drumRoll.Location = new Point(149, 70);
            button_drumRoll.Name = "button_drumRoll";
            button_drumRoll.Size = new Size(90, 23);
            button_drumRoll.TabIndex = 15;
            button_drumRoll.TabStop = false;
            button_drumRoll.Text = "Drum Roll";
            button_drumRoll.UseVisualStyleBackColor = true;
            button_drumRoll.Click += button_drumRoll_Click;
            // 
            // button_pianoRoll
            // 
            button_pianoRoll.Location = new Point(149, 99);
            button_pianoRoll.Name = "button_pianoRoll";
            button_pianoRoll.Size = new Size(90, 23);
            button_pianoRoll.TabIndex = 16;
            button_pianoRoll.TabStop = false;
            button_pianoRoll.Text = "Piano Roll";
            button_pianoRoll.UseVisualStyleBackColor = true;
            button_pianoRoll.Click += button_pianoRoll_Click;
            // 
            // textBox_info
            // 
            textBox_info.Font = new Font("Bahnschrift", 9F, FontStyle.Regular, GraphicsUnit.Point, 0);
            textBox_info.Location = new Point(511, 12);
            textBox_info.Multiline = true;
            textBox_info.Name = "textBox_info";
            textBox_info.PlaceholderText = "No track currently selected.";
            textBox_info.ReadOnly = true;
            textBox_info.Size = new Size(181, 210);
            textBox_info.TabIndex = 17;
            textBox_info.TabStop = false;
            // 
            // button_record
            // 
            button_record.ForeColor = Color.Black;
            button_record.Location = new Point(12, 199);
            button_record.Name = "button_record";
            button_record.Size = new Size(23, 23);
            button_record.TabIndex = 18;
            button_record.TabStop = false;
            button_record.Text = "●";
            button_record.UseVisualStyleBackColor = true;
            button_record.Click += button_record_Click;
            // 
            // textBox_recordingTime
            // 
            textBox_recordingTime.Location = new Point(93, 199);
            textBox_recordingTime.Name = "textBox_recordingTime";
            textBox_recordingTime.PlaceholderText = "Not recording";
            textBox_recordingTime.Size = new Size(80, 23);
            textBox_recordingTime.TabIndex = 19;
            textBox_recordingTime.TabStop = false;
            // 
            // label_stopRecordInfo
            // 
            label_stopRecordInfo.AutoSize = true;
            label_stopRecordInfo.ForeColor = Color.Red;
            label_stopRecordInfo.Location = new Point(12, 181);
            label_stopRecordInfo.Name = "label_stopRecordInfo";
            label_stopRecordInfo.Size = new Size(154, 15);
            label_stopRecordInfo.TabIndex = 20;
            label_stopRecordInfo.Text = "Ctrl-Click to stop recording.";
            label_stopRecordInfo.Visible = false;
            // 
            // button_newTrack
            // 
            button_newTrack.BackColor = Color.FromArgb(192, 192, 255);
            button_newTrack.Location = new Point(12, 91);
            button_newTrack.Name = "button_newTrack";
            button_newTrack.Size = new Size(75, 23);
            button_newTrack.TabIndex = 21;
            button_newTrack.Text = "New Track";
            button_newTrack.UseVisualStyleBackColor = false;
            button_newTrack.Click += button_newTrack_Click;
            // 
            // listBox_log
            // 
            listBox_log.Font = new Font("Bahnschrift SemiLight", 8.25F, FontStyle.Regular, GraphicsUnit.Point, 0);
            listBox_log.FormattingEnabled = true;
            listBox_log.HorizontalScrollbar = true;
            listBox_log.Location = new Point(245, 175);
            listBox_log.Name = "listBox_log";
            listBox_log.Size = new Size(260, 134);
            listBox_log.TabIndex = 22;
            listBox_log.TabStop = false;
            listBox_log.DoubleClick += listBox_log_DoubleClick;
            // 
            // button_breakbeatArchitect
            // 
            button_breakbeatArchitect.Location = new Point(245, 70);
            button_breakbeatArchitect.Name = "button_breakbeatArchitect";
            button_breakbeatArchitect.Size = new Size(90, 23);
            button_breakbeatArchitect.TabIndex = 23;
            button_breakbeatArchitect.TabStop = false;
            button_breakbeatArchitect.Text = "Break Beat";
            button_breakbeatArchitect.UseVisualStyleBackColor = true;
            button_breakbeatArchitect.Click += button_breakbeatArchitect_Click;
            // 
            // button_pitchShift
            // 
            button_pitchShift.Location = new Point(245, 41);
            button_pitchShift.Name = "button_pitchShift";
            button_pitchShift.Size = new Size(90, 23);
            button_pitchShift.TabIndex = 24;
            button_pitchShift.TabStop = false;
            button_pitchShift.Text = "Pitch Shift";
            button_pitchShift.UseVisualStyleBackColor = true;
            button_pitchShift.Click += button_pitchShift_Click;
            // 
            // button_applyCloseAll
            // 
            button_applyCloseAll.BackColor = Color.FromArgb(255, 192, 255);
            button_applyCloseAll.Location = new Point(12, 286);
            button_applyCloseAll.Name = "button_applyCloseAll";
            button_applyCloseAll.Size = new Size(113, 23);
            button_applyCloseAll.TabIndex = 25;
            button_applyCloseAll.TabStop = false;
            button_applyCloseAll.Text = "Apply + Close All";
            button_applyCloseAll.UseVisualStyleBackColor = false;
            button_applyCloseAll.Click += button_applyCloseAll_Click;
            // 
            // checkBox_structure
            // 
            checkBox_structure.AutoSize = true;
            checkBox_structure.Checked = true;
            checkBox_structure.CheckState = CheckState.Checked;
            checkBox_structure.Location = new Point(12, 41);
            checkBox_structure.Name = "checkBox_structure";
            checkBox_structure.Size = new Size(103, 19);
            checkBox_structure.TabIndex = 26;
            checkBox_structure.Text = "Keep Structure";
            checkBox_structure.UseVisualStyleBackColor = true;
            // 
            // button_loopControl
            // 
            button_loopControl.Location = new Point(245, 12);
            button_loopControl.Name = "button_loopControl";
            button_loopControl.Size = new Size(90, 23);
            button_loopControl.TabIndex = 27;
            button_loopControl.Text = "Loop Control";
            button_loopControl.UseVisualStyleBackColor = true;
            button_loopControl.Click += button_loopControl_Click;
            // 
            // button_devMode
            // 
            button_devMode.BackColor = Color.DarkGray;
            button_devMode.Location = new Point(131, 286);
            button_devMode.Name = "button_devMode";
            button_devMode.Size = new Size(108, 23);
            button_devMode.TabIndex = 28;
            button_devMode.Text = "Dev Mode";
            button_devMode.UseVisualStyleBackColor = false;
            button_devMode.Click += button_devMode_Click;
            // 
            // button_cuda
            // 
            button_cuda.Location = new Point(415, 12);
            button_cuda.Name = "button_cuda";
            button_cuda.Size = new Size(90, 23);
            button_cuda.TabIndex = 29;
            button_cuda.Text = "CUDA";
            button_cuda.UseVisualStyleBackColor = true;
            button_cuda.Click += button_cuda_Click;
            // 
            // checkBox_oneBag
            // 
            checkBox_oneBag.AutoSize = true;
            checkBox_oneBag.Location = new Point(12, 66);
            checkBox_oneBag.Name = "checkBox_oneBag";
            checkBox_oneBag.Size = new Size(103, 19);
            checkBox_oneBag.TabIndex = 30;
            checkBox_oneBag.Text = "All-in-one bag";
            checkBox_oneBag.UseVisualStyleBackColor = true;
            checkBox_oneBag.CheckedChanged += checkBox_oneBag_CheckedChanged;
            // 
            // button_bringAllToFront
            // 
            button_bringAllToFront.BackColor = Color.CornflowerBlue;
            button_bringAllToFront.Location = new Point(12, 258);
            button_bringAllToFront.Name = "button_bringAllToFront";
            button_bringAllToFront.Size = new Size(113, 23);
            button_bringAllToFront.TabIndex = 31;
            button_bringAllToFront.TabStop = false;
            button_bringAllToFront.Text = "Bring all to Front";
            button_bringAllToFront.UseVisualStyleBackColor = false;
            button_bringAllToFront.Click += button_bringAllToFront_Click;
            // 
            // WindowMain
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(704, 321);
            Controls.Add(button_bringAllToFront);
            Controls.Add(checkBox_oneBag);
            Controls.Add(button_cuda);
            Controls.Add(button_devMode);
            Controls.Add(button_loopControl);
            Controls.Add(checkBox_structure);
            Controls.Add(button_applyCloseAll);
            Controls.Add(button_pitchShift);
            Controls.Add(button_breakbeatArchitect);
            Controls.Add(listBox_log);
            Controls.Add(button_newTrack);
            Controls.Add(label_stopRecordInfo);
            Controls.Add(textBox_recordingTime);
            Controls.Add(button_record);
            Controls.Add(textBox_info);
            Controls.Add(button_drumRoll);
            Controls.Add(button_newBag);
            Controls.Add(button_autoSamples);
            Controls.Add(comboBox_exportBits);
            Controls.Add(comboBox_exportFormat);
            Controls.Add(button_export);
            Controls.Add(button_timeStretch);
            Controls.Add(textBox_scanKeyResult);
            Controls.Add(button_scanKey);
            Controls.Add(textBox_scanTimingResult);
            Controls.Add(button_scanTiming);
            Controls.Add(textBox_scanBpmResult);
            Controls.Add(button_scanBpm);
            Controls.Add(button_browse);
            Controls.Add(button_import);
            MaximumSize = new Size(720, 360);
            MinimumSize = new Size(720, 360);
            Name = "WindowMain";
            Text = "ModularAudience (Main Control)";
            ResumeLayout(false);
            PerformLayout();
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
    }
}
