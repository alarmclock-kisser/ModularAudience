using ModularAudience.Ytdlp;
using System.Drawing.Drawing2D;
using System.Net.Http;

namespace ModularAudience.Forms.Modules.Dialogs;

internal static class YoutubeThumbnailLoader
{
    private const int MaximumBytes = 5 * 1024 * 1024;

    internal static IEnumerable<Uri> Candidates(YtdlpVideo video)
    {
        // Flat metadata often chooses /vi_webp/ images. GDI+ cannot decode WebP/AVIF.
        var canonical = YtdlpParser.ValidateYoutubeUrl(video.Url);
        string id = canonical.Query.TrimStart('?').Split('&').First(part => part.StartsWith("v=", StringComparison.Ordinal))[2..];
        yield return new Uri($"https://i.ytimg.com/vi/{id}/hqdefault.jpg");
        if (video.ThumbnailUrl is not null) yield return ValidateUrl(video.ThumbnailUrl);
    }

    internal static async Task<Bitmap> LoadAsync(HttpClient client, YtdlpVideo video, Action<string> warning, CancellationToken token)
    {
        var failures = new List<Exception>();
        foreach (var uri in Candidates(video).Distinct())
        {
            token.ThrowIfCancellationRequested();
            try { return await DownloadAsync(client, uri, token); }
            catch (Exception error) when (error is HttpRequestException or InvalidDataException or ArgumentException or System.Runtime.InteropServices.ExternalException)
            {
                failures.Add(error);
                warning($"Warning: Thumbnail source {uri.GetLeftPart(UriPartial.Path)} failed: {error.Message}");
            }
        }
        throw new AggregateException("No supported thumbnail image was available.", failures);
    }

    internal static Uri ValidateUrl(string url)
    {
        if (url.Length > 4096 || !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443 || !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("Thumbnail URLs must use trusted HTTPS hosts.");
        if (!new[] { "ytimg.com", "ggpht.com" }.Any(domain => uri.IdnHost.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
            uri.IdnHost.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Thumbnail host is not trusted.");
        return uri;
    }

    private static async Task<Bitmap> DownloadAsync(HttpClient client, Uri uri, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.ParseAdd("image/jpeg, image/png;q=0.9");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumBytes)
            throw new InvalidDataException("Thumbnail exceeds the 5 MiB limit.");
        using var data = new MemoryStream();
        using var input = await response.Content.ReadAsStreamAsync(token);
        byte[] buffer = new byte[32768];
        int count;
        while ((count = await input.ReadAsync(buffer.AsMemory(), token)) > 0)
        {
            if (data.Length + count > MaximumBytes) throw new InvalidDataException("Thumbnail exceeds the 5 MiB limit.");
            await data.WriteAsync(buffer.AsMemory(0, count), token);
        }
        token.ThrowIfCancellationRequested();
        return Decode(data, response.Content.Headers.ContentType?.MediaType);
    }

    private static Bitmap Decode(MemoryStream data, string? contentType)
    {
        var bytes = data.GetBuffer().AsSpan(0, (int)data.Length);
        bool jpeg = bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff;
        bool png = bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        if (!jpeg && !png)
            throw new InvalidDataException($"Unsupported thumbnail encoding ({contentType ?? "unknown MIME type"}); JPEG or PNG required, not WebP/AVIF/HTML.");
        data.Position = 0;
        using var original = Image.FromStream(data, false, true);
        if (original.Width > 4096 || original.Height > 4096 || (long)original.Width * original.Height > 8388608)
            throw new InvalidDataException("Thumbnail dimensions exceed the preview limit.");
        double scale = Math.Min(320d / original.Width, 180d / original.Height);
        var preview = new Bitmap(Math.Max(1, (int)(original.Width * scale)), Math.Max(1, (int)(original.Height * scale)));
        try
        {
            using var graphics = Graphics.FromImage(preview);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(original, 0, 0, preview.Width, preview.Height);
            return preview;
        }
        catch { preview.Dispose(); throw; }
    }
}
