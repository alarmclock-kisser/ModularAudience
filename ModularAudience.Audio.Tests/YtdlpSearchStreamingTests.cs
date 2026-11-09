using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Ytdlp;
using System.Reflection;
using System.Text.Json;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class YtdlpSearchStreamingTests
{
    [TestMethod]
    public void MixedSearchEntriesPreservePlaylistAndChannelIdentityAndMetadata()
    {
        const string json = """
            {"entries":[
              {"id":"abcdefghijk","title":"Video","duration":123},
              {"id":"PL123456789","title":"Playlist","ie_key":"YoutubeTab",
               "url":"https://www.youtube.com/playlist?list=PL123456789&feature=share",
               "playlist_count":245,"uploader":"Artist","channel_id":"UCsXVk37bltHxD1rDPwtNM8Q",
               "thumbnail":"https://i.ytimg.com/playlist.jpg"},
              {"id":"UCsXVk37bltHxD1rDPwtNM8Q","title":"Channel","ie_key":"YoutubeTab"},
              {"id":"PL_unsafe","url":"https://evil.example/playlist?list=PL_unsafe"},null]}
            """;
        var all = YtdlpParser.ParseVideos(json, 30, includeChannels: true, includePlaylists: true);
        Assert.HasCount(3, all);
        Assert.IsTrue(all[0].IsVideo);
        Assert.IsTrue(all[1].IsPlaylist);
        Assert.IsFalse(all[1].IsVideo);
        Assert.AreEqual("https://www.youtube.com/playlist?list=PL123456789", all[1].Url);
        Assert.AreEqual(245L, all[1].PlaylistCount);
        Assert.AreEqual("Artist", all[1].Uploader);
        Assert.AreEqual("https://www.youtube.com/channel/UCsXVk37bltHxD1rDPwtNM8Q/videos", all[1].ChannelUrl);
        Assert.IsTrue(all[2].IsChannel);
        Assert.HasCount(1, YtdlpParser.ParseVideos(json));
        Assert.HasCount(2, YtdlpParser.ParseVideos(json, 30, includeChannels: true));
        Assert.HasCount(1, YtdlpParser.ParseVideos(JsonSerializer.Serialize(new
        { id = "PL_example", title = "Single playlist", url = "https://www.youtube.com/playlist?list=PL_example" }),
            includePlaylists: true));
    }

    [TestMethod]
    [DataRow(YtdlpResultType.All, null)]
    [DataRow(YtdlpResultType.Videos, "EgIQAQ==")]
    [DataRow(YtdlpResultType.Playlists, "EgIQAw==")]
    [DataRow(YtdlpResultType.Channels, "EgIQAg==")]
    public void SearchTypeUsesYoutubeServerSideFilterAndEscapesTheQuery(YtdlpResultType type, string? filter)
    {
        string url = YtdlpClient.CreateSearchUrl("  synth & drums + Ü  ", type);
        StringAssert.StartsWith(url, "https://www.youtube.com/results?search_query=synth%20%26%20drums%20%2B%20%C3%9C");
        if (filter is null) Assert.IsFalse(url.Contains("&sp="));
        else StringAssert.EndsWith(url, "&sp=" + Uri.EscapeDataString(filter));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => YtdlpClient.CreateSearchUrl("query", (YtdlpResultType)99));
    }

    [TestMethod]
    public async Task ProcessStreamsCompleteRawJsonBeforeExitAndCancelsWithoutWaitingForRemainingOutput()
    {
        string title = "Ä ♪ " + new string('x', 12000);
        string json = JsonSerializer.Serialize(new { id = "abcdefghijk", title });
        string script = "[Console]::Out.WriteLine('" + json.Replace("'", "''") + "'); [Console]::Out.Flush(); Start-Sleep -Seconds 30";
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var source = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Task<string> process = RunStreaming(script, (line, _) =>
        {
            received.TrySetResult(line);
            return ValueTask.CompletedTask;
        }, source.Token);
        try
        {
            string line = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual(title, YtdlpParser.ParseVideos(line).Single().Title, "Metadata must not use the 1024-character diagnostic truncation.");
            Assert.IsFalse(process.IsCompleted, "The first result must arrive while yt-dlp is still running.");
            source.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => process.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            source.Cancel();
            try { await process; } catch (OperationCanceledException) { }
        }
    }

    [TestMethod]
    public async Task ProcessStreamsMultipleLinesAndTheFinalEntryWithoutANewline()
    {
        var lines = new List<string>();
        const string script = "[Console]::Out.WriteLine('{\"id\":\"abcdefghijk\"}'); [Console]::Out.Write('{\"id\":\"0123456789_\"}'); [Console]::Out.Flush()";
        using var source = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        string captured = await RunStreaming(script, (line, _) =>
        {
            lines.Add(line);
            return ValueTask.CompletedTask;
        }, source.Token);
        Assert.AreEqual(string.Empty, captured, "Large playlists must not be accumulated as one giant process-output string.");
        CollectionAssert.AreEqual(new[] { "abcdefghijk", "0123456789_" }, lines.Select(line => YtdlpParser.ParseVideos(line).Single().Id).ToArray());
    }

    private static Task<string> RunStreaming(string script, Func<string, CancellationToken, ValueTask> onLine, CancellationToken token)
    {
        Type type = typeof(YtdlpClient).Assembly.GetType("ModularAudience.Ytdlp.YtdlpProcess")!;
        string executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        return (Task<string>)type.GetMethod("RunStreamingAsync", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null,
            [executable, new[] { "-NoProfile", "-NonInteractive", "-Command", script }, Path.GetTempPath(), null, onLine, token, null])!;
    }
}
