#nullable enable

namespace ModularAudience.Forms.Modules.Dialogs;

public partial class QuantumBeatEngineDialog
{
    private System.ComponentModel.IContainer? components;
    private MenuStrip menu = null!;
    private ToolStripMenuItem connectionMenu = null!, editMenu = null!, circuitMenu = null!, patternMenu = null!, outputMenu = null!, helpMenu = null!;
    private ToolStripMenuItem saveItem = null!, loadItem = null!, renameItem = null!, deleteResetItem = null!;
    private Label titleLabel = null!, summaryLabel = null!, connectionLabel = null!, delayLabel = null!, statusLabel = null!;
    private TabControl editorTabs = null!, resultTabs = null!;
    private TabPage circuitTab = null!, rhythmTab = null!, tracksTab = null!, connectionTab = null!;
    private SplitContainer mainSplit = null!;
    private PictureBox circuitPicture = null!, sequencePicture = null!;
    private DataGridView gatesGrid = null!, tracksGrid = null!, eventsGrid = null!, countsGrid = null!, backendsGrid = null!;
    private ComboBox providerCombo = null!, qasmVersionCombo = null!, presetCombo = null!, gateCombo = null!, arrangementCombo = null!;
    private ComboBox connectionProfileCombo = null!, authCombo = null!, backendCombo = null!, endpointCombo = null!;
    private TextBox credentialText = null!, crnText = null!, apiVersionText = null!, pythonText = null!, logicalQasmText = null!, hardwareQasmText = null!;
    private Button generateButton = null!, remapButton = null!, cancelButton = null!, renderButton = null!, playButton = null!, stopButton = null!, exportButton = null!;
    private Button connectButton = null!, disconnectButton = null!;
    private readonly Dictionary<string, NumericUpDown> numbers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CheckBox> checks = new(StringComparer.Ordinal);
    private ToolTip tips = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing) { this.ReleaseResources(); this.components?.Dispose(); }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        this.components = new System.ComponentModel.Container();
        this.tips = new ToolTip(this.components) { AutoPopDelay = 15000, InitialDelay = 450, ReshowDelay = 100 };
        this.SuspendLayout();
        this.Name = "QuantumBeatEngineDialog";
        this.Text = "Quantum Beat Engine";
        this.Font = new Font("Segoe UI", 9);
        this.AutoScaleMode = AutoScaleMode.Dpi;
        this.ClientSize = new Size(1340, 860);
        this.MinimumSize = new Size(1120, 760);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.KeyPreview = true;

        this.menu = new MenuStrip { Name = "menuStrip_quantum", Dock = DockStyle.Top };
        this.connectionMenu = new ToolStripMenuItem("Connection") { Name = "menu_connection" };
        this.editMenu = new ToolStripMenuItem("Edit") { Name = "menu_edit" };
        this.circuitMenu = new ToolStripMenuItem("Circuit") { Name = "menu_circuit" };
        this.patternMenu = new ToolStripMenuItem("Pattern") { Name = "menu_pattern" };
        this.outputMenu = new ToolStripMenuItem("Output") { Name = "menu_output" };
        this.helpMenu = new ToolStripMenuItem("Help") { Name = "menu_help" };
        this.menu.Items.AddRange([this.connectionMenu, this.editMenu, this.circuitMenu, this.patternMenu, this.outputMenu, this.helpMenu]);
        this.MainMenuStrip = this.menu;
        this.connectionMenu.DropDownItems.Add("IBM connection settings...", null, (_, _) => this.editorTabs.SelectedTab = this.connectionTab);
        this.connectionMenu.DropDownItems.Add("Test connection / Refresh backends", null, async (_, _) => await this.ConnectAsync());
        this.connectionMenu.DropDownItems.Add("Disconnect", null, (_, _) => this.Disconnect());
        this.connectionMenu.DropDownItems.Add("Use local simulator", null, (_, _) => this.providerCombo.SelectedIndex = 0);
        this.connectionMenu.DropDownItems.Add(new ToolStripSeparator());
        this.connectionMenu.DropDownItems.Add("Save encrypted connection profile", null, (_, _) => this.SaveConnection());
        this.connectionMenu.DropDownItems.Add("Load connection profile", null, (_, _) => this.LoadSelectedConnection());
        this.saveItem = new ToolStripMenuItem("Save...") { Name = "menu_save", ShortcutKeys = Keys.Control | Keys.S };
        this.loadItem = new ToolStripMenuItem("Load...") { Name = "menu_load", ShortcutKeys = Keys.Control | Keys.L };
        this.renameItem = new ToolStripMenuItem("Rename...") { Name = "menu_rename" };
        this.deleteResetItem = new ToolStripMenuItem("Reset current setup") { Name = "menu_deleteReset" };
        this.editMenu.DropDownItems.AddRange([this.saveItem, this.loadItem, this.renameItem, this.deleteResetItem]);
        this.saveItem.Click += (_, _) => this.SaveExperiment();
        this.loadItem.Click += (_, _) => this.ShowLibrary("Load");
        this.renameItem.Click += (_, _) => this.ShowLibrary("Rename");
        this.deleteResetItem.Click += (_, _) => { if (this.loadedExperimentName is null) this.ResetWorkspace(); else this.ShowLibrary("Delete"); };
        this.circuitMenu.DropDownItems.Add("Load selected preset", null, (_, _) => this.LoadPreset());
        this.circuitMenu.DropDownItems.Add("Randomize gates", null, (_, _) => this.RandomizeCircuit());
        this.circuitMenu.DropDownItems.Add("Clear gates", null, (_, _) => this.ReplaceGates([]));
        this.circuitMenu.DropDownItems.Add("Copy logical QASM", null, (_, _) => this.CopyText(this.logicalQasmText.Text));
        this.patternMenu.DropDownItems.Add("Measure circuit and generate", null, async (_, _) => await this.GenerateAsync());
        this.patternMenu.DropDownItems.Add("Remap cached measurements (no API call)", null, async (_, _) => await this.RemapAsync());
        this.outputMenu.DropDownItems.Add("Render audio", null, async (_, _) => await this.RenderAsync());
        this.outputMenu.DropDownItems.Add("Play", null, (_, _) => this.Play());
        this.outputMenu.DropDownItems.Add("Stop", null, (_, _) => this.Stop());
        this.outputMenu.DropDownItems.Add("Add rendered breakbeat to collection", null, (_, _) => this.ExportToCollection());
        this.outputMenu.DropDownItems.Add("Open rendered waveform", null, (_, _) => this.OpenWaveform());

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(12, 4, 12, 8) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 59)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2 };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 15));
        this.titleLabel = new Label { Text = "QUANTUM BEAT ENGINE", Font = new Font("Segoe UI", 15, FontStyle.Bold), Dock = DockStyle.Fill };
        this.summaryLabel = new Label { Text = "Draw circuits. Measure possibilities. Build a breakbeat.", Dock = DockStyle.Fill };
        this.connectionLabel = new Label { Text = "LOCAL SIMULATOR · no cloud connection", AutoSize = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight };
        this.delayLabel = new Label { Text = "Response delay: — ms", AutoSize = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight };
        header.Controls.Add(this.titleLabel, 0, 0); header.Controls.Add(this.summaryLabel, 0, 1);
        header.Controls.Add(this.connectionLabel, 1, 0); header.SetColumnSpan(this.connectionLabel, 2);
        header.Controls.Add(this.delayLabel, 1, 1);
        header.Controls.Add(this.Check("AutoSave", "Auto-save", true), 2, 1);
        root.Controls.Add(header, 0, 0);

        var run = Flow();
        this.providerCombo = Drop("comboBox_provider", ["Local state-vector simulator", "IBM Quantum REST"], 205);
        this.qasmVersionCombo = Drop("comboBox_qasmVersion", ["OpenQASM 2.0", "OpenQASM 3.0"], 135);
        run.Controls.AddRange([this.providerCombo, Caption("Shots"), this.Num("Shots", 1, 1_000_000, 1024, width: 82), this.qasmVersionCombo,
            this.Check("FreshLocalSeed", "Fresh local shots", false)]);
        this.generateButton = ActionButton("Measure + Generate", "button_generate", async (_, _) => await this.GenerateAsync(), 149);
        this.remapButton = ActionButton("Remap counts", "button_remap", async (_, _) => await this.RemapAsync(), 115);
        this.cancelButton = ActionButton("Cancel", "button_cancel", (_, _) => this.workCts?.Cancel(), 75);
        run.Controls.AddRange([this.generateButton, this.remapButton, this.cancelButton]);
        root.Controls.Add(run, 0, 1);

        this.mainSplit = new SplitContainer { Name = "splitContainer_studio", Size = new Size(1300, 580), Dock = DockStyle.Fill, Panel1MinSize = 485, Panel2MinSize = 400, SplitterDistance = 568 };
        this.editorTabs = new TabControl { Name = "tabControl_editors", Dock = DockStyle.Fill };
        this.circuitTab = new TabPage("Circuit designer"); this.rhythmTab = new TabPage("Rhythm + DSP"); this.tracksTab = new TabPage("Sample tracks"); this.connectionTab = new TabPage("IBM connection");
        this.editorTabs.TabPages.AddRange([this.circuitTab, this.rhythmTab, this.tracksTab, this.connectionTab]);
        this.mainSplit.Panel1.Controls.Add(this.editorTabs);
        this.resultTabs = new TabControl { Name = "tabControl_results", Dock = DockStyle.Fill };
        this.mainSplit.Panel2.Controls.Add(this.resultTabs);
        root.Controls.Add(this.mainSplit, 0, 2);
        this.InitializeCircuitDesigner(); this.InitializeRhythmDesigner(); this.InitializeTracksDesigner(); this.InitializeConnectionDesigner(); this.InitializeResultsDesigner();

        var transport = Flow();
        this.renderButton = ActionButton("Render audio", "button_render", async (_, _) => await this.RenderAsync());
        this.playButton = ActionButton("Play", "button_play", (_, _) => this.Play(), 70);
        this.stopButton = ActionButton("Stop", "button_stop", (_, _) => this.Stop(), 70);
        this.exportButton = ActionButton("Add to collection", "button_export", (_, _) => this.ExportToCollection(), 130);
        transport.Controls.AddRange([this.renderButton, this.playButton, this.stopButton, this.exportButton,
            this.Check("LoopPlayback", "Loop", true), this.Check("AutoRender", "Render after generation", true), this.Check("AutoPlay", "Auto-play", false)]);
        root.Controls.Add(transport, 0, 3);
        this.statusLabel = new Label { Name = "label_status", Dock = DockStyle.Fill, Text = "Ready. Local simulation does not need credentials or Python.", Padding = new Padding(5), BorderStyle = BorderStyle.FixedSingle };
        root.Controls.Add(this.statusLabel, 0, 4);
        this.Controls.Add(root); this.Controls.Add(this.menu);
        this.ResumeLayout(true);
    }

    private void InitializeCircuitDesigner()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(5) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 154));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 133));
        var width = Flow();
        width.Controls.AddRange([Caption("Qubits"), this.Num("Qubits", 4, 16, 4, width: 60), this.Check("AutoQubits", "Infer width from gates / track mapping", false)]);
        layout.Controls.Add(width, 0, 0);
        var canvas = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(19, 26, 38) };
        this.circuitPicture = new PictureBox { Name = "pictureBox_circuit", Location = Point.Empty, SizeMode = PictureBoxSizeMode.AutoSize, TabStop = false };
        canvas.Controls.Add(this.circuitPicture); layout.Controls.Add(canvas, 0, 1);
        var gateTools = Flow(); gateTools.WrapContents = true;
        this.gateCombo = Drop("comboBox_gate", ["H", "X", "CX", "Ry"], 55);
        gateTools.Controls.AddRange([this.gateCombo, Caption("q / control"), this.Num("GateQubit", 0, 15, 0, width: 48), Caption("target"),
            this.Num("GateTarget", 0, 15, 1, width: 48), Caption("Ry °"), this.Num("GateAngle", -360, 360, 45, 2, 72),
            ActionButton("Add gate", "button_addGate", (_, _) => this.AddGate(), 82),
            ActionButton("Remove", "button_removeGate", (_, _) => this.RemoveGate(), 75),
            ActionButton("↑", "button_gateUp", (_, _) => this.MoveGate(-1), 32), ActionButton("↓", "button_gateDown", (_, _) => this.MoveGate(1), 32)]);
        layout.Controls.Add(gateTools, 0, 2);
        this.gatesGrid = Grid("dataGridView_gates", false);
        this.gatesGrid.AutoGenerateColumns = false;
        this.gatesGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Enabled", DataPropertyName = "Enabled", HeaderText = "On", Width = 35, AutoSizeMode = DataGridViewAutoSizeColumnMode.None });
        this.gatesGrid.Columns.Add(new DataGridViewComboBoxColumn { Name = "Gate", DataPropertyName = "Gate", HeaderText = "Gate", DataSource = new[] { "H", "X", "CX", "Ry" }, Width = 62 });
        this.gatesGrid.Columns.Add(TextColumn("Qubit", "Qubit / control")); this.gatesGrid.Columns.Add(TextColumn("Target", "CX target")); this.gatesGrid.Columns.Add(TextColumn("AngleDegrees", "Ry degrees"));
        layout.Controls.Add(this.gatesGrid, 0, 3);
        var presets = Flow();
        this.presetCombo = Drop("comboBox_circuitPreset", ["Bell break", "Weighted groove", "GHZ / correlated", "Independent superposition", "Empty"], 204);
        presets.Controls.AddRange([this.presetCombo, ActionButton("Load preset", "button_preset", (_, _) => this.LoadPreset(), 94),
            ActionButton("Randomize", "button_randomizeCircuit", (_, _) => this.RandomizeCircuit(), 94), ActionButton("Clear", "button_clearCircuit", (_, _) => this.ReplaceGates([]), 65)]);
        layout.Controls.Add(presets, 0, 4);
        var randomGroup = new GroupBox { Text = "Circuit randomization · probabilities change real quantum gates", Dock = DockStyle.Fill };
        var random = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 3, Padding = new Padding(5) };
        random.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 29)); random.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 21));
        random.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 29)); random.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 21));
        AddPair(random, 0, 0, "Layers", this.Num("Layers", 1, 16, 2)); AddPair(random, 2, 0, "Seed", this.Num("Seed", 0, int.MaxValue, 42));
        AddPair(random, 0, 1, "Hadamard %", this.Num("HadamardChance", 0, 100, 40)); AddPair(random, 2, 1, "CNOT %", this.Num("EntanglementChance", 0, 100, 45));
        AddPair(random, 0, 2, "Min rotation °", this.Num("MinimumAngleDegrees", -360, 360, -180, 1)); AddPair(random, 2, 2, "Max rotation °", this.Num("MaximumAngleDegrees", -360, 360, 180, 1));
        randomGroup.Controls.Add(random); layout.Controls.Add(randomGroup, 0, 5);
        this.circuitTab.Controls.Add(layout);
        this.tips.SetToolTip(this.circuitPicture, "Click a wire to select a qubit; right-click a wire to select the CX target. Click a gate column to select its row.");
    }

    private void InitializeRhythmDesigner()
    {
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(5) };
        var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(3) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 63)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 37));
        void Row(string caption, Control input) { int row = table.RowCount++; table.RowStyles.Add(new RowStyle(SizeType.AutoSize)); table.Controls.Add(Caption(caption), 0, row); table.Controls.Add(input, 1, row); input.Anchor = AnchorStyles.Left | AnchorStyles.Right; }
        Row("Tempo (BPM)", this.Num("Bpm", 20, 400, 120, 2)); Row("Bars", this.Num("Bars", 1, 16, 2)); Row("Steps per bar / resolution", this.Num("StepsPerBar", 4, 64, 16));
        this.arrangementCombo = Drop("comboBox_arrangement", ["Independent bars", "Repeat first bar", "Alternate A / B bars"], 170); Row("Arrangement", this.arrangementCombo);
        Row("Post-map density %", this.Num("Density", 0, 100, 80)); Row("Swing % (odd steps)", this.Num("Swing", 0, 49, 0, 1));
        Row("Modifier starting qubit", this.Num("ModifierQubit", 0, 15, 2));
        Row("One triggered track per grid step", this.Check("Interleaved", "Interleaved", false));
        Row("Measured frequency → velocity", this.Check("QuantumVelocity", "Quantum velocity", true));
        Row("Minimum velocity %", this.Num("MinimumVelocity", 0, 100, 35)); Row("Maximum velocity %", this.Num("MaximumVelocity", 0, 100, 100));
        Row("Classical ghost-note variation", this.Check("GhostNotes", "Enable ghost notes", false)); Row("Ghost chance %", this.Num("GhostChance", 0, 100, 8)); Row("Ghost velocity scale %", this.Num("GhostVelocityScale", 0, 100, 30));
        Row("Measured bits → note duration", this.Check("VaryDuration", "Vary duration", false));
        Row("Minimum / fixed duration (steps)", this.Num("MinimumDurationSteps", 0.125m, 16, 0.5m, 3)); Row("Maximum duration (steps)", this.Num("MaximumDurationSteps", 0.125m, 16, 2, 3));
        Row("Measured bits → pitch", this.Check("PitchEnabled", "Pitch shift", false)); Row("Minimum pitch (cents)", this.Num("MinimumPitchCents", -1200, 1200, -500));
        Row("Maximum pitch (cents)", this.Num("MaximumPitchCents", -1200, 1200, 300)); Row("Pitch quantization (cents)", this.Num("PitchStepCents", 1, 1200, 100));
        Row("Measured bits → duration factor", this.Check("TimeStretchEnabled", "Time stretch", false)); Row("Minimum stretch ×", this.Num("MinimumStretch", 0.5m, 2, 0.75m, 2)); Row("Maximum stretch ×", this.Num("MaximumStretch", 0.5m, 2, 1.5m, 2));
        Row("Measured bits → speed / pitch", this.Check("VarispeedEnabled", "Varispeed", false)); Row("Minimum speed ×", this.Num("MinimumSpeed", 0.5m, 2, 0.75m, 2)); Row("Maximum speed ×", this.Num("MaximumSpeed", 0.5m, 2, 1.25m, 2));
        Row("Measured bits → sample start", this.Check("CutEnabled", "Slice cut offset", false)); Row("Maximum cut offset (ms)", this.Num("MaximumCutOffsetMs", 0, 10_000, 80, 1));
        Row("Measured bit → reverse", this.Check("ReverseEnabled", "Reverse variation", false));
        Row("Fit PCM to note duration", this.Check("FitToNoteDuration", "Pitch-preserving fit", false)); Row("Limit samples to note length", this.Check("TruncateNotes", "Truncate notes", true));
        Row("Keep tails after the arrangement", this.Check("KeepEndTails", "Keep end tails", false)); Row("Normalize rendered peak", this.Check("NormalizeOutput", "Normalize to 0.95", true));
        Row("Output gain (dB)", this.Num("OutputGainDb", -48, 12, 0, 1));
        var explanation = new Label { Text = "Audio stays classical. Measured bit pairs drive the enabled DSP ranges.\nResolution and arrangement do not require one qubit per note.", AutoSize = true, MaximumSize = new Size(490, 0), Margin = new Padding(5, 12, 5, 10) };
        int last = table.RowCount++; table.Controls.Add(explanation, 0, last); table.SetColumnSpan(explanation, 2);
        scroll.Controls.Add(table); this.rhythmTab.Controls.Add(scroll);
    }

    private void InitializeTracksDesigner()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(5) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Text = "Each selected sample is an instrument track.\nAssign a trigger bit, invert it for anti-phase notes, and adjust density / gain.\nTracks may share qubits; q0 is the rightmost measurement bit.", Dock = DockStyle.Fill }, 0, 0);
        this.tracksGrid = Grid("dataGridView_tracks", false); this.tracksGrid.AutoGenerateColumns = false;
        this.tracksGrid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = "Enabled", HeaderText = "On", Name = "Enabled", Width = 35 });
        var name = TextColumn("Sample", "Sample track"); name.ReadOnly = true; name.FillWeight = 180; this.tracksGrid.Columns.Add(name);
        this.tracksGrid.Columns.Add(TextColumn("TriggerQubit", "Trigger q"));
        this.tracksGrid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = "Invert", HeaderText = "Invert", Name = "Invert" });
        this.tracksGrid.Columns.Add(TextColumn("DensityPercent", "Density %")); this.tracksGrid.Columns.Add(TextColumn("StepOffset", "Offset steps"));
        this.tracksGrid.Columns.Add(TextColumn("GainPercent", "Gain %")); this.tracksGrid.Columns.Add(TextColumn("PitchCents", "Pitch cents"));
        layout.Controls.Add(this.tracksGrid, 0, 1); this.tracksTab.Controls.Add(layout);
    }

    private void InitializeConnectionDesigner()
    {
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(5) };
        var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 37)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 63));
        void Row(string caption, Control input) { int row = table.RowCount++; table.Controls.Add(Caption(caption), 0, row); table.Controls.Add(input, 1, row); input.Anchor = AnchorStyles.Left | AnchorStyles.Right; }
        this.connectionProfileCombo = new ComboBox { Name = "comboBox_connectionProfile", Text = "Default", Width = 240 };
        this.authCombo = Drop("comboBox_authentication", ["API key (recommended)", "IAM bearer token (expires)"], 240);
        this.credentialText = new TextBox { Name = "textBox_credential", UseSystemPasswordChar = true, Width = 240 };
        this.crnText = new TextBox { Name = "textBox_serviceCrn", Width = 240 };
        this.backendCombo = new ComboBox { Name = "comboBox_backend", Width = 240 };
        this.endpointCombo = new ComboBox { Name = "comboBox_endpoint", Width = 240 };
        this.endpointCombo.Items.AddRange(["https://quantum.cloud.ibm.com/api/v1/", "https://eu-de.quantum.cloud.ibm.com/api/v1/"]); this.endpointCombo.SelectedIndex = 0;
        this.apiVersionText = new TextBox { Name = "textBox_apiVersion", Text = "2026-04-15", Width = 240 };
        this.pythonText = new TextBox { Name = "textBox_python", Text = "python", Width = 240 };
        Row("Connection profile", this.connectionProfileCombo); Row("Authentication", this.authCombo); Row("API key / IAM token", this.credentialText);
        Row("Reveal credential", this.Check("ShowCredential", "Show in this window", false)); Row("Instance CRN", this.crnText); Row("Backend", this.backendCombo);
        Row("REST base URL (HTTPS)", this.endpointCombo); Row("IBM API version", this.apiVersionText); Row("Qiskit Python executable", this.pythonText);
        Row("Request timeout (s)", this.Num("RequestTimeout", 1, 300, 30)); Row("Job deadline (s)", this.Num("JobTimeout", 5, 7200, 300));
        Row("Polling interval (ms)", this.Num("PollInterval", 100, 60_000, 1000)); Row("Read retries", this.Num("ReadRetries", 0, 10, 2));
        Row("Transpilation level", this.Num("OptimizationLevel", 0, 3, 1));
        Row("Hardware source", this.Check("UsePreparedIsa", "Use pasted ISA QASM instead of Qiskit", false));
        var consent = this.Check("AllowCloudJobs", "Allow IBM cloud jobs (may use paid resources)", false); consent.ForeColor = Color.DarkOrange;
        int consentRow = table.RowCount++; table.Controls.Add(consent, 0, consentRow); table.SetColumnSpan(consent, 2);
        var actions = Flow(); actions.AutoSize = true;
        this.connectButton = ActionButton("Test / Connect", "button_connect", async (_, _) => await this.ConnectAsync(), 113);
        this.disconnectButton = ActionButton("Disconnect", "button_disconnect", (_, _) => this.Disconnect(), 99);
        actions.Controls.AddRange([this.connectButton, this.disconnectButton, ActionButton("Save protected", "button_saveConnection", (_, _) => this.SaveConnection(), 114),
            ActionButton("Load profile", "button_loadConnection", (_, _) => this.LoadSelectedConnection(), 100),
            ActionButton("Forget saved profile", "button_forgetConnection", (_, _) => this.ForgetConnection(), 145)]);
        int actionRow = table.RowCount++; table.Controls.Add(actions, 0, actionRow); table.SetColumnSpan(actions, 2);
        var privacy = new Label { Text = "Connection tests only read backend information; they do not submit jobs.\nCredentials are saved in AppData JSON encrypted with Windows DPAPI for this user.\nCloud job permission is never restored automatically. No network request runs on startup.", AutoSize = true, MaximumSize = new Size(490, 0), Margin = new Padding(5, 8, 5, 8) };
        int privacyRow = table.RowCount++; table.Controls.Add(privacy, 0, privacyRow); table.SetColumnSpan(privacy, 2);
        this.backendsGrid = Grid("dataGridView_backends", true); this.backendsGrid.Height = 155; this.backendsGrid.Dock = DockStyle.None; this.backendsGrid.Width = 485;
        int gridRow = table.RowCount++; table.Controls.Add(this.backendsGrid, 0, gridRow); table.SetColumnSpan(this.backendsGrid, 2);
        scroll.Controls.Add(table); this.connectionTab.Controls.Add(scroll);
        this.tips.SetToolTip(this.pythonText, "Use the Python executable from a virtual environment containing Qiskit. No automatic installation is performed.");
    }

    private void InitializeResultsDesigner()
    {
        var sequence = new TabPage("Sequence diagram"); var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(19, 26, 38) };
        this.sequencePicture = new PictureBox { Name = "pictureBox_sequence", SizeMode = PictureBoxSizeMode.AutoSize, TabStop = false }; panel.Controls.Add(this.sequencePicture); sequence.Controls.Add(panel);
        var events = new TabPage("Note timings"); this.eventsGrid = Grid("dataGridView_events", true); events.Controls.Add(this.eventsGrid);
        var logical = new TabPage("Logical QASM"); this.logicalQasmText = CodeBox("textBox_logicalQasm", true); logical.Controls.Add(this.logicalQasmText);
        var hardware = new TabPage("Hardware ISA QASM"); this.hardwareQasmText = CodeBox("textBox_hardwareQasm", true); hardware.Controls.Add(this.hardwareQasmText);
        var counts = new TabPage("Measurement counts"); this.countsGrid = Grid("dataGridView_counts", true); counts.Controls.Add(this.countsGrid);
        this.resultTabs.TabPages.AddRange([sequence, events, logical, hardware, counts]);
    }

    private NumericUpDown Num(string key, decimal minimum, decimal maximum, decimal value, int decimals = 0, int width = 90)
    {
        var number = new NumericUpDown { Name = "numericUpDown_" + key, Minimum = minimum, Maximum = maximum, Value = value,
            DecimalPlaces = decimals, Increment = decimals > 0 ? (decimals > 2 ? 0.125m : 0.05m) : 1, Width = width, Margin = new Padding(3, 4, 3, 4) };
        this.numbers.Add(key, number); return number;
    }
    private CheckBox Check(string key, string text, bool value)
    {
        var check = new CheckBox { Name = "checkBox_" + key, Text = text, Checked = value, AutoSize = true, Margin = new Padding(3, 6, 3, 5) };
        this.checks.Add(key, check); return check;
    }
    private static FlowLayoutPanel Flow() => new() { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty, Padding = new Padding(0, 3, 0, 2), AutoScroll = true };
    private static Label Caption(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(3, 8, 3, 5), Anchor = AnchorStyles.Left };
    private static ComboBox Drop(string name, string[] items, int width)
    {
        var combo = new ComboBox { Name = name, DropDownStyle = ComboBoxStyle.DropDownList, Width = width, Margin = new Padding(3, 4, 3, 4) };
        combo.Items.AddRange(items); combo.SelectedIndex = 0; return combo;
    }
    private static Button ActionButton(string text, string name, EventHandler handler, int width = 105)
    {
        var button = new Button { Text = text, Name = name, Width = width, Height = 27, Margin = new Padding(3, 3, 3, 3), UseVisualStyleBackColor = true };
        button.Click += handler; return button;
    }
    private static DataGridView Grid(string name, bool readOnly) => new()
    {
        Name = name, Dock = DockStyle.Fill, ReadOnly = readOnly, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false, BackgroundColor = SystemColors.Window, BorderStyle = BorderStyle.FixedSingle, AutoGenerateColumns = true
    };
    private static DataGridViewTextBoxColumn TextColumn(string property, string caption) => new() { Name = property, DataPropertyName = property, HeaderText = caption };
    private static TextBox CodeBox(string name, bool readOnly) => new()
    {
        Name = name, Dock = DockStyle.Fill, Multiline = true, ReadOnly = readOnly, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 9), MaxLength = 524_288
    };
    private static void AddPair(TableLayoutPanel panel, int column, int row, string label, Control input)
    { panel.Controls.Add(Caption(label), column, row); panel.Controls.Add(input, column + 1, row); input.Dock = DockStyle.Fill; }
}
