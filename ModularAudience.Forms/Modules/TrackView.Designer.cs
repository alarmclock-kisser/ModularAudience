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
			this.menuItem_removeSelection = new ToolStripMenuItem();
			this.menuItem_normalizeSelection = new ToolStripMenuItem();
			this.menuItem_fadeIn = new ToolStripMenuItem();
			this.menuItem_fadeOut = new ToolStripMenuItem();
			this.trimSilenceToolStripMenuItem = new ToolStripMenuItem();
			this.drawBeatGridToolStripMenuItem = new ToolStripMenuItem();
			this.vScrollBar_volume = new VScrollBar();
			this.hScrollBar_offset = new HScrollBar();
			this.label_volume = new Label();
			this.textBox_time = new TextBox();
			this.checkBox_settings = new CheckBox();
			this.button_apply = new Button();
			this.checkBox_sync = new CheckBox();
			((System.ComponentModel.ISupportInitialize) this.pictureBox_waveform).BeginInit();
			this.contextMenu_waveform.SuspendLayout();
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
			this.pictureBox_waveform.Location = new Point(123, 12);
			this.pictureBox_waveform.Name = "pictureBox_waveform";
			this.pictureBox_waveform.Size = new Size(929, 160);
			this.pictureBox_waveform.TabIndex = 7;
			this.pictureBox_waveform.TabStop = false;
			// 
			// contextMenu_waveform
			// 
			this.contextMenu_waveform.Items.AddRange(new ToolStripItem[] { this.menuItem_copySelection, this.menuItem_removeSelection, this.menuItem_normalizeSelection, this.menuItem_fadeIn, this.menuItem_fadeOut, this.trimSilenceToolStripMenuItem, this.drawBeatGridToolStripMenuItem });
			this.contextMenu_waveform.Name = "contextMenu_waveform";
			this.contextMenu_waveform.Size = new Size(181, 180);
			this.contextMenu_waveform.Opening += this.contextMenu_waveform_Opening;
			// 
			// menuItem_copySelection
			// 
			this.menuItem_copySelection.Name = "menuItem_copySelection";
			this.menuItem_copySelection.Size = new Size(180, 22);
			this.menuItem_copySelection.Text = "Copy Selection";
			this.menuItem_copySelection.Click += this.menuItem_copySelection_Click;
			// 
			// menuItem_removeSelection
			// 
			this.menuItem_removeSelection.Name = "menuItem_removeSelection";
			this.menuItem_removeSelection.Size = new Size(180, 22);
			this.menuItem_removeSelection.Text = "Remove Selection";
			this.menuItem_removeSelection.Click += this.menuItem_removeSelection_Click;
			// 
			// menuItem_normalizeSelection
			// 
			this.menuItem_normalizeSelection.Name = "menuItem_normalizeSelection";
			this.menuItem_normalizeSelection.Size = new Size(180, 22);
			this.menuItem_normalizeSelection.Text = "Normalize...";
			this.menuItem_normalizeSelection.Click += this.menuItem_normalizeSelection_Click;
			// 
			// menuItem_fadeIn
			// 
			this.menuItem_fadeIn.Name = "menuItem_fadeIn";
			this.menuItem_fadeIn.Size = new Size(180, 22);
			this.menuItem_fadeIn.Text = "Fade In...";
			this.menuItem_fadeIn.Click += this.menuItem_fadeIn_Click;
			// 
			// menuItem_fadeOut
			// 
			this.menuItem_fadeOut.Name = "menuItem_fadeOut";
			this.menuItem_fadeOut.Size = new Size(180, 22);
			this.menuItem_fadeOut.Text = "Fade Out...";
			this.menuItem_fadeOut.Click += this.menuItem_fadeOut_Click;
			// 
			// trimSilenceToolStripMenuItem
			// 
			this.trimSilenceToolStripMenuItem.Name = "trimSilenceToolStripMenuItem";
			this.trimSilenceToolStripMenuItem.Size = new Size(180, 22);
			this.trimSilenceToolStripMenuItem.Text = "Trim Silence...";
			this.trimSilenceToolStripMenuItem.Click += this.menuItem_trimSilence_Click;
			// 
			// drawBeatGridToolStripMenuItem
			// 
			this.drawBeatGridToolStripMenuItem.CheckOnClick = true;
			this.drawBeatGridToolStripMenuItem.Name = "drawBeatGridToolStripMenuItem";
			this.drawBeatGridToolStripMenuItem.Size = new Size(180, 22);
			this.drawBeatGridToolStripMenuItem.Text = "Draw Beat-Grid";
			this.drawBeatGridToolStripMenuItem.Click += this.menuItem_drawBeatGrid_Click;
			// 
			// vScrollBar_volume
			// 
			this.vScrollBar_volume.LargeChange = 1;
			this.vScrollBar_volume.Location = new Point(104, 12);
			this.vScrollBar_volume.Maximum = 9999;
			this.vScrollBar_volume.Name = "vScrollBar_volume";
			this.vScrollBar_volume.Size = new Size(17, 160);
			this.vScrollBar_volume.TabIndex = 8;
			this.vScrollBar_volume.Scroll += this.vScrollBar_volume_Scroll;
			// 
			// hScrollBar_offset
			// 
			this.hScrollBar_offset.Location = new Point(123, 175);
			this.hScrollBar_offset.Name = "hScrollBar_offset";
			this.hScrollBar_offset.Size = new Size(928, 17);
			this.hScrollBar_offset.TabIndex = 9;
			this.hScrollBar_offset.Scroll += this.hScrollBar_offset_Scroll;
			// 
			// label_volume
			// 
			this.label_volume.AutoSize = true;
			this.label_volume.Location = new Point(72, 175);
			this.label_volume.Name = "label_volume";
			this.label_volume.Size = new Size(44, 15);
			this.label_volume.TabIndex = 10;
			this.label_volume.Text = "100.0%";
			// 
			// textBox_time
			// 
			this.textBox_time.Location = new Point(9, 120);
			this.textBox_time.MaxLength = 32;
			this.textBox_time.Name = "textBox_time";
			this.textBox_time.PlaceholderText = "0:00:00.000";
			this.textBox_time.ReadOnly = true;
			this.textBox_time.Size = new Size(92, 23);
			this.textBox_time.TabIndex = 11;
			this.textBox_time.TabStop = false;
			// 
			// checkBox_settings
			// 
			this.checkBox_settings.AutoSize = true;
			this.checkBox_settings.Location = new Point(9, 96);
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
			this.button_apply.Font = new Font("Bahnschrift SemiCondensed", 9F, FontStyle.Regular, GraphicsUnit.Point,  0);
			this.button_apply.Location = new Point(9, 149);
			this.button_apply.Name = "button_apply";
			this.button_apply.Size = new Size(92, 23);
			this.button_apply.TabIndex = 13;
			this.button_apply.Text = "Apply Changes";
			this.button_apply.UseVisualStyleBackColor = false;
			this.button_apply.Click += this.button_apply_Click;
			// 
			// checkBox_sync
			// 
			this.checkBox_sync.AutoSize = true;
			this.checkBox_sync.Font = new Font("Bahnschrift SemiCondensed", 9F, FontStyle.Regular, GraphicsUnit.Point,  0);
			this.checkBox_sync.Location = new Point(9, 36);
			this.checkBox_sync.Name = "checkBox_sync";
			this.checkBox_sync.Size = new Size(94, 18);
			this.checkBox_sync.TabIndex = 14;
			this.checkBox_sync.Text = "Sync Playback";
			this.checkBox_sync.UseVisualStyleBackColor = true;
			// 
			// TrackView
			// 
			this.AutoScaleDimensions = new SizeF(7F, 15F);
			this.AutoScaleMode = AutoScaleMode.Font;
			this.BackColor = SystemColors.ControlLight;
			this.ClientSize = new Size(1064, 197);
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
        private ToolStripMenuItem menuItem_removeSelection;
        private ToolStripMenuItem menuItem_normalizeSelection;
        private ToolStripMenuItem menuItem_fadeIn;
        private ToolStripMenuItem menuItem_fadeOut;
		private Button button_apply;
        private CheckBox checkBox_sync;
		private ToolStripMenuItem trimSilenceToolStripMenuItem;
		private ToolStripMenuItem drawBeatGridToolStripMenuItem;
	}
}