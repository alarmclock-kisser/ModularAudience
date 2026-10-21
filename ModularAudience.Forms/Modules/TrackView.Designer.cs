namespace ModularAudience.Forms.Modules
{
    // The designer file assigns the fields in InitializeComponent, the compiler cannot see it.
#pragma warning disable CS0649

    partial class TrackView
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
            components = new System.ComponentModel.Container();
            button_loop = new Button();
            button_playback = new Button();
            button_pause = new Button();
            pictureBox_waveform = new PictureBox();
            contextMenu_waveform = new ContextMenuStrip(components);
            toolStripMenuItem_jumpHere = new ToolStripMenuItem();
            menuItem_copySelection = new ToolStripMenuItem();
            menuItem_splitEqualParts = new ToolStripMenuItem();
            menuItem_splitEqualParts2 = new ToolStripMenuItem();
            menuItem_splitEqualParts4 = new ToolStripMenuItem();
            menuItem_splitEqualParts8 = new ToolStripMenuItem();
            menuItem_splitEqualParts16 = new ToolStripMenuItem();
            menuItem_splitEqualParts32 = new ToolStripMenuItem();
            menuItem_atomize = new ToolStripMenuItem();
            menuItem_removeSelection = new ToolStripMenuItem();
            menuItem_normalizeSelection = new ToolStripMenuItem();
            menuItem_fadeIn = new ToolStripMenuItem();
            menuItem_fadeOut = new ToolStripMenuItem();
            trimSilenceToolStripMenuItem = new ToolStripMenuItem();
            drawBeatGridToolStripMenuItem = new ToolStripMenuItem();
            beatGridV1ToolStripMenuItem = new ToolStripMenuItem();
            beatGridV2ToolStripMenuItem = new ToolStripMenuItem();
            beatGridV3ToolStripMenuItem = new ToolStripMenuItem();
            beatGridAnalogToolStripMenuItem = new ToolStripMenuItem();
            vScrollBar_volume = new VScrollBar();
            hScrollBar_offset = new HScrollBar();
            hScrollBar_rate = new ModularAudience.Forms.Controls.LiveRateScrollBar();
            label_volume = new Label();
            textBox_time = new TextBox();
            checkBox_settings = new CheckBox();
            button_apply = new Button();
            checkBox_sync = new CheckBox();
            checkBox_mute = new CheckBox();
            checkBox_solo = new CheckBox();
            contextMenu_rate = new ContextMenuStrip(components);
            menuItem_rateJumpHere = new ToolStripMenuItem();
            menuItem_rateResetCenter = new ToolStripMenuItem();
            label_info_rate = new Label();
            toolTip_trackView = new ToolTip(components);
            ((System.ComponentModel.ISupportInitialize)pictureBox_waveform).BeginInit();
            contextMenu_waveform.SuspendLayout();
            contextMenu_rate.SuspendLayout();
            SuspendLayout();
            // 
            // button_loop
            // 
            button_loop.Font = new Font("Segoe UI Symbol", 9F, FontStyle.Bold, GraphicsUnit.Point, 0);
            button_loop.Location = new Point(71, 8);
            button_loop.Margin = new Padding(2);
            button_loop.Name = "button_loop";
            button_loop.Size = new Size(23, 23);
            button_loop.TabIndex = 6;
            button_loop.TabStop = false;
            button_loop.Text = "↺";
            toolTip_trackView.SetToolTip(button_loop, "Loop toggle.\nNormal click: enable loop.\nClick again: cycle forward.\nCtrl+click: disable loop.\nShift+click: cycle backward.");
            button_loop.UseVisualStyleBackColor = true;
            // 
            // button_playback
            // 
            button_playback.Location = new Point(10, 8);
            button_playback.Margin = new Padding(2);
            button_playback.Name = "button_playback";
            button_playback.Size = new Size(23, 23);
            button_playback.TabIndex = 4;
            button_playback.TabStop = false;
            button_playback.Tag = "■";
            button_playback.Text = "▶";
            toolTip_trackView.SetToolTip(button_playback, "Play/Stop.\nCtrl+click: toggle all TrackViews.");
            button_playback.UseVisualStyleBackColor = true;
            button_playback.Click += button_playback_Click;
            // 
            // button_pause
            // 
            button_pause.Font = new Font("Bahnschrift", 9F, FontStyle.Regular, GraphicsUnit.Point, 0);
            button_pause.Location = new Point(40, 8);
            button_pause.Margin = new Padding(2);
            button_pause.Name = "button_pause";
            button_pause.Size = new Size(23, 23);
            button_pause.TabIndex = 5;
            button_pause.TabStop = false;
            button_pause.Text = "||";
            toolTip_trackView.SetToolTip(button_pause, "Pause/Resume.\nCtrl+click: toggle all TrackViews.");
            button_pause.UseVisualStyleBackColor = true;
            button_pause.Click += button_pause_Click;
            // 
            // pictureBox_waveform
            // 
            pictureBox_waveform.BackColor = Color.White;
            pictureBox_waveform.BorderStyle = BorderStyle.Fixed3D;
            pictureBox_waveform.ContextMenuStrip = contextMenu_waveform;
            pictureBox_waveform.Location = new Point(135, 12);
            pictureBox_waveform.Name = "pictureBox_waveform";
            pictureBox_waveform.Size = new Size(917, 160);
            pictureBox_waveform.TabIndex = 7;
            pictureBox_waveform.TabStop = false;
            toolTip_trackView.SetToolTip(pictureBox_waveform, "Right-click for more actions...");
            // 
            // contextMenu_waveform
            // 
            contextMenu_waveform.Items.AddRange(new ToolStripItem[] { toolStripMenuItem_jumpHere, menuItem_copySelection, menuItem_splitEqualParts, menuItem_atomize, menuItem_removeSelection, menuItem_normalizeSelection, menuItem_fadeIn, menuItem_fadeOut, trimSilenceToolStripMenuItem, drawBeatGridToolStripMenuItem });
            contextMenu_waveform.Name = "contextMenu_waveform";
            contextMenu_waveform.Size = new Size(183, 224);
            contextMenu_waveform.Opening += contextMenu_waveform_Opening;
            // 
            // toolStripMenuItem_jumpHere
            // 
            toolStripMenuItem_jumpHere.Name = "toolStripMenuItem_jumpHere";
            toolStripMenuItem_jumpHere.Size = new Size(182, 22);
            toolStripMenuItem_jumpHere.Text = "Jump to [-:--:--.---]";
            toolStripMenuItem_jumpHere.Click += toolStripMenuItem_jumpHere_Click;
            // 
            // menuItem_copySelection
            // 
            menuItem_copySelection.Name = "menuItem_copySelection";
            menuItem_copySelection.Size = new Size(182, 22);
            menuItem_copySelection.Text = "Copy Selection";
            menuItem_copySelection.Click += menuItem_copySelection_Click;
            // 
            // menuItem_splitEqualParts
            // 
            menuItem_splitEqualParts.DropDownItems.AddRange(new ToolStripItem[] { menuItem_splitEqualParts2, menuItem_splitEqualParts4, menuItem_splitEqualParts8, menuItem_splitEqualParts16, menuItem_splitEqualParts32 });
            menuItem_splitEqualParts.Name = "menuItem_splitEqualParts";
            menuItem_splitEqualParts.Size = new Size(182, 22);
            menuItem_splitEqualParts.Text = "Split Into Equal Parts";
            // 
            // menuItem_splitEqualParts2
            // 
            menuItem_splitEqualParts2.Name = "menuItem_splitEqualParts2";
            menuItem_splitEqualParts2.Size = new Size(86, 22);
            menuItem_splitEqualParts2.Text = "2";
            menuItem_splitEqualParts2.Click += menuItem_splitEqualParts2_Click;
            // 
            // menuItem_splitEqualParts4
            // 
            menuItem_splitEqualParts4.Name = "menuItem_splitEqualParts4";
            menuItem_splitEqualParts4.Size = new Size(86, 22);
            menuItem_splitEqualParts4.Text = "4";
            menuItem_splitEqualParts4.Click += menuItem_splitEqualParts4_Click;
            // 
            // menuItem_splitEqualParts8
            // 
            menuItem_splitEqualParts8.Name = "menuItem_splitEqualParts8";
            menuItem_splitEqualParts8.Size = new Size(86, 22);
            menuItem_splitEqualParts8.Text = "8";
            menuItem_splitEqualParts8.Click += menuItem_splitEqualParts8_Click;
            // 
            // menuItem_splitEqualParts16
            // 
            menuItem_splitEqualParts16.Name = "menuItem_splitEqualParts16";
            menuItem_splitEqualParts16.Size = new Size(86, 22);
            menuItem_splitEqualParts16.Text = "16";
            menuItem_splitEqualParts16.Click += menuItem_splitEqualParts16_Click;
            // 
            // menuItem_splitEqualParts32
            // 
            menuItem_splitEqualParts32.Name = "menuItem_splitEqualParts32";
            menuItem_splitEqualParts32.Size = new Size(86, 22);
            menuItem_splitEqualParts32.Text = "32";
            menuItem_splitEqualParts32.Click += menuItem_splitEqualParts32_Click;
            // 
            // menuItem_atomize
            // 
            menuItem_atomize.Name = "menuItem_atomize";
            menuItem_atomize.Size = new Size(182, 22);
            menuItem_atomize.Text = "Atomize";
            menuItem_atomize.Click += menuItem_atomize_Click;
            // 
            // menuItem_removeSelection
            // 
            menuItem_removeSelection.Name = "menuItem_removeSelection";
            menuItem_removeSelection.Size = new Size(182, 22);
            menuItem_removeSelection.Text = "Remove Selection";
            menuItem_removeSelection.Click += menuItem_removeSelection_Click;
            // 
            // menuItem_normalizeSelection
            // 
            menuItem_normalizeSelection.Name = "menuItem_normalizeSelection";
            menuItem_normalizeSelection.Size = new Size(182, 22);
            menuItem_normalizeSelection.Text = "Normalize...";
            menuItem_normalizeSelection.Click += menuItem_normalizeSelection_Click;
            // 
            // menuItem_fadeIn
            // 
            menuItem_fadeIn.Name = "menuItem_fadeIn";
            menuItem_fadeIn.Size = new Size(182, 22);
            menuItem_fadeIn.Text = "Fade In...";
            menuItem_fadeIn.Click += menuItem_fadeIn_Click;
            // 
            // menuItem_fadeOut
            // 
            menuItem_fadeOut.Name = "menuItem_fadeOut";
            menuItem_fadeOut.Size = new Size(182, 22);
            menuItem_fadeOut.Text = "Fade Out...";
            menuItem_fadeOut.Click += menuItem_fadeOut_Click;
            // 
            // trimSilenceToolStripMenuItem
            // 
            trimSilenceToolStripMenuItem.Name = "trimSilenceToolStripMenuItem";
            trimSilenceToolStripMenuItem.Size = new Size(182, 22);
            trimSilenceToolStripMenuItem.Text = "Trim Silence...";
            trimSilenceToolStripMenuItem.Click += menuItem_trimSilence_Click;
            // 
            // drawBeatGridToolStripMenuItem
            // 
            drawBeatGridToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { beatGridV1ToolStripMenuItem, beatGridV2ToolStripMenuItem, beatGridV3ToolStripMenuItem, beatGridAnalogToolStripMenuItem });
            drawBeatGridToolStripMenuItem.Name = "drawBeatGridToolStripMenuItem";
            drawBeatGridToolStripMenuItem.Size = new Size(182, 22);
            drawBeatGridToolStripMenuItem.Text = "Draw Beat-Grid";
            // 
            // beatGridV1ToolStripMenuItem
            // 
            beatGridV1ToolStripMenuItem.CheckOnClick = true;
            beatGridV1ToolStripMenuItem.Name = "beatGridV1ToolStripMenuItem";
            beatGridV1ToolStripMenuItem.Size = new Size(151, 22);
            beatGridV1ToolStripMenuItem.Text = "Version 1";
            beatGridV1ToolStripMenuItem.Click += beatgridV1ToolStripMenuItem_Click;
            // 
            // beatGridV2ToolStripMenuItem
            // 
            beatGridV2ToolStripMenuItem.CheckOnClick = true;
            beatGridV2ToolStripMenuItem.Name = "beatGridV2ToolStripMenuItem";
            beatGridV2ToolStripMenuItem.Size = new Size(151, 22);
            beatGridV2ToolStripMenuItem.Text = "Version 2";
            beatGridV2ToolStripMenuItem.Click += beatGridV2ToolStripMenuItem_Click;
            // 
            // beatGridV3ToolStripMenuItem
            // 
            beatGridV3ToolStripMenuItem.CheckOnClick = true;
            beatGridV3ToolStripMenuItem.Name = "beatGridV3ToolStripMenuItem";
            beatGridV3ToolStripMenuItem.Size = new Size(151, 22);
            beatGridV3ToolStripMenuItem.Text = "Version 3";
            beatGridV3ToolStripMenuItem.Click += beatGridV3ToolStripMenuItem_Click;
            // 
            // beatGridAnalogToolStripMenuItem
            // 
            beatGridAnalogToolStripMenuItem.CheckOnClick = true;
            beatGridAnalogToolStripMenuItem.Name = "beatGridAnalogToolStripMenuItem";
            beatGridAnalogToolStripMenuItem.Size = new Size(151, 22);
            beatGridAnalogToolStripMenuItem.Text = "Version analog";
            beatGridAnalogToolStripMenuItem.Click += beatGridAnalogToolStripMenuItem_Click;
            // 
            // vScrollBar_volume
            // 
            vScrollBar_volume.LargeChange = 1;
            vScrollBar_volume.Location = new Point(115, 12);
            vScrollBar_volume.Maximum = 1000;
            vScrollBar_volume.Name = "vScrollBar_volume";
            vScrollBar_volume.Size = new Size(17, 160);
            vScrollBar_volume.TabIndex = 8;
            vScrollBar_volume.Scroll += vScrollBar_volume_Scroll;
            // 
            // hScrollBar_offset
            // 
            hScrollBar_offset.Location = new Point(135, 175);
            hScrollBar_offset.Name = "hScrollBar_offset";
            hScrollBar_offset.Size = new Size(916, 17);
            hScrollBar_offset.TabIndex = 9;
            hScrollBar_offset.Scroll += hScrollBar_offset_Scroll;
            // 
            // label_volume
            // 
            label_volume.AutoSize = true;
            label_volume.Location = new Point(88, 175);
            label_volume.Name = "label_volume";
            label_volume.Size = new Size(44, 15);
            label_volume.TabIndex = 10;
            label_volume.Text = "100.0%";
            // 
            // textBox_time
            // 
            textBox_time.Location = new Point(10, 142);
            textBox_time.MaxLength = 32;
            textBox_time.Name = "textBox_time";
            textBox_time.PlaceholderText = "0:00:00.000";
            textBox_time.ReadOnly = true;
            textBox_time.Size = new Size(82, 23);
            textBox_time.TabIndex = 11;
            textBox_time.TabStop = false;
            // 
            // checkBox_settings
            // 
            checkBox_settings.AutoSize = true;
            checkBox_settings.Location = new Point(10, 120);
            checkBox_settings.Margin = new Padding(3, 0, 3, 0);
            checkBox_settings.Name = "checkBox_settings";
            checkBox_settings.Size = new Size(68, 19);
            checkBox_settings.TabIndex = 12;
            checkBox_settings.Text = "Settings";
            checkBox_settings.UseVisualStyleBackColor = true;
            checkBox_settings.CheckedChanged += checkBox_settings_CheckedChanged;
            // 
            // button_apply
            // 
            button_apply.BackColor = SystemColors.Info;
            button_apply.Font = new Font("Bahnschrift Light SemiCondensed", 8.25F, FontStyle.Regular, GraphicsUnit.Point, 0);
            button_apply.Location = new Point(10, 169);
            button_apply.Margin = new Padding(1);
            button_apply.Name = "button_apply";
            button_apply.Size = new Size(82, 23);
            button_apply.TabIndex = 13;
            button_apply.Text = "Apply Changes";
            button_apply.UseVisualStyleBackColor = false;
            button_apply.Click += button_apply_Click;
            // 
            // checkBox_sync
            // 
            checkBox_sync.AutoSize = true;
            checkBox_sync.Font = new Font("Bahnschrift SemiCondensed", 9F, FontStyle.Regular, GraphicsUnit.Point, 0);
            checkBox_sync.Location = new Point(10, 36);
            checkBox_sync.Name = "checkBox_sync";
            checkBox_sync.Size = new Size(94, 18);
            checkBox_sync.TabIndex = 14;
            checkBox_sync.Text = "Sync Playback";
            toolTip_trackView.SetToolTip(checkBox_sync, "Sync Playback.\nCtrl+click: toggle all TrackViews (if on, unchecks all; if off, checks all).");
            checkBox_sync.UseVisualStyleBackColor = true;
            checkBox_sync.CheckedChanged += checkBox_sync_CheckedChanged;
            // 
            // checkBox_mute
            // 
            checkBox_mute.AutoSize = true;
            checkBox_mute.Font = new Font("Segoe UI Semilight", 8.25F);
            checkBox_mute.Location = new Point(10, 57);
            checkBox_mute.Margin = new Padding(0);
            checkBox_mute.Name = "checkBox_mute";
            checkBox_mute.Size = new Size(51, 17);
            checkBox_mute.TabIndex = 15;
            checkBox_mute.TabStop = false;
            checkBox_mute.Text = "Mute";
            checkBox_mute.UseVisualStyleBackColor = true;
            checkBox_mute.CheckedChanged += checkBox_mute_CheckedChanged;
            // 
            // checkBox_solo
            // 
            checkBox_solo.AutoSize = true;
            checkBox_solo.Font = new Font("Segoe UI Semilight", 8.25F);
            checkBox_solo.Location = new Point(61, 57);
            checkBox_solo.Margin = new Padding(0);
            checkBox_solo.Name = "checkBox_solo";
            checkBox_solo.Size = new Size(46, 17);
            checkBox_solo.TabIndex = 16;
            checkBox_solo.TabStop = false;
            checkBox_solo.Text = "Solo";
            checkBox_solo.UseVisualStyleBackColor = true;
            checkBox_solo.CheckedChanged += checkBox_solo_CheckedChanged;
            //
            // hScrollBar_rate
            //
            hScrollBar_rate.ContextMenuStrip = contextMenu_rate;
            hScrollBar_rate.Location = new Point(2, 103);
            hScrollBar_rate.Maximum = 100;
            hScrollBar_rate.Minimum = -100;
            hScrollBar_rate.Name = "hScrollBar_rate";
            hScrollBar_rate.Size = new Size(113, 17);
            hScrollBar_rate.TabIndex = 17;
            hScrollBar_rate.MouseDown += hScrollBar_rate_MouseDown;
            hScrollBar_rate.Scroll += hScrollBar_rate_Scroll;
            hScrollBar_rate.ValueChanged += hScrollBar_rate_ValueChanged;
            toolTip_trackView.SetToolTip(hScrollBar_rate, "Playback rate (varispeed).\nCtrl+click: change rate on all TrackViews.\nRight-click: context menu (jump here, reset center).");
            //
            // contextMenu_rate
            //
            contextMenu_rate.Items.AddRange(new ToolStripItem[] { menuItem_rateJumpHere, menuItem_rateResetCenter });
            contextMenu_rate.Name = "contextMenu_rate";
            contextMenu_rate.Size = new Size(169, 48);
            contextMenu_rate.Opening += contextMenu_rate_Opening;
            // 
            // menuItem_rateJumpHere
            // 
            menuItem_rateJumpHere.Name = "menuItem_rateJumpHere";
            menuItem_rateJumpHere.Size = new Size(168, 22);
            menuItem_rateJumpHere.Text = "Jump here";
            menuItem_rateJumpHere.Click += menuItem_rateJumpHere_Click;
            // 
            // menuItem_rateResetCenter
            // 
            menuItem_rateResetCenter.Name = "menuItem_rateResetCenter";
            menuItem_rateResetCenter.Size = new Size(168, 22);
            menuItem_rateResetCenter.Text = "Center/Reset Rate";
            menuItem_rateResetCenter.Click += menuItem_rateResetCenter_Click;
            // 
            // label_info_rate
            // 
            label_info_rate.AutoSize = true;
            label_info_rate.Font = new Font("Segoe UI Semilight", 8.25F, FontStyle.Regular, GraphicsUnit.Point, 0);
            label_info_rate.Location = new Point(10, 88);
            label_info_rate.Name = "label_info_rate";
            label_info_rate.Size = new Size(66, 13);
            label_info_rate.TabIndex = 18;
            label_info_rate.Text = "Rate: 100.0%";
            // 
            // toolTip_trackView
            // 
            toolTip_trackView.AutoPopDelay = 10000;
            toolTip_trackView.InitialDelay = 500;
            toolTip_trackView.ReshowDelay = 100;
            toolTip_trackView.ShowAlways = true;
            // 
            // TrackView
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = SystemColors.ControlLight;
            ClientSize = new Size(1064, 197);
            Controls.Add(label_info_rate);
            Controls.Add(hScrollBar_rate);
            Controls.Add(checkBox_solo);
            Controls.Add(checkBox_mute);
            Controls.Add(checkBox_sync);
            Controls.Add(button_apply);
            Controls.Add(checkBox_settings);
            Controls.Add(textBox_time);
            Controls.Add(label_volume);
            Controls.Add(hScrollBar_offset);
            Controls.Add(vScrollBar_volume);
            Controls.Add(pictureBox_waveform);
            Controls.Add(button_loop);
            Controls.Add(button_playback);
            Controls.Add(button_pause);
            MaximizeBox = false;
            MaximumSize = new Size(8192, 236);
            MinimumSize = new Size(400, 236);
            Name = "TrackView";
            Text = "#00 - No Track Loaded";
            ((System.ComponentModel.ISupportInitialize)pictureBox_waveform).EndInit();
            contextMenu_waveform.ResumeLayout(false);
            contextMenu_rate.ResumeLayout(false);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Button button_loop;
        private Button button_playback;
        private Button button_pause;
        private PictureBox pictureBox_waveform;
        private VScrollBar vScrollBar_volume;
        private HScrollBar hScrollBar_offset;
        private Label label_volume;
        private TextBox textBox_time;
        private CheckBox checkBox_settings;
        private ContextMenuStrip contextMenu_waveform;
        private ToolStripMenuItem menuItem_copySelection;
        private ToolStripMenuItem menuItem_splitEqualParts;
        private ToolStripMenuItem menuItem_splitEqualParts2;
        private ToolStripMenuItem menuItem_splitEqualParts4;
        private ToolStripMenuItem menuItem_splitEqualParts8;
        private ToolStripMenuItem menuItem_splitEqualParts16;
        private ToolStripMenuItem menuItem_splitEqualParts32;
        private ToolStripMenuItem menuItem_removeSelection;
        private ToolStripMenuItem menuItem_normalizeSelection;
		private ToolStripMenuItem menuItem_fadeIn;
		private ToolStripMenuItem menuItem_fadeOut;
		private ToolStripMenuItem menuItem_atomize;
		private Button button_apply;
        private CheckBox checkBox_sync;
		private ToolStripMenuItem trimSilenceToolStripMenuItem;
		private ToolStripMenuItem drawBeatGridToolStripMenuItem;
        private ToolStripMenuItem beatGridV1ToolStripMenuItem;
        private ToolStripMenuItem beatGridV2ToolStripMenuItem;
        private ToolStripMenuItem beatGridV3ToolStripMenuItem;
        private ToolStripMenuItem beatGridAnalogToolStripMenuItem;
        private CheckBox checkBox_mute;
        private CheckBox checkBox_solo;
        private HScrollBar hScrollBar_rate;
        private Label label_info_rate;
        private ContextMenuStrip contextMenu_rate;
        private ToolStripMenuItem menuItem_rateJumpHere;
        private ToolStripMenuItem menuItem_rateResetCenter;
        private ToolStripMenuItem toolStripMenuItem_jumpHere;
        private ToolTip toolTip_trackView;
    }
}