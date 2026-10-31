namespace ModularAudience.Forms.Modules.Dialogs;

public partial class YoutubeBrowserWindow
{
    private CancellationTokenSource? browseCancellation;
    private Task browseOperation = Task.CompletedTask;
    private readonly HashSet<Task> browseTasks = [];
    private bool browsing;
    private bool CanBrowse => !closing && binariesReady && (!busy || activeBatch is not null);

    private void StartBrowseOperation(string message, Func<CancellationToken, Task> action)
    {
        if (!CanBrowse) return;
        browseCancellation?.Cancel();
        var source = new CancellationTokenSource();
        browseCancellation = source;
        try { client.NetworkOptions = CaptureNetworkOptions(); }
        catch (ArgumentException error)
        {
            browseCancellation = null;
            source.Dispose();
            ReportError("Invalid network settings", error);
            return;
        }
        browsing = true;
        statusLabel.Text = message;
        browsingTabs.SelectedTab = resultsTab;
        UseWaitCursor = true;
        RefreshAvailability();
        browseOperation = RunBrowseAsync();
        browseTasks.Add(browseOperation);
        _ = ObserveBrowseTaskAsync(browseOperation);
        async Task RunBrowseAsync()
        {
            try
            {
                await Task.Yield();
                source.Token.ThrowIfCancellationRequested();
                await ExecuteWithRecoveryAsync(action, source.Token);
            }
            catch (OperationCanceledException) when (source.IsCancellationRequested) { }
            catch (Exception error) { if (!closing) ReportError("YouTube browsing failed", error); }
            finally
            {
                if (ReferenceEquals(browseCancellation, source))
                {
                    browsing = false;
                    browseCancellation = null;
                    UseWaitCursor = false;
                    RefreshAvailability();
                }
                source.Dispose();
            }
        }
    }

    private async Task ObserveBrowseTaskAsync(Task task)
    {
        try { await task; }
        finally { browseTasks.Remove(task); }
    }

    private void Result_DoubleClick(object? sender, EventArgs e)
    {
        var hit = resultsList.HitTest(resultsList.PointToClient(Cursor.Position));
        if (SelectedVideo() is { IsChannel: true } || hit.Item != null && hit.SubItem != null && hit.Item.SubItems.IndexOf(hit.SubItem) == 1)
        { NavigateToChannel_Click(sender, e); return; }
        Open_Click(sender, e);
    }

    private Func<string, int, CancellationToken, Task<IReadOnlyList<ModularAudience.Ytdlp.YtdlpVideo>>>? channelVideosProvider;
    private Func<ModularAudience.Ytdlp.YtdlpVideo, CancellationToken, Task<ModularAudience.Ytdlp.YtdlpVideo>>? channelMetadataProvider;

    private void NavigateToChannel_Click(object? sender, EventArgs e)
    {
        if (!CanBrowse || SelectedVideo() is not { } video) return;
        int limit = (int)resultLimit.Value;
        StartBrowseOperation("Loading channel videos...", async token =>
        {
            var metadata = video;
            if (!video.IsChannel && string.IsNullOrWhiteSpace(video.ChannelUrl))
                metadata = await (channelMetadataProvider?.Invoke(video, token) ?? client.GetVideoMetadataAsync(video, token));
            string? url = metadata.IsChannel ? metadata.Url : metadata.ChannelUrl;
            if (string.IsNullOrWhiteSpace(url)) throw new InvalidOperationException("YouTube did not provide a channel URL for this video.");
            url = ModularAudience.Ytdlp.YtdlpParser.ValidateYoutubeUrl(url).AbsoluteUri;
            string name = metadata.IsChannel ? metadata.Title : metadata.Uploader;
            string query = "Channel :: " + name;
            var videos = await (channelVideosProvider?.Invoke(url, limit, token) ?? client.BrowseAsync(url, limit, token));
            token.ThrowIfCancellationRequested();
            PublishResults(videos, query, $"{videos.Count}/{limit} videos from {name}", recommendationOrder: true);
            queryText.Text = query;
            StartUploadDates(videos);
        });
    }
}
