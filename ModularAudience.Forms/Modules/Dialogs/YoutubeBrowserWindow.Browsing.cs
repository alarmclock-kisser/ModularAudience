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
        client.NetworkOptions = CaptureNetworkOptions();
        browsing = true;
        statusLabel.Text = message;
        RefreshAvailability();
        browseOperation = RunBrowseAsync();
        browseTasks.Add(browseOperation);
        _ = ObserveBrowseTaskAsync(browseOperation);
        async Task RunBrowseAsync()
        {
            try
            {
                await Task.Yield();
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
        if (SelectedVideo() is not { IsChannel: true } channel) { Open_Click(sender, e); return; }
        string query = "Channel :: " + channel.Title;
        int limit = (int)resultLimit.Value;
        StartBrowseOperation("Loading channel videos...", async token =>
        {
            var videos = await client.BrowseAsync(channel.Url, limit, token);
            token.ThrowIfCancellationRequested();
            PublishResults(videos, query, $"{videos.Count}/{limit} videos from {channel.Title}", recommendationOrder: true);
            queryText.Text = query;
            StartUploadDates(videos);
        });
    }
}
