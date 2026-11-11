using System.Net;

namespace ModularAudience.Ytdlp;

public sealed class YoutubeAudioStreamClient : IDisposable
{
    private const int MaximumRedirects = 5;
    private readonly HttpClient client;
    private readonly bool ownsClient;

    public YoutubeAudioStreamClient(YtdlpNetworkOptions? networkOptions = null)
    {
        networkOptions ??= new YtdlpNetworkOptions();
        this.client = new HttpClient(networkOptions.CreateHttpHandler())
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        this.client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/131.0.0.0 Safari/537.36");
        this.client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        this.ownsClient = true;
    }

    public YoutubeAudioStreamClient(HttpClient client)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<YoutubeAudioStreamResponse> OpenAsync(Uri streamUrl, CancellationToken cancellationToken)
    {
        YoutubeAudioStreamUrl.Validate(streamUrl);
        Uri currentUrl = streamUrl;
        for (int redirectCount = 0; ; redirectCount++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, currentUrl);
            var response = await this.client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (IsRedirect(response.StatusCode))
            {
                var location = response.Headers.Location;
                var status = response.StatusCode;
                response.Dispose();
                if (location is null)
                    throw new HttpRequestException("The YouTube audio stream redirected without a Location header.", null, status);
                if (redirectCount >= MaximumRedirects)
                    throw new HttpRequestException($"The YouTube audio stream exceeded {MaximumRedirects} redirects.", null, status);

                try
                {
                    currentUrl = location.IsAbsoluteUri ? location : new Uri(currentUrl, location);
                    YoutubeAudioStreamUrl.Validate(currentUrl);
                }
                catch (Exception error) when (error is ArgumentException or UriFormatException)
                {
                    throw new HttpRequestException(
                        "The YouTube audio stream redirected to an invalid or untrusted URL.", error, status);
                }
                continue;
            }

            try
            {
                response.EnsureSuccessStatusCode();
                var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                return new YoutubeAudioStreamResponse(response, stream);
            }
            catch
            {
                response.Dispose();
                throw;
            }
        }
    }

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.SeeOther or
            HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    public void Dispose()
    {
        if (this.ownsClient) this.client.Dispose();
    }
}

public sealed class YoutubeAudioStreamResponse : IDisposable, IAsyncDisposable
{
    private readonly HttpResponseMessage response;

    public Stream Stream { get; }
    public long? ContentLength => this.response.Content.Headers.ContentLength;

    internal YoutubeAudioStreamResponse(HttpResponseMessage response, Stream stream)
    {
        this.response = response;
        this.Stream = stream;
    }

    public void Dispose()
    {
        this.Stream.Dispose();
        this.response.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await this.Stream.DisposeAsync().ConfigureAwait(false);
        this.response.Dispose();
    }
}

public static class YoutubeAudioStreamUrl
{
    public static Uri Parse(string output)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(output);
        if (output.Length > 16384) throw new InvalidDataException("yt-dlp returned an audio URL that exceeds the size limit.");
        string[] lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length != 1 || !Uri.TryCreate(lines[0], UriKind.Absolute, out var url))
            throw new InvalidDataException("yt-dlp did not return exactly one audio stream URL.");
        Validate(url);
        return url;
    }

    public static void Validate(Uri streamUrl)
    {
        ArgumentNullException.ThrowIfNull(streamUrl);
        if (!streamUrl.IsAbsoluteUri || streamUrl.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(streamUrl.UserInfo) || !string.IsNullOrEmpty(streamUrl.Fragment) ||
            !(streamUrl.Host.Equals("googlevideo.com", StringComparison.OrdinalIgnoreCase) ||
              streamUrl.Host.EndsWith(".googlevideo.com", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("The audio stream URL must be an HTTPS Google Video URL.", nameof(streamUrl));
    }
}
