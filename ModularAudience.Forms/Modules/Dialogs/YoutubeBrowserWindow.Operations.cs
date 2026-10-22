using ModularAudience.Audio;
using ModularAudience.Audio.Processing;
using ModularAudience.Ytdlp;

namespace ModularAudience.Forms.Modules.Dialogs;

public partial class YoutubeBrowserWindow
{
    private IProgress<string>? currentProgress;

    private void StartOperation(string message, Func<CancellationToken, Task> action, bool isBatch = false)
    {
        if (busy || closing || IsDisposed || resourcesReleased) return;
        busy = true;
        activeBatch = null;
        operationGeneration++;
        operationClock.Restart();
        operationCancellation = new CancellationTokenSource();
        var source = operationCancellation;
        currentProgress = new Progress<string>(text =>
        {
            if (operationCancellation != source || isBatch || activeBatch is not null || closing || IsDisposed || resourcesReleased) return;
            ShowProgress(text);
        });
        statusLabel.Text = message;
        AppendLog(message);
        operationProgress.Style = isBatch ? ProgressBarStyle.Continuous : ProgressBarStyle.Marquee;
        operationProgress.Value = 0;
        operationProgress.AccessibleName = "YouTube operation is running";
        batchProgressLabel.Text = isBatch ? "Preparing batch..." : "No active batch";
        RefreshOperationTiming();
        progressTimer.Start();
        RefreshAvailability();
        operation = ExecuteOperationAsync(action, source);
    }

    private async Task ExecuteOperationAsync(Func<CancellationToken, Task> action, CancellationTokenSource source)
    {
        await Task.Yield();
        try
        {
            source.Token.ThrowIfCancellationRequested();
            await action(source.Token);
            if (!IsDisposed && !resourcesReleased)
                statusLabel.Text = activeBatch?.Jobs.Any(job => job.Error is not null) == true
                    ? "Batch finished with errors. See the activity log." : "Ready.";
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested)
        {
            string message = activeBatch?.Jobs.Any(job => job.Error is not null) == true
                ? "Batch cancelled; cleanup completed. Some jobs failed; see the activity log."
                : "Operation cancelled; cleanup completed.";
            if (!IsDisposed && !resourcesReleased) statusLabel.Text = message;
            AppendLog(message);
        }
        catch (Exception error)
        {
            operationClock.Stop();
            RefreshOperationTiming();
            if (!IsDisposed && !resourcesReleased)
                statusLabel.Text = "Operation failed. See the activity log; retry when ready.";
            ReportError("YouTube operation failed", error);
        }
        finally
        {
            operationClock.Stop();
            if (!resourcesReleased) progressTimer.Stop();
            RefreshOperationTiming();
            currentProgress = null;
            operationCancellation = null;
            source.Dispose();
            busy = false;
            RefreshAvailability();
        }
    }

    private async Task UpdateBinariesAsync(CancellationToken token)
    {
        binariesReady = false;
        await binaryManager.EnsureBinariesAsync(currentProgress, token);
        token.ThrowIfCancellationRequested();
        binariesReady = true;
        AppendLog("Tool release check completed. Search and import are available.");
        LogManager.LogSuccess("YouTube browser: tool release check completed.");
    }

    private void Client_ProgressChanged(object? sender, string text)
    {
        long now = Environment.TickCount64;
        long previous = Interlocked.Read(ref lastProgressTick);
        if (now - previous < 300 && !text.StartsWith("Warning:", StringComparison.OrdinalIgnoreCase)) return;
        Interlocked.Exchange(ref lastProgressTick, now);
        currentProgress?.Report(text);
    }

    private void ShowProgress(string text)
    {
        string line = text.Replace('\r', ' ').Replace('\n', ' ');
        statusLabel.Text = line.Length > 240 ? line[..240] : line;
        AppendLog(text);
        if (line.StartsWith("Warning:", StringComparison.OrdinalIgnoreCase))
            LogManager.LogWarning("YouTube browser: " + statusLabel.Text);
    }

    private void Search(bool requirePlaylistUrl, bool forceSearch = false)
    {
        if (!binariesReady || busy || closing) return;
        string query = queryText.Text.Trim();
        int limit = (int)resultLimit.Value;
        StartOperation("Loading YouTube metadata...", async token =>
        {
            if (string.IsNullOrWhiteSpace(query)) throw new ArgumentException("Enter search words or a YouTube URL.");
            bool isUrl = requirePlaylistUrl || (!forceSearch && LooksLikeUrl(query));
            var videos = isUrl
                ? await client.BrowseAsync(YtdlpParser.ValidateYoutubeUrl(query).AbsoluteUri, limit, token)
                : await client.SearchAsync(query, limit, token);
            token.ThrowIfCancellationRequested();
            PopulateResults(videos);
            browsingTabs.SelectedTab = resultsTab;
            AppendLog($"Loaded {videos.Count} result(s). Related results are searches, not personal recommendations.");
        });
    }

    private static bool LooksLikeUrl(string query) =>
        Uri.TryCreate(query, UriKind.Absolute, out _) || query.Contains("://", StringComparison.Ordinal) ||
        query.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ||
        query.StartsWith("youtube.com/", StringComparison.OrdinalIgnoreCase) ||
        query.StartsWith("youtu.be/", StringComparison.OrdinalIgnoreCase);

    private void PopulateResults(IReadOnlyList<YtdlpVideo> videos)
    {
        resultsList.BeginUpdate();
        try
        {
            resultsList.Items.Clear();
            foreach (var video in videos)
            {
                var row = new ListViewItem(video.Title) { Tag = video };
                row.SubItems.AddRange([video.Uploader, FormatDuration(video.DurationSeconds),
                    video.ViewCount?.ToString("N0") ?? "Unknown", video.Id, video.Url]);
                resultsList.Items.Add(row);
            }
        }
        finally { resultsList.EndUpdate(); }
        resultsSummary.Text = $"{videos.Count} result(s) | Select multiple rows for bounded parallel downloads and serial audio import";
        Selection_Changed(this, EventArgs.Empty);
    }

    private static string FormatDuration(double? seconds)
    {
        if (seconds is not { } value || !double.IsFinite(value) || value < 0 || value > 86400000)
            return "Unknown / live";
        var duration = TimeSpan.FromSeconds(value);
        return duration.TotalHours >= 1 ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes}:{duration.Seconds:00}";
    }

    private void Download_Click(object? sender, EventArgs e)
    {
        if (busy || closing || !binariesReady) return;
        var videos = SelectedVideos();
        if (videos.Length == 0) return;
        var options = CaptureDownloadOptions();
        var cooldown = TimeSpan.FromSeconds((int)cooldownSeconds.Value);
        var stretch = stretchCheck.Checked ? stretchSettings : null;
        int maxParallel = (int)maxParallelDownloadsNumber.Value;
        StartOperation($"Importing {videos.Length} selected video(s)...",
            token => ImportBatchAsync(videos, options, cooldown, stretch, maxParallel, token), isBatch: true);
    }

    private YtdlpDownloadOptions CaptureDownloadOptions() => new()
    {
        Format = formatCombo.SelectedIndex == 1 ? "mp3" : "wav",
        SaveToDisk = saveCheck.Checked,
        OutputDirectory = outputDirectory.Text.Trim(),
        Mp3Bitrate = (int)bitrateNumber.Value,
        MaxDuration = TimeSpan.FromMinutes((double)maxDurationMinutes.Value),
        MaxFileSizeBytes = (long)maxSizeMiB.Value * 1024 * 1024
    };

    private async Task ImportBatchAsync(YtdlpVideo[] videos, YtdlpDownloadOptions options,
        TimeSpan cooldown, PlaylistStretchSettings? stretch, int maxParallel, CancellationToken token)
    {
        var batch = InitializeBatch(videos);
        using var importGate = new SemaphoreSlim(1, 1);
        try
        {
            ValidateBatchOutput(options);
            await YtdlpBatchRunner.RunAsync(batch.Jobs, maxParallel,
                (job, workerToken) => RunImportJobAsync(batch, job, options, cooldown, stretch, importGate, workerToken), token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            batch.Cancelled = true;
            throw;
        }
        catch (Exception error)
        {
            foreach (var job in batch.Jobs.Where(job => !job.Terminal)) RecordJobFailure(job, error);
        }
        finally
        {
            batch.Cancelled |= token.IsCancellationRequested;
            foreach (var job in batch.Jobs.Where(job => !job.Terminal))
                CompleteJob(job, job.Error is null ? "Cancelled" : "Failed");
            batch.Settled = true;
            operationClock.Stop();
            RefreshOperationTiming();
            var failures = batch.Jobs.Where(job => job.Error is not null)
                .Select(job => new InvalidOperationException($"{job.Video.Title} [{job.Video.Id}]: {job.Error!.Message}", job.Error)).ToArray();
            AppendLog($"Batch finished: {batch.Jobs.Count(job => job.Status == "Completed")} imported, " +
                $"{failures.Length} failed, {batch.Jobs.Count(job => job.Status == "Cancelled")} cancelled.");
            if (failures.Length > 0 && !closing && !IsDisposed && !resourcesReleased)
                ReportError("Some YouTube imports failed", new AggregateException("Failed imports (including cleanup failures):", failures));
        }
    }

    private static void ValidateBatchOutput(YtdlpDownloadOptions options)
    {
        if (!options.SaveToDisk) return;
        if (string.IsNullOrWhiteSpace(options.OutputDirectory) || !Path.IsPathFullyQualified(options.OutputDirectory))
            throw new ArgumentException("Choose a fully qualified output folder before saving audio.");
        _ = Path.GetFullPath(options.OutputDirectory);
    }

    private async Task RunImportJobAsync(DownloadBatch batch, DownloadJob job, YtdlpDownloadOptions options,
        TimeSpan cooldown, PlaylistStretchSettings? stretch, SemaphoreSlim importGate, CancellationToken token)
    {
        job.Started = true;
        job.Clock.Start();
        job.NetworkStage = true;
        job.Status = "Preparing download";
        RefreshJob(job);
        try
        {
            await ImportOneAsync(batch, job, options, cooldown, stretch, importGate, token);
            job.Status = "Completed";
            AppendLog($"Imported: {job.Video.Title}");
            LogManager.LogSuccess($"YouTube browser: imported audio [{job.Video.Id}].");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            job.Status = "Cancelled";
            throw;
        }
        catch (Exception error) { RecordJobFailure(job, error); }
        finally
        {
            CompleteJob(job, job.Status);
            RefreshBatchProgress(batch);
        }
    }

    private async Task ImportOneAsync(DownloadBatch batch, DownloadJob job, YtdlpDownloadOptions options,
        TimeSpan cooldown, PlaylistStretchSettings? stretch, SemaphoreSlim importGate, CancellationToken token)
    {
        var jobClient = new YtdlpClient(binaryManager);
        var text = CreateJobTextProgress(batch, job, audio: false);
        var typed = CreateDownloadProgress(batch, job);
        var result = await jobClient.DownloadAsync(job.Video, options, text, token, typed);
        job.NetworkStage = false;
        job.DownloadFinished = true;
        await ImportDownloadedAsync(batch, job, result, cooldown, stretch, importGate, token);
    }

    private async Task ImportDownloadedAsync(DownloadBatch batch, DownloadJob job, YtdlpDownloadResult result,
        TimeSpan cooldown, PlaylistStretchSettings? stretch, SemaphoreSlim importGate, CancellationToken token)
    {
        Exception? processingError = null;
        try
        {
            await using (result)
            {
                try { await CooldownAndImportAsync(batch, job, result, cooldown, stretch, importGate, token); }
                catch (Exception error) { processingError = error; throw; }
                finally
                {
                    job.Status = "Cleaning up";
                    RefreshJob(job);
                }
            }
        }
        catch (Exception error) when (job.HandedOff)
        {
            throw new IOException("Audio was already transferred to the caller; subsequent cleanup failed. Do not import it again automatically.", error);
        }
        catch (Exception error) when (processingError is not null && !ReferenceEquals(error, processingError))
        {
            throw new AggregateException("Audio processing and temporary-file cleanup failed.", processingError, error);
        }
    }

    private async Task CooldownAndImportAsync(DownloadBatch batch, DownloadJob job, YtdlpDownloadResult result,
        TimeSpan cooldown, PlaylistStretchSettings? stretch, SemaphoreSlim importGate, CancellationToken token)
    {
        job.Status = "Cooldown";
        job.CooldownDeadline = job.Clock.Elapsed + cooldown;
        RefreshJob(job);
        try { await Task.Delay(cooldown, token); }
        finally { job.CooldownDeadline = null; }
        job.Status = "Waiting for audio import";
        RefreshJob(job);
        await importGate.WaitAsync(token);
        try
        {
            job.Status = "Loading audio";
            job.AudioStage = true;
            RefreshJob(job);
            var progress = CreateJobTextProgress(batch, job, audio: true);
            var audio = await YoutubeAudioImporter.ImportAsync(result.FilePath, job.Video.Title,
                result.IsTemporary, TimeSpan.Zero, stretch, progress, token);
            TransferAudio(job, audio, token);
        }
        finally
        {
            job.AudioStage = false;
            importGate.Release();
        }
    }

    private void TransferAudio(DownloadJob job, AudioObj audio, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            importAudio(audio);
            job.HandedOff = true;
        }
        catch
        {
            audio.Dispose();
            throw;
        }
    }
}
