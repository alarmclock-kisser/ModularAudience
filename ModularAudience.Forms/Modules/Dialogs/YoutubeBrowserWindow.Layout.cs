namespace ModularAudience.Forms.Modules.Dialogs;

public partial class YoutubeBrowserWindow
{
    private bool fittingColumns;

    private void ResultList_SizeChanged(object? sender, EventArgs e) => FitResultColumns();
    private void ResultsColumn_WidthChanged(object? sender, ColumnWidthChangedEventArgs e) => FitResultColumns();
    private void JobList_SizeChanged(object? sender, EventArgs e)
    {
        if (downloadJobsList is null) return;
        FitColumns(downloadJobsList, [320, 190, 160, 90, 100, 110]);
    }

    private void FitResultColumns()
    {
        if (resultsList is null || columnMenuItems is null || fittingColumns) return;
        int[] weights = columnWidths.Select((width, index) => columnMenuItems[index].Checked ? width : 0).ToArray();
        FitColumns(resultsList, weights);
    }

    private void FitColumns(ListView list, int[] weights)
    {
        if (fittingColumns || list.Columns.Count != weights.Length || list.ClientSize.Width < 20) return;
        int total = weights.Sum();
        if (total == 0) return;
        fittingColumns = true;
        try
        {
            int available = Math.Max(1, list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 2);
            int remaining = available;
            int last = Array.FindLastIndex(weights, value => value > 0);
            for (int index = 0; index < weights.Length; index++)
            {
                int width = weights[index] == 0 ? 0 : index == last ? remaining : (int)((long)available * weights[index] / total);
                remaining -= width;
                if (list.Columns[index].Width != width) list.Columns[index].Width = width;
            }
        }
        finally { fittingColumns = false; }
    }
}
