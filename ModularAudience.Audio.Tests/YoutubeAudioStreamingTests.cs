using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Ytdlp;
using System.Net;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class YoutubeAudioStreamingTests
{
    [TestMethod]
    public void ParseAudioUrl_RequiresOneHttpsGoogleVideoAddress()
    {
        var url = YoutubeAudioStreamUrl.Parse(
            "https://rr1.googlevideo.com/videoplayback?expire=123&sig=example\r\n");
        Assert.AreEqual("rr1.googlevideo.com", url.Host);
        Assert.ThrowsExactly<ArgumentException>(() =>
            YoutubeAudioStreamUrl.Parse("http://rr1.googlevideo.com/videoplayback"));
        Assert.ThrowsExactly<InvalidDataException>(() =>
            YoutubeAudioStreamUrl.Parse("https://rr1.googlevideo.com/a\nhttps://rr2.googlevideo.com/b"));
    }

    [TestMethod]
    public async Task OpenAsync_StreamsResponseBodyOnDemand()
    {
        byte[] expected = [1, 2, 3, 4, 5];
        using var contentStream = new TrackingStream(expected);
        using var handler = new AudioHandler(contentStream);
        using var http = new HttpClient(handler);
        using var client = new YoutubeAudioStreamClient(http);
        await using var response = await client.OpenAsync(
            new Uri("https://rr1.googlevideo.com/videoplayback?token=test"), CancellationToken.None);

        Assert.AreEqual(0, contentStream.ReadCount);
        using var output = new MemoryStream();
        await response.Stream.CopyToAsync(output);
        CollectionAssert.AreEqual(expected, output.ToArray());
        Assert.IsTrue(contentStream.ReadCount > 0);
        Assert.AreEqual(1, handler.RequestCount);
    }

    [TestMethod]
    public async Task OpenAsync_FollowsRelativeGoogleVideoRedirect()
    {
        byte[] expected = [1, 2, 3];
        using var contentStream = new TrackingStream(expected);
        using var redirect = new HttpResponseMessage(HttpStatusCode.Found);
        redirect.Headers.Location = new Uri("/videoplayback?token=redirected", UriKind.Relative);
        using var handler = new RedirectHandler(redirect,
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(contentStream) });
        using var http = new HttpClient(handler);
        using var client = new YoutubeAudioStreamClient(http);

        await using var response = await client.OpenAsync(
            new Uri("https://rr1.googlevideo.com/videoplayback?token=initial"), CancellationToken.None);
        using var output = new MemoryStream();
        await response.Stream.CopyToAsync(output);

        CollectionAssert.AreEqual(expected, output.ToArray());
        Assert.AreEqual(2, handler.RequestUris.Count);
        Assert.AreEqual("https://rr1.googlevideo.com/videoplayback?token=redirected",
            handler.RequestUris[1].AbsoluteUri);
    }

    [TestMethod]
    public async Task OpenAsync_RejectsRedirectOutsideGoogleVideo()
    {
        using var redirect = new HttpResponseMessage(HttpStatusCode.Found)
        {
            Headers = { Location = new Uri("https://example.com/audio") }
        };
        using var handler = new RedirectHandler(redirect);
        using var http = new HttpClient(handler);
        using var client = new YoutubeAudioStreamClient(http);

        var error = await Assert.ThrowsExactlyAsync<HttpRequestException>(async () =>
            await client.OpenAsync(new Uri("https://rr1.googlevideo.com/audio"), CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.Found, error.StatusCode);
        Assert.AreEqual(1, handler.RequestUris.Count);
    }

    [TestMethod]
    public async Task OpenAsync_StopsRedirectLoops()
    {
        var redirects = Enumerable.Range(0, 6).Select(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
            response.Headers.Location = new Uri("/audio", UriKind.Relative);
            return response;
        }).ToArray();
        using var handler = new RedirectHandler(redirects);
        using var http = new HttpClient(handler);
        using var client = new YoutubeAudioStreamClient(http);

        var error = await Assert.ThrowsExactlyAsync<HttpRequestException>(async () =>
            await client.OpenAsync(new Uri("https://rr1.googlevideo.com/audio"), CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.TemporaryRedirect, error.StatusCode);
        Assert.AreEqual(6, handler.RequestUris.Count);
    }

    [TestMethod]
    public async Task OpenAsync_RejectsUnsafeHostBeforeSendingRequest()
    {
        using var handler = new AudioHandler(new TrackingStream([]));
        using var http = new HttpClient(handler);
        using var client = new YoutubeAudioStreamClient(http);

        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await client.OpenAsync(new Uri("https://example.com/audio"), CancellationToken.None));
        Assert.AreEqual(0, handler.RequestCount);
    }

    [TestMethod]
    public async Task OpenAsync_ReportsHttpFailure()
    {
        using var handler = new AudioHandler(new TrackingStream([]), HttpStatusCode.NotFound);
        using var http = new HttpClient(handler);
        using var client = new YoutubeAudioStreamClient(http);

        await Assert.ThrowsExactlyAsync<HttpRequestException>(async () =>
            await client.OpenAsync(new Uri("https://rr1.googlevideo.com/audio"), CancellationToken.None));
        Assert.AreEqual(1, handler.RequestCount);
    }

    private sealed class AudioHandler(Stream stream, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        internal int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            this.RequestCount++;
            Assert.AreEqual("https", request.RequestUri!.Scheme);
            Assert.IsTrue(request.RequestUri.Host.EndsWith(".googlevideo.com", StringComparison.Ordinal));
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StreamContent(stream) });
        }
    }

    private sealed class RedirectHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        internal List<Uri> RequestUris { get; } = [];
        private int responseIndex;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            this.RequestUris.Add(request.RequestUri!);
            if (this.responseIndex >= responses.Length)
                throw new InvalidOperationException("The redirect test handler ran out of responses.");
            return Task.FromResult(responses[this.responseIndex++]);
        }
    }

    private sealed class TrackingStream(byte[] bytes) : MemoryStream(bytes)
    {
        internal int ReadCount { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            this.ReadCount++;
            return base.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            this.ReadCount++;
            return base.Read(buffer);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            this.ReadCount++;
            return base.ReadAsync(buffer, cancellationToken);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            this.ReadCount++;
            return base.ReadAsync(buffer, offset, count, cancellationToken);
        }
    }
}
