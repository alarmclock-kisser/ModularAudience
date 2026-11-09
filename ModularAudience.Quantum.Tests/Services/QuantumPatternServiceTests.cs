using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Quantum.Abstractions;
using ModularAudience.Quantum.Extensions;
using ModularAudience.Quantum.Models;
using ModularAudience.Quantum.Services;

namespace ModularAudience.Quantum.Tests.Services;

[TestClass]
public sealed class QuantumPatternServiceTests
{
    [TestMethod]
    public async Task DefaultRegistration_GeneratesOfflineBreakbeatsWithCustomGridAndSeed()
    {
        var services = new ServiceCollection().AddLocalQuantumSimulator(options => options.Seed = 42);
        using var provider = services.BuildServiceProvider(validateScopes: true);
        Assert.IsInstanceOfType<LocalMatrixQuantumSimulatorClient>(provider.GetRequiredService<IQuantumClient>());
        var service = provider.GetRequiredService<IQuantumPatternService>();
        var result = await service.GenerateBreakbeatAsync(4, 1024, CancellationToken.None, 32);
        Assert.AreEqual(32, result.Slices.Count);
        Assert.AreEqual(1024, result.RawCounts.Sum(pair => pair.Value));
        StringAssert.Contains(result.CircuitQasm, "h q[0];\ncx q[0], q[1];\nx q[1];");
        StringAssert.Contains(result.CircuitQasm, "cx q[2], q[3];");
        Assert.AreEqual(42, provider.GetRequiredService<IOptions<LocalQuantumSimulatorOptions>>().Value.Seed);
    }

    [TestMethod]
    public async Task ConcurrentGenerations_DoNotShareMutableCircuitState()
    {
        var requests = new ConcurrentBag<QuantumJobRequest>();
        var client = new StubClient((request, _) =>
        {
            requests.Add(request);
            return Task.FromResult(new QuantumJobResponse("test", new Dictionary<string, int>
            { [new string('1', request.QubitCount)] = request.Shots }, TimeSpan.FromMilliseconds(12)));
        });
        var service = new QuantumPatternService(client, new OpenQasmCircuitBuilder(), new BreakbeatPatternMapper());
        var patterns = await Task.WhenAll(service.GenerateBreakbeatAsync(4, 64, CancellationToken.None),
            service.GenerateBreakbeatAsync(16, 128, CancellationToken.None, 5));
        Assert.AreEqual(2, requests.Count);
        StringAssert.Contains(requests.Single(request => request.QubitCount == 16).CircuitQasm, "cx q[14], q[15];");
        Assert.DoesNotContain("q[15]", requests.Single(request => request.QubitCount == 4).CircuitQasm);
        Assert.AreEqual(16, patterns[0].Slices.Count);
        Assert.AreEqual(5, patterns[1].Slices.Count);
        Assert.AreEqual(TimeSpan.FromMilliseconds(12), patterns[1].ExecutionLatency);
    }

    [TestMethod]
    public async Task ValidationAndCancellation_HappenBeforeExecution()
    {
        var client = new StubClient((_, _) => throw new AssertFailedException("The client must not be called."));
        var service = new QuantumPatternService(client, new OpenQasmCircuitBuilder(), new BreakbeatPatternMapper());
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => service.GenerateBreakbeatAsync(3, 10, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => service.GenerateBreakbeatAsync(4, 0, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => service.GenerateBreakbeatAsync(4, 10, CancellationToken.None, 0));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.GenerateBreakbeatAsync(4, 10, cts.Token));
    }

    [TestMethod]
    public async Task CancellationAfterExecution_PreventsMapping()
    {
        using var cts = new CancellationTokenSource();
        var service = new QuantumPatternService(new StubClient((_, _) =>
        {
            cts.Cancel();
            return Task.FromResult(new QuantumJobResponse("test", new Dictionary<string, int>(), TimeSpan.Zero));
        }), new OpenQasmCircuitBuilder(), new BreakbeatPatternMapper());
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.GenerateBreakbeatAsync(4, 10, cts.Token));
    }

    [TestMethod]
    public async Task MismatchedClientResults_AreRejected()
    {
        var service = new QuantumPatternService(new StubClient((_, _) => Task.FromResult(new QuantumJobResponse("test",
            new Dictionary<string, int> { ["00000"] = 1 }, TimeSpan.Zero))), new OpenQasmCircuitBuilder(), new BreakbeatPatternMapper());
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.GenerateBreakbeatAsync(4, 10, CancellationToken.None));
    }

    [TestMethod]
    public void Registration_IsIdempotentPreservesOverridesAndSupportsProviderSwitching()
    {
        var builder = new OpenQasmCircuitBuilder(4, OpenQasmVersion.Version3);
        var mapper = new BreakbeatPatternMapper();
        var services = new ServiceCollection();
        services.AddSingleton<IQuantumCircuitBuilder>(builder);
        services.AddSingleton<IQuantumPatternMapper>(mapper);
        services.AddQuantumPatterns().AddQuantumPatterns();
        Assert.AreEqual(1, services.Count(service => service.ServiceType == typeof(IQuantumPatternService)));
        services.AddIbmQuantum(options => { options.AccessToken = "token"; options.ServiceCrn = "crn"; options.Backend = "ibm_test"; });
        using (var cloud = services.BuildServiceProvider())
        {
            Assert.IsInstanceOfType<IbmQuantumClient>(cloud.GetRequiredService<IQuantumClient>());
            Assert.AreSame(builder, cloud.GetRequiredService<IQuantumCircuitBuilder>());
            Assert.AreSame(mapper, cloud.GetRequiredService<IQuantumPatternMapper>());
        }
        services.AddLocalQuantumSimulator();
        using var local = services.BuildServiceProvider();
        Assert.IsInstanceOfType<LocalMatrixQuantumSimulatorClient>(local.GetRequiredService<IQuantumClient>());
    }

    [TestMethod]
    public void Registrations_RejectNullArguments()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => ServiceCollectionExtensions.AddQuantumPatterns(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => ServiceCollectionExtensions.AddLocalQuantumSimulator(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => new ServiceCollection().AddIbmQuantum(null!));
    }

    private sealed class StubClient(Func<QuantumJobRequest, CancellationToken, Task<QuantumJobResponse>> execute) : IQuantumClient
    {
        public Task<QuantumJobResponse> ExecuteAsync(QuantumJobRequest request, CancellationToken cancellationToken) => execute(request, cancellationToken);
    }
}
