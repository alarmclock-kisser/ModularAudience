using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Forms;
using ModularAudience.Onnx;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class DemucsSharedModelTests
{
    [STATestMethod]
    public void BrowseStartsAtSelectedModelFolderOtherwiseConfiguredDemucsDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "demucs-browse-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var audio = new AudioObj();
            using var dialog = new ModularAudience.Forms.OnnxDemucsDialog(audio);
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            Type type = dialog.GetType();
            object settings = type.GetField("settings", flags)!.GetValue(dialog)!;
            settings.GetType().GetProperty("ModelDirectories")!.SetValue(settings, new List<string> { directory });
            string Browse() => (string)type.GetMethod("ModelsBrowseDirectory", flags)!.Invoke(dialog, null)!;
            Assert.AreEqual(directory, Browse());
            string selectedDirectory = Path.Combine(directory, "Selected"); Directory.CreateDirectory(selectedDirectory);
            Type itemType = type.GetNestedType("ModelItem", System.Reflection.BindingFlags.NonPublic)!;
            object item = Activator.CreateInstance(itemType, [Path.Combine(selectedDirectory, "model.onnx")])!;
            var combo = (ComboBox)type.GetField("comboBox_models", flags)!.GetValue(dialog)!;
            combo.Items.Add(item); combo.SelectedIndex = 0;
            Assert.AreEqual(selectedDirectory, Browse());
        }
        finally { Directory.Delete(directory, true); }
    }
    private sealed class Model : IDisposable
    {
        public int Disposals;
        public void Dispose() => Interlocked.Increment(ref Disposals);
    }
    [TestMethod]
    public async Task ConcurrentLoadsShareOneModelAndIdleExpiryCannotDisposeActiveUsers()
    {
        var pool = new SharedModelPool<Model>(); int loads = 0;
        Model Load() { Interlocked.Increment(ref loads); Thread.Sleep(30); return new(); }
        var leases = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => pool.AcquireAsync("model", "options", Load)));
        Assert.AreEqual(1, loads);
        var model = leases[0]!.Model;
        foreach (var lease in leases) Assert.AreSame(model, lease!.Model);
        leases[0]!.AddAlias("effective-options");
        using (var alias = await pool.AcquireAsync("model", "effective-options", null)) Assert.AreSame(model, alias!.Model);
        foreach (var lease in leases.Take(11)) { lease!.KeepAlive = TimeSpan.Zero; lease.Dispose(); }
        pool.ClearIdle(); Assert.AreEqual(0, model.Disposals);
        leases[11]!.KeepAlive = TimeSpan.FromMilliseconds(80); leases[11]!.Dispose();
        using (var reused = await pool.AcquireAsync("model", "options", null))
        {
            Assert.AreSame(model, reused!.Model);
            Assert.IsTrue(pool.AnyLoaded((_, _) => true));
            await Task.Delay(130);
            Assert.AreEqual(0, model.Disposals);
            reused.KeepAlive = TimeSpan.Zero;
        }
        Assert.AreEqual(1, model.Disposals);
        Assert.IsFalse(pool.AnyLoaded((_, _) => true));
        Assert.IsNull(await pool.AcquireAsync("model", "options", null));
    }
    [TestMethod]
    public async Task ChangedOptionsNeverLoadASecondCopyWhileActiveAndReplaceIdleBeforeLoading()
    {
        var pool = new SharedModelPool<Model>(); var first = new Model();
        var lease = await pool.AcquireAsync("model", "a", () => first);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () => await pool.AcquireAsync("model", "b", () => new Model()));
        Assert.AreEqual(0, first.Disposals);
        lease!.Dispose();
        var replacement = await pool.AcquireAsync("model", "b", () => { Assert.AreEqual(1, first.Disposals); return new Model(); });
        replacement!.KeepAlive = TimeSpan.Zero; replacement.Dispose();
    }
    [TestMethod]
    public async Task FailedLoadCanBeRetriedAndRetentionExpiresExactlyOnce()
    {
        var pool = new SharedModelPool<Model>();
        await Assert.ThrowsExactlyAsync<IOException>(async () => await pool.AcquireAsync("model", "a", () => throw new IOException("failed")));
        var model = new Model(); var lease = await pool.AcquireAsync("model", "a", () => model);
        lease!.KeepAlive = TimeSpan.FromMilliseconds(30); lease.Dispose(); lease.Dispose();
        for (int i = 0; i < 100 && model.Disposals == 0; i++) await Task.Delay(10);
        Assert.AreEqual(1, model.Disposals); pool.ClearIdle(); Assert.AreEqual(1, model.Disposals);
    }
    [TestMethod]
    public void DirectMLCacheKeyPreservesGraphOptimizationButNormalizesUnsupportedSessionOptions()
    {
        var first = new DemucsLoadOptions
        {
            UseDirectML = true, EnableMemoryPattern = true,
            ExecutionMode = Microsoft.ML.OnnxRuntime.ExecutionMode.ORT_PARALLEL,
            GraphOptimization = Microsoft.ML.OnnxRuntime.GraphOptimizationLevel.ORT_ENABLE_ALL
        };
        var effective = new DemucsLoadOptions
        {
            UseDirectML = true, EnableMemoryPattern = false, ExecutionMode = Microsoft.ML.OnnxRuntime.ExecutionMode.ORT_SEQUENTIAL,
            GraphOptimization = Microsoft.ML.OnnxRuntime.GraphOptimizationLevel.ORT_ENABLE_ALL
        };
        Assert.AreEqual(DemucsModelCache.Key("test.onnx", first), DemucsModelCache.Key("test.onnx", effective));
        effective.GraphOptimization = Microsoft.ML.OnnxRuntime.GraphOptimizationLevel.ORT_ENABLE_BASIC;
        Assert.AreNotEqual(DemucsModelCache.Key("test.onnx", first), DemucsModelCache.Key("test.onnx", effective));
    }

    [TestMethod]
    public void DemucsSettingsRoundTripRestoresRetentionDevicesCuFftAndGraphOptimization()
    {
        string directory = Path.Combine(Path.GetTempPath(), "demucs-settings-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "settings.json");
        try
        {
            var settings = new DemucsDialogSettings
            {
                ModelDirectories = [directory],
                SelectedModel = Path.Combine(directory, "htdemucs.onnx"),
                KeepModelLoadedSeconds = 135,
                SelectedDirectMLDeviceIds = [1, 3],
                SelectedStems = ["vocals", "drums"],
                Options = new DemucsLoadOptions
                {
                    UseDirectML = true,
                    GraphOptimization = Microsoft.ML.OnnxRuntime.GraphOptimizationLevel.ORT_ENABLE_EXTENDED,
                    UseCudaCuFft = true,
                    CudaToolkitVersion = 13,
                    CudaDeviceId = 2,
                    DeviceLuid = 0x12345678
                }
            };
            settings.Save(path);

            var restored = DemucsDialogSettings.Load(path);
            Assert.AreEqual(135, restored.KeepModelLoadedSeconds);
            CollectionAssert.AreEqual(new[] { 1, 3 }, restored.SelectedDirectMLDeviceIds);
            CollectionAssert.AreEqual(new[] { "vocals", "drums" }, restored.SelectedStems);
            Assert.AreEqual(Microsoft.ML.OnnxRuntime.GraphOptimizationLevel.ORT_ENABLE_EXTENDED, restored.Options.GraphOptimization);
            Assert.IsTrue(restored.Options.UseCudaCuFft);
            Assert.AreEqual(13, restored.Options.CudaToolkitVersion);
            Assert.AreEqual(0x12345678L, restored.Options.DeviceLuid);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public async Task DirectMLLeasesHonorConfiguredIdleRetention()
    {
        var pool = new SharedModelPool<OnnxService>();
        var service = new OnnxService(loadImmediately: false);
        typeof(OnnxService).GetProperty(nameof(OnnxService.EffectiveOptions))!
            .GetSetMethod(nonPublic: true)!.Invoke(service, [new DemucsLoadOptions { UseDirectML = true }]);
        var lease = await pool.AcquireAsync("directml-resource", "directml-key", () => service);

        OnnxDemucsDialog.ReleaseLeases([lease!], TimeSpan.FromMilliseconds(250));
        Assert.IsTrue(pool.AnyLoaded((_, _) => true), "DirectML sessions must remain cached for the configured close grace period.");
        await Task.Delay(350);
        Assert.IsFalse(pool.AnyLoaded((_, _) => true), "The idle timer must dispose the retained session.");
    }

    [TestMethod]
    public void CudaDeviceMapsToDirectMLOnlyByMatchingPhysicalLuid()
    {
        var expected = new DirectMLDevices.Adapter(3, "NVIDIA GPU", 0x12345678);
        var other = new DirectMLDevices.Adapter(1, "Other GPU", 0x87654321);

        Assert.AreSame(expected, DirectMLDevices.MatchCudaDevice(
            new DemucsCudaDevice(2, "NVIDIA GPU", expected.Luid), [other, expected]));
        Assert.IsNull(DirectMLDevices.MatchCudaDevice(
            new DemucsCudaDevice(0, "Unknown GPU", null), [expected]));
        Assert.IsNull(DirectMLDevices.MatchCudaDevice(
            new DemucsCudaDevice(1, "Different GPU", other.Luid), [expected]));
    }
}
