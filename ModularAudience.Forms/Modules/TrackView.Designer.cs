namespace ModularAudience.Forms.Modules
{
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
            this.components = new System.ComponentModel.Container();
            this.button_loop = new Button();
            this.button_playback = new Button();
            this.button_pause = new Button();
            this.pictureBox_waveform = new PictureBox();
            this.contextMenu_waveform = new ContextMenuStrip(this.components);
            this.menuItem_copySelection = new ToolStripMenuItem();
            this.menuItem_splitEqualParts = new ToolStripMenuItem();
            this.menuItem_splitEqualParts2 = new ToolStripMenuItem();
            this.menuItem_splitEqualParts4 = new ToolStripMenuItem();
            this.menuItem_splitEqualParts8 = new ToolStripMenuItem();
            this.menuItem_splitEqualParts16 = new ToolStripMenuItem();
            this.menuItem_splitEqualParts32 = new ToolStripMenuItem();
            this.menuItem_removeSelection = new ToolStripMenuItem();
            this.menuItem_normalizeSelection = new ToolStripMenuItem();
            this.menuItem_fadeIn = new ToolStripMenuItem();
            this.menuItem_fadeOut = new ToolStripMenuItem();
            this.trimSilenceToolStripMenuItem = new ToolStripMenuItem();
            this.drawBeatGridToolStripMenuItem = new ToolStripMenuItem();
            this.beatGridV1ToolStripMenuItem = new ToolStripMenuItem();
            this.beatGridV2ToolStripMenuItem = new ToolStripMenuItem();
            this.vScrollBar_volume = new VScrollBar();
            this.hScrollBar_offset = new HScrollBar();
            this.label_volume = new Label();
            this.textBox_time = new TextBox();
            this.checkBox_settings = new CheckBox();
            this.button_apply = new Button();
            this.checkBox_sync = new CheckBox();
            this.checkBox_mute = new CheckBox();
            this.checkBox_solo = new CheckBox();
            this.hScrollBar_rate = new HScrollBar();
            this.contextMenu_rate = new ContextMenuStrip(this.components);
            this.menuItem_rateJumpHere = new ToolStripMenuItem();
            this.menuItem_rateResetCenter = new ToolStripMenuItem();
            this.label_info_rate = new Label();
            this.toolStripMenuItem_jumpHere = new ToolStripMenuItem();
            ((System.ComponentModel.ISupportInitialize) this.pictureBox_waveform).BeginInit();
            this.contextMenu_waveform.SuspendLayout();
            this.contextMenu_rate.SuspendLayout();
            this.SuspendLayout();
            // 
            // button_loop
            // 
            this.button_loop.Font = new Font("Segoe UI Symbol", 9F, FontStyle.Bold, GraphicsUnit.Point,  0);
            this.button_loop.Location = new Point(71, 8);
            this.button_loop.Margin = new Padding(2);
            this.button_loop.Name = "button_loop";
            this.button_loop.Size = new Size(23, 23);
            this.button_loop.TabIndex = 6;
            this.button_loop.TabStop = false;
            this.button_loop.Text = "↺";
            this.button_loop.UseVisualStyleBackColor = true;
            // 
            // button_playback
            // 
            this.button_playback.Location = new Point(10, 8);
            this.button_playback.Margin = new Padding(2);
            this.button_playback.Name = "button_playback";
            this.button_playback.Size = new Size(23, 23);
            this.button_playback.TabIndex = 4;
            this.button_playback.TabStop = false;
            this.button_playback.Tag = "■";
            this.button_playback.Text = "▶";
            this.button_playback.UseVisualStyleBackColor = true;
            this.button_playback.Click += this.button_playback_Click;
            // 
            // button_pause
            // 
            this.button_pause.Font = new Font("Bahnschrift", 9F, FontStyle.Regular, GraphicsUnit.Point,  0);
            this.button_pause.Location = new Point(40, 8);
            this.button_pause.Margin = new Padding(2);
            this.button_pause.Name = "button_pause";
            this.button_pause.Size = new Size(23, 23);
            this.button_pause.TabIndex = 5;
            this.button_pause.TabStop = false;
            this.button_pause.Text = "||";
            this.button_pause.UseVisualStyleBackColor = true;
            this.button_pause.Click += this.button_pause_Click;
            // 
            // pictureBox_waveform
            // 
            this.pictureBox_waveform.BackColor = Color.White;
            this.pictureBox_waveform.BorderStyle = BorderStyle.Fixed3D;
            this.pictureBox_waveform.ContextMenuStrip = this.contextMenu_waveform;
            this.pictureBox_waveform.Location = new Point(135, 12);
            this.pictureBox_waveform.Name = "pictureBox_waveform";
            this.pictureBox_waveform.Size = new Size(917, 160);
            this.pictureBox_waveform.TabIndex = 7;
            this.pictureBox_waveform.TabStop = false;
            // 
            // contextMenu_waveform
            // 
            this.contextMenu_waveform.Items.AddRange(new ToolStripItem[] { this.toolStripMenuItem_jumpHere, this.menuItem_copySelection, this.menuItem_splitEqualParts, this.menuItem_removeSelection, this.menuItem_normalizeSelection, this.menuItem_fadeIn, this.menuItem_fadeOut, this.trimSilenceToolStripMenuItem, this.drawBeatGridToolStripMenuItem });
            this.contextMenu_waveform.Name = "contextMenu_waveform";
            this.contextMenu_waveform.Size = new Size(183, 224);
            this.contextMenu_waveform.Opening += this.contextMenu_waveform_Opening;
            // 
            // menuItem_copySelection
            // 
            this.menuItem_copySelection.Name = "menuItem_copySelection";
            this.menuItem_copySelection.Size = new Size(182, 22);
            this.menuItem_copySelection.Text = "Copy Selection";
            this.menuItem_copySelection.Click += this.menuItem_copySelection_Click;
            // 
            // menuItem_splitEqualParts
            // 
            this.menuItem_splitEqualParts.DropDownItems.AddRange(new ToolStripItem[] { this.menuItem_splitEqualParts2, this.menuItem_splitEqualParts4, this.menuItem_splitEqualParts8, this.menuItem_splitEqualParts16, this.menuItem_splitEqualParts32 });
            this.menuItem_splitEqualParts.Name = "menuItem_splitEqualParts";
            this.menuItem_splitEqualParts.Size = new Size(182, 22);
            this.menuItem_splitEqualParts.Text = "Split Into Equal Parts";
            // 
            // menuItem_splitEqualParts2
            // 
            this.menuItem_splitEqualParts2.Name = "menuItem_splitEqualParts2";
            this.menuItem_splitEqualParts2.Size = new Size(180, 22);
            this.menuItem_splitEqualParts2.Text = "2";
            this.menuItem_splitEqualParts2.Click += this.menuItem_splitEqualParts2_Click;
            // 
            // menuItem_splitEqualParts4
            // 
            this.menuItem_splitEqualParts4.Name = "menuItem_splitEqualParts4";
            this.menuItem_splitEqualParts4.Size = new Size(180, 22);
            this.menuItem_splitEqualParts4.Text = "4";
            this.menuItem_splitEqualParts4.Click += this.menuItem_splitEqualParts4_Click;
            // 
            // menuItem_splitEqualParts8
            // 
            this.menuItem_splitEqualParts8.Name = "menuItem_splitEqualParts8";
            this.menuItem_splitEqualParts8.Size = new Size(180, 22);
            this.menuItem_splitEqualParts8.Text = "8";
            this.menuItem_splitEqualParts8.Click += this.menuItem_splitEqualParts8_Click;
            // 
            // menuItem_splitEqualParts16
            // 
            this.menuItem_splitEqualParts16.Name = "menuItem_splitEqualParts16";
            this.menuItem_splitEqualParts16.Size = new Size(180, 22);
            this.menuItem_splitEqualParts16.Text = "16";
            this.menuItem_splitEqualParts16.Click += this.menuItem_splitEqualParts16_Click;
            // 
            // menuItem_splitEqualParts32
            // 
            this.menuItem_splitEqualParts32.Name = "menuItem_splitEqualParts32";
            this.menuItem_splitEqualParts32.Size = new Size(180, 22);
            this.menuItem_splitEqualParts32.Text = "32";
            this.menuItem_splitEqualParts32.Click += this.menuItem_splitEqualParts32_Click;
            // 
            // menuItem_removeSelection
            // 
            this.menuItem_removeSelection.Name = "menuItem_removeSelection";
            this.menuItem_removeSelection.Size = new Size(182, 22);
            this.menuItem_removeSelection.Text = "Remove Selection";
            this.menuItem_removeSelection.Click += this.menuItem_removeSelection_Click;
            // 
            // menuItem_normalizeSelection
            // 
            this.menuItem_normalizeSelection.Name = "menuItem_normalizeSelection";
            this.menuItem_normalizeSelection.Size = new Size(182, 22);
            this.menuItem_normalizeSelection.Text = "Normalize...";
            this.menuItem_normalizeSelection.Click += this.menuItem_normalizeSelection_Click;
            // 
            // menuItem_fadeIn
            // 
            this.menuItem_fadeIn.Name = "menuItem_fadeIn";
            this.menuItem_fadeIn.Size = new Size(182, 22);
            this.menuItem_fadeIn.Text = "Fade In...";
            this.menuItem_fadeIn.Click += this.menuItem_fadeIn_Click;
            // 
            // menuItem_fadeOut
            // 
            this.menuItem_fadeOut.Name = "menuItem_fadeOut";
            this.menuItem_fadeOut.Size = new Size(182, 22);
            this.menuItem_fadeOut.Text = "Fade Out...";
            this.menuItem_fadeOut.Click += this.menuItem_fadeOut_Click;
            // 
            // trimSilenceToolStripMenuItem
            // 
            this.trimSilenceToolStripMenuItem.Name = "trimSilenceToolStripMenuItem";
            this.trimSilenceToolStripMenuItem.Size = new Size(182, 22);
            this.trimSilenceToolStripMenuItem.Text = "Trim Silence...";
            this.trimSilenceToolStripMenuItem.Click += this.menuItem_trimSilence_Click;
            // 
            // drawBeatGridToolStripMenuItem
            // 
            this.drawBeatGridToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { this.beatGridV1ToolStripMenuItem, this.beatGridV2ToolStripMenuItem });
            this.drawBeatGridToolStripMenuItem.Name = "drawBeatGridToolStripMenuItem";
            this.drawBeatGridToolStripMenuItem.Size = new Size(182, 22);
            this.drawBeatGridToolStripMenuItem.Text = "Draw Beat-Grid";
            // 
            // beatGridV1ToolStripMenuItem
            // 
            this.beatGridV1ToolStripMenuItem.CheckOnClick = true;
            this.beatGridV1ToolStripMenuItem.Name = "beatGridV1ToolStripMenuItem";
            this.beatGridV1ToolStripMenuItem.Size = new Size(121, 22);
            this.beatGridV1ToolStripMenuItem.Text = "Version 1";
            this.beatGridV1ToolStripMenuItem.Click += this.beatgridV1ToolStripMenuItem_Click;
            // 
            // beatGridV2ToolStripMenuItem
            // 
            this.beatGridV2ToolStripMenuItem.CheckOnClick = true;
            this.beatGridV2ToolStripMenuItem.Name = "beatGridV2ToolStripMenuItem";
            this.beatGridV2ToolStripMenuItem.Size = new Size(121, 22);
            this.beatGridV2ToolStripMenuItem.Text = "Version 2";
            this.beatGridV2ToolStripMenuItem.Click += this.beatGridV2ToolStripMenuItem_Click;
            // 
            // vScrollBar_volume
            // 
            this.vScrollBar_volume.LargeChange = 1;
            this.vScrollBar_volume.Location = new Point(115, 12);
            this.vScrollBar_volume.Maximum = 1000;
            this.vScrollBar_volume.Name = "vScrollBar_volume";
            this.vScrollBar_volume.Size = new Size(17, 160);
            this.vScrollBar_volume.TabIndex = 8;
            this.vScrollBar_volume.Scroll += this.vScrollBar_volume_Scroll;
            // 
            // hScrollBar_offset
            // 
            this.hScrollBar_offset.Location = new Point(135, 175);
            this.hScrollBar_offset.Name = "hScrollBar_offset";
            this.hScrollBar_offset.Size = new Size(916, 17);
            this.hScrollBar_offset.TabIndex = 9;
            this.hScrollBar_offset.Scroll += this.hScrollBar_offset_Scroll;
            // 
            // label_volume
            // 
            this.label_volume.AutoSize = true;
            this.label_volume.Location = new Point(88, 175);
            this.label_volume.Name = "label_volume";
            this.label_volume.Size = new Size(44, 15);
            this.label_volume.TabIndex = 10;
            this.label_volume.Text = "100.0%";
            // 
            // textBox_time
            // 
            this.textBox_time.Location = new Point(10, 142);
            this.textBox_time.MaxLength = 32;
            this.textBox_time.Name = "textBox_time";
            this.textBox_time.PlaceholderText = "0:00:00.000";
            this.textBox_time.ReadOnly = true;
            this.textBox_time.Size = new Size(82, 23);
            this.textBox_time.TabIndex = 11;
            this.textBox_time.TabStop = false;
            // 
            // checkBox_settings
            // 
            this.checkBox_settings.AutoSize = true;
            this.checkBox_settings.Location = new Point(10, 120);
            this.checkBox_settings.Margin = new Padding(3, 0, 3, 0);
            this.checkBox_settings.Name = "checkBox_settings";
            this.checkBox_settings.Size = new Size(68, 19);
            this.checkBox_settings.TabIndex = 12;
            this.checkBox_settings.Text = "Settings";
            this.checkBox_settings.UseVisualStyleBackColor = true;
            this.checkBox_settings.CheckedChanged += this.checkBox_settings_CheckedChanged;
            // 
            // button_apply
            // 
            this.button_apply.BackColor = SystemColors.Info;
            this.button_apply.Font = new Font("Bahnschrift Light SemiCondensed", 8.25F, FontStyle.Regular, GraphicsUnit.Point,  0);
            this.button_apply.Location = new Point(10, 169);
            this.button_apply.Margin = new Padding(1);
            this.button_apply.Name = "button_apply";
            this.button_apply.Size = new Size(82, 23);
            this.button_apply.TabIndex = 13;
            this.button_apply.Text = "Apply Changes";
            this.button_apply.UseVisualStyleBackColor = false;
            this.button_apply.Click += this.button_apply_Click;
            // 
            // checkBox_sync
            // 
            this.checkBox_sync.AutoSize = true;
            this.checkBox_sync.Font = new Font("Bahnschrift SemiCondensed", 9F, FontStyle.Regular, GraphicsUnit.Point,  0);
            this.checkBox_sync.Location = new Point(10, 36);
            this.checkBox_sync.Name = "checkBox_sync";
            this.checkBox_sync.Size = new Size(94, 18);
            this.checkBox_sync.TabIndex = 14;
            this.checkBox_sync.Text = "Sync Playback";
            this.checkBox_sync.UseVisualStyleBackColor = true;
            this.checkBox_sync.CheckedChanged += this.checkBox_sync_CheckedChanged;
            // 
            // checkBox_mute
            // 
            this.checkBox_mute.AutoSize = true;
            this.checkBox_mute.Font = new Font("Segoe UI Semilight", 8.25F);
            this.checkBox_mute.Location = new Point(10, 57);
            this.checkBox_mute.Margin = new Padding(0);
            this.checkBox_mute.Name = "checkBox_mute";
            this.checkBox_mute.Size = new Size(51, 17);
            this.checkBox_mute.TabIndex = 15;
            this.checkBox_mute.TabStop = false;
            this.checkBox_mute.Text = "Mute";
            this.checkBox_mute.UseVisualStyleBackColor = true;
            this.checkBox_mute.CheckedChanged += this.checkBox_mute_CheckedChanged;
            // 
            // checkBox_solo
            // 
            this.checkBox_solo.AutoSize = true;
            this.checkBox_solo.Font = new Font("Segoe UI Semilight", 8.25F);
            this.checkBox_solo.Location = new Point(61, 57);
            this.checkBox_solo.Margin = new Padding(0);
            this.checkBox_solo.Name = "checkBox_solo";
            this.checkBox_solo.Size = new Size(46, 17);
            this.checkBox_solo.TabIndex = 16;
            this.checkBox_solo.TabStop = false;
            this.checkBox_solo.Text = "Solo";
            this.checkBox_solo.UseVisualStyleBackColor = true;
            this.checkBox_solo.CheckedChanged += this.checkBox_solo_CheckedChanged;
            // 
            // hScrollBar_rate
            // 
            this.hScrollBar_rate.ContextMenuStrip = this.contextMenu_rate;
            this.hScrollBar_rate.Location = new Point(2, 103);
            this.hScrollBar_rate.Maximum = 500;
            this.hScrollBar_rate.Minimum = -500;
            this.hScrollBar_rate.Name = "hScrollBar_rate";
            this.hScrollBar_rate.Size = new Size(113, 17);
            this.hScrollBar_rate.TabIndex = 17;
            this.hScrollBar_rate.MouseDown += this.hScrollBar_rate_MouseDown;
            this.hScrollBar_rate.Scroll += this.hScrollBar_rate_Scroll;
            this.hScrollBar_rate.ValueChanged += this.hScrollBar_rate_ValueChanged;
            // 
            // contextMenu_rate
            // 
            this.contextMenu_rate.Items.AddRange(new ToolStripItem[] { this.menuItem_rateJumpHere, this.menuItem_rateResetCenter });
            this.contextMenu_rate.Name = "contextMenu_rate";
            this.contextMenu_rate.Size = new Size(155, 48);
            this.contextMenu_rate.Opening += this.contextMenu_rate_Opening;
            // 
            // menuItem_rateJumpHere
            // 
            this.menuItem_rateJumpHere.Name = "menuItem_rateJumpHere";
            this.menuItem_rateJumpHere.Size = new Size(154, 22);
            this.menuItem_rateJumpHere.Text = "Jump here";
            this.menuItem_rateJumpHere.Click += this.menuItem_rateJumpHere_Click;
            // 
            // menuItem_rateResetCenter
            // 
            this.menuItem_rateResetCenter.Name = "menuItem_rateResetCenter";
            this.menuItem_rateResetCenter.Size = new Size(154, 22);
            this.menuItem_rateResetCenter.Text = "Reset to Center";
            this.menuItem_rateResetCenter.Click += this.menuItem_rateResetCenter_Click;
            // 
            // label_info_rate
            // 
            this.label_info_rate.AutoSize = true;
            this.label_info_rate.Font = new Font("Segoe UI Semilight", 8.25F, FontStyle.Regular, GraphicsUnit.Point,  0);
            this.label_info_rate.Location = new Point(10, 88);
            this.label_info_rate.Name = "label_info_rate";
            this.label_info_rate.Size = new Size(66, 13);
            this.label_info_rate.TabIndex = 18;
            this.label_info_rate.Text = "Rate: 100.0%";
            // 
            // toolStripMenuItem_jumpHere
            // 
            this.toolStripMenuItem_jumpHere.Name = "toolStripMenuItem_jumpHere";
            this.toolStripMenuItem_jumpHere.Size = new Size(182, 22);
            this.toolStripMenuItem_jumpHere.Text = "Jump to [-:--:--.---]";
            this.toolStripMenuItem_jumpHere.Click += this.toolStripMenuItem_jumpHere_Click;
            // 
            // TrackView
            // 
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.BackColor = SystemColors.ControlLight;
            this.ClientSize = new Size(1064, 197);
            this.Controls.Add(this.label_info_rate);
            this.Controls.Add(this.hScrollBar_rate);
            this.Controls.Add(this.checkBox_solo);
            this.Controls.Add(this.checkBox_mute);
            this.Controls.Add(this.checkBox_sync);
            this.Controls.Add(this.button_apply);
            this.Controls.Add(this.checkBox_settings);
            this.Controls.Add(this.textBox_time);
            this.Controls.Add(this.label_volume);
            this.Controls.Add(this.hScrollBar_offset);
            this.Controls.Add(this.vScrollBar_volume);
            this.Controls.Add(this.pictureBox_waveform);
            this.Controls.Add(this.button_loop);
            this.Controls.Add(this.button_playback);
            this.Controls.Add(this.button_pause);
            this.MaximizeBox = false;
            this.MaximumSize = new Size(8192, 236);
            this.MinimumSize = new Size(400, 236);
            this.Name = "TrackView";
            this.Text = "#00 - No Track Loaded";
            ((System.ComponentModel.ISupportInitialize) this.pictureBox_waveform).EndInit();
            this.contextMenu_waveform.ResumeLayout(false);
            this.contextMenu_rate.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();
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
		private Button button_apply;
        private CheckBox checkBox_sync;
		private ToolStripMenuItem trimSilenceToolStripMenuItem;
		private ToolStripMenuItem drawBeatGridToolStripMenuItem;
        private ToolStripMenuItem beatGridV1ToolStripMenuItem;
        private ToolStripMenuItem beatGridV2ToolStripMenuItem;
        private CheckBox checkBox_mute;
        private CheckBox checkBox_solo;
        private HScrollBar hScrollBar_rate;
        private Label label_info_rate;
        private ContextMenuStrip contextMenu_rate;
        private ToolStripMenuItem menuItem_rateJumpHere;
        private ToolStripMenuItem menuItem_rateResetCenter;
        private ToolStripMenuItem toolStripMenuItem_jumpHere;
    }
}