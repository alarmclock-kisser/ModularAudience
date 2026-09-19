namespace ModularAudience.Forms.Modules
{
    partial class LoopControl
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
            if (disposing)
            {
                this.playlistTargetsTimer.Dispose();
                if (ReferenceEquals(WindowMain.LoopControlWindow, this))
                {
                    WindowMain.LoopControlWindow = null;
                }
            }
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(LoopControl));
            contextMenuStrip_playlistItem = new ContextMenuStrip(components);
            toolStripMenuItem_resetPlaylistRate = new ToolStripMenuItem();
            toolStripMenuItem_removeFromEnsemble = new ToolStripMenuItem();
            toolTip_playlistTracks = new ToolTip(components);
            label_targetMode = new Label();
            button_playlistAllOn = new Button();
            button_playlistAllOff = new Button();
            checkedListBox_playlistTracks = new ModularAudience.Forms.Controls.PlaylistTrackListBox();
            panel_buttons = new Panel();
            button_loop = new Button();
            button_copy = new Button();
            domainUpDown_multiplier = new DomainUpDown();
            label_info_multiplier = new Label();
            numericUpDown_jump = new NumericUpDown();
            button_forward = new Button();
            button_backward = new Button();
            label_info_jump = new Label();
            comboBox_drops = new ComboBox();
            label_info_manageDrops = new Label();
            contextMenuStrip_playlistItem.SuspendLayout();
            panel_buttons.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)numericUpDown_jump).BeginInit();
            SuspendLayout();
            // 
            // contextMenuStrip_playlistItem
            // 
            contextMenuStrip_playlistItem.Items.AddRange(new ToolStripItem[] { toolStripMenuItem_resetPlaylistRate, toolStripMenuItem_removeFromEnsemble });
            contextMenuStrip_playlistItem.Name = "contextMenuStrip_playlistItem";
            contextMenuStrip_playlistItem.Size = new Size(201, 48);
            contextMenuStrip_playlistItem.Opening += contextMenuStrip_playlistItem_Opening;
            // 
            // toolStripMenuItem_resetPlaylistRate
            // 
            toolStripMenuItem_resetPlaylistRate.Name = "toolStripMenuItem_resetPlaylistRate";
            toolStripMenuItem_resetPlaylistRate.Size = new Size(200, 22);
            toolStripMenuItem_resetPlaylistRate.Text = "Center/Reset Rate";
            toolStripMenuItem_resetPlaylistRate.Click += toolStripMenuItem_resetPlaylistRate_Click;
            // 
            // toolStripMenuItem_removeFromEnsemble
            // 
            toolStripMenuItem_removeFromEnsemble.Name = "toolStripMenuItem_removeFromEnsemble";
            toolStripMenuItem_removeFromEnsemble.Size = new Size(200, 22);
            toolStripMenuItem_removeFromEnsemble.Text = "Remove from ensemble";
            toolStripMenuItem_removeFromEnsemble.Click += toolStripMenuItem_removeFromEnsemble_Click;
            // 
            // label_targetMode
            // 
            label_targetMode.AutoEllipsis = true;
            label_targetMode.Location = new Point(12, 4);
            label_targetMode.Name = "label_targetMode";
            label_targetMode.Size = new Size(408, 18);
            label_targetMode.TabIndex = 9;
            label_targetMode.Text = "Target: none";
            toolTip_playlistTracks.SetToolTip(label_targetMode, resources.GetString("label_targetMode.ToolTip"));
            // 
            // button_playlistAllOn
            // 
            button_playlistAllOn.Font = new Font("Bahnschrift Light Condensed", 8.25F, FontStyle.Regular, GraphicsUnit.Point, 0);
            button_playlistAllOn.Location = new Point(426, 3);
            button_playlistAllOn.Name = "button_playlistAllOn";
            button_playlistAllOn.Size = new Size(23, 19);
            button_playlistAllOn.TabIndex = 10;
            button_playlistAllOn.TabStop = false;
            button_playlistAllOn.Text = "+";
            toolTip_playlistTracks.SetToolTip(button_playlistAllOn, "Check all rows for Ctrl+loop actions. Checking does not change loops or playback.");
            button_playlistAllOn.UseVisualStyleBackColor = true;
            button_playlistAllOn.Click += button_playlistAllOn_Click;
            // 
            // button_playlistAllOff
            // 
            button_playlistAllOff.Font = new Font("Bahnschrift Light Condensed", 8.25F, FontStyle.Regular, GraphicsUnit.Point, 0);
            button_playlistAllOff.Location = new Point(453, 3);
            button_playlistAllOff.Name = "button_playlistAllOff";
            button_playlistAllOff.Size = new Size(23, 19);
            button_playlistAllOff.TabIndex = 11;
            button_playlistAllOff.TabStop = false;
            button_playlistAllOff.Text = "−";
            toolTip_playlistTracks.SetToolTip(button_playlistAllOff, "Uncheck all rows. An empty Ctrl+loop group does nothing; loops and playback are unchanged.");
            toolTip_playlistTracks.SetToolTip(button_loop, "Loop range buttons (generated dynamically).\nEach button sets the loop fraction for all tracks.\nCtrl+click: set loop fraction on all tracks.\nFirst half (16..1/8): start looping backward from that range.\nSecond half (1/8..16): start looping forward from that range.");
            toolTip_playlistTracks.SetToolTip(button_copy, "Copy ensemble.\nNormal click: capture all tracks (backwards, multiplier×1 beat).\nCtrl+click: capture only current looped track(s).\nCtrl+click with multiple looped: capture only looped tracks in max range.");
            toolTip_playlistTracks.SetToolTip(domainUpDown_multiplier, "Loop multiplier.\nCtrl+click: toggle multiplier for all tracks.\nNo active loop: determines capture duration globally.");
            toolTip_playlistTracks.SetToolTip(numericUpDown_jump, "Jump ms.\nCtrl+click: increment/decrement for all tracks.\nSame for forward/backward buttons.");
            toolTip_playlistTracks.SetToolTip(button_forward, "Forward jump.\nCtrl+click: jump all tracks forward.");
            toolTip_playlistTracks.SetToolTip(button_backward, "Backward jump.\nCtrl+click: jump all tracks backward.");
            button_playlistAllOff.UseVisualStyleBackColor = true;
            button_playlistAllOff.Click += button_playlistAllOff_Click;
            // 
            // checkedListBox_playlistTracks
            // 
            checkedListBox_playlistTracks.ContextMenuStrip = contextMenuStrip_playlistItem;
            checkedListBox_playlistTracks.Font = new Font("Segoe UI", 8F, FontStyle.Regular, GraphicsUnit.Point, 0);
            checkedListBox_playlistTracks.FormattingEnabled = true;
            checkedListBox_playlistTracks.Location = new Point(12, 102);
            checkedListBox_playlistTracks.Name = "checkedListBox_playlistTracks";
            checkedListBox_playlistTracks.Size = new Size(464, 72);
            checkedListBox_playlistTracks.TabIndex = 12;
            toolTip_playlistTracks.SetToolTip(checkedListBox_playlistTracks, resources.GetString("checkedListBox_playlistTracks.ToolTip"));
            checkedListBox_playlistTracks.RatePositionChanged += checkedListBox_playlistTracks_RatePositionChanged;
            checkedListBox_playlistTracks.ItemCheck += checkedListBox_playlistTracks_ItemCheck;
            checkedListBox_playlistTracks.SelectedIndexChanged += checkedListBox_playlistTracks_SelectedIndexChanged;
            checkedListBox_playlistTracks.MouseDown += checkedListBox_playlistTracks_MouseDown;
            // 
            // panel_buttons
            // 
            panel_buttons.BackColor = SystemColors.ButtonFace;
            panel_buttons.Controls.Add(button_loop);
            panel_buttons.Location = new Point(12, 70);
            panel_buttons.Name = "panel_buttons";
            panel_buttons.Size = new Size(435, 29);
            panel_buttons.TabIndex = 0;
            // 
            // button_loop
            // 
            button_loop.Font = new Font("Bahnschrift Light Condensed", 8.25F, FontStyle.Regular, GraphicsUnit.Point, 0);
            button_loop.Location = new Point(3, 3);
            button_loop.Name = "button_loop";
            button_loop.Size = new Size(23, 23);
            button_loop.TabIndex = 1;
            button_loop.Text = "4";
            button_loop.UseVisualStyleBackColor = true;
            // 
            // button_copy
            // 
            button_copy.Font = new Font("Bahnschrift Light Condensed", 8.25F, FontStyle.Regular, GraphicsUnit.Point, 0);
            button_copy.Location = new Point(453, 73);
            button_copy.Name = "button_copy";
            button_copy.Size = new Size(23, 23);
            button_copy.TabIndex = 2;
            button_copy.TabStop = false;
            button_copy.Text = "⿻";
            button_copy.UseVisualStyleBackColor = true;
            button_copy.Click += button_copy_Click;
            // 
            // numericUpDown_multiplier
            // 
            domainUpDown_multiplier.Items.Add("16");
            domainUpDown_multiplier.Items.Add("15");
            domainUpDown_multiplier.Items.Add("14");
            domainUpDown_multiplier.Items.Add("13");
            domainUpDown_multiplier.Items.Add("12");
            domainUpDown_multiplier.Items.Add("11");
            domainUpDown_multiplier.Items.Add("10");
            domainUpDown_multiplier.Items.Add("9");
            domainUpDown_multiplier.Items.Add("8");
            domainUpDown_multiplier.Items.Add("7");
            domainUpDown_multiplier.Items.Add("6");
            domainUpDown_multiplier.Items.Add("5");
            domainUpDown_multiplier.Items.Add("4");
            domainUpDown_multiplier.Items.Add("3");
            domainUpDown_multiplier.Items.Add("2");
            domainUpDown_multiplier.Items.Add("1");
            domainUpDown_multiplier.Items.Add("1/2");
            domainUpDown_multiplier.Items.Add("1/4");
            domainUpDown_multiplier.Items.Add("1/8");
            domainUpDown_multiplier.Location = new Point(12, 44);
            domainUpDown_multiplier.Name = "domainUpDown_multiplier";
            domainUpDown_multiplier.Size = new Size(45, 23);
            domainUpDown_multiplier.TabIndex = 3;
            domainUpDown_multiplier.SelectedItem = "1";
            domainUpDown_multiplier.SelectedItemChanged += domainUpDown_multiplier_SelectedItemChanged;
            // 
            // label_info_multiplier
            // 
            label_info_multiplier.AutoSize = true;
            label_info_multiplier.Location = new Point(12, 26);
            label_info_multiplier.Name = "label_info_multiplier";
            label_info_multiplier.Size = new Size(35, 15);
            label_info_multiplier.TabIndex = 4;
            label_info_multiplier.Text = "Multi";
            // 
            // numericUpDown_jump
            // 
            numericUpDown_jump.DecimalPlaces = 1;
            numericUpDown_jump.Location = new Point(368, 41);
            numericUpDown_jump.Maximum = new decimal(new int[] { 10000, 0, 0, 0 });
            numericUpDown_jump.Minimum = new decimal(new int[] { 1, 0, 0, 131072 });
            numericUpDown_jump.Name = "numericUpDown_jump";
            numericUpDown_jump.Size = new Size(60, 23);
            numericUpDown_jump.TabIndex = 5;
            numericUpDown_jump.Value = new decimal(new int[] { 1, 0, 0, 0 });
            numericUpDown_jump.ValueChanged += numericUpDown_jump_ValueChanged;
            // 
            // button_forward
            // 
            button_forward.Font = new Font("Bahnschrift Light Condensed", 8.25F, FontStyle.Regular, GraphicsUnit.Point, 0);
            button_forward.Location = new Point(434, 40);
            button_forward.Name = "button_forward";
            button_forward.Size = new Size(23, 23);
            button_forward.TabIndex = 6;
            button_forward.TabStop = false;
            button_forward.Text = "→";
            button_forward.UseVisualStyleBackColor = true;
            button_forward.Click += button_forward_Click;
            // 
            // button_backward
            // 
            button_backward.Font = new Font("Bahnschrift Light Condensed", 8.25F, FontStyle.Regular, GraphicsUnit.Point, 0);
            button_backward.Location = new Point(339, 41);
            button_backward.Name = "button_backward";
            button_backward.Size = new Size(23, 23);
            button_backward.TabIndex = 7;
            button_backward.TabStop = false;
            button_backward.Text = "←";
            button_backward.UseVisualStyleBackColor = true;
            button_backward.Click += button_backward_Click;
            // 
            // label_info_jump
            // 
            label_info_jump.AutoSize = true;
            label_info_jump.Location = new Point(368, 23);
            label_info_jump.Name = "label_info_jump";
            label_info_jump.Size = new Size(55, 15);
            label_info_jump.TabIndex = 8;
            label_info_jump.Text = "Jump ms";
            // 
            // comboBox_drops
            // 
            comboBox_drops.FormattingEnabled = true;
            comboBox_drops.Location = new Point(166, 40);
            comboBox_drops.Name = "comboBox_drops";
            comboBox_drops.Size = new Size(167, 23);
            comboBox_drops.TabIndex = 13;
            comboBox_drops.SelectedIndexChanged += comboBox_drops_SelectedIndexChanged;
            // 
            // label_info_manageDrops
            // 
            label_info_manageDrops.AutoSize = true;
            label_info_manageDrops.Location = new Point(73, 45);
            label_info_manageDrops.Name = "label_info_manageDrops";
            label_info_manageDrops.Size = new Size(87, 15);
            label_info_manageDrops.TabIndex = 14;
            label_info_manageDrops.Text = "Manage Drops:";
            // 
            // LoopControl
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(484, 187);
            Controls.Add(label_info_manageDrops);
            Controls.Add(comboBox_drops);
            Controls.Add(button_playlistAllOff);
            Controls.Add(button_playlistAllOn);
            Controls.Add(checkedListBox_playlistTracks);
            Controls.Add(label_targetMode);
            Controls.Add(label_info_jump);
            Controls.Add(button_backward);
            Controls.Add(button_forward);
            Controls.Add(numericUpDown_jump);
            Controls.Add(label_info_multiplier);
            Controls.Add(domainUpDown_multiplier);
            Controls.Add(button_copy);
            Controls.Add(panel_buttons);
            MaximizeBox = false;
            MaximumSize = new Size(500, 226);
            MinimizeBox = false;
            MinimumSize = new Size(500, 226);
            Name = "LoopControl";
            Text = "Loop Control";
            contextMenuStrip_playlistItem.ResumeLayout(false);
            panel_buttons.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)numericUpDown_jump).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Panel panel_buttons;
        private Button button_loop;
        private Button button_copy;
        private DomainUpDown domainUpDown_multiplier;
        private Label label_info_multiplier;
        private NumericUpDown numericUpDown_jump;
        private Button button_forward;
        private Button button_backward;
        private Label label_info_jump;
        private Label label_targetMode;
        private Button button_playlistAllOn;
        private Button button_playlistAllOff;
        private ModularAudience.Forms.Controls.PlaylistTrackListBox checkedListBox_playlistTracks;
        private System.Windows.Forms.ContextMenuStrip contextMenuStrip_playlistItem;
        private System.Windows.Forms.ToolStripMenuItem toolStripMenuItem_removeFromEnsemble;
        private System.Windows.Forms.ToolStripMenuItem toolStripMenuItem_resetPlaylistRate;
        private ToolTip toolTip_playlistTracks;
        private ComboBox comboBox_drops;
        private Label label_info_manageDrops;
    }
}