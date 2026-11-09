using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio;
using ModularAudience.Forms;
using ModularAudience.Forms.Effects;
using ModularAudience.Forms.Modules.Dialogs;
using ModularAudience.Forms.Quantum;
using ModularAudience.Quantum.Models;
using ModularAudience.Quantum.Services;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class QuantumBeatEngineTests
{
    private static QuantumStudioWorkspace Workspace(bool autoSave = false, bool useIbm = false) => new(
        new(1, QuantumCircuitComposer.CreatePreset("Bell break", 4), new QuantumBeatOptions(),
            [new("Kick", 0), new("Snare", 1)], new QuantumCircuitRandomization()), AutoSave: autoSave, AutoRender: false, UseIbm: useIbm);

    [TestMethod]
    public void Persistence_UsesVersionedJsonAndWindowsDpapiWithoutPlaintextCredentials()
    {
        using var scope = new StoreScope();
        const string secret = "TEST_SECRET_DO_NOT_WRITE_PLAINTEXT";
        var settings = new QuantumConnectionSettings { ProfileName = "Studio", Secret = secret, Backend = "ibm_test", ServiceCrn = "crn:test", Authentication = QuantumAuthenticationMode.IamBearerToken };
        scope.Store.SaveConnection(settings);
        var loaded = scope.Store.LoadConnection("Studio");
        Assert.AreEqual(secret, loaded.Secret);
        Assert.AreEqual(settings.Authentication, loaded.Authentication);
        Assert.IsFalse(settings.ToString().Contains(secret, StringComparison.Ordinal));
        string json = File.ReadAllText(Path.Combine(scope.Root, "Connections", "connection-Studio.json"));
        Assert.IsFalse(json.Contains(secret, StringComparison.Ordinal));
        StringAssert.Contains(json, "dpapi:v1:");
        using var document = JsonDocument.Parse(json);
        Assert.IsFalse(document.RootElement.GetProperty("Settings").TryGetProperty("Secret", out _));
        scope.Store.SaveWorkspace(Workspace() with { ConnectionProfile = "Studio" });
        scope.Store.SaveExperiment("Amen", Workspace());
        foreach (string file in Directory.GetFiles(scope.Root, "*.json", SearchOption.AllDirectories))
            Assert.IsFalse(File.ReadAllText(file).Contains(secret, StringComparison.Ordinal));
        Assert.AreEqual(0, Directory.GetFiles(scope.Root, "*.tmp", SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public void Persistence_RoundTripsAutoSaveAndSupportsSaveLoadRenameDelete()
    {
        using var scope = new StoreScope();
        Assert.IsNull(scope.Store.LoadWorkspace());
        scope.Store.SaveWorkspace(Workspace(autoSave: false));
        Assert.IsFalse(scope.Store.LoadWorkspace()!.AutoSave);
        scope.Store.SaveExperiment("First", Workspace());
        scope.Store.RenameExperiment("First", "Renamed");
        CollectionAssert.AreEqual(new[] { "Renamed" }, scope.Store.ExperimentNames());
        Assert.AreEqual("Renamed", scope.Store.LoadExperiment("Renamed").LoadedExperimentName);
        scope.Store.SaveExperiment("Existing", Workspace());
        Assert.ThrowsExactly<IOException>(() => scope.Store.RenameExperiment("Renamed", "Existing"));
        scope.Store.DeleteExperiment("Renamed");
        CollectionAssert.AreEqual(new[] { "Existing" }, scope.Store.ExperimentNames());
        Assert.IsNotNull(scope.Store.LoadWorkspace());
        Assert.ThrowsExactly<ArgumentException>(() => scope.Store.SaveExperiment("../escape", Workspace()));
        Assert.ThrowsExactly<ArgumentException>(() => scope.Store.SaveConnection(new() { ProfileName = "..", Secret = "test" }));
        Assert.ThrowsExactly<CryptographicException>(() => new WindowsQuantumSecretProtector().Unprotect("plaintext"));
    }

    [TestMethod]
    public void Persistence_PreservesCorruptWorkspaceAndWritesSeparateRecovery()
    {
        using var scope = new StoreScope();
        Directory.CreateDirectory(scope.Root);
        string path = Path.Combine(scope.Root, "workspace.json"); File.WriteAllText(path, "{broken");
        Assert.ThrowsExactly<JsonException>(() => scope.Store.LoadWorkspace());
        scope.Store.SaveRecoveryWorkspace(Workspace(), Guid.NewGuid());
        Assert.AreEqual("{broken", File.ReadAllText(path));
        Assert.AreEqual(1, Directory.GetFiles(Path.Combine(scope.Root, "Recovery"), "*.json").Length);
    }

    [TestMethod]
    public async Task Renderer_CutsOnFramesResamplesMixesVelocityAndPreservesSource()
    {
        using var source = new AudioObj { Data = Enumerable.Range(0, 16).Select(index => index / 20f).ToArray(), Channels = 2, SampleRate = 1000 };
        float[] original = source.Data.ToArray();
        var options = new QuantumBeatOptions { Bars = 1, StepsPerBar = 4, NormalizeOutput = false, TruncateNotes = false };
        var sequence = new QuantumBeatSequence("qasm", [new(0, 0, 1, 0.5, 0, 1, 1, 2, false, false, "1001")],
            new Dictionary<string, int> { ["1001"] = 1 }, TimeSpan.Zero, options, [new("Kick", 0)], 4);
        using var rendered = await QuantumSequenceAudioRenderer.RenderAsync(sequence, [source], CancellationToken.None);
        Assert.AreEqual(44100, rendered.SampleRate); Assert.AreEqual(2, rendered.Channels);
        Assert.AreEqual(0.1f, rendered.Data[0], 1e-6f);
        Assert.AreEqual(2, rendered.Duration.TotalSeconds, 1e-6);
        CollectionAssert.AreEqual(original, source.Data);
    }

    [TestMethod]
    public async Task Renderer_SupportsCombinedDspFitTailsAndCancellation()
    {
        using var source = new AudioObj { Data = Enumerable.Range(0, 4410).Select(i => (float)(0.25 * Math.Sin(i * 0.1))).ToArray(), Channels = 1, SampleRate = 44100 };
        var options = new QuantumBeatOptions { Bars = 1, StepsPerBar = 4, TruncateNotes = false, FitToNoteDuration = true, KeepEndTails = true };
        var sequence = new QuantumBeatSequence("qasm", [new(0, 3.8, 1, 0.8, 300, 1.5, 0.75, 4, true, false, "1001")],
            new Dictionary<string, int>(), TimeSpan.Zero, options, [new("Kick", 0)], 4);
        using var rendered = await QuantumSequenceAudioRenderer.RenderAsync(sequence, [source], CancellationToken.None);
        Assert.IsTrue(rendered.Duration.TotalSeconds > 2);
        Assert.IsTrue(rendered.Data.All(float.IsFinite));
        Assert.IsTrue(rendered.Data.Max(value => Math.Abs(value)) <= 0.951f);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => QuantumSequenceAudioRenderer.RenderAsync(sequence, [source], cts.Token));
    }

    [STATestMethod]
    public void Designer_IsModelessHasConnectionEditHelpAndRestoresWithoutNetworkOrCloudConsent()
    {
        using var fixture = new Fixture(useIbm: true);
        fixture.Editor.Show();
        Assert.IsFalse(fixture.Editor.Modal);
        CollectionAssert.AreEqual(new[] { "Connection", "Edit", "Circuit", "Pattern", "Output", "Help" }, fixture.Editor.MainMenuStrip!.Items.Cast<ToolStripMenuItem>().Select(item => item.Text!).ToArray());
        var help = fixture.Editor.MainMenuStrip.Items.Cast<ToolStripMenuItem>().Last();
        Assert.IsTrue(help.DropDownItems.Count >= 8);
        Assert.IsFalse(Check(fixture.Editor, "AllowCloudJobs").Checked);
        Assert.IsFalse(Check(fixture.Editor, "AutoSave").Checked);
        Assert.AreEqual(0, fixture.Session.ConnectCalls + fixture.Session.LocalCalls + fixture.Session.CloudCalls);
        Assert.IsTrue(Find<PictureBox>(fixture.Editor, "pictureBox_circuit").Image is not null);
        Assert.IsTrue(Find<TextBox>(fixture.Editor, "textBox_logicalQasm").Text.Contains("cx q[0], q[1];", StringComparison.Ordinal));
        Assert.AreEqual(4m, Number(fixture.Editor, "Qubits").Value);
    }

    [STATestMethod]
    public void Designer_LocalGenerationAndRemappingUseOneExecutionAndExposeEveryTrack()
    {
        using var fixture = new Fixture(); fixture.Editor.Show();
        Drain(fixture.Editor.GenerateAsync());
        Assert.AreEqual(1, fixture.Session.LocalCalls); Assert.AreEqual(0, fixture.Session.CloudCalls);
        Assert.IsNotNull(fixture.Editor.CurrentSequence);
        Assert.AreEqual(2, fixture.Editor.CurrentSequence.Tracks.Count);
        Assert.IsTrue(fixture.Editor.CurrentSequence.Notes.Count > 0);
        Assert.IsTrue(Find<DataGridView>(fixture.Editor, "dataGridView_events").Rows.Count > 0);
        Number(fixture.Editor, "Density").Value = 0;
        Drain(fixture.Editor.RemapAsync());
        Assert.AreEqual(0, fixture.Editor.CurrentSequence!.Notes.Count);
        Assert.AreEqual(1, fixture.Session.LocalCalls);
        Assert.IsTrue(fixture.Sources.All(source => source.Data.All(value => value == 0.25f)));
    }

    [STATestMethod]
    public void Designer_CloudExecutionRequiresConnectionAndFreshExplicitPermission()
    {
        using var fixture = new Fixture(useIbm: true); fixture.Editor.Show();
        Find<TextBox>(fixture.Editor, "textBox_credential").Text = "test-api-key";
        Find<TextBox>(fixture.Editor, "textBox_serviceCrn").Text = "crn:test";
        Drain(fixture.Editor.GenerateAsync()); Assert.AreEqual(0, fixture.Session.CloudCalls);
        Drain(fixture.Editor.ConnectAsync()); Assert.AreEqual(1, fixture.Session.ConnectCalls);
        Assert.IsFalse(Check(fixture.Editor, "AllowCloudJobs").Checked);
        Drain(fixture.Editor.GenerateAsync()); Assert.AreEqual(0, fixture.Session.CloudCalls);
        Check(fixture.Editor, "AllowCloudJobs").Checked = true;
        Drain(fixture.Editor.GenerateAsync()); Assert.AreEqual(1, fixture.Session.CloudCalls);
        Assert.AreEqual(0, fixture.Session.LocalCalls);
        Assert.AreEqual("test-api-key", fixture.Scope.Store.LoadConnection("Default").Secret);
    }

    [STATestMethod]
    public void Designer_ClosingCancelsWorkAndPersistsWorkspaceEvenWhenAutoSaveIsOff()
    {
        using var fixture = new Fixture(); fixture.Editor.Show();
        fixture.Session.Pending = new TaskCompletionSource<QuantumExecutionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        Number(fixture.Editor, "Bpm").Value = 137;
        Task work = fixture.Editor.GenerateAsync();
        Assert.IsTrue(fixture.Editor.IsWorking);
        fixture.Editor.Close(); Drain(work);
        Assert.IsTrue(fixture.Session.LastToken.IsCancellationRequested);
        Assert.AreEqual(137, fixture.Scope.Store.LoadWorkspace()!.Experiment.Options.Bpm);
        Assert.IsFalse(fixture.Scope.Store.LoadWorkspace()!.AutoSave);
    }

    [STATestMethod]
    public void Designer_AutoSaveCheckboxAndNamedExperimentLifecycleArePersistent()
    {
        using var fixture = new Fixture(); fixture.Editor.Show();
        Check(fixture.Editor, "AutoSave").Checked = true;
        Assert.IsTrue(fixture.Scope.Store.LoadWorkspace()!.AutoSave);
        fixture.Editor.SaveNamedExperiment("Break one");
        Number(fixture.Editor, "Bpm").Value = 140;
        fixture.Editor.SaveCurrentWorkspace();
        Assert.AreEqual(140, fixture.Scope.Store.LoadExperiment("Break one").Experiment.Options.Bpm);
        fixture.Editor.RenameNamedExperiment("Break one", "Break two");
        fixture.Editor.LoadNamedExperiment("Break two");
        Assert.AreEqual(140m, Number(fixture.Editor, "Bpm").Value);
        fixture.Editor.DeleteNamedExperiment("Break two");
        Assert.AreEqual(0, fixture.Scope.Store.ExperimentNames().Length);
        var edit = fixture.Editor.MainMenuStrip!.Items.Cast<ToolStripMenuItem>().Single(item => item.Text == "Edit");
        Assert.AreEqual("Reset current setup", edit.DropDownItems[^1].Text);
    }

    [STATestMethod]
    public void CollectionMenu_PlacesQuantumBelowNeuralAndOpensAModelessWorkbench()
    {
        using var scope = new StoreScope();
        using var first = Sample("Kick"); using var second = Sample("Snare");
        using var collection = new AudioCollectionView([first, second], "Quantum menu test");
        QuantumBeatEngineDialog? opened = null;
        collection.QuantumDialogFactory = sources => opened = new(sources, new FakeSession(), scope.Store, new FakePlayer());
        var list = (ListBox)typeof(AudioCollectionView).GetField("listBox_audios", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(collection)!;
        list.SetSelected(0, true); list.SetSelected(1, true); Application.DoEvents(); collection.UpdateContextMenuState();
        var parent = (ToolStripMenuItem)typeof(AudioCollectionView).GetField("menuToolStripItem_generateBreakbeat", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(collection)!;
        int neural = parent.DropDownItems.IndexOfKey("neuralFeedbackEngineToolStripMenuItem");
        var item = (ToolStripMenuItem)parent.DropDownItems[neural + 1];
        Assert.AreEqual("Quantum Beat Engine (IBM®)", item.Text); Assert.IsTrue(item.Enabled);
        item.PerformClick();
        Assert.IsNotNull(opened); Assert.IsFalse(opened.Modal); Assert.IsTrue(collection.Enabled); Assert.AreEqual(2, opened.CaptureWorkspace().Experiment.Tracks.Count);
        opened.Close(); opened.Dispose(); collection.Close();
    }

    [TestMethod]
    public void ErrorMessagesNeverEchoProviderSecretsAndDiagramsHaveUsableDimensions()
    {
        const string secret = "PRIVATE-SECRET";
        Exception[] errors = [new Exception(secret), new InvalidOperationException(secret), new ArgumentException(secret), new IOException(secret),
            new CryptographicException(secret), new JsonException(secret), new HttpRequestException(secret), new TimeoutException(secret)];
        foreach (var error in errors) Assert.IsFalse(QuantumBeatEngineDialog.EnglishError(error).Contains(secret, StringComparison.Ordinal));
        using var circuit = QuantumDiagramRenderer.Circuit(QuantumCircuitComposer.CreatePreset("Bell break", 16));
        using var sequence = QuantumDiagramRenderer.Sequence(null, ["Kick", "Snare"], 16, 64);
        Assert.IsTrue(circuit.Height >= 16 * QuantumDiagramRenderer.WireHeight);
        Assert.IsTrue(sequence.Width <= 8192);
    }

    private static AudioObj Sample(string name)
    {
        var sample = new AudioObj { Data = Enumerable.Repeat(0.25f, 1024).ToArray(), SampleRate = 44100, Channels = 2 }; sample.Rename(name); return sample;
    }
    private static T Find<T>(Control parent, string name) where T : Control => (T)parent.Controls.Find(name, true).Single();
    private static NumericUpDown Number(Control parent, string name) => Find<NumericUpDown>(parent, "numericUpDown_" + name);
    private static CheckBox Check(Control parent, string name) => Find<CheckBox>(parent, "checkBox_" + name);
    private static void Drain(Task task)
    {
        var watch = Stopwatch.StartNew();
        while (!task.IsCompleted && watch.Elapsed < TimeSpan.FromSeconds(15)) { Application.DoEvents(); Thread.Sleep(1); }
        Assert.IsTrue(task.IsCompleted, "The modeless quantum operation did not complete."); task.GetAwaiter().GetResult();
    }
    private sealed class StoreScope : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "ModularAudienceQuantum-" + Guid.NewGuid().ToString("N"));
        internal QuantumStudioStore Store { get; }
        internal StoreScope() => this.Store = new(this.Root);
        public void Dispose() { if (Directory.Exists(this.Root)) Directory.Delete(this.Root, true); }
    }
    private sealed class Fixture : IDisposable
    {
        internal StoreScope Scope { get; } = new(); internal FakeSession Session { get; } = new();
        internal AudioObj[] Sources { get; } = [Sample("Kick"), Sample("Snare")];
        internal QuantumBeatEngineDialog Editor { get; }
        internal Fixture(bool useIbm = false)
        {
            this.Scope.Store.SaveWorkspace(Workspace(useIbm: useIbm));
            this.Editor = new(this.Sources, this.Session, this.Scope.Store, new FakePlayer());
        }
        public void Dispose() { this.Editor.Close(); this.Editor.Dispose(); foreach (var sample in this.Sources) sample.Dispose(); this.Scope.Dispose(); }
    }
    private sealed class FakePlayer : IEffectPreviewPlayer
    {
        public bool IsPlaying { get; private set; }
        public void Play(float[] samples, int sampleRate, int channels, bool loop) => this.IsPlaying = true;
        public void Stop() => this.IsPlaying = false;
        public void Dispose() => this.Stop();
    }
    private sealed class FakeSession : IQuantumBeatSession
    {
        public bool IsConnected { get; private set; }
        public string ConnectedBackend => this.IsConnected ? "ibm_test" : "";
        internal int ConnectCalls, LocalCalls, CloudCalls;
        internal CancellationToken LastToken;
        internal TaskCompletionSource<QuantumExecutionResult>? Pending;
        public Task<IReadOnlyList<QuantumBackendInfo>> ConnectAsync(IbmQuantumOptions options, CancellationToken token)
        { this.ConnectCalls++; this.IsConnected = true; return Task.FromResult<IReadOnlyList<QuantumBackendInfo>>([new("ibm_test", "online", 127, 0, false)]); }
        public void Disconnect() => this.IsConnected = false;
        public async Task<QuantumExecutionResult> ExecuteLocalAsync(QuantumCircuitDefinition circuit, int shots, int? seed, CancellationToken token)
        { this.LocalCalls++; this.LastToken = token; return this.Pending is null ? Result(circuit, shots) : await this.Pending.Task.WaitAsync(token); }
        public Task<QuantumExecutionResult> ExecuteIbmAsync(QuantumCircuitDefinition circuit, int shots, string backend, string pythonExecutable, int optimizationLevel, int seed, string? preparedIsaQasm, CancellationToken token)
        { this.CloudCalls++; this.LastToken = token; return Task.FromResult(Result(circuit, shots)); }
        private static QuantumExecutionResult Result(QuantumCircuitDefinition circuit, int shots) => new(new("test-job",
            new Dictionary<string, int> { ["1001".PadLeft(circuit.QubitCount, '0')] = shots / 2, ["0110".PadLeft(circuit.QubitCount, '0')] = shots - shots / 2 }, TimeSpan.FromMilliseconds(15)),
            QuantumCircuitComposer.Build(circuit), TimeSpan.Zero);
        public void Dispose() => this.Disconnect();
    }
}
