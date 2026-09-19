namespace ModularAudience.Forms
{
    partial class DeterministicSeparationDialog
    {
        private System.ComponentModel.IContainer components = null!;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.ReleaseResources();
                this.components?.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.tableLayoutPanel_main = new TableLayoutPanel();
            this.label_header = new Label();
            this.label_source = new Label();
            this.groupBox_settings = new GroupBox();
            this.tableLayoutPanel_settings = new TableLayoutPanel();
            this.label_windowSize = new Label();
            this.comboBox_windowSize = new ComboBox();
            this.label_maxComponents = new Label();
            this.numeric_maxComponents = new NumericUpDown();
            this.label_iterations = new Label();
            this.numeric_iterations = new NumericUpDown();
            this.label_analysisFrames = new Label();
            this.numeric_analysisFrames = new NumericUpDown();
            this.label_blockFrames = new Label();
            this.numeric_blockFrames = new NumericUpDown();
            this.label_medianFrames = new Label();
            this.numeric_medianFrames = new NumericUpDown();
            this.label_medianBins = new Label();
            this.numeric_medianBins = new NumericUpDown();
            this.label_separationMargin = new Label();
            this.numeric_separationMargin = new NumericUpDown();
            this.label_maskFloor = new Label();
            this.numeric_maskFloor = new NumericUpDown();
            this.label_transientPreservation = new Label();
            this.numeric_transientPreservation = new NumericUpDown();
            this.label_threads = new Label();
            this.numeric_threads = new NumericUpDown();
            this.label_hopSize = new Label();
            this.label_hopSizeValue = new Label();
            this.label_summary = new Label();
            this.dataGridView_sources = new DataGridView();
            this.column_use = new DataGridViewCheckBoxColumn();
            this.column_name = new DataGridViewTextBoxColumn();
            this.column_character = new DataGridViewTextBoxColumn();
            this.column_score = new DataGridViewTextBoxColumn();
            this.column_energy = new DataGridViewTextBoxColumn();
            this.column_pitch = new DataGridViewTextBoxColumn();
            this.column_pan = new DataGridViewTextBoxColumn();
            this.label_output = new Label();
            this.groupBox_warnings = new GroupBox();
            this.textBox_warnings = new TextBox();
            this.label_status = new Label();
            this.progressBar_operation = new ProgressBar();
            this.flowLayoutPanel_buttons = new FlowLayoutPanel();
            this.button_detect = new Button();
            this.button_separate = new Button();
            this.button_cancel = new Button();
            this.button_close = new Button();
            this.toolTip_settings = new ToolTip(this.components);
            this.tableLayoutPanel_main.SuspendLayout();
            this.groupBox_settings.SuspendLayout();
            this.tableLayoutPanel_settings.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize) this.numeric_maxComponents).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numeric_iterations).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numeric_analysisFrames).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numeric_blockFrames).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numeric_medianFrames).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numeric_medianBins).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numeric_separationMargin).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numeric_maskFloor).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numeric_transientPreservation).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numeric_threads).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.dataGridView_sources).BeginInit();
            this.groupBox_warnings.SuspendLayout();
            this.flowLayoutPanel_buttons.SuspendLayout();
            this.SuspendLayout();
            //
            // tableLayoutPanel_main
            //
            this.tableLayoutPanel_main.AutoScroll = true;
            this.tableLayoutPanel_main.ColumnCount = 1;
            this.tableLayoutPanel_main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            this.tableLayoutPanel_main.Controls.Add(this.label_header, 0, 0);
            this.tableLayoutPanel_main.Controls.Add(this.label_source, 0, 1);
            this.tableLayoutPanel_main.Controls.Add(this.groupBox_settings, 0, 2);
            this.tableLayoutPanel_main.Controls.Add(this.label_summary, 0, 3);
            this.tableLayoutPanel_main.Controls.Add(this.dataGridView_sources, 0, 4);
            this.tableLayoutPanel_main.Controls.Add(this.label_output, 0, 5);
            this.tableLayoutPanel_main.Controls.Add(this.groupBox_warnings, 0, 6);
            this.tableLayoutPanel_main.Controls.Add(this.label_status, 0, 7);
            this.tableLayoutPanel_main.Controls.Add(this.progressBar_operation, 0, 8);
            this.tableLayoutPanel_main.Controls.Add(this.flowLayoutPanel_buttons, 0, 9);
            this.tableLayoutPanel_main.Dock = DockStyle.Fill;
            this.tableLayoutPanel_main.Name = "tableLayoutPanel_main";
            this.tableLayoutPanel_main.Padding = new Padding(12);
            this.tableLayoutPanel_main.RowCount = 10;
            this.tableLayoutPanel_main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            this.tableLayoutPanel_main.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            this.tableLayoutPanel_main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            this.tableLayoutPanel_main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            this.tableLayoutPanel_main.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            this.tableLayoutPanel_main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            this.tableLayoutPanel_main.RowStyles.Add(new RowStyle(SizeType.Absolute, 112F));
            this.tableLayoutPanel_main.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            this.tableLayoutPanel_main.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            this.tableLayoutPanel_main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            this.tableLayoutPanel_main.TabIndex = 0;
            //
            // Header and source
            //
            this.label_header.AutoSize = true;
            this.label_header.Dock = DockStyle.Fill;
            this.label_header.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            this.label_header.Margin = new Padding(3, 3, 3, 6);
            this.label_header.Name = "label_header";
            this.label_header.Text = "CPU only • No models • 75% overlap • Original phase";
            this.label_source.AutoEllipsis = true;
            this.label_source.Dock = DockStyle.Fill;
            this.label_source.Name = "label_source";
            this.label_source.Text = "Source information unavailable.";
            this.label_source.TextAlign = ContentAlignment.MiddleLeft;
            this.label_source.UseMnemonic = false;
            //
            // Settings layout
            //
            this.groupBox_settings.AutoSize = true;
            this.groupBox_settings.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            this.groupBox_settings.Controls.Add(this.tableLayoutPanel_settings);
            this.groupBox_settings.Dock = DockStyle.Fill;
            this.groupBox_settings.Name = "groupBox_settings";
            this.groupBox_settings.Padding = new Padding(8, 3, 8, 8);
            this.groupBox_settings.TabIndex = 0;
            this.groupBox_settings.TabStop = false;
            this.groupBox_settings.Text = "DSP settings";
            this.tableLayoutPanel_settings.AutoSize = true;
            this.tableLayoutPanel_settings.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            this.tableLayoutPanel_settings.ColumnCount = 4;
            this.tableLayoutPanel_settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32F));
            this.tableLayoutPanel_settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18F));
            this.tableLayoutPanel_settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32F));
            this.tableLayoutPanel_settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18F));
            this.tableLayoutPanel_settings.Controls.Add(this.label_windowSize, 0, 0);
            this.tableLayoutPanel_settings.Controls.Add(this.comboBox_windowSize, 1, 0);
            this.tableLayoutPanel_settings.Controls.Add(this.label_maxComponents, 2, 0);
            this.tableLayoutPanel_settings.Controls.Add(this.numeric_maxComponents, 3, 0);
            this.tableLayoutPanel_settings.Controls.Add(this.label_iterations, 0, 1);
            this.tableLayoutPanel_settings.Controls.Add(this.numeric_iterations, 1, 1);
            this.tableLayoutPanel_settings.Controls.Add(this.label_analysisFrames, 2, 1);
            this.tableLayoutPanel_settings.Controls.Add(this.numeric_analysisFrames, 3, 1);
            this.tableLayoutPanel_settings.Controls.Add(this.label_blockFrames, 0, 2);
            this.tableLayoutPanel_settings.Controls.Add(this.numeric_blockFrames, 1, 2);
            this.tableLayoutPanel_settings.Controls.Add(this.label_medianFrames, 2, 2);
            this.tableLayoutPanel_settings.Controls.Add(this.numeric_medianFrames, 3, 2);
            this.tableLayoutPanel_settings.Controls.Add(this.label_medianBins, 0, 3);
            this.tableLayoutPanel_settings.Controls.Add(this.numeric_medianBins, 1, 3);
            this.tableLayoutPanel_settings.Controls.Add(this.label_separationMargin, 2, 3);
            this.tableLayoutPanel_settings.Controls.Add(this.numeric_separationMargin, 3, 3);
            this.tableLayoutPanel_settings.Controls.Add(this.label_maskFloor, 0, 4);
            this.tableLayoutPanel_settings.Controls.Add(this.numeric_maskFloor, 1, 4);
            this.tableLayoutPanel_settings.Controls.Add(this.label_transientPreservation, 2, 4);
            this.tableLayoutPanel_settings.Controls.Add(this.numeric_transientPreservation, 3, 4);
            this.tableLayoutPanel_settings.Controls.Add(this.label_threads, 0, 5);
            this.tableLayoutPanel_settings.Controls.Add(this.numeric_threads, 1, 5);
            this.tableLayoutPanel_settings.Controls.Add(this.label_hopSize, 2, 5);
            this.tableLayoutPanel_settings.Controls.Add(this.label_hopSizeValue, 3, 5);
            this.tableLayoutPanel_settings.Dock = DockStyle.Fill;
            this.tableLayoutPanel_settings.Name = "tableLayoutPanel_settings";
            this.tableLayoutPanel_settings.RowCount = 6;
            this.tableLayoutPanel_settings.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            this.tableLayoutPanel_settings.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            this.tableLayoutPanel_settings.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            this.tableLayoutPanel_settings.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            this.tableLayoutPanel_settings.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            this.tableLayoutPanel_settings.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            this.tableLayoutPanel_settings.TabIndex = 0;
            //
            // Window size and component limit
            //
            this.label_windowSize.Anchor = AnchorStyles.Left;
            this.label_windowSize.AutoSize = true;
            this.label_windowSize.Name = "label_windowSize";
            this.label_windowSize.Text = "FFT window size";
            this.comboBox_windowSize.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            this.comboBox_windowSize.DropDownStyle = ComboBoxStyle.DropDownList;
            this.comboBox_windowSize.FormattingEnabled = true;
            this.comboBox_windowSize.Items.AddRange(new object[] { 256, 512, 1024, 2048, 4096, 8192, 16384 });
            this.comboBox_windowSize.Name = "comboBox_windowSize";
            this.comboBox_windowSize.SelectedIndex = 4;
            this.comboBox_windowSize.TabIndex = 0;
            this.toolTip_settings.SetToolTip(this.comboBox_windowSize, "Larger FFTs resolve nearby pitches but smear timing and use more workspace. Hop is always FFT size / 4.");
            this.label_maxComponents.Anchor = AnchorStyles.Left;
            this.label_maxComponents.AutoSize = true;
            this.label_maxComponents.Name = "label_maxComponents";
            this.label_maxComponents.Text = "Max components";
            this.numeric_maxComponents.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            this.numeric_maxComponents.Maximum = 32;
            this.numeric_maxComponents.Minimum = 1;
            this.numeric_maxComponents.Name = "numeric_maxComponents";
            this.numeric_maxComponents.TabIndex = 1;
            this.numeric_maxComponents.Value = 16;
            this.toolTip_settings.SetToolTip(this.numeric_maxComponents, "Upper limit on learned components, not an instrument count. Higher limits cost CPU time and model memory.");
            //
            // Learning settings
            //
            this.label_iterations.Anchor = AnchorStyles.Left;
            this.label_iterations.AutoSize = true;
            this.label_iterations.Name = "label_iterations";
            this.label_iterations.Text = "Iterations";
            this.numeric_iterations.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            this.numeric_iterations.Maximum = 500;
            this.numeric_iterations.Minimum = 1;
            this.numeric_iterations.Name = "numeric_iterations";
            this.numeric_iterations.TabIndex = 2;
            this.numeric_iterations.Value = 80;
            this.toolTip_settings.SetToolTip(this.numeric_iterations, "More learning iterations can improve the fit but take longer; they do not guarantee better source isolation.");
            this.label_analysisFrames.Anchor = AnchorStyles.Left;
            this.label_analysisFrames.AutoSize = true;
            this.label_analysisFrames.Name = "label_analysisFrames";
            this.label_analysisFrames.Text = "Analysis frames";
            this.numeric_analysisFrames.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            this.numeric_analysisFrames.Maximum = 8192;
            this.numeric_analysisFrames.Minimum = 32;
            this.numeric_analysisFrames.Name = "numeric_analysisFrames";
            this.numeric_analysisFrames.TabIndex = 3;
            this.numeric_analysisFrames.Value = 1024;
            this.toolTip_settings.SetToolTip(this.numeric_analysisFrames, "Distributed, sampled analysis may miss rare events. More frames improve coverage but require more time and workspace.");
            //
            // Blocking and median settings
            //
            this.label_blockFrames.Anchor = AnchorStyles.Left;
            this.label_blockFrames.AutoSize = true;
            this.label_blockFrames.Name = "label_blockFrames";
            this.label_blockFrames.Text = "Block frames";
            this.numeric_blockFrames.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            this.numeric_blockFrames.Maximum = 512;
            this.numeric_blockFrames.Minimum = 8;
            this.numeric_blockFrames.Name = "numeric_blockFrames";
            this.numeric_blockFrames.TabIndex = 4;
            this.numeric_blockFrames.Value = 128;
            this.toolTip_settings.SetToolTip(this.numeric_blockFrames, "Larger separation blocks reduce repeated context processing but require more workspace. Output size is unchanged.");
            this.label_medianFrames.Anchor = AnchorStyles.Left;
            this.label_medianFrames.AutoSize = true;
            this.label_medianFrames.Name = "label_medianFrames";
            this.label_medianFrames.Text = "Median frames (odd)";
            this.numeric_medianFrames.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            this.numeric_medianFrames.Increment = 2;
            this.numeric_medianFrames.Maximum = 65;
            this.numeric_medianFrames.Minimum = 3;
            this.numeric_medianFrames.Name = "numeric_medianFrames";
            this.numeric_medianFrames.TabIndex = 5;
            this.numeric_medianFrames.Value = 17;
            this.toolTip_settings.SetToolTip(this.numeric_medianFrames, "Temporal median width. Larger values favor sustained structure over short events. Enter an odd number; even values are rejected.");
            this.label_medianBins.Anchor = AnchorStyles.Left;
            this.label_medianBins.AutoSize = true;
            this.label_medianBins.Name = "label_medianBins";
            this.label_medianBins.Text = "Median bins (odd)";
            this.numeric_medianBins.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            this.numeric_medianBins.Increment = 2;
            this.numeric_medianBins.Maximum = 65;
            this.numeric_medianBins.Minimum = 3;
            this.numeric_medianBins.Name = "numeric_medianBins";
            this.numeric_medianBins.TabIndex = 6;
            this.numeric_medianBins.Value = 17;
            this.toolTip_settings.SetToolTip(this.numeric_medianBins, "Frequency median width. Larger values favor broadband structure over fine spectral detail. Enter an odd number; even values are rejected.");
            //
            // Mask and restoration settings
            //
            this.label_separationMargin.Anchor = AnchorStyles.Left;
            this.label_separationMargin.AutoSize = true;
            this.label_separationMargin.Name = "label_separationMargin";
            this.label_separationMargin.Text = "Separation margin";
            this.numeric_separationMargin.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            this.numeric_separationMargin.DecimalPlaces = 2;
            this.numeric_separationMargin.Increment = 0.1M;
            this.numeric_separationMargin.Maximum = 10;
            this.numeric_separationMargin.Minimum = 1;
            this.numeric_separationMargin.Name = "numeric_separationMargin";
            this.numeric_separationMargin.TabIndex = 7;
            this.numeric_separationMargin.Value = 2;
            this.toolTip_settings.SetToolTip(this.numeric_separationMargin, "Higher margins favor conservative separation, leaving more ambiguous material in Residual.");
            this.label_maskFloor.Anchor = AnchorStyles.Left;
            this.label_maskFloor.AutoSize = true;
            this.label_maskFloor.Name = "label_maskFloor";
            this.label_maskFloor.Text = "Mask floor (gain)";
            this.numeric_maskFloor.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            this.numeric_maskFloor.DecimalPlaces = 4;
            this.numeric_maskFloor.Increment = 0.001M;
            this.numeric_maskFloor.Maximum = 0.05M;
            this.numeric_maskFloor.Minimum = 0;
            this.numeric_maskFloor.Name = "numeric_maskFloor";
            this.numeric_maskFloor.TabIndex = 8;
            this.numeric_maskFloor.Value = 0.001M;
            this.toolTip_settings.SetToolTip(this.numeric_maskFloor, "Linear mask gain, not decibels. A higher floor reduces deep spectral holes but retains more bleed.");
            this.label_transientPreservation.Anchor = AnchorStyles.Left;
            this.label_transientPreservation.AutoSize = true;
            this.label_transientPreservation.Name = "label_transientPreservation";
            this.label_transientPreservation.Text = "Transient preservation (%)";
            this.numeric_transientPreservation.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            this.numeric_transientPreservation.DecimalPlaces = 1;
            this.numeric_transientPreservation.Increment = 1;
            this.numeric_transientPreservation.Maximum = 100;
            this.numeric_transientPreservation.Minimum = 0;
            this.numeric_transientPreservation.Name = "numeric_transientPreservation";
            this.numeric_transientPreservation.TabIndex = 9;
            this.numeric_transientPreservation.Value = 75;
            this.toolTip_settings.SetToolTip(this.numeric_transientPreservation, "Higher values preserve more attack detail but can retain bleed. 100% corresponds to a preservation factor of 1.");
            //
            // Threads and fixed hop size
            //
            this.label_threads.Anchor = AnchorStyles.Left;
            this.label_threads.AutoSize = true;
            this.label_threads.Name = "label_threads";
            this.label_threads.Text = "Threads";
            this.numeric_threads.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            this.numeric_threads.Maximum = Environment.ProcessorCount;
            this.numeric_threads.Minimum = 1;
            this.numeric_threads.Name = "numeric_threads";
            this.numeric_threads.TabIndex = 10;
            this.numeric_threads.Value = Math.Max(1, Environment.ProcessorCount / 2);
            this.toolTip_settings.SetToolTip(this.numeric_threads, "More CPU workers may improve throughput but leave less CPU time for playback and other applications.");
            this.label_hopSize.Anchor = AnchorStyles.Left;
            this.label_hopSize.AutoSize = true;
            this.label_hopSize.Name = "label_hopSize";
            this.label_hopSize.Text = "Hop size (FFT / 4)";
            this.label_hopSizeValue.Anchor = AnchorStyles.Left;
            this.label_hopSizeValue.AutoSize = true;
            this.label_hopSizeValue.Name = "label_hopSizeValue";
            this.label_hopSizeValue.Text = "1024 samples (fixed)";
            this.toolTip_settings.SetToolTip(this.label_hopSizeValue, "Fixed 75% overlap. Change the FFT window size to change the hop size.");
            //
            // Source estimates
            //
            this.label_summary.AutoSize = true;
            this.label_summary.Dock = DockStyle.Fill;
            this.label_summary.Margin = new Padding(3, 6, 3, 6);
            this.label_summary.Name = "label_summary";
            this.label_summary.Text = "No current analysis. Detect sources to estimate acoustic groups.";
            this.dataGridView_sources.AllowUserToAddRows = false;
            this.dataGridView_sources.AllowUserToDeleteRows = false;
            this.dataGridView_sources.AllowUserToResizeRows = false;
            this.dataGridView_sources.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            this.dataGridView_sources.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            this.dataGridView_sources.BackgroundColor = SystemColors.Window;
            this.dataGridView_sources.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dataGridView_sources.Columns.AddRange(new DataGridViewColumn[]
            {
                this.column_use, this.column_name, this.column_character, this.column_score,
                this.column_energy, this.column_pitch, this.column_pan
            });
            this.dataGridView_sources.DefaultCellStyle.FormatProvider = System.Globalization.CultureInfo.InvariantCulture;
            this.dataGridView_sources.Dock = DockStyle.Fill;
            this.dataGridView_sources.EditMode = DataGridViewEditMode.EditOnEnter;
            this.dataGridView_sources.Enabled = false;
            this.dataGridView_sources.MinimumSize = new Size(0, 140);
            this.dataGridView_sources.MultiSelect = false;
            this.dataGridView_sources.Name = "dataGridView_sources";
            this.dataGridView_sources.RowHeadersVisible = false;
            this.dataGridView_sources.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            this.dataGridView_sources.TabIndex = 1;
            this.toolTip_settings.SetToolTip(this.dataGridView_sources, "Only Use is editable. Deselect every group for Residual only. Scores, pitch and energy are acoustic hints, not instrument identification.");
            this.column_use.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            this.column_use.HeaderText = "Use";
            this.column_use.MinimumWidth = 45;
            this.column_use.Name = "column_use";
            this.column_use.ReadOnly = false;
            this.column_use.SortMode = DataGridViewColumnSortMode.NotSortable;
            this.column_use.Width = 45;
            this.column_name.FillWeight = 140;
            this.column_name.HeaderText = "Name";
            this.column_name.MinimumWidth = 100;
            this.column_name.Name = "column_name";
            this.column_name.ReadOnly = true;
            this.column_name.SortMode = DataGridViewColumnSortMode.NotSortable;
            this.column_character.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            this.column_character.FillWeight = 270;
            this.column_character.HeaderText = "Acoustic evidence";
            this.column_character.MinimumWidth = 180;
            this.column_character.Name = "column_character";
            this.column_character.ReadOnly = true;
            this.column_character.SortMode = DataGridViewColumnSortMode.NotSortable;
            this.column_score.DefaultCellStyle.Format = "F3";
            this.column_score.FillWeight = 85;
            this.column_score.HeaderText = "Score (0–1)";
            this.column_score.MinimumWidth = 85;
            this.column_score.Name = "column_score";
            this.column_score.ReadOnly = true;
            this.column_score.SortMode = DataGridViewColumnSortMode.NotSortable;
            this.column_score.ToolTipText = "Heuristic acoustic score, not a calibrated probability.";
            this.column_energy.DefaultCellStyle.Format = "P1";
            this.column_energy.FillWeight = 95;
            this.column_energy.HeaderText = "Energy share";
            this.column_energy.MinimumWidth = 90;
            this.column_energy.Name = "column_energy";
            this.column_energy.ReadOnly = true;
            this.column_energy.SortMode = DataGridViewColumnSortMode.NotSortable;
            this.column_energy.ToolTipText = "Estimated energy share, not isolated instrument loudness.";
            this.column_pitch.DefaultCellStyle.Format = "F1";
            this.column_pitch.DefaultCellStyle.NullValue = "—";
            this.column_pitch.FillWeight = 100;
            this.column_pitch.HeaderText = "Pitch hint Hz";
            this.column_pitch.MinimumWidth = 95;
            this.column_pitch.Name = "column_pitch";
            this.column_pitch.ReadOnly = true;
            this.column_pitch.SortMode = DataGridViewColumnSortMode.NotSortable;
            this.column_pitch.ToolTipText = "Approximate fundamental frequency when available; not a note transcription.";
            this.column_pan.DefaultCellStyle.Format = "F2";
            this.column_pan.FillWeight = 65;
            this.column_pan.HeaderText = "Pan";
            this.column_pan.MinimumWidth = 60;
            this.column_pan.Name = "column_pan";
            this.column_pan.ReadOnly = true;
            this.column_pan.SortMode = DataGridViewColumnSortMode.NotSortable;
            this.column_pan.ToolTipText = "Estimated pan: -1 left, 0 center, +1 right.";
            //
            // Output memory and copyable warnings
            //
            this.label_output.AutoSize = true;
            this.label_output.Dock = DockStyle.Fill;
            this.label_output.Margin = new Padding(3, 6, 3, 6);
            this.label_output.Name = "label_output";
            this.label_output.Text = "Approx. OUTPUT: analyze first (Residual is always included).\r\nAdditional snapshot and processing workspace are excluded; this is not a total RAM estimate.";
            this.groupBox_warnings.Controls.Add(this.textBox_warnings);
            this.groupBox_warnings.Dock = DockStyle.Fill;
            this.groupBox_warnings.Name = "groupBox_warnings";
            this.groupBox_warnings.Padding = new Padding(8, 3, 8, 8);
            this.groupBox_warnings.TabIndex = 2;
            this.groupBox_warnings.TabStop = false;
            this.groupBox_warnings.Text = "Analysis notes and warnings (copyable)";
            this.textBox_warnings.BackColor = SystemColors.Window;
            this.textBox_warnings.Dock = DockStyle.Fill;
            this.textBox_warnings.Multiline = true;
            this.textBox_warnings.Name = "textBox_warnings";
            this.textBox_warnings.ReadOnly = true;
            this.textBox_warnings.ScrollBars = ScrollBars.Vertical;
            this.textBox_warnings.ShortcutsEnabled = true;
            this.textBox_warnings.TabIndex = 0;
            //
            // Independent operation status and progress
            //
            this.label_status.AutoEllipsis = true;
            this.label_status.Dock = DockStyle.Fill;
            this.label_status.Name = "label_status";
            this.label_status.Text = "Ready. Detection runs only when requested.";
            this.label_status.TextAlign = ContentAlignment.MiddleLeft;
            this.label_status.UseMnemonic = false;
            this.progressBar_operation.Dock = DockStyle.Fill;
            this.progressBar_operation.Maximum = 1000;
            this.progressBar_operation.Name = "progressBar_operation";
            this.progressBar_operation.Style = ProgressBarStyle.Blocks;
            this.progressBar_operation.TabIndex = 3;
            //
            // Action buttons
            //
            this.flowLayoutPanel_buttons.AutoSize = true;
            this.flowLayoutPanel_buttons.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            this.flowLayoutPanel_buttons.Controls.Add(this.button_detect);
            this.flowLayoutPanel_buttons.Controls.Add(this.button_separate);
            this.flowLayoutPanel_buttons.Controls.Add(this.button_cancel);
            this.flowLayoutPanel_buttons.Controls.Add(this.button_close);
            this.flowLayoutPanel_buttons.Dock = DockStyle.Fill;
            this.flowLayoutPanel_buttons.Margin = new Padding(0, 6, 0, 0);
            this.flowLayoutPanel_buttons.Name = "flowLayoutPanel_buttons";
            this.flowLayoutPanel_buttons.TabIndex = 4;
            this.flowLayoutPanel_buttons.WrapContents = true;
            this.button_detect.AutoSize = true;
            this.button_detect.MinimumSize = new Size(130, 32);
            this.button_detect.Name = "button_detect";
            this.button_detect.TabIndex = 0;
            this.button_detect.Text = "Detect sources";
            this.button_detect.UseVisualStyleBackColor = true;
            this.button_separate.AutoSize = true;
            this.button_separate.Enabled = false;
            this.button_separate.MinimumSize = new Size(160, 32);
            this.button_separate.Name = "button_separate";
            this.button_separate.TabIndex = 1;
            this.button_separate.Text = "Separate & restore";
            this.button_separate.UseMnemonic = false;
            this.button_separate.UseVisualStyleBackColor = true;
            this.toolTip_settings.SetToolTip(this.button_separate, "Create selected groups plus Residual and restore mixture consistency. All groups may be deselected. Original phase is retained; lost information cannot be recovered.");
            this.button_cancel.AutoSize = true;
            this.button_cancel.Enabled = false;
            this.button_cancel.MinimumSize = new Size(90, 32);
            this.button_cancel.Name = "button_cancel";
            this.button_cancel.TabIndex = 2;
            this.button_cancel.Text = "Cancel";
            this.button_cancel.UseVisualStyleBackColor = true;
            this.button_close.AutoSize = true;
            this.button_close.MinimumSize = new Size(90, 32);
            this.button_close.Name = "button_close";
            this.button_close.TabIndex = 3;
            this.button_close.Text = "Close";
            this.button_close.UseVisualStyleBackColor = true;
            this.toolTip_settings.AutoPopDelay = 10000;
            this.toolTip_settings.InitialDelay = 400;
            this.toolTip_settings.ReshowDelay = 100;
            //
            // Event bindings
            //
            this.comboBox_windowSize.SelectedIndexChanged += this.settings_ValueChanged;
            this.numeric_maxComponents.ValueChanged += this.settings_ValueChanged;
            this.numeric_maxComponents.TextChanged += this.settings_ValueChanged;
            this.numeric_iterations.ValueChanged += this.settings_ValueChanged;
            this.numeric_iterations.TextChanged += this.settings_ValueChanged;
            this.numeric_analysisFrames.ValueChanged += this.settings_ValueChanged;
            this.numeric_analysisFrames.TextChanged += this.settings_ValueChanged;
            this.numeric_blockFrames.ValueChanged += this.settings_ValueChanged;
            this.numeric_blockFrames.TextChanged += this.settings_ValueChanged;
            this.numeric_medianFrames.ValueChanged += this.settings_ValueChanged;
            this.numeric_medianFrames.TextChanged += this.settings_ValueChanged;
            this.numeric_medianBins.ValueChanged += this.settings_ValueChanged;
            this.numeric_medianBins.TextChanged += this.settings_ValueChanged;
            this.numeric_separationMargin.ValueChanged += this.settings_ValueChanged;
            this.numeric_separationMargin.TextChanged += this.settings_ValueChanged;
            this.numeric_maskFloor.ValueChanged += this.settings_ValueChanged;
            this.numeric_maskFloor.TextChanged += this.settings_ValueChanged;
            this.numeric_transientPreservation.ValueChanged += this.settings_ValueChanged;
            this.numeric_transientPreservation.TextChanged += this.settings_ValueChanged;
            this.numeric_threads.ValueChanged += this.settings_ValueChanged;
            this.numeric_threads.TextChanged += this.settings_ValueChanged;
            this.dataGridView_sources.CurrentCellDirtyStateChanged += this.dataGridView_sources_CurrentCellDirtyStateChanged;
            this.dataGridView_sources.CellValueChanged += this.dataGridView_sources_CellValueChanged;
            this.button_detect.Click += this.button_detect_Click;
            this.button_separate.Click += this.button_separate_Click;
            this.button_cancel.Click += this.button_cancel_Click;
            this.button_close.Click += this.button_close_Click;
            this.FormClosing += this.DeterministicSeparationDialog_FormClosing;
            //
            // DeterministicSeparationDialog
            //
            this.AutoScaleDimensions = new SizeF(96F, 96F);
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.ClientSize = new Size(1060, 860);
            this.Controls.Add(this.tableLayoutPanel_main);
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MinimumSize = new Size(920, 800);
            this.Name = "DeterministicSeparationDialog";
            this.StartPosition = FormStartPosition.CenterParent;
            this.Text = "Deterministic separation";
            this.tableLayoutPanel_main.ResumeLayout(false);
            this.tableLayoutPanel_main.PerformLayout();
            this.groupBox_settings.ResumeLayout(false);
            this.groupBox_settings.PerformLayout();
            this.tableLayoutPanel_settings.ResumeLayout(false);
            this.tableLayoutPanel_settings.PerformLayout();
            ((System.ComponentModel.ISupportInitialize) this.numeric_maxComponents).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numeric_iterations).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numeric_analysisFrames).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numeric_blockFrames).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numeric_medianFrames).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numeric_medianBins).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numeric_separationMargin).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numeric_maskFloor).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numeric_transientPreservation).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numeric_threads).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.dataGridView_sources).EndInit();
            this.groupBox_warnings.ResumeLayout(false);
            this.groupBox_warnings.PerformLayout();
            this.flowLayoutPanel_buttons.ResumeLayout(false);
            this.flowLayoutPanel_buttons.PerformLayout();
            this.ResumeLayout(false);
        }

        #endregion

        private TableLayoutPanel tableLayoutPanel_main = null!;
        private Label label_header = null!;
        private Label label_source = null!;
        private GroupBox groupBox_settings = null!;
        private TableLayoutPanel tableLayoutPanel_settings = null!;
        private Label label_windowSize = null!;
        private ComboBox comboBox_windowSize = null!;
        private Label label_maxComponents = null!;
        private NumericUpDown numeric_maxComponents = null!;
        private Label label_iterations = null!;
        private NumericUpDown numeric_iterations = null!;
        private Label label_analysisFrames = null!;
        private NumericUpDown numeric_analysisFrames = null!;
        private Label label_blockFrames = null!;
        private NumericUpDown numeric_blockFrames = null!;
        private Label label_medianFrames = null!;
        private NumericUpDown numeric_medianFrames = null!;
        private Label label_medianBins = null!;
        private NumericUpDown numeric_medianBins = null!;
        private Label label_separationMargin = null!;
        private NumericUpDown numeric_separationMargin = null!;
        private Label label_maskFloor = null!;
        private NumericUpDown numeric_maskFloor = null!;
        private Label label_transientPreservation = null!;
        private NumericUpDown numeric_transientPreservation = null!;
        private Label label_threads = null!;
        private NumericUpDown numeric_threads = null!;
        private Label label_hopSize = null!;
        private Label label_hopSizeValue = null!;
        private Label label_summary = null!;
        private DataGridView dataGridView_sources = null!;
        private DataGridViewCheckBoxColumn column_use = null!;
        private DataGridViewTextBoxColumn column_name = null!;
        private DataGridViewTextBoxColumn column_character = null!;
        private DataGridViewTextBoxColumn column_score = null!;
        private DataGridViewTextBoxColumn column_energy = null!;
        private DataGridViewTextBoxColumn column_pitch = null!;
        private DataGridViewTextBoxColumn column_pan = null!;
        private Label label_output = null!;
        private GroupBox groupBox_warnings = null!;
        private TextBox textBox_warnings = null!;
        private Label label_status = null!;
        private ProgressBar progressBar_operation = null!;
        private FlowLayoutPanel flowLayoutPanel_buttons = null!;
        private Button button_detect = null!;
        private Button button_separate = null!;
        private Button button_cancel = null!;
        private Button button_close = null!;
        private ToolTip toolTip_settings = null!;
    }
}
