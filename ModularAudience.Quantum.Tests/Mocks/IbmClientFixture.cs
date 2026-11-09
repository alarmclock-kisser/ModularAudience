using Microsoft.Extensions.DependencyInjection;
using ModularAudience.Quantum.Abstractions;
using ModularAudience.Quantum.Extensions;
using ModularAudience.Quantum.Models;
using ModularAudience.Quantum.Services;

namespace ModularAudience.Quantum.Tests.Mocks;

internal sealed class IbmClientFixture : IDisposable
{
    private readonly ServiceProvider provider;
    internal MockHttpMessageHandler Handler { get; }
    internal IQuantumClient Client { get; }

    internal IbmClientFixture(Func<RecordedRequest, CancellationToken, Task<HttpResponseMessage>>? respond = null,
        Action<IbmQuantumOptions>? configure = null, TimeProvider? clock = null)
    {
        this.Handler = new(respond ?? ((request, _) => Task.FromResult(DefaultResponse(request))));
        var services = new ServiceCollection();
        services.AddIbmQuantum(options =>
        {
            options.AccessToken = "test-bearer";
            options.Backend = "ibm_test";
            options.ServiceCrn = "crn:test-instance";
            options.PollInterval = TimeSpan.FromMilliseconds(1);
            options.RetryDelay = TimeSpan.Zero;
            options.RequestTimeout = TimeSpan.FromSeconds(2);
            options.JobTimeout = TimeSpan.FromSeconds(10);
            configure?.Invoke(options);
        });
        if (clock is not null) services.AddSingleton(clock);
        services.AddHttpClient(IbmQuantumClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => this.Handler);
        services.AddHttpClient(IbmQuantumClient.IamHttpClientName).ConfigurePrimaryHttpMessageHandler(() => this.Handler);
        this.provider = services.BuildServiceProvider();
        try { this.Client = this.provider.GetRequiredService<IQuantumClient>(); }
        catch { this.provider.Dispose(); throw; }
    }

    internal IbmClientFixture(Func<RecordedRequest, HttpResponseMessage> respond,
        Action<IbmQuantumOptions>? configure = null, TimeProvider? clock = null)
        : this((request, _) => Task.FromResult(respond(request)), configure, clock) { }

    internal static QuantumJobRequest Request(int shots = 1024, int qubits = 4) =>
        new(new OpenQasmCircuitBuilder(qubits).WithSuperposition(0).WithEntanglement(0, 1).Build(), qubits, shots);

    internal static HttpResponseMessage DefaultResponse(RecordedRequest request) => MockHttpMessageHandler.Json(
        request.Uri.Host == "iam.cloud.ibm.com" ? """{"access_token":"iam-bearer","expires_in":3600}""" :
        request.Method == HttpMethod.Post ? """{"id":"job-1"}""" :
        request.Uri.AbsolutePath.EndsWith("/results", StringComparison.Ordinal) ? """{"counts":{"1001":512,"0110":512}}""" :
        """{"state":{"status":"Completed"}}""");

    public void Dispose() => this.provider.Dispose();
}

internal sealed class AdjustableTimeProvider : TimeProvider
{
    private DateTimeOffset now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => this.now;
    internal void Advance(TimeSpan amount) => this.now += amount;
}
