using ModularAudience.Ytdlp;

namespace ModularAudience.Forms.Modules.Dialogs;

public partial class YoutubeBrowserWindow
{
    private sealed record PlaybackStartRequest(
        CancellationTokenSource Source,
        long Generation,
        Task PreviousStart,
        Task PreviousSequenceCleanup);

    private sealed class PlaybackSequence(
        IAsyncEnumerator<YtdlpVideo> enumerator,
        CancellationTokenSource cancellation) : IAsyncDisposable
    {
        private readonly SemaphoreSlim gate = new(1, 1);
        private readonly CancellationTokenSource source = cancellation;
        private IAsyncEnumerator<YtdlpVideo>? enumerator = enumerator;
        private bool disposed;

        public async Task<YtdlpVideo?> NextAsync()
        {
            await this.gate.WaitAsync();
            try
            {
                var current = this.enumerator;
                if (this.disposed || this.source.IsCancellationRequested || current is null)
                    return null;
                while (await current.MoveNextAsync())
                {
                    if (this.source.IsCancellationRequested) return null;
                    if (current.Current.IsVideo) return current.Current;
                }
                return null;
            }
            catch (OperationCanceledException) when (this.source.IsCancellationRequested)
            {
                return null;
            }
            finally { this.gate.Release(); }
        }

        public async ValueTask DisposeAsync()
        {
            try { this.source.Cancel(); }
            catch (ObjectDisposedException) { }
            await this.gate.WaitAsync();
            try
            {
                if (this.disposed) return;
                this.disposed = true;
                try
                {
                    if (this.enumerator is not null) await this.enumerator.DisposeAsync();
                }
                finally
                {
                    this.enumerator = null;
                    this.source.Dispose();
                }
            }
            finally { this.gate.Release(); }
        }
    }

    private enum SkipSource { ManualQueue, Collection, Autoplay }
    private sealed record SkipTarget(YtdlpVideo Video, SkipSource Source, int Index, long Generation);

    private readonly YoutubeAudioPlayer audioPlayer;
    private readonly LinkedList<YtdlpVideo> queuedPlaybackVideos = [];
    private readonly List<YtdlpVideo> queuedCollectionVideos = [];
    private readonly List<YtdlpVideo> queuedAutoplayVideos = [];
    private YtdlpVideo? activePlaybackVideo;
    private Task? activePlaybackTask;
    private Task playbackStartTask = Task.CompletedTask;
    private CancellationTokenSource? playbackStartCancellation;
    private CancellationTokenSource? autoplayPreparationCancellation;
    private Task autoplayPreparationTask = Task.CompletedTask;
    private PlaybackSequence? playbackSequence;
    private Task collectionPreviewTask = Task.CompletedTask;
    private long playbackGeneration;
    private Func<string, CancellationToken, IAsyncEnumerable<YtdlpVideo>>? channelPlaybackVideosProvider;

    private bool IsPlayingVideo(YtdlpVideo? video) =>
        video is not null && activePlaybackVideo?.Id == video.Id;

    private bool CanAddNext =>
        !this.closing && this.autoplayCheck.Checked && this.activePlaybackVideo is not null &&
        SelectedVideo() is { IsVideo: true } selected && !this.IsPlayingVideo(selected);
    private bool CanSkipPlayback =>
        !this.closing && this.autoplayCheck.Checked && this.activePlaybackVideo is not null &&
        this.activePlaybackTask is { IsCompleted: false };

    private async void PlayStop_Click(object? sender, EventArgs e)
    {
        var selected = SelectedVideo();
        if (selected is null) return;
        try
        {
            if (selected.IsVideo && this.IsPlayingVideo(selected))
            {
                await this.StopPlaybackAsync();
                return;
            }

            Task start = selected.IsVideo
                ? this.StartPlaybackAsync(selected)
                : this.StartCollectionPlaybackAsync(selected);
            await start;
        }
        catch (Exception error) { this.ReportError("YouTube playback failed", error); }
    }

    private void AddNext_Click(object? sender, EventArgs e)
    {
        if (!this.CanAddNext || SelectedVideo() is not { IsVideo: true } video) return;
        this.queuedPlaybackVideos.AddFirst(video);
        this.statusLabel.Text = $"Next up: {video.Title}";
        this.AppendLog($"Added to play next: {video.Title}");
    }

    private async void Skip_Click(object? sender, EventArgs e)
    {
        if (!this.CanSkipPlayback) return;
        try
        {
            if (this.queuedPlaybackVideos.Count == 0 && this.playbackSequence is not null &&
                this.queuedCollectionVideos.Count == 0)
                await this.collectionPreviewTask;
            var target = this.GetSkipTargets().FirstOrDefault();
            if (target is not null) await this.SkipToTargetAsync(target);
        }
        catch (Exception error) { this.ReportError("Unable to skip YouTube playback", error); }
    }

    private async Task SkipToTargetAsync(SkipTarget target)
    {
        if (!this.CanSkipPlayback || target.Generation != this.playbackGeneration ||
            !this.TryApplySkipTarget(target)) return;
        await this.BeginPlaybackRequest(preserveUpcoming: true, async request =>
        {
            await this.StopActivePlaybackAsync(updateStatus: false);
            request.Source.Token.ThrowIfCancellationRequested();
            await this.StartResolvedPlaybackAsync(target.Video, request);
        });
    }

    private bool TryApplySkipTarget(SkipTarget target)
    {
        switch (target.Source)
        {
            case SkipSource.ManualQueue:
                if (target.Index >= this.queuedPlaybackVideos.Count ||
                    this.queuedPlaybackVideos.ElementAt(target.Index).Id != target.Video.Id) return false;
                for (int index = 0; index <= target.Index; index++) this.queuedPlaybackVideos.RemoveFirst();
                return true;
            case SkipSource.Collection:
                if (target.Index >= this.queuedCollectionVideos.Count ||
                    this.queuedCollectionVideos[target.Index].Id != target.Video.Id) return false;
                this.queuedPlaybackVideos.Clear();
                this.queuedCollectionVideos.RemoveRange(0, target.Index + 1);
                return true;
            case SkipSource.Autoplay:
                if (target.Index >= this.queuedAutoplayVideos.Count ||
                    this.queuedAutoplayVideos[target.Index].Id != target.Video.Id) return false;
                this.queuedPlaybackVideos.Clear();
                this.queuedCollectionVideos.Clear();
                var sequence = this.playbackSequence;
                this.playbackSequence = null;
                if (sequence is not null) _ = sequence.DisposeAsync();
                foreach (var video in this.queuedAutoplayVideos.Skip(target.Index + 1))
                    this.queuedPlaybackVideos.AddLast(video);
                this.queuedAutoplayVideos.Clear();
                return true;
            default:
                return false;
        }
    }

    private SkipTarget[] GetSkipTargets()
    {
        var targets = new List<SkipTarget>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        long generation = this.playbackGeneration;
        void Add(YtdlpVideo video, SkipSource source, int index)
        {
            if (seen.Add(video.Id)) targets.Add(new SkipTarget(video, source, index, generation));
        }

        for (int index = 0; index < this.queuedPlaybackVideos.Count; index++)
            Add(this.queuedPlaybackVideos.ElementAt(index), SkipSource.ManualQueue, index);
        if (this.playbackSequence is not null)
        {
            for (int index = 0; index < this.queuedCollectionVideos.Count; index++)
                Add(this.queuedCollectionVideos[index], SkipSource.Collection, index);
        }
        else
        {
            for (int index = 0; index < this.queuedAutoplayVideos.Count; index++)
                Add(this.queuedAutoplayVideos[index], SkipSource.Autoplay, index);
        }
        return targets.ToArray();
    }

    private void PopulateSkipMenu()
    {
        this.skipItem.DropDownItems.Clear();
        foreach (var target in this.GetSkipTargets())
        {
            var item = new ToolStripMenuItem($"Skip -> {target.Video.Title}");
            item.Click += async (_, _) =>
            {
                try { await this.SkipToTargetAsync(target); }
                catch (Exception error) { this.ReportError("Unable to skip to the selected YouTube video", error); }
            };
            this.skipItem.DropDownItems.Add(item);
        }
        if (this.skipItem.DropDownItems.Count == 0)
            this.skipItem.DropDownItems.Add(new ToolStripMenuItem(
                this.playbackSequence is not null ? "Loading playlist / channel videos..." : "Loading next-up videos...")
            { Enabled = false });
    }

    private Task StartPlaybackAsync(YtdlpVideo video) =>
        this.BeginPlaybackRequest(preserveUpcoming: false, async request =>
        {
            await this.StopActivePlaybackAsync(updateStatus: false);
            request.Source.Token.ThrowIfCancellationRequested();
            await this.StartResolvedPlaybackAsync(video, request);
        });

    private Task StartCollectionPlaybackAsync(YtdlpVideo collection) =>
        this.BeginPlaybackRequest(preserveUpcoming: false, async request =>
        {
            await this.StopActivePlaybackAsync(updateStatus: false);
            request.Source.Token.ThrowIfCancellationRequested();
            this.statusLabel.Text = $"Loading videos from {collection.Title}...";
            this.AppendLog($"Resolving videos for playback: {collection.Title}");
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(request.Source.Token);
            var sequence = new PlaybackSequence(
                this.GetPlaybackVideos(collection, cancellation.Token).GetAsyncEnumerator(cancellation.Token),
                cancellation);
            try
            {
                YtdlpVideo? first = await sequence.NextAsync();
                request.Source.Token.ThrowIfCancellationRequested();
                if (first is null)
                {
                    this.statusLabel.Text = $"No playable videos found in {collection.Title}.";
                    this.AppendLog($"No playable videos found in {collection.Title}.");
                    return;
                }

                this.playbackSequence = sequence;
                sequence = null!;
                await this.StartResolvedPlaybackAsync(first, request);
            }
            finally
            {
                if (sequence is not null) await sequence.DisposeAsync();
            }
        });

    private IAsyncEnumerable<YtdlpVideo> GetPlaybackVideos(YtdlpVideo collection, CancellationToken token)
    {
        if (collection.IsPlaylist) return this.PlaylistVideos(collection.Url, token);
        if (collection.IsChannel)
            return this.channelPlaybackVideosProvider?.Invoke(collection.Url, token)
                ?? this.client.BrowseChannelVideosStreamAsync(collection.Url, token);
        throw new ArgumentException("Only a playlist or channel can provide a playback sequence.", nameof(collection));
    }

    private Task BeginPlaybackRequest(bool preserveUpcoming, Func<PlaybackStartRequest, Task> start)
    {
        Task previousStart = this.playbackStartTask;
        var previousCancellation = this.playbackStartCancellation;
        var source = new CancellationTokenSource();
        this.playbackStartCancellation = source;
        previousCancellation?.Cancel();
        long generation = ++this.playbackGeneration;
        this.CancelAutoplayPreparation();

        Task sequenceCleanup = Task.CompletedTask;
        if (!preserveUpcoming)
        {
            this.queuedPlaybackVideos.Clear();
            this.queuedCollectionVideos.Clear();
            this.queuedAutoplayVideos.Clear();
            var oldSequence = this.playbackSequence;
            this.playbackSequence = null;
            if (oldSequence is not null) sequenceCleanup = oldSequence.DisposeAsync().AsTask();
        }

        var request = new PlaybackStartRequest(source, generation, previousStart, sequenceCleanup);
        Task task = this.RunPlaybackRequestAsync(request, start);
        this.playbackStartTask = task;
        return task;
    }

    private async Task RunPlaybackRequestAsync(PlaybackStartRequest request, Func<PlaybackStartRequest, Task> start)
    {
        try
        {
            await request.PreviousStart;
            await request.PreviousSequenceCleanup;
            if (!this.IsCurrentPlaybackRequest(request)) return;
            await start(request);
        }
        catch (OperationCanceledException) when (request.Source.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!this.closing) this.ReportError("Unable to play YouTube audio", error);
            await this.ClearUpcomingPlaybackAsync(request.Generation);
        }
        finally
        {
            if (ReferenceEquals(this.playbackStartCancellation, request.Source))
                this.playbackStartCancellation = null;
            request.Source.Dispose();
        }
    }

    private bool IsCurrentPlaybackRequest(PlaybackStartRequest request) =>
        !request.Source.IsCancellationRequested && request.Generation == this.playbackGeneration &&
        !this.closing && !this.resourcesReleased;

    private async Task StartResolvedPlaybackAsync(YtdlpVideo video, PlaybackStartRequest request)
    {
        request.Source.Token.ThrowIfCancellationRequested();
        this.client.NetworkOptions = this.CaptureNetworkOptions();
        this.client.MetadataSession = this.connectedSession;
        this.statusLabel.Text = "Resolving the audio-only stream...";
        this.AppendLog($"Resolving audio-only playback for: {video.Title}");
        Uri streamUrl = await this.client.GetAudioStreamUrlAsync(video, request.Source.Token);
        request.Source.Token.ThrowIfCancellationRequested();
        if (!this.IsCurrentPlaybackRequest(request)) return;
        Task completion = await this.audioPlayer.StartAsync(streamUrl, this.client.NetworkOptions, request.Source.Token);
        request.Source.Token.ThrowIfCancellationRequested();
        if (!this.IsCurrentPlaybackRequest(request))
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
        this.PrepareCollectionPreview();
        this.playbackObserverTask = this.ObservePlaybackAsync(video, completion, request.Generation);
    }

    private Task playbackObserverTask = Task.CompletedTask;

    private async Task ObservePlaybackAsync(YtdlpVideo video, Task completion, long generation)
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

        if (!endedNormally || this.closing || generation != this.playbackGeneration) return;
        if (failure is not null)
        {
            await this.ClearUpcomingPlaybackAsync(generation);
            return;
        }

        try
        {
            YtdlpVideo? next = await this.GetNextPlaybackVideoAsync(generation);
            if (next is null || generation != this.playbackGeneration || this.closing) return;
            await this.BeginPlaybackRequest(preserveUpcoming: true, async request =>
            {
                await this.StopActivePlaybackAsync(updateStatus: false);
                request.Source.Token.ThrowIfCancellationRequested();
                await this.StartResolvedPlaybackAsync(next, request);
            });
        }
        catch (OperationCanceledException) when (generation != this.playbackGeneration) { }
        catch (Exception error)
        {
            if (!this.closing) this.ReportError("Unable to continue YouTube playback", error);
            await this.ClearUpcomingPlaybackAsync(generation);
        }
    }

    private async Task<YtdlpVideo?> GetNextPlaybackVideoAsync(long generation)
    {
        if (this.TakeQueuedPlaybackVideo() is { } queued) return queued;
        var sequence = this.playbackSequence;
        if (sequence is not null)
        {
            await this.collectionPreviewTask;
            if (generation != this.playbackGeneration) return null;
            YtdlpVideo? next = this.queuedCollectionVideos.Count == 0
                ? null : this.queuedCollectionVideos[0];
            if (next is not null)
            {
                this.queuedCollectionVideos.RemoveAt(0);
                return next;
            }

            if (ReferenceEquals(this.playbackSequence, sequence)) this.playbackSequence = null;
            await sequence.DisposeAsync();
            if (generation != this.playbackGeneration) return null;
            if (this.TakeQueuedPlaybackVideo() is { } queuedAfterSequence) return queuedAfterSequence;
        }

        if (!this.autoplayCheck.Checked) return null;
        await this.autoplayPreparationTask;
        if (generation != this.playbackGeneration || !this.autoplayCheck.Checked) return null;
        if (this.queuedAutoplayVideos.Count == 0) return null;
        var recommendation = this.queuedAutoplayVideos[0];
        this.queuedAutoplayVideos.RemoveAt(0);
        return recommendation;
    }

    private YtdlpVideo? TakeQueuedPlaybackVideo()
    {
        if (this.queuedPlaybackVideos.First is not { } first) return null;
        this.queuedPlaybackVideos.RemoveFirst();
        return first.Value;
    }

    private Task StopPlaybackAsync(bool updateStatus = true)
    {
        Task previousStart = this.playbackStartTask;
        this.playbackStartCancellation?.Cancel();
        this.playbackStartCancellation = null;
        long generation = ++this.playbackGeneration;
        this.CancelAutoplayPreparation();
        this.queuedPlaybackVideos.Clear();
        this.queuedCollectionVideos.Clear();
        this.queuedAutoplayVideos.Clear();
        var sequence = this.playbackSequence;
        this.playbackSequence = null;
        Task sequenceCleanup = sequence?.DisposeAsync().AsTask() ?? Task.CompletedTask;
        Task task = this.StopPlaybackCoreAsync(previousStart, sequenceCleanup, generation, updateStatus);
        this.playbackStartTask = task;
        return task;
    }

    private async Task StopPlaybackCoreAsync(Task previousStart, Task sequenceCleanup, long generation, bool updateStatus)
    {
        try
        {
            await previousStart;
            await sequenceCleanup;
            if (generation != this.playbackGeneration) return;
            await this.StopActivePlaybackAsync(updateStatus);
        }
        catch (OperationCanceledException) { }
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

    private async Task ClearUpcomingPlaybackAsync(long generation)
    {
        if (generation != this.playbackGeneration) return;
        this.queuedPlaybackVideos.Clear();
        var sequence = this.playbackSequence;
        this.playbackSequence = null;
        if (sequence is not null) await sequence.DisposeAsync();
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
            this.queuedAutoplayVideos.Clear();
            this.RefreshAvailability();
            return;
        }
        if (this.activePlaybackVideo is { } playing) this.PrepareAutoplayNext(playing);
        this.RefreshAvailability();
    }

    private void PrepareAutoplayNext(YtdlpVideo video)
    {
        this.CancelAutoplayPreparation();
        this.queuedAutoplayVideos.Clear();
        var source = new CancellationTokenSource();
        long generation = this.playbackGeneration;
        this.autoplayPreparationCancellation = source;
        this.autoplayPreparationTask = this.LoadAutoplayNextAsync(video, generation, source);
    }

    private async Task LoadAutoplayNextAsync(YtdlpVideo video, long generation, CancellationTokenSource source)
    {
        try
        {
            await foreach (var next in this.client.GetRelatedVideosStreamAsync(video, 10, source.Token))
            {
                if (source.IsCancellationRequested || !this.autoplayCheck.Checked ||
                    generation != this.playbackGeneration) return;
                this.queuedAutoplayVideos.Add(next);
            }
            if (this.queuedAutoplayVideos.Count > 0)
                this.AppendLog($"Autoplay prepared {this.queuedAutoplayVideos.Count} next-up video(s).");
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
            if (!this.closing && !this.IsDisposed) this.RefreshAvailability();
        }
    }

    private void CancelAutoplayPreparation()
    {
        this.autoplayPreparationCancellation?.Cancel();
        this.autoplayPreparationCancellation = null;
    }

    private void PrepareCollectionPreview()
    {
        if (this.playbackSequence is not { } sequence || !this.collectionPreviewTask.IsCompleted) return;
        this.collectionPreviewTask = this.LoadCollectionPreviewAsync(sequence);
    }

    private async Task LoadCollectionPreviewAsync(PlaybackSequence sequence)
    {
        try
        {
            while (ReferenceEquals(this.playbackSequence, sequence) &&
                this.queuedCollectionVideos.Count < 10)
            {
                YtdlpVideo? video = await sequence.NextAsync();
                if (video is null) break;
                if (this.queuedCollectionVideos.All(queued => queued.Id != video.Id))
                    this.queuedCollectionVideos.Add(video);
            }
        }
        catch (Exception error)
        {
            if (!this.closing) this.ReportError("Unable to load the next videos in this playlist or channel", error);
        }
        finally
        {
            if (!this.closing && !this.IsDisposed) this.RefreshAvailability();
        }
    }
}
