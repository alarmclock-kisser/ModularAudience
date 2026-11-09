using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Forms.Modules.Dialogs;
using ModularAudience.Ytdlp;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace ModularAudience.Audio.Tests;

[TestClass]
[DoNotParallelize]
public sealed class YoutubeSearchStreamingTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [STATestMethod]
    [DataRow(YtdlpResultType.All, 3)]
    [DataRow(YtdlpResultType.Videos, 1)]
    [DataRow(YtdlpResultType.Playlists, 1)]
    [DataRow(YtdlpResultType.Channels, 1)]
    public void ResultTypeFiltersMixedArrivalsAndKeepsTheDefaultAll(YtdlpResultType type, int expectedCount)
    {
        using var scope = new BrowserScope();
        var window = scope.Window;
        var combo = Field<ComboBox>(window, "resultTypeCombo");
        Assert.AreEqual(0, combo.SelectedIndex);
        CollectionAssert.AreEqual(new[] { "All", "Videos", "Playlists", "Channels" }, combo.Items.Cast<string>().ToArray());
        combo.SelectedIndex = (int)type;
        YtdlpResultType? requested = null;
        Set(window, "searchStreamProvider", new Func<string, int, YtdlpResultType, CancellationToken, IAsyncEnumerable<YtdlpVideo>>((_, _, selected, token) =>
        {
            requested = selected;
            return Stream([Video(1), Playlist(), Channel(), Video(1)], token);
        }));
        Search(window, "mixed search");
        PumpBrowse(window);
        var list = Field<ListView>(window, "resultsList");
        Assert.AreEqual(type, requested);
        Assert.AreEqual(expectedCount, list.Items.Count);
        Assert.IsTrue(list.Items.Cast<ListViewItem>().All(row => YtdlpParser.MatchesResultType((YtdlpVideo)row.Tag!, type)));
        if (type == YtdlpResultType.Playlists)
        {
            Assert.AreEqual("[Playlist] Synth playlist", list.Items[0].Text);
            foreach (int column in new[] { 2, 3, 6 }) Assert.AreEqual(string.Empty, list.Items[0].SubItems[column].Text);
            list.Items[0].Selected = true;
            Assert.IsFalse(Field<Button>(window, "metadataButton").Enabled);
            Assert.IsFalse(Field<Button>(window, "showRelatedButton").Enabled);
        }
    }

    [STATestMethod]
    public void ChangingResultTypeRefreshesTheCurrentSearchWithoutAnExtraSearchClick()
    {
        using var scope = new BrowserScope();
        var window = scope.Window;
        Set(window, "searchStreamProvider", new Func<string, int, YtdlpResultType, CancellationToken, IAsyncEnumerable<YtdlpVideo>>((_, _, _, token) =>
            Stream([Video(1), Playlist(), Channel()], token)));
        Search(window, "synth music");
        PumpBrowse(window);
        Assert.AreEqual(3, Field<ListView>(window, "resultsList").Items.Count);
        Field<ComboBox>(window, "resultTypeCombo").SelectedIndex = (int)YtdlpResultType.Channels;
        PumpBrowse(window);
        var list = Field<ListView>(window, "resultsList");
        Assert.AreEqual(1, list.Items.Count);
        Assert.IsTrue(((YtdlpVideo)list.Items[0].Tag!).IsChannel);
    }

    [STATestMethod]
    public void FirstArrivalIsVisibleWhileTheRestIsPendingAndPreservesSelectionAndNativeHandle()
    {
        using var scope = new BrowserScope();
        var window = scope.Window;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Set(window, "searchStreamProvider", new Func<string, int, YtdlpResultType, CancellationToken, IAsyncEnumerable<YtdlpVideo>>((_, _, _, token) => Arrivals(token)));
        var list = Field<ListView>(window, "resultsList");
        IntPtr handle = list.Handle;
        Search(window, "streaming search");
        PumpUntil(() => list.Items.Count == 1);
        Task operation = Field<Task>(window, "browseOperation");
        Assert.IsFalse(operation.IsCompleted);
        var first = list.Items[0];
        first.Selected = true;
        first.Focused = true;
        release.SetResult();
        PumpBrowse(window);
        Assert.AreEqual(2, list.Items.Count);
        Assert.AreSame(first, list.Items[0]);
        Assert.IsTrue(first.Selected);
        Assert.AreSame(first, list.FocusedItem);
        Assert.AreEqual(handle, list.Handle);

        async IAsyncEnumerable<YtdlpVideo> Arrivals([EnumeratorCancellation] CancellationToken token)
        {
            yield return Video(1);
            await release.Task.WaitAsync(token);
            yield return Video(2);
            yield return Video(1);
        }
    }

    [STATestMethod]
    public void NewSearchDiscardsLateRowsFromACancelledOlderStream()
    {
        using var scope = new BrowserScope();
        var window = scope.Window;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Set(window, "searchStreamProvider", new Func<string, int, YtdlpResultType, CancellationToken, IAsyncEnumerable<YtdlpVideo>>((query, _, _, token) =>
            query == "old" ? OldArrivals() : Stream([Video(3)], token)));
        var list = Field<ListView>(window, "resultsList");
        Search(window, "old");
        PumpUntil(() => list.Items.Count == 1);
        Task previous = Field<Task>(window, "browseOperation");
        Search(window, "new");
        PumpBrowse(window);
        release.SetResult();
        PumpUntil(() => previous.IsCompleted);
        previous.GetAwaiter().GetResult();
        Assert.AreEqual(1, list.Items.Count);
        Assert.AreEqual(Video(3).Id, ((YtdlpVideo)list.Items[0].Tag!).Id);
        Assert.AreEqual("new", Field<TextBox>(window, "queryText").Text);

        async IAsyncEnumerable<YtdlpVideo> OldArrivals()
        {
            yield return Video(1);
            await release.Task; // Simulates a provider returning a late result despite cancellation.
            yield return Video(2);
        }
    }

    [STATestMethod]
    public void DoubleClickPlaylistStreamsAllVideosAndShowsItsNameAndCount()
    {
        using var scope = new BrowserScope();
        var window = scope.Window;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var list = Field<ListView>(window, "resultsList");
        Field<NumericUpDown>(window, "resultLimit").Value = 1;
        Set(window, "playlistVideosProvider", new Func<string, CancellationToken, IAsyncEnumerable<YtdlpVideo>>((url, token) =>
        {
            Assert.AreEqual(Playlist().Url, url);
            return PlaylistArrivals(token);
        }));
        Invoke(window, "PopulateResults", (object)new[] { Playlist() });
        list.Items[0].Selected = true;
        Invoke(window, "Result_DoubleClick", null, EventArgs.Empty);
        PumpUntil(() => list.Items.Count == 1 && list.Items[0].Text == Video(0).Title);
        Assert.IsFalse(Field<Task>(window, "browseOperation").IsCompleted);
        release.SetResult();
        PumpBrowse(window);
        Assert.AreEqual(205, list.Items.Count, "Playlist browsing must not silently stop at the search limit.");
        Assert.AreEqual("Playlist :: Synth playlist (205)", Field<TextBox>(window, "queryText").Text);
        Assert.IsTrue(list.Items.Cast<ListViewItem>().All(row => ((YtdlpVideo)row.Tag!).IsVideo));

        async IAsyncEnumerable<YtdlpVideo> PlaylistArrivals([EnumeratorCancellation] CancellationToken token)
        {
            yield return Video(0);
            await release.Task.WaitAsync(token);
            for (int index = 1; index < 205; index++) yield return Video(index);
        }
    }

    [STATestMethod]
    public void PlaylistContextEnqueueAddsAllVideosWithoutSwitchingTabsOrDuplicatingActiveJobs()
    {
        using var cancellation = new CancellationTokenSource();
        using var scope = new BrowserScope();
        var window = scope.Window;
        object queue = InitializeRunningBatch(window, cancellation);
        Field<NumericUpDown>(window, "resultLimit").Value = 1;
        Set(window, "playlistVideosProvider", new Func<string, CancellationToken, IAsyncEnumerable<YtdlpVideo>>((_, token) =>
            Stream(Enumerable.Range(0, 225).Select(Video).Concat([Video(3), Channel(), Playlist()]), token)));
        Invoke(window, "PopulateResults", (object)new[] { Playlist(), Video(3) });
        var list = Field<ListView>(window, "resultsList");
        list.Items[0].Selected = true;
        list.Items[1].Selected = true;
        var tabs = Field<TabControl>(window, "browsingTabs");
        var originalTab = tabs.SelectedTab;
        list.ContextMenuStrip!.Items[0].PerformClick();
        PumpUntil(() => Field<HashSet<Task>>(window, "playlistEnqueueTasks").Count == 0);
        Assert.AreSame(originalTab, tabs.SelectedTab);
        Assert.AreEqual(2, list.Items.Count, "Queueing a playlist must not replace the search results.");
        Assert.AreEqual(225, Field<ListView>(window, "downloadJobsList").Items.Count);
        Assert.AreEqual(224, (int)queue.GetType().GetProperty("Count")!.GetValue(queue)!);
        Assert.IsTrue(Field<ListView>(window, "downloadJobsList").Items.Cast<ListViewItem>()
            .All(row => ((YtdlpVideo)row.Tag!.GetType().GetProperty("Video")!.GetValue(row.Tag)!).IsVideo));
    }

    [STATestMethod]
    public void CancellingPlaylistExpansionAddsNoPartialOrStaleDownloads()
    {
        using var batchCancellation = new CancellationTokenSource();
        using var scope = new BrowserScope();
        var window = scope.Window;
        object queue = InitializeRunningBatch(window, batchCancellation);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Set(window, "playlistVideosProvider", new Func<string, CancellationToken, IAsyncEnumerable<YtdlpVideo>>((_, token) => Pending(token)));
        Invoke(window, "PopulateResults", (object)new[] { Playlist() });
        Field<ListView>(window, "resultsList").Items[0].Selected = true;
        Invoke(window, "DownloadEnqueue_Click", null, EventArgs.Empty);
        PumpUntil(() => entered.Task.IsCompleted);
        Invoke(window, "Cancel_Click", null, EventArgs.Empty);
        PumpUntil(() => Field<HashSet<Task>>(window, "playlistEnqueueTasks").Count == 0);
        Assert.AreEqual(1, Field<ListView>(window, "downloadJobsList").Items.Count);
        Assert.AreEqual(0, (int)queue.GetType().GetProperty("Count")!.GetValue(queue)!);

        async IAsyncEnumerable<YtdlpVideo> Pending([EnumeratorCancellation] CancellationToken token)
        {
            yield return Video(1);
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            yield return Video(2);
        }
    }

    [STATestMethod]
    public void QualityUpArrowRaisesMp3BitrateAndWavDepthAndBpmLabelIsCapitalized()
    {
        using var scope = new BrowserScope();
        var window = scope.Window;
        var format = Field<ComboBox>(window, "formatCombo");
        var quality = Field<DomainUpDown>(window, "audioQuality");
        format.SelectedIndex = 1;
        quality.SelectedItem = "256";
        quality.UpButton();
        Assert.AreEqual("320", quality.SelectedItem);
        quality.UpButton();
        Assert.AreEqual("320", quality.SelectedItem, "Do not wrap at maximum quality.");
        quality.DownButton();
        Assert.AreEqual("256", quality.SelectedItem);
        format.SelectedIndex = 0;
        quality.SelectedItem = "16";
        quality.UpButton();
        Assert.AreEqual("24", quality.SelectedItem);
        quality.UpButton();
        Assert.AreEqual("32", quality.SelectedItem);
        format.SelectedIndex = 1;
        Assert.AreEqual("256", quality.SelectedItem, "Changing formats keeps their independent quality settings.");
        Assert.AreEqual("Wait for BPM tag", Field<CheckBox>(window, "waitForBpmCheck").Text);
    }

    [STATestMethod]
    public void HistoryRemovesOldTestArtifactsAndSizesDropdownToRealEntriesOnly()
    {
        using var scope = new BrowserScope();
        string[] entries = ["omega synthesizer", "{Width=800, Height=600}Search_Click", "{Width=420, Height=260}Query_KeyDown", "Jazz Vibes"];
        File.WriteAllText(scope.HistoryPath, JsonSerializer.Serialize(entries));
        var history = new YoutubeSearchHistory(scope.HistoryPath);
        CollectionAssert.AreEqual(new[] { "omega synthesizer", "Jazz Vibes" }, history.Entries.ToArray());
        history.Add("{Width=600, Height=400}Search_Click");
        Assert.AreEqual(2, history.Entries.Count);
        Set(scope.Window, "searchHistory", history);
        Invoke(scope.Window, "ShowSearchHistory");
        var menu = Field<ContextMenuStrip>(scope.Window, "searchHistoryMenu");
        Assert.IsTrue(menu.AutoSize);
        Assert.AreEqual(2, menu.Items.Count);
        Assert.IsTrue(menu.Height < 120, "Two real searches must not reserve room for 32 entries.");
        menu.Close();
        for (int index = 0; index < 40; index++) history.Add("Music " + index);
        var loaded = new YoutubeSearchHistory(scope.HistoryPath);
        Assert.AreEqual(32, loaded.Entries.Count);
        Assert.AreEqual("Music 39", loaded.Entries[0]);
        Assert.AreEqual("Music 8", loaded.Entries[^1]);
    }

    [STATestMethod]
    public void HistoryRestoresTheResultFilterOfEachPage()
    {
        using var scope = new BrowserScope();
        var window = scope.Window;
        Set(window, "searchStreamProvider", new Func<string, int, YtdlpResultType, CancellationToken, IAsyncEnumerable<YtdlpVideo>>((_, _, _, token) =>
            Stream([Video(1), Playlist(), Channel()], token)));
        Search(window, "all query");
        PumpBrowse(window);
        Field<ComboBox>(window, "resultTypeCombo").SelectedIndex = (int)YtdlpResultType.Playlists;
        Search(window, "playlist query");
        PumpBrowse(window);
        Invoke(window, "NavigateResultsHistory", -1);
        Assert.AreEqual((int)YtdlpResultType.All, Field<ComboBox>(window, "resultTypeCombo").SelectedIndex);
        Assert.AreEqual(3, Field<ListView>(window, "resultsList").Items.Count);
        Invoke(window, "NavigateResultsHistory", 1);
        Assert.AreEqual((int)YtdlpResultType.Playlists, Field<ComboBox>(window, "resultTypeCombo").SelectedIndex);
        Assert.AreEqual(1, Field<ListView>(window, "resultsList").Items.Count);
    }

    private static object InitializeRunningBatch(YoutubeBrowserWindow window, CancellationTokenSource cancellation)
    {
        Set(window, "operationCancellation", cancellation);
        Set(window, "busy", true);
        object batch = Invoke(window, "InitializeBatch", (object)new[] { Video(0) })!;
        Type jobType = batch.GetType().GetProperty("Jobs")!.PropertyType.GetElementType()!;
        object queue = Activator.CreateInstance(typeof(Queue<>).MakeGenericType(jobType))!;
        batch.GetType().GetProperty("PendingQueue")!.SetValue(batch, queue);
        Field<TabControl>(window, "browsingTabs").SelectedTab = Field<TabPage>(window, "resultsTab");
        return queue;
    }

    private static YtdlpVideo Video(int index) => new()
    {
        Id = $"v{index:0000000000}", Title = $"Video {index}", Url = $"https://www.youtube.com/watch?v=v{index:0000000000}",
        UploadDate = new(2025, 1, 1)
    };

    private static YtdlpVideo Playlist() => new()
    { IsPlaylist = true, Id = "PL_synth", Title = "Synth playlist", Url = "https://www.youtube.com/playlist?list=PL_synth", PlaylistCount = 205 };

    private static YtdlpVideo Channel() => new()
    { IsChannel = true, Id = "UCsXVk37bltHxD1rDPwtNM8Q", Title = "Channel", Url = "https://www.youtube.com/channel/UCsXVk37bltHxD1rDPwtNM8Q/videos" };

    private static async IAsyncEnumerable<YtdlpVideo> Stream(IEnumerable<YtdlpVideo> videos, [EnumeratorCancellation] CancellationToken token)
    {
        foreach (var video in videos)
        {
            token.ThrowIfCancellationRequested();
            yield return video;
            await Task.Yield();
        }
    }

    private static void Search(YoutubeBrowserWindow window, string query)
    {
        Field<TextBox>(window, "queryText").Text = query;
        Invoke(window, "Search", false, false);
    }

    private static void PumpBrowse(YoutubeBrowserWindow window)
    {
        Task task = Field<Task>(window, "browseOperation");
        PumpUntil(() => task.IsCompleted);
        task.GetAwaiter().GetResult();
    }

    private static void PumpUntil(Func<bool> done)
    {
        var timer = Stopwatch.StartNew();
        while (!done() && timer.Elapsed < TimeSpan.FromSeconds(10)) { Application.DoEvents(); Thread.Sleep(1); }
        Assert.IsTrue(done(), "The asynchronous UI operation did not complete in time.");
    }

    private static T Field<T>(YoutubeBrowserWindow window, string name) =>
        (T)typeof(YoutubeBrowserWindow).GetField(name, PrivateInstance)!.GetValue(window)!;
    private static void Set(YoutubeBrowserWindow window, string name, object value) =>
        typeof(YoutubeBrowserWindow).GetField(name, PrivateInstance)!.SetValue(window, value);
    private static object? Invoke(YoutubeBrowserWindow window, string name, params object?[] args) =>
        typeof(YoutubeBrowserWindow).GetMethod(name, PrivateInstance)!.Invoke(window, args);

    private sealed class BrowserScope : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "opencode", "youtube-tests-" + Guid.NewGuid().ToString("N"));
        public string HistoryPath => Path.Combine(directory, "history.json");
        public YoutubeBrowserWindow Window { get; }
        public BrowserScope()
        {
            Directory.CreateDirectory(directory);
            Window = new YoutubeBrowserWindow(_ => Assert.Fail("No real audio import is expected."));
            _ = Window.Handle;
            _ = Field<ListView>(Window, "resultsList").Handle;
            Field<CheckBox>(Window, "thumbnailCheck").Checked = false;
            Set(Window, "searchHistory", new YoutubeSearchHistory(HistoryPath));
            Set(Window, "binariesReady", true);
            Set(Window, "sortColumn", -1);
            Invoke(Window, "ApplySort");
        }
        public void Dispose()
        {
            Window.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }
}
