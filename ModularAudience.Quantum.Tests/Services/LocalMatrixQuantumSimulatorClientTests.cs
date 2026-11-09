using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Quantum.Abstractions;
using ModularAudience.Quantum.Models;
using ModularAudience.Quantum.Services;

namespace ModularAudience.Quantum.Tests.Services;

[TestClass]
public sealed class LocalMatrixQuantumSimulatorClientTests
{
    private static IQuantumClient Create(int? seed = 42) => new LocalMatrixQuantumSimulatorClient(Options.Create(new LocalQuantumSimulatorOptions { Seed = seed }));

    [TestMethod]
    [DataRow(OpenQasmVersion.Version2)]
    [DataRow(OpenQasmVersion.Version3)]
    public async Task BellPairAndBitFlip_CreateOnlyAntiCorrelatedStates(OpenQasmVersion version)
    {
        string qasm = new OpenQasmCircuitBuilder(4, version).WithSuperposition(0).WithEntanglement(0, 1).WithBitFlip(1).Build();
        var result = await Create().ExecuteAsync(new(qasm, 4, 1024), CancellationToken.None);
        CollectionAssert.AreEquivalent(new[] { "0001", "0010" }, result.Counts.Keys.ToArray());
        Assert.AreEqual(1024, result.Counts.Sum(pair => pair.Value));
        Assert.IsTrue(result.Counts.Values.All(count => count is > 400 and < 624));
        StringAssert.StartsWith(result.JobId, "local-");
        Assert.IsTrue(result.ExecutionLatency >= TimeSpan.Zero);
    }

    [TestMethod]
    public async Task RyAndXGates_UseCorrectLittleEndianBasisStates()
    {
        string qasm = new OpenQasmCircuitBuilder().WithRotation(3, Math.PI).WithBitFlip(0).Build();
        var result = await Create().ExecuteAsync(new(qasm, 4, 128), CancellationToken.None);
        Assert.AreEqual(128, result.Counts["1001"]);
        Assert.AreEqual(1, result.Counts.Count);
    }

    [TestMethod]
    public async Task RepeatedHadamards_InterfereBackToZero()
    {
        string qasm = new OpenQasmCircuitBuilder().WithSuperposition(0).WithSuperposition(0).Build();
        var result = await Create().ExecuteAsync(new(qasm, 4, 128), CancellationToken.None);
        Assert.AreEqual(128, result.Counts["0000"]);
        Assert.AreEqual(1, result.Counts.Count);
    }

    [TestMethod]
    public async Task RyWeightedRotation_ProducesTheExpectedMeasurementProbability()
    {
        string qasm = new OpenQasmCircuitBuilder().WithRotation(2, Math.PI / 3).Build();
        var result = await Create().ExecuteAsync(new(qasm, 4, 20_000), CancellationToken.None);
        double oneProbability = result.Counts["0100"] / 20_000.0;
        Assert.AreEqual(0.25, oneProbability, 0.02); // sin²(theta / 2)
    }

    [TestMethod]
    public async Task SeededExecutions_AreReproducibleAndSixteenQubitsAreSupported()
    {
        string qasm = new OpenQasmCircuitBuilder(16).WithSuperposition(15).WithRotation(2, Math.PI / 3).WithEntanglement(15, 0).Build();
        IQuantumClient simulator = Create();
        var request = new QuantumJobRequest(qasm, 16, 512);
        var results = await Task.WhenAll(simulator.ExecuteAsync(request, CancellationToken.None), simulator.ExecuteAsync(request, CancellationToken.None));
        CollectionAssert.AreEquivalent(results[0].Counts.ToArray(), results[1].Counts.ToArray());
        Assert.IsTrue(results[0].Counts.Keys.All(bits => bits.Length == 16 && bits[0] == bits[^1]));
        Assert.AreEqual(512, results[0].Counts.Sum(pair => pair.Value));
    }

    [TestMethod]
    public async Task UnseededExecution_WorksThroughTheSameInterface()
    {
        var result = await Create(null).ExecuteAsync(new(new OpenQasmCircuitBuilder().Build(), 4, 1), CancellationToken.None);
        Assert.AreEqual(1, result.Counts["0000"]);
    }

    [TestMethod]
    [DataRow("z q[0];", false)]
    [DataRow("measure q[0] -> c[0];", false)]
    [DataRow("h q[4];", true)]
    [DataRow("h q[9999999999999];", true)]
    [DataRow("h(1) q[0];", true)]
    [DataRow("cx q[0];", true)]
    [DataRow("cx q[0], q[0];", true)]
    [DataRow("cx q[0], q[4];", true)]
    [DataRow("cx q[0], q[9999999999999];", true)]
    [DataRow("ry(NaN) q[0];", true)]
    [DataRow("ry(pi) q[0];", true)]
    public async Task UnsupportedOrMalformedGates_AreRejectedRatherThanIgnored(string gate, bool argumentError)
    {
        string qasm = new OpenQasmCircuitBuilder().Build().Replace("measure q -> c;", gate + "\nmeasure q -> c;", StringComparison.Ordinal);
        if (argumentError)
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => Create().ExecuteAsync(new(qasm, 4, 1), CancellationToken.None));
        else
            await Assert.ThrowsExactlyAsync<NotSupportedException>(() => Create().ExecuteAsync(new(qasm, 4, 1), CancellationToken.None));
    }

    [TestMethod]
    public async Task InvalidRegistersHeadersAndMeasurements_AreRejected()
    {
        string valid = new OpenQasmCircuitBuilder().Build();
        string[] invalid = ["not qasm", valid.Replace("qreg q[4]", "qreg q[5]", StringComparison.Ordinal),
            valid.Replace("OPENQASM 2.0", "OPENQASM 1.0", StringComparison.Ordinal),
            valid.Replace("measure q -> c;", "", StringComparison.Ordinal)];
        foreach (string qasm in invalid)
            await Assert.ThrowsExactlyAsync<NotSupportedException>(() => Create().ExecuteAsync(new(qasm, 4, 1), CancellationToken.None));
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => Create().ExecuteAsync(new(valid, 4, 1, "other"), CancellationToken.None));
    }

    [TestMethod]
    public async Task Cancellation_IsHonoredBeforeAndDuringExecution()
    {
        using var before = new CancellationTokenSource(); before.Cancel();
        var request = new QuantumJobRequest(new OpenQasmCircuitBuilder(16).WithSuperposition(0).Build(), 16, 1_000_000);
        await Assert.ThrowsAsync<OperationCanceledException>(() => Create().ExecuteAsync(request, before.Token));
        using var during = new CancellationTokenSource();
        Task execution = Create().ExecuteAsync(request, during.Token);
        await during.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => execution);
    }
}
