using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Processing;
using ModularAudience.Forms.Modules.Dialogs;
using ModularAudience.Ytdlp;
using NAudio.Wave;

namespace ModularAudience.Audio.Tests;

[TestClass]
[DoNotParallelize]
public sealed class YoutubePipelineUiTests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

    [TestMethod]
    [DataRow(YtdlpResultType.All, "featured")]
    [DataRow(YtdlpResultType.Videos, "videos")]
    [DataRow(YtdlpResultType.Playlists, "playlists")]
    [DataRow(YtdlpResultType.Channels, "channels")]
    public void ChannelFiltersTargetTheChannelTabRatherThanGlobalSearch(YtdlpResultType type, string tab)
    {
        foreach (string source in new[] { "https://www.youtube.com/@artist/videos", "https://www.youtube.com/channel/UCsXVk37bltHxD1rDPwtNM8Q/playlists" })
        {
            string url = YtdlpClient.CreateChannelBrowseUrl(source, type);
            StringAssert.EndsWith(url, "/" + tab);
            Assert.AreEqual(url, YtdlpParser.ValidateYoutubeUrl(url).AbsoluteUri);
        }
        Assert.ThrowsExactly<ArgumentException>(() => YtdlpClient.CreateChannelBrowseUrl(Video(0).Url, type));
    }

    [STATestMethod]
    public void ChannelEntryResetsAllAndTypeChangesRefreshThatChannelIncludingAfterHistoryNavigation()
    {
        using var scope = new Scope();
        var window = scope.Window;
        var requests = new List<(string Url, YtdlpResultType Type)>();
        Set(window, "channelResultsProvider", new Func<string, int, YtdlpResultType, CancellationToken, IAsyncEnumerable<YtdlpVideo>>((url, _, type, token) =>
        {
            requests.Add((url, type));
            return Stream([Video(1), Playlist(), Channel()], token);
        }));
        Set(window, "searchProvider", new Func<string, int, CancellationToken, Task<IReadOnlyList<YtdlpVideo>>>((_, _, _) => throw new AssertFailedException("Channel filters must not perform a global word search.")));
        var combo = Field<ComboBox>(window, "resultTypeCombo");
        combo.SelectedIndex = (int)YtdlpResultType.Channels;
        Invoke(window, "PopulateResults", (object)new[] { Channel() });
        Field<ListView>(window, "resultsList").Items[0].Selected = true;
        Invoke(window, "NavigateToChannel_Click", null, EventArgs.Empty);
        Assert.AreEqual((int)YtdlpResultType.All, combo.SelectedIndex);
        PumpTask(Field<Task>(window, "browseOperation"));
        Assert.AreEqual(3, Field<ListView>(window, "resultsList").Items.Count);
        combo.SelectedIndex = (int)YtdlpResultType.Playlists;
        PumpTask(Field<Task>(window, "browseOperation"));
        Assert.AreEqual(YtdlpResultType.Playlists, requests[^1].Type);
        Assert.AreEqual(Channel().Url, requests[^1].Url);
        Assert.AreEqual("[Playlist] Artist playlist", Field<ListView>(window, "resultsList").Items[0].Text);
        Assert.AreEqual("Channel :: Artist", Field<TextBox>(window, "queryText").Text);
        Invoke(window, "NavigateResultsHistory", -1);
        Assert.AreEqual((int)YtdlpResultType.All, combo.SelectedIndex);
        combo.SelectedIndex = (int)YtdlpResultType.Videos;
        PumpTask(Field<Task>(window, "browseOperation"));
        Assert.AreEqual(YtdlpResultType.Videos, requests[^1].Type);
        Assert.AreEqual(Channel().Url, requests[^1].Url);
        Assert.IsTrue(((YtdlpVideo)Field<ListView>(window, "resultsList").Items[0].Tag!).IsVideo);
    }

    [STATestMethod]
    public void TypeSelectionStartsATypedSearchWithoutRequiringAnExistingResultsPage()
    {
        using var scope = new Scope();
        var window = scope.Window;
        int searches = 0;
        Set(window, "searchStreamProvider", new Func<string, int, YtdlpResultType, CancellationToken, IAsyncEnumerable<YtdlpVideo>>((query, _, type, token) =>
        {
            Assert.AreEqual("new words", query); Assert.AreEqual(YtdlpResultType.Playlists, type);
            searches++;
            return Stream([Playlist()], token);
        }));
        Field<TextBox>(window, "queryText").Text = "new words";
        Field<ComboBox>(window, "resultTypeCombo").SelectedIndex = (int)YtdlpResultType.Playlists;
        PumpTask(Field<Task>(window, "browseOperation"));
        Assert.AreEqual(1, searches);
        Assert.AreEqual(1, Field<ListView>(window, "resultsList").Items.Count);
    }

    [STATestMethod]
    public void SearchControlsStayEnabledFromTheFirstMomentOfStartingADownloadBatch()
    {
        using var scope = new Scope();
        var window = scope.Window;
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Invoke(window, "StartOperation", "Preparing downloads", new Func<CancellationToken, Task>(token => finish.Task.WaitAsync(token)), true);
        scope.Work = Field<Task>(window, "operation");
        Assert.IsNull(Field<object?>(window, "activeBatch"));
        Assert.IsTrue(Field<TableLayoutPanel>(window, "queryPanel").Enabled,
            "The old code disabled the query before asynchronous batch initialization and never refreshed it.");
        Assert.IsTrue(Field<TextBox>(window, "queryText").Enabled);
        Assert.IsTrue(Field<ComboBox>(window, "resultTypeCombo").Enabled);
        finish.SetResult();
        PumpTask(scope.Work);
    }

    [STATestMethod]
    public void FailedNeighborCannotEndUnlimitedTagWaitAndWaitersDoNotOccupyDownloadSlots()
    {
        using var source = new CancellationTokenSource();
        using var scope = new Scope();
        var window = scope.Window;
        int downloads = 0, active = 0, peak = 0;
        foreach (int i in Enumerable.Range(1, 5)) scope.Wav(i);
        Set(window, "downloadProvider", new Func<YtdlpVideo, YtdlpDownloadOptions, CancellationToken, Task<YtdlpDownloadResult>>(async (video, _, token) =>
        {
            downloads++; active++; peak = Math.Max(peak, active);
            try
            {
                await Task.Delay(20, token);
                if (video.Id == Video(0).Id) throw new IOException("Independent download failure");
                return Result(scope.PathFor(video));
            }
            finally { active--; }
        }));
        object batch = scope.StartPass(Enumerable.Range(0, 6).Select(Video).ToArray(), source, waitForTag: true, TimeSpan.Zero, retries: 0, parallel: 2);
        PumpUntil(() => Jobs(batch).Count(job => Get<string>(job, "Status").StartsWith("Waiting for BPM tag", StringComparison.Ordinal)) == 5);
        Assert.AreEqual(6, downloads);
        Assert.IsTrue(peak <= 2);
        Assert.AreEqual("Failed", Get<string>(Jobs(batch)[0], "Status"));
        Assert.IsFalse(scope.Work!.IsCompleted);
        Assert.AreEqual(0, scope.Imported.Count);
        Assert.IsTrue(Field<TableLayoutPanel>(window, "queryPanel").Enabled);
        Assert.IsTrue(Jobs(batch).Skip(1).All(job => Get<bool>(job, "WaitingForBpmWithoutLimit")));
        foreach (int i in Enumerable.Range(1, 5)) Assert.IsTrue(AudioObj.WriteBpmTagToFile(scope.PathFor(Video(i)), 100 + i));
        PumpTask(scope.Work);
        Assert.AreEqual(5, scope.Imported.Count);
        Assert.IsTrue(scope.Imported.All(audio => audio.Bpm > 0));
        Assert.IsTrue(Jobs(batch).Skip(1).All(job => Get<string>(job, "Status") == "Completed"));
    }

    [STATestMethod]
    public void ACancelledUnlimitedTagWaitStopsOnlyThatJobAndDoesNotImportIt()
    {
        using var source = new CancellationTokenSource();
        using var scope = new Scope();
        foreach (int i in new[] { 0, 1 }) scope.Wav(i);
        scope.UseLocalDownloads();
        object batch = scope.StartPass([Video(0), Video(1)], source, true, TimeSpan.Zero, 0, 1);
        PumpUntil(() => Jobs(batch).All(job => Get<bool>(job, "WaitingForBpmWithoutLimit")));
        Field<ListView>(scope.Window, "downloadJobsList").Items[0].Selected = true;
        Invoke(scope.Window, "CancelJobs_Click", null, EventArgs.Empty);
        PumpUntil(() => Get<string>(Jobs(batch)[0], "Status") == "Cancelled");
        Assert.IsFalse(source.IsCancellationRequested);
        Assert.IsFalse(scope.Work!.IsCompleted);
        Assert.AreEqual(0, scope.Imported.Count);
        Assert.IsTrue(AudioObj.WriteBpmTagToFile(scope.PathFor(Video(1)), 128));
        PumpTask(scope.Work);
        Assert.AreEqual(1, scope.Imported.Count);
        Assert.AreEqual(128f, scope.Imported[0].Bpm);
    }

    [STATestMethod]
    public void ClosingTheWindowCancelsAndAwaitsAnUnlimitedBpmWaitWithoutImporting()
    {
        using var scope = new Scope();
        scope.Wav(0); scope.UseLocalDownloads();
        var window = scope.Window;
        Field<CheckBox>(window, "waitForBpmCheck").Checked = true;
        Field<NumericUpDown>(window, "cooldownSeconds").Value = 0;
        Field<NumericUpDown>(window, "retryCountNumber").Value = 0;
        Invoke(window, "QueueDownloadVideos", new[] { Video(0) }, false);
        scope.Work = Field<Task>(window, "operation");
        PumpUntil(() => Field<object?>(window, "activeBatch") is { } batch && Jobs(batch).All(job => Get<bool>(job, "WaitingForBpmWithoutLimit")));
        object active = Field<object>(window, "activeBatch");
        window.Close();
        PumpUntil(() => window.IsDisposed && scope.Work.IsCompleted);
        scope.Work.GetAwaiter().GetResult();
        Assert.AreEqual(0, scope.Imported.Count);
        Assert.AreEqual("Cancelled", Get<string>(Jobs(active)[0], "Status"));
    }

    [STATestMethod]
    public void ZeroWaitWithoutTagWaitingImportsImmediatelyAndRetriesKeepTheirCapturedOptions()
    {
        using var source = new CancellationTokenSource();
        using var scope = new Scope();
        scope.Wav(0);
        int attempts = 0;
        YtdlpDownloadOptions? first = null;
        Set(scope.Window, "downloadProvider", new Func<YtdlpVideo, YtdlpDownloadOptions, CancellationToken, Task<YtdlpDownloadResult>>((video, options, _) =>
        {
            attempts++;
            if (first is null) first = options; else Assert.AreSame(first, options);
            return attempts == 1 ? Task.FromException<YtdlpDownloadResult>(new IOException("Retry me")) : Task.FromResult(Result(scope.PathFor(video)));
        }));
        object batch = scope.StartPass([Video(0)], source, false, TimeSpan.Zero, 1, 1);
        PumpUntil(() => Get<string>(Jobs(batch)[0], "Status").StartsWith("Retry", StringComparison.Ordinal));
        Field<NumericUpDown>(scope.Window, "retryCountNumber").Value = 0;
        PumpTask(scope.Work!);
        Assert.AreEqual(2, attempts);
        Assert.AreEqual(1, Get<int>(batch, "MaxRetries"));
        Assert.AreEqual(1, scope.Imported.Count);
        Assert.IsFalse(Get<bool>(Jobs(batch)[0], "BpmTagTimedOut"));
    }

    [STATestMethod]
    public void ImportRetriesReuseTheDownloadedFileInsteadOfDownloadingAgain()
    {
        using var source = new CancellationTokenSource();
        using var scope = new Scope();
        File.WriteAllText(scope.PathFor(Video(0)), "not a wave yet");
        int downloads = 0;
        Set(scope.Window, "downloadProvider", new Func<YtdlpVideo, YtdlpDownloadOptions, CancellationToken, Task<YtdlpDownloadResult>>((video, _, _) =>
        { downloads++; return Task.FromResult(Result(scope.PathFor(video))); }));
        object batch = scope.StartPass([Video(0)], source, false, TimeSpan.Zero, 1, 1);
        PumpUntil(() => Get<string>(Jobs(batch)[0], "Status").Contains("audio import", StringComparison.Ordinal));
        scope.Wav(0);
        PumpTask(scope.Work!);
        Assert.AreEqual(1, downloads);
        Assert.AreEqual(1, scope.Imported.Count);
        Assert.AreEqual("Completed", Get<string>(Jobs(batch)[0], "Status"));
    }

    [STATestMethod]
    public void PositiveBpmTimeoutIsVisiblePerItemInsteadOfSilentlyPretendingATagWasDetected()
    {
        using var source = new CancellationTokenSource();
        using var scope = new Scope();
        scope.Wav(0); scope.UseLocalDownloads();
        object batch = scope.StartPass([Video(0)], source, true, TimeSpan.FromMilliseconds(100), 0, 1);
        PumpTask(scope.Work!);
        Assert.AreEqual(1, scope.Imported.Count);
        Assert.IsTrue(Get<bool>(Jobs(batch)[0], "BpmTagTimedOut"));
        StringAssert.Contains(Field<ListView>(scope.Window, "downloadJobsList").Items[0].SubItems[1].Text, "timed out");
        StringAssert.Contains(Field<TextBox>(scope.Window, "activityLog").Text, "without an awaited BPM tag");
    }

    [STATestMethod]
    public void RetriesControlIsBeforeParallelAndPersistsToJsonIncludingZeroWait()
    {
        using var scope = new Scope();
        var retries = Field<NumericUpDown>(scope.Window, "retryCountNumber");
        var parallel = Field<NumericUpDown>(scope.Window, "maxParallelDownloadsNumber");
        Assert.AreSame(parallel.Parent, retries.Parent);
        Assert.IsTrue(retries.Parent!.Controls.IndexOf(retries) < parallel.Parent!.Controls.IndexOf(parallel));
        Field<CheckBox>(scope.Window, "waitForBpmCheck").Checked = true;
        Field<NumericUpDown>(scope.Window, "cooldownSeconds").Value = 0;
        retries.Value = 5;
        var loaded = YoutubeBrowserSettings.Load(out _, scope.SettingsPath);
        Assert.AreEqual(5, loaded.MaxRetries);
        Assert.AreEqual(0, loaded.CooldownSeconds);
        Assert.IsTrue(loaded.WaitForBpmTag);
        using var reopened = new YoutubeBrowserWindow(audio => audio.Dispose(), scope.SettingsPath);
        Assert.AreEqual(5m, Field<NumericUpDown>(reopened, "retryCountNumber").Value);
        Assert.AreEqual(0m, Field<NumericUpDown>(reopened, "cooldownSeconds").Value);
        Assert.IsTrue(Field<CheckBox>(reopened, "waitForBpmCheck").Checked);
    }

    private static YtdlpVideo Video(int i) => new() { Id = $"v{i:0000000000}", Title = $"Video {i}", Url = $"https://www.youtube.com/watch?v=v{i:0000000000}", UploadDate = new(2025, 1, 1) };
    private static YtdlpVideo Channel() => new() { IsChannel = true, Id = "UCsXVk37bltHxD1rDPwtNM8Q", Title = "Artist", Url = "https://www.youtube.com/channel/UCsXVk37bltHxD1rDPwtNM8Q/videos" };
    private static YtdlpVideo Playlist() => new() { IsPlaylist = true, Id = "PL_artist", Title = "Artist playlist", Url = "https://www.youtube.com/playlist?list=PL_artist" };
    private static async IAsyncEnumerable<YtdlpVideo> Stream(IEnumerable<YtdlpVideo> videos, [EnumeratorCancellation] CancellationToken token)
    {
        foreach (var video in videos) { token.ThrowIfCancellationRequested(); yield return video; await Task.Yield(); }
    }
    private static YtdlpDownloadResult Result(string path) => (YtdlpDownloadResult)Activator.CreateInstance(typeof(YtdlpDownloadResult),
        BindingFlags.Instance | BindingFlags.NonPublic, null, [path, null], null)!;
    private static object[] Jobs(object batch) => ((Array)batch.GetType().GetProperty("Jobs")!.GetValue(batch)!).Cast<object>().ToArray();
    private static T Get<T>(object instance, string name) => (T)instance.GetType().GetProperty(name)!.GetValue(instance)!;
    private static T Field<T>(YoutubeBrowserWindow window, string name) => (T)typeof(YoutubeBrowserWindow).GetField(name, Flags)!.GetValue(window)!;
    private static void Set(YoutubeBrowserWindow window, string name, object? value) => typeof(YoutubeBrowserWindow).GetField(name, Flags)!.SetValue(window, value);
    private static object? Invoke(YoutubeBrowserWindow window, string name, params object?[] args) => typeof(YoutubeBrowserWindow).GetMethod(name, Flags)!.Invoke(window, args);
    private static void PumpTask(Task task) { PumpUntil(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static void PumpUntil(Func<bool> done)
    {
        var clock = Stopwatch.StartNew();
        while (!done() && clock.Elapsed < TimeSpan.FromSeconds(10)) { Application.DoEvents(); Thread.Sleep(1); }
        Assert.IsTrue(done(), "The asynchronous operation did not complete in time.");
    }

    private sealed class Scope : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "opencode", "yt-pipeline-tests-" + Guid.NewGuid().ToString("N"));
        private readonly SemaphoreSlim importGate = new(1, 1);
        public string SettingsPath => Path.Combine(directory, "settings.json");
        public YoutubeBrowserWindow Window { get; }
        public List<AudioObj> Imported { get; } = [];
        public Task? Work { get; set; }
        public Scope()
        {
            Directory.CreateDirectory(directory);
            Window = new YoutubeBrowserWindow(audio => Imported.Add(audio), SettingsPath);
            _ = Window.Handle; _ = Field<ListView>(Window, "resultsList").Handle; _ = Field<ListView>(Window, "downloadJobsList").Handle;
            Field<CheckBox>(Window, "thumbnailCheck").Checked = false;
            Set(Window, "binariesReady", true);
            Set(Window, "searchHistory", new YoutubeSearchHistory(Path.Combine(directory, "history.json")));
            Invoke(Window, "RefreshAvailability");
        }
        public string PathFor(YtdlpVideo video) => Path.Combine(directory, video.Id + ".wav");
        public void Wav(int index)
        {
            using var writer = new WaveFileWriter(PathFor(Video(index)), new WaveFormat(16000, 16, 1));
            writer.WriteSamples(new short[] { 0, 8192, -8192, 0 }, 0, 4);
        }
        public void UseLocalDownloads() => Set(Window, "downloadProvider",
            new Func<YtdlpVideo, YtdlpDownloadOptions, CancellationToken, Task<YtdlpDownloadResult>>((video, _, _) => Task.FromResult(Result(PathFor(video)))));
        public object StartPass(YtdlpVideo[] videos, CancellationTokenSource source, bool waitForTag, TimeSpan cooldown, int retries, int parallel)
        {
            Field<NumericUpDown>(Window, "retryCountNumber").Value = retries;
            Set(Window, "operationCancellation", source); Set(Window, "busy", true); Set(Window, "downloadBatchRunning", true);
            Set(Window, "batchWaitForBpmTag", waitForTag);
            object batch = Invoke(Window, "InitializeBatch", (object)videos)!;
            Work = (Task)Invoke(Window, "RunBatchPassAsync", batch, batch.GetType().GetProperty("Jobs")!.GetValue(batch),
                new YtdlpDownloadOptions(), cooldown, null, parallel, importGate, source.Token)!;
            return batch;
        }
        public void Dispose()
        {
            Field<CancellationTokenSource?>(Window, "operationCancellation")?.Cancel();
            if (Work is not null)
            {
                PumpUntil(() => Work.IsCompleted);
                try { Work.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
            }
            Window.Dispose();
            foreach (var audio in Imported) audio.Dispose();
            importGate.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }
}
