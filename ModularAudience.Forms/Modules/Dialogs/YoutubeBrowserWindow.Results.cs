using ModularAudience.Ytdlp;
using System.Collections;
using System.Globalization;

namespace ModularAudience.Forms.Modules.Dialogs;

public partial class YoutubeBrowserWindow
{
    private int sortColumn = -1;
    private bool sortDescending;
    private static readonly string[] ResultHeaders = ["Title", "Uploader", "Duration", "Views", "ID", "URL", "Upload Date"];

    private static string FormatUploadDate(DateOnly? date) =>
        date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "Unknown";

    private void Column_Click(object? sender, ColumnClickEventArgs e)
    {
        sortDescending = sortColumn == e.Column && !sortDescending;
        sortColumn = e.Column;
        ApplySort();
    }

    private void ApplySort()
    {
        for (int index = 0; index < ResultHeaders.Length; index++)
            resultsList.Columns[index].Text = ResultHeaders[index] +
                (index == sortColumn ? sortDescending ? " ▼" : " ▲" : "");
        if (sortColumn < 0) return;
        resultsList.ListViewItemSorter = new VideoRowComparer(sortColumn, sortDescending);
        resultsList.Sort();
    }

    private void Metadata_Click(object? sender, EventArgs e)
    {
        if (SelectedVideo() is not { } video) return;
        StartOperation("Loading selected-video metadata...", async token =>
        {
            var metadata = await client.GetVideoMetadataAsync(video, token);
            token.ThrowIfCancellationRequested();
            var row = resultsList.Items.Cast<ListViewItem>().FirstOrDefault(item => ((YtdlpVideo)item.Tag!).Id == video.Id);
            if (row is null) return;
            row.Tag = metadata;
            row.Text = metadata.Title;
            string[] values = [metadata.Uploader, FormatDuration(metadata.DurationSeconds),
                metadata.ViewCount?.ToString("N0") ?? "Unknown", metadata.Id, metadata.Url, FormatUploadDate(metadata.UploadDate)];
            for (int index = 0; index < values.Length; index++) row.SubItems[index + 1].Text = values[index];
            ApplySort();
            Selection_Changed(this, EventArgs.Empty);
            AppendLog(metadata.UploadDate is null ? "Upload date remains unknown; YouTube supplied no date." : "Selected-video metadata loaded.");
        });
    }

    private sealed class VideoRowComparer(int column, bool descending) : IComparer
    {
        public int Compare(object? x, object? y)
        {
            if (x is not ListViewItem { Tag: YtdlpVideo a } || y is not ListViewItem { Tag: YtdlpVideo b }) return 0;
            object? left = Key(a), right = Key(b);
            // Unknown values remain last in both directions.
            if (left is null || right is null) return left is null ? right is null ? 0 : 1 : -1;
            int result = left is string text ? StringComparer.CurrentCultureIgnoreCase.Compare(text, (string)right)
                : ((IComparable)left).CompareTo(right);
            if (result == 0) result = StringComparer.Ordinal.Compare(a.Id, b.Id);
            return descending ? -Math.Sign(result) : Math.Sign(result);
        }

        private object? Key(YtdlpVideo video) => column switch
        {
            0 => video.Title, 1 => video.Uploader, 2 => video.DurationSeconds, 3 => video.ViewCount,
            4 => video.Id, 5 => video.Url, 6 => video.UploadDate, _ => video.Title
        };
    }
}
