using Microsoft.ML.OnnxRuntime;
using ModularAudience.Audio;
using ModularAudience.Onnx;
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
    private readonly NumericUpDown keepModelLoaded = new() { Minimum = 0, Maximum = 86400, Value = 300, Increment = 30, Width = 120 };
    private string? loadedCacheKey;
    private readonly NumericUpDown cudaDevice = Number(0, 127);
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
    private readonly TableLayoutPanel optionsPanel = new() { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 4 };
    private readonly TableLayoutPanel checksPanel = new() { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2 };
    private int optionCount;
    private bool busy;
    private bool initializing = true;
    private bool closeRequested;
    private bool loadedWithCurrentOptions;

    public OnnxDemucsDialog(AudioObj audioObj)
    {
        this.InitializeComponent();
        this.OriginalAudio = audioObj;
        this.Text = "Demucs Source Separation";
        this.MaximumSize = Size.Empty;
        this.MinimumSize = new Size(1100, 740);
        this.ClientSize = new Size(1140, 770);
        this.StartPosition = FormStartPosition.CenterParent;
        this.Controls.Clear();
        this.comboBox_models.DropDownStyle = ComboBoxStyle.DropDownList;
        this.comboBox_models.DisplayMember = nameof(ModelItem.Name);
        this.comboBox_models.Dock = DockStyle.Fill;
        this.comboBox_models.Width = 560;
        this.checkedListBox_stems.Dock = DockStyle.Fill;
        this.checkedListBox_stems.Height = 115;
        this.progressBar_inferencing.Dock = DockStyle.Fill;
        this.progressBar_inferencing.Maximum = 1000;
        this.button_run.Text = "Run";
        this.button_run.AutoSize = true;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 115));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var modelRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        modelRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        modelRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        modelRow.Controls.Add(this.comboBox_models, 0, 0);
        modelRow.Controls.Add(this.browse, 1, 0);
        for (int i = 0; i < 2; i++)
        {
            this.optionsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 175));
            this.optionsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        }
        this.AddOption("Execution provider", this.provider);
        this.AddOption("DirectML device (DXGI adapter)", this.device);
        this.AddOption("Graph optimization", this.optimization);
        this.AddOption("Execution mode (CPU only)", this.execution);
        this.AddOption("Intra-op threads (0 = automatic)", this.intraThreads);
        this.AddOption("Inter-op threads (0 = automatic)", this.interThreads);
        this.AddOption("Profiling output prefix", this.profilePath);
        this.AddOption("Save optimized model (optional)", this.optimizedPath);
        this.AddOption("CPU FFT workers", this.workers);
        this.AddOption("CUDA device ID", this.cudaDevice);
        this.AddOption("CUDA 12 toolkit directory (optional)", this.cudaDirectory);
        this.AddOption("Chunk overlap (%)", this.overlap);
        int checksRow = (this.optionCount + 1) / 2;
        var checkGroup = new GroupBox { Text = "Execution and FFT options", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(8, 18, 8, 6) };
        this.checksPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        this.checksPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        int checkIndex = 0;
        foreach (var check in new[] { this.cpuFallback, this.cpuOperators, this.validate, this.disableDmlFusion, this.memoryPattern, this.cpuArena, this.spinning, this.prepacking, this.profiling, this.logging, this.cuda })
        {
            this.checksPanel.Controls.Add(check, checkIndex % 2, checkIndex / 2);
            checkIndex++;
        }
        checkGroup.Controls.Add(this.checksPanel);
        this.optionsPanel.Controls.Add(checkGroup, 0, checksRow);
        this.optionsPanel.SetColumnSpan(checkGroup, 4);
        this.optionCount = (checksRow + 1) * 2;
        this.AddOption("Extra ONNX session entries\n(one key=value per line)", this.advanced);
        var qualityInfo = new Label { AutoSize = true, Dock = DockStyle.Fill,
            Text = "Model FFT: 4096 / hop: 1024 (fixed).\nOverlap: 0% fastest, 25% balanced, 50–75% smoother chunk boundaries; more inference work." };
        this.AddOption("Separation quality", qualityInfo);
        this.AddOption("Keep model loaded (seconds)", this.keepModelLoaded);
        this.AddOption("After closing", new Label { AutoSize = true, Text = "0 = unload immediately; idle models are reused before expiry." });
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        buttons.Controls.AddRange([this.load, this.button_run, this.cancel]);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1 };
        footer.Controls.Add(this.status);
        footer.Controls.Add(this.progressBar_inferencing);
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
        this.optionsPanel.Controls.Add(new Label { Text = caption, AutoSize = true, Margin = new Padding(3, 7, 3, 5) }, column, row);
        control.Margin = new Padding(3, 4, 3, 4);
        this.optionsPanel.Controls.Add(control, column + 1, row);
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        this.SetBusy(true, "Looking for Demucs models…", loading: true);
        await Task.Yield();
        try
        {
            string? warning = null;
            try { this.settings = DemucsDialogSettings.Load(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
            { warning = $"Settings could not be loaded: {error.Message}"; }
            var devices = await Task.Run(DirectMLDevices.GetAdapters);
            this.device.Items.AddRange(devices.Cast<object>().ToArray());
            if (this.device.Items.Count == 0) this.device.Items.Add(new DirectMLDevices.Adapter(0, "Default adapter"));
            this.ApplySettings();
            this.Onnx = await Task.Run(() => new OnnxService(this.settings.ModelDirectories, loadImmediately: false));
            this.RefreshModels();
            if (this.Onnx.ModelPaths.Count == 0) await this.BrowseModelsAsync();
            bool reused = this.comboBox_models.SelectedItem is ModelItem selected && this.TryReuseModel(selected.Path, this.ReadOptions());
            if (!reused) this.status.Text = warning ?? "Select a model and load options, then click Load model. No model is loaded yet.";
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
        this.cudaDevice.Value = Math.Clamp(o.CudaDeviceId, 0, 127);
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
            FourierWorkers = (int)this.workers.Value, CudaDeviceId = (int)this.cudaDevice.Value,
            ChunkOverlap = (double)this.overlap.Value / 100,
            AllowCpuFallback = this.cpuFallback.Checked, AllowCpuOperators = this.cpuOperators.Checked,
            ValidateDirectML = this.validate.Checked, EnableMemoryPattern = this.memoryPattern.Checked,
            DisableDirectMLGraphFusion = this.disableDmlFusion.Checked,
            EnableCpuMemoryArena = this.cpuArena.Checked, DisableSpinning = this.spinning.Checked,
            DisablePrepacking = this.prepacking.Checked, EnableProfiling = this.profiling.Checked,
            LogSession = this.logging.Checked, PreferCudaFourier = this.cuda.Checked,
            CudaToolkitDirectory = this.cudaDirectory.Text.Trim(), ProfileOutputPathPrefix = this.profilePath.Text.Trim(),
            OptimizedModelPath = this.optimizedPath.Text.Trim(), SessionConfigEntries = entries
        };
    }

    private void OptionsChanged()
    {
        if (this.initializing || this.busy) return;
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
            var options = this.ReadOptions();
            this.settings.Options = options;
            this.settings.KeepModelLoadedSeconds = (int)this.keepModelLoaded.Value;
            this.settings.SelectedModel = model.Path;
            this.settings.Save();
            if (this.TryReuseModel(model.Path, options)) return;
            this.ReleaseModel();
            this.Onnx = await Task.Run(() => new OnnxService(this.settings.ModelDirectories, loadImmediately: false));
            await Task.Run(() => this.Onnx.LoadModel(model.Path, options));
            this.ApplyEffectiveOptions(this.Onnx.EffectiveOptions!);
            this.loadedCacheKey = DemucsModelCache.Key(model.Path, this.Onnx.EffectiveOptions!);
            this.settings.Save();
            this.loadedWithCurrentOptions = true;
            this.checkedListBox_stems.Items.Clear();
            foreach (string stem in this.Onnx.AvailableStems)
                this.checkedListBox_stems.Items.Add(stem, this.settings.SelectedStems.Contains(stem));
            this.status.Text = $"Ready — {this.Onnx.ExecutionProvider}\n{this.Onnx.FourierBackend}\n{this.Onnx.LoadWarning}";
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
        this.load.Enabled = !value && this.comboBox_models.Items.Count > 0;
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
        var progress = new Progress<double>(fraction =>
        {
            if (this.IsDisposed || this.Disposing || !this.busy) return;
            this.progressBar_inferencing.Value = Math.Clamp((int)(fraction * 1000), 0, 1000);
            if (completed >= selected.Length) return;
            this.status.Text = $"Separating {selected[completed]} ({completed + 1}/{selected.Length}) — {fraction:P0}\n{this.Onnx.FourierBackend}";
        });
        try
        {
            this.settings.SelectedStems = selected;
            this.settings.Save();
            AudioCollectionView? collection = null;
            // MoveNextAsync is driven on the worker pool; publishing each complete stem returns to the UI.
            await using var stems = this.Onnx.ExtractStemsSequentialAsync(this.OriginalAudio, selected, progress, this.cancellation.Token)
                .GetAsyncEnumerator(this.cancellation.Token);
            while (await Task.Run(async () => await stems.MoveNextAsync()))
            {
                this.cancellation.Token.ThrowIfCancellationRequested();
                var (name, samples) = stems.Current;
                AudioObj extract = await this.OriginalAudio.CloneAsync();
                extract.Data = samples;
                extract.Rename($"{this.OriginalAudio.OriginalName}_{name}");
                if (collection == null || collection.IsDisposed)
                    collection = new AudioCollectionView([], $"{this.OriginalAudio.Name} - Stems Demucs");
                collection.AudioC.Audios.Add(extract);
                collection.Show();
                completed++;
                this.progressBar_inferencing.Value = 0;
                this.status.Text = $"{name} ready — {completed}/{selected.Length} stem(s) extracted.\n{this.Onnx.FourierBackend}";
            }
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
        this.ReleaseModel();
        this.cancellation.Dispose();
        base.OnFormClosed(e);
    }

    private bool TryReuseModel(string path, DemucsLoadOptions options)
    {
        string key = DemucsModelCache.Key(path, options);
        if (this.Onnx?.IsOnline == true && this.loadedCacheKey == key)
        {
            this.loadedWithCurrentOptions = true;
            this.status.Text = $"Ready — model already loaded — {this.Onnx.ExecutionProvider}\n{this.Onnx.FourierBackend}";
            return true;
        }
        OnnxService? cached = DemucsModelCache.Take(key);
        if (cached == null) return false;
        this.ReleaseModel();
        this.Onnx = cached;
        this.loadedCacheKey = key;
        this.ApplyEffectiveOptions(cached.EffectiveOptions!);
        this.loadedWithCurrentOptions = true;
        this.checkedListBox_stems.Items.Clear();
        foreach (string stem in cached.AvailableStems)
            this.checkedListBox_stems.Items.Add(stem, this.settings.SelectedStems.Contains(stem));
        this.status.Text = $"Ready — reused loaded model — {cached.ExecutionProvider}\n{cached.FourierBackend}\n{cached.LoadWarning}";
        return true;
    }

    private void ReleaseModel()
    {
        var service = this.Onnx;
        this.Onnx = null;
        string? key = this.loadedCacheKey;
        this.loadedCacheKey = null;
        if (service == null) return;
        if (key != null && service.IsOnline)
            DemucsModelCache.Return(key, service, TimeSpan.FromSeconds((double)this.keepModelLoaded.Value));
        else service.Dispose();
    }
}
