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
    private readonly ComboBox device = DropDown([]);
    private readonly ComboBox optimization = DropDown(Enum.GetNames<GraphOptimizationLevel>());
    private readonly ComboBox execution = DropDown(Enum.GetNames<ExecutionMode>());
    private readonly NumericUpDown intraThreads = Number(0, Environment.ProcessorCount);
    private readonly NumericUpDown interThreads = Number(0, Environment.ProcessorCount);
    private readonly NumericUpDown workers = Number(1, Environment.ProcessorCount);
    private readonly NumericUpDown overlap = new() { Minimum = 0, Maximum = 75, Value = 25, Increment = 5, Width = 120 };
    private readonly NumericUpDown keepModelLoaded = new() { Name = "keepModelLoaded", Minimum = 0, Maximum = 86400, Value = 300, Increment = 30, Width = 120,
        AccessibleName = "Keep Model Loaded Seconds" };
    private string? loadedCacheKey;
    private readonly List<SharedModelPool<OnnxService>.Lease> modelLeases = [];
    private string[] loadedCacheKeys = [];
    private readonly CheckBox cpuFallback = Check("Fall back to CPU if DirectML fails");
    private readonly CheckBox cpuOperators = Check("Allow CPU operators in DirectML graphs");
    private readonly CheckBox validate = Check("Verify DirectML results against CPU when loading");
    private readonly CheckBox disableDmlFusion = Check("Disable DirectML graph fusion (Demucs compatibility)");
    private readonly CheckBox memoryPattern = Check("Enable memory pattern (CPU only)");
    private readonly CheckBox cpuArena = Check("Enable CPU memory arena");
    private readonly CheckBox spinning = Check("Disable worker spinning");
    private readonly CheckBox prepacking = Check("Disable weight prepacking");
    private readonly CheckBox profiling = Check("Enable ONNX profiling");
    private readonly CheckBox logging = Check("Verbose ONNX session logging");
    private readonly CheckBox cuda = Check("Prefer CUDA 12 CuFFT; fall back to parallel CPU FFT");
    private readonly TextBox cudaDirectory = new() { Dock = DockStyle.Fill };
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

    private sealed record GpuDeviceChoice(DemucsCudaDevice Cuda, DirectMLDevices.Adapter? DirectML)
    {
        public override string ToString() => $"cuda:{this.Cuda.Id} — {this.Cuda.Name}" +
            (this.DirectML is null ? " (no DirectML match)" : $" -> DirectML {this.DirectML.Id}");
    }

    public OnnxDemucsDialog(AudioObj audioObj)
    {
        this.InitializeComponent();
        this.OriginalAudio = audioObj;
        this.Text = "Demucs Source Separation";
        this.MaximumSize = Size.Empty;
        this.MinimumSize = new Size(1020, 770);
        this.ClientSize = new Size(1040, 760);
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
        retentionRow.Controls.Add(new Label { Text = "0 = unload on close; default 300 s", AutoSize = true, Margin = new Padding(8, 7, 3, 3) });
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
        this.AddOption("DirectML device", this.device);
        this.AddOption("Graph optimization", this.optimization);
        this.AddOption("Execution mode (CPU only)", this.execution);
        this.AddOption("Intra-op threads (0 = auto)", this.intraThreads);
        this.AddOption("Inter-op threads (0 = auto)", this.interThreads);
        this.AddOption("Profiling output prefix", this.profilePath);
        this.AddOption("Save optimized model (optional)", this.optimizedPath);
        this.AddOption("CPU FFT workers", this.workers);
        this.AddOption("Parallel GPU devices (cuda:<id>)", this.checkedListBox_cudaDevices);
        this.AddOption("CUDA 12 directory (optional)", this.cudaDirectory);
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
        foreach (var check in new[] { this.cpuFallback, this.cpuOperators, this.validate, this.disableDmlFusion, this.memoryPattern, this.cpuArena, this.spinning, this.prepacking, this.profiling, this.logging, this.cuda })
        {
            check.AutoSize = false;
            check.Dock = DockStyle.Fill;
            check.Margin = new Padding(3, 1, 3, 1);
            check.AutoEllipsis = true;
            this.checksPanel.Controls.Add(check, checkIndex % 2, checkIndex / 2);
            checkIndex++;
        }
        this.checksPanel.RowCount = (checkIndex + 1) / 2;
        for (int row = 0; row < this.checksPanel.RowCount; row++) this.checksPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / this.checksPanel.RowCount));
        checkGroup.Controls.Add(this.checksPanel);
        this.optionsPanel.Controls.Add(checkGroup, 0, checksRow);
        this.optionsPanel.SetColumnSpan(checkGroup, 4);
        this.optionCount = (checksRow + 1) * 2;
        this.AddOption("Extra ONNX session entries\n(one key=value per line)", this.advanced);
        var qualityInfo = new Label { AutoSize = false, Dock = DockStyle.Fill, AutoEllipsis = true,
            Text = "FFT 4096 / hop 1024 (fixed).\nOverlap: 0% fast; 25% balanced; 50–75% smoother boundaries, more work." };
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
            if (ReferenceEquals(control, this.keepModelLoaded)) continue;
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
            TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Margin = new Padding(3, 2, 3, 2) }, column, row);
        control.MinimumSize = Size.Empty;
        if (control is ComboBox or TextBox) control.Width = 100;
        control.Margin = new Padding(3, 4, 3, 4);
        this.optionsPanel.Controls.Add(control, column + 1, row);
    }

    private void LoadCudaDevices(DemucsCudaDeviceCatalog catalog, DirectMLDevices.Adapter[] adapters)
    {
        this.checkedListBox_cudaDevices.Items.Clear();
        var choices = catalog.Devices.Select(cuda =>
            new GpuDeviceChoice(cuda, DirectMLDevices.MatchCudaDevice(cuda, adapters))).ToArray();
        foreach (var choice in choices) this.checkedListBox_cudaDevices.Items.Add(choice, false);
        var notices = new List<string>();
        if (!string.IsNullOrWhiteSpace(catalog.Warning)) notices.Add(catalog.Warning);
        notices.AddRange(choices.Where(choice => choice.DirectML is null)
            .Select(choice => $"CUDA device cuda:{choice.Cuda.Id} has no matching DirectML adapter and cannot be selected."));
        if (choices.Length == 0) notices.Add("No CUDA devices were detected; GPU parallel selection is unavailable.");
        this.deviceNotice = string.Join(" ", notices.Distinct(StringComparer.Ordinal));
    }

    private GpuDeviceChoice? FindCudaDevice(DirectMLDevices.Adapter? adapter) =>
        adapter is null ? null : this.checkedListBox_cudaDevices.Items.Cast<GpuDeviceChoice>()
            .FirstOrDefault(choice => choice.DirectML?.Id == adapter.Id);

    private DemucsLoadOptions[] GetDesiredOptions()
    {
        var options = this.ReadOptions();
        var selected = this.checkedListBox_cudaDevices.CheckedItems.Cast<GpuDeviceChoice>().ToArray();
        if (selected.Length == 0) return [options];
        return selected.Select(choice =>
        {
            var copy = JsonSerializer.Deserialize<DemucsLoadOptions>(JsonSerializer.Serialize(options))!;
            copy.UseDirectML = true;
            copy.DeviceId = choice.DirectML!.Id;
            copy.CudaDeviceId = choice.Cuda.Id;
            return copy;
        }).ToArray();
    }

    private string WithDeviceNotice(string message) =>
        string.IsNullOrWhiteSpace(this.deviceNotice) ? message : $"{message}\n{this.deviceNotice}";

    private void CudaDevices_ItemCheck(object? sender, ItemCheckEventArgs e)
    {
        if (e.NewValue == CheckState.Checked &&
            this.checkedListBox_cudaDevices.Items[e.Index] is GpuDeviceChoice { DirectML: null })
        {
            e.NewValue = CheckState.Unchecked;
            this.status.Text = this.WithDeviceNotice("This CUDA device cannot be mapped to a DirectML adapter.");
            return;
        }
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
            this.device.Items.AddRange(devices.Cast<object>().ToArray());
            if (this.device.Items.Count == 0) this.device.Items.Add(new DirectMLDevices.Adapter(0, "Default adapter"));
            this.LoadCudaDevices(cudaTask.Result, devices);
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
        this.provider.SelectedIndex = o.UseDirectML ? 0 : 1;
        this.device.SelectedItem = this.device.Items.Cast<DirectMLDevices.Adapter>().FirstOrDefault(d => d.Id == o.DeviceId) ?? this.device.Items[0];
        this.optimization.SelectedItem = Enum.IsDefined(o.GraphOptimization) ? o.GraphOptimization.ToString() : GraphOptimizationLevel.ORT_ENABLE_ALL.ToString();
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
        this.cuda.Checked = o.PreferCudaFourier;
        this.cudaDirectory.Text = o.CudaToolkitDirectory;
        this.profilePath.Text = o.ProfileOutputPathPrefix;
        this.optimizedPath.Text = o.OptimizedModelPath;
        this.advanced.Text = string.Join(Environment.NewLine, (o.SessionConfigEntries ?? []).Select(p => $"{p.Key}={p.Value}"));
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
        return new DemucsLoadOptions
        {
            UseDirectML = this.provider.SelectedIndex == 0,
            DeviceId = (this.device.SelectedItem as DirectMLDevices.Adapter)?.Id ?? 0,
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
            PreferCudaFourier = this.cuda.Checked && (this.provider.SelectedIndex != 0 ||
                this.FindCudaDevice(this.device.SelectedItem as DirectMLDevices.Adapter) is not null),
            CudaDeviceId = this.FindCudaDevice(this.device.SelectedItem as DirectMLDevices.Adapter)?.Cuda.Id
                ?? Math.Clamp(this.settings.Options.CudaDeviceId, 0, 127),
            CudaToolkitDirectory = this.cudaDirectory.Text.Trim(), ProfileOutputPathPrefix = this.profilePath.Text.Trim(),
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
                    this.loadedWithCurrentOptions = true; this.button_run.Enabled = true; this.load.Enabled = false;
                    this.status.Text = this.WithDeviceNotice("Ready — reusing the loaded model."); this.UpdateProviderControls(); return;
                }
            }
            catch (ArgumentException) { }
        }
        this.loadedWithCurrentOptions = false;
        this.button_run.Enabled = false;
        this.load.Enabled = this.comboBox_models.Items.Count > 0;
        this.status.Text = "Load options changed. Click Load model to apply the selected settings.";
        this.UpdateProviderControls();
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
        if (this.busy || this.Onnx == null || this.comboBox_models.SelectedItem is not ModelItem model) return;
        this.SetBusy(true, $"Loading {model.Name}…", loading: true);
        this.loadedWithCurrentOptions = false;
        try
        {
            var desiredOptions = this.GetDesiredOptions();
            this.settings.Options = this.ReadOptions();
            this.settings.KeepModelLoadedSeconds = (int)this.keepModelLoaded.Value;
            this.settings.SelectedModel = model.Path;
            this.settings.Save();
            if (await this.TryReuseModelsAsync(model.Path, desiredOptions)) return;
            this.ReleaseModel();
            var acquired = new List<SharedModelPool<OnnxService>.Lease>();
            try
            {
                for (int index = 0; index < desiredOptions.Length; index++)
                {
                    this.status.Text = desiredOptions.Length == 1
                        ? $"Loading {model.Name}…"
                        : $"Loading {model.Name} on GPU {index + 1}/{desiredOptions.Length}…";
                    var lease = await DemucsModelCache.AcquireAsync(model.Path, desiredOptions[index],
                        this.settings.ModelDirectories, allowLoad: true);
                    acquired.Add(lease ?? throw new InvalidOperationException("The selected Demucs model could not be loaded."));
                }
            }
            catch
            {
                ReleaseLeases(acquired, TimeSpan.Zero);
                throw;
            }
            this.modelLeases.AddRange(acquired);
            this.Onnx = acquired[0].Model;
            this.ApplyEffectiveOptions(this.Onnx.EffectiveOptions!);
            this.loadedCacheKeys = desiredOptions.Select(options => DemucsModelCache.Key(model.Path, options))
                .Order(StringComparer.Ordinal).ToArray();
            this.loadedCacheKey = this.loadedCacheKeys.FirstOrDefault();
            this.settings.Save();
            this.loadedWithCurrentOptions = true;
            this.checkedListBox_stems.Items.Clear();
            foreach (string stem in this.Onnx.AvailableStems)
                this.checkedListBox_stems.Items.Add(stem, this.settings.SelectedStems.Contains(stem));
            this.status.Text = this.WithDeviceNotice($"Ready — {this.Onnx.ExecutionProvider}" +
                (acquired.Count > 1 ? $" on {acquired.Count} GPUs" : string.Empty) +
                $"\n{this.Onnx.FourierBackend}\n{this.Onnx.LoadWarning}");
        }
        catch (Exception error) { this.ShowError("Unable to load Demucs", error); }
        finally { this.SetBusy(false); }
    }

    private void ApplyEffectiveOptions(DemucsLoadOptions options)
    {
        bool wasInitializing = this.initializing;
        this.initializing = true;
        try
        {
            this.settings.Options = options;
            this.ApplySettings();
            this.UpdateProviderControls();
        }
        finally { this.initializing = wasInitializing; }
    }

    private void SetBusy(bool value, string? message = null, bool loading = false)
    {
        this.busy = value;
        this.load.Enabled = !value && !this.loadedWithCurrentOptions && this.comboBox_models.Items.Count > 0;
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
        this.device.Enabled = dml;
        this.execution.Enabled = !dml;
        this.memoryPattern.Enabled = !dml;
        this.cpuFallback.Enabled = dml;
        this.cpuOperators.Enabled = dml;
        this.validate.Enabled = dml;
        this.disableDmlFusion.Enabled = dml;
        this.checkedListBox_cudaDevices.Enabled = !DemucsModelCache.HasDirectMLSessions;
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
        this.SetBusy(true, "Preparing audio for separation…");
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
                    $"{this.Onnx?.FourierBackend}";
            })).ToArray();
            var channel = Channel.CreateBounded<(int Index, string Name, float[] Samples)>(
                new BoundedChannelOptions(Math.Max(1, leases.Length))
                {
                    SingleReader = true,
                    SingleWriter = leases.Length == 1,
                    FullMode = BoundedChannelFullMode.Wait
                });
            var workers = leases.Select((lease, deviceIndex) => Task.Run(async () =>
            {
                await lease.Operation.WaitAsync(this.cancellation.Token);
                try
                {
                    for (int index = deviceIndex; index < selected.Length; index += leases.Length)
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
                    this.status.Text = $"{result.Name} ready — {completed}/{selected.Length} stem(s) extracted.";
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
            this.status.Text = $"Finished — {selected.Length} stem(s) extracted.\n{this.Onnx.FourierBackend}";
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
        base.OnFormClosed(e);
    }

    private async Task<bool> TryReuseModelsAsync(string path, DemucsLoadOptions[] options)
    {
        string[] keys = options.Select(option => DemucsModelCache.Key(path, option)).Order(StringComparer.Ordinal).ToArray();
        if (this.Onnx?.IsOnline == true && this.loadedCacheKeys.SequenceEqual(keys, StringComparer.Ordinal))
        {
            this.loadedWithCurrentOptions = true;
            this.status.Text = this.WithDeviceNotice($"Ready — model already loaded — {this.Onnx.ExecutionProvider}\n{this.Onnx.FourierBackend}");
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
        this.loadedCacheKey = keys.FirstOrDefault();
        this.ApplyEffectiveOptions(cached.EffectiveOptions!);
        this.loadedWithCurrentOptions = true;
        this.checkedListBox_stems.Items.Clear();
        foreach (string stem in cached.AvailableStems)
            this.checkedListBox_stems.Items.Add(stem, this.settings.SelectedStems.Contains(stem));
        this.status.Text = this.WithDeviceNotice($"Ready — reused {acquired.Count} loaded model(s) — {cached.ExecutionProvider}\n{cached.FourierBackend}\n{cached.LoadWarning}");
        return true;
    }

    private void ReleaseModel()
    {
        var service = this.Onnx;
        this.Onnx = null;
        this.loadedCacheKey = null;
        this.loadedCacheKeys = [];
        if (this.modelLeases.Count > 0)
        {
            ReleaseLeases(this.modelLeases, TimeSpan.FromSeconds((double)this.keepModelLoaded.Value));
        }
        else service?.Dispose();
    }

    private static void ReleaseLeases(IEnumerable<SharedModelPool<OnnxService>.Lease> leases, TimeSpan keepAlive)
    {
        foreach (var lease in leases)
        {
            lease.KeepAlive = keepAlive;
            lease.Dispose();
        }
        if (leases is List<SharedModelPool<OnnxService>.Lease> list) list.Clear();
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
