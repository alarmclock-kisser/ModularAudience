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
            menuToolStripItem_generateBreakbeat = new ToolStripMenuItem();
            menuToolStripItem_generateBreakbeatRun = new ToolStripMenuItem();
            menuToolStripItem_generateBreakbeatBpm = new ToolStripMenuItem();
            menuToolStripItem_generateBreakbeatBpm80 = new ToolStripMenuItem();
            menuToolStripItem_generateBreakbeatBpm875 = new ToolStripMenuItem();
            menuToolStripItem_generateBreakbeatBpm100 = new ToolStripMenuItem();
            menuToolStripItem_generateBreakbeatBpm120 = new ToolStripMenuItem();
            menuToolStripItem_generateBreakbeatBpm140 = new ToolStripMenuItem();
            menuToolStripItem_generateBreakbeatBars = new ToolStripMenuItem();
            menuToolStripItem_generateBreakbeatBars1 = new ToolStripMenuItem();
            menuToolStripItem_generateBreakbeatBars2 = new ToolStripMenuItem();
            menuToolStripItem_generateBreakbeatBars4 = new ToolStripMenuItem();
            menuToolStripItem_generateBreakbeatBars8 = new ToolStripMenuItem();
            menuToolStripItem_generateBreakbeatHits = new ToolStripMenuItem();
            menuToolStripItem_generateBreakbeatHits6 = new ToolStripMenuItem();
            menuToolStripItem_generateBreakbeatHits8 = new ToolStripMenuItem();
            menuToolStripItem_generateBreakbeatHits12 = new ToolStripMenuItem();
            menuToolStripItem_generateBreakbeatHits16 = new ToolStripMenuItem();
            menuToolStripItem_generateBreakbeatHits24 = new ToolStripMenuItem();
            menuToolStripItem_generateBreakbeatDensity = new ToolStripMenuItem();
            menuToolStripItem_generateBreakbeatDensitySparse = new ToolStripMenuItem();
            menuToolStripItem_generateBreakbeatDensityBalanced = new ToolStripMenuItem();
            menuToolStripItem_generateBreakbeatDensityDense = new ToolStripMenuItem();
            menuToolStripItem_generateBreakbeatDensityMax = new ToolStripMenuItem();
            menuToolStripItem_generateBreakbeatComplexity = new ToolStripMenuItem();
            menuToolStripItem_generateBreakbeatComplexityLow = new ToolStripMenuItem();
            menuToolStripItem_generateBreakbeatComplexityBalanced = new ToolStripMenuItem();
            menuToolStripItem_generateBreakbeatComplexityBusy = new ToolStripMenuItem();
            menuToolStripItem_generateBreakbeatComplexityWild = new ToolStripMenuItem();
            menuToolStripItem_generateBreakbeatResolution = new ToolStripMenuItem();
            menuToolStripItem_generateBreakbeatResolution16 = new ToolStripMenuItem();
            menuToolStripItem_generateBreakbeatResolution32 = new ToolStripMenuItem();
            menuToolStripItem_generateBreakbeatSwing = new ToolStripMenuItem();
            menuToolStripItem_generateBreakbeatSwing0 = new ToolStripMenuItem();
            menuToolStripItem_generateBreakbeatSwing6 = new ToolStripMenuItem();
            menuToolStripItem_generateBreakbeatSwing12 = new ToolStripMenuItem();
            menuToolStripItem_generateBreakbeatSwing18 = new ToolStripMenuItem();
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
            contextMenuStrip_audios.Items.AddRange(new ToolStripItem[] { menuToolStripItem_rename, menuToolStripItem_clone, menuToolStripItem_editTags, menuToolStripItem_splitEqualParts, menuToolStripItem_generateBreakbeat, menuToolStripItem_atomize, menuToolStripItem_delete, menuToolStripItem_toNewCollection, menuToolStripItem_addIndexToNames, menuToolStripItem_aggregateMixSelected, menuToolStripItem_timeStretchSelected, menuToolStripItem_demucsSeparateSelected });
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
            // menuToolStripItem_generateBreakbeat
            // 
            menuToolStripItem_generateBreakbeat.DropDownItems.AddRange(new ToolStripItem[] { menuToolStripItem_generateBreakbeatRun, menuToolStripItem_generateBreakbeatBpm, menuToolStripItem_generateBreakbeatBars, menuToolStripItem_generateBreakbeatHits, menuToolStripItem_generateBreakbeatDensity, menuToolStripItem_generateBreakbeatComplexity, menuToolStripItem_generateBreakbeatResolution, menuToolStripItem_generateBreakbeatSwing });
            menuToolStripItem_generateBreakbeat.Name = "menuToolStripItem_generateBreakbeat";
            menuToolStripItem_generateBreakbeat.Size = new Size(212, 22);
            menuToolStripItem_generateBreakbeat.Text = "Generate Breakbeat";
            // 
            // menuToolStripItem_generateBreakbeatRun
            // 
            menuToolStripItem_generateBreakbeatRun.Name = "menuToolStripItem_generateBreakbeatRun";
            menuToolStripItem_generateBreakbeatRun.Size = new Size(180, 22);
            menuToolStripItem_generateBreakbeatRun.Text = "Run";
            menuToolStripItem_generateBreakbeatRun.Click += menuToolStripItem_generateBreakbeatRun_Click;
            // 
            // menuToolStripItem_generateBreakbeatBpm
            // 
            menuToolStripItem_generateBreakbeatBpm.DropDownItems.AddRange(new ToolStripItem[] { menuToolStripItem_generateBreakbeatBpm80, menuToolStripItem_generateBreakbeatBpm875, menuToolStripItem_generateBreakbeatBpm100, menuToolStripItem_generateBreakbeatBpm120, menuToolStripItem_generateBreakbeatBpm140 });
            menuToolStripItem_generateBreakbeatBpm.Name = "menuToolStripItem_generateBreakbeatBpm";
            menuToolStripItem_generateBreakbeatBpm.Size = new Size(180, 22);
            menuToolStripItem_generateBreakbeatBpm.Text = "BPM";
            // 
            // menuToolStripItem_generateBreakbeatBpm80
            // 
            menuToolStripItem_generateBreakbeatBpm80.Name = "menuToolStripItem_generateBreakbeatBpm80";
            menuToolStripItem_generateBreakbeatBpm80.Size = new Size(99, 22);
            menuToolStripItem_generateBreakbeatBpm80.Text = "80";
            menuToolStripItem_generateBreakbeatBpm80.Click += menuToolStripItem_generateBreakbeatBpm80_Click;
            // 
            // menuToolStripItem_generateBreakbeatBpm875
            // 
            menuToolStripItem_generateBreakbeatBpm875.Name = "menuToolStripItem_generateBreakbeatBpm875";
            menuToolStripItem_generateBreakbeatBpm875.Size = new Size(99, 22);
            menuToolStripItem_generateBreakbeatBpm875.Text = "87.5";
            menuToolStripItem_generateBreakbeatBpm875.Click += menuToolStripItem_generateBreakbeatBpm875_Click;
            // 
            // menuToolStripItem_generateBreakbeatBpm100
            // 
            menuToolStripItem_generateBreakbeatBpm100.Name = "menuToolStripItem_generateBreakbeatBpm100";
            menuToolStripItem_generateBreakbeatBpm100.Size = new Size(99, 22);
            menuToolStripItem_generateBreakbeatBpm100.Text = "100";
            menuToolStripItem_generateBreakbeatBpm100.Click += menuToolStripItem_generateBreakbeatBpm100_Click;
            // 
            // menuToolStripItem_generateBreakbeatBpm120
            // 
            menuToolStripItem_generateBreakbeatBpm120.Name = "menuToolStripItem_generateBreakbeatBpm120";
            menuToolStripItem_generateBreakbeatBpm120.Size = new Size(99, 22);
            menuToolStripItem_generateBreakbeatBpm120.Text = "120";
            menuToolStripItem_generateBreakbeatBpm120.Click += menuToolStripItem_generateBreakbeatBpm120_Click;
            // 
            // menuToolStripItem_generateBreakbeatBpm140
            // 
            menuToolStripItem_generateBreakbeatBpm140.Name = "menuToolStripItem_generateBreakbeatBpm140";
            menuToolStripItem_generateBreakbeatBpm140.Size = new Size(99, 22);
            menuToolStripItem_generateBreakbeatBpm140.Text = "140";
            menuToolStripItem_generateBreakbeatBpm140.Click += menuToolStripItem_generateBreakbeatBpm140_Click;
            // 
            // menuToolStripItem_generateBreakbeatBars
            // 
            menuToolStripItem_generateBreakbeatBars.DropDownItems.AddRange(new ToolStripItem[] { menuToolStripItem_generateBreakbeatBars1, menuToolStripItem_generateBreakbeatBars2, menuToolStripItem_generateBreakbeatBars4, menuToolStripItem_generateBreakbeatBars8 });
            menuToolStripItem_generateBreakbeatBars.Name = "menuToolStripItem_generateBreakbeatBars";
            menuToolStripItem_generateBreakbeatBars.Size = new Size(180, 22);
            menuToolStripItem_generateBreakbeatBars.Text = "Bars";
            // 
            // menuToolStripItem_generateBreakbeatBars1
            // 
            menuToolStripItem_generateBreakbeatBars1.Name = "menuToolStripItem_generateBreakbeatBars1";
            menuToolStripItem_generateBreakbeatBars1.Size = new Size(80, 22);
            menuToolStripItem_generateBreakbeatBars1.Text = "1";
            menuToolStripItem_generateBreakbeatBars1.Click += menuToolStripItem_generateBreakbeatBars1_Click;
            // 
            // menuToolStripItem_generateBreakbeatBars2
            // 
            menuToolStripItem_generateBreakbeatBars2.Name = "menuToolStripItem_generateBreakbeatBars2";
            menuToolStripItem_generateBreakbeatBars2.Size = new Size(80, 22);
            menuToolStripItem_generateBreakbeatBars2.Text = "2";
            menuToolStripItem_generateBreakbeatBars2.Click += menuToolStripItem_generateBreakbeatBars2_Click;
            // 
            // menuToolStripItem_generateBreakbeatBars4
            // 
            menuToolStripItem_generateBreakbeatBars4.Name = "menuToolStripItem_generateBreakbeatBars4";
            menuToolStripItem_generateBreakbeatBars4.Size = new Size(80, 22);
            menuToolStripItem_generateBreakbeatBars4.Text = "4";
            menuToolStripItem_generateBreakbeatBars4.Click += menuToolStripItem_generateBreakbeatBars4_Click;
            // 
            // menuToolStripItem_generateBreakbeatBars8
            // 
            menuToolStripItem_generateBreakbeatBars8.Name = "menuToolStripItem_generateBreakbeatBars8";
            menuToolStripItem_generateBreakbeatBars8.Size = new Size(80, 22);
            menuToolStripItem_generateBreakbeatBars8.Text = "8";
            menuToolStripItem_generateBreakbeatBars8.Click += menuToolStripItem_generateBreakbeatBars8_Click;
            // 
            // menuToolStripItem_generateBreakbeatHits
            // 
            menuToolStripItem_generateBreakbeatHits.DropDownItems.AddRange(new ToolStripItem[] { menuToolStripItem_generateBreakbeatHits6, menuToolStripItem_generateBreakbeatHits8, menuToolStripItem_generateBreakbeatHits12, menuToolStripItem_generateBreakbeatHits16, menuToolStripItem_generateBreakbeatHits24 });
            menuToolStripItem_generateBreakbeatHits.Name = "menuToolStripItem_generateBreakbeatHits";
            menuToolStripItem_generateBreakbeatHits.Size = new Size(180, 22);
            menuToolStripItem_generateBreakbeatHits.Text = "Hits / Bar";
            // 
            // menuToolStripItem_generateBreakbeatHits6
            // 
            menuToolStripItem_generateBreakbeatHits6.Name = "menuToolStripItem_generateBreakbeatHits6";
            menuToolStripItem_generateBreakbeatHits6.Size = new Size(86, 22);
            menuToolStripItem_generateBreakbeatHits6.Text = "6";
            menuToolStripItem_generateBreakbeatHits6.Click += menuToolStripItem_generateBreakbeatHits6_Click;
            // 
            // menuToolStripItem_generateBreakbeatHits8
            // 
            menuToolStripItem_generateBreakbeatHits8.Name = "menuToolStripItem_generateBreakbeatHits8";
            menuToolStripItem_generateBreakbeatHits8.Size = new Size(86, 22);
            menuToolStripItem_generateBreakbeatHits8.Text = "8";
            menuToolStripItem_generateBreakbeatHits8.Click += menuToolStripItem_generateBreakbeatHits8_Click;
            // 
            // menuToolStripItem_generateBreakbeatHits12
            // 
            menuToolStripItem_generateBreakbeatHits12.Name = "menuToolStripItem_generateBreakbeatHits12";
            menuToolStripItem_generateBreakbeatHits12.Size = new Size(86, 22);
            menuToolStripItem_generateBreakbeatHits12.Text = "12";
            menuToolStripItem_generateBreakbeatHits12.Click += menuToolStripItem_generateBreakbeatHits12_Click;
            // 
            // menuToolStripItem_generateBreakbeatHits16
            // 
            menuToolStripItem_generateBreakbeatHits16.Name = "menuToolStripItem_generateBreakbeatHits16";
            menuToolStripItem_generateBreakbeatHits16.Size = new Size(86, 22);
            menuToolStripItem_generateBreakbeatHits16.Text = "16";
            menuToolStripItem_generateBreakbeatHits16.Click += menuToolStripItem_generateBreakbeatHits16_Click;
            // 
            // menuToolStripItem_generateBreakbeatHits24
            // 
            menuToolStripItem_generateBreakbeatHits24.Name = "menuToolStripItem_generateBreakbeatHits24";
            menuToolStripItem_generateBreakbeatHits24.Size = new Size(86, 22);
            menuToolStripItem_generateBreakbeatHits24.Text = "24";
            menuToolStripItem_generateBreakbeatHits24.Click += menuToolStripItem_generateBreakbeatHits24_Click;
            // 
            // menuToolStripItem_generateBreakbeatDensity
            // 
            menuToolStripItem_generateBreakbeatDensity.DropDownItems.AddRange(new ToolStripItem[] { menuToolStripItem_generateBreakbeatDensitySparse, menuToolStripItem_generateBreakbeatDensityBalanced, menuToolStripItem_generateBreakbeatDensityDense, menuToolStripItem_generateBreakbeatDensityMax });
            menuToolStripItem_generateBreakbeatDensity.Name = "menuToolStripItem_generateBreakbeatDensity";
            menuToolStripItem_generateBreakbeatDensity.Size = new Size(180, 22);
            menuToolStripItem_generateBreakbeatDensity.Text = "Density";
            // 
            // menuToolStripItem_generateBreakbeatDensitySparse
            // 
            menuToolStripItem_generateBreakbeatDensitySparse.Name = "menuToolStripItem_generateBreakbeatDensitySparse";
            menuToolStripItem_generateBreakbeatDensitySparse.Size = new Size(121, 22);
            menuToolStripItem_generateBreakbeatDensitySparse.Text = "Sparse";
            menuToolStripItem_generateBreakbeatDensitySparse.Click += menuToolStripItem_generateBreakbeatDensitySparse_Click;
            // 
            // menuToolStripItem_generateBreakbeatDensityBalanced
            // 
            menuToolStripItem_generateBreakbeatDensityBalanced.Name = "menuToolStripItem_generateBreakbeatDensityBalanced";
            menuToolStripItem_generateBreakbeatDensityBalanced.Size = new Size(121, 22);
            menuToolStripItem_generateBreakbeatDensityBalanced.Text = "Balanced";
            menuToolStripItem_generateBreakbeatDensityBalanced.Click += menuToolStripItem_generateBreakbeatDensityBalanced_Click;
            // 
            // menuToolStripItem_generateBreakbeatDensityDense
            // 
            menuToolStripItem_generateBreakbeatDensityDense.Name = "menuToolStripItem_generateBreakbeatDensityDense";
            menuToolStripItem_generateBreakbeatDensityDense.Size = new Size(121, 22);
            menuToolStripItem_generateBreakbeatDensityDense.Text = "Dense";
            menuToolStripItem_generateBreakbeatDensityDense.Click += menuToolStripItem_generateBreakbeatDensityDense_Click;
            // 
            // menuToolStripItem_generateBreakbeatDensityMax
            // 
            menuToolStripItem_generateBreakbeatDensityMax.Name = "menuToolStripItem_generateBreakbeatDensityMax";
            menuToolStripItem_generateBreakbeatDensityMax.Size = new Size(121, 22);
            menuToolStripItem_generateBreakbeatDensityMax.Text = "Maximum";
            menuToolStripItem_generateBreakbeatDensityMax.Click += menuToolStripItem_generateBreakbeatDensityMax_Click;
            // 
            // menuToolStripItem_generateBreakbeatComplexity
            // 
            menuToolStripItem_generateBreakbeatComplexity.DropDownItems.AddRange(new ToolStripItem[] { menuToolStripItem_generateBreakbeatComplexityLow, menuToolStripItem_generateBreakbeatComplexityBalanced, menuToolStripItem_generateBreakbeatComplexityBusy, menuToolStripItem_generateBreakbeatComplexityWild });
            menuToolStripItem_generateBreakbeatComplexity.Name = "menuToolStripItem_generateBreakbeatComplexity";
            menuToolStripItem_generateBreakbeatComplexity.Size = new Size(180, 22);
            menuToolStripItem_generateBreakbeatComplexity.Text = "Complexity";
            // 
            // menuToolStripItem_generateBreakbeatComplexityLow
            // 
            menuToolStripItem_generateBreakbeatComplexityLow.Name = "menuToolStripItem_generateBreakbeatComplexityLow";
            menuToolStripItem_generateBreakbeatComplexityLow.Size = new Size(121, 22);
            menuToolStripItem_generateBreakbeatComplexityLow.Text = "Low";
            menuToolStripItem_generateBreakbeatComplexityLow.Click += menuToolStripItem_generateBreakbeatComplexityLow_Click;
            // 
            // menuToolStripItem_generateBreakbeatComplexityBalanced
            // 
            menuToolStripItem_generateBreakbeatComplexityBalanced.Name = "menuToolStripItem_generateBreakbeatComplexityBalanced";
            menuToolStripItem_generateBreakbeatComplexityBalanced.Size = new Size(121, 22);
            menuToolStripItem_generateBreakbeatComplexityBalanced.Text = "Balanced";
            menuToolStripItem_generateBreakbeatComplexityBalanced.Click += menuToolStripItem_generateBreakbeatComplexityBalanced_Click;
            // 
            // menuToolStripItem_generateBreakbeatComplexityBusy
            // 
            menuToolStripItem_generateBreakbeatComplexityBusy.Name = "menuToolStripItem_generateBreakbeatComplexityBusy";
            menuToolStripItem_generateBreakbeatComplexityBusy.Size = new Size(121, 22);
            menuToolStripItem_generateBreakbeatComplexityBusy.Text = "Busy";
            menuToolStripItem_generateBreakbeatComplexityBusy.Click += menuToolStripItem_generateBreakbeatComplexityBusy_Click;
            // 
            // menuToolStripItem_generateBreakbeatComplexityWild
            // 
            menuToolStripItem_generateBreakbeatComplexityWild.Name = "menuToolStripItem_generateBreakbeatComplexityWild";
            menuToolStripItem_generateBreakbeatComplexityWild.Size = new Size(121, 22);
            menuToolStripItem_generateBreakbeatComplexityWild.Text = "Wild";
            menuToolStripItem_generateBreakbeatComplexityWild.Click += menuToolStripItem_generateBreakbeatComplexityWild_Click;
            // 
            // menuToolStripItem_generateBreakbeatResolution
            // 
            menuToolStripItem_generateBreakbeatResolution.DropDownItems.AddRange(new ToolStripItem[] { menuToolStripItem_generateBreakbeatResolution16, menuToolStripItem_generateBreakbeatResolution32 });
            menuToolStripItem_generateBreakbeatResolution.Name = "menuToolStripItem_generateBreakbeatResolution";
            menuToolStripItem_generateBreakbeatResolution.Size = new Size(180, 22);
            menuToolStripItem_generateBreakbeatResolution.Text = "Resolution";
            // 
            // menuToolStripItem_generateBreakbeatResolution16
            // 
            menuToolStripItem_generateBreakbeatResolution16.Name = "menuToolStripItem_generateBreakbeatResolution16";
            menuToolStripItem_generateBreakbeatResolution16.Size = new Size(80, 22);
            menuToolStripItem_generateBreakbeatResolution16.Text = "16";
            menuToolStripItem_generateBreakbeatResolution16.Click += menuToolStripItem_generateBreakbeatResolution16_Click;
            // 
            // menuToolStripItem_generateBreakbeatResolution32
            // 
            menuToolStripItem_generateBreakbeatResolution32.Name = "menuToolStripItem_generateBreakbeatResolution32";
            menuToolStripItem_generateBreakbeatResolution32.Size = new Size(80, 22);
            menuToolStripItem_generateBreakbeatResolution32.Text = "32";
            menuToolStripItem_generateBreakbeatResolution32.Click += menuToolStripItem_generateBreakbeatResolution32_Click;
            // 
            // menuToolStripItem_generateBreakbeatSwing
            // 
            menuToolStripItem_generateBreakbeatSwing.DropDownItems.AddRange(new ToolStripItem[] { menuToolStripItem_generateBreakbeatSwing0, menuToolStripItem_generateBreakbeatSwing6, menuToolStripItem_generateBreakbeatSwing12, menuToolStripItem_generateBreakbeatSwing18 });
            menuToolStripItem_generateBreakbeatSwing.Name = "menuToolStripItem_generateBreakbeatSwing";
            menuToolStripItem_generateBreakbeatSwing.Size = new Size(180, 22);
            menuToolStripItem_generateBreakbeatSwing.Text = "Swing";
            // 
            // menuToolStripItem_generateBreakbeatSwing0
            // 
            menuToolStripItem_generateBreakbeatSwing0.Name = "menuToolStripItem_generateBreakbeatSwing0";
            menuToolStripItem_generateBreakbeatSwing0.Size = new Size(86, 22);
            menuToolStripItem_generateBreakbeatSwing0.Text = "0 %";
            menuToolStripItem_generateBreakbeatSwing0.Click += menuToolStripItem_generateBreakbeatSwing0_Click;
            // 
            // menuToolStripItem_generateBreakbeatSwing6
            // 
            menuToolStripItem_generateBreakbeatSwing6.Name = "menuToolStripItem_generateBreakbeatSwing6";
            menuToolStripItem_generateBreakbeatSwing6.Size = new Size(86, 22);
            menuToolStripItem_generateBreakbeatSwing6.Text = "6 %";
            menuToolStripItem_generateBreakbeatSwing6.Click += menuToolStripItem_generateBreakbeatSwing6_Click;
            // 
            // menuToolStripItem_generateBreakbeatSwing12
            // 
            menuToolStripItem_generateBreakbeatSwing12.Name = "menuToolStripItem_generateBreakbeatSwing12";
            menuToolStripItem_generateBreakbeatSwing12.Size = new Size(86, 22);
            menuToolStripItem_generateBreakbeatSwing12.Text = "12 %";
            menuToolStripItem_generateBreakbeatSwing12.Click += menuToolStripItem_generateBreakbeatSwing12_Click;
            // 
            // menuToolStripItem_generateBreakbeatSwing18
            // 
            menuToolStripItem_generateBreakbeatSwing18.Name = "menuToolStripItem_generateBreakbeatSwing18";
            menuToolStripItem_generateBreakbeatSwing18.Size = new Size(86, 22);
            menuToolStripItem_generateBreakbeatSwing18.Text = "18 %";
            menuToolStripItem_generateBreakbeatSwing18.Click += menuToolStripItem_generateBreakbeatSwing18_Click;
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
        private ToolStripMenuItem menuToolStripItem_generateBreakbeat;
        private ToolStripMenuItem menuToolStripItem_generateBreakbeatRun;
        private ToolStripMenuItem menuToolStripItem_generateBreakbeatBpm;
        private ToolStripMenuItem menuToolStripItem_generateBreakbeatBpm80;
        private ToolStripMenuItem menuToolStripItem_generateBreakbeatBpm875;
        private ToolStripMenuItem menuToolStripItem_generateBreakbeatBpm100;
        private ToolStripMenuItem menuToolStripItem_generateBreakbeatBpm120;
        private ToolStripMenuItem menuToolStripItem_generateBreakbeatBpm140;
        private ToolStripMenuItem menuToolStripItem_generateBreakbeatBars;
        private ToolStripMenuItem menuToolStripItem_generateBreakbeatBars1;
        private ToolStripMenuItem menuToolStripItem_generateBreakbeatBars2;
        private ToolStripMenuItem menuToolStripItem_generateBreakbeatBars4;
        private ToolStripMenuItem menuToolStripItem_generateBreakbeatBars8;
        private ToolStripMenuItem menuToolStripItem_generateBreakbeatHits;
        private ToolStripMenuItem menuToolStripItem_generateBreakbeatHits6;
        private ToolStripMenuItem menuToolStripItem_generateBreakbeatHits8;
        private ToolStripMenuItem menuToolStripItem_generateBreakbeatHits12;
        private ToolStripMenuItem menuToolStripItem_generateBreakbeatHits16;
        private ToolStripMenuItem menuToolStripItem_generateBreakbeatHits24;
        private ToolStripMenuItem menuToolStripItem_generateBreakbeatDensity;
        private ToolStripMenuItem menuToolStripItem_generateBreakbeatDensitySparse;
        private ToolStripMenuItem menuToolStripItem_generateBreakbeatDensityBalanced;
        private ToolStripMenuItem menuToolStripItem_generateBreakbeatDensityDense;
        private ToolStripMenuItem menuToolStripItem_generateBreakbeatDensityMax;
        private ToolStripMenuItem menuToolStripItem_generateBreakbeatComplexity;
        private ToolStripMenuItem menuToolStripItem_generateBreakbeatComplexityLow;
        private ToolStripMenuItem menuToolStripItem_generateBreakbeatComplexityBalanced;
        private ToolStripMenuItem menuToolStripItem_generateBreakbeatComplexityBusy;
        private ToolStripMenuItem menuToolStripItem_generateBreakbeatComplexityWild;
        private ToolStripMenuItem menuToolStripItem_generateBreakbeatResolution;
        private ToolStripMenuItem menuToolStripItem_generateBreakbeatResolution16;
        private ToolStripMenuItem menuToolStripItem_generateBreakbeatResolution32;
        private ToolStripMenuItem menuToolStripItem_generateBreakbeatSwing;
        private ToolStripMenuItem menuToolStripItem_generateBreakbeatSwing0;
        private ToolStripMenuItem menuToolStripItem_generateBreakbeatSwing6;
        private ToolStripMenuItem menuToolStripItem_generateBreakbeatSwing12;
        private ToolStripMenuItem menuToolStripItem_generateBreakbeatSwing18;
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