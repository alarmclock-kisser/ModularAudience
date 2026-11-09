using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ModularAudience.Ytdlp;

public static partial class YtdlpParser
{
    public const int MaximumResults = 200;

    public static string ValidateOutputFormat(string format)
    {
        ArgumentNullException.ThrowIfNull(format);
        var normalized = format.Trim().ToLowerInvariant();
        return normalized is "mp3" or "wav"
            ? normalized
            : throw new ArgumentException("Only mp3 and wav output formats are supported.", nameof(format));
    }

    public static void ValidateMaxResults(int maxResults)
    {
        if (maxResults is < 1 or > MaximumResults)
            throw new ArgumentOutOfRangeException(nameof(maxResults), $"Use 1 to {MaximumResults} results.");
    }

    public static bool IsYoutubeUrl(string? url)
    {
        try { return url is not null && ValidateYoutubeUrl(url) is not null; }
        catch (ArgumentException) { return false; }
    }

    public static Uri ValidateYoutubeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > 4096 ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) || uri.Port != 443)
            throw new ArgumentException("An HTTPS YouTube video or playlist URL is required.", nameof(url));

        var host = uri.IdnHost.ToLowerInvariant();
        if (host is not ("youtube.com" or "www.youtube.com" or "m.youtube.com" or "music.youtube.com" or "youtu.be"))
            throw new ArgumentException("Only youtube.com and youtu.be URLs are allowed.", nameof(url));

        return Canonicalize(uri, host);
    }

    private static Uri Canonicalize(Uri uri, string host)
    {
        var segments = uri.AbsolutePath.Trim('/').Split('/');
        if (host != "youtu.be" &&
            ((segments[0] == "channel" && segments.Length is 2 or 3 && Regex.IsMatch(segments[1], "^UC[A-Za-z0-9_-]{22}$") &&
              (segments.Length == 2 || segments[2] == "videos")) ||
             (segments.Length is 1 or 2 && segments[0].StartsWith('@') && Regex.IsMatch(segments[0], "^@[\\p{L}\\p{N}_.-]+$") &&
              (segments.Length == 1 || segments[1] == "videos"))))
            return new Uri("https://www.youtube.com/" + (segments[0] == "channel" ? "channel/" + segments[1] : segments[0]) + "/videos");
        var videoId = host == "youtu.be" && segments.Length == 1 ? segments[0] : null;
        if (host != "youtu.be" && segments.Length == 2 && segments[0] is "shorts" or "live" or "embed")
            videoId = segments[1];
        if (host != "youtu.be" && uri.AbsolutePath == "/watch")
            videoId = QueryValue(uri, "v");

        var playlist = QueryValue(uri, "list");
        if (videoId is not null && VideoIdPattern().IsMatch(videoId))
            return new Uri($"https://www.youtube.com/watch?v={videoId}" +
                (IsPlaylistId(playlist) ? $"&list={playlist}" : string.Empty));
        if (host != "youtu.be" && uri.AbsolutePath == "/playlist" && IsPlaylistId(playlist))
            return new Uri($"https://www.youtube.com/playlist?list={playlist}");
        throw new ArgumentException("The URL must identify a YouTube video or playlist.", nameof(uri));
    }

    private static bool IsPlaylistId(string? value) =>
        value is { Length: >= 2 and <= 200 } && PlaylistIdPattern().IsMatch(value);

    private static string? QueryValue(Uri uri, string name)
    {
        foreach (var part in uri.Query.TrimStart('?').Split('&'))
        {
            var pair = part.Split('=', 2);
            if (pair.Length == 2 && pair[0] == name)
                return Uri.UnescapeDataString(pair[1]);
        }
        return null;
    }

    public static IReadOnlyList<YtdlpVideo> ParseVideos(string json, int maxResults = 50, bool includeChannels = false, bool includePlaylists = false)
    {
        ValidateMaxResults(maxResults);
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException("Expected a video or playlist JSON object.");
        var result = new List<YtdlpVideo>();
        if (root.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in entries.EnumerateArray())
            {
                var video = ParseEntry(entry);
                if (video is not null)
                    result.Add(video);
                if (result.Count == maxResults)
                    break;
            }
        }
        else if (ParseEntry(root) is { } video)
            result.Add(video);
        return result.AsReadOnly();

        YtdlpVideo? ParseEntry(JsonElement entry) => ParseVideo(entry)
            ?? (includeChannels ? ParseChannel(entry) : null)
            ?? (includePlaylists ? ParsePlaylist(entry) : null);
    }

    public static YtdlpVideo? ParseVideo(JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.Object)
            return null;
        var id = Text(entry, "id");
        if (id is null || !VideoIdPattern().IsMatch(id))
            return null;
        if (Text(entry, "ie_key") is "YoutubeTab" or "YoutubePlaylist") return null;
        foreach (string key in new[] { "url", "webpage_url" })
            if (Uri.TryCreate(Text(entry, key), UriKind.Absolute, out var url) && url.AbsolutePath == "/playlist") return null;
        var duration = Number(entry, "duration");
        return new YtdlpVideo
        {
            Id = id,
            Title = Text(entry, "title") ?? id,
            Url = $"https://www.youtube.com/watch?v={id}",
            Uploader = Text(entry, "uploader") ?? Text(entry, "channel") ?? string.Empty,
            ChannelUrl = ParseChannelUrl(entry),
            DurationSeconds = duration is >= 0 && double.IsFinite(duration.Value) ? duration : null,
            ThumbnailUrl = Thumbnail(entry),
            ViewCount = Integer(entry, "view_count"),
            UploadDate = DateOnly.TryParseExact(Text(entry, "upload_date"), "yyyyMMdd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var uploaded) ? uploaded : null,
            Description = Text(entry, "description")
        };
    }

    private static YtdlpVideo? ParseChannel(JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.Object) return null;
        string? id = Text(entry, "id");
        if (id is null || !Regex.IsMatch(id, "^UC[A-Za-z0-9_-]{22}$")) return null;
        return new YtdlpVideo { IsChannel = true, Id = id, Title = Text(entry, "title") ?? Text(entry, "channel") ?? id,
            Uploader = Text(entry, "channel") ?? Text(entry, "uploader") ?? string.Empty,
            Url = $"https://www.youtube.com/channel/{id}/videos", ThumbnailUrl = Thumbnail(entry), Description = Text(entry, "description") };
    }

    private static YtdlpVideo? ParsePlaylist(JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.Object) return null;
        // A search hit must identify a playlist URL, not merely have a non-video ID.
        // In particular, never turn a search/channel container into a playlist hit.
        foreach (string key in new[] { "url", "webpage_url", "original_url" })
        {
            string? value = Text(entry, key);
            if (value is null) continue;
            try
            {
                Uri url = ValidateYoutubeUrl(value);
                if (url.AbsolutePath != "/playlist") continue;
                string id = QueryValue(url, "list")!;
                return new YtdlpVideo
                {
                    IsPlaylist = true, Id = id, Title = Text(entry, "title") ?? id, Url = url.AbsoluteUri,
                    Uploader = Text(entry, "uploader") ?? Text(entry, "channel") ?? string.Empty,
                    ChannelUrl = ParseChannelUrl(entry), ThumbnailUrl = Thumbnail(entry),
                    PlaylistCount = Integer(entry, "playlist_count") ?? Integer(entry, "n_entries") ?? Integer(entry, "video_count"),
                    Description = Text(entry, "description")
                };
            }
            catch (ArgumentException) { }
        }
        return null;
    }

    public static bool MatchesResultType(YtdlpVideo result, YtdlpResultType resultType) => resultType switch
    {
        YtdlpResultType.All => true,
        YtdlpResultType.Videos => result.IsVideo,
        YtdlpResultType.Playlists => result.IsPlaylist,
        YtdlpResultType.Channels => result.IsChannel,
        _ => throw new ArgumentOutOfRangeException(nameof(resultType))
    };

    private static string? ParseChannelUrl(JsonElement entry)
    {
        string? id = Text(entry, "channel_id");
        if (id != null && Regex.IsMatch(id, "^UC[A-Za-z0-9_-]{22}$")) return $"https://www.youtube.com/channel/{id}/videos";
        foreach (string key in new[] { "channel_url", "uploader_url" })
        {
            try
            {
                string? value = Text(entry, key);
                if (value == null) continue;
                var canonical = ValidateYoutubeUrl(value);
                if (canonical.AbsolutePath.StartsWith("/channel/", StringComparison.Ordinal) || canonical.AbsolutePath.StartsWith("/@", StringComparison.Ordinal))
                    return canonical.AbsoluteUri;
            }
            catch (ArgumentException) { }
        }
        return null;
    }

    private static string? Thumbnail(JsonElement entry)
    {
        if (SafeImage(Text(entry, "thumbnail")) is { } direct)
            return direct;
        if (entry.TryGetProperty("thumbnails", out var thumbnails) && thumbnails.ValueKind == JsonValueKind.Array)
        {
            foreach (var image in thumbnails.EnumerateArray().Reverse())
                if (image.ValueKind == JsonValueKind.Object && SafeImage(Text(image, "url")) is { } url)
                    return url;
        }
        return null;
    }

    private static string? SafeImage(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
        string.IsNullOrEmpty(uri.UserInfo) ? uri.AbsoluteUri : null;

    private static string? Text(JsonElement element, string key) =>
        element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static double? Number(JsonElement element, string key)
    {
        if (!element.TryGetProperty(key, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
            return number;
        return value.ValueKind == JsonValueKind.String &&
            double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number) ? number : null;
    }

    private static long? Integer(JsonElement element, string key)
    {
        if (!element.TryGetProperty(key, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
            return number >= 0 ? number : null;
        return value.ValueKind == JsonValueKind.String &&
            long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number) && number >= 0 ? number : null;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{11}$", RegexOptions.CultureInvariant)]
    private static partial Regex VideoIdPattern();

    [GeneratedRegex("^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex PlaylistIdPattern();
}
