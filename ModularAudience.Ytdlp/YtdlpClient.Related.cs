using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ModularAudience.Ytdlp;

public sealed partial class YtdlpClient
{
    public async Task<IReadOnlyList<YtdlpVideo>> GetRelatedVideosAsync(YtdlpVideo video, int maxResults,
        CancellationToken cancellationToken)
    {
        string url = ValidateVideo(video);
        YtdlpParser.ValidateMaxResults(maxResults);
        NetworkOptions.Validate();
        using var handler = new SocketsHttpHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.All };
        if (NetworkOptions.ProxyUrl is { } proxy)
        {
            handler.UseProxy = proxy.Length > 0;
            if (proxy.Length > 0) handler.Proxy = new WebProxy(proxy);
        }
        if (NetworkOptions.IpVersion != YtdlpIpVersion.Automatic)
        {
            var family = NetworkOptions.IpVersion == YtdlpIpVersion.IPv4 ? AddressFamily.InterNetwork : AddressFamily.InterNetworkV6;
            handler.ConnectCallback = async (context, token) =>
            {
                var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, family, token);
                var socket = new Socket(family, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, token);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch { socket.Dispose(); throw; }
            };
        }
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/131.0.0.0 Safari/537.36");
        http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        var progress = CreateProgress(null);
        progress.Report("Loading YouTube related / next-up recommendations...");
        using var pageRequest = new HttpRequestMessage(HttpMethod.Get, url + "&hl=en");
        string html = await ReadRelatedResponseAsync(http, pageRequest, cancellationToken);
        using var initial = YoutubeRelatedParser.ExtractInitialData(html);
        var results = new List<YtdlpVideo>();
        var seen = new HashSet<string>(StringComparer.Ordinal) { video.Id };
        string? continuation = null;
        if (initial is not null)
            Add(initial.RootElement);

        string version = Regex.Match(html, "\"INNERTUBE_CLIENT_VERSION\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;
        string visitor = Regex.Match(html, "\"VISITOR_DATA\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;
        var usedTokens = new HashSet<string>(StringComparer.Ordinal);
        for (int page = 0; results.Count < maxResults && version.Length > 0 && page < YtdlpParser.MaximumResults; page++)
        {
            if (results.Count > 0 && continuation is null) break;
            if (continuation is not null && !usedTokens.Add(continuation)) break;
            var payload = new Dictionary<string, object?>
            {
                ["context"] = new { client = new { clientName = "WEB", clientVersion = version, hl = "en", visitorData = visitor } }
            };
            if (continuation is null) payload["videoId"] = video.Id;
            else payload["continuation"] = continuation;
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://www.youtube.com/youtubei/v1/next")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Origin", "https://www.youtube.com");
            request.Headers.Referrer = new Uri(url);
            string json = await ReadRelatedResponseAsync(http, request, cancellationToken);
            using var next = JsonDocument.Parse(json);
            Add(next.RootElement);
            progress.Report($"Loaded {results.Count}/{maxResults} related videos; loading further recommendations...");
            if (continuation is null) break;
        }
        if (results.Count == 0)
            throw new InvalidDataException("YouTube returned no related / next-up videos for this video. Recommendations may be unavailable for this session.");
        return results.AsReadOnly();

        void Add(JsonElement root)
        {
            foreach (var related in YoutubeRelatedParser.Parse(root, maxResults, out continuation))
                if (seen.Add(related.Id) && results.Count < maxResults) results.Add(related);
        }
    }

    private static async Task<string> ReadRelatedResponseAsync(HttpClient client, HttpRequestMessage request, CancellationToken token)
    {
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await stream.ReadAsync(buffer, token)) > 0)
        {
            if (output.Length + count > 16 * 1024 * 1024)
                throw new InvalidDataException("The YouTube recommendations response exceeded the size limit.");
            output.Write(buffer, 0, count);
        }
        return Encoding.UTF8.GetString(output.GetBuffer(), 0, (int)output.Length);
    }
}
