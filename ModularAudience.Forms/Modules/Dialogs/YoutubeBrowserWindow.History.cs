using ModularAudience.Ytdlp;

namespace ModularAudience.Forms.Modules.Dialogs;

public partial class YoutubeBrowserWindow
{
    private sealed record ResultsPage(YtdlpVideo[] Videos, string Query, int Limit, string Summary,
        int SortColumn, bool Descending, string[] SelectedIds, int TopIndex, YtdlpResultType ResultType);

    private readonly List<ResultsPage> resultsHistory = [];
    private int historyPosition = -1;
    private string displayedQuery = string.Empty;
    private int displayedLimit = 30;
    private YtdlpResultType displayedResultType;

    private ResultsPage CaptureResultsPage() => new(
        resultsList.Items.Cast<ListViewItem>().Select(row => (YtdlpVideo)row.Tag!).ToArray(),
        displayedQuery, displayedLimit, resultsSummary.Text, sortColumn, sortDescending,
        SelectedVideos().Select(video => video.Id).ToArray(), resultsList.TopItem?.Index ?? 0,
        displayedResultType);

    private void PublishResults(IReadOnlyList<YtdlpVideo> videos, string query, string summary, bool recommendationOrder = false)
    {
        if (historyPosition >= 0) resultsHistory[historyPosition] = CaptureResultsPage();
        if (historyPosition + 1 < resultsHistory.Count)
            resultsHistory.RemoveRange(historyPosition + 1, resultsHistory.Count - historyPosition - 1);
        if (recommendationOrder) sortColumn = -1;
        PopulateResults(videos);
        displayedQuery = query;
        displayedLimit = (int)resultLimit.Value;
        displayedResultType = (YtdlpResultType)Math.Max(0, resultTypeCombo.SelectedIndex);
        resultsSummary.Text = summary + (videos.Count < displayedLimit ? " | YouTube returned fewer usable videos than requested." : "");
        resultsHistory.Add(CaptureResultsPage());
        if (resultsHistory.Count > 50) resultsHistory.RemoveAt(0);
        historyPosition = resultsHistory.Count - 1;
    }

    private void NavigateResultsHistory(int direction)
    {
        if (closing || historyPosition < 0) return;
        int next = historyPosition + direction;
        if (next < 0 || next >= resultsHistory.Count) return;
        browseCancellation?.Cancel();
        resultsHistory[historyPosition] = CaptureResultsPage();
        dateCancellation?.Cancel();
        ++dateGeneration;
        historyPosition = next;
        var page = resultsHistory[next];
        sortColumn = page.SortColumn;
        sortDescending = page.Descending;
        displayedQuery = queryText.Text = page.Query;
        displayedLimit = page.Limit;
        resultLimit.Value = page.Limit;
        displayedResultType = page.ResultType;
        restoringResultType = true;
        try { resultTypeCombo.SelectedIndex = (int)displayedResultType; }
        finally { restoringResultType = false; }
        PopulateResults(page.Videos);
        resultsSummary.Text = page.Summary;
        var selected = page.SelectedIds.ToHashSet(StringComparer.Ordinal);
        foreach (ListViewItem row in resultsList.Items) row.Selected = selected.Contains(((YtdlpVideo)row.Tag!).Id);
        if (resultsList.Items.Count > 0)
            resultsList.TopItem = resultsList.Items[Math.Clamp(page.TopIndex, 0, resultsList.Items.Count - 1)];
        browsingTabs.SelectedTab = resultsTab;
        RefreshAvailability();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Handle Enter before WinForms routes it to an AcceptButton on another/hidden tab.
        if (keyData == Keys.Enter && queryText.ContainsFocus)
        {
            Search(false);
            return true;
        }
        if (keyData is (Keys.Control | Keys.Z) or (Keys.Control | Keys.Y))
        {
            NavigateResultsHistory(keyData == (Keys.Control | Keys.Z) ? -1 : 1);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
