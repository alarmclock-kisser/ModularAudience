using Microsoft.ML.OnnxRuntime;
using ModularAudience.Audio;
using ModularAudience.Audio.Processing;
using ModularAudience.Forms.Helpers;
using ModularAudience.Onnx;
using System.Text.Json;

namespace ModularAudience.Forms.Modules.Dialogs;

public sealed partial class DecompileConfigDialog
{
    private DemucsDialogSettings demucsSettings = new();
    private DirectMLDevices.Adapter[] directMLAdapters = [];
    private DemucsCudaDevice[] cudaDevices = [];
    private DemucsCudaToolkit[] cudaToolkits = [];
    private bool initializingDemucs;
    private bool demucsInitializationFailed;

    private sealed record DemucsModelChoice(string Path)
    {
        public string Name => System.IO.Path.GetFileName(this.Path);
        public override string ToString() => this.Name;
    }

    private sealed record DirectMLChoice(DirectMLDevices.Adapter Adapter, DemucsCudaDevice? Cuda)
    {
        public override string ToString() => this.Adapter.ToString();
    }

    private sealed record CudaToolkitChoice(int Version)
    {
        public override string ToString() => $"CUDA {this.Version}";
    }

    private async Task InitializeDemucsUiAsync()
    {
        if (this.initializingDemucs)
        {
            return;
        }

        this.initializingDemucs = true;
        this.demucsInitializationFailed = false;
        this.SetDemucsInitializing(true);
        try
        {
            try
            {
                this.demucsSettings = CloneSettings(this.persistedSettings.Demucs)
                    ?? DemucsDialogSettings.Load();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
            {
                LogManager.Log($"Decompile Demucs settings load failed: {error}");
                this.demucsSettings = new DemucsDialogSettings();
            }

            Task<DirectMLDevices.Adapter[]> adaptersTask = Task.Run(DirectMLDevices.GetAdapters);
            Task<DemucsCudaDeviceCatalog> cudaTask = Task.Run(DemucsFourier.EnumerateCudaDevices);
            await Task.WhenAll(adaptersTask, cudaTask).ConfigureAwait(true);
            this.directMLAdapters = adaptersTask.Result;
            this.cudaDevices = cudaTask.Result.Devices.ToArray();
            this.cudaToolkits = cudaTask.Result.CuFftToolkits.ToArray();
            this.comboBox_demucsProvider.SelectedIndexChanged -= this.comboBox_demucsProvider_SelectedIndexChanged;
            this.comboBox_demucsProvider.SelectedIndexChanged += this.comboBox_demucsProvider_SelectedIndexChanged;
            this.checkBox_cudaCuFft.CheckedChanged -= this.useCudaCuFft_CheckedChanged;
            this.checkBox_cudaCuFft.CheckedChanged += this.useCudaCuFft_CheckedChanged;
            this.PopulateDemucsDevices(cudaTask.Result.Warning);
            this.PopulateDemucsEnums();
            this.ApplyDemucsSettings();
            await this.RefreshDemucsModelsAsync(this.demucsSettings.ModelDirectories).ConfigureAwait(true);
            this.UpdateDemucsProviderControls();
        }
        catch (Exception error)
        {
            this.demucsInitializationFailed = true;
            LogManager.Log($"Decompile Demucs initialization failed: {error}");
            WindowMainStaticHelpers.ShowErrorWithCopyButton(this, "Demucs Configuration Failed", error);
        }
        finally
        {
            this.initializingDemucs = false;
            this.SetDemucsInitializing(false);
        }
    }

    private async void button_browseModels_Click(object? sender, EventArgs e)
    {
        if (this.initializingDemucs)
        {
            return;
        }

        using var dialog = new FolderBrowserDialog
        {
            Description = "Select a directory containing Demucs ONNX models.",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
            SelectedPath = this.GetModelsBrowseDirectory()
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        this.label_status.Text = "Scanning Demucs models...";
        try
        {
            List<string> directories = this.demucsSettings.ModelDirectories
                .Append(dialog.SelectedPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            await this.RefreshDemucsModelsAsync(directories).ConfigureAwait(true);
            this.demucsSettings.ModelDirectories = directories;
            this.demucsSettings.Save();
        }
        catch (Exception error)
        {
            LogManager.Log($"Decompile model directory scan failed: {error}");
            WindowMainStaticHelpers.ShowErrorWithCopyButton(this, "Demucs Model Scan Failed", error);
        }
    }

    private IAudioStemSeparator CreateSeparator(int maxWorkers)
    {
        return this.comboBox_separationMethod.SelectedIndex switch
        {
            0 => this.CreateDemucsSeparator(maxWorkers),
            1 => this.CreateAnalogSeparator(maxWorkers),
            2 => this.CreateDeterministicSeparator(maxWorkers),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private IAudioStemSeparator CreateDemucsSeparator(int maxWorkers)
    {
        if (this.demucsInitializationFailed || this.comboBox_demucsModel.SelectedItem is not DemucsModelChoice model)
        {
            throw new InvalidOperationException("Select a valid Demucs ONNX model in the Separation tab.");
        }

        DemucsLoadOptions baseline = this.ReadDemucsOptions(maxWorkers);
        List<DemucsLoadOptions> desired = [];
        if (baseline.UseDirectML)
        {
            DirectMLChoice[] selected = this.checkedListBox_directMLDevices.CheckedItems
                .Cast<DirectMLChoice>()
                .DistinctBy(choice => choice.Adapter.Id)
                .Take(maxWorkers)
                .ToArray();
            if (selected.Length == 0)
            {
                throw new ArgumentException("Select at least one DirectML device or choose CPU execution.");
            }

            foreach (DirectMLChoice choice in selected)
            {
                DemucsLoadOptions copy = CloneDemucsOptions(baseline);
                copy.DeviceId = choice.Adapter.Id;
                copy.DeviceLuid = choice.Adapter.Luid;
                copy.CudaDeviceId = copy.UseCudaCuFft ? choice.Cuda?.Id ?? -1 : -1;
                desired.Add(copy);
            }
        }
        else
        {
            desired.Add(baseline);
        }

        this.demucsSettings.Options = CloneDemucsOptions(baseline);
        this.demucsSettings.SelectedModel = model.Path;
        this.demucsSettings.KeepModelLoadedSeconds = (int)this.numeric_keepModelLoaded.Value;
        this.demucsSettings.SelectedDirectMLDeviceIds = this.checkedListBox_directMLDevices.CheckedItems
            .Cast<DirectMLChoice>()
            .Select(choice => choice.Adapter.Id)
            .ToArray();
        try
        {
            this.demucsSettings.Save();
        }
        catch (Exception error)
        {
            LogManager.Log($"Decompile Demucs settings save failed: {error}");
        }

        return new DemucsAudioStemSeparator(
            model.Path,
            desired,
            this.demucsSettings.ModelDirectories,
            TimeSpan.FromSeconds((double)this.numeric_keepModelLoaded.Value));
    }

    private DemucsLoadOptions ReadDemucsOptions(int maxWorkers)
    {
        bool useDirectML = this.comboBox_demucsProvider.SelectedIndex == 0;
        DirectMLChoice? selectedDevice = this.checkedListBox_directMLDevices.CheckedItems
            .Cast<DirectMLChoice>()
            .FirstOrDefault();
        int intraThreads = (int)this.numeric_intraThreads.Value;
        int interThreads = (int)this.numeric_interThreads.Value;
        DemucsCudaDevice? cpuCuda = this.cudaDevices.FirstOrDefault();
        bool useCuda = this.checkBox_cudaCuFft.Checked && this.cudaDevices.Length > 0 && this.cudaToolkits.Length > 0;
        int cudaDeviceId = !useCuda
            ? -1
            : useDirectML
                ? selectedDevice?.Cuda?.Id ?? -1
                : cpuCuda?.Id ?? -1;
        var options = new DemucsLoadOptions
        {
            UseDirectML = useDirectML,
            DeviceId = selectedDevice?.Adapter.Id ?? this.demucsSettings.Options.DeviceId,
            AllowCpuFallback = this.checkBox_cpuFallback.Checked,
            AllowCpuOperators = this.checkBox_cpuOperators.Checked,
            DisableDirectMLGraphFusion = this.checkBox_disableDmlFusion.Checked,
            FourierWorkers = Math.Min((int)this.numeric_fourierWorkers.Value, maxWorkers),
            ChunkOverlap = (double)this.numeric_chunkOverlap.Value / 100.0,
            CudaDeviceId = cudaDeviceId,
            UseCudaCuFft = useCuda,
            CudaToolkitVersion = this.comboBox_cudaToolkit.SelectedItem is CudaToolkitChoice choice
                ? choice.Version
                : 0,
            DeviceLuid = useDirectML
                ? selectedDevice?.Adapter.Luid
                : useCuda
                    ? cpuCuda?.Luid
                    : null,
            GraphOptimization = Enum.Parse<GraphOptimizationLevel>(
                this.comboBox_graphOptimization.Text,
                ignoreCase: false),
            ExecutionMode = Enum.Parse<ExecutionMode>(
                this.comboBox_executionMode.Text,
                ignoreCase: false),
            EnableMemoryPattern = this.checkBox_memoryPattern.Checked,
            EnableCpuMemoryArena = this.checkBox_cpuArena.Checked,
            IntraOpThreads = intraThreads == 0 ? maxWorkers : Math.Min(intraThreads, maxWorkers),
            InterOpThreads = interThreads == 0 ? maxWorkers : Math.Min(interThreads, maxWorkers),
            EnableProfiling = this.checkBox_profiling.Checked,
            ProfileOutputPathPrefix = this.textBox_profilePrefix.Text.Trim(),
            LogSession = this.checkBox_verboseLogging.Checked,
            DisableSpinning = this.checkBox_disableSpinning.Checked,
            DisablePrepacking = this.checkBox_disablePrepacking.Checked,
            OptimizedModelPath = this.textBox_optimizedModel.Text.Trim(),
            SessionConfigEntries = new Dictionary<string, string>(
                this.demucsSettings.Options.SessionConfigEntries ?? [])
        };
        options.NormalizeForProvider(options.UseDirectML);
        return options;
    }

    private async Task RefreshDemucsModelsAsync(IReadOnlyList<string> directories)
    {
        using var discovery = new OnnxService(directories, loadImmediately: false);
        string[] paths = await Task.Run(() => discovery.GetModelPaths()).ConfigureAwait(true);
        this.comboBox_demucsModel.Items.Clear();
        this.comboBox_demucsModel.Items.AddRange(
            paths.Select(path => (object)new DemucsModelChoice(path)).ToArray());
        this.comboBox_demucsModel.SelectedItem = this.comboBox_demucsModel.Items
            .Cast<DemucsModelChoice>()
            .FirstOrDefault(item => string.Equals(
                item.Path,
                this.demucsSettings.SelectedModel,
                StringComparison.OrdinalIgnoreCase));
        if (this.comboBox_demucsModel.SelectedIndex < 0 && this.comboBox_demucsModel.Items.Count > 0)
        {
            this.comboBox_demucsModel.SelectedIndex = 0;
        }

        this.label_status.Text = paths.Length == 0
            ? "No Demucs ONNX models found. Use Browse models."
            : $"{paths.Length} Demucs ONNX model(s) found.";
    }

    private void PopulateDemucsEnums()
    {
        if (this.comboBox_graphOptimization.Items.Count == 0)
        {
            this.comboBox_graphOptimization.Items.AddRange(Enum.GetNames<GraphOptimizationLevel>());
        }

        if (this.comboBox_executionMode.Items.Count == 0)
        {
            this.comboBox_executionMode.Items.AddRange(Enum.GetNames<ExecutionMode>());
        }
    }

    private void PopulateDemucsDevices(string? warning)
    {
        this.checkedListBox_directMLDevices.Items.Clear();
        foreach (DirectMLDevices.Adapter adapter in this.directMLAdapters)
        {
            DemucsCudaDevice? cuda = this.cudaDevices.FirstOrDefault(device =>
                DirectMLDevices.MatchCudaDevice(device, [adapter]) is not null);
            this.checkedListBox_directMLDevices.Items.Add(new DirectMLChoice(adapter, cuda), false);
        }

        this.comboBox_cudaToolkit.Items.Clear();
        this.comboBox_cudaToolkit.Items.AddRange(
            this.cudaToolkits.Select(toolkit => (object)new CudaToolkitChoice(toolkit.MajorVersion)).ToArray());
        List<string> notices = [];
        if (!string.IsNullOrWhiteSpace(warning))
        {
            notices.Add(warning);
        }

        if (this.directMLAdapters.Length == 0)
        {
            notices.Add("No DirectML adapters were detected; CPU execution is required.");
        }

        if (this.cudaDevices.Length == 0)
        {
            notices.Add("No CUDA device was detected.");
        }
        else if (this.cudaToolkits.Length == 0)
        {
            notices.Add("No supported CUDA 12/13 CuFFT toolkit was detected.");
        }

        this.label_demucsDeviceNotice.Text = notices.Count == 0
            ? "DirectML devices and CUDA CuFFT availability were detected successfully."
            : string.Join(" ", notices);
    }

    private void ApplyDemucsSettings()
    {
        DemucsLoadOptions options = this.demucsSettings.Options;
        options.NormalizeWorkers();
        bool directMLAvailable = this.directMLAdapters.Length > 0;
        if (!directMLAvailable)
        {
            options.UseDirectML = false;
        }

        this.numeric_keepModelLoaded.Value = Math.Clamp(this.demucsSettings.KeepModelLoadedSeconds, 0, 86400);
        this.comboBox_demucsProvider.SelectedIndex = options.UseDirectML && directMLAvailable ? 0 : 1;
        this.comboBox_graphOptimization.SelectedItem = options.GraphOptimization.ToString();
        this.comboBox_executionMode.SelectedItem = options.ExecutionMode.ToString();
        this.numeric_intraThreads.Value = Math.Clamp(options.IntraOpThreads, 0, Environment.ProcessorCount);
        this.numeric_interThreads.Value = Math.Clamp(options.InterOpThreads, 0, Environment.ProcessorCount);
        this.numeric_fourierWorkers.Value = options.FourierWorkers;
        this.numeric_chunkOverlap.Value = Math.Clamp((decimal)(options.ChunkOverlap * 100), 0, 75);
        this.checkBox_cpuFallback.Checked = options.AllowCpuFallback;
        this.checkBox_cpuOperators.Checked = options.AllowCpuOperators;
        this.checkBox_disableDmlFusion.Checked = options.DisableDirectMLGraphFusion;
        this.checkBox_memoryPattern.Checked = options.EnableMemoryPattern;
        this.checkBox_cpuArena.Checked = options.EnableCpuMemoryArena;
        this.checkBox_disableSpinning.Checked = options.DisableSpinning;
        this.checkBox_disablePrepacking.Checked = options.DisablePrepacking;
        this.checkBox_profiling.Checked = options.EnableProfiling;
        this.checkBox_verboseLogging.Checked = options.LogSession;
        this.textBox_profilePrefix.Text = options.ProfileOutputPathPrefix;
        this.textBox_optimizedModel.Text = options.OptimizedModelPath;
        this.checkBox_cudaCuFft.Checked = options.UseCudaCuFft
            && this.cudaDevices.Length > 0
            && this.cudaToolkits.Length > 0;
        CudaToolkitChoice? selectedToolkit = this.comboBox_cudaToolkit.Items
            .Cast<CudaToolkitChoice>()
            .FirstOrDefault(choice => choice.Version == options.CudaToolkitVersion)
            ?? this.comboBox_cudaToolkit.Items.Cast<CudaToolkitChoice>().FirstOrDefault();
        this.comboBox_cudaToolkit.SelectedItem = selectedToolkit;

        int[] selectedIds = this.demucsSettings.SelectedDirectMLDeviceIds.Length > 0
            ? this.demucsSettings.SelectedDirectMLDeviceIds
            : [options.DeviceId];
        if (!selectedIds.Any(id => this.checkedListBox_directMLDevices.Items
                .Cast<DirectMLChoice>().Any(choice => choice.Adapter.Id == id))
            && this.checkedListBox_directMLDevices.Items.Count > 0)
        {
            selectedIds = [this.checkedListBox_directMLDevices.Items.Cast<DirectMLChoice>().First().Adapter.Id];
        }

        for (int index = 0; index < this.checkedListBox_directMLDevices.Items.Count; index++)
        {
            var choice = (DirectMLChoice)this.checkedListBox_directMLDevices.Items[index];
            this.checkedListBox_directMLDevices.SetItemChecked(index, selectedIds.Contains(choice.Adapter.Id));
        }
    }

    private void UpdateDemucsProviderControls()
    {
        bool directML = this.comboBox_demucsProvider.SelectedIndex == 0;
        this.checkedListBox_directMLDevices.Enabled = directML;
        this.checkBox_disableDmlFusion.Checked = true;
        this.checkBox_disableDmlFusion.Enabled = false;
        this.checkBox_cpuOperators.Enabled = directML;
        this.checkBox_memoryPattern.Enabled = !directML;
        this.comboBox_executionMode.Enabled = !directML;
        this.checkBox_cudaCuFft.Enabled = this.cudaDevices.Length > 0 && this.cudaToolkits.Length > 0;
        this.comboBox_cudaToolkit.Enabled = this.checkBox_cudaCuFft.Enabled && this.checkBox_cudaCuFft.Checked;
        if (!directML)
        {
        }
    }

    private void SetDemucsInitializing(bool busy)
    {
        this.comboBox_demucsModel.Enabled = !busy;
        this.button_browseModels.Enabled = !busy;
        this.panel_demucs.UseWaitCursor = busy;
        if (busy)
        {
            this.label_status.Text = "Checking Demucs models, DirectML adapters and CUDA 12/13...";
        }
    }

    private void comboBox_separationMethod_SelectedIndexChanged(object? sender, EventArgs e)
    {
        this.UpdateSeparationPanel();
    }

    private void comboBox_demucsProvider_SelectedIndexChanged(object? sender, EventArgs e)
    {
        this.UpdateDemucsProviderControls();
    }

    private void useCudaCuFft_CheckedChanged(object? sender, EventArgs e)
    {
        this.UpdateDemucsProviderControls();
    }

    private void UpdateSeparationPanel()
    {
        this.panel_demucs.Visible = this.comboBox_separationMethod.SelectedIndex == 0;
        this.panel_analog.Visible = this.comboBox_separationMethod.SelectedIndex == 1;
        this.panel_deterministic.Visible = this.comboBox_separationMethod.SelectedIndex == 2;
        this.panel_demucs.BringToFront();
        if (this.panel_analog.Visible)
        {
            this.panel_analog.BringToFront();
        }
        else if (this.panel_deterministic.Visible)
        {
            this.panel_deterministic.BringToFront();
        }
    }

    private string GetModelsBrowseDirectory()
    {
        if (this.comboBox_demucsModel.SelectedItem is DemucsModelChoice selected)
        {
            string? parent = System.IO.Path.GetDirectoryName(selected.Path);
            if (parent != null && Directory.Exists(parent))
            {
                return parent;
            }
        }

        return this.demucsSettings.ModelDirectories.FirstOrDefault(Directory.Exists)
            ?? this.demucsSettings.ModelDirectories.FirstOrDefault()
            ?? "D:/Models/Demucs";
    }

    private static DemucsLoadOptions CloneDemucsOptions(DemucsLoadOptions source)
    {
        string json = JsonSerializer.Serialize(source);
        return JsonSerializer.Deserialize<DemucsLoadOptions>(json)
            ?? throw new InvalidDataException("Could not clone Demucs options.");
    }
}
