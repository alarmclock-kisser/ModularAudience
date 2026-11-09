using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ModularAudience.Quantum.Abstractions;
using ModularAudience.Quantum.Models;
using ModularAudience.Quantum.Serialization;

namespace ModularAudience.Quantum.Services;

/// <summary>IBM Quantum Compute Service Sampler V2 transport. Job submission is deliberately never retried.</summary>
public sealed class IbmQuantumClient : IQuantumClient, IQuantumBackendClient, IDisposable
{
    public const string HttpClientName = "ModularAudience.Quantum.IBM";
    public const string IamHttpClientName = "ModularAudience.Quantum.IAM";

    private readonly IHttpClientFactory httpClientFactory;
    private readonly IbmQuantumOptions options;
    private readonly TimeProvider timeProvider;
    private readonly SemaphoreSlim tokenLock = new(1, 1);
    private string? cachedToken;
    private DateTimeOffset tokenExpiresAt;

    public IbmQuantumClient(IHttpClientFactory httpClientFactory, IOptions<IbmQuantumOptions> options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        this.httpClientFactory = httpClientFactory;
        this.options = options.Value;
        this.options.Validate();
        this.timeProvider = timeProvider;
    }

    public async Task<QuantumJobResponse> ExecuteAsync(QuantumJobRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        long start = Stopwatch.GetTimestamp();
        using var timeout = new CancellationTokenSource(this.options.JobTimeout, this.timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        CancellationToken token = linked.Token;
        string? jobId = null;

        try
        {
            var submission = new IbmJobSubmission(this.options.Backend, "sampler",
                new IbmSamplerParameters([[request.CircuitQasm]], request.Shots));
            using var submitted = await this.SendJsonAsync(HttpMethod.Post, new Uri(this.options.BaseAddress, "jobs"),
                () => JsonContent.Create(submission, QuantumJsonContext.Default.IbmJobSubmission), true, token).ConfigureAwait(false);
            var job = submitted.RootElement.Deserialize(QuantumJsonContext.Default.IbmJobStatus)
                ?? throw new JsonException("IBM Quantum returned an empty job response.");
            if (string.IsNullOrWhiteSpace(job.Id)) throw new JsonException("IBM Quantum did not return a job ID.");
            jobId = job.Id;
            var jobUri = new Uri(this.options.BaseAddress, "jobs/" + Uri.EscapeDataString(jobId));
            string status = job.State?.Status ?? job.Status ?? "Queued";

            while (true)
            {
                switch (status.ToUpperInvariant())
                {
                    case "COMPLETED":
                    case "DONE":
                        using (var result = await this.SendJsonAsync(HttpMethod.Get, new Uri(jobUri.AbsoluteUri + "/results"),
                            null, true, token).ConfigureAwait(false))
                        {
                            var counts = IbmSamplerResultDecoder.Decode(result.RootElement, request, token);
                            return new(jobId, counts, Stopwatch.GetElapsedTime(start));
                        }
                    case "FAILED":
                    case "ERROR":
                    case "CANCELLED":
                    case "CANCELED":
                        throw new InvalidOperationException($"IBM Quantum job '{jobId}' ended with status '{status}'.");
                    case "QUEUED":
                    case "RUNNING":
                    case "CREATING":
                    case "PENDING":
                    case "VALIDATING":
                        break;
                    default:
                        throw new JsonException($"IBM Quantum returned an unsupported job status '{status}'.");
                }

                await Task.Delay(this.options.PollInterval, this.timeProvider, token).ConfigureAwait(false);
                using var polled = await this.SendJsonAsync(HttpMethod.Get, new Uri(jobUri.AbsoluteUri + "?exclude_params=true"),
                    null, true, token).ConfigureAwait(false);
                var update = polled.RootElement.Deserialize(QuantumJsonContext.Default.IbmJobStatus)
                    ?? throw new JsonException("IBM Quantum returned an empty job status.");
                status = update.State?.Status ?? update.Status
                    ?? throw new JsonException("IBM Quantum did not return a job status.");
            }
        }
        catch (OperationCanceledException ex) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(jobId is null ? "IBM Quantum execution timed out before a job ID was received."
                : $"Waiting for IBM Quantum job '{jobId}' timed out. The remote job may still be running.", ex);
        }
    }

    public async Task<IReadOnlyList<QuantumBackendInfo>> GetBackendsAsync(CancellationToken cancellationToken)
    {
        using var json = await this.ReadBackendJsonAsync("backends", cancellationToken).ConfigureAwait(false);
        if (!json.RootElement.TryGetProperty("devices", out var devices) || devices.ValueKind != JsonValueKind.Array || devices.GetArrayLength() > 2048)
            throw new JsonException("IBM Quantum returned an invalid backend list.");
        var result = new List<QuantumBackendInfo>();
        foreach (var device in devices.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? name = device.GetProperty("name").GetString();
            int qubits = device.GetProperty("qubits").GetInt32();
            if (string.IsNullOrWhiteSpace(name) || qubits is < 1 or > 4096) throw new JsonException("IBM Quantum returned invalid backend metadata.");
            string status = device.TryGetProperty("status", out var state) && state.TryGetProperty("name", out var label)
                ? label.GetString() ?? "unknown" : "unknown";
            result.Add(new(name, status, qubits, device.TryGetProperty("queue_length", out var queue) ? queue.GetInt32() : 0,
                device.TryGetProperty("is_simulator", out var simulator) && simulator.GetBoolean()));
        }
        return result.AsReadOnly();
    }

    public async Task<QuantumBackendConfiguration> GetBackendConfigurationAsync(string backend, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backend);
        using var json = await this.ReadBackendJsonAsync("backends/" + Uri.EscapeDataString(backend) + "/configuration", cancellationToken).ConfigureAwait(false);
        var root = json.RootElement;
        int qubits = root.GetProperty("n_qubits").GetInt32();
        if (qubits is < 1 or > 4096) throw new JsonException("IBM Quantum returned an unsupported backend size.");
        var basis = root.GetProperty("basis_gates").EnumerateArray().Select(value => value.GetString()
            ?? throw new JsonException("A backend basis gate is missing its name.")).ToArray();
        if (basis.Length is < 1 or > 128) throw new JsonException("IBM Quantum returned an invalid basis gate set.");
        var coupling = root.TryGetProperty("coupling_map", out var map) ? ReadQubits(map, qubits, 2) : [];
        var gates = new List<QuantumBackendGate>();
        if (root.TryGetProperty("gates", out var instructions))
            foreach (var gate in instructions.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                string name = gate.GetProperty("name").GetString() ?? throw new JsonException("A backend gate name is missing.");
                var indices = gate.TryGetProperty("coupling_map", out var gateMap) ? ReadQubits(gateMap, qubits) : [];
                gates.Add(new(name, Array.AsReadOnly(indices)));
            }
        int shots = root.TryGetProperty("max_shots", out var limit) ? limit.GetInt32() : 1_000_000;
        if (shots <= 0) throw new JsonException("The backend shot limit is invalid.");
        return new(backend, qubits, Array.AsReadOnly(basis), Array.AsReadOnly(coupling), gates.AsReadOnly(), shots);
    }

    private async Task<JsonDocument> ReadBackendJsonAsync(string path, CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(this.options.JobTimeout, this.timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try { return await this.SendJsonAsync(HttpMethod.Get, new Uri(this.options.BaseAddress, path), null, true, linked.Token).ConfigureAwait(false); }
        catch (OperationCanceledException ex) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        { throw new TimeoutException("IBM Quantum backend discovery timed out. No job was submitted.", ex); }
    }

    private static int[][] ReadQubits(JsonElement element, int qubits, int? arity = null)
    {
        if (element.ValueKind == JsonValueKind.Null) return [];
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() > 65_536) throw new JsonException("Invalid backend coupling map.");
        return element.EnumerateArray().Select(row =>
        {
            int[] indices = row.EnumerateArray().Select(value => value.GetInt32()).ToArray();
            if (indices.Length is < 1 or > 3 || (arity.HasValue && indices.Length != arity.Value) ||
                indices.Any(index => index < 0 || index >= qubits) || indices.Distinct().Count() != indices.Length)
                throw new JsonException("A backend coupling references invalid qubits.");
            return indices;
        }).ToArray();
    }

    private async Task<JsonDocument> SendJsonAsync(HttpMethod method, Uri uri, Func<HttpContent>? content,
        bool authenticate, CancellationToken cancellationToken)
    {
        bool canRetry = method == HttpMethod.Get;
        for (int attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? bearer = authenticate ? await this.GetBearerTokenAsync(cancellationToken).ConfigureAwait(false) : null;
            using var client = this.httpClientFactory.CreateClient(authenticate ? HttpClientName : IamHttpClientName);
            using var message = new HttpRequestMessage(method, uri) { Content = content?.Invoke() };
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (authenticate)
            {
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
                message.Headers.Add("Service-CRN", this.options.ServiceCrn);
                message.Headers.Add("IBM-API-Version", this.options.ApiVersion);
            }
            using var timeout = new CancellationTokenSource(this.options.RequestTimeout, this.timeProvider);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            try
            {
                using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);
                if (canRetry && attempt < this.options.MaxReadRetries && IsTransient(response.StatusCode))
                {
                    TimeSpan delay = response.Headers.RetryAfter?.Delta
                        ?? (response.Headers.RetryAfter?.Date is { } date ? date - this.timeProvider.GetUtcNow() : this.RetryDelay(attempt));
                    // Release the connection before backing off; the total job deadline remains in effect.
                    response.Dispose();
                    await Task.Delay(TimeSpan.FromMilliseconds(Math.Clamp(delay.TotalMilliseconds, 0, 60_000)),
                        this.timeProvider, cancellationToken).ConfigureAwait(false);
                    continue;
                }
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException(FormattableString.Invariant(
                        $"IBM Quantum request failed with HTTP {(int)response.StatusCode} ({response.StatusCode})."), null, response.StatusCode);
                using var stream = await response.Content.ReadAsStreamAsync(linked.Token).ConfigureAwait(false);
                return await JsonDocument.ParseAsync(stream, cancellationToken: linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("The IBM Quantum HTTP request timed out. A submitted job may still exist.", ex);
            }
            catch (HttpRequestException ex) when (canRetry && ex.StatusCode is null && attempt < this.options.MaxReadRetries)
            {
                await Task.Delay(this.RetryDelay(attempt), this.timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private TimeSpan RetryDelay(int attempt) => TimeSpan.FromMilliseconds(Math.Min(60_000,
        this.options.RetryDelay.TotalMilliseconds * (1 << attempt)));

    private static bool IsTransient(HttpStatusCode status) => status is HttpStatusCode.TooManyRequests or
        HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;

    private async Task<string> GetBearerTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(this.options.AccessToken)) return this.options.AccessToken;
        await this.tokenLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (this.cachedToken is not null && this.timeProvider.GetUtcNow() < this.tokenExpiresAt) return this.cachedToken;
            using var json = await this.SendJsonAsync(HttpMethod.Post, this.options.IamTokenEndpoint,
                () => new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "urn:ibm:params:oauth:grant-type:apikey",
                    ["apikey"] = this.options.ApiKey
                }), false, cancellationToken).ConfigureAwait(false);
            var token = json.RootElement.Deserialize(QuantumJsonContext.Default.IbmIamToken);
            if (token is null || string.IsNullOrWhiteSpace(token.AccessToken) || token.ExpiresIn <= 0)
                throw new JsonException("IBM Cloud IAM did not return a usable access token and expiry.");
            this.cachedToken = token.AccessToken;
            this.tokenExpiresAt = this.timeProvider.GetUtcNow().AddSeconds(token.ExpiresIn - Math.Min(60, token.ExpiresIn / 10.0));
            return this.cachedToken;
        }
        finally { this.tokenLock.Release(); }
    }

    public void Dispose() => this.tokenLock.Dispose();
}
