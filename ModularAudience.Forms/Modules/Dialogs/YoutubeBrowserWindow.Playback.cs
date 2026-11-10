using ModularAudience.Ytdlp;

namespace ModularAudience.Forms.Modules.Dialogs;

public partial class YoutubeBrowserWindow
{
    private readonly YoutubeAudioPlayer audioPlayer;
    private YtdlpVideo? activePlaybackVideo;
    private Task? activePlaybackTask;
    private Task playbackStartTask = Task.CompletedTask;
    private CancellationTokenSource? playbackStartCancellation;
    private CancellationTokenSource? autoplayPreparationCancellation;
    private Task autoplayPreparationTask = Task.CompletedTask;
    private YtdlpVideo? queuedAutoplayVideo;

    private bool IsPlayingVideo(YtdlpVideo? video) =>
        video is not null && activePlaybackVideo?.Id == video.Id;

    private async void PlayStop_Click(object? sender, EventArgs e)
    {
        if (SelectedVideo() is not { IsVideo: true } video) return;
        try
        {
            if (IsPlayingVideo(video))
            {
                await StopPlaybackAsync();
                return;
            }

            Task start = StartPlaybackAsync(video);
            this.playbackStartTask = start;
            await start;
        }
        catch (Exception error) { this.ReportError("YouTube playback failed", error); }
    }

    private async Task StartPlaybackAsync(YtdlpVideo video)
    {
        if (this.closing || this.resourcesReleased) return;
        await CancelPlaybackStartAsync();
        if (this.closing || this.resourcesReleased) return;
        var source = new CancellationTokenSource();
        this.playbackStartCancellation = source;
        try
        {
            await StopActivePlaybackAsync(updateStatus: false);
            source.Token.ThrowIfCancellationRequested();
            this.client.NetworkOptions = CaptureNetworkOptions();
            this.client.MetadataSession = this.connectedSession;
            this.statusLabel.Text = "Resolving the audio-only stream...";
            this.AppendLog($"Resolving audio-only playback for: {video.Title}");
            Uri streamUrl = await this.client.GetAudioStreamUrlAsync(video, source.Token);
            source.Token.ThrowIfCancellationRequested();
            Task completion = await this.audioPlayer.StartAsync(streamUrl, this.client.NetworkOptions, source.Token);
            source.Token.ThrowIfCancellationRequested();
            if (this.closing || this.IsDisposed || this.resourcesReleased)
            {
                await this.audioPlayer.StopAsync();
                return;
            }

            this.activePlaybackVideo = video;
            this.activePlaybackTask = completion;
            this.PinPlaybackVideo(video);
            this.statusLabel.Text = $"Playing audio: {video.Title}";
            this.AppendLog($"Started audio-only playback for: {video.Title}");
            this.RefreshAvailability();
            if (this.autoplayCheck.Checked) this.PrepareAutoplayNext(video);
            this.playbackObserverTask = this.ObservePlaybackAsync(video, completion);
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!this.closing) this.ReportError("Unable to play YouTube audio", error);
        }
        finally
        {
            if (ReferenceEquals(this.playbackStartCancellation, source))
                this.playbackStartCancellation = null;
            source.Dispose();
        }
    }

    private Task playbackObserverTask = Task.CompletedTask;

    private async Task ObservePlaybackAsync(YtdlpVideo video, Task completion)
    {
        Exception? failure = null;
        bool endedNormally = false;
        try
        {
            await completion;
            endedNormally = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { failure = error; }

        if (!ReferenceEquals(this.activePlaybackTask, completion)) return;
        this.activePlaybackTask = null;
        this.activePlaybackVideo = null;
        this.RemovePlaybackPin(video);
        if (failure is not null)
            this.ReportError("YouTube audio playback failed", failure);
        else if (endedNormally)
        {
            this.statusLabel.Text = $"Finished: {video.Title}";
            this.AppendLog($"Finished audio playback for: {video.Title}");
        }
        this.RefreshAvailability();

        if (!endedNormally || !this.autoplayCheck.Checked || this.closing) return;
        try { await this.autoplayPreparationTask; }
        catch (OperationCanceledException) { return; }
        if (!this.autoplayCheck.Checked || this.closing || this.queuedAutoplayVideo is not { } next) return;
        this.queuedAutoplayVideo = null;
        Task start = this.StartPlaybackAsync(next);
        this.playbackStartTask = start;
        try { await start; }
        catch (Exception error)
        {
            if (!this.closing) this.ReportError("Unable to start the autoplay video", error);
        }
    }

    private async Task StopPlaybackAsync(bool updateStatus = true)
    {
        this.playbackStartCancellation?.Cancel();
        this.CancelAutoplayPreparation();
        await this.StopActivePlaybackAsync(updateStatus);
        if (this.playbackStartTask.IsCompleted) return;
        try { await this.playbackStartTask; }
        catch (OperationCanceledException) { }
    }

    private async Task CancelPlaybackStartAsync()
    {
        var source = this.playbackStartCancellation;
        if (source is null) return;
        source.Cancel();
        if (!this.playbackStartTask.IsCompleted)
            await this.playbackStartTask;
    }

    private async Task StopActivePlaybackAsync(bool updateStatus)
    {
        YtdlpVideo? stopped = this.activePlaybackVideo;
        this.activePlaybackVideo = null;
        this.activePlaybackTask = null;
        this.CancelAutoplayPreparation();
        await this.audioPlayer.StopAsync();
        if (stopped is not null)
        {
            this.RemovePlaybackPin(stopped);
            this.AppendLog($"Stopped audio playback for: {stopped.Title}");
        }
        if (updateStatus && !this.closing && !this.IsDisposed)
            this.statusLabel.Text = stopped is null ? "Ready." : "Playback stopped.";
        this.RefreshAvailability();
    }

    private void PinPlaybackVideo(YtdlpVideo video)
    {
        var row = this.resultsList.Items.Cast<ListViewItem>()
            .FirstOrDefault(item => item.Tag is YtdlpVideo current && current.Id == video.Id);
        if (row is null)
        {
            row = CreateResultRow(video);
            this.originalResultOrder[row] = this.originalResultOrder.Values.DefaultIfEmpty(-1).Max() + 1;
            this.resultsList.Items.Add(row);
        }
        row.Tag = video;
        row.Text = FormatResultTitle(video, true);
        row.Selected = true;
        UpdateResultRowsPreservingInteraction(this.ApplySort);
        if (this.resultsList.Items.Count > 0) this.resultsList.TopItem = this.resultsList.Items[0];
    }

    private void RemovePlaybackPin(YtdlpVideo video)
    {
        var row = this.resultsList.Items.Cast<ListViewItem>()
            .FirstOrDefault(item => item.Tag is YtdlpVideo current && current.Id == video.Id);
        if (row is not null) row.Text = FormatResultTitle((YtdlpVideo)row.Tag!, false);
        UpdateResultRowsPreservingInteraction(this.ApplySort);
    }

    private void Autoplay_CheckedChanged(object? sender, EventArgs e)
    {
        if (this.restoringSettings || this.closing) return;
        this.SaveSettings();
        if (!this.autoplayCheck.Checked)
        {
            this.CancelAutoplayPreparation();
            return;
        }
        if (this.activePlaybackVideo is { } playing) this.PrepareAutoplayNext(playing);
    }

    private void PrepareAutoplayNext(YtdlpVideo video)
    {
        this.CancelAutoplayPreparation();
        var source = new CancellationTokenSource();
        this.autoplayPreparationCancellation = source;
        this.autoplayPreparationTask = LoadAutoplayNextAsync(video, source);
    }

    private async Task LoadAutoplayNextAsync(YtdlpVideo video, CancellationTokenSource source)
    {
        try
        {
            await foreach (var next in this.client.GetRelatedVideosStreamAsync(video, 1, source.Token))
            {
                if (source.IsCancellationRequested || !this.autoplayCheck.Checked ||
                    this.activePlaybackVideo?.Id != video.Id) return;
                this.queuedAutoplayVideo = next;
                this.AppendLog($"Autoplay prepared the next video: {next.Title}");
                return;
            }
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!this.closing) this.ReportError("Unable to prepare the autoplay recommendation", error);
        }
        finally
        {
            if (ReferenceEquals(this.autoplayPreparationCancellation, source))
                this.autoplayPreparationCancellation = null;
            source.Dispose();
        }
    }

    private void CancelAutoplayPreparation()
    {
        this.autoplayPreparationCancellation?.Cancel();
        this.autoplayPreparationCancellation = null;
        this.queuedAutoplayVideo = null;
    }
}
