using System.Net;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Quantum.Abstractions;
using ModularAudience.Quantum.Tests.Mocks;

namespace ModularAudience.Quantum.Tests.Services;

[TestClass]
public sealed class IbmQuantumBackendTests
{
    [TestMethod]
    public async Task Discovery_UsesReadOnlyAuthenticatedRequestsAndMapsMetadata()
    {
        using var fixture = new IbmClientFixture(_ => MockHttpMessageHandler.Json("""
            {"devices":[{"name":"ibm_test","qubits":127,"status":{"name":"online"},"queue_length":4,"is_simulator":false},
            {"name":"local_test","qubits":5}]}
            """));
        var result = await ((IQuantumBackendClient)fixture.Client).GetBackendsAsync(CancellationToken.None);
        Assert.AreEqual(2, result.Count);
        Assert.AreEqual("online", result[0].Status); Assert.AreEqual(127, result[0].Qubits); Assert.AreEqual(4, result[0].QueueLength);
        Assert.AreEqual("unknown", result[1].Status); Assert.AreEqual(0, result[1].QueueLength);
        Assert.IsTrue(fixture.Handler.Requests.All(request => request.Method == HttpMethod.Get && request.Authorization == "Bearer test-bearer"));
        Assert.AreEqual("/api/v1/backends", fixture.Handler.Requests.Single().Uri.AbsolutePath);
    }

    [TestMethod]
    public async Task Configuration_ReadsNativeGatesDirectedConnectivityAndShotLimit()
    {
        using var fixture = new IbmClientFixture(_ => MockHttpMessageHandler.Json("""
            {"n_qubits":6,"basis_gates":["rz","sx","x","ecr"],"coupling_map":[[0,1],[1,2]],"max_shots":8192,
             "gates":[{"name":"rz","coupling_map":[[0],[1],[2]]},{"name":"ecr","coupling_map":[[0,1]]},{"name":"reset","coupling_map":null}]}
            """));
        var result = await ((IQuantumBackendClient)fixture.Client).GetBackendConfigurationAsync("ibm_test", CancellationToken.None);
        Assert.AreEqual(6, result.QubitCount); Assert.AreEqual(8192, result.MaximumShots);
        CollectionAssert.AreEqual(new[] { "rz", "sx", "x", "ecr" }, result.BasisGates.ToArray());
        CollectionAssert.AreEqual(new[] { 0, 1 }, result.CouplingMap[0]);
        Assert.AreEqual(3, result.Gates.Count); Assert.AreEqual(0, result.Gates[2].Qubits.Count);
        Assert.AreEqual("/api/v1/backends/ibm_test/configuration", fixture.Handler.Requests.Single().Uri.AbsolutePath);
    }

    [TestMethod]
    public async Task MinimalConfiguration_UsesConservativeDefaults()
    {
        using var fixture = new IbmClientFixture(_ => MockHttpMessageHandler.Json("""{"n_qubits":4,"basis_gates":["x"]}"""));
        var result = await ((IQuantumBackendClient)fixture.Client).GetBackendConfigurationAsync("ibm_test", CancellationToken.None);
        Assert.AreEqual(0, result.CouplingMap.Count); Assert.AreEqual(0, result.Gates.Count); Assert.AreEqual(1_000_000, result.MaximumShots);
    }

    [TestMethod]
    [DataRow("{\"devices\":{}}")]
    [DataRow("{\"devices\":[{\"name\":\"\",\"qubits\":4}]}")]
    [DataRow("{\"devices\":[{\"name\":\"ibm_test\",\"qubits\":0}]}")]
    public async Task InvalidDiscoveryMetadata_IsRejected(string json)
    {
        using var fixture = new IbmClientFixture(_ => MockHttpMessageHandler.Json(json));
        await Assert.ThrowsExactlyAsync<JsonException>(() => ((IQuantumBackendClient)fixture.Client).GetBackendsAsync(CancellationToken.None));
    }

    [TestMethod]
    [DataRow("{\"n_qubits\":0,\"basis_gates\":[\"x\"]}")]
    [DataRow("{\"n_qubits\":4,\"basis_gates\":[]}")]
    [DataRow("{\"n_qubits\":4,\"basis_gates\":[null]}")]
    [DataRow("{\"n_qubits\":4,\"basis_gates\":[\"x\"],\"coupling_map\":{}}")]
    [DataRow("{\"n_qubits\":4,\"basis_gates\":[\"x\"],\"coupling_map\":[[0,0]]}")]
    [DataRow("{\"n_qubits\":4,\"basis_gates\":[\"x\"],\"coupling_map\":[[0,4]]}")]
    [DataRow("{\"n_qubits\":4,\"basis_gates\":[\"x\"],\"max_shots\":0}")]
    public async Task InvalidNativeTargets_AreRejected(string json)
    {
        using var fixture = new IbmClientFixture(_ => MockHttpMessageHandler.Json(json));
        await Assert.ThrowsExactlyAsync<JsonException>(() => ((IQuantumBackendClient)fixture.Client).GetBackendConfigurationAsync("ibm_test", CancellationToken.None));
    }

    [TestMethod]
    public async Task AuthenticationFailuresAndDiscoveryTimeout_DoNotSubmitJobs()
    {
        using (var unauthorized = new IbmClientFixture(_ => MockHttpMessageHandler.Json("{}", HttpStatusCode.Unauthorized)))
            await Assert.ThrowsExactlyAsync<HttpRequestException>(() => ((IQuantumBackendClient)unauthorized.Client).GetBackendsAsync(CancellationToken.None));
        using var timeout = new IbmClientFixture(async (_, token) =>
        { await Task.Delay(Timeout.InfiniteTimeSpan, token); return MockHttpMessageHandler.Json("{}"); }, options => options.JobTimeout = TimeSpan.FromMilliseconds(30));
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => ((IQuantumBackendClient)timeout.Client).GetBackendsAsync(CancellationToken.None));
        Assert.IsTrue(timeout.Handler.Requests.All(request => request.Method == HttpMethod.Get));
    }
}
