using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Forms.Modules.Dialogs;
using ModularAudience.Ytdlp;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Windows.Forms;

namespace ModularAudience.Audio.Tests;

[TestClass]
[DoNotParallelize]
public sealed class YoutubeBrowserWindowTests
{
    [STATestMethod]
    public void Constructor_ResizableLayout_CanResizeWithoutNetworkOrImport()
    {
        int imported = 0;
        using var window = new YoutubeBrowserWindow(_ => imported++);
        _ = window.Handle;
        _ = Field<TabControl>(window, "browsingTabs").Handle;
        window.WindowState = FormWindowState.Normal;
        window.Size = window.MinimumSize;
        window.PerformLayout();
        var split = Field<SplitContainer>(window, "mainSplit");
        Assert.AreEqual(FormBorderStyle.Sizable, window.FormBorderStyle);
        Assert.IsTrue(split.Panel1.Width >= split.Panel1MinSize);
        Assert.IsTrue(split.Panel2.Width >= split.Panel2MinSize);
        int originalWidth = split.Width;
        window.Width += 400;
        window.PerformLayout();
        Assert.IsTrue(split.Width > originalWidth);
        Assert.IsFalse(window.Visible);
        Assert.AreEqual(0, imported);
    }

    [STATestMethod]
    public void PopulateResults_PreservesMetadataAndAllowsBatchSelection()
    {
        using var window = new YoutubeBrowserWindow(_ => Assert.Fail("No import was requested."));
        YtdlpVideo[] videos =
        [
            new() { Id = "dQw4w9WgXcQ", Title = "First", Url = "https://www.youtube.com/watch?v=dQw4w9WgXcQ", Uploader = "Channel", DurationSeconds = 125 },
            new() { Id = "abcdefghijk", Title = "Second", Url = "https://www.youtube.com/watch?v=abcdefghijk", Uploader = "Another channel" }
        ];
        typeof(YoutubeBrowserWindow).GetMethod("PopulateResults", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [videos]);
        var list = Field<ListView>(window, "resultsList");
        Assert.AreEqual(2, list.Items.Count);
        Assert.AreEqual("First", list.Items[0].Text);
        Assert.AreEqual("Channel", list.Items[0].SubItems[1].Text);
        Assert.AreEqual("2:05", list.Items[0].SubItems[2].Text);
        Assert.AreSame(videos[1], list.Items[1].Tag);
        Assert.IsTrue(list.MultiSelect);
        Assert.AreEqual(7, list.Columns.Count);
    }

    [STATestMethod]
    public void Constructor_ParallelControlsAndTimer_ExposeJobTimingWithoutWrappingHours()
    {
        using var window = new YoutubeBrowserWindow(_ => Assert.Fail("No import was requested."));
        var parallel = Field<NumericUpDown>(window, "maxParallelDownloadsNumber");
        Assert.AreEqual(1m, parallel.Minimum);
        Assert.AreEqual(8m, parallel.Maximum);
        var list = Field<ListView>(window, "downloadJobsList");
        Assert.IsTrue(list.OwnerDraw);
        Assert.AreEqual(View.Details, list.View);
        CollectionAssert.AreEqual(new[] { "Title", "Phase", "Download", "Elapsed", "ETA", "Speed" },
            list.Columns.Cast<ColumnHeader>().Select(column => column.Text).ToArray());
        var elapsed = Field<Label>(window, "elapsedLabel");
        var eta = Field<Label>(window, "etaLabel");
        elapsed.Text = "Timer has not refreshed";
        eta.Text = "Timer has not refreshed";
        var clock = Field<Stopwatch>(window, "operationClock");
        clock.Start();
        Invoke(window, "ProgressTimer_Tick", null, EventArgs.Empty);
        clock.Stop();
        StringAssert.StartsWith(elapsed.Text, "Elapsed: ");
        Assert.AreEqual("ETA: unknown", eta.Text);
        Assert.AreEqual(3, elapsed.Text["Elapsed: ".Length..].Split(':').Length);
        var format = typeof(YoutubeBrowserWindow).GetMethod("FormatClock", BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.AreEqual("25:02:03", format.Invoke(null, [TimeSpan.FromHours(25) + TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(3)]));
        Assert.IsFalse(window.Visible);
    }

    [STATestMethod]
    public void DownloadProgress_FinishedTransfer_IsSeparateFromTerminalBatchCompletion()
    {
        var previousContext = SynchronizationContext.Current;
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            using var cancellation = new CancellationTokenSource();
            using var window = new YoutubeBrowserWindow(_ => Assert.Fail("No import was requested."));
            using var context = new WindowsFormsSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(context);
            SetField(window, "operationCancellation", cancellation);
            SetField(window, "operationGeneration", 7);
            YtdlpVideo[] videos = [new() { Title = "First" }, new() { Title = "Second" }];
            var batch = Invoke(window, "InitializeBatch", (object)videos)!;
            Assert.AreSame(cancellation, Property<CancellationTokenSource>(batch, "Source"));
            Assert.AreEqual(7, Property<int>(batch, "Generation"));
            var list = Field<ListView>(window, "downloadJobsList");
            Assert.AreEqual(2, list.Items.Count);
            var row = list.Items[0];
            var job = row.Tag!;
            Assert.AreEqual("Queued", row.SubItems[1].Text);
            Assert.AreEqual("Unknown", row.SubItems[2].Text);
            Assert.AreSame(videos[0], Property<YtdlpVideo>(job, "Video"));
            SetProperty(job, "Started", true);
            SetProperty(job, "NetworkStage", true);
            Property<Stopwatch>(job, "Clock").Start();
            var callback = (IProgress<YtdlpDownloadProgress>)Invoke(window, "CreateDownloadProgress", batch, job)!;
            ReportAndPump(callback, job, new YtdlpDownloadProgress
            {
                Status = "downloading", DownloadedBytes = 1024, TotalBytes = 4096,
                Percent = 25, SpeedBytesPerSecond = 512, Eta = TimeSpan.FromSeconds(6)
            });
            Assert.AreEqual("Downloading", row.SubItems[1].Text);
            Assert.AreEqual("25.0%", row.SubItems[2].Text);
            Assert.AreEqual("00:00:06", row.SubItems[4].Text);
            Assert.AreEqual("0.5 KiB/s", row.SubItems[5].Text);
            var overall = Field<ProgressBar>(window, "operationProgress");
            Assert.AreEqual(0, overall.Value);

            ReportAndPump(callback, job, new YtdlpDownloadProgress { Status = "finished", Percent = 100 });
            Assert.AreEqual("Converting / saving", row.SubItems[1].Text);
            Assert.AreEqual("100.0%", row.SubItems[2].Text);
            Assert.AreEqual("Unknown", row.SubItems[4].Text);
            Assert.AreEqual("--", row.SubItems[5].Text);
            Assert.IsTrue(Property<bool>(job, "DownloadFinished"));
            Assert.IsFalse(Property<bool>(job, "Terminal"));
            Assert.AreEqual(0, overall.Value, "A finished transfer is not a completed import.");
            Invoke(window, "CompleteJob", job, "Completed");
            Invoke(window, "ProgressTimer_Tick", null, EventArgs.Empty);
            Assert.IsTrue(Property<bool>(job, "Terminal"));
            Assert.AreEqual("Completed", row.SubItems[1].Text);
            Assert.AreEqual(overall.Maximum / 2, overall.Value);
            Invoke(window, "CompleteJob", list.Items[1].Tag!, "Completed");
            Invoke(window, "ProgressTimer_Tick", null, EventArgs.Empty);
            Assert.AreEqual(overall.Maximum, overall.Value);
            StringAssert.StartsWith(Field<Label>(window, "batchProgressLabel").Text, "Completed: 2/2");
            Assert.IsFalse(window.Visible);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    private static void ReportAndPump(IProgress<YtdlpDownloadProgress> callback, object job, YtdlpDownloadProgress progress)
    {
        callback.Report(progress);
        var deadline = Stopwatch.StartNew();
        while (!ReferenceEquals(progress, Property<YtdlpDownloadProgress?>(job, "Latest")) && deadline.Elapsed < TimeSpan.FromSeconds(5))
            Application.DoEvents();
        Assert.AreSame(progress, Property<YtdlpDownloadProgress?>(job, "Latest"), "The UI progress callback did not arrive within five seconds.");
    }

    private static object? Invoke(YoutubeBrowserWindow window, string name, params object?[] arguments) =>
        typeof(YoutubeBrowserWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, arguments);

    private static void SetField(YoutubeBrowserWindow window, string name, object value) =>
        typeof(YoutubeBrowserWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, value);

    private static T Property<T>(object target, string name) =>
        (T)target.GetType().GetProperty(name)!.GetValue(target)!;

    private static void SetProperty(object target, string name, object value) =>
        target.GetType().GetProperty(name)!.SetValue(target, value);

    private static T Field<T>(YoutubeBrowserWindow window, string name) =>
        (T)typeof(YoutubeBrowserWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
}
