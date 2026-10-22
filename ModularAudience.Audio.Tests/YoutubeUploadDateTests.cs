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
        Assert.AreEqual("2024-03-04", list.Items[0].SubItems[6].Text);
        Assert.AreEqual(new DateOnly(2024, 3, 4), ((YtdlpVideo)list.Items[1].Tag!).UploadDate);
        Assert.AreEqual("2020-01-01", list.Items[2].SubItems[6].Text);
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
