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
    public void SearchInSmallLayouts_EnterAndSearchButtonPublishResultsAndSelectResultsTab()
    {
        using var window = new YoutubeBrowserWindow(_ => Assert.Fail());
        _ = window.Handle;
        _ = Field<ListView>(window, "resultsList").Handle;
        SetField(window, "binariesReady", true);
        int searches = 0;
        SetField(window, "searchProvider", new Func<string, int, CancellationToken, Task<IReadOnlyList<YtdlpVideo>>>((query, limit, token) =>
        {
            searches++;
            return Task.FromResult<IReadOnlyList<YtdlpVideo>>([new() { Id = "abcdefghijk", Title = query, UploadDate = new(2025, 1, 1) }]);
        }));
        foreach (var size in new[] { new System.Drawing.Size(600, 400), new System.Drawing.Size(420, 260), new System.Drawing.Size(800, 600) })
        {
            window.Size = size;
            window.PerformLayout();
            Invoke(window, "RefreshAvailability");
            foreach (string handler in new[] { "Query_KeyDown", "Search_Click" })
            {
                Field<TextBox>(window, "queryText").Text = size + handler;
                if (handler == "Query_KeyDown") Invoke(window, handler, null, new KeyEventArgs(Keys.Enter));
                else Invoke(window, handler, null, EventArgs.Empty);
                var timer = Stopwatch.StartNew();
                while (!Field<Task>(window, "browseOperation").IsCompleted && timer.Elapsed < TimeSpan.FromSeconds(5)) Application.DoEvents();
                Assert.IsTrue(Field<Task>(window, "browseOperation").IsCompleted);
                Assert.AreEqual(size + handler, Field<ListView>(window, "resultsList").Items[0].Text);
                Assert.AreSame(Field<TabPage>(window, "resultsTab"), Field<TabControl>(window, "browsingTabs").SelectedTab);
            }
        }
        Assert.AreEqual(6, searches);
    }

    [STATestMethod]
    public void RunningDownloads_LeaveBrowsingAndHistoryAvailable_AndChannelRowsHaveNoVideoMetrics()
    {
        using var cancellation = new CancellationTokenSource();
        using var window = new YoutubeBrowserWindow(_ => Assert.Fail());
        _ = window.Handle;
        _ = Field<ListView>(window, "resultsList").Handle;
        SetField(window, "operationCancellation", cancellation);
        SetField(window, "binariesReady", true);
        SetField(window, "busy", true);
        Invoke(window, "InitializeBatch", (object)new[] { new YtdlpVideo { Title = "Downloading" } });
        Invoke(window, "RefreshAvailability");
        Assert.IsTrue(Field<TableLayoutPanel>(window, "queryPanel").Enabled);
        var channel = new YtdlpVideo { IsChannel = true, Id = "UCsXVk37bltHxD1rDPwtNM8Q", Title = "Channel", Url = "https://www.youtube.com/channel/UCsXVk37bltHxD1rDPwtNM8Q/videos" };
        Invoke(window, "PublishResults", new[] { channel }, "channel search", "Channels", false);
        var row = Field<ListView>(window, "resultsList").Items[0];
        foreach (int index in new[] { 2, 3, 6 }) Assert.AreEqual("", row.SubItems[index].Text);
        Invoke(window, "PublishResults", new[] { new YtdlpVideo { Title = "Video" } }, "Channel :: Channel", "Channel videos", false);
        Invoke(window, "NavigateResultsHistory", -1);
        Assert.AreSame(channel, Field<ListView>(window, "resultsList").Items[0].Tag);
    }

    [TestMethod]
    public void ChannelParser_RecognizesChannelEntriesAndCanonicalizesVideosUrl()
    {
        const string id = "UCsXVk37bltHxD1rDPwtNM8Q";
        var entries = YtdlpParser.ParseVideos("{\"entries\":[{\"id\":\"" + id + "\",\"title\":\"Kurzgesagt\",\"ie_key\":\"YoutubeTab\"}]}", 30, true);
        Assert.HasCount(1, entries);
        Assert.IsTrue(entries[0].IsChannel);
        Assert.AreEqual("https://www.youtube.com/channel/" + id + "/videos", YtdlpParser.ValidateYoutubeUrl(entries[0].Url).AbsoluteUri);
        Assert.AreEqual("https://www.youtube.com/@kurzgesagt/videos", YtdlpParser.ValidateYoutubeUrl("https://www.youtube.com/@kurzgesagt").AbsoluteUri);
    }

    [STATestMethod]
    public void EnqueueRunningBatch_AddsJobsWithoutChangingResultsTabOrDuplicatingActiveVideo()
    {
        using var cancellation = new CancellationTokenSource();
        using var window = new YoutubeBrowserWindow(_ => Assert.Fail());
        SetField(window, "operationCancellation", cancellation);
        SetField(window, "binariesReady", true);
        SetField(window, "busy", true);
        var first = new YtdlpVideo { Id = "abcdefghijk", Title = "First" };
        var second = new YtdlpVideo { Id = "dQw4w9WgXcQ", Title = "Second" };
        var batch = Invoke(window, "InitializeBatch", (object)new[] { first })!;
        var jobType = batch.GetType().GetProperty("Jobs")!.PropertyType.GetElementType()!;
        var queue = Activator.CreateInstance(typeof(Queue<>).MakeGenericType(jobType))!;
        SetProperty(batch, "PendingQueue", queue);
        var tabs = Field<TabControl>(window, "browsingTabs");
        tabs.SelectedTab = Field<TabPage>(window, "resultsTab");
        Invoke(window, "EnqueueDownloadVideos", batch, new[] { first, second });
        Assert.AreEqual(2, Field<ListView>(window, "downloadJobsList").Items.Count);
        Assert.AreEqual(1, (int)queue.GetType().GetProperty("Count")!.GetValue(queue)!);
        Assert.AreSame(Field<TabPage>(window, "resultsTab"), tabs.SelectedTab);
        Assert.AreEqual(2, Property<Array>(batch, "Jobs").Length);
    }

    [STATestMethod]
    public void ResultsHistory_RestoresPagesAndDiscardsForwardBranchAfterNewSearch()
    {
        using var window = new YoutubeBrowserWindow(_ => Assert.Fail());
        _ = window.Handle;
        _ = Field<ListView>(window, "resultsList").Handle;
        var limit = Field<NumericUpDown>(window, "resultLimit");
        limit.Value = 64;
        Invoke(window, "PublishResults", new YtdlpVideo[] { new() { Id = "abcdefghijk", Title = "First" } }, "first query", "First page", false);
        limit.Value = 128;
        Invoke(window, "PublishResults", new YtdlpVideo[] { new() { Id = "dQw4w9WgXcQ", Title = "Related" } }, "related query", "Related page", true);
        Invoke(window, "NavigateResultsHistory", -1);
        Assert.AreEqual("First", Field<ListView>(window, "resultsList").Items[0].Text);
        Assert.AreEqual("first query", Field<TextBox>(window, "queryText").Text);
        Assert.AreEqual(64m, limit.Value);
        Invoke(window, "NavigateResultsHistory", 1);
        Assert.AreEqual("Related", Field<ListView>(window, "resultsList").Items[0].Text);
        Assert.AreEqual(128m, limit.Value);
        Invoke(window, "NavigateResultsHistory", -1);
        Invoke(window, "PublishResults", new YtdlpVideo[] { new() { Id = "third123456", Title = "Third" } }, "third", "Third page", false);
        Invoke(window, "NavigateResultsHistory", 1);
        Assert.AreEqual("Third", Field<ListView>(window, "resultsList").Items[0].Text);
    }

    [STATestMethod]
    public void BpmWaitToggle_ScalesWaitAndRelatedActionsRequireSelection()
    {
        using var window = new YoutubeBrowserWindow(_ => Assert.Fail());
        var wait = Field<NumericUpDown>(window, "cooldownSeconds");
        var check = Field<CheckBox>(window, "waitForBpmCheck");
        check.Checked = false;
        wait.Value = 5;
        check.Checked = true;
        Assert.AreEqual(20m, wait.Value);
        check.Checked = false;
        Assert.AreEqual(5m, wait.Value);
        Assert.IsFalse(Field<Button>(window, "showRelatedButton").Enabled);
        var menu = Field<ContextMenuStrip>(window, "columnsMenu");
        Assert.AreEqual("Download enqueue", menu.Items[0].Text);
        Assert.AreEqual("Show related / next-up", menu.Items[2].Text);
    }

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
        Assert.IsTrue(split.Panel2Collapsed, "At the new minimum size the compact layout hides the detail panel.");
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
        CollectionAssert.AreEqual(new[] { "Title", "Phase", "Download", "Elapsed", "ETA", "Speed", "Transferred" },
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
            StringAssert.Contains(row.SubItems[6].Text, "MB");
            StringAssert.Contains(Field<Label>(window, "etaLabel").Text, "00:00:06");
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
