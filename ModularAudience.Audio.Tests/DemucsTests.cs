using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Onnx;
using ModularAudience.Forms;
using System.Reflection;
using System.Numerics;

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
        var backend = new DemucsFourier(new DemucsLoadOptions { FourierWorkers = workers, CudaDeviceId = cuda ? 0 : -1 });
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
            CudaDeviceId = int.MaxValue, FourierWorkers = 2
        });
        var spectrum = await DemucsSpectrogram.ForwardAsync(CreateSignal(8192), 8192, backend);
        Assert.AreEqual(-1.98622127f, spectrum[0, 0, 46, 0], 0.00002f);
        StringAssert.Contains(backend.Backend, "Parallel CPU FFT (2 workers)");
    }

    [TestMethod]
    public void CudaWaveformNormalizationLimitsPeakToUnity()
    {
        float[] waveform = [0.25f, -2.0f, 1.0f, 4.0f];

        DemucsSpectrogram.NormalizeWaveformPeak(waveform, 1.0f);

        Assert.AreEqual(0.0625f, waveform[0], 0.000001f);
        Assert.AreEqual(-0.5f, waveform[1], 0.000001f);
        Assert.AreEqual(0.25f, waveform[2], 0.000001f);
        Assert.AreEqual(1.0f, waveform[3], 0.000001f);
        Assert.AreEqual(1.0, waveform.Max(Math.Abs), 0.000001);
    }

    [TestMethod]
    public void FinalStemCleanupRemovesChannelDcAndPreservesStereoBalance()
    {
        float[] waveform = [2.0f, 1.0f, -1.0f, 4.0f];

        DemucsSpectrogram.PrepareFinalStemWaveform(waveform, 2, 1.0f);

        CollectionAssert.AreEqual(
            new[] { 1.0f, -1.0f, -1.0f, 1.0f },
            waveform);
        Assert.AreEqual(0.0, waveform.Where((_, index) => index % 2 == 0).Average(), 0.000001);
        Assert.AreEqual(0.0, waveform.Where((_, index) => index % 2 == 1).Average(), 0.000001);
    }

    [TestMethod]
    public void CuFftManyFramesAndChannelsMatchManagedCpu()
    {
        const int frameCount = 64;
        const int frameLength = 4096;
        Complex[][] source = Enumerable.Range(0, frameCount)
            .Select(frame => Enumerable.Range(0, frameLength)
                .Select(sample => new Complex(
                    Math.Sin((sample + frame * 3.0) * 0.017) * (1.0 + frame % 2 * 0.25),
                    Math.Cos((sample * 2.0 - frame) * 0.011) * 0.05))
                .ToArray())
            .ToArray();
        Complex[][] managedInput = Clone(source);
        Complex[][] cudaInput = Clone(source);
        var managed = new DemucsFourier(new DemucsLoadOptions
        {
            CudaDeviceId = -1,
            UseCudaCuFft = false,
            FourierWorkers = 2
        });
        var cuda = new DemucsFourier(new DemucsLoadOptions
        {
            CudaDeviceId = 0,
            UseCudaCuFft = true,
            FourierWorkers = 2
        });

        managed.Transform(managedInput, inverse: false, CancellationToken.None);
        cuda.Transform(cudaInput, inverse: false, CancellationToken.None);
        AssertFramesClose(managedInput, cudaInput, 0.005, "forward");

        managed.Transform(managedInput, inverse: true, CancellationToken.None);
        cuda.Transform(cudaInput, inverse: true, CancellationToken.None);
        AssertFramesClose(managedInput, cudaInput, 0.00001, "inverse");
        AssertFramesClose(source, cudaInput, 0.00002, "round trip");
        Console.WriteLine(cuda.Backend);
    }

    private static Complex[][] Clone(Complex[][] source) =>
        source.Select(frame => (Complex[])frame.Clone()).ToArray();

    private static void AssertFramesClose(
        Complex[][] expected,
        Complex[][] actual,
        double tolerance,
        string transform)
    {
        Assert.AreEqual(expected.Length, actual.Length);
        for (int frame = 0; frame < expected.Length; frame++)
        {
            Assert.AreEqual(expected[frame].Length, actual[frame].Length);
            for (int sample = 0; sample < expected[frame].Length; sample++)
            {
                Complex error = expected[frame][sample] - actual[frame][sample];
                Assert.IsTrue(
                    error.Magnitude <= tolerance,
                    $"{transform}: frame {frame}, sample {sample}, expected {expected[frame][sample]}, actual {actual[frame][sample]}.");
            }
        }
    }

    [STATestMethod]
    public void DialogConstructsWithoutLoadingModelAndExposesLoadRunAndWorkers()
    {
        using var audio = new AudioObj();
        using var dialog = new OnnxDemucsDialog(audio);
        object? Field(string name) => typeof(OnnxDemucsDialog).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog);
        Assert.IsNull(Field("Onnx"));
        Assert.AreEqual(0m, ((NumericUpDown)Field("keepModelLoaded")!).Value);
        Assert.IsTrue(((NumericUpDown)Field("keepModelLoaded")!).Enabled);
        Assert.AreEqual("Load model", ((Button)Field("load")!).Text);
        Assert.IsFalse(((Button)Field("button_run")!).Enabled);
        Assert.IsTrue(((ComboBox)Field("optimization")!).Enabled);
        Assert.AreEqual("DirectML", ((ComboBox)Field("provider")!).Text);
        Assert.AreEqual(Microsoft.ML.OnnxRuntime.GraphOptimizationLevel.ORT_ENABLE_BASIC.ToString(),
            ((ComboBox)Field("optimization")!).Text);
        Assert.IsTrue(((NumericUpDown)Field("workers")!).Minimum >= 1);
        Assert.AreEqual((decimal)Environment.ProcessorCount, ((NumericUpDown)Field("workers")!).Maximum);
        Assert.AreEqual((decimal)Environment.ProcessorCount, ((NumericUpDown)Field("intraThreads")!).Maximum);
        Assert.AreEqual((decimal)Environment.ProcessorCount, ((NumericUpDown)Field("interThreads")!).Maximum);
        dialog.ShowInTaskbar = false;
        dialog.CreateControl();
        dialog.PerformLayout();
        Assert.IsTrue(dialog.ClientSize.Width >= 780);
        Assert.IsTrue(dialog.ClientSize.Height >= 800);
        var options = (TableLayoutPanel)Field("optionsPanel")!;
        ((Label)Field("status")!).Text = "Ready — DirectML (device 0)\nCUDA 12 CuFFT (device 0)\nSelect stems to extract.";
        var devices = (CheckedListBox)Field("checkedListBox_directMLDevices")!;
        devices.Items.Add("DirectML 1 — A graphics adapter with an intentionally very long descriptive name that must never expand the form layout");
        devices.SetItemChecked(0, true);
        ((TextBox)Field("optimizedPath")!).Text = @"C:\An intentionally long model directory\with additional subdirectories\optimized-demucs-model.onnx";
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
            var footer = (TableLayoutPanel)root.GetControlFromPosition(0, 4)!;
            var progress = (ProgressBar)Field("progressBar_inferencing")!;
            Assert.IsTrue(progress.Right <= footer.ClientSize.Width, "Progress bar must dock inside the footer.");
            var modelRow = (TableLayoutPanel)root.GetControlFromPosition(0, 0)!;
            Assert.IsTrue(((Button)Field("browse")!).Right <= modelRow.ClientSize.Width, "Browse button must remain visible.");
            Assert.IsTrue(((CheckedListBox)Field("checkedListBox_stems")!).Right <= root.ClientSize.Width);
        }
    }

    [STATestMethod]
    public void DialogUsesAnIndependentAudioSnapshot()
    {
        float[] samples = [0.1f, 0.2f, -0.3f, 0.4f];
        var source = new AudioObj
        {
            Name = "Source track",
            OriginalName = "source.wav",
            Data = (float[])samples.Clone(),
            SampleRate = 44100,
            Channels = 2
        };
        using var dialog = new OnnxDemucsDialog(source);
        var snapshot = (AudioObj)typeof(OnnxDemucsDialog)
            .GetField("OriginalAudio", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(dialog)!;

        source.Dispose();

        Assert.AreNotSame(source, snapshot);
        CollectionAssert.AreEqual(samples, snapshot.Data);
        Assert.AreEqual("source.wav", snapshot.OriginalName);
        snapshot.Dispose();
    }

    [STATestMethod]
    public void DemucsDialogOutlivesItsSourceCollection()
    {
        float[] samples = [0.1f, 0.2f, -0.3f, 0.4f];
        using var source = new AudioObj
        {
            Name = "Source track",
            OriginalName = "source.wav",
            Data = (float[])samples.Clone(),
            SampleRate = 44100,
            Channels = 2
        };
        using var owner = new Form();
        using var dialog = new DemucsDialogWithoutInitialization(source);
        var snapshot = (AudioObj)typeof(OnnxDemucsDialog)
            .GetField("OriginalAudio", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(dialog)!;

        owner.Show();
        dialog.Owner = owner;
        dialog.ShowIndependent();
        source.Dispose();
        owner.Close();

        Assert.IsNull(dialog.Owner);
        Assert.IsFalse(dialog.IsDisposed);
        Assert.IsTrue(dialog.Visible);
        CollectionAssert.AreEqual(samples, snapshot.Data);

        typeof(OnnxDemucsDialog)
            .GetField("initializing", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(dialog, true);
        dialog.Close();
    }

    private sealed class DemucsDialogWithoutInitialization(AudioObj audioObj) : OnnxDemucsDialog(audioObj)
    {
        protected override void OnShown(EventArgs e) { }
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
    public void DirectMLOptionsPreserveGraphOptimizationAndDisableUnsupportedModes()
    {
        var options = new DemucsLoadOptions
        {
            UseDirectML = true,
            DisableDirectMLGraphFusion = false,
            GraphOptimization = Microsoft.ML.OnnxRuntime.GraphOptimizationLevel.ORT_ENABLE_EXTENDED,
            ExecutionMode = Microsoft.ML.OnnxRuntime.ExecutionMode.ORT_PARALLEL,
            EnableMemoryPattern = true
        };
        options.NormalizeForProvider(true);
        Assert.AreEqual(Microsoft.ML.OnnxRuntime.GraphOptimizationLevel.ORT_ENABLE_EXTENDED, options.GraphOptimization);
        Assert.AreEqual(Microsoft.ML.OnnxRuntime.ExecutionMode.ORT_SEQUENTIAL, options.ExecutionMode);
        Assert.IsTrue(options.DisableDirectMLGraphFusion,
            "DirectML graph fusion must remain disabled for the Demucs ONNX graph.");
        Assert.IsFalse(options.EnableMemoryPattern);
        Assert.AreEqual(Microsoft.ML.OnnxRuntime.GraphOptimizationLevel.ORT_ENABLE_BASIC, new DemucsLoadOptions().GraphOptimization);

        options.UseDirectML = false;
        options.GraphOptimization = Microsoft.ML.OnnxRuntime.GraphOptimizationLevel.ORT_ENABLE_ALL;
        options.NormalizeForProvider(false);
        Assert.AreEqual(Microsoft.ML.OnnxRuntime.GraphOptimizationLevel.ORT_ENABLE_ALL, options.GraphOptimization);
    }

    [TestMethod]
    public void CpuEffectiveProviderPreservesIndependentCudaCuFftDevice()
    {
        const long luid = 0x12345678;
        var options = new DemucsLoadOptions
        {
            UseDirectML = true,
            UseCudaCuFft = true,
            CudaDeviceId = 1,
            DeviceLuid = luid
        };

        options.ApplyEffectiveProvider(false);

        Assert.IsFalse(options.UseDirectML);
        Assert.IsTrue(options.UseCudaCuFft);
        Assert.AreEqual(1, options.CudaDeviceId);
        Assert.AreEqual(luid, options.DeviceLuid);

        var cpuOnly = new DemucsLoadOptions
        {
            UseDirectML = true,
            UseCudaCuFft = true,
            CudaDeviceId = -1,
            DeviceLuid = luid
        };
        cpuOnly.ApplyEffectiveProvider(false);
        Assert.IsNull(cpuOnly.DeviceLuid);
    }

    [TestMethod]
    public void CuFftDiscoveryUsesAnAvailableToolkitWhenTheNewestHasNoLibrary()
    {
        string root = Path.Combine(Path.GetTempPath(), "demucs-cufft-" + Guid.NewGuid().ToString("N"));
        string cuda13 = Path.Combine(root, "v13.3");
        string cuda12 = Path.Combine(root, "v12.8");
        string libraryPath = Path.Combine(cuda12, "bin", "cufft64_11.dll");
        string cuda13LibraryPath = Path.Combine(cuda13, "bin", "cufft64_11.dll");
        Directory.CreateDirectory(Path.Combine(cuda13, "bin"));
        Directory.CreateDirectory(Path.GetDirectoryName(libraryPath)!);
        File.WriteAllBytes(libraryPath, Array.Empty<byte>());
        try
        {
            Type backend = typeof(DemucsFourier).Assembly.GetType("ModularAudience.Onnx.CudaToolkitFourier")!;
            var findLibrary = backend.GetMethod("FindCuFftLibrary", BindingFlags.Static | BindingFlags.NonPublic)!;
            string? found = (string?)findLibrary.Invoke(null, [new[] { cuda13, cuda12 }]);
            Assert.AreEqual(Path.GetFullPath(libraryPath), found);
            Assert.IsNull(findLibrary.Invoke(null, [new[] { cuda13 }]));

            File.WriteAllBytes(cuda13LibraryPath, Array.Empty<byte>());
            var findToolkits = backend.GetMethod("FindCuFftToolkits", BindingFlags.Static | BindingFlags.NonPublic)!;
            var toolkits = (Array)findToolkits.Invoke(null, [new[] { cuda13, cuda12 }])!;
            CollectionAssert.AreEqual(new[] { 13, 12 }, toolkits.Cast<object>()
                .Select(toolkit => (int)toolkit.GetType().GetProperty("MajorVersion")!.GetValue(toolkit)!).ToArray());
            var resolveToolkit = backend.GetMethod("FindCuFftToolkit", BindingFlags.Static | BindingFlags.NonPublic)!;
            object cuda12Toolkit = resolveToolkit.Invoke(null, [12, new[] { cuda13, cuda12 }])!;
            object cuda13Toolkit = resolveToolkit.Invoke(null, [13, new[] { cuda13, cuda12 }])!;
            Assert.AreEqual(Path.GetFullPath(libraryPath), cuda12Toolkit.GetType().GetProperty("LibraryPath")!.GetValue(cuda12Toolkit));
            Assert.AreEqual(Path.GetFullPath(cuda13LibraryPath), cuda13Toolkit.GetType().GetProperty("LibraryPath")!.GetValue(cuda13Toolkit));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void SelectedStemsAreDistributedEvenlyAcrossDevices()
    {
        int[][] assigned = OnnxDemucsDialog.AssignStemsToDevices(6, 2);
        CollectionAssert.AreEqual(new[] { 0, 2, 4 }, assigned[0]);
        CollectionAssert.AreEqual(new[] { 1, 3, 5 }, assigned[1]);

        int[][] moreDevicesThanStems = OnnxDemucsDialog.AssignStemsToDevices(2, 4);
        CollectionAssert.AreEqual(new[] { 0 }, moreDevicesThanStems[0]);
        CollectionAssert.AreEqual(new[] { 1 }, moreDevicesThanStems[1]);
        Assert.IsEmpty(moreDevicesThanStems[2]);
        Assert.IsEmpty(moreDevicesThanStems[3]);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => OnnxDemucsDialog.AssignStemsToDevices(1, 0));
    }

    [TestMethod]
    public void ModelCacheKeysIncludeDeviceOptionsButNormalizeDictionaryOrder()
    {
        var first = new DemucsLoadOptions { SessionConfigEntries = new() { ["b"] = "2", ["a"] = "1" } };
        var second = new DemucsLoadOptions { SessionConfigEntries = new() { ["a"] = "1", ["b"] = "2" } };
        Assert.AreEqual(DemucsModelCache.Key("test.onnx", first), DemucsModelCache.Key("test.onnx", second));
        second.DeviceId = 1;
        Assert.AreNotEqual(DemucsModelCache.Key("test.onnx", first), DemucsModelCache.Key("test.onnx", second));
    }

    [TestMethod]
    public void ModelSessionKeysIgnoreRuntimeFourierOptions()
    {
        var first = new DemucsLoadOptions
        {
            FourierWorkers = 1,
            ChunkOverlap = 0.25,
            UseCudaCuFft = false,
            CudaDeviceId = -1,
            CudaToolkitVersion = 0
        };
        var second = new DemucsLoadOptions
        {
            FourierWorkers = 8,
            ChunkOverlap = 0.75,
            UseCudaCuFft = true,
            CudaDeviceId = 2,
            CudaToolkitVersion = 13
        };

        Assert.AreNotEqual(DemucsModelCache.Key("test.onnx", first), DemucsModelCache.Key("test.onnx", second));
        Assert.AreEqual(
            DemucsModelCache.SessionKey("test.onnx", first),
            DemucsModelCache.SessionKey("test.onnx", second));
        second.IntraOpThreads = 7;
        Assert.AreNotEqual(
            DemucsModelCache.SessionKey("test.onnx", first),
            DemucsModelCache.SessionKey("test.onnx", second));
    }

    [TestMethod]
    [TestCategory("DemucsIntegration")]
    public void ApplyingRuntimeFourierOptionsKeepsInstalledModelOnline()
    {
        const string model = "D:/Models/Demucs/htdemucs_6s.onnx";
        if (!File.Exists(model)) Assert.Inconclusive("The local Demucs model is not installed.");
        using var service = new OnnxService(loadImmediately: false);
        service.LoadModel(model, new DemucsLoadOptions
        {
            UseDirectML = false,
            UseCudaCuFft = false,
            CudaDeviceId = -1,
            FourierWorkers = 1,
            ChunkOverlap = 0.25
        });
        string sessionKey = DemucsModelCache.SessionKey(model, service.EffectiveOptions!);

        service.ApplyRuntimeFourierOptions(new DemucsLoadOptions
        {
            UseDirectML = false,
            UseCudaCuFft = true,
            CudaDeviceId = -1,
            CudaToolkitVersion = 13,
            FourierWorkers = 3,
            ChunkOverlap = 0.5
        });

        Assert.IsTrue(service.IsOnline);
        Assert.AreEqual(sessionKey, DemucsModelCache.SessionKey(model, service.EffectiveOptions!));
        Assert.AreEqual(3, service.EffectiveOptions!.FourierWorkers);
        Assert.AreEqual(0.5, service.EffectiveOptions.ChunkOverlap);
    }

    [TestMethod]
    [TestCategory("DemucsIntegration")]
    public async Task IdleModelIsReusedExclusivelyAndUnloadedAfterTimeout()
    {
        const string model = "D:/Models/Demucs/htdemucs_6s.onnx";
        if (!File.Exists(model)) Assert.Inconclusive("The local Demucs model is not installed.");
        var service = new OnnxService(loadImmediately: false);
        service.LoadModel(model, new DemucsLoadOptions { UseDirectML = false });
        string key = DemucsModelCache.Key(model, service.EffectiveOptions!);
        try
        {
            DemucsModelCache.Return(key, service, TimeSpan.FromSeconds(5));
            var reused = DemucsModelCache.Take(key);
            Assert.AreSame(service, reused);
            Assert.IsNull(DemucsModelCache.Take(key), "An active dialog must exclusively own its session.");
            await Task.Delay(60);
            Assert.IsTrue(service.IsOnline, "Taken sessions cannot be expired by the old timer.");
            DemucsModelCache.Return(key, service, TimeSpan.FromMilliseconds(50));
            for (int i = 0; i < 100 && service.IsOnline; i++) await Task.Delay(20);
            Assert.IsFalse(service.IsOnline, "Idle expiry must dispose the native ONNX session.");
            Assert.IsNull(DemucsModelCache.Take(key));
        }
        finally { DemucsModelCache.Clear(); service.Dispose(); }
    }

    [TestMethod]
    [TestCategory("DemucsIntegration")]
    public void ZeroRetentionUnloadsModelImmediately()
    {
        const string model = "D:/Models/Demucs/htdemucs_6s.onnx";
        if (!File.Exists(model)) Assert.Inconclusive("The local Demucs model is not installed.");
        using var service = new OnnxService(loadImmediately: false);
        service.LoadModel(model, new DemucsLoadOptions { UseDirectML = false });
        string key = DemucsModelCache.Key(model, service.EffectiveOptions!);
        DemucsModelCache.Return(key, service, TimeSpan.Zero);
        Assert.IsFalse(service.IsOnline);
        Assert.IsNull(DemucsModelCache.Take(key));
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
        string initializedProvider = service.ExecutionProvider;
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
        Assert.AreEqual(initializedProvider, service.ExecutionProvider, "Inference must keep the provider initialized during loading.");
        Assert.IsTrue(service.IsOnline, "Inference must not dispose the initialized ONNX session.");
        Assert.HasCount(6, stems);
        var reconstructed = new double[original.Length];
        foreach (var (name, samples) in stems)
        {
            Assert.HasCount(original.Length, samples);
            Assert.IsTrue(samples.All(float.IsFinite), name);
            double rms = Math.Sqrt(samples.Sum(v => (double)v * v) / samples.Length);
            Assert.IsTrue(rms > 1e-8 && rms < 1, $"{name}: RMS {rms}");
            double leftRms = Math.Sqrt(samples.Where((_, index) => index % 2 == 0)
                .Sum(value => (double)value * value) / (samples.Length / 2.0));
            double rightRms = Math.Sqrt(samples.Where((_, index) => index % 2 == 1)
                .Sum(value => (double)value * value) / (samples.Length / 2.0));
            Assert.IsTrue(leftRms > 1e-10, $"{name}: left channel is silent.");
            Assert.IsTrue(rightRms > 1e-10, $"{name}: right channel is silent.");
            Console.WriteLine(FormattableString.Invariant(
                $"{name}: rms={rms:F8}, left={leftRms:F8}, right={rightRms:F8}, peak={samples.Max(Math.Abs):F8}"));
            for (int index = 0; index < samples.Length; index++)
            {
                reconstructed[index] += samples[index];
            }
        }
        double leftError = 0, leftEnergy = 0, rightError = 0, rightEnergy = 0;
        for (int index = 0; index < original.Length; index += 2)
        {
            double leftDelta = reconstructed[index] - original[index];
            double rightDelta = reconstructed[index + 1] - original[index + 1];
            leftError += leftDelta * leftDelta;
            leftEnergy += (double)original[index] * original[index];
            rightError += rightDelta * rightDelta;
            rightEnergy += (double)original[index + 1] * original[index + 1];
        }
        double leftRelativeError = Math.Sqrt(leftError / Math.Max(leftEnergy, 1e-12));
        double rightRelativeError = Math.Sqrt(rightError / Math.Max(rightEnergy, 1e-12));
        Console.WriteLine(FormattableString.Invariant(
            $"reconstruction: leftRel={leftRelativeError:F6}, rightRel={rightRelativeError:F6}"));
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
    public async Task ResourceAudioKeepsInterleavedStereoChannels()
    {
        const string model = "D:/Models/Demucs/htdemucs_6s.onnx";
        string audioPath = Path.Combine(
            AppContext.BaseDirectory,
            "Resources",
            "gimme what you got sample.wav");
        if (!File.Exists(model)) Assert.Inconclusive("The local Demucs model is not installed.");
        if (!File.Exists(audioPath)) Assert.Inconclusive("The local audio fixture is not available.");
        using var service = new OnnxService(loadImmediately: false);
        service.LoadModel(model, new DemucsLoadOptions
        {
            UseDirectML = false,
            UseCudaCuFft = false,
            ChunkOverlap = 0.25,
            GraphOptimization = Microsoft.ML.OnnxRuntime.GraphOptimizationLevel.ORT_ENABLE_BASIC
        });
        using AudioObj? source = await AudioObj.FromFileAsync(audioPath);
        Assert.IsNotNull(source);
        float[] original = (float[])source.Data.Clone();
        float[] result = (await service.ExtractStemsAsync(source, ["other"]))["other"];

        Assert.HasCount(source.Data.Length, result);
        double[] channelRms = new double[source.Channels];
        for (int channel = 0; channel < source.Channels; channel++)
        {
            int frames = result.Length / source.Channels;
            for (int frame = 0; frame < frames; frame++)
            {
                float sample = result[frame * source.Channels + channel];
                channelRms[channel] += (double)sample * sample;
            }

            channelRms[channel] = Math.Sqrt(channelRms[channel] / frames);
        }

        foreach (double rms in channelRms)
        {
            Assert.IsTrue(rms > 1e-10);
        }

        Console.WriteLine(
            $"source: {source.SampleRate} Hz, {source.Channels} ch, {result.Length} samples; " +
            $"other rms=[{string.Join(", ", channelRms.Select(value => value.ToString("F8")))}], peak={result.Max(Math.Abs):F8}");
        CollectionAssert.AreEqual(original, source.Data);
    }

    [TestMethod]
    [TestCategory("DemucsIntegration")]
    public async Task DirectMlRealAudioWithGraphFusionDisabledMatchesCpuOutput()
    {
        const string model = "D:/Models/Demucs/htdemucs_6s.onnx";
        string audioPath = Path.Combine(
            AppContext.BaseDirectory,
            "Resources",
            "gimme what you got sample.wav");
        if (!File.Exists(model)) Assert.Inconclusive("The local Demucs model is not installed.");
        if (!File.Exists(audioPath)) Assert.Inconclusive("The local audio fixture is not available.");
        using AudioObj? source = await AudioObj.FromFileAsync(audioPath);
        Assert.IsNotNull(source);
        using var cpu = new OnnxService(loadImmediately: false);
        using var directMl = new OnnxService(loadImmediately: false);
        cpu.LoadModel(model, new DemucsLoadOptions
        {
            UseDirectML = false,
            UseCudaCuFft = false,
            ChunkOverlap = 0.25
        });
        directMl.LoadModel(model, new DemucsLoadOptions
        {
            UseDirectML = true,
            DeviceId = 0,
            AllowCpuFallback = true,
            DisableDirectMLGraphFusion = true,
            UseCudaCuFft = false,
            ChunkOverlap = 0.25
        });
        float[] cpuResult = (await cpu.ExtractStemsAsync(source, ["other"]))["other"];
        float[] directMlResult = (await directMl.ExtractStemsAsync(source, ["other"]))["other"];

        double energy = 0, error = 0;
        for (int index = 0; index < cpuResult.Length; index++)
        {
            double delta = directMlResult[index] - cpuResult[index];
            energy += (double)cpuResult[index] * cpuResult[index];
            error += delta * delta;
        }

        double relativeError = Math.Sqrt(error / Math.Max(energy, 1e-12));
        Console.WriteLine(
            $"providers=[{cpu.ExecutionProvider}, {directMl.ExecutionProvider}], warning={directMl.LoadWarning ?? "<none>"}, " +
            $"relativeError={relativeError:F8}, cpuRms={Math.Sqrt(energy / cpuResult.Length):F8}, " +
            $"dmlL={RmsForChannel(directMlResult, 2, 0):F8}, dmlR={RmsForChannel(directMlResult, 2, 1):F8}");
        Assert.HasCount(cpuResult.Length, directMlResult);
        Assert.IsTrue(relativeError < 0.05,
            $"DirectML output differs from CPU by {relativeError:P2}: {directMl.LoadWarning}");
        Assert.IsTrue(cpuResult.All(float.IsFinite));
        Assert.IsTrue(directMlResult.All(float.IsFinite));
    }

    [TestMethod]
    [TestCategory("DemucsIntegration")]
    public async Task OverlappingChunksPreserveStereoAndBoundAmplitude()
    {
        const string model = "D:/Models/Demucs/htdemucs_6s.onnx";
        string audioPath = Path.Combine(
            AppContext.BaseDirectory,
            "Resources",
            "gimme what you got sample.wav");
        if (!File.Exists(model)) Assert.Inconclusive("The local Demucs model is not installed.");
        if (!File.Exists(audioPath)) Assert.Inconclusive("The local audio fixture is not available.");
        using var service = new OnnxService(loadImmediately: false);
        service.LoadModel(model, new DemucsLoadOptions
        {
            UseDirectML = false,
            UseCudaCuFft = false,
            ChunkOverlap = 0.5
        });
        using AudioObj? fixture = await AudioObj.FromFileAsync(audioPath);
        Assert.IsNotNull(fixture);
        const int repeatCount = 4;
        float[] repeated = new float[fixture.Data.Length * repeatCount];
        for (int index = 0; index < repeatCount; index++)
        {
            fixture.Data.CopyTo(repeated, index * fixture.Data.Length);
        }

        using var source = new AudioObj
        {
            Name = fixture.Name,
            OriginalName = fixture.OriginalName,
            Data = repeated,
            SampleRate = fixture.SampleRate,
            Channels = fixture.Channels,
            BitDepth = fixture.BitDepth,
            Length = repeated.LongLength,
            Duration = TimeSpan.FromSeconds(
                repeated.LongLength / (double)(fixture.SampleRate * fixture.Channels))
        };
        float[] result = (await service.ExtractStemsAsync(source, ["other"]))["other"];

        Assert.HasCount(source.Data.Length, result);
        Assert.IsTrue(result.All(float.IsFinite));
        double leftRms = RmsForChannel(result, 2, 0);
        double rightRms = RmsForChannel(result, 2, 1);
        Assert.IsTrue(leftRms > 1e-6);
        Assert.IsTrue(rightRms > 1e-6);
        double seamFrame = OnnxService.ChunkSizeSeconds * source.SampleRate * source.Channels * 0.5;
        int seamSample = (int)Math.Round(seamFrame);
        int seamWindow = source.SampleRate * source.Channels / 4;
        float seamPeak = result.Skip(Math.Max(0, seamSample - seamWindow / 2))
            .Take(seamWindow)
            .Max(Math.Abs);
        Assert.IsTrue(result.Max(Math.Abs) <= 1.0f,
            $"Final stem peak must be normalized: {result.Max(Math.Abs):F8}.");
        Console.WriteLine(FormattableString.Invariant(
            $"chunks: left={leftRms:F8}, right={rightRms:F8}, peak={result.Max(Math.Abs):F8}, seamPeak={seamPeak:F8}"));
    }

    [TestMethod]
    [TestCategory("DemucsIntegration")]
    [DataRow(32000)]
    [DataRow(48000)]
    public async Task Non44100AudioKeepsInterleavedStereoChannelsAfterRoundTrip(int sampleRate)
    {
        const string model = "D:/Models/Demucs/htdemucs_6s.onnx";
        if (!File.Exists(model)) Assert.Inconclusive("The local Demucs model is not installed.");
        using var service = new OnnxService(loadImmediately: false);
        service.LoadModel(model, new DemucsLoadOptions
        {
            UseDirectML = false,
            UseCudaCuFft = false,
            ChunkOverlap = 0.25
        });
        var data = new float[sampleRate * 2];
        for (int frame = 0; frame < sampleRate; frame++)
        {
            data[frame * 2] = (float)(0.2 * Math.Sin(2 * Math.PI * 110 * frame / sampleRate));
            data[frame * 2 + 1] = (float)(0.12 * Math.Sin(2 * Math.PI * 330 * frame / sampleRate));
        }

        using var source = new AudioObj
        {
            Data = data,
            SampleRate = sampleRate,
            Channels = 2,
            Length = data.LongLength
        };
        float[] result = (await service.ExtractStemsAsync(source, ["bass"]))["bass"];

        Assert.HasCount(source.Data.Length, result);
        double leftRms = RmsForChannel(result, 2, 0);
        double rightRms = RmsForChannel(result, 2, 1);
        Assert.IsTrue(leftRms > 1e-10);
        Assert.IsTrue(rightRms > 1e-10);
        Assert.AreNotEqual(leftRms, rightRms, 1e-6,
            "Different source channels must not collapse into the same interleaved stream.");
        Console.WriteLine(FormattableString.Invariant(
            $"{sampleRate}: left={leftRms:F8}, right={rightRms:F8}, peak={result.Max(Math.Abs):F8}"));
    }

    private static double RmsForChannel(float[] samples, int channels, int channel)
    {
        double sum = 0;
        int count = 0;
        for (int index = channel; index < samples.Length; index += channels)
        {
            sum += (double)samples[index] * samples[index];
            count++;
        }

        return Math.Sqrt(sum / Math.Max(1, count));
    }

    [TestMethod]
    [TestCategory("DemucsIntegration")]
    public void FailedDirectMLDeviceReportsEffectiveCpuOptions()
    {
        const string model = "D:/Models/Demucs/htdemucs_6s.onnx";
        if (!File.Exists(model)) Assert.Inconclusive("The local Demucs model is not installed.");
        using var service = new OnnxService(loadImmediately: false);
        const long luid = 0x12345678;
        var requested = new DemucsLoadOptions
        {
            DeviceId = int.MaxValue,
            AllowCpuFallback = true,
            IntraOpThreads = 2,
            UseCudaCuFft = true,
            CudaDeviceId = 1,
            DeviceLuid = luid
        };
        service.LoadModel(model, requested);
        Assert.AreEqual("CPU", service.ExecutionProvider);
        Assert.IsFalse(service.EffectiveOptions!.UseDirectML);
        Assert.AreEqual(1, service.EffectiveOptions.CudaDeviceId);
        Assert.AreEqual(luid, service.EffectiveOptions.DeviceLuid);
        Assert.AreEqual(2, service.EffectiveOptions.IntraOpThreads);
        Assert.IsTrue(requested.UseDirectML, "Reporting effective settings must not mutate the caller's requested settings.");
        StringAssert.Contains(service.LoadWarning!, "DirectML could not initialize");
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
