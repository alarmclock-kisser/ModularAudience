using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Forms.Modules.Dialogs;
using ModularAudience.Ytdlp;
using System.Reflection;
using System.Windows.Forms;

namespace ModularAudience.Audio.Tests;

[TestClass]
[DoNotParallelize]
public sealed class YoutubeUploadDateTests
{
    [STATestMethod]
    public void AutomaticDates_KeepSelectedFocusedRowAndNativeHandleInMiniLayout()
    {
        using var window = new YoutubeBrowserWindow(_ => Assert.Fail());
        window.Size = new System.Drawing.Size(500, 300);
        _ = window.Handle;
        var list = (ListView)Field(window, "resultsList").GetValue(window)!;
        _ = list.Handle;
        YtdlpVideo[] videos = Enumerable.Range(0, 25).Select(index => new YtdlpVideo
        { Id = $"video{index:000000}", Title = $"Video {index:00}" }).ToArray();
        Invoke(window, "PopulateResults", videos);
        var selected = list.Items[10];
        int selectedVideoIndex = Array.FindIndex(videos, video => video.Id == ((YtdlpVideo)selected.Tag!).Id);
        selected.Selected = true;
        selected.Focused = true;
        list.TopItem = list.Items[8];
        var top = list.TopItem;
        IntPtr handle = list.Handle;
        int recreated = 0;
        list.HandleCreated += (_, _) => recreated++;
        Field(window, "sortColumn").SetValue(window, 6);
        Func<YtdlpVideo, CancellationToken, Task<DateOnly?>> provider = (video, token) =>
            Task.FromResult<DateOnly?>(new DateOnly(2024, 1, 1).AddDays(24 - Array.IndexOf(videos, video)));
        Field(window, "uploadDateProvider").SetValue(window, provider);
        Invoke(window, "StartUploadDates", videos);
        Assert.AreEqual(handle, list.Handle);
        Assert.AreEqual(0, recreated, "Background enrichment must not recreate the native list control.");
        Assert.IsTrue(selected.Selected);
        Assert.AreSame(selected, list.FocusedItem);
        Assert.AreSame(top, list.TopItem);
        Assert.AreEqual(new DateOnly(2024, 1, 1).AddDays(24 - selectedVideoIndex).ToString("yyyy-MM-dd"), selected.SubItems[6].Text);
    }

    [TestMethod]
    public void DateOutput_UsesExactCalendarDatesOnly()
    {
        Assert.AreEqual(new DateOnly(2024, 2, 29), YtdlpClient.ParseUploadDateOutput("WARNING: ignored\n20240229\n"));
        Assert.IsNull(YtdlpClient.ParseUploadDateOutput("NA\n2 years ago\n20230229\n"));
    }

    [STATestMethod]
    public void AutomaticDates_AllResultsEnrichedAndCached_WithoutFullMetadata()
    {
        using var window = new YoutubeBrowserWindow(_ => Assert.Fail());
        int calls = 0;
        Func<YtdlpVideo, CancellationToken, Task<DateOnly?>> provider = (video, token) =>
        {
            calls++;
            return Task.FromResult<DateOnly?>(new DateOnly(2024, 3, 4));
        };
        Field(window, "uploadDateProvider").SetValue(window, provider);
        YtdlpVideo[] videos =
        [
            new() { Id = "v586fXgj50Y", Title = "First" },
            new() { Id = "bqwhPlX7daw", Title = "Second" },
            new() { Id = "abcdefghijk", Title = "Known", UploadDate = new(2020, 1, 1) }
        ];
        Invoke(window, "PopulateResults", videos);
        Invoke(window, "StartUploadDates", videos);
        var list = (ListView)Field(window, "resultsList").GetValue(window)!;
        Assert.AreEqual(2, calls);
        ListViewItem Row(string id) => list.Items.Cast<ListViewItem>().Single(row => ((YtdlpVideo)row.Tag!).Id == id);
        Assert.AreEqual("2024-03-04", Row(videos[0].Id).SubItems[6].Text);
        Assert.AreEqual(new DateOnly(2024, 3, 4), ((YtdlpVideo)Row(videos[1].Id).Tag!).UploadDate);
        Assert.AreEqual("2020-01-01", Row(videos[2].Id).SubItems[6].Text);
        Invoke(window, "PopulateResults", videos);
        Invoke(window, "StartUploadDates", videos);
        Assert.AreEqual(2, calls, "Repeated searches must use cached exact dates.");
    }

    [STATestMethod]
    public void AutomaticDates_StaleGenerationCannotChangeNewRows()
    {
        using var window = new YoutubeBrowserWindow(_ => Assert.Fail());
        YtdlpVideo[] videos = [new() { Id = "v586fXgj50Y", Title = "New row" }];
        Invoke(window, "PopulateResults", videos);
        Field(window, "dateGeneration").SetValue(window, 2);
        Func<YtdlpVideo, CancellationToken, Task<DateOnly?>> provider = (_, _) => Task.FromResult<DateOnly?>(new DateOnly(2024, 1, 1));
        var method = window.GetType().GetMethod("EnrichUploadDatesAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ((Task)method.Invoke(window, [videos, provider, 1, CancellationToken.None])!).GetAwaiter().GetResult();
        var list = (ListView)Field(window, "resultsList").GetValue(window)!;
        Assert.AreEqual("Unknown", list.Items[0].SubItems[6].Text);
        Assert.IsNull(((YtdlpVideo)list.Items[0].Tag!).UploadDate);
    }

    private static FieldInfo Field(object target, string name) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static void Invoke(object target, string name, YtdlpVideo[] videos) =>
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, [videos]);
}
