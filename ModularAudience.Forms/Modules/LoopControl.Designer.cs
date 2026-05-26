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
            this.contextMenuStrip_playlistItem = new ContextMenuStrip(this.components);
            this.toolStripMenuItem_removeFromEnsemble = new ToolStripMenuItem();
            this.panel_buttons = new Panel();
            this.button_loop = new Button();
            this.button_copy = new Button();
            this.numericUpDown_multiplier = new NumericUpDown();
            this.label_info_multiplier = new Label();
            this.numericUpDown_jump = new NumericUpDown();
            this.button_forward = new Button();
            this.button_backward = new Button();
            this.label_info_jump = new Label();
            this.label_targetMode = new Label();
            this.button_playlistAllOn = new Button();
            this.button_playlistAllOff = new Button();
            this.checkedListBox_playlistTracks = new CheckedListBox();
            this.comboBox_drops = new ComboBox();
            this.label_info_manageDrops = new Label();
            this.contextMenuStrip_playlistItem.SuspendLayout();
            this.panel_buttons.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_multiplier).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_jump).BeginInit();
            this.SuspendLayout();
            // 
            // contextMenuStrip_playlistItem
            // 
            this.contextMenuStrip_playlistItem.Items.AddRange(new ToolStripItem[] { this.toolStripMenuItem_removeFromEnsemble });
            this.contextMenuStrip_playlistItem.Name = "contextMenuStrip_playlistItem";
            this.contextMenuStrip_playlistItem.Size = new Size(201, 26);
            this.contextMenuStrip_playlistItem.Opening += this.contextMenuStrip_playlistItem_Opening;
            // 
            // toolStripMenuItem_removeFromEnsemble
            // 
            this.toolStripMenuItem_removeFromEnsemble.Name = "toolStripMenuItem_removeFromEnsemble";
            this.toolStripMenuItem_removeFromEnsemble.Size = new Size(200, 22);
            this.toolStripMenuItem_removeFromEnsemble.Text = "Remove from ensemble";
            this.toolStripMenuItem_removeFromEnsemble.Click += this.toolStripMenuItem_removeFromEnsemble_Click;
            // 
            // panel_buttons
            // 
            this.panel_buttons.BackColor = SystemColors.ButtonFace;
            this.panel_buttons.Controls.Add(this.button_loop);
            this.panel_buttons.Location = new Point(12, 70);
            this.panel_buttons.Name = "panel_buttons";
            this.panel_buttons.Size = new Size(435, 29);
            this.panel_buttons.TabIndex = 0;
            // 
            // button_loop
            // 
            this.button_loop.Font = new Font("Bahnschrift Light Condensed", 8.25F, FontStyle.Regular, GraphicsUnit.Point,  0);
            this.button_loop.Location = new Point(3, 3);
            this.button_loop.Name = "button_loop";
            this.button_loop.Size = new Size(23, 23);
            this.button_loop.TabIndex = 1;
            this.button_loop.Text = "4";
            this.button_loop.UseVisualStyleBackColor = true;
            // 
            // button_copy
            // 
            this.button_copy.Font = new Font("Bahnschrift Light Condensed", 8.25F, FontStyle.Regular, GraphicsUnit.Point,  0);
            this.button_copy.Location = new Point(453, 73);
            this.button_copy.Name = "button_copy";
            this.button_copy.Size = new Size(23, 23);
            this.button_copy.TabIndex = 2;
            this.button_copy.TabStop = false;
            this.button_copy.Text = "⿻";
            this.button_copy.UseVisualStyleBackColor = true;
            this.button_copy.Click += this.button_copy_Click;
            // 
            // numericUpDown_multiplier
            // 
            this.numericUpDown_multiplier.Location = new Point(12, 44);
            this.numericUpDown_multiplier.Maximum = new decimal(new int[] { 32, 0, 0, 0 });
            this.numericUpDown_multiplier.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numericUpDown_multiplier.Name = "numericUpDown_multiplier";
            this.numericUpDown_multiplier.Size = new Size(40, 23);
            this.numericUpDown_multiplier.TabIndex = 3;
            this.numericUpDown_multiplier.Value = new decimal(new int[] { 1, 0, 0, 0 });
            this.numericUpDown_multiplier.ValueChanged += this.numericUpDown_multiplier_ValueChanged;
            // 
            // label_info_multiplier
            // 
            this.label_info_multiplier.AutoSize = true;
            this.label_info_multiplier.Location = new Point(12, 26);
            this.label_info_multiplier.Name = "label_info_multiplier";
            this.label_info_multiplier.Size = new Size(35, 15);
            this.label_info_multiplier.TabIndex = 4;
            this.label_info_multiplier.Text = "Multi";
            // 
            // numericUpDown_jump
            // 
            this.numericUpDown_jump.Location = new Point(368, 41);
            this.numericUpDown_jump.Maximum = new decimal(new int[] { 10000, 0, 0, 0 });
            this.numericUpDown_jump.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numericUpDown_jump.Name = "numericUpDown_jump";
            this.numericUpDown_jump.Size = new Size(50, 23);
            this.numericUpDown_jump.TabIndex = 5;
            this.numericUpDown_jump.Value = new decimal(new int[] { 1, 0, 0, 0 });
            this.numericUpDown_jump.ValueChanged += this.numericUpDown_jump_ValueChanged;
            // 
            // button_forward
            // 
            this.button_forward.Font = new Font("Bahnschrift Light Condensed", 8.25F, FontStyle.Regular, GraphicsUnit.Point,  0);
            this.button_forward.Location = new Point(424, 41);
            this.button_forward.Name = "button_forward";
            this.button_forward.Size = new Size(23, 23);
            this.button_forward.TabIndex = 6;
            this.button_forward.TabStop = false;
            this.button_forward.Text = "→";
            this.button_forward.UseVisualStyleBackColor = true;
            this.button_forward.Click += this.button_forward_Click;
            // 
            // button_backward
            // 
            this.button_backward.Font = new Font("Bahnschrift Light Condensed", 8.25F, FontStyle.Regular, GraphicsUnit.Point,  0);
            this.button_backward.Location = new Point(339, 41);
            this.button_backward.Name = "button_backward";
            this.button_backward.Size = new Size(23, 23);
            this.button_backward.TabIndex = 7;
            this.button_backward.TabStop = false;
            this.button_backward.Text = "←";
            this.button_backward.UseVisualStyleBackColor = true;
            this.button_backward.Click += this.button_backward_Click;
            // 
            // label_info_jump
            // 
            this.label_info_jump.AutoSize = true;
            this.label_info_jump.Location = new Point(368, 23);
            this.label_info_jump.Name = "label_info_jump";
            this.label_info_jump.Size = new Size(55, 15);
            this.label_info_jump.TabIndex = 8;
            this.label_info_jump.Text = "Jump ms";
            // 
            // label_targetMode
            // 
            this.label_targetMode.AutoEllipsis = true;
            this.label_targetMode.Location = new Point(12, 4);
            this.label_targetMode.Name = "label_targetMode";
            this.label_targetMode.Size = new Size(464, 18);
            this.label_targetMode.TabIndex = 9;
            this.label_targetMode.Text = "Target: none";
            // 
            // button_playlistAllOn
            // 
            this.button_playlistAllOn.Font = new Font("Bahnschrift Light Condensed", 8.25F, FontStyle.Regular, GraphicsUnit.Point,  0);
            this.button_playlistAllOn.Location = new Point(426, 3);
            this.button_playlistAllOn.Name = "button_playlistAllOn";
            this.button_playlistAllOn.Size = new Size(23, 19);
            this.button_playlistAllOn.TabIndex = 10;
            this.button_playlistAllOn.TabStop = false;
            this.button_playlistAllOn.Text = "+";
            this.button_playlistAllOn.UseVisualStyleBackColor = true;
            this.button_playlistAllOn.Click += this.button_playlistAllOn_Click;
            // 
            // button_playlistAllOff
            // 
            this.button_playlistAllOff.Font = new Font("Bahnschrift Light Condensed", 8.25F, FontStyle.Regular, GraphicsUnit.Point,  0);
            this.button_playlistAllOff.Location = new Point(453, 3);
            this.button_playlistAllOff.Name = "button_playlistAllOff";
            this.button_playlistAllOff.Size = new Size(23, 19);
            this.button_playlistAllOff.TabIndex = 11;
            this.button_playlistAllOff.TabStop = false;
            this.button_playlistAllOff.Text = "−";
            this.button_playlistAllOff.UseVisualStyleBackColor = true;
            this.button_playlistAllOff.Click += this.button_playlistAllOff_Click;
            // 
            // checkedListBox_playlistTracks
            // 
            this.checkedListBox_playlistTracks.ContextMenuStrip = this.contextMenuStrip_playlistItem;
            this.checkedListBox_playlistTracks.Font = new Font("Segoe UI", 8F, FontStyle.Regular, GraphicsUnit.Point,  0);
            this.checkedListBox_playlistTracks.FormattingEnabled = true;
            this.checkedListBox_playlistTracks.Location = new Point(12, 102);
            this.checkedListBox_playlistTracks.Name = "checkedListBox_playlistTracks";
            this.checkedListBox_playlistTracks.Size = new Size(464, 72);
            this.checkedListBox_playlistTracks.TabIndex = 12;
            this.checkedListBox_playlistTracks.MouseUp += this.checkedListBox_playlistTracks_MouseUp;
            // 
            // comboBox_drops
            // 
            this.comboBox_drops.FormattingEnabled = true;
            this.comboBox_drops.Location = new Point(166, 40);
            this.comboBox_drops.Name = "comboBox_drops";
            this.comboBox_drops.Size = new Size(167, 23);
            this.comboBox_drops.TabIndex = 13;
            this.comboBox_drops.SelectedIndexChanged += this.comboBox_drops_SelectedIndexChanged;
            // 
            // label_info_manageDrops
            // 
            this.label_info_manageDrops.AutoSize = true;
            this.label_info_manageDrops.Location = new Point(73, 45);
            this.label_info_manageDrops.Name = "label_info_manageDrops";
            this.label_info_manageDrops.Size = new Size(87, 15);
            this.label_info_manageDrops.TabIndex = 14;
            this.label_info_manageDrops.Text = "Manage Drops:";
            // 
            // LoopControl
            // 
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(484, 187);
            this.Controls.Add(this.label_info_manageDrops);
            this.Controls.Add(this.comboBox_drops);
            this.Controls.Add(this.button_playlistAllOff);
            this.Controls.Add(this.button_playlistAllOn);
            this.Controls.Add(this.checkedListBox_playlistTracks);
            this.Controls.Add(this.label_targetMode);
            this.Controls.Add(this.label_info_jump);
            this.Controls.Add(this.button_backward);
            this.Controls.Add(this.button_forward);
            this.Controls.Add(this.numericUpDown_jump);
            this.Controls.Add(this.label_info_multiplier);
            this.Controls.Add(this.numericUpDown_multiplier);
            this.Controls.Add(this.button_copy);
            this.Controls.Add(this.panel_buttons);
            this.MaximizeBox = false;
            this.MaximumSize = new Size(500, 226);
            this.MinimizeBox = false;
            this.MinimumSize = new Size(500, 226);
            this.Name = "LoopControl";
            this.Text = "Loop Control";
            this.contextMenuStrip_playlistItem.ResumeLayout(false);
            this.panel_buttons.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_multiplier).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_jump).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private Panel panel_buttons;
        private Button button_loop;
        private Button button_copy;
        private NumericUpDown numericUpDown_multiplier;
        private Label label_info_multiplier;
        private NumericUpDown numericUpDown_jump;
        private Button button_forward;
        private Button button_backward;
        private Label label_info_jump;
        private Label label_targetMode;
        private Button button_playlistAllOn;
        private Button button_playlistAllOff;
        private CheckedListBox checkedListBox_playlistTracks;
        private System.Windows.Forms.ContextMenuStrip contextMenuStrip_playlistItem;
        private System.Windows.Forms.ToolStripMenuItem toolStripMenuItem_removeFromEnsemble;
        private ComboBox comboBox_drops;
        private Label label_info_manageDrops;
    }
}