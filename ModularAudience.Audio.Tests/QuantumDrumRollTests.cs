using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio;
using ModularAudience.Forms.Modules;
using ModularAudience.Forms.Quantum;
using ModularAudience.Quantum.Abstractions;
using ModularAudience.Quantum.Models;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class QuantumDrumRollTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [TestMethod]
    public async Task Renderer_CutsOnFrameBoundariesBakesVelocityCachesVariantsAndPreservesSource()
    {
        using var source = new AudioObj { Data = Enumerable.Range(0, 16).Select(i => i / 20f).ToArray(), Channels = 2, SampleRate = 1000 };
        float[] original = source.Data.ToArray();
        var pattern = new QuantumPatternResult("qasm",
        [
            new(2, true, 0.5, 0, 1, 2), new(0, false, 0, 0, 1, 0), new(1, true, 0.5, 0, 1, 2)
        ], new Dictionary<string, int>(), TimeSpan.Zero);
        using var prepared = await QuantumAudioPatternRenderer.PrepareAsync(source, pattern, CancellationToken.None);
        CollectionAssert.AreEqual(new[] { false, true, true }, prepared.Steps);
        Assert.AreSame(prepared.Samples[1], prepared.Samples[2]);
        CollectionAssert.AreEqual(original.Skip(4).Select(value => value * 0.5f).ToArray(), prepared.Samples[1].Data);
        CollectionAssert.AreEqual(original, source.Data);
    }

    [TestMethod]
    public async Task Renderer_AppliesPitchAndDurationStretchBeforePlayback()
    {
        using var source = new AudioObj
        {
            Data = Enumerable.Range(0, 4410).Select(i => (float)(0.25 * Math.Sin(2 * Math.PI * 440 * i / 44100))).ToArray(),
            Channels = 1, SampleRate = 44100
        };
        float[] original = source.Data.ToArray();
        var pattern = new QuantumPatternResult("qasm", [new(0, true, 0.75, 300, 1.5, 5)], new Dictionary<string, int>(), TimeSpan.Zero);
        using var prepared = await QuantumAudioPatternRenderer.PrepareAsync(source, pattern, CancellationToken.None);
        Assert.AreEqual((int)((4410 - 220) * 1.5), prepared.Samples[0].Data.Length);
        Assert.IsTrue(prepared.Samples[0].Data.All(float.IsFinite));
        Assert.AreEqual(44100, prepared.Samples[0].SampleRate);
        Assert.AreEqual(1, prepared.Samples[0].Channels);
        CollectionAssert.AreEqual(original, source.Data);
    }

    [TestMethod]
    public async Task Renderer_RejectsInvalidEventsFormatsAndCancellation()
    {
        using var source = new AudioObj { Data = [0.1f, 0.2f, 0.3f, 0.4f], Channels = 1, SampleRate = 1000 };
        DrumSliceTrigger good = new(0, true, 1, 0, 1, 0);
        DrumSliceTrigger[] invalid =
        [
            good with { StepIndex = -1 }, good with { StepIndex = 1 }, good with { Velocity = 1.1 },
            good with { Velocity = double.NaN }, good with { PitchShiftCents = 1201 }, good with { PitchShiftCents = double.NaN },
            good with { TimeStretchFactor = 0.1 }, good with { TimeStretchFactor = double.PositiveInfinity },
            good with { SliceCutOffsetMs = -1 }, good with { SliceCutOffsetMs = double.NaN }
        ];
        foreach (DrumSliceTrigger slice in invalid)
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => QuantumAudioPatternRenderer.PrepareAsync(source, Pattern([slice]), CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => QuantumAudioPatternRenderer.PrepareAsync(source, Pattern([]), CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => QuantumAudioPatternRenderer.PrepareAsync(source, Pattern([good, good]), CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => QuantumAudioPatternRenderer.PrepareAsync(source, Pattern([null!]), CancellationToken.None));
        source.SampleRate = 0;
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => QuantumAudioPatternRenderer.PrepareAsync(source, Pattern([good]), CancellationToken.None));
        source.SampleRate = 1000; source.Channels = 3;
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => QuantumAudioPatternRenderer.PrepareAsync(source, Pattern([good]), CancellationToken.None));
        source.Channels = 1; source.Data[0] = float.NaN;
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => QuantumAudioPatternRenderer.PrepareAsync(source, Pattern([good]), CancellationToken.None));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => QuantumAudioPatternRenderer.PrepareAsync(source, Pattern([good]), cts.Token));
    }

    [TestMethod]
    public async Task Renderer_ClampsCutsToTheLastCompleteFrameAndSupportsRests()
    {
        using var source = new AudioObj { Data = [0.1f, 0.2f, 0.3f, 0.4f], Channels = 1, SampleRate = 1000 };
        using var cut = await QuantumAudioPatternRenderer.PrepareAsync(source, Pattern([new(0, true, 1, 0, 1, 500)]), CancellationToken.None);
        CollectionAssert.AreEqual(new[] { 0.4f }, cut.Samples[0].Data);
        using var rest = await QuantumAudioPatternRenderer.PrepareAsync(source, Pattern([new(0, false, 0, 0, 1, 0)]), CancellationToken.None);
        Assert.AreEqual(0, rest.Samples.Count);
        CollectionAssert.AreEqual(new[] { false }, rest.Steps);
    }

    [STATestMethod]
    public void DrumRoll_AppliesPreparedStepsToPlaybackResolutionAndExportWithoutEditingSource()
    {
        using var fixture = new Fixture();
        var result = Drain(fixture.Editor.ApplyQuantumPatternToRowAsync(0, 4, 1024, CancellationToken.None));
        CollectionAssert.AreEqual(new[] { 0, 2 }, fixture.Steps.Select((active, step) => (active, step)).Where(item => item.active).Select(item => item.step).ToArray());
        Assert.AreEqual(16, result.Slices.Count);
        AudioObj rendered = fixture.Resolve(0);
        Assert.AreNotSame(fixture.RowAudio, rendered);
        Assert.AreSame(rendered, fixture.Resolve(2));
        Assert.AreEqual(0.125f, rendered.Data[0]);
        using AudioObj export = Drain(fixture.Editor.GenerateSampleAsync());
        float expected = MathF.Tanh(0.125f * 0.8f * 0.92f * 1.1f) * 0.9f;
        Assert.AreEqual(expected, export.Data[0], 1e-6f);
        Assert.IsTrue(fixture.Source.Data.All(value => value == 0.25f));
        Call(fixture.Editor, "ClearQuantumPattern", fixture.RowAudio);
        Assert.AreSame(fixture.RowAudio, fixture.Resolve(0));
        Assert.AreEqual(0, rendered.Data.Length);
    }

    [STATestMethod]
    public void DrumRoll_ExportResamplesPreparedAudioInsteadOfChangingItsPitchAndDuration()
    {
        using var fixture = new Fixture(sampleRate: 48000);
        Drain(fixture.Editor.ApplyQuantumPatternToRowAsync(0, 4, 1024, CancellationToken.None));
        Assert.AreEqual(48000, fixture.Resolve(0).SampleRate);
        using AudioObj export = Drain(fixture.Editor.GenerateSampleAsync());
        Assert.AreEqual(44100, export.SampleRate);
        Assert.IsTrue(export.Data[470 * 2] > 0);
        Assert.AreEqual(0f, export.Data[471 * 2]); // ceil(512 input frames * 44100 / 48000)
    }

    [STATestMethod]
    public void DrumRoll_RespectsLocksRejectsUnknownRowsAndRequiresRegistration()
    {
        using var fixture = new Fixture();
        Call(fixture.Editor, "ToggleRowLock", 0);
        Assert.ThrowsExactly<InvalidOperationException>(() => Drain(fixture.Editor.ApplyQuantumPatternToRowAsync(0, 4, 1024, CancellationToken.None)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Drain(fixture.Editor.ApplyQuantumPatternToRowAsync(1, 4, 1024, CancellationToken.None)));
        using var unregistered = new Fixture(register: false);
        Assert.ThrowsExactly<InvalidOperationException>(() => Drain(unregistered.Editor.ApplyQuantumPatternToRowAsync(0, 4, 1024, CancellationToken.None)));
    }

    [STATestMethod]
    public void DrumRoll_CancelsOnCloseAndRejectsConcurrentGeneration()
    {
        var completion = new TaskCompletionSource<QuantumPatternResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new StubService((_, token) => completion.Task.WaitAsync(token));
        using var fixture = new Fixture(service);
        Task<QuantumPatternResult> pending = fixture.Editor.ApplyQuantumPatternToRowAsync(0, 4, 1024, CancellationToken.None);
        Assert.ThrowsExactly<InvalidOperationException>(() => Drain(fixture.Editor.ApplyQuantumPatternToRowAsync(0, 4, 1024, CancellationToken.None)));
        fixture.Editor.Close();
        Assert.Throws<OperationCanceledException>(() => Drain(pending));
        Assert.IsTrue(service.LastToken.IsCancellationRequested);
    }

    [STATestMethod]
    public void DrumRoll_DoesNotCommitResultsForAChangedGrid()
    {
        var completion = new TaskCompletionSource<QuantumPatternResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var fixture = new Fixture(new StubService((_, token) => completion.Task.WaitAsync(token)));
        Task<QuantumPatternResult> pending = fixture.Editor.ApplyQuantumPatternToRowAsync(0, 4, 1024, CancellationToken.None);
        var hits = (DomainUpDown)typeof(DrumRollEditor).GetField("domainUpDown_hits", Private)!.GetValue(fixture.Editor)!;
        hits.SelectedItem = "8";
        completion.SetResult(GeneratePattern(16));
        Assert.ThrowsExactly<InvalidOperationException>(() => Drain(pending));
        Assert.IsTrue(fixture.Source.Data.All(value => value == 0.25f));
    }

    [STATestMethod]
    public void DrumRoll_ContextMenuExposesEnglishQuantumActions()
    {
        using var fixture = new Fixture();
        var menu = (ContextMenuStrip)typeof(DrumRollEditor).GetField("rowContextMenu", Private)!.GetValue(fixture.Editor)!;
        string[] labels = menu.Items.OfType<ToolStripMenuItem>().Select(item => item.Text!).ToArray();
        CollectionAssert.Contains(labels, "Generate Quantum Pattern...");
        CollectionAssert.Contains(labels, "Clear Quantum DSP");
    }

    [TestMethod]
    public void UiErrorMessages_AreEnglishAndDoNotExposeLocalizedOrSensitiveDetails()
    {
        const string privateMessage = "Geheime Zugangsdaten: PRIVATE_TOKEN";
        Exception[] errors = [new TimeoutException(privateMessage), new HttpRequestException(privateMessage, null, HttpStatusCode.Unauthorized),
            new HttpRequestException(privateMessage, null, HttpStatusCode.TooManyRequests),
            new HttpRequestException(privateMessage, null, HttpStatusCode.BadRequest), new HttpRequestException(privateMessage),
            new NotSupportedException(privateMessage), new ArgumentException(privateMessage), new InvalidOperationException(privateMessage), new Exception(privateMessage)];
        foreach (Exception error in errors)
        {
            string message = DrumRollEditor.GetQuantumErrorMessage(error);
            Assert.IsFalse(message.Contains(privateMessage, StringComparison.Ordinal));
            Assert.IsFalse(message.Contains("PRIVATE_TOKEN", StringComparison.Ordinal));
            Assert.IsTrue(message.All(character => character <= 127));
            Assert.IsTrue(message.Contains("quantum", StringComparison.OrdinalIgnoreCase) || message.Contains("IBM", StringComparison.Ordinal) || message.Contains("sample", StringComparison.Ordinal));
        }
    }

    private static QuantumPatternResult Pattern(IReadOnlyList<DrumSliceTrigger> slices) => new("qasm", slices, new Dictionary<string, int>(), TimeSpan.Zero);
    private static QuantumPatternResult GeneratePattern(int count) => Pattern(Enumerable.Range(0, count)
        .Select(step => new DrumSliceTrigger(step, step is 0 or 2, step is 0 or 2 ? 0.5 : 0, 0, 1, 0)).ToArray());

    private static T Drain<T>(Task<T> task)
    {
        WaitUntil(() => task.IsCompleted);
        return task.GetAwaiter().GetResult();
    }

    private static void WaitUntil(Func<bool> done)
    {
        var watch = Stopwatch.StartNew();
        while (!done())
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(10)) throw new AssertFailedException("The quantum UI operation did not complete.");
            Application.DoEvents();
            Thread.Sleep(1);
        }
    }

    private static object? Call(DrumRollEditor editor, string name, params object[] arguments) =>
        typeof(DrumRollEditor).GetMethod(name, Private)!.Invoke(editor, arguments);

    private sealed class StubService(Func<int, CancellationToken, Task<QuantumPatternResult>> generate) : IQuantumPatternService
    {
        internal CancellationToken LastToken { get; private set; }
        public Task<QuantumPatternResult> GenerateBreakbeatAsync(int qubits, int shots, CancellationToken cancellationToken, int stepCount = 16)
        {
            this.LastToken = cancellationToken;
            return generate(stepCount, cancellationToken);
        }
    }

    private sealed class Fixture : IDisposable
    {
        internal AudioObj Source { get; } = new() { Data = Enumerable.Repeat(0.25f, 1024).ToArray(), SampleRate = 44100, Channels = 2 };
        internal DrumRollEditor Editor { get; }
        private IList Rows => (IList)typeof(DrumRollEditor).GetField("patternRows", Private)!.GetValue(this.Editor)!;
        internal bool[] Steps => (bool[])this.Rows[0]!.GetType().GetProperty("Steps")!.GetValue(this.Rows[0])!;
        internal AudioObj RowAudio => ((AudioCollection)typeof(DrumRollEditor).GetField("AudioC", Private)!.GetValue(this.Editor)!).Audios[0];

        internal Fixture(IQuantumPatternService? service = null, bool register = true, int sampleRate = 44100)
        {
            this.Source.SampleRate = sampleRate;
            this.Source.Rename("Quantum test sample");
            service ??= new StubService((steps, _) => Task.FromResult(GeneratePattern(steps)));
            this.Editor = new([this.Source], register ? service : null) { ShowInTaskbar = false };
            this.Editor.Show();
            WaitUntil(() => this.Rows.Count == 1);
        }

        internal AudioObj Resolve(int step) => (AudioObj)Call(this.Editor, "ResolveStepAudio", 0, step)!;
        public void Dispose() { this.Editor.Close(); this.Editor.Dispose(); this.Source.Dispose(); }
    }
}
