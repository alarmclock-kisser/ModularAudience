using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Onnx;
using ModularAudience.Forms;
using System.Reflection;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class DemucsTests
{
    [TestMethod]
    public void SpectrogramMatchesTorchNormalizedHannAndComplexChannelOrder()
    {
        const int length = 8192;
        float[] audio = CreateSignal(length);
        var spectrum = DemucsSpectrogram.Forward(audio, length);
        // Reference values from torch.stft + HTDemucs._spec (float64).
        Assert.AreEqual(0.11110828f, spectrum[0, 0, 0, 0], 0.00001f);
        Assert.AreEqual(-1.98622127f, spectrum[0, 0, 46, 0], 0.00001f);
        Assert.AreEqual(-1.49592949f, spectrum[0, 1, 46, 0], 0.00001f);
        Assert.AreEqual(4.55405699f, spectrum[0, 1, 46, 3], 0.00001f);
        Assert.AreEqual(0.76154061f, spectrum[0, 2, 25, 7], 0.00001f);
        Assert.AreEqual(1.24516231f, spectrum[0, 3, 25, 7], 0.00001f);
    }

    [TestMethod]
    public void InverseMatchesTorchIncludingCroppedFrameBoundaryWeights()
    {
        const int length = 8192;
        var spectrum = DemucsSpectrogram.Forward(CreateSignal(length), length);
        var batched = new DenseTensor<float>(spectrum.ToArray(), [1, 1, 4, 2048, 8]);
        float[] audio = DemucsSpectrogram.Inverse(batched, 0, length);
        Assert.AreEqual(0.05000130f, audio[0], 0.00001f);
        Assert.AreEqual(0.14040060f, audio[200], 0.00001f);
        Assert.AreEqual(0.24195934f, audio[4096], 0.00001f);
        Assert.AreEqual(0.05465871f, audio[2 * 8191 + 1], 0.00001f);
        Assert.AreEqual(-0.18935127f, audio[2 * 7000 + 1], 0.00001f);
    }

    [TestMethod]
    public void MissingModelDirectoriesAreIgnored()
    {
        using var service = new OnnxService(loadImmediately: false);
        service.ModelDirectories = [System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString())];
        Assert.IsEmpty(service.GetModelPaths());
        Assert.IsFalse(service.IsOnline);
    }

    [TestMethod]
    [DataRow(1, false)]
    [DataRow(4, false)]
    [DataRow(4, true)]
    public async Task AsyncFourierBackendsMatchTorchAndPreserveStereo(int workers, bool cuda)
    {
        var backend = new DemucsFourier(new DemucsLoadOptions { FourierWorkers = workers, PreferCudaFourier = cuda });
        var spectrum = await DemucsSpectrogram.ForwardAsync(CreateSignal(8192), 8192, backend);
        Assert.AreEqual(-1.98622127f, spectrum[0, 0, 46, 0], 0.00002f);
        Assert.AreEqual(1.24516231f, spectrum[0, 3, 25, 7], 0.00002f);
        var batch = new DenseTensor<float>(spectrum.ToArray(), [1, 1, 4, 2048, 8]);
        float[] audio = await DemucsSpectrogram.InverseAsync(batch, 0, 8192, backend);
        Assert.AreEqual(0.24195934f, audio[4096], 0.00002f);
        Assert.AreEqual(-0.18935127f, audio[2 * 7000 + 1], 0.00002f);
        Console.WriteLine(backend.Backend);
    }

    [TestMethod]
    public async Task UnavailableCudaFallsBackToCpuFourier()
    {
        var backend = new DemucsFourier(new DemucsLoadOptions
        {
            PreferCudaFourier = true, CudaToolkitDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()), FourierWorkers = 2
        });
        var spectrum = await DemucsSpectrogram.ForwardAsync(CreateSignal(8192), 8192, backend);
        Assert.AreEqual(-1.98622127f, spectrum[0, 0, 46, 0], 0.00002f);
        StringAssert.Contains(backend.Backend, "Parallel CPU FFT (2 workers)");
    }

    [STATestMethod]
    public void DialogConstructsWithoutLoadingModelAndExposesLoadRunAndWorkers()
    {
        using var audio = new AudioObj();
        using var dialog = new OnnxDemucsDialog(audio);
        object? Field(string name) => typeof(OnnxDemucsDialog).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog);
        Assert.IsNull(Field("Onnx"));
        Assert.AreEqual("Load model", ((Button)Field("load")!).Text);
        Assert.IsFalse(((Button)Field("button_run")!).Enabled);
        Assert.IsTrue(((NumericUpDown)Field("workers")!).Minimum >= 1);
        Assert.AreEqual((decimal)Environment.ProcessorCount, ((NumericUpDown)Field("workers")!).Maximum);
        Assert.AreEqual((decimal)Environment.ProcessorCount, ((NumericUpDown)Field("intraThreads")!).Maximum);
        Assert.AreEqual((decimal)Environment.ProcessorCount, ((NumericUpDown)Field("interThreads")!).Maximum);
        dialog.ShowInTaskbar = false;
        dialog.CreateControl();
        dialog.PerformLayout();
        Assert.IsTrue(dialog.ClientSize.Width >= 780);
        var options = (TableLayoutPanel)Field("optionsPanel")!;
        ((Label)Field("status")!).Text = "Ready — DirectML (device 0)\nCUDA 12 CuFFT (device 0)\nSelect stems to extract.";
        Assert.IsTrue(options.Bottom <= options.Parent!.ClientSize.Height, "All options must fit without an internal scrollbar.");
        Assert.AreEqual(4, options.ColumnCount);
        Assert.AreEqual(2, ((TableLayoutPanel)Field("checksPanel")!).ColumnCount);
        foreach (Size size in new[] { dialog.Size, dialog.MinimumSize })
        {
            dialog.Size = size;
            dialog.PerformLayout();
            var root = (TableLayoutPanel)options.Parent!;
            Assert.IsTrue(options.Bottom <= root.GetControlFromPosition(0, 2)!.Top,
                $"Options must not overlap the stems row: bottom={options.Bottom}, stems={root.GetControlFromPosition(0, 2)!.Top}, size={dialog.Size}.");
            foreach (Control child in options.Controls)
                Assert.IsTrue(child.Right <= options.ClientSize.Width, $"Option {child.Text} must fit horizontally.");
            var checks = (TableLayoutPanel)Field("checksPanel")!;
            foreach (Control child in checks.Controls)
                Assert.IsTrue(child.Right <= checks.ClientSize.Width, $"Checkbox {child.Text} must fit horizontally.");
            Assert.IsTrue(root.GetControlFromPosition(0, 4)!.Bottom <= root.ClientSize.Height,
                "Progress and status must remain inside the dialog.");
        }
    }

    [TestMethod]
    public void WorkerLimitsAlsoApplyToLoadedSettingsAndBackend()
    {
        var options = new DemucsLoadOptions { FourierWorkers = int.MaxValue, IntraOpThreads = int.MaxValue, InterOpThreads = int.MaxValue };
        Assert.AreEqual(Environment.ProcessorCount, new DemucsFourier(options).Workers);
        options.NormalizeWorkers();
        Assert.AreEqual(Environment.ProcessorCount, options.FourierWorkers);
        Assert.AreEqual(Environment.ProcessorCount, options.IntraOpThreads);
        Assert.AreEqual(Environment.ProcessorCount, options.InterOpThreads);
    }

    [TestMethod]
    public void DirectMLAdaptersUseNamedDxgiIndices()
    {
        Type type = typeof(OnnxDemucsDialog).Assembly.GetType("ModularAudience.Forms.DirectMLDevices")!;
        var adapters = (Array)type.GetMethod("GetAdapters", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
        Assert.IsGreaterThan(0, adapters.Length);
        foreach (object adapter in adapters) Console.WriteLine(adapter);
    }

    [TestMethod]
    [DataRow(false, Microsoft.ML.OnnxRuntime.GraphOptimizationLevel.ORT_ENABLE_ALL)]
    [DataRow(true, Microsoft.ML.OnnxRuntime.GraphOptimizationLevel.ORT_ENABLE_ALL)]
    [TestCategory("DemucsIntegration")]
    public async Task InstalledModelSeparatesAllStemsWithoutMutatingSource(bool directML, Microsoft.ML.OnnxRuntime.GraphOptimizationLevel optimization)
    {
        const string model = "D:/Models/Demucs/htdemucs_6s.onnx";
        if (!File.Exists(model)) Assert.Inconclusive("The local Demucs model is not installed.");
        using var service = new OnnxService(loadImmediately: false);
        service.LoadModel(model, new DemucsLoadOptions { UseDirectML = directML, DeviceId = 0, GraphOptimization = optimization });
        Console.WriteLine(service.ExecutionProvider + " " + service.LoadWarning);
        if (directML) StringAssert.StartsWith(service.ExecutionProvider, "DirectML");
        Assert.AreEqual(directML, service.EffectiveOptions!.UseDirectML);
        using var source = new AudioObj { Data = new float[44100 * 2], SampleRate = 44100, Channels = 2 };
        for (int i = 0; i < 44100; i++)
        {
            source.Data[2 * i] = (float)(0.2 * Math.Sin(2 * Math.PI * 110 * i / 44100)
                + 0.1 * Math.Sin(2 * Math.PI * 440 * i / 44100));
            source.Data[2 * i + 1] = source.Data[2 * i] * 0.8f;
        }
        float[] original = (float[])source.Data.Clone();
        var stems = await service.ExtractStemsAsync(source, service.AvailableStems);
        Assert.HasCount(6, stems);
        foreach (var (name, samples) in stems)
        {
            Assert.HasCount(original.Length, samples);
            Assert.IsTrue(samples.All(float.IsFinite), name);
            double rms = Math.Sqrt(samples.Sum(v => (double)v * v) / samples.Length);
            Assert.IsTrue(rms > 1e-8 && rms < 1, $"{name}: RMS {rms}");
        }
        CollectionAssert.AreEqual(original, source.Data);
        Assert.AreEqual(44100, source.SampleRate);
        Assert.AreEqual(2, source.Channels);
    }

    [TestMethod]
    [TestCategory("DemucsIntegration")]
    public async Task SequentialStemsYieldFirstTrackBeforeNextPassAndKeepItOnCancellation()
    {
        const string model = "D:/Models/Demucs/htdemucs_6s.onnx";
        if (!File.Exists(model)) Assert.Inconclusive("The local Demucs model is not installed.");
        using var service = new OnnxService(loadImmediately: false);
        service.LoadModel(model, new DemucsLoadOptions { UseDirectML = false });
        using var source = new AudioObj { Data = new float[44100 * 2], SampleRate = 44100, Channels = 2 };
        for (int i = 0; i < 44100; i++)
            source.Data[2 * i] = source.Data[2 * i + 1] = (float)(0.2 * Math.Sin(2 * Math.PI * 110 * i / 44100));
        using var cancellation = new CancellationTokenSource();
        await using var stems = service.ExtractStemsSequentialAsync(source, ["drums", "bass", "vocals"], cancellationToken: cancellation.Token)
            .GetAsyncEnumerator();
        Assert.IsTrue(await stems.MoveNextAsync());
        Assert.AreEqual("drums", stems.Current.Key);
        float[] first = stems.Current.Value;
        Assert.HasCount(source.Data.Length, first);
        Assert.IsTrue(first.Any(value => Math.Abs(value) > 1e-8));
        Assert.IsTrue(await stems.MoveNextAsync());
        Assert.AreEqual("bass", stems.Current.Key);
        Assert.HasCount(source.Data.Length, stems.Current.Value);
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await stems.MoveNextAsync());
        Assert.IsTrue(first.All(float.IsFinite));
    }

    [TestMethod]
    [TestCategory("DemucsIntegration")]
    public void FailedDirectMLDeviceReportsEffectiveCpuOptions()
    {
        const string model = "D:/Models/Demucs/htdemucs_6s.onnx";
        if (!File.Exists(model)) Assert.Inconclusive("The local Demucs model is not installed.");
        using var service = new OnnxService(loadImmediately: false);
        var requested = new DemucsLoadOptions { DeviceId = int.MaxValue, AllowCpuFallback = true, IntraOpThreads = 2 };
        service.LoadModel(model, requested);
        Assert.AreEqual("CPU", service.ExecutionProvider);
        Assert.IsFalse(service.EffectiveOptions!.UseDirectML);
        Assert.AreEqual(2, service.EffectiveOptions.IntraOpThreads);
        Assert.IsTrue(requested.UseDirectML, "Reporting effective settings must not mutate the caller's requested settings.");
        StringAssert.Contains(service.LoadWarning!, "DirectML could not initialize");
    }

    [STATestMethod]
    public void EffectiveCpuFallbackOptionsUpdateDialogFields()
    {
        using var audio = new AudioObj();
        using var dialog = new OnnxDemucsDialog(audio);
        object Field(string name) => typeof(OnnxDemucsDialog).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
        var devices = (ComboBox)Field("device");
        Type adapter = typeof(OnnxDemucsDialog).Assembly.GetType("ModularAudience.Forms.DirectMLDevices+Adapter")!;
        devices.Items.Add(Activator.CreateInstance(adapter, [0, "Test adapter"])!);
        var effective = new DemucsLoadOptions
        {
            UseDirectML = false, IntraOpThreads = 3, InterOpThreads = 2, EnableMemoryPattern = true,
            ExecutionMode = Microsoft.ML.OnnxRuntime.ExecutionMode.ORT_PARALLEL
        };
        typeof(OnnxDemucsDialog).GetMethod("ApplyEffectiveOptions", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(dialog, [effective]);
        Assert.AreEqual("CPU", ((ComboBox)Field("provider")).Text);
        Assert.IsFalse(devices.Enabled);
        Assert.AreEqual(3m, ((NumericUpDown)Field("intraThreads")).Value);
        Assert.AreEqual(2m, ((NumericUpDown)Field("interThreads")).Value);
        Assert.IsTrue(((CheckBox)Field("memoryPattern")).Checked);
        Assert.AreEqual("ORT_PARALLEL", ((ComboBox)Field("execution")).Text);
    }

    private static float[] CreateSignal(int length)
    {
        var audio = new float[2 * length];
        for (int i = 0; i < length; i++)
        {
            audio[i] = (float)(0.3 * Math.Sin(i * 0.071) + 0.1 * Math.Cos(i * 0.013));
            audio[length + i] = (float)(0.2 * Math.Cos(i * 0.039));
        }
        return audio;
    }
}
