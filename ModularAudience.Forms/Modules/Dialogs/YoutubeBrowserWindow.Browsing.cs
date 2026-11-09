namespace ModularAudience.Forms.Modules.Dialogs;

public partial class YoutubeBrowserWindow
{
    private CancellationTokenSource? browseCancellation;
    private Task browseOperation = Task.CompletedTask;
    private readonly HashSet<Task> browseTasks = [];
    private bool browsing;
    private static readonly TimeSpan BrowseTimeout = TimeSpan.FromSeconds(90);
    private bool CanBrowse => !closing && binariesReady && (!busy || downloadBatchRunning || activeBatch is not null);

    private void StartBrowseOperation(string message, Func<CancellationToken, Task> action)
    {
        if (!CanBrowse) return;
        browseCancellation?.Cancel();
        var source = new CancellationTokenSource();
        source.CancelAfter(BrowseTimeout);
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
        AppendLog(message);
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
                bool succeeded = await ExecuteWithRecoveryAsync(action, source.Token);
                if (ReferenceEquals(browseCancellation, source) && !closing && !resourcesReleased)
                    statusLabel.Text = succeeded ? "Ready." : "Browsing failed. See the activity log.";
            }
            catch (OperationCanceledException) when (source.IsCancellationRequested)
            {
                if (ReferenceEquals(browseCancellation, source) && !closing)
                {
                    statusLabel.Text = "Search stopped or timed out. Please retry; see the activity log.";
                    AppendLog("YouTube browsing stopped. A metadata request is limited to 90 seconds; use Search to retry.");
                }
            }
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
        if (SelectedVideo() is { IsPlaylist: true }) { NavigateToPlaylist_Click(sender, e); return; }
        var hit = resultsList.HitTest(resultsList.PointToClient(Cursor.Position));
        if (SelectedVideo() is { IsChannel: true } || hit.Item != null && hit.SubItem != null && hit.Item.SubItems.IndexOf(hit.SubItem) == 1)
        { NavigateToChannel_Click(sender, e); return; }
        Open_Click(sender, e);
    }

    private Func<string, int, CancellationToken, Task<IReadOnlyList<ModularAudience.Ytdlp.YtdlpVideo>>>? channelVideosProvider;
    private Func<ModularAudience.Ytdlp.YtdlpVideo, CancellationToken, Task<ModularAudience.Ytdlp.YtdlpVideo>>? channelMetadataProvider;
    private sealed record ChannelContext(string Url, string Name);
    private ChannelContext? displayedChannel;
    private Func<string, int, ModularAudience.Ytdlp.YtdlpResultType, CancellationToken, IAsyncEnumerable<ModularAudience.Ytdlp.YtdlpVideo>>? channelResultsProvider = null;

    private void NavigateToChannel_Click(object? sender, EventArgs e)
    {
        if (!CanBrowse || SelectedVideo() is not { } video) return;
        if (video.IsPlaylist && string.IsNullOrWhiteSpace(video.ChannelUrl)) return;
        SetResultType(ModularAudience.Ytdlp.YtdlpResultType.All);
        StartBrowseOperation("Loading channel videos...", async token =>
        {
            var metadata = video;
            if (video.IsVideo && string.IsNullOrWhiteSpace(video.ChannelUrl))
                metadata = await (channelMetadataProvider?.Invoke(video, token) ?? client.GetVideoMetadataAsync(video, token));
            string? url = metadata.IsChannel ? metadata.Url : metadata.ChannelUrl;
            if (string.IsNullOrWhiteSpace(url)) throw new InvalidOperationException("YouTube did not provide a channel URL for this video.");
            url = ModularAudience.Ytdlp.YtdlpParser.ValidateYoutubeUrl(url).AbsoluteUri;
            string name = metadata.IsChannel ? metadata.Title : metadata.Uploader;
            await LoadChannelResultsAsync(new(url, name), ModularAudience.Ytdlp.YtdlpResultType.All, token);
        });
    }

    private void SetResultType(ModularAudience.Ytdlp.YtdlpResultType type)
    {
        restoringResultType = true;
        try { resultTypeCombo.SelectedIndex = (int)type; }
        finally { restoringResultType = false; }
    }

    private void RefreshChannel(ChannelContext channel)
    {
        var type = (ModularAudience.Ytdlp.YtdlpResultType)Math.Max(0, resultTypeCombo.SelectedIndex);
        StartBrowseOperation($"Loading channel {type.ToString().ToLowerInvariant()}...", token => LoadChannelResultsAsync(channel, type, token));
    }

    private async Task LoadChannelResultsAsync(ChannelContext channel, ModularAudience.Ytdlp.YtdlpResultType type, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (closing || resourcesReleased) return;
        int limit = (int)resultLimit.Value;
        string query = "Channel :: " + channel.Name;
        queryText.Text = query;
        var stream = channelResultsProvider?.Invoke(channel.Url, limit, type, token)
            ?? (channelVideosProvider is not null ? StreamListAsync(channelVideosProvider(channel.Url, limit, token), token)
                : client.BrowseChannelStreamAsync(channel.Url, limit, token, type));
        await StreamResultsAsync(stream, query, count => $"{count}/{limit} {type.ToString().ToLowerInvariant()} results from {channel.Name}", token,
            recommendationOrder: true, resultType: type, maxResults: limit, channel: channel);
    }
}
