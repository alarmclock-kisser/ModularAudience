using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Forms;
using ModularAudience.Forms.Modules;

namespace ModularAudience.Audio.Tests;

[TestClass]
[DoNotParallelize]
public sealed class AudioScanControllerTests
{
    private WinFormsTestContext? uiContext;

    [TestInitialize]
    public void Initialize()
    {
        this.uiContext = new WinFormsTestContext();
        Assert.IsNull(WindowMain.Instance, "Scanning tests must not start recording or construct WindowMain.");
        Assert.AreEqual(0, WindowMain.TrackViews.Count);
        Assert.AreEqual(0, WindowMain.CollectionViews.Count);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            foreach (TrackView track in WindowMain.TrackViews.ToArray())
            {
                track.Close();
                PumpUntil(() => track.IsDisposed);
            }
            foreach (AudioCollectionView collection in WindowMain.CollectionViews.ToArray())
            {
                collection.Close();
                collection.Dispose();
            }
            WindowMain.LastSelectedCollectionView = null;
            WindowMain.CommandSelection.FocusTrackView();
        }
        finally { this.uiContext?.Dispose(); }
    }

    [STATestMethod]
    public void ExistingScanResultsFollowTheTargetAndUnknownValuesClearOldResults()
    {
        using AudioTestScope scope = new();
        using ScanFixture fixture = new();
        AudioObj first = scope.Create([0f]);
        first.ScannedBpm = 127.5f;
        first.ScannedTiming = 2f;
        first.ScannedKey = "F# minor";
        fixture.Controller.SetTarget(first);

        Assert.AreEqual($"{127.5f:F3} BPM", fixture.BpmResult.Text);
        Assert.AreEqual("1", fixture.TimingResult.Text);
        Assert.AreEqual("F# minor", fixture.KeyResult.Text);
        Assert.IsTrue(fixture.Buttons.All(button => button.Enabled));

        AudioObj second = scope.Create([0f]);
        second.ScannedTiming = 0f;
        fixture.Controller.SetTarget(second);
        Assert.IsTrue(fixture.Results.All(result => result.Text == string.Empty));
        fixture.Controller.SetTarget(null);
        Assert.IsTrue(fixture.Results.All(result => result.Text == string.Empty));
        Assert.IsTrue(fixture.Buttons.All(button => !button.Enabled));
    }

    [STATestMethod]
    public void SelectedAudioMetadataChangesUpdateAllThreeFieldsWithoutAnotherFocusChange()
    {
        using AudioTestScope scope = new();
        using ScanFixture fixture = new();
        AudioObj audio = scope.Create([0f]);
        fixture.Controller.SetTarget(audio);

        audio.ScannedBpm = 139.25f;
        audio.ScannedTiming = 0.25f;
        audio.ScannedKey = "Bb major";

        Assert.AreEqual($"{139.25f:F3} BPM", fixture.BpmResult.Text);
        Assert.AreEqual("1/8", fixture.TimingResult.Text);
        Assert.AreEqual("Bb major", fixture.KeyResult.Text);
    }

    [STATestMethod]
    public void BackgroundMetadataChangesAreMarshaledToTheOwningUiThread()
    {
        using AudioTestScope scope = new();
        using ScanFixture fixture = new();
        AudioObj audio = scope.Create([0f]);
        fixture.Controller.SetTarget(audio);
        int uiThread = Environment.CurrentManagedThreadId;
        List<int> resultThreads = [];
        foreach (TextBox result in fixture.Results)
            result.TextChanged += (_, _) => resultThreads.Add(Environment.CurrentManagedThreadId);

        Task.Run(() =>
        {
            audio.ScannedBpm = 111.5f;
            audio.ScannedTiming = 3f;
            audio.ScannedKey = "C minor";
        }).GetAwaiter().GetResult();
        PumpUntil(() => fixture.KeyResult.Text == "C minor");

        Assert.AreEqual($"{111.5f:F3} BPM", fixture.BpmResult.Text);
        Assert.AreEqual("2", fixture.TimingResult.Text);
        Assert.IsTrue(resultThreads.Count > 0);
        Assert.IsTrue(resultThreads.All(thread => thread == uiThread));
    }

    [STATestMethod]
    public void FocusedCollectionAndRefocusedTrackViewUseTheirOwnAudioAndCachedResults()
    {
        using AudioTestScope scope = new();
        AudioObj collectionAudio = scope.Create(new float[1024]);
        collectionAudio.ScannedBpm = 100f;
        collectionAudio.ScannedTiming = 0.25f;
        collectionAudio.ScannedKey = "A minor";
        AudioCollectionView collection = new([collectionAudio]);
        ListBox list = (ListBox)collection.Controls.Find("listBox_audios", true).Single();
        list.SetSelected(0, true);
        collection.Activate();
        list.Focus();
        using ScanFixture fixture = new();
        fixture.Controller.SetTarget(WindowMain.ResolveSelectedAudioForCommands());
        Assert.AreEqual($"{100f:F3} BPM", fixture.BpmResult.Text);

        AudioObj other = scope.Create(new float[1024]);
        other.ScannedBpm = 155f;
        other.ScannedTiming = 2f;
        other.ScannedKey = "E major";
        TrackView track = new(other);
        track.Activate();
        track.Focus();
        Application.DoEvents();
        Assert.AreSame(track.OriginalAudio, WindowMain.ResolveSelectedAudioForCommands());
        fixture.Controller.SetTarget(WindowMain.ResolveSelectedAudioForCommands());
        Assert.AreEqual($"{155f:F3} BPM", fixture.BpmResult.Text);
        Assert.AreEqual("1", fixture.TimingResult.Text);
        Assert.AreEqual("E major", fixture.KeyResult.Text);

        collection.Activate();
        list.Focus();
        Application.DoEvents();
        Assert.AreSame(collectionAudio, WindowMain.ResolveSelectedAudioForCommands(), "An old open TrackView must not take precedence.");
        fixture.Controller.SetTarget(WindowMain.ResolveSelectedAudioForCommands());
        Assert.AreEqual("A minor", fixture.KeyResult.Text);

        // LastSelectedTrackView is still the same object. Returning to it must nevertheless change the command source.
        track.Activate();
        track.Focus();
        Application.DoEvents();
        Assert.AreSame(track.OriginalAudio, WindowMain.ResolveSelectedAudioForCommands());
        fixture.Controller.SetTarget(WindowMain.ResolveSelectedAudioForCommands());
        Assert.AreEqual($"{155f:F3} BPM", fixture.BpmResult.Text);
    }

    [STATestMethod]
    public void AllScannersUpdateAnUnopenedSingleCollectionAudio()
    {
        using AudioTestScope scope = new();
        AudioObj audio = scope.Create([0f]);
        AudioCollectionView collection = new([audio]);
        ListBox list = (ListBox)collection.Controls.Find("listBox_audios", true).Single();
        list.SetSelected(0, true);
        collection.Activate();
        list.Focus();
        List<AudioObj> scanned = [];
        using ScanFixture fixture = new(
            scanBpm: selected => { scanned.Add(selected); return Task.FromResult(123.25); },
            scanTiming: selected => { scanned.Add(selected); return Task.FromResult(3f); },
            scanKey: selected => { scanned.Add(selected); return Task.FromResult("D minor"); });
        Assert.AreEqual(0, WindowMain.TrackViews.Count);
        fixture.Controller.SetTarget(WindowMain.ResolveSelectedAudioForCommands());

        Assert.IsTrue(Drain(fixture.Controller.ScanBpmAsync()));
        Assert.IsTrue(Drain(fixture.Controller.ScanTimingAsync()));
        Assert.IsTrue(Drain(fixture.Controller.ScanKeyAsync()));

        Assert.AreEqual(3, scanned.Count);
        Assert.IsTrue(scanned.All(selected => ReferenceEquals(selected, audio)));
        Assert.AreEqual(123.25f, audio.ScannedBpm);
        Assert.AreEqual(3f, audio.ScannedTiming);
        Assert.AreEqual("D minor", audio.ScannedKey);
        Assert.AreEqual($"{123.25f:F3} BPM", fixture.BpmResult.Text);
        Assert.AreEqual("2", fixture.TimingResult.Text);
        Assert.AreEqual("D minor", fixture.KeyResult.Text);
    }

    [STATestMethod]
    [DataRow("BPM")]
    [DataRow("timing")]
    [DataRow("key")]
    public void FinishingAnOldScanUpdatesItsCapturedAudioButNeverTheNewTargetsDisplay(string kind)
    {
        using AudioTestScope scope = new();
        AudioObj first = scope.Create([0f]);
        AudioObj second = scope.Create([0f]);
        second.ScannedBpm = 170f;
        second.ScannedTiming = 0.25f;
        second.ScannedKey = "G major";
        var bpm = new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously);
        var timing = new TaskCompletionSource<float>(TaskCreationOptions.RunContinuationsAsynchronously);
        var key = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using ScanFixture fixture = new(_ => bpm.Task, _ => timing.Task, _ => key.Task);
        fixture.Controller.SetTarget(first);

        Task<bool> scan = RunScan(fixture.Controller, kind);
        Assert.IsFalse(fixture.Button(kind).Enabled);
        fixture.Controller.SetTarget(second);
        Task.Run(() => CompleteScan(kind, bpm, timing, key)).GetAwaiter().GetResult();
        Assert.IsTrue(Drain(scan));

        AssertScanResult(kind, first);
        Assert.AreEqual(170f, second.ScannedBpm);
        Assert.AreEqual(0.25f, second.ScannedTiming);
        Assert.AreEqual("G major", second.ScannedKey);
        Assert.AreEqual($"{170f:F3} BPM", fixture.BpmResult.Text);
        Assert.AreEqual("1/8", fixture.TimingResult.Text);
        Assert.AreEqual("G major", fixture.KeyResult.Text);
        Assert.IsTrue(fixture.Button(kind).Enabled);
    }

    [STATestMethod]
    [DataRow("BPM")]
    [DataRow("timing")]
    [DataRow("key")]
    public void FinishingAScanAfterDeselectingDoesNotRestoreStaleResults(string kind)
    {
        using AudioTestScope scope = new();
        AudioObj audio = scope.Create([0f]);
        var bpm = new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously);
        var timing = new TaskCompletionSource<float>(TaskCreationOptions.RunContinuationsAsynchronously);
        var key = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using ScanFixture fixture = new(_ => bpm.Task, _ => timing.Task, _ => key.Task);
        fixture.Controller.SetTarget(audio);
        Task<bool> scan = RunScan(fixture.Controller, kind);
        fixture.Controller.SetTarget(null);

        CompleteScan(kind, bpm, timing, key);
        Assert.IsTrue(Drain(scan));

        AssertScanResult(kind, audio);
        Assert.IsTrue(fixture.Results.All(result => result.Text == string.Empty));
        Assert.IsTrue(fixture.Buttons.All(button => !button.Enabled));
    }

    [STATestMethod]
    public void FailedScanReenablesTheButtonAndKeepsExistingMetadata()
    {
        using AudioTestScope scope = new();
        AudioObj audio = scope.Create([0f]);
        audio.ScannedBpm = 145f;
        using ScanFixture fixture = new(scanBpm: _ => Task.FromException<double>(new IOException("scan test failure")));
        fixture.Controller.SetTarget(audio);

        Assert.IsFalse(Drain(fixture.Controller.ScanBpmAsync()));
        Assert.AreEqual(145f, audio.ScannedBpm);
        Assert.AreEqual($"{145f:F3} BPM", fixture.BpmResult.Text);
        Assert.IsTrue(fixture.BpmButton.Enabled);
        Assert.AreEqual(1, fixture.Errors.Count);
    }

    [STATestMethod]
    public void ClosingTheOwnerDuringAnInFlightScanDoesNotAccessDisposedControls()
    {
        using AudioTestScope scope = new();
        AudioObj audio = scope.Create([0f]);
        var completion = new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously);
        using ScanFixture fixture = new(scanBpm: _ => completion.Task);
        fixture.Controller.SetTarget(audio);
        Task<bool> scan = fixture.Controller.ScanBpmAsync();

        fixture.Dispose();
        completion.SetResult(121);

        Assert.IsFalse(Drain(scan));
        Assert.AreEqual(0f, audio.ScannedBpm);
        Assert.AreEqual(0, fixture.Errors.Count);
    }

    private static Task<bool> RunScan(AudioScanController controller, string kind) => kind switch
    {
        "BPM" => controller.ScanBpmAsync(),
        "timing" => controller.ScanTimingAsync(),
        _ => controller.ScanKeyAsync()
    };

    private static void CompleteScan(string kind, TaskCompletionSource<double> bpm, TaskCompletionSource<float> timing, TaskCompletionSource<string> key)
    {
        if (kind == "BPM") bpm.SetResult(128.5);
        else if (kind == "timing") timing.SetResult(2f);
        else key.SetResult("C# minor");
    }

    private static void AssertScanResult(string kind, AudioObj audio)
    {
        if (kind == "BPM") Assert.AreEqual(128.5f, audio.ScannedBpm);
        else if (kind == "timing") Assert.AreEqual(2f, audio.ScannedTiming);
        else Assert.AreEqual("C# minor", audio.ScannedKey);
    }

    private static bool Drain(Task<bool> task)
    {
        PumpUntil(() => task.IsCompleted);
        return task.GetAwaiter().GetResult();
    }

    private static void PumpUntil(Func<bool> complete)
    {
        Stopwatch watch = Stopwatch.StartNew();
        while (!complete() && watch.Elapsed < TimeSpan.FromSeconds(5))
        {
            Application.DoEvents();
            Thread.Sleep(1);
        }
        Assert.IsTrue(complete(), "The UI operation did not complete within five seconds.");
    }

    private sealed class ScanFixture : IDisposable
    {
        internal readonly Form Owner = new() { ShowInTaskbar = false };
        internal readonly Button BpmButton = new();
        internal readonly Button TimingButton = new();
        internal readonly Button KeyButton = new();
        internal readonly TextBox BpmResult = new();
        internal readonly TextBox TimingResult = new();
        internal readonly TextBox KeyResult = new();
        internal readonly ConcurrentQueue<string> Errors = new();
        internal readonly AudioScanController Controller;
        internal Button[] Buttons => [this.BpmButton, this.TimingButton, this.KeyButton];
        internal TextBox[] Results => [this.BpmResult, this.TimingResult, this.KeyResult];

        internal ScanFixture(Func<AudioObj, Task<double>>? scanBpm = null,
            Func<AudioObj, Task<float>>? scanTiming = null, Func<AudioObj, Task<string>>? scanKey = null)
        {
            this.Owner.Controls.AddRange([.. this.Buttons, .. this.Results]);
            this.Owner.Show();
            this.Controller = new AudioScanController(this.Owner,
                this.BpmButton, this.BpmResult, this.TimingButton, this.TimingResult, this.KeyButton, this.KeyResult,
                scanBpm, scanTiming, scanKey, this.Errors.Enqueue);
        }

        internal Button Button(string kind) => kind == "BPM" ? this.BpmButton : kind == "timing" ? this.TimingButton : this.KeyButton;

        public void Dispose()
        {
            this.Controller.Dispose();
            this.Owner.Dispose();
        }
    }
}
