using ModularAudience.Ytdlp;

namespace ModularAudience.Forms.Modules.Dialogs;

public partial class YoutubeBrowserWindow
{
    private Func<string, CancellationToken, IAsyncEnumerable<YtdlpVideo>>? playlistVideosProvider = null;
    private CancellationTokenSource? playlistEnqueueCancellation;
    private readonly HashSet<Task> playlistEnqueueTasks = [];

    private IAsyncEnumerable<YtdlpVideo> PlaylistVideos(string url, CancellationToken token) =>
        playlistVideosProvider?.Invoke(url, token) ?? client.BrowsePlaylistStreamAsync(url, token);

    private void NavigateToPlaylist_Click(object? sender, EventArgs e)
    {
        if (!CanBrowse || SelectedVideo() is not { IsPlaylist: true } playlist) return;
        StartBrowseOperation("Loading playlist videos...", async token =>
        {
            string query = $"Playlist :: {playlist.Title} ({playlist.PlaylistCount?.ToString() ?? "…"})";
            queryText.Text = query;
            await StreamResultsAsync(PlaylistVideos(playlist.Url, token), query,
                count => $"{count} videos from playlist: {playlist.Title}", token, recommendationOrder: true,
                resultType: YtdlpResultType.Videos, countChanged: count =>
                    queryText.Text = $"Playlist :: {playlist.Title} ({count})");
        });
    }

    private void StartPlaylistEnqueue(YtdlpVideo[] selection, bool showDownloads)
    {
        var source = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        playlistEnqueueCancellation = source;
        Task task = RunAsync();
        playlistEnqueueTasks.Add(task);
        _ = ObserveAsync(task);

        async Task RunAsync()
        {
            try
            {
                client.NetworkOptions = CaptureNetworkOptions();
                statusLabel.Text = "Loading all playlist videos for the download queue...";
                RefreshAvailability();
                await Task.Yield();
                var videos = selection.Where(video => video.IsVideo).DistinctBy(video => video.Id).ToList();
                var seen = videos.Select(video => video.Id).ToHashSet(StringComparer.Ordinal);
                bool succeeded = await ExecuteWithRecoveryAsync(async token =>
                {
                    foreach (var playlist in selection.Where(video => video.IsPlaylist).DistinctBy(video => video.Url))
                    {
                        await foreach (var video in PlaylistVideos(playlist.Url, token).WithCancellation(token))
                        {
                            token.ThrowIfCancellationRequested();
                            if (video.IsVideo && seen.Add(video.Id)) videos.Add(video);
                        }
                    }
                }, source.Token);
                source.Token.ThrowIfCancellationRequested();
                if (!succeeded || closing || resourcesReleased) return;
                if (busy && activeBatch is { Settled: true }) await operation;
                source.Token.ThrowIfCancellationRequested();
                // The old batch may have completed while playlist metadata was loading.
                // QueueDownloadVideos handles either the live queue or a fresh batch.
                playlistEnqueueCancellation = null;
                QueueDownloadVideos(videos.ToArray(), showDownloads);
                AppendLog($"Resolved {videos.Count} distinct playlist/selected videos for downloading.");
            }
            catch (OperationCanceledException) when (source.IsCancellationRequested)
            {
                if (!closing && !resourcesReleased) AppendLog("Playlist enqueue cancelled; no playlist downloads were added.");
            }
            catch (Exception error) { if (!closing && !resourcesReleased) ReportError("Unable to enqueue playlist downloads", error); }
            finally
            {
                if (ReferenceEquals(playlistEnqueueCancellation, source)) playlistEnqueueCancellation = null;
                source.Dispose();
                RefreshAvailability();
            }
        }

        async Task ObserveAsync(Task pending)
        {
            try { await pending; }
            finally { playlistEnqueueTasks.Remove(pending); }
        }
    }
}
