using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Forms.Modules.Dialogs;
using ModularAudience.Ytdlp;
using System.Reflection;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class YoutubeSearchHistoryTests
{
    [TestMethod]
    [TestCategory("YoutubeNetworkIntegration")]
    public async Task InstalledClientCanLoadRealSearchResults()
    {
        if (Environment.GetEnvironmentVariable("MODULARAUDIENCE_YOUTUBE_LIVE_TEST") != "1")
            Assert.Inconclusive("Enable MODULARAUDIENCE_YOUTUBE_LIVE_TEST for the live YouTube request.");
        var client = new YtdlpClient();
        var progress = new List<string>();
        client.ProgressChanged += (_, text) => { progress.Add(text); Console.WriteLine(text); };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var results = await client.SearchAsync("reggae", 3, timeout.Token);
        Assert.IsGreaterThan(0, results.Count);
        Assert.IsTrue(results.All(video => !string.IsNullOrWhiteSpace(video.Title)));
        Assert.IsFalse(progress.Any(line => line.StartsWith("Checking ", StringComparison.Ordinal)),
            "Installed tools must be discovered without hidden update checks during search.");
        Console.WriteLine($"Loaded {results.Count} actual YouTube search results.");
    }

    [STATestMethod]
    public void SearchAfterHistoryRecallPublishesResultsWithoutRebuildingAutocomplete()
    {
        string path = Path.Combine(Path.GetTempPath(), "history-search-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var history = new YoutubeSearchHistory(path); history.Add("reggae");
            using var window = new YoutubeBrowserWindow(_ => Assert.Fail());
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            Type type = typeof(YoutubeBrowserWindow);
            void Set(string name, object value) => type.GetField(name, flags)!.SetValue(window, value);
            object Get(string name) => type.GetField(name, flags)!.GetValue(window)!;
            _ = window.Handle;
            var query = (TextBox)Get("queryText"); _ = query.Handle;
            var originalSuggestions = query.AutoCompleteCustomSource;
            Set("searchHistory", history); Set("binariesReady", true);
            Set("searchProvider", new Func<string, int, CancellationToken, Task<IReadOnlyList<YtdlpVideo>>>(async (text, _, token) =>
            {
                await Task.Delay(20, token);
                return [new YtdlpVideo { Id = "abcdefghijk", Title = text, UploadDate = new(2025, 1, 1) }];
            }));
            query.Text = "reggae";
            type.GetMethod("Search", flags)!.Invoke(window, [false, false]);
            var task = (Task)Get("browseOperation");
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (!task.IsCompleted && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(1); }
            Assert.IsTrue(task.IsCompleted);
            task.GetAwaiter().GetResult();
            Assert.AreSame(originalSuggestions, query.AutoCompleteCustomSource, "Starting a search must not rebuild native autocomplete.");
            Assert.AreEqual("reggae", ((ListView)Get("resultsList")).Items[0].Text);
            Assert.IsFalse((bool)Get("browsing"));
        }
        finally { File.Delete(path); }
    }

    [STATestMethod]
    public void HistoryDropdownCanBeDismissedAndReopenedWithoutDisposingItsNativeHandle()
    {
        string path = Path.Combine(Path.GetTempPath(), "history-menu-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var history = new YoutubeSearchHistory(path);
            history.Add("First query"); history.Add("Second query");
            using var window = new YoutubeBrowserWindow(_ => Assert.Fail());
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            Type type = typeof(YoutubeBrowserWindow);
            type.GetField("searchHistory", flags)!.SetValue(window, history);
            _ = window.Handle;
            ContextMenuStrip? previous = null;
            for (int i = 0; i < 8; i++)
            {
                type.GetMethod("ShowSearchHistory", flags)!.Invoke(window, null);
                var menu = (ContextMenuStrip)type.GetField("searchHistoryMenu", flags)!.GetValue(window)!;
                Assert.HasCount(2, menu.Items);
                if (previous != null) Assert.AreSame(previous, menu);
                menu.Close(ToolStripDropDownCloseReason.Keyboard);
                Application.DoEvents();
                Assert.IsFalse(menu.IsDisposed, "Dismissal must not dispose a ToolStrip inside its close callback.");
                previous = menu;
            }
            window.Dispose();
            Assert.IsTrue(previous!.IsDisposed, "Browser disposal must release the retained dropdown.");
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void LocalHistoryIsPersistentBoundedAndMovesRepeatedSearchToFront()
    {
        string path = Path.Combine(Path.GetTempPath(), "search-history-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var history = new YoutubeSearchHistory(path);
            for (int i = 0; i < 40; i++) history.Add("Query " + i);
            history.Add(" query 12 ");
            var loaded = new YoutubeSearchHistory(path);
            Assert.HasCount(32, loaded.Entries);
            Assert.AreEqual("query 12", loaded.Entries[0]);
            Assert.AreEqual(1, loaded.Entries.Count(q => q.Equals("Query 12", StringComparison.OrdinalIgnoreCase)));
            Assert.IsFalse(loaded.Entries.Contains("Query 0"));
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void VideoParserRetainsCanonicalUploaderChannelUrl()
    {
        const string id = "UCsXVk37bltHxD1rDPwtNM8Q";
        var video = YtdlpParser.ParseVideos("{\"id\":\"abcdefghijk\",\"channel_id\":\"" + id + "\",\"uploader\":\"Test uploader\"}")[0];
        Assert.AreEqual("https://www.youtube.com/channel/" + id + "/videos", video.ChannelUrl);
        var handle = YtdlpParser.ParseVideos("{\"id\":\"abcdefghijk\",\"uploader_url\":\"https://www.youtube.com/@test\"}")[0];
        Assert.AreEqual("https://www.youtube.com/@test/videos", handle.ChannelUrl);
    }

    [STATestMethod]
    public void NavigateToVideoChannelPublishesChannelUploadsInsteadOfSearch()
    {
        using var window = new YoutubeBrowserWindow(_ => Assert.Fail());
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(YoutubeBrowserWindow);
        void Set(string key, object value) => type.GetField(key, flags)!.SetValue(window, value);
        object Get(string key) => type.GetField(key, flags)!.GetValue(window)!;
        _ = window.Handle;
        var list = (ListView)Get("resultsList"); _ = list.Handle;
        Set("binariesReady", true);
        var source = new YtdlpVideo { Id = "abcdefghijk", Title = "Source", Uploader = "Artist", ChannelUrl = "https://www.youtube.com/@artist/videos" };
        type.GetMethod("PopulateResults", flags)!.Invoke(window, [new[] { source }]);
        list.Items[0].Selected = true;
        string? navigated = null;
        Set("channelVideosProvider", new Func<string, int, CancellationToken, Task<IReadOnlyList<YtdlpVideo>>>((url, _, _) =>
        {
            navigated = url;
            return Task.FromResult<IReadOnlyList<YtdlpVideo>>([new() { Id = "0123456789_", Title = "Channel upload", UploadDate = new(2025, 1, 1) }]);
        }));
        type.GetMethod("NavigateToChannel_Click", flags)!.Invoke(window, [null, EventArgs.Empty]);
        var operation = (Task)Get("browseOperation");
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!operation.IsCompleted && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(1); }
        Assert.IsTrue(operation.IsCompleted);
        operation.GetAwaiter().GetResult();
        Assert.AreEqual(source.ChannelUrl, navigated);
        Assert.AreEqual("Channel upload", list.Items[0].Text);
        Assert.AreEqual("Channel :: Artist", ((TextBox)Get("queryText")).Text);
    }
}
