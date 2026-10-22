using System.Drawing.Drawing2D;
using System.Net.Http;

namespace ModularAudience.Forms.Modules.Dialogs;

public partial class YoutubeBrowserWindow
{
    private const int MaximumThumbnailBytes = 5 * 1024 * 1024;
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
        detailsLayout.RowStyles[1].Height = thumbnailCheck.Checked ? 160 : 0;
        RefreshAvailability();
        var video = SelectedVideo();
        descriptionText.Text = video is null
            ? $"{resultsList.SelectedItems.Count} selected. Select one video to view its available metadata."
            : $"{video.Title}{Environment.NewLine}Uploader: {video.Uploader}{Environment.NewLine}" +
              $"Duration: {FormatDuration(video.DurationSeconds)} | Views: {video.ViewCount?.ToString("N0") ?? "Unknown"}" +
              $"{Environment.NewLine}ID: {video.Id}{Environment.NewLine}{video.Url}{Environment.NewLine}{Environment.NewLine}" +
              (string.IsNullOrWhiteSpace(video.Description) ? "No description was provided by the metadata service." : video.Description);
        if (closing || restoringSettings || !thumbnailCheck.Checked || video?.ThumbnailUrl is null) return;
        var source = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        previewCancellation = source;
        var task = LoadPreviewAsync(video.ThumbnailUrl, source);
        previewTasks.Add(task);
        try { await task; }
        finally { previewTasks.Remove(task); }
    }

    private async Task LoadPreviewAsync(string url, CancellationTokenSource source)
    {
        await Task.Yield();
        try
        {
            await Task.Delay(180, source.Token);
            var uri = ValidateThumbnailUrl(url);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await thumbnailClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, source.Token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaximumThumbnailBytes)
                throw new InvalidDataException("Thumbnail exceeds the 5 MiB limit.");
            using var data = await ReadThumbnailAsync(response, source.Token);
            using var original = Image.FromStream(data, useEmbeddedColorManagement: false, validateImageData: false);
            if (original.Width > 4096 || original.Height > 4096 || (long)original.Width * original.Height > 8388608)
                throw new InvalidDataException("Thumbnail dimensions exceed the preview limit.");
            using var preview = RenderPreview(original);
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

    private static Uri ValidateThumbnailUrl(string url)
    {
        if (url.Length > 4096 || !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443 || !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("Thumbnail URLs must use trusted HTTPS hosts.");
        string host = uri.IdnHost;
        bool trusted = new[] { "ytimg.com", "ggpht.com" }.Any(domain =>
            host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));
        if (!trusted) throw new ArgumentException("Thumbnail host is not trusted.");
        return uri;
    }

    private static async Task<MemoryStream> ReadThumbnailAsync(HttpResponseMessage response, CancellationToken token)
    {
        var data = new MemoryStream();
        try
        {
            using var input = await response.Content.ReadAsStreamAsync(token);
            byte[] buffer = new byte[32768];
            int count;
            while ((count = await input.ReadAsync(buffer.AsMemory(), token)) > 0)
            {
                if (data.Length + count > MaximumThumbnailBytes)
                    throw new InvalidDataException("Thumbnail exceeds the 5 MiB limit.");
                await data.WriteAsync(buffer.AsMemory(0, count), token);
            }
            data.Position = 0;
            return data;
        }
        catch
        {
            data.Dispose();
            throw;
        }
    }

    private static Bitmap RenderPreview(Image original)
    {
        double scale = Math.Min(320d / original.Width, 180d / original.Height);
        var preview = new Bitmap(Math.Max(1, (int)(original.Width * scale)), Math.Max(1, (int)(original.Height * scale)));
        try
        {
            using var graphics = Graphics.FromImage(preview);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(original, 0, 0, preview.Width, preview.Height);
            return preview;
        }
        catch
        {
            preview.Dispose();
            throw;
        }
    }

    private void ClearThumbnail()
    {
        var old = thumbnailPicture.Image;
        thumbnailPicture.Image = null;
        old?.Dispose();
    }
}
