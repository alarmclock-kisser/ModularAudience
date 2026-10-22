using ModularAudience.Ytdlp;

namespace ModularAudience.Forms.Modules.Dialogs;

public partial class YoutubeBrowserWindow
{
    private DownloadJob[] SelectedDownloadJobs() => downloadJobsList.SelectedItems.Cast<ListViewItem>()
        .Select(row => row.Tag).OfType<DownloadJob>().ToArray();

    private static bool CanRetryJob(DownloadJob job) => job.Terminal && !job.HandedOff &&
        (job.Status == "Failed" || job.Status == "Cancelled");

    private void CancelJobs_Click(object? sender, EventArgs e)
    {
        foreach (var job in SelectedDownloadJobs().Where(job => !job.Terminal && !job.HandedOff))
        {
            job.CancelRequested = true;
            job.Cancellation?.Cancel();
            job.Status = "Cancelling / cleanup";
            RefreshJob(job);
        }
    }

    private void DeleteJobs_Click(object? sender, EventArgs e)
    {
        foreach (var job in SelectedDownloadJobs().Where(job => job.Terminal))
            downloadJobsList.Items.Remove(job.Row);
    }

    private void RetryJobs_Click(object? sender, EventArgs e)
    {
        if (busy || closing || !binariesReady) return;
        var jobs = SelectedDownloadJobs().Where(CanRetryJob).ToArray();
        if (jobs.Length == 0) return;
        YtdlpDownloadOptions options;
        try { options = CaptureDownloadOptions(); }
        catch (ArgumentException error) { ReportError("Invalid download settings", error); return; }
        var cooldown = TimeSpan.FromSeconds((int)cooldownSeconds.Value);
        var stretch = stretchCheck.Checked ? stretchSettings : null;
        int parallel = (int)maxParallelDownloadsNumber.Value;
        StartOperation($"Retrying {jobs.Length} selected download(s)...",
            token => ImportBatchAsync(jobs.Select(job => job.Video).ToArray(), options, cooldown,
                stretch, parallel, token, jobs), isBatch: true);
    }
}
