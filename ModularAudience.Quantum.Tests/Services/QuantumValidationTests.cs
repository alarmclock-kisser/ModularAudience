using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Quantum.Models;
using ModularAudience.Quantum.Services;
using ModularAudience.Quantum.Tests.Mocks;

namespace ModularAudience.Quantum.Tests.Services;

[TestClass]
public sealed class QuantumValidationTests
{
    [TestMethod]
    public void InvalidIbmOptions_AreRejectedWithoutNetworkAccess()
    {
        Action<IbmQuantumOptions>[] invalid =
        [
            options => options.BaseAddress = null!,
            options => options.BaseAddress = new Uri("relative", UriKind.Relative),
            options => options.BaseAddress = new Uri("http://quantum.cloud.ibm.com/api/v1/"),
            options => options.BaseAddress = new Uri("https://user:password@quantum.cloud.ibm.com/api/v1/"),
            options => options.BaseAddress = new Uri("https://quantum.cloud.ibm.com/api/v1/?key=value"),
            options => options.BaseAddress = new Uri("https://quantum.cloud.ibm.com/api/v1/#fragment"),
            options => options.BaseAddress = new Uri("https://quantum.cloud.ibm.com/api/v1"),
            options => options.IamTokenEndpoint = new Uri("http://iam.cloud.ibm.com/identity/token"),
            options => { options.ApiKey = ""; options.AccessToken = " "; },
            options => options.Backend = " ", options => options.ServiceCrn = "", options => options.ApiVersion = "yesterday",
            options => options.RequestTimeout = TimeSpan.Zero, options => options.JobTimeout = TimeSpan.FromTicks(-1),
            options => options.PollInterval = TimeSpan.Zero, options => options.RetryDelay = TimeSpan.FromTicks(-1),
            options => options.RequestTimeout = TimeSpan.MaxValue, options => options.MaxReadRetries = -1,
            options => options.MaxReadRetries = 11
        ];
        foreach (var configure in invalid)
            Assert.Throws<ArgumentException>(() => { using var fixture = new IbmClientFixture(configure: configure); });
    }

    [TestMethod]
    public async Task InvalidRequests_AreRejectedByBothClients()
    {
        using var fixture = new IbmClientFixture();
        var simulator = new LocalMatrixQuantumSimulatorClient(Options.Create(new LocalQuantumSimulatorOptions()));
        QuantumJobRequest[] invalid =
        [
            new(null!, 4, 10), new(" ", 4, 10), new("qasm", 3, 10), new("qasm", 17, 10),
            new("qasm", 4, 0), new("qasm", 4, 1_000_001), new("qasm", 4, 1, " ")
        ];
        foreach (QuantumJobRequest request in invalid)
        {
            await Assert.ThrowsAsync<ArgumentException>(() => fixture.Client.ExecuteAsync(request, CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentException>(() => simulator.ExecuteAsync(request, CancellationToken.None));
        }
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => fixture.Client.ExecuteAsync(null!, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => simulator.ExecuteAsync(null!, CancellationToken.None));
        Assert.AreEqual(0, fixture.Handler.Requests.Length);
    }

    [TestMethod]
    public void ClientConstructor_RejectsMissingDependencies()
    {
        using var provider = new ServiceCollection().AddHttpClient().BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        var options = Options.Create(new IbmQuantumOptions());
        Assert.ThrowsExactly<ArgumentNullException>(() => new IbmQuantumClient(null!, options, TimeProvider.System));
        Assert.ThrowsExactly<ArgumentNullException>(() => new IbmQuantumClient(factory, null!, TimeProvider.System));
        Assert.ThrowsExactly<ArgumentNullException>(() => new IbmQuantumClient(factory, options, null!));
    }
}
