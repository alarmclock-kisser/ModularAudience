using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModularAudience.Quantum.Abstractions;
using ModularAudience.Quantum.Extensions;
using ModularAudience.Quantum.Models;
using ModularAudience.Quantum.Services;

namespace ModularAudience.Forms.Quantum;

internal sealed class QuantumBeatSession(IQuantumCircuitTranspiler? transpiler = null) : IQuantumBeatSession
{
    private readonly IQuantumCircuitTranspiler transpiler = transpiler ?? new QiskitCircuitTranspiler();
    private ServiceProvider? services;
    private IQuantumClient? cloudClient;
    private IQuantumBackendClient? backendClient;
    private string configuredBackend = "";
    public bool IsConnected => this.cloudClient is not null;
    public string ConnectedBackend => this.configuredBackend;

    public async Task<IReadOnlyList<QuantumBackendInfo>> ConnectAsync(IbmQuantumOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.Disconnect();
        var registrations = new ServiceCollection();
        registrations.AddIbmQuantum(target =>
        {
            target.ApiKey = options.ApiKey; target.AccessToken = options.AccessToken; target.ServiceCrn = options.ServiceCrn;
            target.BaseAddress = options.BaseAddress; target.IamTokenEndpoint = options.IamTokenEndpoint;
            target.Backend = string.IsNullOrWhiteSpace(options.Backend) ? "discovery" : options.Backend;
            target.ApiVersion = options.ApiVersion; target.JobTimeout = options.JobTimeout;
            target.RequestTimeout = options.RequestTimeout; target.PollInterval = options.PollInterval;
            target.RetryDelay = options.RetryDelay; target.MaxReadRetries = options.MaxReadRetries;
        });
        var provider = registrations.BuildServiceProvider();
        try
        {
            var client = provider.GetRequiredService<IQuantumClient>();
            var discovery = (IQuantumBackendClient)client;
            var backends = await discovery.GetBackendsAsync(cancellationToken).ConfigureAwait(false);
            string selected = string.IsNullOrWhiteSpace(options.Backend)
                ? (backends.FirstOrDefault(backend => backend.Status.Equals("online", StringComparison.OrdinalIgnoreCase)) ?? backends.FirstOrDefault())?.Name ?? ""
                : options.Backend;
            provider.GetRequiredService<IOptions<IbmQuantumOptions>>().Value.Backend = selected;
            this.services = provider; this.cloudClient = client; this.backendClient = discovery;
            this.configuredBackend = selected;
            return backends;
        }
        catch { provider.Dispose(); throw; }
    }

    public async Task<QuantumExecutionResult> ExecuteLocalAsync(QuantumCircuitDefinition circuit, int shots, int? seed, CancellationToken cancellationToken)
    {
        string qasm = QuantumCircuitComposer.Build(circuit);
        var simulator = new LocalMatrixQuantumSimulatorClient(Options.Create(new LocalQuantumSimulatorOptions { Seed = seed }));
        var job = await simulator.ExecuteAsync(new(qasm, circuit.QubitCount, shots), cancellationToken).ConfigureAwait(false);
        return new(job, qasm, TimeSpan.Zero);
    }

    public async Task<QuantumExecutionResult> ExecuteIbmAsync(QuantumCircuitDefinition circuit, int shots, string backend,
        string pythonExecutable, int optimizationLevel, int seed, string? preparedIsaQasm, CancellationToken cancellationToken)
    {
        if (this.cloudClient is null || this.backendClient is null) throw new InvalidOperationException("Connect to IBM Quantum before submitting a hardware job.");
        if (!string.Equals(backend, this.configuredBackend, StringComparison.Ordinal))
            throw new InvalidOperationException("Reconnect after selecting or changing the IBM backend.");
        var target = await this.backendClient.GetBackendConfigurationAsync(backend, cancellationToken).ConfigureAwait(false);
        if (circuit.QubitCount > target.QubitCount || shots > target.MaximumShots)
            throw new ArgumentException("The circuit width or shot count exceeds the selected backend's limits.");
        string qasm;
        TimeSpan compilation = TimeSpan.Zero;
        if (!string.IsNullOrWhiteSpace(preparedIsaQasm)) qasm = preparedIsaQasm;
        else
        {
            long start = Stopwatch.GetTimestamp();
            // Always feed the unambiguous QASM 2 subset to Qiskit; output is backend-specific QASM 3.
            string logical = QuantumCircuitComposer.Build(circuit with { Version = OpenQasmVersion.Version2 });
            qasm = await this.transpiler.TranspileAsync(logical, circuit.QubitCount, target, pythonExecutable, optimizationLevel, seed, cancellationToken).ConfigureAwait(false);
            compilation = Stopwatch.GetElapsedTime(start);
        }
        var job = await this.cloudClient.ExecuteAsync(new(qasm, circuit.QubitCount, shots), cancellationToken).ConfigureAwait(false);
        return new(job, qasm, compilation);
    }

    public void Disconnect()
    {
        this.cloudClient = null; this.backendClient = null; this.configuredBackend = "";
        this.services?.Dispose(); this.services = null;
    }
    public void Dispose() => this.Disconnect();
}
