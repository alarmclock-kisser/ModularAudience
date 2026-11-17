using Microsoft.ML.OnnxRuntime;
using ModularAudience.Audio;
using ModularAudience.Onnx;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using System.Text.Json;

namespace ModularAudience.Forms;

public partial class OnnxDemucsDialog : Form
{
    private readonly AudioObj OriginalAudio;
    private OnnxService? Onnx;
    private DemucsDialogSettings settings = new();
    private CancellationTokenSource cancellation = new();
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(850, 0), Text = "Looking for Demucs models…" };
    private readonly Button load = new() { Text = "Load model", AutoSize = true };
    private readonly Button browse = new() { Text = "Browse models directory…", AutoSize = true };
    private readonly Button cancel = new() { Text = "Cancel", AutoSize = true, Enabled = false };
    private readonly ComboBox provider = DropDown(["DirectML", "CPU"]);
    private readonly ComboBox optimization = DropDown(Enum.GetNames<GraphOptimizationLevel>());
    private readonly ComboBox execution = DropDown(Enum.GetNames<ExecutionMode>());
    private readonly NumericUpDown intraThreads = Number(0, Environment.ProcessorCount);
    private readonly NumericUpDown interThreads = Number(0, Environment.ProcessorCount);
    private readonly NumericUpDown workers = Number(1, Environment.ProcessorCount);
    private readonly NumericUpDown overlap = new() { Minimum = 0, Maximum = 75, Value = 25, Increment = 5, Width = 120 };
    private readonly NumericUpDown keepModelLoaded = new() { Name = "keepModelLoaded", Minimum = 0, Maximum = 86400, Value = 0, Increment = 30, Width = 120,
        AccessibleName = "Keep Model Loaded Seconds" };
    private readonly List<SharedModelPool<OnnxService>.Lease> modelLeases = [];
    private string[] loadedCacheKeys = [];
    private readonly CheckBox cpuFallback = Check("Fall back to CPU if DirectML fails");
    private readonly CheckBox cpuOperators = Check("Allow CPU operators in DirectML graphs");
    private readonly CheckBox validate = Check("Compare DirectML with CPU on load (diagnostic only)");
    private readonly CheckBox disableDmlFusion = Check("Disable DirectML graph fusion (Demucs compatibility)");
    private readonly CheckBox memoryPattern = Check("Enable memory pattern (CPU only)");
    private readonly CheckBox cpuArena = Check("Enable CPU memory arena");
    private readonly CheckBox spinning = Check("Disable worker spinning");
    private readonly CheckBox prepacking = Check("Disable weight prepacking");
    private readonly CheckBox profiling = Check("Enable ONNX profiling");
    private readonly CheckBox logging = Check("Verbose ONNX session logging");
    private readonly TextBox profilePath = new() { Dock = DockStyle.Fill };
    private readonly TextBox optimizedPath = new() { Dock = DockStyle.Fill };
    private readonly TextBox advanced = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Height = 65, Dock = DockStyle.Fill };
    private readonly TableLayoutPanel optionsPanel = new() { AutoSize = false, Dock = DockStyle.Fill, ColumnCount = 4, Margin = Padding.Empty };
    private readonly TableLayoutPanel checksPanel = new() { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2 };
    private int optionCount;
    private bool busy;
    private bool initializing = true;
    private bool closeRequested;
    private bool loadedWithCurrentOptions;
    private string? deviceNotice;
    private DemucsCudaDevice[] cudaDevices = [];
    private DemucsCudaToolkit[] cudaToolkits = [];
    private bool CanUseCudaCuFft => this.cudaDevices.Length > 0 && this.cudaToolkits.Length > 0;
    private int SelectedCudaToolkitVersion => this.comboBox_cudaToolkitVersion.SelectedItem is CudaToolkitChoice choice
        ? choice.MajorVersion
        : this.cudaToolkits.FirstOrDefault()?.MajorVersion ?? this.settings.Options.CudaToolkitVersion;

    private sealed record DirectMLDeviceChoice(DirectMLDevices.Adapter Adapter, DemucsCudaDevice? Cuda)
    {
        public override string ToString() => $"DirectML {this.Adapter.Id} — {this.Adapter.Name}";
    }

    private sealed record CudaToolkitChoice(int MajorVersion)
    {
        public override string ToString() => $"CUDA {this.MajorVersion}";
    }

    public OnnxDemucsDialog(AudioObj audioObj)
    {
        this.InitializeComponent();
        this.optimization.SelectedItem = GraphOptimizationLevel.ORT_ENABLE_BASIC.ToString();
        this.OriginalAudio = audioObj.Clone();
        this.Text = "Demucs Source Separation";
        this.MaximumSize = Size.Empty;
        this.StartPosition = FormStartPosition.CenterParent;
        this.Controls.Clear();
        this.comboBox_models.DropDownStyle = ComboBoxStyle.DropDownList;
        this.comboBox_models.DisplayMember = nameof(ModelItem.Name);
        this.comboBox_models.Dock = DockStyle.Fill;
        this.comboBox_models.MinimumSize = Size.Empty;
        this.comboBox_models.Size = new Size(100, 23);
        this.checkedListBox_stems.Dock = DockStyle.Fill;
        this.checkedListBox_stems.Height = 115;
        this.checkedListBox_stems.IntegralHeight = false;
        this.checkedListBox_stems.Size = new Size(100, 112);
        this.progressBar_inferencing.Dock = DockStyle.Fill;
        this.progressBar_inferencing.Maximum = 1000;
        this.progressBar_inferencing.Size = new Size(100, 22);
        this.button_run.Text = "Run";
        this.button_run.AutoSize = true;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(10), Margin = Padding.Empty };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var modelRow = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
        modelRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        modelRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        modelRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        modelRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        modelRow.Controls.Add(this.comboBox_models, 0, 0);
        modelRow.Controls.Add(this.browse, 1, 0);
        var retentionRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        retentionRow.Controls.Add(new Label { Text = "Keep Model Loaded Seconds", AutoSize = true, Margin = new Padding(3, 7, 8, 3) });
        retentionRow.Controls.Add(this.keepModelLoaded);
        retentionRow.Controls.Add(new Label { Text = "0 = unload immediately; retention starts after closing this window", AutoSize = true, Margin = new Padding(8, 7, 3, 3) });
        modelRow.Controls.Add(retentionRow, 0, 1);
        modelRow.SetColumnSpan(retentionRow, 2);
        this.keepModelLoaded.ValueChanged += (_, _) =>
        {
            if (this.initializing) return;
            this.settings.KeepModelLoadedSeconds = (int)this.keepModelLoaded.Value;
            try { this.settings.Save(); }
            catch (Exception error) { this.ShowError("Unable to save model retention setting", error); }
        };
        for (int i = 0; i < 2; i++)
        {
            this.optionsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155));
            this.optionsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        }
        this.AddOption("Execution provider", this.provider);
        this.AddOption("Graph optimization (DirectML uses BASIC)", this.optimization);
        this.AddOption("Execution mode (CPU only)", this.execution);
        this.AddOption("Intra-op threads (0 = auto)", this.intraThreads);
        this.AddOption("Inter-op threads (0 = auto)", this.interThreads);
        this.AddOption("Profiling output prefix", this.profilePath);
        this.AddOption("Save optimized model (optional)", this.optimizedPath);
        this.AddOption("CPU FFT workers (fallback)", this.workers);
        this.AddOption("Parallel DirectML devices", this.checkedListBox_directMLDevices);
        this.AddOption("Chunk overlap (%)", this.overlap);
        int checksRow = (this.optionCount + 1) / 2;
        for (int row = 0; row < checksRow; row++) this.optionsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        this.optionsPanel.RowStyles[4].Height = 82;
        this.optionsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));
        this.optionsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var checkGroup = new GroupBox { Text = "Execution and FFT options", Dock = DockStyle.Fill, AutoSize = false, Padding = new Padding(8, 18, 8, 6), Margin = new Padding(3, 5, 3, 5) };
        this.checksPanel.AutoSize = false;
        this.checksPanel.Margin = Padding.Empty;
        this.checksPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        this.checksPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        int checkIndex = 0;
        foreach (var check in new[] { this.cpuFallback, this.cpuOperators, this.validate, this.disableDmlFusion, this.memoryPattern, this.cpuArena, this.spinning, this.prepacking, this.profiling, this.logging, this.useCudaCuFft })
        {
            check.AutoSize = false;
            check.Dock = DockStyle.Fill;
            check.Margin = new Padding(3, 1, 3, 1);
            check.AutoEllipsis = true;
            this.checksPanel.Controls.Add(check, checkIndex % 2, checkIndex / 2);
            checkIndex++;
        }
        this.comboBox_cudaToolkitVersion.Margin = new Padding(3, 4, 3, 4);
        this.checksPanel.Controls.Add(this.comboBox_cudaToolkitVersion, 1, checkIndex / 2);
        this.checksPanel.RowCount = (checkIndex + 1) / 2;
        for (int row = 0; row < this.checksPanel.RowCount; row++) this.checksPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / this.checksPanel.RowCount));
        checkGroup.Controls.Add(this.checksPanel);
        this.optionsPanel.Controls.Add(checkGroup, 0, checksRow);
        this.optionsPanel.SetColumnSpan(checkGroup, 4);
        this.optionCount = (checksRow + 1) * 2;
        this.AddOption("Extra ONNX session entries\n(one key=value per line)", this.advanced);
        var qualityInfo = new Label { AutoSize = false, Dock = DockStyle.Fill, AutoEllipsis = true,
            Text = "CuFFT is selected automatically when a compatible CUDA toolkit is available; otherwise parallel CPU FFT is used.\nFFT 4096 / hop 1024 (fixed). Overlap: 0% fast; 25% balanced; 50–75% smoother boundaries, more work." };
        this.AddOption("Separation quality", qualityInfo);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        buttons.Controls.AddRange([this.load, this.button_run, this.cancel]);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = false, Height = 82, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        this.status.AutoSize = false;
        this.status.MaximumSize = Size.Empty;
        this.status.Dock = DockStyle.Fill;
        this.status.AutoEllipsis = true;
        footer.Controls.Add(this.status, 0, 0);
        footer.Controls.Add(this.progressBar_inferencing, 0, 1);
        root.RowStyles[4] = new RowStyle(SizeType.Absolute, 82);
        this.progressBar_inferencing.Height = 23;
        root.Controls.Add(modelRow, 0, 0);
        root.Controls.Add(this.optionsPanel, 0, 1);
        root.Controls.Add(this.checkedListBox_stems, 0, 2);
        root.Controls.Add(buttons, 0, 3);
        root.Controls.Add(footer, 0, 4);
        this.Controls.Add(root);
        this.load.Click += this.Load_Click;
        this.browse.Click += async (_, _) => await this.BrowseModelsAsync();
        this.cancel.Click += (_, _) => { this.cancellation.Cancel(); this.status.Text = "Cancelling after the current operation…"; };
        foreach (Control control in this.optionsPanel.Controls.Cast<Control>().Concat(this.checksPanel.Controls.Cast<Control>()))
        {
            if (ReferenceEquals(control, this.keepModelLoaded)
                || ReferenceEquals(control, this.useCudaCuFft)
                || ReferenceEquals(control, this.comboBox_cudaToolkitVersion)) continue;
            if (control is CheckBox check) check.CheckedChanged += (_, _) => this.OptionsChanged();
            if (control is ComboBox combo) combo.SelectedIndexChanged += (_, _) => this.OptionsChanged();
            if (control is NumericUpDown number) number.ValueChanged += (_, _) => this.OptionsChanged();
            if (control is TextBox text) text.TextChanged += (_, _) => this.OptionsChanged();
        }
        this.SetBusy(false);
    }

    private static ComboBox DropDown(string[] values)
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        combo.Items.AddRange(values);
        if (values.Length > 0) combo.SelectedIndex = 0;
        return combo;
    }
    private static NumericUpDown Number(int min, int max) => new() { Minimum = min, Maximum = max, Width = 120 };
    private static CheckBox Check(string text) => new() { Text = text, AutoSize = true };
    private void AddOption(string caption, Control control)
    {
        int row = this.optionCount / 2;
        int column = this.optionCount++ % 2 * 2;
        this.optionsPanel.Controls.Add(new Label { Text = caption, AutoSize = false, Dock = DockStyle.Fill,
            TextAlign = ReferenceEquals(control, this.overlap) ? ContentAlignment.TopLeft : ContentAlignment.MiddleLeft,
            AutoEllipsis = true, Margin = new Padding(3, 2, 3, 2) }, column, row);
        control.MinimumSize = Size.Empty;
        if (control is ComboBox or TextBox) control.Width = 100;
        control.Margin = new Padding(3, 4, 3, 4);
        this.optionsPanel.Controls.Add(control, column + 1, row);
    }

    private void LoadDirectMLDevices(DirectMLDevices.Adapter[] adapters, DemucsCudaDeviceCatalog cudaCatalog)
    {
        this.cudaDevices = cudaCatalog.Devices.ToArray();
        this.cudaToolkits = cudaCatalog.CuFftToolkits.ToArray();
        this.comboBox_cudaToolkitVersion.Items.Clear();
        this.comboBox_cudaToolkitVersion.Items.AddRange(this.cudaToolkits
            .Select(toolkit => (object)new CudaToolkitChoice(toolkit.MajorVersion)).ToArray());
        this.comboBox_cudaToolkitVersion.Visible = this.CanUseCudaCuFft && this.cudaToolkits.Length > 1;
        this.checkedListBox_directMLDevices.Items.Clear();
        foreach (var adapter in adapters)
        {
            DemucsCudaDevice? cuda = this.cudaDevices.FirstOrDefault(device =>
                DirectMLDevices.MatchCudaDevice(device, [adapter]) is not null);
            this.checkedListBox_directMLDevices.Items.Add(new DirectMLDeviceChoice(adapter, cuda), false);
        }

        var notices = new List<string>();
        if (!string.IsNullOrWhiteSpace(cudaCatalog.Warning)) notices.Add(cudaCatalog.Warning);
        if (adapters.Length == 0) notices.Add("No DirectML adapters were detected; choose CPU execution.");
        if (this.cudaDevices.Length == 0) notices.Add("No CUDA devices were detected; CUDA CuFFT is disabled.");
        else if (this.cudaToolkits.Length == 0) notices.Add("No supported CUDA CuFFT toolkit was detected; parallel CPU FFT will be used.");
        this.deviceNotice = string.Join(" ", notices.Distinct(StringComparer.Ordinal));
    }

    private DirectMLDeviceChoice? FindDirectMLDevice(int id) =>
        this.checkedListBox_directMLDevices.Items.Cast<DirectMLDeviceChoice>()
            .FirstOrDefault(choice => choice.Adapter.Id == id);

    private void UpdateCudaCuFftLabel() =>
        this.useCudaCuFft.Text = this.CanUseCudaCuFft
            ? $"Use CUDA CuFFT {this.SelectedCudaToolkitVersion}"
            : "Use CUDA CuFFT (not detected)";

    private void useCudaCuFft_CheckedChanged(object? sender, EventArgs e) => this.OptionsChanged();

    private void comboBox_cudaToolkitVersion_SelectedIndexChanged(object? sender, EventArgs e)
    {
        this.UpdateCudaCuFftLabel();
        this.OptionsChanged();
    }

    private int[] GetSelectedDirectMLDeviceIds() =>
        this.checkedListBox_directMLDevices.CheckedItems.Cast<DirectMLDeviceChoice>()
            .Select(choice => choice.Adapter.Id).Distinct().ToArray();

    private DemucsLoadOptions[] GetDesiredOptions()
    {
        var options = this.ReadOptions();
        if (!options.UseDirectML) return [options];
        var selected = this.checkedListBox_directMLDevices.CheckedItems.Cast<DirectMLDeviceChoice>()
            .DistinctBy(choice => choice.Adapter.Id).ToArray();
        if (selected.Length == 0)
            throw new ArgumentException("Select at least one DirectML device or choose CPU execution.");
        return selected.Select(choice =>
        {
            var copy = JsonSerializer.Deserialize<DemucsLoadOptions>(JsonSerializer.Serialize(options))!;
            copy.UseDirectML = true;
            copy.DeviceId = choice.Adapter.Id;
            copy.CudaDeviceId = copy.UseCudaCuFft ? choice.Cuda?.Id ?? -1 : -1;
            copy.DeviceLuid = choice.Adapter.Luid;
            return copy;
        }).ToArray();
    }

    private string WithDeviceNotice(string message) =>
        string.IsNullOrWhiteSpace(this.deviceNotice) ? message : $"{message}\n{this.deviceNotice}";

    private void DirectMLDevices_ItemCheck(object? sender, ItemCheckEventArgs e)
    {
        if (!this.initializing && this.IsHandleCreated)
            this.BeginInvoke((Action)this.OptionsChanged);
    }

    private void GpuCacheTimer_Tick(object? sender, EventArgs e) => this.UpdateProviderControls();

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        this.gpuCacheTimer.Start();
        this.SetBusy(true, "Looking for Demucs models…", loading: true);
        await Task.Yield();
        try
        {
            string? warning = null;
            try { this.settings = DemucsDialogSettings.Load(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
            { warning = $"Settings could not be loaded: {error.Message}"; }
            Task<DirectMLDevices.Adapter[]> adaptersTask = Task.Run(DirectMLDevices.GetAdapters);
            Task<DemucsCudaDeviceCatalog> cudaTask = Task.Run(DemucsFourier.EnumerateCudaDevices);
            await Task.WhenAll(adaptersTask, cudaTask);
            var devices = adaptersTask.Result;
            this.LoadDirectMLDevices(devices, cudaTask.Result);
            this.ApplySettings();
            this.Onnx = await Task.Run(() => new OnnxService(this.settings.ModelDirectories, loadImmediately: false));
            this.RefreshModels();
            if (this.Onnx.ModelPaths.Count == 0) await this.BrowseModelsAsync();
            bool reused = this.comboBox_models.SelectedItem is ModelItem selected &&
                await this.TryReuseModelsAsync(selected.Path, this.GetDesiredOptions());
            if (!reused) this.status.Text = this.WithDeviceNotice(warning ?? "Select a model and load options, then click Load model. No model is loaded yet.");
        }
        catch (Exception error) { this.ShowError("Unable to prepare Demucs", error); }
        finally { this.initializing = false; this.SetBusy(false); }
    }

    private void ApplySettings()
    {
        var o = this.settings.Options;
        this.keepModelLoaded.Value = Math.Clamp(this.settings.KeepModelLoadedSeconds, 0, 86400);
        o.NormalizeWorkers();
        bool directMLAvailable = this.checkedListBox_directMLDevices.Items.Count > 0;
        if (!directMLAvailable) o.UseDirectML = false;
        o.NormalizeForProvider(o.UseDirectML);
        this.provider.SelectedIndex = o.UseDirectML && directMLAvailable ? 0 : 1;
        if (this.cudaToolkits.Length > 0)
        {
            var toolkit = this.cudaToolkits.FirstOrDefault(item => item.MajorVersion == o.CudaToolkitVersion)
                ?? this.cudaToolkits[0];
            this.comboBox_cudaToolkitVersion.SelectedItem = this.comboBox_cudaToolkitVersion.Items
                .Cast<CudaToolkitChoice>().FirstOrDefault(item => item.MajorVersion == toolkit.MajorVersion);
        }
        this.useCudaCuFft.Checked = this.CanUseCudaCuFft && o.UseCudaCuFft;
        this.UpdateCudaCuFftLabel();
        this.optimization.SelectedItem = Enum.IsDefined(o.GraphOptimization) ? o.GraphOptimization.ToString() : GraphOptimizationLevel.ORT_ENABLE_BASIC.ToString();
        this.execution.SelectedItem = Enum.IsDefined(o.ExecutionMode) ? o.ExecutionMode.ToString() : ExecutionMode.ORT_SEQUENTIAL.ToString();
        this.intraThreads.Value = o.IntraOpThreads;
        this.interThreads.Value = o.InterOpThreads;
        this.workers.Value = o.FourierWorkers;
        this.overlap.Value = (decimal)(o.ChunkOverlap * 100);
        this.cpuFallback.Checked = o.AllowCpuFallback;
        this.cpuOperators.Checked = o.AllowCpuOperators;
        this.validate.Checked = o.ValidateDirectML;
        this.disableDmlFusion.Checked = o.DisableDirectMLGraphFusion;
        this.memoryPattern.Checked = o.EnableMemoryPattern;
        this.cpuArena.Checked = o.EnableCpuMemoryArena;
        this.spinning.Checked = o.DisableSpinning;
        this.prepacking.Checked = o.DisablePrepacking;
        this.profiling.Checked = o.EnableProfiling;
        this.logging.Checked = o.LogSession;
        this.profilePath.Text = o.ProfileOutputPathPrefix;
        this.optimizedPath.Text = o.OptimizedModelPath;
        this.advanced.Text = string.Join(Environment.NewLine, (o.SessionConfigEntries ?? []).Select(p => $"{p.Key}={p.Value}"));

        int[] selectedIds = this.settings.SelectedDirectMLDeviceIds.Length == 0
            ? [o.DeviceId]
            : this.settings.SelectedDirectMLDeviceIds;
        if (!selectedIds.Any(id => this.FindDirectMLDevice(id) is not null)
            && this.checkedListBox_directMLDevices.Items.Count > 0)
            selectedIds = [((DirectMLDeviceChoice)this.checkedListBox_directMLDevices.Items[0]).Adapter.Id];
        this.settings.SelectedDirectMLDeviceIds = selectedIds
            .Where(id => this.FindDirectMLDevice(id) is not null).Distinct().ToArray();
        for (int index = 0; index < this.checkedListBox_directMLDevices.Items.Count; index++)
        {
            var choice = (DirectMLDeviceChoice)this.checkedListBox_directMLDevices.Items[index];
            this.checkedListBox_directMLDevices.SetItemChecked(index, selectedIds.Contains(choice.Adapter.Id));
        }
    }

    private DemucsLoadOptions ReadOptions()
    {
        var entries = new Dictionary<string, string>();
        foreach (string line in this.advanced.Lines.Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            int equals = line.IndexOf('=');
            if (equals <= 0) throw new ArgumentException("Extra session entries must use key=value, one entry per line.");
            entries.Add(line[..equals].Trim(), line[(equals + 1)..].Trim());
        }
        bool useDirectML = this.provider.SelectedIndex == 0;
        bool useCudaCuFft = this.CanUseCudaCuFft ? this.useCudaCuFft.Checked : this.settings.Options.UseCudaCuFft;
        DirectMLDeviceChoice? selectedDevice = this.checkedListBox_directMLDevices.CheckedItems
            .Cast<DirectMLDeviceChoice>().FirstOrDefault()
            ?? this.FindDirectMLDevice(this.settings.Options.DeviceId);
        int cudaDeviceId;
        if (!useCudaCuFft) cudaDeviceId = -1;
        else if (useDirectML) cudaDeviceId = selectedDevice?.Cuda?.Id ?? -1;
        else cudaDeviceId = this.cudaDevices.FirstOrDefault(device => device.Id == this.settings.Options.CudaDeviceId)?.Id
            ?? this.cudaDevices.FirstOrDefault()?.Id ?? -1;
        long? deviceLuid = useDirectML ? selectedDevice?.Adapter.Luid
            : useCudaCuFft ? this.cudaDevices.FirstOrDefault(device => device.Id == cudaDeviceId)?.Luid : null;
        return new DemucsLoadOptions
        {
            UseDirectML = useDirectML,
            DeviceId = selectedDevice?.Adapter.Id ?? this.settings.Options.DeviceId,
            GraphOptimization = Enum.Parse<GraphOptimizationLevel>(this.optimization.Text),
            ExecutionMode = Enum.Parse<ExecutionMode>(this.execution.Text),
            IntraOpThreads = (int)this.intraThreads.Value, InterOpThreads = (int)this.interThreads.Value,
            FourierWorkers = (int)this.workers.Value,
            ChunkOverlap = (double)this.overlap.Value / 100,
            AllowCpuFallback = this.cpuFallback.Checked, AllowCpuOperators = this.cpuOperators.Checked,
            ValidateDirectML = this.validate.Checked, EnableMemoryPattern = this.memoryPattern.Checked,
            DisableDirectMLGraphFusion = this.disableDmlFusion.Checked,
            EnableCpuMemoryArena = this.cpuArena.Checked, DisableSpinning = this.spinning.Checked,
            DisablePrepacking = this.prepacking.Checked, EnableProfiling = this.profiling.Checked,
            LogSession = this.logging.Checked,
            CudaDeviceId = cudaDeviceId,
            UseCudaCuFft = useCudaCuFft,
            CudaToolkitVersion = this.SelectedCudaToolkitVersion,
            DeviceLuid = deviceLuid,
            ProfileOutputPathPrefix = this.profilePath.Text.Trim(),
            OptimizedModelPath = this.optimizedPath.Text.Trim(), SessionConfigEntries = entries
        };
    }

    private void OptionsChanged()
    {
        if (this.initializing || this.busy) return;
        if (this.Onnx?.IsOnline == true && this.comboBox_models.SelectedItem is ModelItem selected)
        {
            try
            {
                string[] desiredKeys = this.GetDesiredOptions().Select(options => DemucsModelCache.Key(selected.Path, options))
                    .Order(StringComparer.Ordinal).ToArray();
                if (this.loadedCacheKeys.SequenceEqual(desiredKeys, StringComparer.Ordinal))
                {
                    this.loadedWithCurrentOptions = true;
                    this.button_run.Enabled = this.Onnx.IsOnline;
                    this.status.Text = this.WithDeviceNotice("Ready — reusing the loaded model.");
                    this.UpdateProviderControls();
                    this.UpdateLoadButton();
                    return;
                }
            }
            catch (ArgumentException) { }
        }
        this.loadedWithCurrentOptions = false;
        this.button_run.Enabled = false;
        this.status.Text = "Load options changed. Click Load model to apply the selected settings.";
        this.UpdateProviderControls();
        this.UpdateLoadButton();
    }

    private async Task BrowseModelsAsync()
    {
        if (this.Onnx == null) return;
        using var browser = new FolderBrowserDialog
        {
            Description = this.Onnx.ModelPaths.Count == 0
                ? "No Demucs ONNX models were found. Select the Demucs models directory."
                : "Select a directory containing Demucs ONNX models.",
            UseDescriptionForTitle = true, ShowNewFolderButton = false
        };
        browser.SelectedPath = this.ModelsBrowseDirectory();
        if (browser.ShowDialog(this) != DialogResult.OK) return;
        bool wasBusy = this.busy;
        this.SetBusy(true, "Scanning models directory…", loading: true);
        try
        {
            await Task.Run(() => this.Onnx.GetModelPaths([browser.SelectedPath]));
            this.settings.ModelDirectories = this.Onnx.ModelDirectories.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            this.RefreshModels();
            this.settings.Save();
            if (this.Onnx.ModelPaths.Count == 0)
                this.status.Text = "No ONNX models were found in that directory. Click Browse models directory to try another directory.";
        }
        catch (Exception error) { this.ShowError("Unable to scan models directory", error); }
        finally { this.SetBusy(wasBusy); }
    }

    private sealed record ModelItem(string Path)
    {
        public string Name => System.IO.Path.GetFileName(Path);
    }
    private void RefreshModels()
    {
        this.comboBox_models.Items.Clear();
        this.comboBox_models.Items.AddRange(this.Onnx!.ModelPaths.Select(p => new ModelItem(p)).ToArray());
        this.comboBox_models.SelectedItem = this.comboBox_models.Items.Cast<ModelItem>()
            .FirstOrDefault(m => string.Equals(m.Path, this.settings.SelectedModel, StringComparison.OrdinalIgnoreCase));
        if (this.comboBox_models.SelectedIndex < 0 && this.comboBox_models.Items.Count > 0) this.comboBox_models.SelectedIndex = 0;
    }

    private async void Load_Click(object? sender, EventArgs e)
    {
        if (this.busy) return;
        if (this.HasLoadedModels)
        {
            this.SetBusy(true, "Unloading and disposing Demucs sessions…", loading: true);
            try
            {
                this.settings.SelectedModel = (this.comboBox_models.SelectedItem as ModelItem)?.Path ?? "";
                this.ReleaseModel(TimeSpan.Zero);
                this.Onnx = await Task.Run(() => new OnnxService(this.settings.ModelDirectories, loadImmediately: false));
                this.RefreshModels();
                this.loadedWithCurrentOptions = false;
                this.button_run.Enabled = false;
                this.status.Text = "Demucs sessions unloaded; idle sessions were disposed.";
                this.settings.Save();
            }
            catch (Exception error) { this.ShowError("Unable to unload Demucs", error); }
            finally { this.SetBusy(false); }
            return;
        }
        if (this.Onnx == null || this.comboBox_models.SelectedItem is not ModelItem model) return;
        this.SetBusy(true, $"Loading {model.Name}…", loading: true);
        this.loadedWithCurrentOptions = false;
        try
        {
            var desiredOptions = this.GetDesiredOptions();
            this.settings.Options = this.ReadOptions();
            this.settings.KeepModelLoadedSeconds = (int)this.keepModelLoaded.Value;
            this.settings.SelectedModel = model.Path;
            this.settings.SelectedDirectMLDeviceIds = this.GetSelectedDirectMLDeviceIds();
            this.settings.Save();
            if (await this.TryReuseModelsAsync(model.Path, desiredOptions)) return;
            var acquired = await this.LoadModelLeasesAsync(model.Path, desiredOptions);
            this.modelLeases.AddRange(acquired);
            this.Onnx = acquired[0].Model;
            this.loadedCacheKeys = desiredOptions.Select(options => DemucsModelCache.Key(model.Path, options))
                .Order(StringComparer.Ordinal).ToArray();
            this.settings.Save();
            this.loadedWithCurrentOptions = true;
            this.checkedListBox_stems.Items.Clear();
            foreach (string stem in this.Onnx.AvailableStems)
                this.checkedListBox_stems.Items.Add(stem, this.settings.SelectedStems.Contains(stem));
            string ready = acquired.Count > 1
                ? $"Ready — {acquired.Count} model sessions loaded; selected stems will be split across devices in parallel."
                : "Ready — model session loaded.";
            this.status.Text = this.WithDeviceNotice($"{ready}\n{DescribeLoadedModels(acquired)}");
        }
        catch (Exception error) { this.ShowError("Unable to load Demucs", error); }
        finally { this.SetBusy(false); }
    }

    private void SetBusy(bool value, string? message = null, bool loading = false)
    {
        this.busy = value;
        this.UpdateLoadButton();
        this.browse.Enabled = !value;
        this.button_run.Enabled = !value && this.loadedWithCurrentOptions && this.Onnx?.IsOnline == true;
        this.cancel.Enabled = value && !loading;
        this.comboBox_models.Enabled = !value;
        this.optionsPanel.Enabled = !value;
        this.checkedListBox_stems.Enabled = !value;
        this.UpdateProviderControls();
        this.progressBar_inferencing.Style = value && loading ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
        this.progressBar_inferencing.MarqueeAnimationSpeed = value && loading ? 30 : 0;
        if (message != null) this.status.Text = message;
        if (!value && this.closeRequested) this.BeginInvoke(this.Close);
    }

    private void UpdateProviderControls()
    {
        bool dml = this.provider.SelectedIndex == 0;
        this.optimization.Enabled = !this.busy;
        this.execution.Enabled = !dml && !this.busy;
        this.memoryPattern.Enabled = !dml && !this.busy;
        this.cpuFallback.Enabled = dml && !this.busy;
        this.cpuOperators.Enabled = dml && !this.busy;
        this.validate.Enabled = dml && !this.busy;
        this.disableDmlFusion.Enabled = dml && !this.busy;
        this.checkedListBox_directMLDevices.Enabled = dml && !this.busy && !DemucsModelCache.HasDirectMLSessions;
        this.keepModelLoaded.Enabled = !this.busy;
        this.useCudaCuFft.Enabled = this.CanUseCudaCuFft && !this.busy;
        this.comboBox_cudaToolkitVersion.Enabled = this.CanUseCudaCuFft && this.cudaToolkits.Length > 1 && !this.busy;
        this.UpdateCudaCuFftLabel();
    }

    private bool HasLoadedModels => this.modelLeases.Count > 0 || this.Onnx?.IsOnline == true;

    private void UpdateLoadButton()
    {
        bool loaded = this.HasLoadedModels;
        this.load.Text = loaded ? "Unload model" : "Load model";
        this.load.Enabled = !this.busy && (loaded || (this.Onnx is not null && this.comboBox_models.Items.Count > 0));
    }

    private static string DescribeLoadedModels(IEnumerable<SharedModelPool<OnnxService>.Lease> leases)
    {
        var models = leases.Select(lease => lease.Model).ToArray();
        string[] providers = models.Select(model => model.ExecutionProvider).Distinct(StringComparer.Ordinal).ToArray();
        string[] backends = models.Select(model => model.FourierBackend).Distinct(StringComparer.Ordinal).ToArray();
        string[] warnings = models.Select(model => model.LoadWarning)
            .Where(message => !string.IsNullOrWhiteSpace(message)).Cast<string>().Distinct(StringComparer.Ordinal).ToArray();
        return string.Join(Environment.NewLine,
            new[] { $"ONNX: {string.Join(", ", providers)}", $"FFT: {string.Join(", ", backends)}" }.Concat(warnings));
    }

    private async Task<List<SharedModelPool<OnnxService>.Lease>> LoadModelLeasesAsync(
        string modelPath, DemucsLoadOptions[] options)
    {
        string heading = options.Length == 1
            ? $"Loading {Path.GetFileName(modelPath)}…"
            : $"Loading {Path.GetFileName(modelPath)} on {options.Length} devices in parallel…";
        string[] devices = options.Select(option => option.UseDirectML
            ? this.FindDirectMLDevice(option.DeviceId)?.ToString() ?? $"DirectML {option.DeviceId}"
            : "CPU").ToArray();
        string[] states = options.Select(_ => "Waiting for session initialization…").ToArray();
        bool reportProgress = true;
        void UpdateLoadStatus() => this.status.Text = string.Join(Environment.NewLine,
            new[] { heading }.Concat(devices.Select((device, index) => $"{device}: {states[index]}")));
        IProgress<(int Index, string State)> progress = new Progress<(int Index, string State)>(update =>
        {
            if (!reportProgress || this.IsDisposed) return;
            states[update.Index] = update.State;
            UpdateLoadStatus();
        });
        UpdateLoadStatus();
        async Task<SharedModelPool<OnnxService>.Lease> LoadOneAsync(int index, DemucsLoadOptions option)
        {
            progress.Report((index, "Loading ONNX session…"));
            try
            {
                var lease = await DemucsModelCache.AcquireAsync(modelPath, option, this.settings.ModelDirectories, allowLoad: true)
                    ?? throw new InvalidOperationException("The selected Demucs model could not be loaded.");
                progress.Report((index, $"Ready — {lease.Model.ExecutionProvider}; {lease.Model.FourierBackend}"));
                return lease;
            }
            catch (Exception error)
            {
                progress.Report((index, $"Failed — {error.Message}"));
                throw;
            }
        }
        Task<SharedModelPool<OnnxService>.Lease>[] tasks = options.Select((option, index) => LoadOneAsync(index, option)).ToArray();
        try
        {
            var leases = await Task.WhenAll(tasks);
            reportProgress = false;
            return leases.ToList();
        }
        catch
        {
            reportProgress = false;
            ReleaseLeases(tasks.Where(task => task.Status == TaskStatus.RanToCompletion)
                .Select(task => task.Result), TimeSpan.Zero);
            throw;
        }
    }

    private async void button_Run_Click(object sender, EventArgs e)
    {
        if (this.busy || this.Onnx?.IsOnline != true) return;
        string[] selected = this.checkedListBox_stems.CheckedItems.Cast<string>().ToArray();
        if (selected.Length == 0)
        {
            MessageBox.Show(this, "Please select at least one stem to separate.", "No stems selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        this.progressBar_inferencing.Value = 0;
        this.cancellation.Dispose();
        this.cancellation = new();
        this.SetBusy(true, $"Preparing audio for separation…\n{DescribeLoadedModels(this.modelLeases)}");
        int completed = 0;
        var leases = this.modelLeases.ToArray();
        if (leases.Length == 0)
        {
            this.SetBusy(false);
            this.ShowError("Demucs model unavailable", new InvalidOperationException("Load a Demucs model first."));
            return;
        }
        try
        {
            this.settings.SelectedStems = selected;
            this.settings.Save();
            AudioCollectionView? collection = null;
            var progressValues = new double[selected.Length];
            var stemProgress = selected.Select((stem, index) => (IProgress<double>)new Progress<double>(fraction =>
            {
                if (this.IsDisposed || this.Disposing || !this.busy) return;
                progressValues[index] = fraction;
                this.progressBar_inferencing.Value = Math.Clamp(
                    (int)(progressValues.Sum() * 1000 / selected.Length), 0, 1000);
                this.status.Text = $"Separating {stem} ({index + 1}/{selected.Length}) — {fraction:P0}\n" +
                    DescribeLoadedModels(leases);
            })).ToArray();
            var channel = Channel.CreateBounded<(int Index, string Name, float[] Samples)>(
                new BoundedChannelOptions(Math.Max(1, leases.Length))
                {
                    SingleReader = true,
                    SingleWriter = leases.Length == 1,
                    FullMode = BoundedChannelFullMode.Wait
                });
            int[][] assignedStems = AssignStemsToDevices(selected.Length, leases.Length);
            var workers = leases.Select((lease, deviceIndex) => Task.Run(async () =>
            {
                await lease.Operation.WaitAsync(this.cancellation.Token);
                try
                {
                    foreach (int index in assignedStems[deviceIndex])
                    {
                        this.cancellation.Token.ThrowIfCancellationRequested();
                        var stems = await lease.Model.ExtractStemsAsync(this.OriginalAudio,
                            [selected[index]], stemProgress[index], this.cancellation.Token);
                        float[] samples = stems[selected[index]];
                        await channel.Writer.WriteAsync((index, selected[index], samples), this.cancellation.Token);
                    }
                }
                finally { lease.Operation.Release(); }
            }, this.cancellation.Token)).ToArray();
            Task allWorkers = Task.WhenAll(workers);
            _ = allWorkers.ContinueWith(task => channel.Writer.TryComplete(task.Exception?.GetBaseException()),
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            Exception? failure = null;
            try
            {
                await foreach (var result in channel.Reader.ReadAllAsync(this.cancellation.Token))
                {
                    this.cancellation.Token.ThrowIfCancellationRequested();
                    var extract = await this.OriginalAudio.CloneAsync();
                    extract.Data = result.Samples;
                    extract.Rename($"{this.OriginalAudio.OriginalName}_{result.Name}");
                    if (collection == null || collection.IsDisposed)
                        collection = new AudioCollectionView([], $"{this.OriginalAudio.Name} - Stems Demucs");
                    collection.AudioC.Audios.Add(extract);
                    collection.Show();
                    completed++;
                    progressValues[result.Index] = 1;
                    this.progressBar_inferencing.Value = Math.Clamp(
                        (int)(progressValues.Sum() * 1000 / selected.Length), 0, 1000);
                    this.status.Text = $"{result.Name} ready — {completed}/{selected.Length} stem(s) extracted.\n" +
                        DescribeLoadedModels(leases);
                }
            }
            catch (Exception error)
            {
                failure = error;
                this.cancellation.Cancel();
            }
            try { await allWorkers; }
            catch (Exception error) { failure ??= error; }
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
            this.status.Text = $"Finished — {selected.Length} stem(s) extracted.\n{DescribeLoadedModels(leases)}";
        }
        catch (OperationCanceledException) { this.status.Text = $"Separation cancelled — {completed} completed stem(s) kept."; }
        catch (Exception error) { this.ShowError("Demucs separation failed", error); }
        finally { this.SetBusy(false); }
    }

    private void comboBox_models_SelectedIndexChanged(object sender, EventArgs e) => this.OptionsChanged();
    private void ShowError(string title, Exception error)
    {
        this.status.Text = error.Message;
        MessageBox.Show(this, error.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (this.busy)
        {
            this.closeRequested = true;
            this.cancellation.Cancel();
            this.status.Text = "Waiting for the current model operation to finish…";
            e.Cancel = true;
        }
        else if (!this.initializing)
        {
            try
            {
                this.settings.Options = this.ReadOptions();
                this.settings.KeepModelLoadedSeconds = (int)this.keepModelLoaded.Value;
                this.settings.SelectedModel = (this.comboBox_models.SelectedItem as ModelItem)?.Path ?? "";
                this.settings.SelectedDirectMLDeviceIds = this.GetSelectedDirectMLDeviceIds();
                if (this.checkedListBox_stems.Items.Count > 0)
                    this.settings.SelectedStems = this.checkedListBox_stems.CheckedItems.Cast<string>().ToArray();
                this.settings.Save();
            }
            catch (Exception error) { this.ShowError("Unable to save Demucs settings", error); }
        }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        this.gpuCacheTimer.Stop();
        this.ReleaseModel();
        this.cancellation.Dispose();
        this.OriginalAudio.Dispose();
        base.OnFormClosed(e);
    }

    private async Task<bool> TryReuseModelsAsync(string path, DemucsLoadOptions[] options)
    {
        string[] keys = options.Select(option => DemucsModelCache.Key(path, option)).Order(StringComparer.Ordinal).ToArray();
        if (this.Onnx?.IsOnline == true && this.loadedCacheKeys.SequenceEqual(keys, StringComparer.Ordinal))
        {
            this.loadedWithCurrentOptions = true;
            this.status.Text = this.WithDeviceNotice($"Ready — model already loaded.\n{DescribeLoadedModels(this.modelLeases)}");
            return true;
        }
        var acquired = new List<SharedModelPool<OnnxService>.Lease>();
        foreach (var option in options)
        {
            var lease = await DemucsModelCache.AcquireAsync(path, option, this.settings.ModelDirectories, allowLoad: false);
            if (lease is null)
            {
                ReleaseLeases(acquired, TimeSpan.FromSeconds((double)this.keepModelLoaded.Value));
                return false;
            }
            acquired.Add(lease);
        }
        this.ReleaseModel();
        this.modelLeases.AddRange(acquired);
        OnnxService cached = acquired[0].Model;
        this.Onnx = cached;
        this.loadedCacheKeys = keys;
        this.loadedWithCurrentOptions = true;
        this.checkedListBox_stems.Items.Clear();
        foreach (string stem in cached.AvailableStems)
            this.checkedListBox_stems.Items.Add(stem, this.settings.SelectedStems.Contains(stem));
        this.status.Text = this.WithDeviceNotice($"Ready — reused {acquired.Count} loaded model session(s).\n{DescribeLoadedModels(acquired)}");
        return true;
    }

    private void ReleaseModel(TimeSpan? keepAlive = null)
    {
        var service = this.Onnx;
        this.Onnx = null;
        this.loadedCacheKeys = [];
        if (this.modelLeases.Count > 0)
        {
            ReleaseLeases(this.modelLeases, keepAlive ?? TimeSpan.FromSeconds((double)this.keepModelLoaded.Value));
        }
        else service?.Dispose();
    }

    internal static void ReleaseLeases(IEnumerable<SharedModelPool<OnnxService>.Lease> leases, TimeSpan keepAlive)
    {
        foreach (var lease in leases)
        {
            lease.KeepAlive = keepAlive;
            lease.Dispose();
        }
        if (leases is List<SharedModelPool<OnnxService>.Lease> list) list.Clear();
    }

    internal static int[][] AssignStemsToDevices(int stemCount, int deviceCount)
    {
        if (stemCount < 0) throw new ArgumentOutOfRangeException(nameof(stemCount));
        if (deviceCount <= 0) throw new ArgumentOutOfRangeException(nameof(deviceCount));
        var assignments = Enumerable.Range(0, deviceCount).Select(_ => new List<int>()).ToArray();
        for (int stem = 0; stem < stemCount; stem++) assignments[stem % deviceCount].Add(stem);
        return assignments.Select(deviceStems => deviceStems.ToArray()).ToArray();
    }

    private string ModelsBrowseDirectory()
    {
        if (this.comboBox_models.SelectedItem is ModelItem selected)
        {
            string? parent = Path.GetDirectoryName(Path.GetFullPath(selected.Path));
            if (parent != null && Directory.Exists(parent)) return parent;
        }
        return this.settings.ModelDirectories.FirstOrDefault(Directory.Exists)
            ?? this.Onnx?.ModelDirectories.FirstOrDefault(Directory.Exists)
            ?? this.settings.ModelDirectories.FirstOrDefault()
            ?? "D:/Models/Demucs";
    }
}
