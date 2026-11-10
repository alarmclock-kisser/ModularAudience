using ModularAudience.Audio;
using ModularAudience.Audio.Processing;
using ModularAudience.Ytdlp;

namespace ModularAudience.Forms.Modules.Dialogs;

public partial class YoutubeBrowserWindow
{
    private IProgress<string>? currentProgress;
    private bool batchWaitForBpmTag;
    private bool downloadBatchRunning;
    private Func<YtdlpVideo, YtdlpDownloadOptions, CancellationToken, Task<YtdlpDownloadResult>>? downloadProvider = null;
    private Func<string, int, CancellationToken, Task<IReadOnlyList<YtdlpVideo>>>? searchProvider;

    private void StartOperation(string message, Func<CancellationToken, Task> action, bool isBatch = false)
    {
        if (busy || closing || IsDisposed || resourcesReleased) return;
        try { client.NetworkOptions = CaptureNetworkOptions(); }
        catch (ArgumentException error) { ReportError("Invalid network settings", error); return; }
        busy = true;
        downloadBatchRunning = isBatch;
        activeBatch = null;
        toolTransfer = null;
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
        operation = ExecuteOperationAsync(action, source, isBatch);
    }

    private async Task ExecuteOperationAsync(Func<CancellationToken, Task> action, CancellationTokenSource source, bool isBatch)
    {
        await Task.Yield();
        try
        {
            source.Token.ThrowIfCancellationRequested();
            if (proxyModeCombo.SelectedIndex == 2 && string.IsNullOrWhiteSpace(proxyText.Text))
                throw new ArgumentException("Enter an explicit proxy URL or choose another proxy mode.");
            client.NetworkOptions.Validate();
            bool succeeded = true;
            if (isBatch) await action(source.Token);
            else succeeded = await ExecuteWithRecoveryAsync(action, source.Token);
            if (!IsDisposed && !resourcesReleased)
                statusLabel.Text = !succeeded ? "Operation failed; recovery was not applied. See the diagnostics." :
                    activeBatch?.Jobs.Any(job => job.Error is not null) == true
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
            downloadBatchRunning = false;
            RefreshAvailability();
        }
    }

    private async Task UpdateBinariesAsync(CancellationToken token)
    {
        binariesReady = false;
        var source = operationCancellation;
        var transferProgress = new Progress<YtdlpDownloadProgress>(transfer =>
        {
            if (operationCancellation != source || closing || IsDisposed || resourcesReleased) return;
            toolTransfer = transfer;
            operationProgress.Style = ProgressBarStyle.Continuous;
            operationProgress.Value = (int)((transfer.Percent ?? 0) * operationProgress.Maximum / 100);
            batchProgressLabel.Text = TransferText(transfer) + $" | {transfer.SpeedBytesPerSecond / 1_000_000:0.00} MB/s";
            RefreshOperationTiming();
        });
        await binaryManager.EnsureBinariesAsync(currentProgress, token, transferProgress);
        token.ThrowIfCancellationRequested();
        binariesReady = true;
        AppendLog("Tool release check completed. Search and import are available.");
        LogManager.LogSuccess("YouTube browser: tool release check completed.");
    }

    private void Client_ProgressChanged(object? sender, string text)
    {
        long now = Environment.TickCount64;
        long previous = Interlocked.Read(ref lastProgressTick);
        if (now - previous < 300 && !text.StartsWith("Warning:", StringComparison.OrdinalIgnoreCase) &&
            !text.StartsWith(YtdlpRetryPolicy.ProgressPrefix, StringComparison.Ordinal)) return;
        Interlocked.Exchange(ref lastProgressTick, now);
        currentProgress?.Report(text);
        if (browsing && browseCancellation?.IsCancellationRequested == false && !closing && !resourcesReleased && IsHandleCreated)
        {
            var source = browseCancellation;
            try
            {
                BeginInvoke((Action)(() =>
                {
                    if (ReferenceEquals(browseCancellation, source) && !closing && !IsDisposed && !resourcesReleased)
                        ShowProgress(text);
                }));
            }
            catch (InvalidOperationException) when (closing || IsDisposed || resourcesReleased) { }
        }
    }

    private void ShowProgress(string text)
    {
        if (text.StartsWith(YtdlpRetryPolicy.ProgressPrefix, StringComparison.Ordinal))
            text = text[YtdlpRetryPolicy.ProgressPrefix.Length..];
        string line = text.Replace('\r', ' ').Replace('\n', ' ');
        statusLabel.Text = line.Length > 240 ? line[..240] : line;
        if (toolTransfer is not null && !line.StartsWith("Downloading ", StringComparison.Ordinal))
        {
            toolTransfer = null;
            batchProgressLabel.Text = line;
        }
        if (!line.StartsWith("Downloading ", StringComparison.Ordinal))
            AppendLog(text);
        if (line.StartsWith("Warning:", StringComparison.OrdinalIgnoreCase))
            LogManager.LogWarning("YouTube browser: " + statusLabel.Text);
    }

    private void Search(bool requirePlaylistUrl, bool forceSearch = false)
    {
        if (!CanBrowse) return;
        if (!requirePlaylistUrl && !forceSearch && displayedChannel is { } channel && queryText.Text.Trim() == "Channel :: " + channel.Name)
        {
            RefreshChannel(channel);
            return;
        }
        dateCancellation?.Cancel();
        ++dateGeneration;
        string query = queryText.Text.Trim();
        int limit = (int)resultLimit.Value;
        var resultType = (YtdlpResultType)Math.Max(0, resultTypeCombo.SelectedIndex);
        StartBrowseOperation("Loading YouTube metadata...", async token =>
        {
            if (string.IsNullOrWhiteSpace(query)) throw new ArgumentException("Enter search words or a YouTube URL.");
            bool isUrl = requirePlaylistUrl || (!forceSearch && LooksLikeUrl(query));
            AppendLog(isUrl ? "Opening YouTube URL..." : $"Searching YouTube for '{query}' (up to {limit} results)...");
            RememberSearch(query);
            if (isUrl && YtdlpParser.IsChannelUrl(query))
            {
                SetResultType(YtdlpResultType.All);
                await LoadChannelResultsAsync(new(YtdlpParser.ValidateYoutubeUrl(query).AbsoluteUri, query), YtdlpResultType.All, token);
                return;
            }
            var stream = isUrl ? client.BrowseStreamAsync(YtdlpParser.ValidateYoutubeUrl(query).AbsoluteUri, limit, token)
                : searchStreamProvider?.Invoke(query, limit, resultType, token)
                    ?? (searchProvider is not null ? StreamListAsync(searchProvider(query, limit, token), token)
                        : client.SearchStreamAsync(query, limit, token, resultType));
            int count = await StreamResultsAsync(stream, query, count => $"{count}/{limit} result(s) loaded", token,
                resultType: isUrl ? YtdlpResultType.All : resultType, maxResults: limit);
            AppendLog($"Loaded {count} result(s). Related results are searches, not personal recommendations.");
        });
    }

    private static bool LooksLikeUrl(string query) =>
        Uri.TryCreate(query, UriKind.Absolute, out _) || query.Contains("://", StringComparison.Ordinal) ||
        query.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ||
        query.StartsWith("youtube.com/", StringComparison.OrdinalIgnoreCase) ||
        query.StartsWith("youtu.be/", StringComparison.OrdinalIgnoreCase);

    private void PopulateResults(IReadOnlyList<YtdlpVideo> videos)
    {
        var rows = videos.ToList();
        if (activePlaybackVideo is { } playing && rows.All(video => video.Id != playing.Id))
            rows.Add(playing);
        resultsList.BeginUpdate();
        try
        {
            resultsList.Items.Clear();
            originalResultOrder.Clear();
            foreach (var video in rows)
            {
                var row = CreateResultRow(video);
                if (IsPlayingVideo(video)) row.Text = FormatResultTitle(video, true);
                resultsList.Items.Add(row);
                originalResultOrder[row] = originalResultOrder.Count;
            }
        }
        finally { resultsList.EndUpdate(); }
        ApplySort();
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
        if (busy) { DownloadEnqueue_Click(sender, e); return; }
        DownloadSelected(showDownloads: true);
    }

    private bool CanEnqueueDownloads => !closing && binariesReady && playlistEnqueueCancellation is null &&
        (!busy || activeBatch is { PendingQueue: not null, Settled: false } batch && !batch.Source.IsCancellationRequested);

    private void DownloadEnqueue_Click(object? sender, EventArgs e)
    {
        if (!CanEnqueueDownloads) return;
        DownloadSelected(showDownloads: false);
    }

    private void EnqueueDownloadVideos(DownloadBatch batch, YtdlpVideo[] videos)
    {
        if (batch.PendingQueue is null || batch.Source.IsCancellationRequested) return;
        var queued = new List<DownloadJob>();
        var existing = batch.Jobs.Where(job => !job.Terminal).Select(job => job.Video.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var video in videos.Where(video => video.IsVideo))
        {
            if (!existing.Add(video.Id)) continue;
            var row = new ListViewItem(video.Title);
            row.SubItems.AddRange(["Queued", "Unknown", "--", "Unknown", "--", "--"]);
            var job = new DownloadJob(video, batch.Jobs.Length + queued.Count, row);
            row.Tag = job;
            downloadJobsList.Items.Add(row);
            queued.Add(job);
            batch.PendingQueue.Enqueue(job);
        }
        batch.Jobs = [.. batch.Jobs, .. queued];
        if (queued.Count > 0) batch.QueueChanged?.Release();
        RefreshBatchProgress(batch);
        AppendLog($"Enqueued {queued.Count} additional video(s); using the running batch's download and import settings.");
    }

    private void DownloadSelected(bool showDownloads)
    {
        if (!CanEnqueueDownloads) return;
        var selected = SelectedVideos().Where(video => !video.IsChannel).ToArray();
        if (selected.Any(video => video.IsPlaylist))
        {
            StartPlaylistEnqueue(selected, showDownloads);
            return;
        }
        QueueDownloadVideos(selected, showDownloads);
    }

    private void QueueDownloadVideos(YtdlpVideo[] videos, bool showDownloads)
    {
        if (!CanEnqueueDownloads) return;
        videos = videos.Where(video => video.IsVideo).DistinctBy(video => video.Id).ToArray();
        if (videos.Length == 0) return;
        if (busy && activeBatch is { } batch)
        {
            EnqueueDownloadVideos(batch, videos);
            return;
        }
        YtdlpDownloadOptions options;
        try { options = CaptureDownloadOptions(); }
        catch (ArgumentException error) { ReportError("Invalid download settings", error); return; }
        var cooldown = TimeSpan.FromSeconds((int)cooldownSeconds.Value);
        var stretch = stretchCheck.Checked ? stretchSettings : null;
        int maxParallel = (int)maxParallelDownloadsNumber.Value;
        showDownloadsOnBatchStart = showDownloads;
        StartOperation($"Importing {videos.Length} selected video(s)...",
            token => ImportBatchAsync(videos, options, cooldown, stretch, maxParallel, token), isBatch: true);
    }

    private YtdlpDownloadOptions CaptureDownloadOptions() => new()
    {
        Format = formatCombo.SelectedIndex == 1 ? "mp3" : "wav",
        SaveToDisk = saveCheck.Checked,
        OutputDirectory = outputDirectory.Text.Trim(),
        Mp3Bitrate = mp3Bitrate,
        WavBitDepth = wavBitDepth,
        Network = CaptureNetworkOptions(),
        BrowserSession = connectedSession,
        MaxDuration = TimeSpan.FromMinutes((double)maxDurationMinutes.Value),
        MaxFileSizeBytes = (long)maxSizeMiB.Value * 1024 * 1024
    };

    private async Task ImportBatchAsync(YtdlpVideo[] videos, YtdlpDownloadOptions options,
        TimeSpan cooldown, PlaylistStretchSettings? stretch, int maxParallel, CancellationToken token, DownloadJob[]? retryJobs = null)
    {
        batchWaitForBpmTag = waitForBpmCheck.Checked;
        var batch = InitializeRetryBatch(videos, retryJobs);
        using var importGate = new SemaphoreSlim(1, 1);
        bool recoveryShown = false, recoveryRetried = false;
        try
        {
            ValidateBatchOutput(options);
            await RunBatchPassAsync(batch, batch.Jobs, options, cooldown, stretch, maxParallel, importGate, token);
            (recoveryShown, recoveryRetried) = await RecoverBatchAsync(batch, options, cooldown, stretch, maxParallel, importGate, token);
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
            if (failures.Length > 0 && (!recoveryShown || recoveryRetried) && !token.IsCancellationRequested && !closing && !IsDisposed && !resourcesReleased)
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
        TimeSpan cooldown, PlaylistStretchSettings? stretch, SemaphoreSlim importGate, CancellationToken token, Action releaseDownloadSlot)
    {
        using var jobCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        job.Cancellation = jobCancellation;
        if (job.CancelRequested) jobCancellation.Cancel();
        token = jobCancellation.Token;
        job.Started = true;
        job.Clock.Start();
        job.NetworkStage = true;
        job.Status = "Preparing download";
        RefreshJob(job);
        try
        {
            var retries = new YtdlpRetryBudget(batch.MaxRetries);
            await ImportOneAsync(batch, job, options, cooldown, stretch, importGate, token, releaseDownloadSlot, retries);
            job.Status = "Completed";
            AppendLog($"Imported: {job.Video.Title}");
            LogManager.LogSuccess($"YouTube browser: imported audio [{job.Video.Id}].");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            job.Status = "Cancelled";
        }
        catch (Exception error) { RecordJobFailure(job, error); }
        finally
        {
            job.Cancellation = null;
            CompleteJob(job, job.Status);
            RefreshBatchProgress(batch);
        }
    }

    private async Task ImportOneAsync(DownloadBatch batch, DownloadJob job, YtdlpDownloadOptions options,
        TimeSpan cooldown, PlaylistStretchSettings? stretch, SemaphoreSlim importGate, CancellationToken token,
        Action releaseDownloadSlot, YtdlpRetryBudget retries)
    {
        var jobClient = new YtdlpClient(binaryManager);
        if (connectedSession is not null && options.BrowserSession is null)
            options = options with { BrowserSession = connectedSession };
        var result = await retries.RunAsync(cancellation =>
        {
            var text = CreateJobTextProgress(batch, job, audio: false);
            var typed = CreateDownloadProgress(batch, job);
            return downloadProvider?.Invoke(job.Video, options, cancellation)
                ?? jobClient.DownloadAsync(job.Video, options, text, cancellation, typed);
        }, token, (attempt, error) => AutomaticJobRetry(job, retries, attempt, error, networkStage: true),
            _ => !job.HandedOff && !job.CancelRequested);
        job.NetworkStage = false;
        job.DownloadFinished = true;
        releaseDownloadSlot();
        await ImportDownloadedAsync(batch, job, result, cooldown, stretch, importGate, token, retries);
    }

    private void AutomaticJobRetry(DownloadJob job, YtdlpRetryBudget budget, int attempt, Exception error, bool networkStage)
    {
        job.Attempt++; // Ignore callbacks queued by a previous attempt.
        job.Latest = null;
        job.NetworkStage = networkStage;
        job.DownloadFinished = !networkStage;
        job.AudioStage = false;
        job.LastLog = null;
        job.Status = $"Retry {attempt}/{budget.MaxRetries}: {(networkStage ? "download / convert" : "audio import")}";
        RefreshJob(job);
        AppendLog($"[{job.Index + 1}] {job.Video.Title}: {job.Status}; unchanged options. {error.Message}");
    }

    private async Task ImportDownloadedAsync(DownloadBatch batch, DownloadJob job, YtdlpDownloadResult result,
        TimeSpan cooldown, PlaylistStretchSettings? stretch, SemaphoreSlim importGate, CancellationToken token, YtdlpRetryBudget retries)
    {
        Exception? processingError = null;
        try
        {
            await using (result)
            {
                try
                {
                    // Retry import against the same downloaded file. Downloading again
                    // would collide with saved output and lose an external tagger's work.
                    await retries.RunAsync(cancellation => CooldownAndImportAsync(batch, job, result, cooldown, stretch, importGate, cancellation), token,
                        (attempt, error) => AutomaticJobRetry(job, retries, attempt, error, networkStage: false),
                        _ => !job.HandedOff && !job.CancelRequested);
                }
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
        bool unlimited = batchWaitForBpmTag && cooldown == TimeSpan.Zero;
        job.Status = unlimited ? "Waiting for BPM tag (no time limit)" : batchWaitForBpmTag ? "Waiting for BPM tag" : "Cooldown";
        job.WaitingForBpmWithoutLimit = unlimited;
        job.BpmTagTimedOut = false;
        job.CooldownDeadline = unlimited ? null : job.Clock.Elapsed + cooldown;
        float? taggedBpm = null;
        RefreshJob(job);
        try
        {
            if (batchWaitForBpmTag)
            {
                taggedBpm = await YoutubeAudioImporter.WaitForBpmValueAsync(result.FilePath, cooldown, token);
                job.BpmTagTimedOut = taggedBpm is null;
                string message = taggedBpm is { } bpm ? $"BPM tag detected ({bpm:0.###}): {job.Video.Title}"
                    : $"Warning: BPM tag wait timed out after {cooldown.TotalSeconds:0.###} s; importing without an awaited BPM tag: {job.Video.Title}. Increase Wait (s), or set 0 to wait until tagged.";
                AppendLog(message);
                if (job.BpmTagTimedOut) LogManager.LogWarning(message);
            }
            else await Task.Delay(cooldown, token);
        }
        finally { job.CooldownDeadline = null; job.WaitingForBpmWithoutLimit = false; }
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
                result.IsTemporary, TimeSpan.Zero, stretch, progress, token, taggedBpm);
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
