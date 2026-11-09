using ModularAudience.Ytdlp;
using System.Runtime.CompilerServices;

namespace ModularAudience.Forms.Modules.Dialogs;

public partial class YoutubeBrowserWindow
{
    private Func<string, int, YtdlpResultType, CancellationToken, IAsyncEnumerable<YtdlpVideo>>? searchStreamProvider = null;
    private bool restoringResultType;

    private void ResultType_Changed(object? sender, EventArgs e)
    {
        if (restoringSettings || restoringResultType || !CanBrowse) return;
        string query = queryText.Text.Trim();
        if (displayedChannel is { } channel && query == "Channel :: " + channel.Name)
        {
            RefreshChannel(channel);
            return;
        }
        if (query.Length == 0 || query.StartsWith("Playlist :: ", StringComparison.Ordinal)) return;
        Search(false);
    }

    private static ListViewItem CreateResultRow(YtdlpVideo video)
    {
        string title = video.IsChannel ? "[Channel] " + video.Title
            : video.IsPlaylist ? "[Playlist] " + video.Title : video.Title;
        var row = new ListViewItem(title) { Tag = video };
        row.SubItems.AddRange([video.Uploader, video.IsVideo ? FormatDuration(video.DurationSeconds) : "",
            video.IsVideo ? video.ViewCount?.ToString("N0") ?? "Unknown" : "", video.Id, video.Url,
            video.IsVideo ? FormatUploadDate(video.UploadDate) : ""]);
        row.ToolTipText = video.IsPlaylist
            ? $"Playlist — {video.PlaylistCount?.ToString() ?? "Unknown number of"} videos. Double-click to browse; right-click to enqueue all videos."
            : video.IsChannel ? "Channel — double-click to browse videos." : video.Title;
        return row;
    }

    private async Task<int> StreamResultsAsync(IAsyncEnumerable<YtdlpVideo> stream, string query,
        Func<int, string> summary, CancellationToken token, bool recommendationOrder = false,
        YtdlpResultType resultType = YtdlpResultType.All, int? maxResults = null, Action<int>? countChanged = null, ChannelContext? channel = null)
    {
        token.ThrowIfCancellationRequested();
        dateCancellation?.Cancel();
        ++dateGeneration;
        PublishResults([], query, "Loading results...", recommendationOrder);
        displayedChannel = channel;
        resultsSummary.Text = "Loading results...";
        var seen = new HashSet<(bool Channel, bool Playlist, string Id)>();
        int count = 0;
        await foreach (var video in stream.WithCancellation(token))
        {
            token.ThrowIfCancellationRequested();
            if (closing || resourcesReleased) break;
            if (!YtdlpParser.MatchesResultType(video, resultType) || !seen.Add((video.IsChannel, video.IsPlaylist, video.Id))) continue;
            UpdateResultRowsPreservingInteraction(() =>
            {
                var row = CreateResultRow(video);
                originalResultOrder[row] = originalResultOrder.Count;
                resultsList.Items.Add(row);
                if (sortColumn >= 0) ApplySort();
            });
            count++;
            resultsSummary.Text = summary(count) + " | Loading...";
            countChanged?.Invoke(count);
            // EndUpdate has already run: yield to the message loop between arrivals,
            // rather than holding a paint/update lock for the entire network request.
            await Task.Yield();
            if (maxResults is { } limit && count >= limit) break;
        }
        token.ThrowIfCancellationRequested();
        if (closing || resourcesReleased) return count;
        countChanged?.Invoke(count);
        resultsSummary.Text = summary(count);
        displayedQuery = countChanged is null ? query : queryText.Text;
        if (historyPosition >= 0) resultsHistory[historyPosition] = CaptureResultsPage();
        var videos = resultsList.Items.Cast<ListViewItem>().Select(row => (YtdlpVideo)row.Tag!).Where(video => video.IsVideo).ToArray();
        if (videos.Length > 0) StartUploadDates(videos);
        return count;
    }

    private static async IAsyncEnumerable<YtdlpVideo> StreamListAsync(Task<IReadOnlyList<YtdlpVideo>> results,
        [EnumeratorCancellation] CancellationToken token)
    {
        foreach (var video in await results)
        {
            token.ThrowIfCancellationRequested();
            yield return video;
        }
    }
}
