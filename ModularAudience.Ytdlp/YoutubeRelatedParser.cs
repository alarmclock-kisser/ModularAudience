using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ModularAudience.Ytdlp;

public static class YoutubeRelatedParser
{
    internal static JsonDocument? ExtractInitialData(string html)
    {
        var match = Regex.Match(html, @"(?:var\s+ytInitialData|window\[""ytInitialData""\]|ytInitialData)\s*=\s*");
        if (!match.Success) return null;
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(html[(match.Index + match.Length)..]));
        try { return JsonDocument.ParseValue(ref reader); }
        catch (JsonException) { return null; }
    }

    public static IReadOnlyList<YtdlpVideo> Parse(JsonElement root, int maximum, out string? continuation)
    {
        YtdlpParser.ValidateMaxResults(maximum);
        var videos = new List<YtdlpVideo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? next = null;
        // Only the watch-page recommendations and their continuation, never comments, ads or the current video.
        if (At(root, "contents", "twoColumnWatchNextResults", "secondaryResults", "secondaryResults") is { } secondary)
            Visit(secondary);
        foreach (string key in new[] { "onResponseReceivedEndpoints", "onResponseReceivedActions" })
            if (root.TryGetProperty(key, out var actions) && actions.ValueKind == JsonValueKind.Array)
                foreach (var action in actions.EnumerateArray())
                    if (At(action, "appendContinuationItemsAction", "continuationItems") is { } items) Visit(items);
        continuation = next;
        return videos.AsReadOnly();

        void Visit(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray()) Visit(item);
                return;
            }
            if (element.ValueKind != JsonValueKind.Object) return;
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name is "adSlotRenderer" or "promotedSparklesWebRenderer" or "promotedVideoRenderer") continue;
                if (property.Name == "continuationItemRenderer")
                    next ??= Text(At(property.Value, "continuationEndpoint", "continuationCommand", "token"));
                else if (property.Name is "compactVideoRenderer" or "videoRenderer" or "playlistPanelVideoRenderer")
                    Add(Renderer(property.Value));
                else if (property.Name == "lockupViewModel") Add(Lockup(property.Value));
                else Visit(property.Value);
            }
        }

        void Add(YtdlpVideo? video)
        {
            if (video is not null && videos.Count < maximum && seen.Add(video.Id)) videos.Add(video);
        }
    }

    private static YtdlpVideo? Renderer(JsonElement renderer) => Build(
        Text(At(renderer, "videoId")), Display(At(renderer, "title")),
        Display(At(renderer, "shortBylineText")) ?? Display(At(renderer, "longBylineText")),
        Display(At(renderer, "lengthText")), At(renderer, "thumbnail", "thumbnails"),
        Display(At(renderer, "viewCountText")));

    private static YtdlpVideo? Lockup(JsonElement renderer)
    {
        if (Text(At(renderer, "contentType")) != "LOCKUP_CONTENT_TYPE_VIDEO") return null;
        var metadata = At(renderer, "metadata", "lockupMetadataViewModel");
        string? uploader = null, views = null;
        if (At(metadata, "metadata", "contentMetadataViewModel", "metadataRows") is { ValueKind: JsonValueKind.Array } rows)
            foreach (var row in rows.EnumerateArray())
                if (At(row, "metadataParts") is { ValueKind: JsonValueKind.Array } parts)
                    foreach (var part in parts.EnumerateArray())
                    {
                        if (uploader is null && At(part, "text", "commandRuns") is { } commands && commands.ToString().Contains("browseEndpoint", StringComparison.Ordinal))
                            uploader = Text(At(part, "text", "content"));
                        var content = Text(At(part, "text", "content"));
                        if (content?.Contains("views", StringComparison.OrdinalIgnoreCase) == true) views = content;
                    }
        string? duration = null;
        FindDuration(renderer);
        return Build(Text(At(renderer, "contentId")), Text(At(metadata, "title", "content")), uploader,
            duration, At(renderer, "contentImage", "thumbnailViewModel", "image", "sources"), views);

        void FindDuration(JsonElement node)
        {
            if (node.ValueKind == JsonValueKind.Array)
                foreach (var item in node.EnumerateArray()) FindDuration(item);
            else if (node.ValueKind == JsonValueKind.Object)
                foreach (var property in node.EnumerateObject())
                    if (property.Name == "thumbnailBadgeViewModel") duration ??= Text(At(property.Value, "text"));
                    else FindDuration(property.Value);
        }
    }

    private static YtdlpVideo? Build(string? id, string? title, string? uploader, string? duration, JsonElement? images, string? views)
    {
        if (id is null || !Regex.IsMatch(id, "^[A-Za-z0-9_-]{11}$")) return null;
        double? seconds = null;
        if (duration is not null && Regex.IsMatch(duration, @"^\d+(?::\d{2}){1,2}$"))
            seconds = duration.Split(':').Aggregate(0d, (total, part) => total * 60 + int.Parse(part, CultureInfo.InvariantCulture));
        string? thumbnail = null;
        if (images is { ValueKind: JsonValueKind.Array } array)
            thumbnail = array.EnumerateArray().Select(image => Text(At(image, "url"))).LastOrDefault(url => url is not null);
        long? viewCount = null;
        if (views is not null)
        {
            var match = Regex.Match(views, @"^(\d[\d,.]*)([KMB])?\s+views?", RegexOptions.IgnoreCase);
            if (match.Success && double.TryParse(match.Groups[1].Value.Replace(",", ""), CultureInfo.InvariantCulture, out double count))
            {
                count *= match.Groups[2].Value.ToUpperInvariant() switch { "K" => 1000, "M" => 1000000, "B" => 1000000000, _ => 1 };
                if (double.IsFinite(count) && count >= 0 && count < long.MaxValue) viewCount = (long)count;
            }
        }
        return new YtdlpVideo { Id = id, Title = title ?? id, Url = "https://www.youtube.com/watch?v=" + id,
            Uploader = uploader ?? string.Empty, DurationSeconds = seconds, ThumbnailUrl = thumbnail, ViewCount = viewCount };
    }

    private static string? Display(JsonElement? element)
    {
        if (Text(At(element, "simpleText")) is { } simple) return simple;
        return At(element, "runs") is { ValueKind: JsonValueKind.Array } runs
            ? string.Concat(runs.EnumerateArray().Select(run => Text(At(run, "text")))) : null;
    }

    private static string? Text(JsonElement? element) => element is { ValueKind: JsonValueKind.String } text ? text.GetString() : null;

    private static JsonElement? At(JsonElement? node, params string[] path)
    {
        foreach (var key in path)
        {
            if (node is not { ValueKind: JsonValueKind.Object } current || !current.TryGetProperty(key, out var value)) return null;
            node = value;
        }
        return node;
    }
}
