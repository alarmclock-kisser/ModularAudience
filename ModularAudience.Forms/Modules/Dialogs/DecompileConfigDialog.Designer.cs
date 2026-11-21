#nullable enable
namespace ModularAudience.Forms.Modules.Dialogs;

partial class DecompileConfigDialog
{
    private System.ComponentModel.IContainer components = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.components?.Dispose();
            this.CleanupSourceSnapshot();
        }

        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        this.components = new System.ComponentModel.Container();
        this.tabControl_main = new TabControl();
        this.tabPage_separation = new TabPage();
        this.tabPage_atomize = new TabPage();
        this.tabPage_output = new TabPage();
        this.comboBox_separationMethod = new ComboBox();
        this.panel_separationContent = new Panel();
        this.panel_demucs = new Panel();
        this.tableLayoutPanel_demucs = new TableLayoutPanel();
        this.label_demucsModel = new Label();
        this.comboBox_demucsModel = new ComboBox();
        this.button_browseModels = new Button();
        this.label_demucsProvider = new Label();
        this.comboBox_demucsProvider = new ComboBox();
        this.label_directMLDevices = new Label();
        this.checkedListBox_directMLDevices = new CheckedListBox();
        this.label_fourierWorkers = new Label();
        this.numeric_fourierWorkers = new NumericUpDown();
        this.label_chunkOverlap = new Label();
        this.numeric_chunkOverlap = new NumericUpDown();
        this.label_keepModelLoaded = new Label();
        this.numeric_keepModelLoaded = new NumericUpDown();
        this.label_graphOptimization = new Label();
        this.comboBox_graphOptimization = new ComboBox();
        this.label_executionMode = new Label();
        this.comboBox_executionMode = new ComboBox();
        this.label_intraThreads = new Label();
        this.numeric_intraThreads = new NumericUpDown();
        this.label_interThreads = new Label();
        this.numeric_interThreads = new NumericUpDown();
        this.label_profilePrefix = new Label();
        this.textBox_profilePrefix = new TextBox();
        this.label_optimizedModel = new Label();
        this.textBox_optimizedModel = new TextBox();
        this.checkBox_cpuFallback = new CheckBox();
        this.checkBox_cpuOperators = new CheckBox();
        this.checkBox_disableDmlFusion = new CheckBox();
        this.checkBox_memoryPattern = new CheckBox();
        this.checkBox_cpuArena = new CheckBox();
        this.checkBox_disableSpinning = new CheckBox();
        this.checkBox_disablePrepacking = new CheckBox();
        this.checkBox_profiling = new CheckBox();
        this.checkBox_verboseLogging = new CheckBox();
        this.checkBox_cudaCuFft = new CheckBox();
        this.comboBox_cudaToolkit = new ComboBox();
        this.label_demucsDeviceNotice = new Label();
        this.label_demucsStems = new Label();
        this.panel_analog = new Panel();
        this.dataGridView_analogBands = new DataGridView();
        this.column_analogBandName = new DataGridViewTextBoxColumn();
        this.column_analogBandLow = new DataGridViewTextBoxColumn();
        this.column_analogBandHigh = new DataGridViewTextBoxColumn();
        this.button_addAnalogBand = new Button();
        this.button_removeAnalogBand = new Button();
        this.button_autoAnalogBands = new Button();
        this.label_analogWindow = new Label();
        this.numeric_analogWindow = new NumericUpDown();
        this.label_analogOverlap = new Label();
        this.numeric_analogOverlap = new NumericUpDown();
        this.label_analogSharpness = new Label();
        this.numeric_analogSharpness = new NumericUpDown();
        this.checkBox_analogSubtract = new CheckBox();
        this.checkBox_analogTimbre = new CheckBox();
        this.checkBox_analogHarmonic = new CheckBox();
        this.label_analogStatus = new Label();
        this.panel_deterministic = new Panel();
        this.label_deterministicPreset = new Label();
        this.comboBox_deterministicPreset = new ComboBox();
        this.propertyGrid_deterministic = new PropertyGrid();
        this.label_deterministicProfiles = new Label();
        this.checkedListBox_deterministicProfiles = new CheckedListBox();
        this.checkBox_deterministicAutomaticProfiles = new CheckBox();
        this.label_deterministicNote = new Label();
        this.comboBox_atomizer = new ComboBox();
        this.panel_atomizerContent = new Panel();
        this.panel_atomizerV4 = new Panel();
        this.tableLayoutPanel_atomizerV4 = new TableLayoutPanel();
        this.label_atomizerSensitivity = new Label();
        this.comboBox_atomizerSensitivity = new ComboBox();
        this.label_minSlice = new Label();
        this.numeric_minSlice = new NumericUpDown();
        this.label_tailPadding = new Label();
        this.numeric_tailPadding = new NumericUpDown();
        this.checkBox_singleAtomFallback = new CheckBox();
        this.checkBox_atomizerDeduplicate = new CheckBox();
        this.label_maxVariants = new Label();
        this.numeric_maxVariants = new NumericUpDown();
        this.label_minimumRms = new Label();
        this.numeric_minimumRms = new NumericUpDown();
        this.label_minimumDuration = new Label();
        this.numeric_minimumDuration = new NumericUpDown();
        this.label_similarity = new Label();
        this.numeric_similarity = new NumericUpDown();
        this.label_atomizerV4Note = new Label();
        this.panel_atomizerLegacy = new Panel();
        this.label_legacySimilarity = new Label();
        this.numeric_legacySimilarity = new NumericUpDown();
        this.checkBox_legacyAutoSimilarity = new CheckBox();
        this.label_legacyNote = new Label();
        this.groupBox_outputMode = new GroupBox();
        this.checkBox_projectOutput = new CheckBox();
        this.checkBox_collectionOutput = new CheckBox();
        this.label_projectDetail = new Label();
        this.comboBox_projectDetail = new ComboBox();
        this.label_projectOverlap = new Label();
        this.comboBox_projectOverlap = new ComboBox();
        this.label_projectAttack = new Label();
        this.numeric_projectAttack = new NumericUpDown();
        this.checkBox_collectionDeduplicate = new CheckBox();
        this.label_workers = new Label();
        this.numeric_workers = new NumericUpDown();
        this.label_bpm = new Label();
        this.label_outputNote = new Label();
        this.label_status = new Label();
        this.button_decompile = new Button();
        this.button_close = new Button();
        this.toolTip_config = new ToolTip(this.components);

        this.tabControl_main.SuspendLayout();
        this.tabPage_separation.SuspendLayout();
        this.tabPage_atomize.SuspendLayout();
        this.tabPage_output.SuspendLayout();
        this.panel_demucs.SuspendLayout();
        this.tableLayoutPanel_demucs.SuspendLayout();
        this.panel_analog.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)this.dataGridView_analogBands).BeginInit();
        this.panel_deterministic.SuspendLayout();
        this.panel_atomizerV4.SuspendLayout();
        this.tableLayoutPanel_atomizerV4.SuspendLayout();
        this.panel_atomizerLegacy.SuspendLayout();
        this.groupBox_outputMode.SuspendLayout();
        this.SuspendLayout();

        this.tabControl_main.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        this.tabControl_main.Controls.Add(this.tabPage_separation);
        this.tabControl_main.Controls.Add(this.tabPage_atomize);
        this.tabControl_main.Controls.Add(this.tabPage_output);
        this.tabControl_main.Location = new Point(8, 8);
        this.tabControl_main.Name = "tabControl_main";
        this.tabControl_main.Size = new Size(804, 646);
        this.tabControl_main.TabIndex = 0;

        this.tabPage_separation.Controls.Add(this.panel_separationContent);
        this.tabPage_separation.Controls.Add(this.comboBox_separationMethod);
        this.tabPage_separation.Name = "tabPage_separation";
        this.tabPage_separation.Padding = new Padding(8);
        this.tabPage_separation.Text = "1) Separation";
        this.tabPage_separation.UseVisualStyleBackColor = true;

        this.comboBox_separationMethod.Dock = DockStyle.Top;
        this.comboBox_separationMethod.DropDownStyle = ComboBoxStyle.DropDownList;
        this.comboBox_separationMethod.Items.AddRange(new object[] { "Demucs", "Analog Separation", "Best-practice deterministic" });
        this.comboBox_separationMethod.SelectedIndex = 0;
        this.comboBox_separationMethod.SelectedIndexChanged += this.comboBox_separationMethod_SelectedIndexChanged;

        this.panel_separationContent.Dock = DockStyle.Fill;
        this.panel_separationContent.Padding = new Padding(0, 8, 0, 0);

        this.panel_demucs.Dock = DockStyle.Fill;
        this.panel_demucs.Controls.Add(this.tableLayoutPanel_demucs);
        this.tableLayoutPanel_demucs.ColumnCount = 4;
        this.tableLayoutPanel_demucs.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 165F));
        this.tableLayoutPanel_demucs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        this.tableLayoutPanel_demucs.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 165F));
        this.tableLayoutPanel_demucs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        this.tableLayoutPanel_demucs.Dock = DockStyle.Fill;
        this.tableLayoutPanel_demucs.Name = "tableLayoutPanel_demucs";
        this.tableLayoutPanel_demucs.RowCount = 17;
        for (int row = 0; row < 17; row++)
        {
            this.tableLayoutPanel_demucs.RowStyles.Add(new RowStyle(
                SizeType.Absolute,
                row == 2 ? 76F : row is 3 or 4 ? 42F : 29F));
        }

        AddDemucsRow(0, this.label_demucsModel, "ONNX model", this.comboBox_demucsModel, this.button_browseModels);
        this.comboBox_demucsModel.DropDownStyle = ComboBoxStyle.DropDownList;
        this.button_browseModels.Text = "Browse models...";
        this.button_browseModels.Click += this.button_browseModels_Click;
        AddDemucsRow(1, this.label_demucsProvider, "Execution provider", this.comboBox_demucsProvider);
        this.comboBox_demucsProvider.DropDownStyle = ComboBoxStyle.DropDownList;
        this.comboBox_demucsProvider.Items.AddRange(new object[] { "DirectML", "CPU" });
        this.comboBox_demucsProvider.SelectedIndex = 0;
        AddDemucsRow(2, this.label_directMLDevices, "Parallel DirectML devices", this.checkedListBox_directMLDevices);
        this.checkedListBox_directMLDevices.CheckOnClick = true;
        this.checkedListBox_directMLDevices.IntegralHeight = false;
        this.checkedListBox_directMLDevices.Dock = DockStyle.Fill;
        AddDemucsPair(3, this.label_keepModelLoaded, "Keep loaded (seconds; 0 = unload)", this.numeric_keepModelLoaded,
            this.label_chunkOverlap, "Chunk overlap (%)", this.numeric_chunkOverlap);
        AddDemucsPair(4, this.label_fourierWorkers, "CPU FFT workers", this.numeric_fourierWorkers,
            this.label_graphOptimization, "Graph optimization", this.comboBox_graphOptimization);
        AddDemucsPair(5, this.label_executionMode, "Execution mode (CPU)", this.comboBox_executionMode,
            this.label_intraThreads, "Intra-op threads (0 = auto)", this.numeric_intraThreads);
        AddDemucsPair(6, this.label_interThreads, "Inter-op threads (0 = auto)", this.numeric_interThreads,
            this.label_profilePrefix, "Profiling output prefix", this.textBox_profilePrefix);
        AddDemucsPair(7, this.label_optimizedModel, "Optimized model path", this.textBox_optimizedModel,
            this.checkBox_cudaCuFft, "", this.comboBox_cudaToolkit);
        this.comboBox_graphOptimization.DropDownStyle = ComboBoxStyle.DropDownList;
        this.comboBox_executionMode.DropDownStyle = ComboBoxStyle.DropDownList;
        this.comboBox_cudaToolkit.DropDownStyle = ComboBoxStyle.DropDownList;
        AddDemucsCheck(8, this.checkBox_cpuFallback, "Fall back to CPU if DirectML fails", this.checkBox_cpuOperators, "Allow CPU operators in DirectML graphs");
        AddDemucsCheck(9, this.checkBox_disableDmlFusion, "Disable DirectML graph fusion (required for Demucs)");
        AddDemucsCheck(10, this.checkBox_memoryPattern, "Enable memory pattern (CPU only)", this.checkBox_cpuArena, "Enable CPU memory arena");
        AddDemucsCheck(11, this.checkBox_disableSpinning, "Disable worker spinning", this.checkBox_disablePrepacking, "Disable weight prepacking");
        AddDemucsCheck(12, this.checkBox_profiling, "Enable ONNX profiling", this.checkBox_verboseLogging, "Verbose ONNX session logging");
        this.label_demucsDeviceNotice.Dock = DockStyle.Fill;
        this.label_demucsDeviceNotice.AutoEllipsis = true;
        this.label_demucsDeviceNotice.Text = "CUDA 12/13 and DirectML availability are checked at startup.";
        this.tableLayoutPanel_demucs.Controls.Add(this.label_demucsDeviceNotice, 0, 13);
        this.tableLayoutPanel_demucs.SetColumnSpan(this.label_demucsDeviceNotice, 4);
        this.label_demucsStems.Dock = DockStyle.Fill;
        this.label_demucsStems.Text = "All stems exposed by the selected model will be separated; no per-stem selection is required.";
        this.tableLayoutPanel_demucs.Controls.Add(this.label_demucsStems, 0, 14);
        this.tableLayoutPanel_demucs.SetColumnSpan(this.label_demucsStems, 4);
        this.numeric_keepModelLoaded.Maximum = 86400;
        this.numeric_keepModelLoaded.Increment = 30;
        this.numeric_fourierWorkers.Minimum = 1;
        this.numeric_fourierWorkers.Maximum = Environment.ProcessorCount;
        this.numeric_chunkOverlap.Maximum = 75;
        this.numeric_chunkOverlap.Increment = 5;
        this.numeric_chunkOverlap.Value = 25;
        this.numeric_intraThreads.Maximum = Environment.ProcessorCount;
        this.numeric_interThreads.Maximum = Environment.ProcessorCount;
        this.textBox_profilePrefix.Text = "demucs-profile";

        this.panel_analog.Dock = DockStyle.Fill;
        this.dataGridView_analogBands.AllowUserToAddRows = false;
        this.dataGridView_analogBands.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        this.dataGridView_analogBands.Columns.AddRange(new DataGridViewColumn[]
        {
            this.column_analogBandName,
            this.column_analogBandLow,
            this.column_analogBandHigh
        });
        this.column_analogBandName.HeaderText = "Band";
        this.column_analogBandName.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        this.column_analogBandLow.HeaderText = "Low Hz";
        this.column_analogBandLow.Width = 110;
        this.column_analogBandHigh.HeaderText = "High Hz";
        this.column_analogBandHigh.Width = 110;
        this.dataGridView_analogBands.Location = new Point(8, 8);
        this.dataGridView_analogBands.Size = new Size(760, 230);
        this.button_addAnalogBand.Location = new Point(8, 244);
        this.button_addAnalogBand.Size = new Size(100, 27);
        this.button_addAnalogBand.Text = "+ Add band";
        this.button_addAnalogBand.Click += this.button_addAnalogBand_Click;
        this.button_removeAnalogBand.Location = new Point(114, 244);
        this.button_removeAnalogBand.Size = new Size(110, 27);
        this.button_removeAnalogBand.Text = "Remove";
        this.button_removeAnalogBand.Click += this.button_removeAnalogBand_Click;
        this.button_autoAnalogBands.Location = new Point(230, 244);
        this.button_autoAnalogBands.Size = new Size(130, 27);
        this.button_autoAnalogBands.Text = "Analyze bands";
        this.button_autoAnalogBands.Click += this.button_autoAnalogBands_Click;
        AddAnalogSetting(this.panel_analog, this.label_analogWindow, "FFT window", this.numeric_analogWindow, 8, 292, 70, 100);
        AddAnalogSetting(this.panel_analog, this.label_analogOverlap, "Overlap (%)", this.numeric_analogOverlap, 270, 292, 70, 100);
        AddAnalogSetting(this.panel_analog, this.label_analogSharpness, "Sharpness", this.numeric_analogSharpness, 520, 292, 70, 100);
        this.numeric_analogWindow.Minimum = 256;
        this.numeric_analogWindow.Maximum = 65536;
        this.numeric_analogWindow.Value = 4096;
        this.numeric_analogOverlap.Maximum = 90;
        this.numeric_analogOverlap.Value = 50;
        this.numeric_analogSharpness.Minimum = 1;
        this.numeric_analogSharpness.Maximum = 16;
        this.numeric_analogSharpness.Value = 4;
        this.checkBox_analogSubtract.Location = new Point(8, 332);
        this.checkBox_analogSubtract.Text = "Subtract from original";
        this.checkBox_analogTimbre.Location = new Point(230, 332);
        this.checkBox_analogTimbre.Checked = true;
        this.checkBox_analogTimbre.Text = "Timbre-aware peaks";
        this.checkBox_analogHarmonic.Location = new Point(460, 332);
        this.checkBox_analogHarmonic.Checked = true;
        this.checkBox_analogHarmonic.Text = "Harmonic-aware";
        this.label_analogStatus.AutoEllipsis = true;
        this.label_analogStatus.Location = new Point(8, 368);
        this.label_analogStatus.Size = new Size(760, 36);
        this.label_analogStatus.Text = "Bands are separated locally with deterministic CPU DSP.";
        this.panel_analog.Controls.AddRange(new Control[]
        {
            this.dataGridView_analogBands,
            this.button_addAnalogBand,
            this.button_removeAnalogBand,
            this.button_autoAnalogBands,
            this.checkBox_analogSubtract,
            this.checkBox_analogTimbre,
            this.checkBox_analogHarmonic,
            this.label_analogStatus
        });

        this.panel_deterministic.Dock = DockStyle.Fill;
        this.label_deterministicPreset.Location = new Point(8, 10);
        this.label_deterministicPreset.AutoSize = true;
        this.label_deterministicPreset.Text = "Preset";
        this.comboBox_deterministicPreset.DropDownStyle = ComboBoxStyle.DropDownList;
        this.comboBox_deterministicPreset.Location = new Point(65, 7);
        this.comboBox_deterministicPreset.Size = new Size(210, 23);
        this.comboBox_deterministicPreset.Items.AddRange(new object[]
        {
            "Balanced", "Best Quality", "High Quality", "Faster", "Draft",
            "DnB / Breaks", "Electro / Synth", "Pop / Band", "Complex Ensemble"
        });
        this.comboBox_deterministicPreset.SelectedIndex = 0;
        this.comboBox_deterministicPreset.SelectedIndexChanged += this.comboBox_deterministicPreset_SelectedIndexChanged;
        this.propertyGrid_deterministic.Location = new Point(8, 40);
        this.propertyGrid_deterministic.Size = new Size(520, 420);
        this.label_deterministicProfiles.Location = new Point(545, 40);
        this.label_deterministicProfiles.AutoSize = true;
        this.label_deterministicProfiles.Text = "Instrument profiles";
        this.checkedListBox_deterministicProfiles.CheckOnClick = true;
        this.checkedListBox_deterministicProfiles.Location = new Point(545, 62);
        this.checkedListBox_deterministicProfiles.Size = new Size(220, 250);
        this.checkBox_deterministicAutomaticProfiles.Location = new Point(545, 320);
        this.checkBox_deterministicAutomaticProfiles.Checked = true;
        this.checkBox_deterministicAutomaticProfiles.Size = new Size(220, 40);
        this.checkBox_deterministicAutomaticProfiles.Text = "Add extra automatic profiles";
        this.label_deterministicNote.Location = new Point(545, 366);
        this.label_deterministicNote.Size = new Size(220, 94);
        this.label_deterministicNote.Text = "All detected acoustic groups plus Residual are separated and then atomized. The output remains local and deterministic.";
        this.panel_deterministic.Controls.AddRange(new Control[]
        {
            this.label_deterministicPreset,
            this.comboBox_deterministicPreset,
            this.propertyGrid_deterministic,
            this.label_deterministicProfiles,
            this.checkedListBox_deterministicProfiles,
            this.checkBox_deterministicAutomaticProfiles,
            this.label_deterministicNote
        });

        this.tabPage_atomize.Controls.Add(this.panel_atomizerContent);
        this.tabPage_atomize.Controls.Add(this.comboBox_atomizer);
        this.tabPage_atomize.Name = "tabPage_atomize";
        this.tabPage_atomize.Padding = new Padding(8);
        this.tabPage_atomize.Text = "2) Atomize";
        this.tabPage_atomize.UseVisualStyleBackColor = true;
        this.comboBox_atomizer.Dock = DockStyle.Top;
        this.comboBox_atomizer.DropDownStyle = ComboBoxStyle.DropDownList;
        this.comboBox_atomizer.Items.AddRange(new object[] { "Transient Atomizer V4", "Adaptive Legacy Atomizer V1" });
        this.comboBox_atomizer.SelectedIndex = 0;
        this.comboBox_atomizer.SelectedIndexChanged += this.comboBox_atomizer_SelectedIndexChanged;
        this.panel_atomizerContent.Dock = DockStyle.Fill;
        this.panel_atomizerContent.Padding = new Padding(0, 8, 0, 0);
        this.panel_atomizerV4.Dock = DockStyle.Fill;
        this.panel_atomizerV4.Controls.Add(this.tableLayoutPanel_atomizerV4);
        this.tableLayoutPanel_atomizerV4.ColumnCount = 4;
        this.tableLayoutPanel_atomizerV4.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 165F));
        this.tableLayoutPanel_atomizerV4.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        this.tableLayoutPanel_atomizerV4.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 165F));
        this.tableLayoutPanel_atomizerV4.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        this.tableLayoutPanel_atomizerV4.Dock = DockStyle.Fill;
        this.tableLayoutPanel_atomizerV4.RowCount = 7;
        for (int row = 0; row < 7; row++)
        {
            this.tableLayoutPanel_atomizerV4.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        }

        AddAtomizerPair(0, this.label_atomizerSensitivity, "Sensitivity", this.comboBox_atomizerSensitivity,
            this.label_minSlice, "Minimum slice (ms)", this.numeric_minSlice);
        AddAtomizerPair(1, this.label_tailPadding, "Tail padding (ms)", this.numeric_tailPadding,
            this.label_minimumDuration, "Minimum atomic duration (ms)", this.numeric_minimumDuration);
        AddAtomizerPair(2, this.label_minimumRms, "Minimum RMS", this.numeric_minimumRms,
            this.label_similarity, "Similarity threshold", this.numeric_similarity);
        AddAtomizerPair(3, this.label_maxVariants, "Maximum variants per cluster", this.numeric_maxVariants,
            this.checkBox_singleAtomFallback, "", this.checkBox_atomizerDeduplicate);
        this.comboBox_atomizerSensitivity.DropDownStyle = ComboBoxStyle.DropDownList;
        this.comboBox_atomizerSensitivity.Items.AddRange(new object[] { "Conservative", "Balanced", "Aggressive" });
        this.comboBox_atomizerSensitivity.SelectedIndex = 1;
        this.numeric_minSlice.Maximum = 5000;
        this.numeric_minSlice.Value = 60;
        this.numeric_tailPadding.Maximum = 1000;
        this.numeric_tailPadding.Value = 30;
        this.numeric_minimumDuration.Maximum = 10000;
        this.numeric_minimumDuration.Value = 20;
        this.numeric_minimumRms.DecimalPlaces = 4;
        this.numeric_minimumRms.Increment = 0.001M;
        this.numeric_minimumRms.Maximum = 1;
        this.numeric_minimumRms.Value = 0.001M;
        this.numeric_similarity.DecimalPlaces = 3;
        this.numeric_similarity.Increment = 0.001M;
        this.numeric_similarity.Minimum = 0.900M;
        this.numeric_similarity.Maximum = 0.999M;
        this.numeric_similarity.Value = 0.94M;
        this.numeric_maxVariants.Minimum = 1;
        this.numeric_maxVariants.Maximum = 64;
        this.numeric_maxVariants.Value = 2;
        this.checkBox_singleAtomFallback.Checked = true;
        this.checkBox_singleAtomFallback.Text = "Allow single-atom fallback";
        this.checkBox_atomizerDeduplicate.Checked = true;
        this.checkBox_atomizerDeduplicate.Text = "Deduplicate collection output";
        this.label_atomizerV4Note.Dock = DockStyle.Fill;
        this.label_atomizerV4Note.Text = "Defaults retain fuller synth/noise tails (30 ms padding, 20 ms minimum duration, lower RMS floor). Project output always disables deduplication.";
        this.tableLayoutPanel_atomizerV4.Controls.Add(this.label_atomizerV4Note, 0, 5);
        this.tableLayoutPanel_atomizerV4.SetColumnSpan(this.label_atomizerV4Note, 4);
        this.panel_atomizerLegacy.Dock = DockStyle.Fill;
        this.label_legacySimilarity.AutoSize = true;
        this.label_legacySimilarity.Location = new Point(8, 20);
        this.label_legacySimilarity.Text = "Similarity threshold";
        this.numeric_legacySimilarity.Location = new Point(150, 17);
        this.numeric_legacySimilarity.DecimalPlaces = 3;
        this.numeric_legacySimilarity.Increment = 0.01M;
        this.numeric_legacySimilarity.Minimum = 0.60M;
        this.numeric_legacySimilarity.Maximum = 0.98M;
        this.numeric_legacySimilarity.Value = 0.88M;
        this.checkBox_legacyAutoSimilarity.Location = new Point(265, 17);
        this.checkBox_legacyAutoSimilarity.Checked = true;
        this.checkBox_legacyAutoSimilarity.Text = "Adaptive";
        this.label_legacyNote.Location = new Point(8, 58);
        this.label_legacyNote.Size = new Size(720, 100);
        this.label_legacyNote.Text = "The legacy adaptive atomizer exposes one similarity hint. Collection deduplication can be disabled; project placement still references untouched stem samples at the detected offsets.";
        this.panel_atomizerLegacy.Controls.AddRange(new Control[]
        {
            this.label_legacySimilarity,
            this.numeric_legacySimilarity,
            this.checkBox_legacyAutoSimilarity,
            this.label_legacyNote
        });

        this.tabPage_output.Controls.Add(this.groupBox_outputMode);
        this.tabPage_output.Controls.Add(this.label_bpm);
        this.tabPage_output.Controls.Add(this.label_outputNote);
        this.tabPage_output.Controls.Add(this.label_workers);
        this.tabPage_output.Controls.Add(this.numeric_workers);
        this.tabPage_output.Controls.Add(this.label_projectDetail);
        this.tabPage_output.Controls.Add(this.comboBox_projectDetail);
        this.tabPage_output.Controls.Add(this.label_projectOverlap);
        this.tabPage_output.Controls.Add(this.comboBox_projectOverlap);
        this.tabPage_output.Controls.Add(this.label_projectAttack);
        this.tabPage_output.Controls.Add(this.numeric_projectAttack);
        this.tabPage_output.Controls.Add(this.checkBox_collectionDeduplicate);
        this.tabPage_output.Name = "tabPage_output";
        this.tabPage_output.Padding = new Padding(12);
        this.tabPage_output.Text = "3) Output";
        this.tabPage_output.UseVisualStyleBackColor = true;
        this.groupBox_outputMode.Location = new Point(12, 12);
        this.groupBox_outputMode.Size = new Size(750, 90);
        this.groupBox_outputMode.Text = "Output destination";
        this.checkBox_projectOutput.AutoSize = true;
        this.checkBox_projectOutput.Location = new Point(18, 28);
        this.checkBox_projectOutput.Checked = true;
        this.checkBox_projectOutput.Text = "Open as Mix Track Project";
        this.checkBox_projectOutput.CheckedChanged += this.outputOption_CheckedChanged;
        this.checkBox_collectionOutput.AutoSize = true;
        this.checkBox_collectionOutput.Location = new Point(300, 28);
        this.checkBox_collectionOutput.Text = "Open as one Audio Collection per stem";
        this.checkBox_collectionOutput.CheckedChanged += this.outputOption_CheckedChanged;
        this.groupBox_outputMode.Controls.Add(this.checkBox_projectOutput);
        this.groupBox_outputMode.Controls.Add(this.checkBox_collectionOutput);
        this.label_projectDetail.AutoSize = true;
        this.label_projectDetail.Location = new Point(12, 124);
        this.label_projectDetail.Text = "Project atomic detail";
        this.comboBox_projectDetail.DropDownStyle = ComboBoxStyle.DropDownList;
        this.comboBox_projectDetail.Location = new Point(170, 120);
        this.comboBox_projectDetail.Size = new Size(250, 23);
        this.comboBox_projectDetail.Items.AddRange(new object[] { "Individual atomics / transients", "Beat-sized segments", "Bar-sized segments (4/4)" });
        this.comboBox_projectDetail.SelectedIndex = 0;
        this.label_projectOverlap.AutoSize = true;
        this.label_projectOverlap.Location = new Point(12, 164);
        this.label_projectOverlap.Text = "Overlapping project segments";
        this.comboBox_projectOverlap.DropDownStyle = ComboBoxStyle.DropDownList;
        this.comboBox_projectOverlap.Location = new Point(220, 160);
        this.comboBox_projectOverlap.Size = new Size(360, 23);
        this.comboBox_projectOverlap.Items.AddRange(new object[]
        {
            "Allow overlaps in the stem lane",
            "Create additional lanes for overlaps"
        });
        this.comboBox_projectOverlap.SelectedIndex = 0;
        this.label_projectAttack.AutoSize = true;
        this.label_projectAttack.Location = new Point(12, 204);
        this.label_projectAttack.Text = "Segment onset attack (ms)";
        this.numeric_projectAttack.Location = new Point(220, 200);
        this.numeric_projectAttack.Size = new Size(80, 23);
        this.numeric_projectAttack.Minimum = 1;
        this.numeric_projectAttack.Maximum = 100;
        this.numeric_projectAttack.Value = 1;
        this.checkBox_collectionDeduplicate.Location = new Point(440, 120);
        this.checkBox_collectionDeduplicate.Checked = true;
        this.checkBox_collectionDeduplicate.Size = new Size(310, 40);
        this.checkBox_collectionDeduplicate.Text = "Deduplicate repeated samples in atomic collections";
        this.label_workers.AutoSize = true;
        this.label_workers.Location = new Point(12, 244);
        this.label_workers.Text = $"Maximum workers (1-{Environment.ProcessorCount})";
        this.numeric_workers.Location = new Point(300, 240);
        this.numeric_workers.Minimum = 1;
        this.numeric_workers.Maximum = Environment.ProcessorCount;
        this.numeric_workers.Value = Math.Max(1, Environment.ProcessorCount / 2);
        this.label_bpm.AutoSize = true;
        this.label_bpm.Location = new Point(12, 286);
        this.label_bpm.MaximumSize = new Size(740, 0);
        this.label_bpm.Text = "BPM: resolving from tag, scanned value or estimator at run time.";
        this.label_outputNote.Location = new Point(12, 328);
        this.label_outputNote.Size = new Size(740, 95);
        this.label_outputNote.Text = "Project and Collection output can be enabled together. Project clips use the complete atomic pool with a short onset fade; Collections can optionally deduplicate repeated atomics.";

        this.label_status.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        this.label_status.AutoEllipsis = true;
        this.label_status.Location = new Point(12, 664);
        this.label_status.Size = new Size(610, 34);
        this.label_status.Text = "Ready.";
        this.button_decompile.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        this.button_decompile.Location = new Point(632, 664);
        this.button_decompile.Size = new Size(105, 29);
        this.button_decompile.Text = "Decompile...";
        this.button_decompile.Click += this.button_decompile_Click;
        this.button_close.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        this.button_close.Location = new Point(743, 664);
        this.button_close.Size = new Size(69, 29);
        this.button_close.Text = "Close";
        this.button_close.Click += this.button_close_Click;

        this.AutoScaleDimensions = new SizeF(7F, 15F);
        this.AutoScaleMode = AutoScaleMode.Font;
        this.ClientSize = new Size(820, 706);
        this.Controls.Add(this.button_close);
        this.Controls.Add(this.button_decompile);
        this.Controls.Add(this.label_status);
        this.Controls.Add(this.tabControl_main);
        this.MinimumSize = new Size(820, 706);
        this.Name = "DecompileConfigDialog";
        this.StartPosition = FormStartPosition.CenterScreen;
        this.Text = "Audio Decompile";

        this.tabControl_main.ResumeLayout(false);
        this.tabPage_separation.ResumeLayout(false);
        this.tabPage_atomize.ResumeLayout(false);
        this.tabPage_output.ResumeLayout(false);
        this.panel_demucs.ResumeLayout(false);
        this.tableLayoutPanel_demucs.ResumeLayout(false);
        this.panel_analog.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)this.dataGridView_analogBands).EndInit();
        this.panel_deterministic.ResumeLayout(false);
        this.panel_atomizerV4.ResumeLayout(false);
        this.tableLayoutPanel_atomizerV4.ResumeLayout(false);
        this.panel_atomizerLegacy.ResumeLayout(false);
        this.groupBox_outputMode.ResumeLayout(false);
        this.groupBox_outputMode.PerformLayout();
        this.ResumeLayout(false);
        this.PerformLayout();

        void AddDemucsRow(int row, Label label, string text, Control value, Control? extra = null)
        {
            ConfigureLabel(label, text);
            this.tableLayoutPanel_demucs.Controls.Add(label, 0, row);
            this.tableLayoutPanel_demucs.Controls.Add(value, 1, row);
            value.Dock = DockStyle.Fill;
            if (extra == null)
            {
                this.tableLayoutPanel_demucs.SetColumnSpan(value, 3);
                return;
            }

            this.tableLayoutPanel_demucs.Controls.Add(extra, 2, row);
            extra.Dock = DockStyle.Fill;
            this.tableLayoutPanel_demucs.SetColumnSpan(extra, 2);
        }

        void AddDemucsPair(int row, Label leftLabel, string leftText, Control left, Control rightLabelOrCheck, string rightText, Control right)
        {
            ConfigureLabel(leftLabel, leftText);
            this.tableLayoutPanel_demucs.Controls.Add(leftLabel, 0, row);
            this.tableLayoutPanel_demucs.Controls.Add(left, 1, row);
            left.Dock = DockStyle.Fill;
            if (rightLabelOrCheck is Label rightLabel)
            {
                ConfigureLabel(rightLabel, rightText);
                this.tableLayoutPanel_demucs.Controls.Add(rightLabel, 2, row);
                this.tableLayoutPanel_demucs.Controls.Add(right, 3, row);
                right.Dock = DockStyle.Fill;
                return;
            }

            this.tableLayoutPanel_demucs.Controls.Add(rightLabelOrCheck, 2, row);
            rightLabelOrCheck.Dock = DockStyle.Fill;
            if (rightLabelOrCheck is CheckBox rightCheck)
            {
                rightCheck.Text = "Use CUDA CuFFT 12/13";
            }

            this.tableLayoutPanel_demucs.Controls.Add(right, 3, row);
            right.Dock = DockStyle.Fill;
        }

        void AddDemucsCheck(int row, CheckBox first, string firstText, CheckBox? second = null, string secondText = "")
        {
            first.Text = firstText;
            first.Dock = DockStyle.Fill;
            this.tableLayoutPanel_demucs.Controls.Add(first, 0, row);
            this.tableLayoutPanel_demucs.SetColumnSpan(first, second is null ? 4 : 2);
            if (second is null) return;
            second.Text = secondText;
            second.Dock = DockStyle.Fill;
            this.tableLayoutPanel_demucs.Controls.Add(second, 2, row);
            this.tableLayoutPanel_demucs.SetColumnSpan(second, 2);
        }

        void AddAtomizerPair(int row, Label leftLabel, string leftText, Control left, Control rightLabelOrCheck, string rightText, Control right)
        {
            ConfigureLabel(leftLabel, leftText);
            this.tableLayoutPanel_atomizerV4.Controls.Add(leftLabel, 0, row);
            this.tableLayoutPanel_atomizerV4.Controls.Add(left, 1, row);
            left.Dock = DockStyle.Fill;
            if (rightLabelOrCheck is Label rightLabel)
            {
                ConfigureLabel(rightLabel, rightText);
                this.tableLayoutPanel_atomizerV4.Controls.Add(rightLabel, 2, row);
                this.tableLayoutPanel_atomizerV4.Controls.Add(right, 3, row);
                right.Dock = DockStyle.Fill;
                return;
            }

            this.tableLayoutPanel_atomizerV4.Controls.Add(rightLabelOrCheck, 2, row);
            rightLabelOrCheck.Dock = DockStyle.Fill;
            this.tableLayoutPanel_atomizerV4.Controls.Add(right, 3, row);
            right.Dock = DockStyle.Fill;
        }
    }

    private static void ConfigureLabel(Label label, string text)
    {
        label.AutoEllipsis = true;
        label.Dock = DockStyle.Fill;
        label.Text = text;
        label.TextAlign = ContentAlignment.MiddleLeft;
    }

    private static void AddAnalogSetting(
        Panel panel,
        Label label,
        string text,
        NumericUpDown numeric,
        int labelX,
        int y,
        int labelWidth,
        int numericWidth)
    {
        label.AutoSize = false;
        label.Location = new Point(labelX, y + 3);
        label.Size = new Size(labelWidth, 18);
        label.Text = text;
        numeric.Location = new Point(labelX + labelWidth + 8, y);
        numeric.Size = new Size(numericWidth, 23);
        panel.Controls.Add(label);
        panel.Controls.Add(numeric);
    }

    private TabControl tabControl_main = null!;
    private TabPage tabPage_separation = null!;
    private TabPage tabPage_atomize = null!;
    private TabPage tabPage_output = null!;
    private ComboBox comboBox_separationMethod = null!;
    private Panel panel_separationContent = null!;
    private Panel panel_demucs = null!;
    private TableLayoutPanel tableLayoutPanel_demucs = null!;
    private Label label_demucsModel = null!;
    private ComboBox comboBox_demucsModel = null!;
    private Button button_browseModels = null!;
    private Label label_demucsProvider = null!;
    private ComboBox comboBox_demucsProvider = null!;
    private Label label_directMLDevices = null!;
    private CheckedListBox checkedListBox_directMLDevices = null!;
    private Label label_fourierWorkers = null!;
    private NumericUpDown numeric_fourierWorkers = null!;
    private Label label_chunkOverlap = null!;
    private NumericUpDown numeric_chunkOverlap = null!;
    private Label label_keepModelLoaded = null!;
    private NumericUpDown numeric_keepModelLoaded = null!;
    private Label label_graphOptimization = null!;
    private ComboBox comboBox_graphOptimization = null!;
    private Label label_executionMode = null!;
    private ComboBox comboBox_executionMode = null!;
    private Label label_intraThreads = null!;
    private NumericUpDown numeric_intraThreads = null!;
    private Label label_interThreads = null!;
    private NumericUpDown numeric_interThreads = null!;
    private Label label_profilePrefix = null!;
    private TextBox textBox_profilePrefix = null!;
    private Label label_optimizedModel = null!;
    private TextBox textBox_optimizedModel = null!;
    private CheckBox checkBox_cpuFallback = null!;
    private CheckBox checkBox_cpuOperators = null!;
    private CheckBox checkBox_disableDmlFusion = null!;
    private CheckBox checkBox_memoryPattern = null!;
    private CheckBox checkBox_cpuArena = null!;
    private CheckBox checkBox_disableSpinning = null!;
    private CheckBox checkBox_disablePrepacking = null!;
    private CheckBox checkBox_profiling = null!;
    private CheckBox checkBox_verboseLogging = null!;
    private CheckBox checkBox_cudaCuFft = null!;
    private ComboBox comboBox_cudaToolkit = null!;
    private Label label_demucsDeviceNotice = null!;
    private Label label_demucsStems = null!;
    private Panel panel_analog = null!;
    private DataGridView dataGridView_analogBands = null!;
    private DataGridViewTextBoxColumn column_analogBandName = null!;
    private DataGridViewTextBoxColumn column_analogBandLow = null!;
    private DataGridViewTextBoxColumn column_analogBandHigh = null!;
    private Button button_addAnalogBand = null!;
    private Button button_removeAnalogBand = null!;
    private Button button_autoAnalogBands = null!;
    private Label label_analogWindow = null!;
    private NumericUpDown numeric_analogWindow = null!;
    private Label label_analogOverlap = null!;
    private NumericUpDown numeric_analogOverlap = null!;
    private Label label_analogSharpness = null!;
    private NumericUpDown numeric_analogSharpness = null!;
    private CheckBox checkBox_analogSubtract = null!;
    private CheckBox checkBox_analogTimbre = null!;
    private CheckBox checkBox_analogHarmonic = null!;
    private Label label_analogStatus = null!;
    private Panel panel_deterministic = null!;
    private Label label_deterministicPreset = null!;
    private ComboBox comboBox_deterministicPreset = null!;
    private PropertyGrid propertyGrid_deterministic = null!;
    private Label label_deterministicProfiles = null!;
    private CheckedListBox checkedListBox_deterministicProfiles = null!;
    private CheckBox checkBox_deterministicAutomaticProfiles = null!;
    private Label label_deterministicNote = null!;
    private ComboBox comboBox_atomizer = null!;
    private Panel panel_atomizerContent = null!;
    private Panel panel_atomizerV4 = null!;
    private TableLayoutPanel tableLayoutPanel_atomizerV4 = null!;
    private Label label_atomizerSensitivity = null!;
    private ComboBox comboBox_atomizerSensitivity = null!;
    private Label label_minSlice = null!;
    private NumericUpDown numeric_minSlice = null!;
    private Label label_tailPadding = null!;
    private NumericUpDown numeric_tailPadding = null!;
    private CheckBox checkBox_singleAtomFallback = null!;
    private CheckBox checkBox_atomizerDeduplicate = null!;
    private Label label_maxVariants = null!;
    private NumericUpDown numeric_maxVariants = null!;
    private Label label_minimumRms = null!;
    private NumericUpDown numeric_minimumRms = null!;
    private Label label_minimumDuration = null!;
    private NumericUpDown numeric_minimumDuration = null!;
    private Label label_similarity = null!;
    private NumericUpDown numeric_similarity = null!;
    private Label label_atomizerV4Note = null!;
    private Panel panel_atomizerLegacy = null!;
    private Label label_legacySimilarity = null!;
    private NumericUpDown numeric_legacySimilarity = null!;
    private CheckBox checkBox_legacyAutoSimilarity = null!;
    private Label label_legacyNote = null!;
    private GroupBox groupBox_outputMode = null!;
    private CheckBox checkBox_projectOutput = null!;
    private CheckBox checkBox_collectionOutput = null!;
    private Label label_projectDetail = null!;
    private ComboBox comboBox_projectDetail = null!;
    private Label label_projectOverlap = null!;
    private ComboBox comboBox_projectOverlap = null!;
    private Label label_projectAttack = null!;
    private NumericUpDown numeric_projectAttack = null!;
    private CheckBox checkBox_collectionDeduplicate = null!;
    private Label label_workers = null!;
    private NumericUpDown numeric_workers = null!;
    private Label label_bpm = null!;
    private Label label_outputNote = null!;
    private Label label_status = null!;
    private Button button_decompile = null!;
    private Button button_close = null!;
    private ToolTip toolTip_config = null!;
}
