using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Forms.Modules.Dialogs;
using ModularAudience.Ytdlp;
using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;

namespace ModularAudience.Audio.Tests;

[TestClass]
[DoNotParallelize]
public sealed class YoutubeBrowserRepairTests
{
    [STATestMethod]
    public void Sort_NumericDurationViewsAndUploadDate_TogglesAndKeepsUnknownLast()
    {
        using var window = new YoutubeBrowserWindow(_ => Assert.Fail());
        _ = window.Handle;
        YtdlpVideo[] videos =
        [
            new() { Id = "abcdefghijk", Title = "Ten", DurationSeconds = 600, ViewCount = 100, UploadDate = new(2025, 1, 1) },
            new() { Id = "dQw4w9WgXcQ", Title = "Two", DurationSeconds = 120, ViewCount = 20, UploadDate = new(2024, 12, 31) },
            new() { Id = "0123456789_", Title = "Unknown" }
        ];
        Invoke(window, "PopulateResults", (object)videos);
        var list = Field<ListView>(window, "resultsList");
        _ = list.Handle;
        foreach (int column in new[] { 2, 3, 6 })
        {
            Invoke(window, "Column_Click", null, new ColumnClickEventArgs(column));
            Assert.AreEqual("Two", list.Items[0].Text);
            Assert.AreEqual("Unknown", list.Items[2].Text);
            StringAssert.EndsWith(list.Columns[column].Text, "▲");
            Invoke(window, "Column_Click", null, new ColumnClickEventArgs(column));
            Assert.AreEqual("Ten", list.Items[0].Text);
            Assert.AreEqual("Unknown", list.Items[2].Text);
            StringAssert.EndsWith(list.Columns[column].Text, "▼");
        }
        Assert.AreEqual("2025-01-01", list.Items[0].SubItems[6].Text);
    }

    [STATestMethod]
    public void AudioQuality_FormatSwitch_PreservesSeparateBitrateAndBitDepth()
    {
        using var window = new YoutubeBrowserWindow(_ => Assert.Fail());
        var format = Field<ComboBox>(window, "formatCombo");
        var quality = Field<DomainUpDown>(window, "audioQuality");
        format.SelectedIndex = 0;
        CollectionAssert.AreEqual(new[] { "8", "16", "24", "32" }, quality.Items.Cast<string>().ToArray());
        quality.SelectedItem = "24";
        format.SelectedIndex = 1;
        CollectionAssert.AreEqual(new[] { "64", "96", "128", "192", "256", "320" }, quality.Items.Cast<string>().ToArray());
        quality.SelectedItem = "256";
        format.SelectedIndex = 0;
        Assert.AreEqual("24", quality.SelectedItem);
        var options = (YtdlpDownloadOptions)Invoke(window, "CaptureDownloadOptions")!;
        Assert.AreEqual(24, options.WavBitDepth);
        Assert.AreEqual(256, options.Mp3Bitrate);
        Assert.IsTrue(quality.ReadOnly);
    }

    [STATestMethod]
    public void DetailsLayout_ResizingAndHidingImage_LeavesReadableMetadata()
    {
        using var window = new YoutubeBrowserWindow(_ => Assert.Fail());
        _ = window.Handle;
        _ = Field<TabControl>(window, "browsingTabs").Handle;
        window.WindowState = FormWindowState.Normal;
        window.Size = new Size(1180, 820);
        window.PerformLayout();
        var description = Field<TextBox>(window, "descriptionText");
        var check = Field<CheckBox>(window, "thumbnailCheck");
        check.Checked = true;
        window.PerformLayout();
        Assert.IsTrue(description.Height >= 90, $"Metadata height: {description.Height}");
        int initial = description.Height;
        window.Height += 200;
        window.PerformLayout();
        Assert.IsTrue(description.Height > initial);
        int withImage = description.Height;
        check.Checked = false;
        window.PerformLayout();
        Assert.IsTrue(description.Height > withImage);
        Assert.AreEqual(ScrollBars.Vertical, description.ScrollBars);
        check.Checked = true;
        window.Size = window.MinimumSize;
        window.PerformLayout();
        Assert.IsTrue(description.Height >= 90, $"Metadata height at minimum window size: {description.Height}");
    }

    [TestMethod]
    public void Settings_SixColumnsAndLegacyBitrate_MigrateWithoutLosingVisibility()
    {
        var type = typeof(YoutubeBrowserWindow).Assembly.GetType("ModularAudience.Forms.Modules.Dialogs.YoutubeBrowserSettings")!;
        var settings = JsonSerializer.Deserialize("""{"VisibleColumns":[true,false,true,false,true,true],"Mp3Bitrate":200,"Format":"mp3"}""", type)!;
        type.GetMethod("Normalize", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(settings, null);
        var columns = (bool[])type.GetProperty("VisibleColumns")!.GetValue(settings)!;
        CollectionAssert.AreEqual(new[] { true, false, true, false, true, true, true }, columns);
        Assert.AreEqual(192, type.GetProperty("Mp3Bitrate")!.GetValue(settings));
        Assert.AreEqual(16, type.GetProperty("WavBitDepth")!.GetValue(settings));
    }

    [TestMethod]
    public void Parser_UploadDate_ParsesCalendarDateWithoutInventingMissingDates()
    {
        var videos = YtdlpParser.ParseVideos("""{"entries":[{"id":"abcdefghijk","upload_date":"20240229"},{"id":"dQw4w9WgXcQ","upload_date":"20250229"},{"id":"0123456789_"}]}""");
        Assert.AreEqual(new DateOnly(2024, 2, 29), videos[0].UploadDate);
        Assert.IsNull(videos[1].UploadDate);
        Assert.IsNull(videos[2].UploadDate);
    }

    [TestMethod]
    public async Task Thumbnail_WebpFailure_FallsBackToJpegAndOwnsDecodedBitmap()
    {
        byte[] jpeg;
        using (var bitmap = new Bitmap(32, 18))
        using (var data = new MemoryStream())
        {
            bitmap.Save(data, ImageFormat.Jpeg);
            jpeg = data.ToArray();
        }
        using var handler = new ThumbnailHandler(jpeg);
        using var client = new HttpClient(handler);
        var type = typeof(YoutubeBrowserWindow).Assembly.GetType("ModularAudience.Forms.Modules.Dialogs.YoutubeThumbnailLoader")!;
        var warnings = new List<string>();
        var video = new YtdlpVideo { Id = "abcdefghijk", Url = "https://www.youtube.com/watch?v=abcdefghijk", ThumbnailUrl = "https://i.ytimg.com/vi/abcdefghijk/default.jpg" };
        var task = (Task<Bitmap>)type.GetMethod("LoadAsync", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [client, video, (Action<string>)warnings.Add, CancellationToken.None])!;
        using var result = await task;
        Assert.AreEqual(2, handler.Requests);
        Assert.AreEqual(320, result.Width);
        _ = result.GetPixel(0, 0);
        Assert.HasCount(1, warnings);
        StringAssert.Contains(warnings[0], "Unsupported thumbnail encoding (image/webp)");
    }

    private sealed class ThumbnailHandler(byte[] jpeg) : HttpMessageHandler
    {
        internal int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests++;
            Assert.AreEqual("https", request.RequestUri!.Scheme);
            Assert.AreEqual("i.ytimg.com", request.RequestUri.Host);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Requests == 1 ? "RIFF0000WEBPunsupported"u8.ToArray() : jpeg)
            };
            response.Content.Headers.ContentType = new(Requests == 1 ? "image/webp" : "image/jpeg");
            return Task.FromResult(response);
        }
    }

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static object? Invoke(object target, string name, params object?[] values) =>
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, values);
}
