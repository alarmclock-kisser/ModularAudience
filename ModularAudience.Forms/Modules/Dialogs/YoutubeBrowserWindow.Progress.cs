using ModularAudience.Audio;
using ModularAudience.Ytdlp;
using System.Diagnostics;

namespace ModularAudience.Forms.Modules.Dialogs;

public partial class YoutubeBrowserWindow
{
    private readonly Stopwatch operationClock = new();
    private int operationGeneration;
    private DownloadBatch? activeBatch;

    private sealed class DownloadJob(YtdlpVideo video, int index, ListViewItem row)
    {
        public YtdlpVideo Video { get; } = video;
        public int Index { get; } = index;
        public ListViewItem Row { get; } = row;
        public Stopwatch Clock { get; } = new();
        public string Status { get; set; } = "Queued";
        public YtdlpDownloadProgress? Latest { get; set; }
        public long LastProgressTimestamp { get; set; }
        public bool Started { get; set; }
        public bool Terminal { get; set; }
        public bool NetworkStage { get; set; }
        public bool AudioStage { get; set; }
        public bool DownloadFinished { get; set; }
        public bool HandedOff { get; set; }
        public Exception? Error { get; set; }
        public int Attempt { get; set; }
        public TimeSpan? CooldownDeadline { get; set; }
        public string? LastLog { get; set; }
        public long LastLogTimestamp { get; set; }
    }

    private sealed class DownloadBatch(DownloadJob[] jobs, CancellationTokenSource source, int generation)
    {
        public DownloadJob[] Jobs { get; } = jobs;
        public CancellationTokenSource Source { get; } = source;
        public int Generation { get; } = generation;
        public bool Cancelled { get; set; }
        public bool Settled { get; set; }
    }

    private DownloadBatch InitializeBatch(YtdlpVideo[] videos)
    {
        var jobs = new DownloadJob[videos.Length];
        downloadJobsList.BeginUpdate();
        try
        {
            downloadJobsList.Items.Clear();
            for (int index = 0; index < videos.Length; index++)
            {
                var row = new ListViewItem(videos[index].Title);
                row.SubItems.AddRange(["Queued", "Unknown", "--", "Unknown", "--"]);
                jobs[index] = new DownloadJob(videos[index], index, row);
                row.Tag = jobs[index];
                downloadJobsList.Items.Add(row);
            }
        }
        finally { downloadJobsList.EndUpdate(); }
        var batch = new DownloadBatch(jobs, operationCancellation!, operationGeneration);
        activeBatch = batch;
        browsingTabs.SelectedTab = downloadsTab;
        operationProgress.Style = ProgressBarStyle.Continuous;
        operationProgress.Value = 0;
        operationProgress.AccessibleName = "Completed jobs, including failed and cancelled jobs";
        RefreshBatchProgress(batch);
        return batch;
    }

    private bool CanUpdateJob(DownloadBatch batch, DownloadJob job) =>
        !closing && !IsDisposed && !resourcesReleased && !job.Terminal && !batch.Settled &&
        ReferenceEquals(activeBatch, batch) && ReferenceEquals(operationCancellation, batch.Source) &&
        operationGeneration == batch.Generation;

    private IProgress<YtdlpDownloadProgress> CreateDownloadProgress(DownloadBatch batch, DownloadJob job)
    {
        int attempt = job.Attempt;
        return new Progress<YtdlpDownloadProgress>(progress =>
        {
            if (job.Attempt != attempt || !CanUpdateJob(batch, job) || !job.NetworkStage || job.DownloadFinished) return;
            job.Latest = progress;
            job.LastProgressTimestamp = Environment.TickCount64;
            if (progress.Status == "finished")
            {
                job.DownloadFinished = true;
                job.Status = "Converting / saving";
            }
            else if (progress.Status == "downloading") job.Status = "Downloading";
            RefreshJob(job);
            RefreshBatchProgress(batch);
        });
    }

    private IProgress<string> CreateJobTextProgress(DownloadBatch batch, DownloadJob job, bool audio)
    {
        int attempt = job.Attempt;
        return new Progress<string>(text =>
        {
            if (job.Attempt != attempt || !CanUpdateJob(batch, job) || (audio ? !job.AudioStage : !job.NetworkStage)) return;
            if (!audio && text.StartsWith(YtdlpRetryPolicy.ProgressPrefix, StringComparison.Ordinal))
            {
                job.DownloadFinished = false;
                job.Latest = null;
                job.Status = "Retry waiting / refreshing URLs";
                RefreshJob(job);
                LogJobProgress(batch, job, text[YtdlpRetryPolicy.ProgressPrefix.Length..], warning: true, force: true);
                return;
            }
            if (text.StartsWith(YtdlpProgressParser.Prefix, StringComparison.Ordinal)) return;
            bool warning = text.StartsWith("Warning:", StringComparison.OrdinalIgnoreCase);
            string? phase = audio ? AudioPhase(text) : DownloadPhase(text);
            if (phase is not null)
            {
                job.Status = phase;
                if (!audio) job.DownloadFinished = true;
                RefreshJob(job);
                RefreshBatchProgress(batch);
            }
            if (warning || phase is not null) LogJobProgress(batch, job, warning ? text : phase!, warning);
        });
    }

    private static string? DownloadPhase(string text) =>
        text.StartsWith("[ExtractAudio]", StringComparison.Ordinal) ||
        text.StartsWith("[Merger]", StringComparison.Ordinal) ||
        text.StartsWith("[Fixup", StringComparison.Ordinal) ||
        text.StartsWith("[MoveFiles]", StringComparison.Ordinal) ||
        text.StartsWith("[Metadata]", StringComparison.Ordinal) ? "Converting / saving" : null;

    private static string? AudioPhase(string text) => text switch
    {
        "Loading local audio." => "Loading audio",
        "Scanning source BPM." => "Scanning source BPM",
        "Stretching audio to the target BPM." => "Stretching audio",
        "Trimming silence." => "Trimming silence",
        "Audio import completed." => "Transferring audio",
        _ => null
    };

    private void LogJobProgress(DownloadBatch batch, DownloadJob job, string message, bool warning, bool force = false)
    {
        long now = Environment.TickCount64;
        if (job.LastLog == message || (!force && job.LastLog is not null && now - job.LastLogTimestamp < 1000)) return;
        job.LastLog = message;
        job.LastLogTimestamp = now;
        string line = message.Replace('\r', ' ').Replace('\n', ' ');
        if (line.Length > 240) line = line[..240];
        string tagged = $"[{job.Index + 1}/{batch.Jobs.Length}] {job.Video.Title}: {line}";
        AppendLog(tagged);
        if (warning) LogManager.LogWarning("YouTube browser: " + tagged);
    }

    private void RecordJobFailure(DownloadJob job, Exception error)
    {
        job.Error = job.Error is null ? error : new AggregateException(job.Error, error);
        job.Status = "Failed";
        LogManager.LogError($"YouTube browser: video import failed [{job.Video.Id}]. {error}");
        if (!IsDisposed && !resourcesReleased)
            AppendLog($"Error: {job.Video.Title}: {error.Message}. Other jobs will continue.");
    }

    private void CompleteJob(DownloadJob job, string status)
    {
        if (job.Terminal) return;
        job.Terminal = true;
        job.NetworkStage = false;
        job.AudioStage = false;
        job.CooldownDeadline = null;
        job.Status = status;
        job.Clock.Stop();
        RefreshJob(job);
    }

    private void ProgressTimer_Tick(object? sender, EventArgs e)
    {
        if (IsDisposed || resourcesReleased) return;
        if (activeBatch is { } batch)
            foreach (var job in batch.Jobs) RefreshJob(job);
        RefreshOperationTiming();
    }

    private void RefreshOperationTiming()
    {
        if (IsDisposed || resourcesReleased) return;
        elapsedLabel.Text = "Elapsed: " + FormatClock(operationClock.Elapsed);
        if (activeBatch is { } batch) RefreshBatchProgress(batch);
        else etaLabel.Text = "ETA: unknown";
    }

    private void RefreshBatchProgress(DownloadBatch batch)
    {
        if (IsDisposed || resourcesReleased || !ReferenceEquals(activeBatch, batch)) return;
        int completed = batch.Jobs.Count(job => job.Terminal);
        int downloading = batch.Jobs.Count(job => !job.Terminal && job.NetworkStage && !job.DownloadFinished && job.Status == "Downloading");
        int failed = batch.Jobs.Count(job => job.Terminal && job.Error is not null);
        operationProgress.Value = batch.Jobs.Length == 0 ? 0 : (int)((long)completed * operationProgress.Maximum / batch.Jobs.Length);
        batchProgressLabel.Text = $"Completed: {completed}/{batch.Jobs.Length} | Downloading: {downloading} | Failed: {failed}";
        if (batch.Cancelled || batch.Source.IsCancellationRequested)
            etaLabel.Text = "Batch ETA (estimate): cancelled";
        else
        {
            var eta = CalculateBatchEta(operationClock.Elapsed, completed, batch.Jobs.Length);
            etaLabel.Text = "Batch ETA (estimate): " + (eta is { } remaining ? FormatClock(remaining) : "unknown");
        }
    }

    private static TimeSpan? CalculateBatchEta(TimeSpan elapsed, int completed, int total)
    {
        if (completed <= 0 || total < completed || elapsed < TimeSpan.Zero) return null;
        decimal ticks = (decimal)elapsed.Ticks / completed * (total - completed);
        return TimeSpan.FromTicks((long)Math.Min(ticks, long.MaxValue));
    }

    private static string FormatClock(TimeSpan time) =>
        $"{(long)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}";

    private static TimeSpan? JobEta(DownloadJob job)
    {
        if (job.Terminal) return null;
        if (job.CooldownDeadline is { } deadline)
            return TimeSpan.FromTicks(Math.Max(0, (deadline - job.Clock.Elapsed).Ticks));
        return job.NetworkStage && !job.DownloadFinished ? job.Latest?.Eta : null;
    }

    private static double? DownloadPercent(DownloadJob job)
    {
        if (job.DownloadFinished) return 100;
        return job.Latest is { TotalBytes: > 0 } progress
            ? Math.Clamp(100d * progress.DownloadedBytes / progress.TotalBytes.Value, 0, 100) : null;
    }

    private static string DownloadText(DownloadJob job) => DownloadPercent(job) is { } percent
        ? $"{(job.Latest?.IsTotalEstimated == true ? "~" : "")}{percent:0.0}%" : "Unknown";

    private static string JobSpeed(DownloadJob job)
    {
        if (job.Terminal || !job.NetworkStage || job.DownloadFinished) return "--";
        if (job.Latest?.SpeedBytesPerSecond is not { } speed || !double.IsFinite(speed) || speed <= 0)
            return "Unknown";
        return speed >= 1024 * 1024 ? $"{speed / (1024 * 1024):0.0} MiB/s" : $"{speed / 1024:0.0} KiB/s";
    }

    private void RefreshJob(DownloadJob job)
    {
        if (IsDisposed || resourcesReleased) return;
        job.Row.SubItems[1].Text = job.Status;
        job.Row.SubItems[2].Text = DownloadText(job);
        job.Row.SubItems[3].Text = job.Started ? FormatClock(job.Clock.Elapsed) : "--";
        var eta = JobEta(job);
        job.Row.SubItems[4].Text = eta is { } remaining ? FormatClock(remaining) : "Unknown";
        job.Row.SubItems[5].Text = JobSpeed(job);
        downloadJobsList.Invalidate(job.Row.Bounds);
    }

    private void JobList_DrawColumnHeader(object? sender, DrawListViewColumnHeaderEventArgs e) => e.DrawDefault = true;

    private void JobList_DrawItem(object? sender, DrawListViewItemEventArgs e) =>
        e.DrawDefault = downloadJobsList.View != View.Details;

    private void JobList_DrawSubItem(object? sender, DrawListViewSubItemEventArgs e)
    {
        if (e.ColumnIndex != 2 || e.Item?.Tag is not DownloadJob job)
        {
            e.DrawDefault = true;
            return;
        }
        DrawDownloadBar(e.Graphics, e.Bounds, job, e.Item.Selected);
    }

    private void DrawDownloadBar(Graphics graphics, Rectangle bounds, DownloadJob job, bool selected)
    {
        Color background = selected ? SystemColors.Highlight : downloadJobsList.BackColor;
        Color foreground = selected ? SystemColors.HighlightText : downloadJobsList.ForeColor;
        using var backgroundBrush = new SolidBrush(background);
        graphics.FillRectangle(backgroundBrush, bounds);
        var track = Rectangle.Inflate(bounds, -4, -3);
        if (track.Width <= 2 || track.Height <= 2) return;
        using var trackBrush = new SolidBrush(selected ? SystemColors.Highlight : SystemColors.ControlLight);
        graphics.FillRectangle(trackBrush, track);
        var fill = Rectangle.Inflate(track, -1, -1);
        fill.Width = DownloadPercent(job) is { } percent ? (int)(fill.Width * percent / 100) : 0;
        Color barColor = selected ? SystemColors.HighlightText : SystemColors.Highlight;
        using var barBrush = new SolidBrush(barColor);
        graphics.FillRectangle(barBrush, fill);
        ControlPaint.DrawBorder(graphics, track, SystemColors.ControlDark, ButtonBorderStyle.Solid);
        const TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.PreserveGraphicsClipping;
        TextRenderer.DrawText(graphics, DownloadText(job), downloadJobsList.Font, track, foreground, flags);
        if (fill.Width <= 0) return;
        var saved = graphics.Save();
        try
        {
            graphics.SetClip(fill, System.Drawing.Drawing2D.CombineMode.Intersect);
            TextRenderer.DrawText(graphics, DownloadText(job), downloadJobsList.Font, track,
                selected ? SystemColors.Highlight : SystemColors.HighlightText, flags);
        }
        finally { graphics.Restore(saved); }
    }
}