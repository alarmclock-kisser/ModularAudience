using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Quantum.Models;
using ModularAudience.Quantum.Services;

namespace ModularAudience.Quantum.Tests.Services;

[TestClass]
public sealed class OpenQasmCircuitBuilderTests
{
    [TestMethod]
    public void WithSuperposition_EmitsExactHadamardStatement()
    {
        string qasm = new OpenQasmCircuitBuilder(4).WithSuperposition(0).Build();
        CollectionAssert.Contains(qasm.Split('\n'), "h q[0];");
        Assert.AreEqual("OPENQASM 2.0;\ninclude \"qelib1.inc\";\nqreg q[4];\ncreg c[4];\nh q[0];\nmeasure q -> c;\n", qasm);
    }

    [TestMethod]
    public void WithEntanglement_EmitsValidCnotAndAntiPhaseX()
    {
        string qasm = new OpenQasmCircuitBuilder().WithSuperposition(0).WithEntanglement(0, 1).WithBitFlip(1).Build();
        StringAssert.Contains(qasm, "h q[0];\ncx q[0], q[1];\nx q[1];");
        Assert.AreEqual(1, qasm.Split("measure q -> c;").Length - 1);
    }

    [TestMethod]
    public void Version3_UsesStandardGatesAndAssignmentMeasurements()
    {
        string qasm = new OpenQasmCircuitBuilder(16, OpenQasmVersion.Version3).WithRotation(15, 0).Build();
        Assert.AreEqual("OPENQASM 3.0;\ninclude \"stdgates.inc\";\nqubit[16] q;\nbit[16] c;\nry(0) q[15];\nc = measure q;\n", qasm);
    }

    [TestMethod]
    public void WithRotation_UsesInvariantRoundTripRadians()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            string qasm = new OpenQasmCircuitBuilder().WithRotation(2, -1.2345678901234567).Build();
            StringAssert.Contains(qasm, "ry(-1.2345678901234567) q[2];");
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [TestMethod]
    public void Builder_IsImmutableAndCanStartFreshCircuits()
    {
        var original = new OpenQasmCircuitBuilder(4, OpenQasmVersion.Version3);
        var changed = original.WithSuperposition(0);
        Assert.DoesNotContain("h q[0];", original.Build());
        StringAssert.Contains(changed.Build(), "h q[0];");
        var reset = changed.WithQubitCount(8);
        Assert.AreEqual(8, reset.QubitCount);
        StringAssert.Contains(reset.Build(), "OPENQASM 3.0;");
        Assert.DoesNotContain("h q[0];", reset.Build());
        Assert.AreEqual(changed.Build(), changed.Build());
    }

    [TestMethod]
    [DataRow(3)]
    [DataRow(17)]
    public void InvalidQubitCounts_AreRejected(int count) =>
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new OpenQasmCircuitBuilder(count));

    [TestMethod]
    [DataRow(-1)]
    [DataRow(4)]
    public void InvalidIndices_AreRejectedByEveryGate(int index)
    {
        var builder = new OpenQasmCircuitBuilder();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => builder.WithSuperposition(index));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => builder.WithBitFlip(index));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => builder.WithRotation(index, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => builder.WithEntanglement(index, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => builder.WithEntanglement(0, index));
    }

    [TestMethod]
    [DataRow(double.NaN)]
    [DataRow(double.PositiveInfinity)]
    [DataRow(double.NegativeInfinity)]
    public void NonFiniteRotations_AreRejected(double theta) =>
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new OpenQasmCircuitBuilder().WithRotation(0, theta));

    [TestMethod]
    public void InvalidVersionAndSelfEntanglement_AreRejected()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new OpenQasmCircuitBuilder(4, (OpenQasmVersion)100));
        Assert.ThrowsExactly<ArgumentException>(() => new OpenQasmCircuitBuilder().WithEntanglement(1, 1));
    }
}
