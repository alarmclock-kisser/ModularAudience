namespace ModularAudience.Forms
{
    partial class AudioCollectionView
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
            listBox_audios = new ListBox();
            checkBox_autoPlay = new CheckBox();
            button_export = new Button();
            checkBox_preview = new CheckBox();
            contextMenuStrip_audios = new ContextMenuStrip(components);
            menuToolStripItem_rename = new ToolStripMenuItem();
            menuToolStripItem_clone = new ToolStripMenuItem();
            menuToolStripItem_editTags = new ToolStripMenuItem();
            menuToolStripItem_splitEqualParts = new ToolStripMenuItem();
            menuToolStripItem_splitEqualParts2 = new ToolStripMenuItem();
            menuToolStripItem_splitEqualParts4 = new ToolStripMenuItem();
            menuToolStripItem_splitEqualParts8 = new ToolStripMenuItem();
            menuToolStripItem_splitEqualParts16 = new ToolStripMenuItem();
            menuToolStripItem_splitEqualParts32 = new ToolStripMenuItem();
            menuToolStripItem_atomize = new ToolStripMenuItem();
            menuToolStripItem_atomizeRun = new ToolStripMenuItem();
            menuToolStripItem_atomizeSensitivity = new ToolStripMenuItem();
            menuToolStripItem_atomizeSensitivityConservative = new ToolStripMenuItem();
            menuToolStripItem_atomizeSensitivityBalanced = new ToolStripMenuItem();
            menuToolStripItem_atomizeSensitivityAggressive = new ToolStripMenuItem();
            menuToolStripItem_atomizeMinSlice = new ToolStripMenuItem();
            menuToolStripItem_atomizeMinSlice40 = new ToolStripMenuItem();
            menuToolStripItem_atomizeMinSlice80 = new ToolStripMenuItem();
            menuToolStripItem_atomizeMinSlice140 = new ToolStripMenuItem();
            menuToolStripItem_atomizeTailPadding = new ToolStripMenuItem();
            menuToolStripItem_atomizeTail10 = new ToolStripMenuItem();
            menuToolStripItem_atomizeTail30 = new ToolStripMenuItem();
            menuToolStripItem_atomizeTail60 = new ToolStripMenuItem();
            menuToolStripItem_delete = new ToolStripMenuItem();
            menuToolStripItem_toNewCollection = new ToolStripMenuItem();
            menuToolStripItem_addIndexToNames = new ToolStripMenuItem();
            menuToolStripItem_aggregateMixSelected = new ToolStripMenuItem();
            menuToolStripItem_timeStretchSelected = new ToolStripMenuItem();
            menuToolStripItem_demucsSeparateSelected = new ToolStripMenuItem();
            contextMenuStrip_audios.SuspendLayout();
            SuspendLayout();
            // 
            // contextMenuStrip_audios
            // 
            contextMenuStrip_audios.Items.AddRange(new ToolStripItem[] { menuToolStripItem_rename, menuToolStripItem_clone, menuToolStripItem_editTags, menuToolStripItem_splitEqualParts, menuToolStripItem_atomize, menuToolStripItem_delete, menuToolStripItem_toNewCollection, menuToolStripItem_addIndexToNames, menuToolStripItem_aggregateMixSelected, menuToolStripItem_timeStretchSelected, menuToolStripItem_demucsSeparateSelected });
            contextMenuStrip_audios.Name = "contextMenuStrip_audios";
            contextMenuStrip_audios.Size = new Size(213, 224);
            contextMenuStrip_audios.Opening += contextMenuStrip_audios_Opening;
            // 
            // listBox_audios
            // 
            listBox_audios.FormattingEnabled = true;
            listBox_audios.Location = new Point(12, 27);
            listBox_audios.Name = "listBox_audios";
            listBox_audios.SelectionMode = SelectionMode.MultiExtended;
            listBox_audios.Size = new Size(220, 289);
            listBox_audios.TabIndex = 0;
            listBox_audios.SelectedIndexChanged += listBox_audios_SelectedIndexChanged;
            // 
            // checkBox_autoPlay
            // 
            checkBox_autoPlay.AutoSize = true;
            checkBox_autoPlay.Location = new Point(155, 2);
            checkBox_autoPlay.Name = "checkBox_autoPlay";
            checkBox_autoPlay.Size = new Size(77, 19);
            checkBox_autoPlay.TabIndex = 1;
            checkBox_autoPlay.Text = "Auto Play";
            checkBox_autoPlay.UseVisualStyleBackColor = true;
            // 
            // button_export
            // 
            button_export.BackColor = Color.FromArgb(192, 255, 255);
            button_export.Location = new Point(12, 2);
            button_export.Name = "button_export";
            button_export.Size = new Size(60, 23);
            button_export.TabIndex = 2;
            button_export.Text = "Export";
            button_export.UseVisualStyleBackColor = false;
            button_export.Click += button_export_Click;
            // 
            // checkBox_preview
            // 
            checkBox_preview.AutoSize = true;
            checkBox_preview.Location = new Point(82, 2);
            checkBox_preview.Name = "checkBox_preview";
            checkBox_preview.Size = new Size(67, 19);
            checkBox_preview.TabIndex = 3;
            checkBox_preview.Text = "Preview";
            checkBox_preview.UseVisualStyleBackColor = true;
            // 
            // menuToolStripItem_rename
            // 
            menuToolStripItem_rename.Name = "menuToolStripItem_rename";
            menuToolStripItem_rename.Size = new Size(212, 22);
            menuToolStripItem_rename.Text = "Rename";
            menuToolStripItem_rename.Click += menuToolStripItem_rename_Click;
            // 
            // menuToolStripItem_clone
            // 
            menuToolStripItem_clone.Name = "menuToolStripItem_clone";
            menuToolStripItem_clone.Size = new Size(212, 22);
            menuToolStripItem_clone.Text = "Clone";
            menuToolStripItem_clone.Click += menuToolStripItem_clone_Click;
            // 
            // menuToolStripItem_editTags
            // 
            menuToolStripItem_editTags.Name = "menuToolStripItem_editTags";
            menuToolStripItem_editTags.Size = new Size(212, 22);
            menuToolStripItem_editTags.Text = "Edit Tags";
            menuToolStripItem_editTags.Click += menuToolStripItem_editTags_Click;
            // 
            // menuToolStripItem_splitEqualParts
            // 
            menuToolStripItem_splitEqualParts.DropDownItems.AddRange(new ToolStripItem[] { menuToolStripItem_splitEqualParts2, menuToolStripItem_splitEqualParts4, menuToolStripItem_splitEqualParts8, menuToolStripItem_splitEqualParts16, menuToolStripItem_splitEqualParts32 });
            menuToolStripItem_splitEqualParts.Name = "menuToolStripItem_splitEqualParts";
            menuToolStripItem_splitEqualParts.Size = new Size(212, 22);
            menuToolStripItem_splitEqualParts.Text = "Split Into Equal Parts";
            // 
            // menuToolStripItem_splitEqualParts2
            // 
            menuToolStripItem_splitEqualParts2.Name = "menuToolStripItem_splitEqualParts2";
            menuToolStripItem_splitEqualParts2.Size = new Size(80, 22);
            menuToolStripItem_splitEqualParts2.Text = "2";
            menuToolStripItem_splitEqualParts2.Click += menuToolStripItem_splitEqualParts2_Click;
            // 
            // menuToolStripItem_splitEqualParts4
            // 
            menuToolStripItem_splitEqualParts4.Name = "menuToolStripItem_splitEqualParts4";
            menuToolStripItem_splitEqualParts4.Size = new Size(80, 22);
            menuToolStripItem_splitEqualParts4.Text = "4";
            menuToolStripItem_splitEqualParts4.Click += menuToolStripItem_splitEqualParts4_Click;
            // 
            // menuToolStripItem_splitEqualParts8
            // 
            menuToolStripItem_splitEqualParts8.Name = "menuToolStripItem_splitEqualParts8";
            menuToolStripItem_splitEqualParts8.Size = new Size(80, 22);
            menuToolStripItem_splitEqualParts8.Text = "8";
            menuToolStripItem_splitEqualParts8.Click += menuToolStripItem_splitEqualParts8_Click;
            // 
            // menuToolStripItem_splitEqualParts16
            // 
            menuToolStripItem_splitEqualParts16.Name = "menuToolStripItem_splitEqualParts16";
            menuToolStripItem_splitEqualParts16.Size = new Size(80, 22);
            menuToolStripItem_splitEqualParts16.Text = "16";
            menuToolStripItem_splitEqualParts16.Click += menuToolStripItem_splitEqualParts16_Click;
            // 
            // menuToolStripItem_splitEqualParts32
            // 
            menuToolStripItem_splitEqualParts32.Name = "menuToolStripItem_splitEqualParts32";
            menuToolStripItem_splitEqualParts32.Size = new Size(80, 22);
            menuToolStripItem_splitEqualParts32.Text = "32";
            menuToolStripItem_splitEqualParts32.Click += menuToolStripItem_splitEqualParts32_Click;
            // 
            // menuToolStripItem_atomize
            // 
            menuToolStripItem_atomize.Name = "menuToolStripItem_atomize";
            menuToolStripItem_atomize.Size = new Size(212, 22);
            menuToolStripItem_atomize.Text = "Atomize";
            menuToolStripItem_atomize.DropDownItems.AddRange(new ToolStripItem[] { menuToolStripItem_atomizeRun, menuToolStripItem_atomizeSensitivity, menuToolStripItem_atomizeMinSlice, menuToolStripItem_atomizeTailPadding });
            // 
            // menuToolStripItem_atomizeRun
            // 
            menuToolStripItem_atomizeRun.Name = "menuToolStripItem_atomizeRun";
            menuToolStripItem_atomizeRun.Size = new Size(180, 22);
            menuToolStripItem_atomizeRun.Text = "Run";
            menuToolStripItem_atomizeRun.Click += menuToolStripItem_atomize_Click;
            // 
            // menuToolStripItem_atomizeSensitivity
            // 
            menuToolStripItem_atomizeSensitivity.DropDownItems.AddRange(new ToolStripItem[] { menuToolStripItem_atomizeSensitivityConservative, menuToolStripItem_atomizeSensitivityBalanced, menuToolStripItem_atomizeSensitivityAggressive });
            menuToolStripItem_atomizeSensitivity.Name = "menuToolStripItem_atomizeSensitivity";
            menuToolStripItem_atomizeSensitivity.Size = new Size(180, 22);
            menuToolStripItem_atomizeSensitivity.Text = "Sensitivity";
            // 
            // menuToolStripItem_atomizeSensitivityConservative
            // 
            menuToolStripItem_atomizeSensitivityConservative.Name = "menuToolStripItem_atomizeSensitivityConservative";
            menuToolStripItem_atomizeSensitivityConservative.Size = new Size(143, 22);
            menuToolStripItem_atomizeSensitivityConservative.Text = "Conservative";
            menuToolStripItem_atomizeSensitivityConservative.Click += menuToolStripItem_atomizeSensitivityConservative_Click;
            // 
            // menuToolStripItem_atomizeSensitivityBalanced
            // 
            menuToolStripItem_atomizeSensitivityBalanced.Name = "menuToolStripItem_atomizeSensitivityBalanced";
            menuToolStripItem_atomizeSensitivityBalanced.Size = new Size(143, 22);
            menuToolStripItem_atomizeSensitivityBalanced.Text = "Balanced";
            menuToolStripItem_atomizeSensitivityBalanced.Click += menuToolStripItem_atomizeSensitivityBalanced_Click;
            // 
            // menuToolStripItem_atomizeSensitivityAggressive
            // 
            menuToolStripItem_atomizeSensitivityAggressive.Name = "menuToolStripItem_atomizeSensitivityAggressive";
            menuToolStripItem_atomizeSensitivityAggressive.Size = new Size(143, 22);
            menuToolStripItem_atomizeSensitivityAggressive.Text = "Aggressive";
            menuToolStripItem_atomizeSensitivityAggressive.Click += menuToolStripItem_atomizeSensitivityAggressive_Click;
            // 
            // menuToolStripItem_atomizeMinSlice
            // 
            menuToolStripItem_atomizeMinSlice.DropDownItems.AddRange(new ToolStripItem[] { menuToolStripItem_atomizeMinSlice40, menuToolStripItem_atomizeMinSlice80, menuToolStripItem_atomizeMinSlice140 });
            menuToolStripItem_atomizeMinSlice.Name = "menuToolStripItem_atomizeMinSlice";
            menuToolStripItem_atomizeMinSlice.Size = new Size(180, 22);
            menuToolStripItem_atomizeMinSlice.Text = "Min Slice";
            // 
            // menuToolStripItem_atomizeMinSlice40
            // 
            menuToolStripItem_atomizeMinSlice40.Name = "menuToolStripItem_atomizeMinSlice40";
            menuToolStripItem_atomizeMinSlice40.Size = new Size(115, 22);
            menuToolStripItem_atomizeMinSlice40.Text = "40 ms";
            menuToolStripItem_atomizeMinSlice40.Click += menuToolStripItem_atomizeMinSlice40_Click;
            // 
            // menuToolStripItem_atomizeMinSlice80
            // 
            menuToolStripItem_atomizeMinSlice80.Name = "menuToolStripItem_atomizeMinSlice80";
            menuToolStripItem_atomizeMinSlice80.Size = new Size(115, 22);
            menuToolStripItem_atomizeMinSlice80.Text = "80 ms";
            menuToolStripItem_atomizeMinSlice80.Click += menuToolStripItem_atomizeMinSlice80_Click;
            // 
            // menuToolStripItem_atomizeMinSlice140
            // 
            menuToolStripItem_atomizeMinSlice140.Name = "menuToolStripItem_atomizeMinSlice140";
            menuToolStripItem_atomizeMinSlice140.Size = new Size(115, 22);
            menuToolStripItem_atomizeMinSlice140.Text = "140 ms";
            menuToolStripItem_atomizeMinSlice140.Click += menuToolStripItem_atomizeMinSlice140_Click;
            // 
            // menuToolStripItem_atomizeTailPadding
            // 
            menuToolStripItem_atomizeTailPadding.DropDownItems.AddRange(new ToolStripItem[] { menuToolStripItem_atomizeTail10, menuToolStripItem_atomizeTail30, menuToolStripItem_atomizeTail60 });
            menuToolStripItem_atomizeTailPadding.Name = "menuToolStripItem_atomizeTailPadding";
            menuToolStripItem_atomizeTailPadding.Size = new Size(180, 22);
            menuToolStripItem_atomizeTailPadding.Text = "Tail Bias";
            // 
            // menuToolStripItem_atomizeTail10
            // 
            menuToolStripItem_atomizeTail10.Name = "menuToolStripItem_atomizeTail10";
            menuToolStripItem_atomizeTail10.Size = new Size(115, 22);
            menuToolStripItem_atomizeTail10.Text = "10 ms";
            menuToolStripItem_atomizeTail10.Click += menuToolStripItem_atomizeTail10_Click;
            // 
            // menuToolStripItem_atomizeTail30
            // 
            menuToolStripItem_atomizeTail30.Name = "menuToolStripItem_atomizeTail30";
            menuToolStripItem_atomizeTail30.Size = new Size(115, 22);
            menuToolStripItem_atomizeTail30.Text = "30 ms";
            menuToolStripItem_atomizeTail30.Click += menuToolStripItem_atomizeTail30_Click;
            // 
            // menuToolStripItem_atomizeTail60
            // 
            menuToolStripItem_atomizeTail60.Name = "menuToolStripItem_atomizeTail60";
            menuToolStripItem_atomizeTail60.Size = new Size(115, 22);
            menuToolStripItem_atomizeTail60.Text = "60 ms";
            menuToolStripItem_atomizeTail60.Click += menuToolStripItem_atomizeTail60_Click;
            // 
            // menuToolStripItem_delete
            // 
            menuToolStripItem_delete.Name = "menuToolStripItem_delete";
            menuToolStripItem_delete.Size = new Size(212, 22);
            menuToolStripItem_delete.Text = "Delete";
            menuToolStripItem_delete.Click += menuToolStripItem_delete_Click;
            // 
            // menuToolStripItem_toNewCollection
            // 
            menuToolStripItem_toNewCollection.Name = "menuToolStripItem_toNewCollection";
            menuToolStripItem_toNewCollection.Size = new Size(212, 22);
            menuToolStripItem_toNewCollection.Text = "To new Collection";
            menuToolStripItem_toNewCollection.Click += menuToolStripItem_toNewCollection_Click;
            // 
            // menuToolStripItem_addIndexToNames
            // 
            menuToolStripItem_addIndexToNames.CheckOnClick = true;
            menuToolStripItem_addIndexToNames.Name = "menuToolStripItem_addIndexToNames";
            menuToolStripItem_addIndexToNames.Size = new Size(212, 22);
            menuToolStripItem_addIndexToNames.Text = "Add Index to Names";
            menuToolStripItem_addIndexToNames.CheckedChanged += menuToolStripItem_addIndexToNames_CheckedChanged;
            // 
            // menuToolStripItem_aggregateMixSelected
            // 
            menuToolStripItem_aggregateMixSelected.Name = "menuToolStripItem_aggregateMixSelected";
            menuToolStripItem_aggregateMixSelected.Size = new Size(212, 22);
            menuToolStripItem_aggregateMixSelected.Text = "Aggregate Mix Selected";
            menuToolStripItem_aggregateMixSelected.Click += menuToolStripItem_aggregateMixSelected_Click;
            // 
            // menuToolStripItem_timeStretchSelected
            // 
            menuToolStripItem_timeStretchSelected.Name = "menuToolStripItem_timeStretchSelected";
            menuToolStripItem_timeStretchSelected.Size = new Size(212, 22);
            menuToolStripItem_timeStretchSelected.Text = "Time-Stretch Selected";
            menuToolStripItem_timeStretchSelected.Click += menuToolStripItem_timeStretchSelected_Click;
            // 
            // menuToolStripItem_demucsSeparateSelected
            // 
            menuToolStripItem_demucsSeparateSelected.Name = "menuToolStripItem_demucsSeparateSelected";
            menuToolStripItem_demucsSeparateSelected.Size = new Size(212, 22);
            menuToolStripItem_demucsSeparateSelected.Text = "Demucs Separate Selected";
            menuToolStripItem_demucsSeparateSelected.Click += menuToolStripItem_demucsSeparateSelected_Click;
            // 
            // AudioCollectionView
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(244, 321);
            Controls.Add(checkBox_preview);
            Controls.Add(button_export);
            Controls.Add(checkBox_autoPlay);
            Controls.Add(listBox_audios);
            MaximizeBox = false;
            MaximumSize = new Size(480, 8192);
            MinimizeBox = false;
            MinimumSize = new Size(200, 100);
            Name = "AudioCollectionView";
            Text = "Audio Collection #00";
            contextMenuStrip_audios.ResumeLayout(false);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private ListBox listBox_audios;
        private CheckBox checkBox_autoPlay;
        private Button button_export;
        private CheckBox checkBox_preview;
        private ContextMenuStrip contextMenuStrip_audios;
        private ToolStripMenuItem menuToolStripItem_rename;
        private ToolStripMenuItem menuToolStripItem_clone;
        private ToolStripMenuItem menuToolStripItem_editTags;
        private ToolStripMenuItem menuToolStripItem_splitEqualParts;
        private ToolStripMenuItem menuToolStripItem_splitEqualParts2;
        private ToolStripMenuItem menuToolStripItem_splitEqualParts4;
        private ToolStripMenuItem menuToolStripItem_splitEqualParts8;
        private ToolStripMenuItem menuToolStripItem_splitEqualParts16;
        private ToolStripMenuItem menuToolStripItem_splitEqualParts32;
        private ToolStripMenuItem menuToolStripItem_atomize;
        private ToolStripMenuItem menuToolStripItem_atomizeRun;
        private ToolStripMenuItem menuToolStripItem_atomizeSensitivity;
        private ToolStripMenuItem menuToolStripItem_atomizeSensitivityConservative;
        private ToolStripMenuItem menuToolStripItem_atomizeSensitivityBalanced;
        private ToolStripMenuItem menuToolStripItem_atomizeSensitivityAggressive;
        private ToolStripMenuItem menuToolStripItem_atomizeMinSlice;
        private ToolStripMenuItem menuToolStripItem_atomizeMinSlice40;
        private ToolStripMenuItem menuToolStripItem_atomizeMinSlice80;
        private ToolStripMenuItem menuToolStripItem_atomizeMinSlice140;
        private ToolStripMenuItem menuToolStripItem_atomizeTailPadding;
        private ToolStripMenuItem menuToolStripItem_atomizeTail10;
        private ToolStripMenuItem menuToolStripItem_atomizeTail30;
        private ToolStripMenuItem menuToolStripItem_atomizeTail60;
        private ToolStripMenuItem menuToolStripItem_delete;
        private ToolStripMenuItem menuToolStripItem_toNewCollection;
        private ToolStripMenuItem menuToolStripItem_addIndexToNames;
        private ToolStripMenuItem menuToolStripItem_aggregateMixSelected;
        private ToolStripMenuItem menuToolStripItem_timeStretchSelected;
        private ToolStripMenuItem menuToolStripItem_demucsSeparateSelected;
    }
}