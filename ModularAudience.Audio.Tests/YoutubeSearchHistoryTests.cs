using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Forms.Modules.Dialogs;
using ModularAudience.Ytdlp;
using System.Reflection;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class YoutubeSearchHistoryTests
{
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
