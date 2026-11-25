using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Vocal;
using ModularAudience.Llama;

namespace ModularAudience.Audio.Tests;

[TestClass]
public class VocalStreamChunkerTests
{
    private static List<string> Feed(VocalStreamChunkingSettings settings, params string[] deltas)
    {
        var chunker = new VocalStreamChunker(settings);
        var result = new List<string>();
        foreach (string delta in deltas)
        {
            result.AddRange(chunker.Append(delta));
        }

        result.AddRange(chunker.Flush());
        return result;
    }

    [TestMethod]
    public void SentenceEnd_SplitsAndKeepsClosingQuote()
    {
        List<string> chunks = Feed(new VocalStreamChunkingSettings(), "Hello world, this is \"Ada\".");

        CollectionAssert.AreEqual(new[] {"Hello world, this is \"Ada\"."}, chunks);
    }

    [TestMethod]
    public void TokenByToken_MatchesBatchResult()
    {
        string[] tokens = ["Hel", "lo world", ". Next", " sentence!", ""];

        List<string> streamed = Feed(new VocalStreamChunkingSettings(), tokens);
        List<string> batch = Feed(new VocalStreamChunkingSettings(), string.Concat(tokens));

        CollectionAssert.AreEqual(batch, streamed);
        CollectionAssert.AreEqual(new[] {"Hello world.", "Next sentence!"}, streamed);
    }

    [TestMethod]
    public void Comma_WaitsForMinimumWords()
    {
        var settings = new VocalStreamChunkingSettings
        {
            MinWords = 3,
            BreakOnSentenceEnd = false,
            BreakOnColon = false,
        };

        List<string> chunks = Feed(settings, "one, two, three, four");

        CollectionAssert.AreEqual(new[] {"one, two, three,", "four"}, chunks);
    }

    [TestMethod]
    public void MaxWords_ForcesCutAtWordBoundary()
    {
        var settings = new VocalStreamChunkingSettings
        {
            MinWords = 4,
            MaxWords = 4,
            BreakOnSentenceEnd = false,
        };

        List<string> chunks = Feed(settings, "one two three four five six");

        CollectionAssert.AreEqual(new[] {"one two three four", "five six"}, chunks);
    }

    [TestMethod]
    public void AbbreviationsDecimalsAndUrls_DoNotEndChunk()
    {
        var settings = new VocalStreamChunkingSettings { BreakOnColon = false };

        List<string> chunks = Feed(settings, "Dr. Smith paid 3.14 e.g. today. See https://example.com/docs now!");

        CollectionAssert.AreEqual(
            new[] { "Dr. Smith paid 3.14 e.g. today.", "See https://example.com/docs now!" }, chunks);
    }

    [TestMethod]
    public void Ellipsis_IsTreatedAsSentenceEnd()
    {
        List<string> chunks = Feed(new VocalStreamChunkingSettings(), "Well... really?");

        CollectionAssert.AreEqual(new[] {"Well...", "really?"}, chunks);
    }

    [TestMethod]
    public void Flush_ReturnsPartialTailAndResetClearsIt()
    {
        var chunker = new VocalStreamChunker();
        Assert.AreEqual(0, chunker.Append("still waiting").Count);
        Assert.IsTrue(chunker.HasBufferedText);

        CollectionAssert.AreEqual(new[] {"still waiting"}, chunker.Flush().ToArray());
        Assert.IsFalse(chunker.HasBufferedText);

        chunker.Append("discard me");
        chunker.Reset();
        Assert.AreEqual(0, chunker.Flush().Count);
    }
}

[TestClass]
public class VocalLlmStreamSettingsTests
{
    [TestMethod]
    public void SaveLoad_RoundTripsAllValues()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "ma-vocal-stream-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var settings = new VocalLlmStreamSettings
            {
                UpstreamUrl = "http://localhost:1234",
                ListenPort = 9090,
                UseModelFilter = true,
                ModelFilter = "gemma",
                MinWords = 2,
                MaxWords = 20,
                BreakOnComma = false,
                BreakOnColon = true,
                BreakOnSentenceEnd = true,
                FlushMilliseconds = 400,
                MinBufferMilliseconds = 1200,
                AdaptiveSpeed = true,
                MinSpeedFactor = 0.5,
                Enabled = true,
            };
            settings.Save(path);

            VocalLlmStreamSettings loaded = VocalLlmStreamSettings.Load(path);

            Assert.AreEqual("http://localhost:1234", loaded.UpstreamUrl);
            Assert.AreEqual(9090, loaded.ListenPort);
            Assert.IsTrue(loaded.UseModelFilter);
            Assert.AreEqual("gemma", loaded.ModelFilter);
            Assert.AreEqual(2, loaded.MinWords);
            Assert.AreEqual(20, loaded.MaxWords);
            Assert.IsFalse(loaded.BreakOnComma);
            Assert.AreEqual(400, loaded.FlushMilliseconds);
            Assert.AreEqual(1200, loaded.MinBufferMilliseconds);
            Assert.IsTrue(loaded.AdaptiveSpeed);
            Assert.AreEqual(0.5, loaded.MinSpeedFactor);
            Assert.IsTrue(loaded.Enabled);
            Assert.AreEqual(2, loaded.CreateChunkingSettings().MinWords);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [TestMethod]
    public void Normalize_ClampsInvalidValues()
    {
        var settings = new VocalLlmStreamSettings
        {
            UpstreamUrl = "not a url",
            ListenPort = -5,
            MinWords = 0,
            MaxWords = 2,
            FlushMilliseconds = -1,
            MinBufferMilliseconds = 999999,
            MinSpeedFactor = 7,
        };

        settings.Normalize();

        Assert.AreEqual("http://127.0.0.1:9931", settings.UpstreamUrl);
        Assert.AreEqual(1, settings.ListenPort);
        Assert.AreEqual(1, settings.MinWords);
        Assert.AreEqual(2, settings.MaxWords);
        Assert.AreEqual(100, settings.FlushMilliseconds);
        Assert.AreEqual(10000, settings.MinBufferMilliseconds);
        Assert.AreEqual(1.0, settings.MinSpeedFactor);
    }
}

[TestClass]
public class LlamaStreamProxyTests
{
    [TestMethod]
    public void ParseAssistantEvent_ReadsDeltaRoleAndFinishReason()
    {
        AssistantSseEvent delta = LlamaStreamProxy.ParseAssistantEvent(
            "{\"choices\":[{\"delta\":{\"role\":\"assistant\",\"content\":\"Hello\"},\"finish_reason\":null}]}");

        Assert.IsTrue(delta.Started);
        Assert.AreEqual("Hello", delta.Content);
        Assert.IsFalse(delta.Finished);
        Assert.IsFalse(delta.IsDone);

        AssistantSseEvent finished = LlamaStreamProxy.ParseAssistantEvent(
            "{\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}");

        Assert.IsTrue(finished.Finished);
        Assert.IsNull(finished.Content);
    }

    [TestMethod]
    public void ParseAssistantEvent_HandlesDoneLegacyAndMalformed()
    {
        Assert.IsTrue(LlamaStreamProxy.ParseAssistantEvent("[DONE]").IsDone);

        AssistantSseEvent legacy = LlamaStreamProxy.ParseAssistantEvent("{\"content\":\"Hi there\"}");
        Assert.IsTrue(legacy.Started);
        Assert.AreEqual("Hi there", legacy.Content);

        AssistantSseEvent malformed = LlamaStreamProxy.ParseAssistantEvent("not json");
        Assert.IsFalse(malformed.Started);
        Assert.IsNull(malformed.Content);
    }

    [TestMethod]
    public void RequestInspection_DetectsStreamFlagAndModelFilter()
    {
        byte[] streaming = Encoding.UTF8.GetBytes("{\"model\":\"gemma-4-e4b\",\"stream\":true}");
        byte[] nonStreaming = Encoding.UTF8.GetBytes("{\"model\":\"gemma-4-e4b\",\"stream\":false}");

        Assert.IsTrue(LlamaStreamProxy.WantsStreaming(streaming));
        Assert.IsFalse(LlamaStreamProxy.WantsStreaming(nonStreaming));
        Assert.IsTrue(LlamaStreamProxy.ModelMatches(streaming, "gemma"));
        Assert.IsFalse(LlamaStreamProxy.ModelMatches(streaming, "llama"));
        Assert.IsTrue(LlamaStreamProxy.ModelMatches(streaming, ""));
    }

    [TestMethod]
    public void ModelNamesMatch_HandlesFullGgufPathsAndShortAliases()
    {
        const string fullPath = @"D:\Models\GGUF\Others\gemma-4-12b-it-qat-q4_0\gemma-4-12b-it-qat-q4_0.gguf";

        Assert.IsTrue(LlamaStreamProxy.ModelNamesMatch(fullPath, "gemma-4-12b-it-qat-q4_0.gguf"));
        Assert.IsTrue(LlamaStreamProxy.ModelNamesMatch("gemma-4-12b-it-qat-q4_0.gguf", fullPath));
        Assert.IsTrue(LlamaStreamProxy.ModelNamesMatch("gemma-4", fullPath));
        Assert.IsFalse(LlamaStreamProxy.ModelNamesMatch("llama-3", fullPath));
    }
}

[TestClass]
public class LlamaHttpTests
{
    [TestMethod]
    public void LoopbackHttps_CertificateIsAccepted()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:9931/v1/models");
        Assert.IsTrue(LlamaHttp.AcceptCertificate(request,
            System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [TestMethod]
    public void RemoteHttps_InvalidCertificateIsRejected()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://llama.example.com/v1/models");
        Assert.IsFalse(LlamaHttp.AcceptCertificate(request,
            System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors));
        Assert.IsTrue(LlamaHttp.AcceptCertificate(request, System.Net.Security.SslPolicyErrors.None));
    }

    [TestMethod]
    public void DescribeError_IncludesInnerException()
    {
        var error = new HttpRequestException("An error occurred while sending the request.",
            new AuthenticationException("The remote certificate is invalid."));

        string text = LlamaHttp.DescribeError(error);

        StringAssert.Contains(text, "An error occurred while sending the request.");
        StringAssert.Contains(text, "The remote certificate is invalid.");
    }
}

[TestClass]
public class LlamaStreamProxyIntegrationTests
{
    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task Proxy_ForwardsSseAndPublishesAssistantDeltas()
    {
        int upstreamPort = FreePort();
        int proxyPort = FreePort();
        var upstream = new TcpListener(IPAddress.Loopback, upstreamPort);
        upstream.Start();

        const string sse =
            "data: {\"choices\":[{\"delta\":{\"role\":\"assistant\"}}]}\r\n\r\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\"Hello \"}}]}\r\n\r\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\"world\"},\"finish_reason\":\"stop\"}]}\r\n\r\n" +
            "data: [DONE]\r\n\r\n";

        Task serverTask = Task.Run(async () =>
        {
            using TcpClient client = await upstream.AcceptTcpClientAsync();
            using NetworkStream stream = client.GetStream();
            // Buffer size 1 prevents the reader from swallowing body bytes while scanning headers.
            var reader = new StreamReader(stream, Encoding.ASCII, false, 1, leaveOpen: true);
            int contentLength = 0;
            string? line;
            while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
            {
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                {
                    int.TryParse(line["Content-Length:".Length..].Trim(), out contentLength);
                }
            }

            if (contentLength > 0)
            {
                byte[] body = new byte[contentLength];
                int read = await stream.ReadAsync(body);
                if (read < contentLength)
                {
                    while (read < contentLength)
                    {
                        read += await stream.ReadAsync(body.AsMemory(read, contentLength - read));
                    }
                }
            }

            byte[] response = Encoding.UTF8.GetBytes(
                "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nConnection: close\r\n\r\n" + sse);
            await stream.WriteAsync(response);
            await stream.FlushAsync();
        });

        var deltas = new List<string>();
        var errors = new List<string>();
        using (var proxy = new LlamaStreamProxy())
        {
            proxy.Error += error => errors.Add(error);
            proxy.AssistantDelta += delta => deltas.Add(delta);
            proxy.Start($"http://127.0.0.1:{upstreamPort}", proxyPort);

            string body;
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                using var content = new StringContent("{\"model\":\"test\",\"stream\":true,\"messages\":[]}", Encoding.UTF8, "application/json");
                using HttpResponseMessage response = await client.PostAsync($"http://127.0.0.1:{proxyPort}/v1/chat/completions", content);
                body = await response.Content.ReadAsStringAsync();
            }
            catch (Exception error)
            {
                Assert.Fail($"{error}; proxy errors: {string.Join("; ", errors)}");
                return;
            }

            Assert.IsTrue(body.Contains("Hello ", StringComparison.Ordinal));
            Assert.IsTrue(body.Contains("world", StringComparison.Ordinal));
            Assert.IsTrue(body.Contains("[DONE]", StringComparison.Ordinal));
        }

        await serverTask;
        upstream.Stop();
        Assert.AreEqual(0, errors.Count, string.Join("; ", errors));
        CollectionAssert.AreEqual(new[] { "Hello ", "world" }, deltas);
    }

    [TestMethod]
    [Timeout(20000)]
    public async Task Proxy_HandlesConsecutiveTurnsAndCompletesEachOnce()
    {
        int upstreamPort = FreePort();
        int proxyPort = FreePort();
        var upstream = new TcpListener(IPAddress.Loopback, upstreamPort);
        upstream.Start();

        // content and finish_reason in the same event, then [DONE] - twice.
        Task serverTask = Task.Run(async () =>
        {
            for (int turn = 0; turn < 2; turn++)
            {
                using TcpClient client = await upstream.AcceptTcpClientAsync();
                using NetworkStream stream = client.GetStream();
                var reader = new StreamReader(stream, Encoding.ASCII, false, 1, leaveOpen: true);
                int contentLength = 0;
                string? line;
                while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
                {
                    if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    {
                        int.TryParse(line["Content-Length:".Length..].Trim(), out contentLength);
                    }
                }

                if (contentLength > 0)
                {
                    byte[] body = new byte[contentLength];
                    int read = 0;
                    while (read < contentLength)
                    {
                        read += await stream.ReadAsync(body.AsMemory(read, contentLength - read));
                    }
                }

                string sse =
                    "data: {\"choices\":[{\"delta\":{\"role\":\"assistant\"}}]}\r\n\r\n" +
                    $"data: {{\"choices\":[{{\"delta\":{{\"content\":\"Answer {turn + 1}\"}},\"finish_reason\":\"stop\"}}]}}\r\n\r\n" +
                    "data: [DONE]\r\n\r\n";
                byte[] response = Encoding.UTF8.GetBytes(
                    "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nConnection: close\r\n\r\n" + sse);
                await stream.WriteAsync(response);
                await stream.FlushAsync();
            }
        });

        var deltas = new List<string>();
        var errors = new List<string>();
        int completions = 0;
        using (var proxy = new LlamaStreamProxy())
        {
            proxy.Error += error => errors.Add(error);
            proxy.AssistantDelta += delta => deltas.Add(delta);
            proxy.AssistantTurnCompleted += () => completions++;
            proxy.Start($"http://127.0.0.1:{upstreamPort}", proxyPort);

            for (int turn = 0; turn < 2; turn++)
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                using var content = new StringContent(
                    "{\"model\":\"test\",\"stream\":true,\"messages\":[]}", Encoding.UTF8, "application/json");
                using HttpResponseMessage response = await client.PostAsync(
                    $"http://127.0.0.1:{proxyPort}/v1/chat/completions", content);
                Assert.IsTrue((await response.Content.ReadAsStringAsync()).Contains("[DONE]", StringComparison.Ordinal));
            }
        }

        await serverTask;
        upstream.Stop();
        Assert.AreEqual(0, errors.Count, string.Join("; ", errors));
        Assert.AreEqual(2, completions, "one completion per SSE stream");
        CollectionAssert.AreEqual(new[] { "Answer 1", "Answer 2" }, deltas);
    }

    private static byte[] Gzip(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(data);
        }

        return output.ToArray();
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task Proxy_DecompressesGzipWebUiResponse()
    {
        int upstreamPort = FreePort();
        int proxyPort = FreePort();
        var upstream = new TcpListener(IPAddress.Loopback, upstreamPort);
        upstream.Start();

        // llama.cpp rejects requests without Accept-Encoding: gzip with HTTP 415.
        Task serverTask = Task.Run(async () =>
        {
            using TcpClient client = await upstream.AcceptTcpClientAsync();
            using NetworkStream stream = client.GetStream();
            var reader = new StreamReader(stream, Encoding.ASCII, false, 1, leaveOpen: true);
            bool acceptsGzip = false;
            string? line;
            while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
            {
                if (line.StartsWith("Accept-Encoding:", StringComparison.OrdinalIgnoreCase)
                    && line.Contains("gzip", StringComparison.OrdinalIgnoreCase))
                {
                    acceptsGzip = true;
                }
            }

            if (!acceptsGzip)
            {
                byte[] error = Encoding.UTF8.GetBytes(
                    "HTTP/1.1 415 Unsupported Media Type\r\nContent-Type: text/plain\r\nConnection: close\r\n\r\n"
                    + "Error: gzip is not supported by this browser");
                await stream.WriteAsync(error);
                return;
            }

            byte[] compressed = Gzip(Encoding.UTF8.GetBytes("<html><body>llama ui</body></html>"));
            byte[] header = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\n"
                + $"Content-Encoding: gzip\r\nContent-Length: {compressed.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(header);
            await stream.WriteAsync(compressed);
        });

        using (var proxy = new LlamaStreamProxy())
        {
            proxy.Start($"http://127.0.0.1:{upstreamPort}", proxyPort);
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            string body = await client.GetStringAsync($"http://127.0.0.1:{proxyPort}/");
            StringAssert.Contains(body, "llama ui");
        }

        await serverTask;
        upstream.Stop();
    }

    private static X509Certificate2 CreateSelfSignedLoopbackCertificate()
    {
        using RSA rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
        // Windows SChannel cannot use in-memory (ephemeral) private keys for a
        // TLS server, so persist the generated key in the user key store.
        using X509Certificate2 ephemeral = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        return X509CertificateLoader.LoadPkcs12(
            ephemeral.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.PersistKeySet);
    }

    [TestMethod]
    [Timeout(20000)]
    public async Task Proxy_ForwardsHttpsUpstreamWithSelfSignedCertificate()
    {
        int upstreamPort = FreePort();
        int proxyPort = FreePort();
        using X509Certificate2 certificate = CreateSelfSignedLoopbackCertificate();

        var upstream = new TcpListener(IPAddress.Loopback, upstreamPort);
        upstream.Start();

        var serverErrors = new List<string>();
        Task serverTask = Task.Run(async () =>
        {
            try
            {
                using TcpClient client = await upstream.AcceptTcpClientAsync();
                using var ssl = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
                await ssl.AuthenticateAsServerAsync(certificate, false,
                    SslProtocols.Tls12 | SslProtocols.Tls13, false);

                // Buffer size 1 keeps the reader from swallowing body bytes.
                var reader = new StreamReader(ssl, Encoding.ASCII, false, 1, leaveOpen: true);
                int contentLength = 0;
                string? line;
                while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
                {
                    if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    {
                        int.TryParse(line["Content-Length:".Length..].Trim(), out contentLength);
                    }
                }

                if (contentLength > 0)
                {
                    byte[] body = new byte[contentLength];
                    int read = 0;
                    while (read < contentLength)
                    {
                        int count = await ssl.ReadAsync(body.AsMemory(read, contentLength - read));
                        if (count == 0)
                        {
                            break;
                        }

                        read += count;
                    }
                }

                const string sse =
                    "data: {\"choices\":[{\"delta\":{\"role\":\"assistant\"}}]}\r\n\r\n" +
                    "data: {\"choices\":[{\"delta\":{\"content\":\"Hello over TLS\"}}]}\r\n\r\n" +
                    "data: [DONE]\r\n\r\n";
                byte[] sseBytes = Encoding.UTF8.GetBytes(sse);
                byte[] response = Encoding.UTF8.GetBytes(
                    $"HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nContent-Length: {sseBytes.Length}\r\n\r\n");
                await ssl.WriteAsync(response);
                await ssl.WriteAsync(sseBytes);
                await ssl.FlushAsync();
            }
            catch (Exception error)
            {
                serverErrors.Add(error.ToString());
            }
        });

        var deltas = new List<string>();
        var errors = new List<string>();
        using (var proxy = new LlamaStreamProxy())
        {
            proxy.Error += error => errors.Add(error);
            proxy.AssistantDelta += delta => deltas.Add(delta);
            proxy.Start($"https://127.0.0.1:{upstreamPort}", proxyPort);

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var content = new StringContent(
                "{\"model\":\"test\",\"stream\":true,\"messages\":[]}", Encoding.UTF8, "application/json");
            string body;
            try
            {
                using HttpResponseMessage response = await client.PostAsync(
                    $"http://127.0.0.1:{proxyPort}/v1/chat/completions", content);
                body = await response.Content.ReadAsStringAsync();
            }
            catch (Exception error)
            {
                Assert.Fail($"{error}; proxy errors: {string.Join("; ", errors)}; "
                    + $"server errors: {string.Join("; ", serverErrors)}");
                return;
            }

            StringAssert.Contains(body, "Hello over TLS");
        }

        await serverTask;
        upstream.Stop();
        if (certificate.GetRSAPrivateKey() is RSACryptoServiceProvider csp)
        {
            csp.PersistKeyInCsp = false;
        }

        Assert.AreEqual(0, errors.Count, string.Join("; ", errors));
        Assert.AreEqual(0, serverErrors.Count, string.Join("; ", serverErrors));
        CollectionAssert.AreEqual(new[] { "Hello over TLS" }, deltas);
    }
}
