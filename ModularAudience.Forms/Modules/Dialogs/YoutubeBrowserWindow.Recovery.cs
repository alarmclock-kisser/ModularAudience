using ModularAudience.Audio;
using ModularAudience.Audio.Processing;
using ModularAudience.Ytdlp;

namespace ModularAudience.Forms.Modules.Dialogs;

public partial class YoutubeBrowserWindow
{
    private async Task<bool> ExecuteWithRecoveryAsync(Func<CancellationToken, Task> action, CancellationToken token)
    {
        try { await action(token); return true; }
        catch (YtdlpProcessException error)
        {
            LogManager.LogError($"YouTube browser: request failed before recovery. {error}");
            var originalNetwork = client.NetworkOptions;
            var originalSession = client.MetadataSession;
            var original = new YtdlpDownloadOptions { Network = originalNetwork, BrowserSession = originalSession };
            var decision = RequestRecovery([error], original, error.Message, token);
            if (decision is null) return false;
            var recovered = YtdlpRecoveryRules.Apply(error.FailureKind, decision, original);
            try
            {
                client.NetworkOptions = recovered.Network;
                client.MetadataSession = recovered.BrowserSession;
                await WaitForRecoveryWindowAsync([error], token);
                await action(token);
                return true;
            }
            catch (YtdlpProcessException cookieError) when (cookieError.FailureKind == YtdlpFailureKind.BrowserCookies)
            {
                var alternative = RequestRecovery([cookieError], recovered, cookieError.Message, token);
                if (alternative is null) return false;
                var corrected = YtdlpRecoveryRules.Apply(cookieError.FailureKind, alternative, recovered);
                client.NetworkOptions = corrected.Network;
                client.MetadataSession = corrected.BrowserSession;
                await action(token);
                return true;
            }
            finally { client.NetworkOptions = originalNetwork; client.MetadataSession = connectedSession ?? originalSession; }
        }
    }

    private YtdlpRecoveryDecision? RequestRecovery(IReadOnlyList<YtdlpProcessException> errors,
        YtdlpDownloadOptions options, string diagnostics, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (closing || IsDisposed || resourcesReleased) return null;
        using var dialog = new YoutubeRecoveryDialog(errors, options, diagnostics);
        _ = dialog.Handle;
        using var registration = token.Register(() =>
        {
            try { dialog.BeginInvoke((Action)(() => dialog.Close())); }
            catch (InvalidOperationException) { }
        });
        AppendLog("Recovery options opened. No browser session or trust change is applied without confirmation.");
        var result = dialog.ShowDialog(this);
        token.ThrowIfCancellationRequested();
        if (result != DialogResult.OK || dialog.Decision is null)
        {
            AppendLog("Recovery declined; failures are retained. No browser cookies were requested.");
            return null;
        }
        AppendLog($"Explicit recovery selected: {dialog.Decision.Action}. Applies to matching failed requests in this operation only.");
        if (dialog.Decision.Session is { CookieContents: not null } session)
        {
            connectedSession = session;
            client.MetadataSession = session;
            AppendLog("Chrome YouTube session connected for this browser window. Cookies are not saved to settings.");
        }
        return dialog.Decision;
    }

    private async Task RunBatchPassAsync(DownloadBatch batch, IReadOnlyList<DownloadJob> jobs,
        YtdlpDownloadOptions options, TimeSpan cooldown, PlaylistStretchSettings? stretch,
        int maxParallel, SemaphoreSlim importGate, CancellationToken token)
    {
        var queue = new Queue<DownloadJob>(jobs);
        using var queueChanged = new SemaphoreSlim(0);
        batch.PendingQueue = queue;
        batch.QueueChanged = queueChanged;
        try
        {
            await YtdlpBatchRunner.RunQueueAsync(queue, maxParallel,
                (job, cancellation) => RunImportJobAsync(batch, job, options, cooldown, stretch, importGate, cancellation), token, queueChanged);
        }
        finally
        {
            batch.PendingQueue = null;
            batch.QueueChanged = null;
            batch.Settled = true;
        }
    }

    private async Task<(bool Shown, bool Retried)> RecoverBatchAsync(DownloadBatch batch,
        YtdlpDownloadOptions options, TimeSpan cooldown, PlaylistStretchSettings? stretch,
        int maxParallel, SemaphoreSlim importGate, CancellationToken token, bool allowCookieCorrection = true)
    {
        token.ThrowIfCancellationRequested();
        var candidates = RecoverableJobs(batch);
        if (candidates.Length == 0 || closing || IsDisposed || resourcesReleased) return (false, false);
        var errors = candidates.Select(job => (YtdlpProcessException)job.Error!).ToArray();
        string diagnostics = "Showing up to 20 failed items. Full errors remain in the application log.\r\n\r\n" +
            string.Join("\r\n\r\n", batch.Jobs.Where(job => job.Error is not null).Take(20)
                .Select(job => $"{job.Video.Title} [{job.Video.Id}]\r\n{job.Error}"));
        var decision = RequestRecovery(errors, options, diagnostics, token);
        if (decision is null) return (true, false);
        var eligible = candidates.Where(job => job.Row.ListView is not null && !job.CancelRequested && YtdlpRecoveryRules.CanApply(
            (YtdlpProcessException)job.Error!, decision.Action, options)).ToArray();
        if (eligible.Length == 0) return (true, false);
        var recovered = YtdlpRecoveryRules.Apply(((YtdlpProcessException)eligible[0].Error!).FailureKind, decision, options);
        await WaitForRecoveryWindowAsync(eligible.Select(job => (YtdlpProcessException)job.Error!).ToArray(), token);
        foreach (var job in eligible) ResetForRecovery(job);
        batch.Settled = false;
        RefreshBatchProgress(batch);
        await RunBatchPassAsync(batch, eligible, recovered, cooldown, stretch, maxParallel, importGate, token);
        if (allowCookieCorrection && eligible.Any(job => job.Error is YtdlpProcessException { FailureKind: YtdlpFailureKind.BrowserCookies }))
            await RecoverBatchAsync(batch, recovered, cooldown, stretch, maxParallel, importGate, token, allowCookieCorrection: false);
        return (true, true);
    }

    private async Task WaitForRecoveryWindowAsync(IReadOnlyList<YtdlpProcessException> errors, CancellationToken token)
    {
        var deadline = errors.Max(error => error.RetryNotBeforeUtc);
        if (deadline is null || deadline <= DateTimeOffset.UtcNow) return;
        var pause = deadline.Value - DateTimeOffset.UtcNow;
        if (pause <= TimeSpan.Zero) return;
        statusLabel.Text = $"Waiting {Math.Ceiling(pause.TotalSeconds):0} seconds before recovery (server Retry-After).";
        AppendLog(statusLabel.Text);
        await Task.Delay(pause, token);
    }

    private static DownloadJob[] RecoverableJobs(DownloadBatch batch) => batch.Jobs
        .Where(job => job.Terminal && !job.HandedOff && !job.CancelRequested && job.Error is YtdlpProcessException).ToArray();

    private void ResetForRecovery(DownloadJob job)
    {
        job.Attempt++;
        job.CancelRequested = false;
        job.Clock.Reset();
        job.Terminal = false;
        job.Error = null;
        job.Latest = null;
        job.DownloadFinished = false;
        job.Started = false;
        job.NetworkStage = false;
        job.AudioStage = false;
        job.CooldownDeadline = null;
        job.LastLog = null;
        job.LastLogTimestamp = 0;
        job.LastProgressTimestamp = 0;
        job.Status = "Queued for explicit recovery";
        RefreshJob(job);
    }
}
