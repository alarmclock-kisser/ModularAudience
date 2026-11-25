using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace ModularAudience.Llama;

/// <summary>Parsed assistant content of one SSE event.</summary>
public sealed record AssistantSseEvent(bool IsDone, bool Started, string? Content, bool Finished);

/// <summary>
/// Small loopback-only HTTP proxy that forwards every request to llama-server
/// unchanged, but taps the assistant text of chat completions and publishes it
/// as deltas. This lets llama.cpp's web UI keep chatting normally while
/// ModularAudience speaks the answers.
/// </summary>
public sealed class LlamaStreamProxy : IDisposable
{
    // The upstream may be an HTTPS llama-server with a self-signed loopback certificate.
    private readonly HttpClient http = LlamaHttp.CreateClient(TimeSpan.FromMinutes(10));

    private TcpListener? listener;
    private TcpListener? listenerIPv6;
    private CancellationTokenSource? shutdown;
    private Task[] acceptLoops = [];
    private string upstream = "http://127.0.0.1:8080";
    private string modelFilter = "";
    private bool disposed;

    public bool IsRunning => shutdown is { IsCancellationRequested: false } && listener != null;

    public string ListenUrl => $"http://127.0.0.1:{(listener?.LocalEndpoint as IPEndPoint)?.Port ?? 0}/";

    /// <summary>One incremental piece of assistant text.</summary>
    public event Action<string>? AssistantDelta;

    public event Action? AssistantTurnStarted;

    public event Action? AssistantTurnCompleted;

    public event Action<string>? Error;

    /// <summary>Informational messages about requests the proxy saw but could not tap.</summary>
    public event Action<string>? Diagnostic;

    public void Start(string upstreamUrl, int port, string modelFilter = "")
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (IsRunning)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(upstreamUrl)
            || !Uri.TryCreate(upstreamUrl, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Enter a valid http:// or https:// llama-server address.", nameof(upstreamUrl));
        }

        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "Port must be between 1 and 65535.");
        }

        upstream = upstreamUrl.TrimEnd('/');
        this.modelFilter = modelFilter?.Trim() ?? "";

        listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        try
        {
            listenerIPv6 = new TcpListener(IPAddress.IPv6Loopback, port);
            listenerIPv6.Start();
        }
        catch (SocketException)
        {
            // IPv6 is optional; IPv4 loopback is enough when it is disabled.
            listenerIPv6 = null;
        }

        shutdown = new CancellationTokenSource();
        CancellationToken token = shutdown.Token;
        acceptLoops = new[] { listener, listenerIPv6 }
            .Where(item => item != null)
            .Select(item => Task.Run(() => AcceptLoopAsync(item!, token), CancellationToken.None))
            .ToArray();
    }

    private async Task AcceptLoopAsync(TcpListener tcpListener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await tcpListener.AcceptTcpClientAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException error)
            {
                if (!token.IsCancellationRequested)
                {
                    Error?.Invoke(error.Message);
                }

                break;
            }

            _ = Task.Run(() => HandleClientAsync(client, token), CancellationToken.None);
        }
    }

    public void Stop()
    {
        shutdown?.Cancel();
        try
        {
            listener?.Stop();
            listenerIPv6?.Stop();
        }
        catch (SocketException)
        {
            // Already stopped.
        }

        foreach (Task loop in acceptLoops)
        {
            try
            {
                loop.Wait(TimeSpan.FromSeconds(1));
            }
            catch (AggregateException)
            {
                // Shutdown races are expected.
            }
        }

        listener = null;
        listenerIPv6 = null;
        acceptLoops = [];
        shutdown?.Dispose();
        shutdown = null;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Stop();
        http.Dispose();
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken serviceToken)
    {
        using (client)
        using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(serviceToken))
        {
            CancellationToken token = linked.Token;
            try
            {
                client.NoDelay = true;
                NetworkStream stream = client.GetStream();
                var reader = new HttpLineReader(stream);
                string? requestLine = await reader.ReadLineAsync(token).ConfigureAwait(false);
                if (string.IsNullOrEmpty(requestLine))
                {
                    return;
                }

                string[] parts = requestLine.Split(' ', 3);
                if (parts.Length < 2)
                {
                    return;
                }

                string method = parts[0];
                string target = parts[1];
                if (!IsValidMethod(method))
                {
                    Diagnostic?.Invoke($"Ignored malformed request line: {Escape(requestLine)}");
                    return;
                }

                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string? headerLine;
                while (!string.IsNullOrEmpty(headerLine = await reader.ReadLineAsync(token).ConfigureAwait(false)))
                {
                    int colon = headerLine.IndexOf(':');
                    if (colon > 0)
                    {
                        headers[headerLine[..colon].Trim()] = headerLine[(colon + 1)..].Trim();
                    }
                }

                byte[] body = [];
                if (headers.TryGetValue("Content-Length", out string? lengthText)
                    && int.TryParse(lengthText, out int length) && length is > 0 and < 200_000_000)
                {
                    body = new byte[length];
                    await reader.ReadExactlyAsync(body, length, token).ConfigureAwait(false);
                }

                if (IsChatPath(method, target))
                {
                    Diagnostic?.Invoke(ModelMatches(body, modelFilter)
                        ? $"Chat request {target} seen; tapping the assistant stream."
                        : $"Chat request {target} was forwarded without speaking: model filter '{modelFilter}' "
                            + "does not match the requested model. Disable the model filter or pick the model "
                            + "reported by 'Test connection'.");
                }

                string? origin = headers.GetValueOrDefault("Origin");
                string? requestedHeaders = headers.GetValueOrDefault("Access-Control-Request-Headers");
                if (method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteHeadersAsync(stream, "204 No Content", "text/plain", origin, null, token, requestedHeaders).ConfigureAwait(false);
                    return;
                }

                Uri uri = new(new Uri(upstream + "/"), target.TrimStart('/'));
                using var request = new HttpRequestMessage(new HttpMethod(method), uri);

                // llama.cpp serves its static web UI only gzip-compressed and rejects
                // requests without Accept-Encoding with HTTP 415. Ask for gzip on
                // bodyless UI requests; the response is decompressed below.
                if (body.Length == 0
                    && (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
                        || method.Equals("HEAD", StringComparison.OrdinalIgnoreCase)))
                {
                    request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip");
                }

                foreach (KeyValuePair<string, string> header in headers)
                {
                    if (!ShouldSkipRequestHeader(header.Key))
                    {
                        request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }
                }

                if (body.Length > 0)
                {
                    request.Content = new ByteArrayContent(body);
                    if (headers.TryGetValue("Content-Type", out string? contentType))
                    {
                        request.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
                    }
                }

                using HttpResponseMessage response = await http.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);

                string responseContentType = response.Content.Headers.ContentType?.ToString()
                    ?? "application/json; charset=utf-8";
                string status = $"{(int)response.StatusCode} {response.ReasonPhrase}";

                // llama.cpp compresses its static web UI. Decompress here so the
                // browser receives plain bytes (we never forward Content-Encoding).
                using Stream upstreamStream = await OpenUpstreamStreamAsync(response, token).ConfigureAwait(false);
                if (IsChatRequest(method, target, body))
                {
                    bool streaming = WantsStreaming(body) || responseContentType.Contains("event-stream", StringComparison.OrdinalIgnoreCase);
                    if (streaming)
                    {
                        await PumpSseAsync(upstreamStream, stream, status, responseContentType, origin, requestedHeaders, token).ConfigureAwait(false);
                    }
                    else
                    {
                        await PumpJsonAsync(upstreamStream, stream, status, responseContentType, origin, requestedHeaders, token).ConfigureAwait(false);
                    }
                }
                else
                {
                    await WriteHeadersAsync(stream, status, responseContentType, origin, null, token, requestedHeaders).ConfigureAwait(false);
                    await upstreamStream.CopyToAsync(stream, 81920, token).ConfigureAwait(false);
                }

                await stream.FlushAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Shutdown or client disconnected.
            }
            catch (IOException)
            {
                // Browser closed the connection.
            }
            catch (Exception error)
            {
                Error?.Invoke(LlamaHttp.DescribeError(error));
            }
        }
    }

    private static bool IsValidMethod(string method)
    {
        if (method.Length == 0)
        {
            return false;
        }

        foreach (char character in method)
        {
            if (!char.IsAsciiLetterOrDigit(character)
                && character is not ('!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '.'
                    or '^' or '_' or '`' or '|' or '~' or '-'))
            {
                return false;
            }
        }

        return true;
    }

    private static string Escape(string value) =>
        value.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);

    private static bool IsChatPath(string method, string target)
    {
        if (!method.Equals("POST", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string path = target.Split('?', 2)[0];
        return path.EndsWith("/v1/chat/completions", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/v1/completions", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/completion", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsChatRequest(string method, string target, byte[] body) =>
        IsChatPath(method, target) && ModelMatches(body, modelFilter);

    /// <summary>True when the request body asks for a streamed completion (or no model filter excludes it).</summary>
    public static bool ModelMatches(byte[] body, string modelFilter)
    {
        if (string.IsNullOrWhiteSpace(modelFilter) || body.Length == 0)
        {
            return true;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("model", out JsonElement model)
                && model.ValueKind == JsonValueKind.String)
            {
                return ModelNamesMatch(model.GetString() ?? "", modelFilter);
            }

            return true;
        }
        catch (JsonException)
        {
            return true;
        }
    }

    /// <summary>
    /// llama-server often reports the model id as a full GGUF path while the web UI
    /// sends only the file name (or a short alias), so compare both directions and
    /// the file names as well.
    /// </summary>
    public static bool ModelNamesMatch(string requestedModel, string filter)
    {
        requestedModel = requestedModel.Trim();
        filter = filter.Trim();
        if (requestedModel.Length == 0 || filter.Length == 0)
        {
            return true;
        }

        if (requestedModel.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || filter.Contains(requestedModel, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string requestedFile = FileName(requestedModel);
        string filterFile = FileName(filter);
        return requestedFile.Length > 0
            && (requestedFile.Equals(filterFile, StringComparison.OrdinalIgnoreCase)
                || requestedFile.Contains(filterFile, StringComparison.OrdinalIgnoreCase)
                || filterFile.Contains(requestedFile, StringComparison.OrdinalIgnoreCase));
    }

    private static string FileName(string value)
    {
        int separator = value.LastIndexOfAny(['/', '\\']);
        return separator < 0 ? value : value[(separator + 1)..];
    }

    public static bool WantsStreaming(byte[] body)
    {
        if (body.Length == 0)
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("stream", out JsonElement stream)
                && (stream.ValueKind == JsonValueKind.True
                    || stream.ValueKind == JsonValueKind.String
                        && string.Equals(stream.GetString(), "true", StringComparison.OrdinalIgnoreCase));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task PumpSseAsync(
        Stream upstream, Stream downstream, string status, string contentType,
        string? origin, string? requestedHeaders, CancellationToken token)
    {
        await WriteHeadersAsync(downstream, status, contentType, origin, null, token, requestedHeaders).ConfigureAwait(false);
        var reader = new HttpLineReader(upstream);
        var data = new StringBuilder();
        bool turnStarted = false;

        void EnsureStarted()
        {
            if (!turnStarted)
            {
                turnStarted = true;
                AssistantTurnStarted?.Invoke();
            }
        }

        void CompleteTurn()
        {
            if (!turnStarted)
            {
                return;
            }

            turnStarted = false;
            AssistantTurnCompleted?.Invoke();
        }

        string? line;
        while ((line = await reader.ReadLineAsync(token).ConfigureAwait(false)) != null)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(line + "\n");
            await downstream.WriteAsync(bytes, token).ConfigureAwait(false);

            if (line.Length == 0)
            {
                if (data.Length > 0)
                {
                    string payload = data.ToString();
                    data.Clear();
                    await downstream.FlushAsync(token).ConfigureAwait(false);
                    AssistantSseEvent parsed = ParseAssistantEvent(payload);
                    if (parsed.Started)
                    {
                        EnsureStarted();
                    }

                    if (!string.IsNullOrEmpty(parsed.Content))
                    {
                        EnsureStarted();
                        AssistantDelta?.Invoke(parsed.Content!);
                    }

                    if (parsed.Finished || parsed.IsDone)
                    {
                        CompleteTurn();
                    }

                    if (parsed.IsDone)
                    {
                        break;
                    }
                }
                else
                {
                    await downstream.FlushAsync(token).ConfigureAwait(false);
                }

                continue;
            }

            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (data.Length > 0)
                {
                    data.Append('\n');
                }

                data.Append(line["data:".Length..].TrimStart());
            }
        }

        if (data.Length > 0)
        {
            // Some servers omit the trailing blank line of the last SSE event.
            AssistantSseEvent remaining = ParseAssistantEvent(data.ToString());
            if (!string.IsNullOrEmpty(remaining.Content))
            {
                EnsureStarted();
                AssistantDelta?.Invoke(remaining.Content!);
            }

            if (remaining.Finished || remaining.IsDone)
            {
                CompleteTurn();
            }
        }

        CompleteTurn();
        await downstream.FlushAsync(token).ConfigureAwait(false);
    }

    /// <summary>
    /// Parses one SSE payload. Pure function so the stream tap can be tested
    /// without opening a socket.
    /// </summary>
    public static AssistantSseEvent ParseAssistantEvent(string payload)
    {
        string trimmed = payload.Trim();
        if (trimmed.Length == 0)
        {
            return new AssistantSseEvent(false, false, null, false);
        }

        if (string.Equals(trimmed, "[DONE]", StringComparison.Ordinal))
        {
            return new AssistantSseEvent(true, false, null, false);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(trimmed);
            JsonElement root = document.RootElement;
            bool started = false;
            string? content = null;
            bool finished = false;

            if (root.TryGetProperty("choices", out JsonElement choices) && choices.GetArrayLength() > 0)
            {
                JsonElement choice = choices[0];
                if (choice.TryGetProperty("delta", out JsonElement delta))
                {
                    if (delta.TryGetProperty("role", out JsonElement role)
                        && string.Equals(role.GetString(), "assistant", StringComparison.OrdinalIgnoreCase))
                    {
                        started = true;
                    }

                    if (delta.TryGetProperty("content", out JsonElement deltaContent)
                        && deltaContent.ValueKind == JsonValueKind.String)
                    {
                        content = deltaContent.GetString();
                    }
                }

                if (choice.TryGetProperty("finish_reason", out JsonElement finish)
                    && finish.ValueKind != JsonValueKind.Null)
                {
                    finished = true;
                }
            }
            else if (root.TryGetProperty("content", out JsonElement legacyContent)
                && legacyContent.ValueKind == JsonValueKind.String)
            {
                content = legacyContent.GetString();
                started = !string.IsNullOrEmpty(content);
            }

            return new AssistantSseEvent(false, started, content, finished);
        }
        catch (JsonException)
        {
            return new AssistantSseEvent(false, false, null, false);
        }
    }

    private static async Task<Stream> OpenUpstreamStreamAsync(HttpResponseMessage response, CancellationToken token)
    {
        Stream stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        if (response.Content.Headers.ContentEncoding.Any(value =>
                value.Equals("gzip", StringComparison.OrdinalIgnoreCase)))
        {
            return new GZipStream(stream, CompressionMode.Decompress);
        }

        if (response.Content.Headers.ContentEncoding.Any(value =>
                value.Equals("deflate", StringComparison.OrdinalIgnoreCase)))
        {
            return new DeflateStream(stream, CompressionMode.Decompress);
        }

        return stream;
    }

    private async Task PumpJsonAsync(
        Stream upstream, Stream downstream, string status, string contentType,
        string? origin, string? requestedHeaders, CancellationToken token)
    {
        using var memory = new MemoryStream();
        await upstream.CopyToAsync(memory, 81920, token).ConfigureAwait(false);
        byte[] bytes = memory.ToArray();

        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes);
            JsonElement choices = document.RootElement.GetProperty("choices");
            if (choices.GetArrayLength() > 0)
            {
                JsonElement message = choices[0].GetProperty("message");
                if (message.TryGetProperty("content", out JsonElement content)
                    && content.ValueKind == JsonValueKind.String)
                {
                    string? text = content.GetString();
                    if (!string.IsNullOrEmpty(text))
                    {
                        AssistantTurnStarted?.Invoke();
                        AssistantDelta?.Invoke(text);
                        AssistantTurnCompleted?.Invoke();
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Non-JSON response: forward untouched.
        }

        await WriteHeadersAsync(downstream, status, contentType, origin, null, token, requestedHeaders).ConfigureAwait(false);
        await downstream.WriteAsync(bytes, token).ConfigureAwait(false);
    }

    private static bool ShouldSkipRequestHeader(string name) =>
        name is "Host" or "Content-Length" or "Transfer-Encoding" or "Connection"
            or "Keep-Alive" or "Origin" or "Referer" or "Accept-Encoding" or "Proxy-Connection";

    private static async Task WriteHeadersAsync(
        Stream stream, string status, string contentType, string? origin,
        IReadOnlyDictionary<string, string>? extra, CancellationToken token,
        string? requestedHeaders = null)
    {
        var headers = new StringBuilder();
        headers.Append("HTTP/1.1 ").Append(status).Append("\r\n");
        headers.Append("Content-Type: ").Append(contentType).Append("\r\n");
        headers.Append("Access-Control-Allow-Origin: ")
            .Append(string.IsNullOrEmpty(origin) ? "*" : origin).Append("\r\n");
        headers.Append("Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n");
        headers.Append("Access-Control-Allow-Headers: ")
            .Append(string.IsNullOrWhiteSpace(requestedHeaders) ? "*" : requestedHeaders).Append("\r\n");
        headers.Append("Access-Control-Max-Age: 86400\r\n");
        headers.Append("Cache-Control: no-cache\r\n");
        headers.Append("Connection: close\r\n");
        if (extra != null)
        {
            foreach (KeyValuePair<string, string> header in extra)
            {
                headers.Append(header.Key).Append(": ").Append(header.Value).Append("\r\n");
            }
        }

        headers.Append("\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(headers.ToString()), token).ConfigureAwait(false);
    }

    /// <summary>
    /// Minimal buffered line reader that also allows exact body reads from the
    /// same stream without losing already-buffered bytes.
    /// </summary>
    private sealed class HttpLineReader(Stream stream)
    {
        private readonly byte[] buffer = new byte[8192];
        private int position;
        private int length;

        public async ValueTask<int> ReadByteAsync(CancellationToken token)
        {
            if (position >= length)
            {
                length = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
                position = 0;
                if (length <= 0)
                {
                    return -1;
                }
            }

            return buffer[position++];
        }

        public async ValueTask<string?> ReadLineAsync(CancellationToken token)
        {
            MemoryStream? bytes = null;
            try
            {
                while (true)
                {
                    int value = await ReadByteAsync(token).ConfigureAwait(false);
                    if (value < 0)
                    {
                        return bytes is null ? null : Decode(bytes);
                    }

                    if (value == '\n')
                    {
                        return Decode(bytes);
                    }

                    if (value == '\r')
                    {
                        continue;
                    }

                    (bytes ??= new MemoryStream()).WriteByte((byte)value);
                }
            }
            finally
            {
                bytes?.Dispose();
            }
        }

        public async ValueTask ReadExactlyAsync(byte[] destination, int count, CancellationToken token)
        {
            int offset = 0;
            while (offset < count)
            {
                if (position < length)
                {
                    int available = Math.Min(length - position, count - offset);
                    Buffer.BlockCopy(buffer, position, destination, offset, available);
                    position += available;
                    offset += available;
                    continue;
                }

                int read = await stream.ReadAsync(destination.AsMemory(offset, count - offset), token).ConfigureAwait(false);
                if (read <= 0)
                {
                    throw new EndOfStreamException("Request body ended early.");
                }

                offset += read;
            }
        }

        private static string Decode(MemoryStream? bytes) =>
            bytes is null ? string.Empty : Encoding.UTF8.GetString(bytes.ToArray());
    }
}
