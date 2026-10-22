using System.Net.Http;
using ModularAudience.Ytdlp;

namespace ModularAudience.Forms.Modules.Dialogs;

public partial class YoutubeBrowserWindow
{
    private readonly HttpClient thumbnailClient = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(15)
    };
    private readonly HashSet<Task> previewTasks = [];
    private CancellationTokenSource? previewCancellation;

    private async void Selection_Changed(object? sender, EventArgs e)
    {
        previewCancellation?.Cancel();
        ClearThumbnail();
        thumbnailPicture.Visible = thumbnailCheck.Checked;
        detailsLayout.RowStyles[1].Height = thumbnailCheck.Checked ? 35 : 0;
        detailsLayout.RowStyles[2].Height = thumbnailCheck.Checked ? 65 : 100;
        RefreshAvailability();
        var video = SelectedVideo();
        descriptionText.Text = video is null
            ? $"{resultsList.SelectedItems.Count} selected. Select one video to view its available metadata."
            : $"{video.Title}{Environment.NewLine}Uploader: {video.Uploader}{Environment.NewLine}" +
              $"Duration: {FormatDuration(video.DurationSeconds)} | Views: {video.ViewCount?.ToString("N0") ?? "Unknown"}" +
              $"{Environment.NewLine}Upload date: {FormatUploadDate(video.UploadDate)}" +
              $"{Environment.NewLine}ID: {video.Id}{Environment.NewLine}{video.Url}{Environment.NewLine}{Environment.NewLine}" +
              (string.IsNullOrWhiteSpace(video.Description) ? "No description was provided by the metadata service." : video.Description);
        if (closing || restoringSettings || !thumbnailCheck.Checked || video is null) return;
        var source = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        previewCancellation = source;
        var task = LoadPreviewAsync(video, source);
        previewTasks.Add(task);
        try { await task; }
        finally { previewTasks.Remove(task); }
    }

    private async Task LoadPreviewAsync(YtdlpVideo video, CancellationTokenSource source)
    {
        await Task.Yield();
        try
        {
            await Task.Delay(180, source.Token);
            using var preview = await YoutubeThumbnailLoader.LoadAsync(thumbnailClient, video, message =>
            {
                if (previewCancellation == source && !closing && !IsDisposed) AppendLog(message);
            }, source.Token);
            source.Token.ThrowIfCancellationRequested();
            if (previewCancellation == source && !closing && !IsDisposed)
                thumbnailPicture.Image = new Bitmap(preview);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (previewCancellation == source && !closing && !IsDisposed)
                AppendLog($"Warning: Thumbnail unavailable. {error.Message}");
        }
        finally
        {
            if (previewCancellation == source) previewCancellation = null;
            source.Dispose();
        }
    }

    private void ClearThumbnail()
    {
        var old = thumbnailPicture.Image;
        thumbnailPicture.Image = null;
        old?.Dispose();
    }
}
