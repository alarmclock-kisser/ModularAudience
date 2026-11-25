using System.Net;
using System.Net.Sockets;
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

        Assert.AreEqual("http://127.0.0.1:8080", settings.UpstreamUrl);
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
}
