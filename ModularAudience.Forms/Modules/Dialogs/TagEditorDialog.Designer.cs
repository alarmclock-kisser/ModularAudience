namespace ModularAudience.Forms.Modules.Dialogs
{
    partial class TagEditorDialog
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
            this.button_write = new Button();
            this.listBox_tags = new ListBox();
            this.checkBox_metaTags = new CheckBox();
            this.contextMenuStrip_tagMenu = new ContextMenuStrip(this.components);
            this.resetdefaultToolStripMenuItem = new ToolStripMenuItem();
            this.modifyToolStripMenuItem = new ToolStripMenuItem();
            this.toolStripTextBox_value = new ToolStripTextBox();
            this.contextMenuStrip_tagMenu.SuspendLayout();
            this.SuspendLayout();
            // 
            // button_write
            // 
            this.button_write.BackColor = SystemColors.Info;
            this.button_write.Location = new Point(117, 286);
            this.button_write.Name = "button_write";
            this.button_write.Size = new Size(75, 23);
            this.button_write.TabIndex = 0;
            this.button_write.Text = "Write Tags";
            this.button_write.UseVisualStyleBackColor = false;
            this.button_write.Click += this.button_write_Click;
            // 
            // listBox_tags
            // 
            this.listBox_tags.FormattingEnabled = true;
            this.listBox_tags.Location = new Point(12, 12);
            this.listBox_tags.Name = "listBox_tags";
            this.listBox_tags.Size = new Size(180, 259);
            this.listBox_tags.TabIndex = 1;
            // 
            // checkBox_metaTags
            // 
            this.checkBox_metaTags.AutoSize = true;
            this.checkBox_metaTags.Font = new Font("Bahnschrift SemiLight Condensed", 9.75F, FontStyle.Regular, GraphicsUnit.Point,  0);
            this.checkBox_metaTags.Location = new Point(12, 289);
            this.checkBox_metaTags.Name = "checkBox_metaTags";
            this.checkBox_metaTags.Size = new Size(95, 20);
            this.checkBox_metaTags.TabIndex = 2;
            this.checkBox_metaTags.Text = "Show Meta-Tags";
            this.checkBox_metaTags.TextAlign = ContentAlignment.MiddleCenter;
            this.checkBox_metaTags.UseVisualStyleBackColor = true;
            this.checkBox_metaTags.CheckedChanged += this.checkBox_metaTags_CheckedChanged;
            // 
            // contextMenuStrip_tagMenu
            // 
            this.contextMenuStrip_tagMenu.Items.AddRange(new ToolStripItem[] { this.resetdefaultToolStripMenuItem, this.modifyToolStripMenuItem });
            this.contextMenuStrip_tagMenu.Name = "contextMenuStrip_tagMenu";
            this.contextMenuStrip_tagMenu.Size = new Size(194, 70);
            // 
            // resetdefaultToolStripMenuItem
            // 
            this.resetdefaultToolStripMenuItem.Name = "resetdefaultToolStripMenuItem";
            this.resetdefaultToolStripMenuItem.ShortcutKeys =  Keys.Control | Keys.R;
            this.resetdefaultToolStripMenuItem.Size = new Size(193, 22);
            this.resetdefaultToolStripMenuItem.Text = "Reset (default)";
            this.resetdefaultToolStripMenuItem.Click += this.resetdefaultToolStripMenuItem_Click;
            // 
            // modifyToolStripMenuItem
            // 
            this.modifyToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { this.toolStripTextBox_value });
            this.modifyToolStripMenuItem.Name = "modifyToolStripMenuItem";
            this.modifyToolStripMenuItem.ShortcutKeys =  Keys.Control | Keys.M;
            this.modifyToolStripMenuItem.Size = new Size(193, 22);
            this.modifyToolStripMenuItem.Text = "Modify...";
            this.modifyToolStripMenuItem.Click += this.modifyToolStripMenuItem_Click;
            // 
            // toolStripTextBox_value
            // 
            this.toolStripTextBox_value.Name = "toolStripTextBox_value";
            this.toolStripTextBox_value.Size = new Size(120, 23);
            this.toolStripTextBox_value.Text = " - value - ";
            this.toolStripTextBox_value.Click += this.toolStripTextBox_value_Click;
            // 
            // TagEditorDialog
            // 
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(204, 321);
            this.Controls.Add(this.checkBox_metaTags);
            this.Controls.Add(this.listBox_tags);
            this.Controls.Add(this.button_write);
            this.MaximizeBox = false;
            this.MaximumSize = new Size(220, 360);
            this.MinimizeBox = false;
            this.MinimumSize = new Size(220, 360);
            this.Name = "TagEditorDialog";
            this.Text = "Tag Editor Dialog (0)";
            this.contextMenuStrip_tagMenu.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private Button button_write;
        private ListBox listBox_tags;
        private CheckBox checkBox_metaTags;
        private ContextMenuStrip contextMenuStrip_tagMenu;
        private ToolStripMenuItem resetdefaultToolStripMenuItem;
        private ToolStripMenuItem modifyToolStripMenuItem;
        private ToolStripTextBox toolStripTextBox_value;
    }
}