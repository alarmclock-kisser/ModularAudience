using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Quantum.Models;
using ModularAudience.Quantum.Services;

namespace ModularAudience.Quantum.Tests.Services;

[TestClass]
public sealed class BreakbeatPatternMapperTests
{
    private readonly BreakbeatPatternMapper mapper = new();

    [TestMethod]
    public void KnownCounts_ProduceExactDeterministicSixteenStepPattern()
    {
        var counts = new Dictionary<string, int> { ["1001"] = 512, ["0110"] = 512 };
        TimeSpan latency = TimeSpan.FromMilliseconds(42);
        var result = this.mapper.Map("circuit", counts, latency);
        bool[] triggers = [false, true, true, false, false, false, true, true, true, false, false, true, true, true, false, false];
        int[] states = [6, 6, 9, 9, 6, 9, 9, 6, 9, 9, 6, 6, 9, 6, 6, 9];
        DrumSliceTrigger[] expected = Enumerable.Range(0, 16).Select(step => new DrumSliceTrigger(step, triggers[step],
            triggers[step] ? 0.5 : 0, states[step] == 6 ? -200 : 0, 1.5, 50)).ToArray();
        CollectionAssert.AreEqual(expected, result.Slices.ToArray());
        Assert.AreEqual("circuit", result.CircuitQasm);
        Assert.AreEqual(latency, result.ExecutionLatency);
        CollectionAssert.AreEqual(expected, this.mapper.Map("circuit", counts, latency).Slices.ToArray());
    }

    [TestMethod]
    public void DictionaryInsertionOrder_DoesNotChangePattern()
    {
        var first = this.mapper.Map("qasm", new Dictionary<string, int> { ["1001"] = 256, ["0110"] = 768 }, TimeSpan.Zero);
        var second = this.mapper.Map("qasm", new Dictionary<string, int> { ["0110"] = 768, ["1001"] = 256 }, TimeSpan.Zero);
        CollectionAssert.AreEqual(first.Slices.ToArray(), second.Slices.ToArray());
        Assert.AreEqual(12, first.Slices.Count(slice => slice.PitchShiftCents == -200));
        Assert.IsTrue(first.Slices.Where(slice => slice.IsTriggered).All(slice => slice.Velocity is 0.25 or 0.75));
    }

    [TestMethod]
    [DataRow("0001", -500d)]
    [DataRow("0101", -200d)]
    [DataRow("1001", 0d)]
    [DataRow("1101", 300d)]
    public void LittleEndianPitchBits_MapToDiscreteCents(string bits, double cents)
    {
        var slice = this.mapper.Map("qasm", new Dictionary<string, int> { [bits] = 100 }, TimeSpan.Zero, 1).Slices.Single();
        Assert.AreEqual(new DrumSliceTrigger(0, true, 1, cents, 1, 0), slice);
    }

    [TestMethod]
    public void IndependentUniformCounts_HaveNoCorrelationStretch()
    {
        var counts = Enumerable.Range(0, 16).ToDictionary(value => Convert.ToString(value, 2).PadLeft(4, '0'), _ => 1);
        var result = this.mapper.Map("qasm", counts, TimeSpan.Zero);
        Assert.IsTrue(result.Slices.All(slice => slice.TimeStretchFactor == 1 && slice.SliceCutOffsetMs == 0));
        CollectionAssert.AreEquivalent(new double[] { -500, -200, 0, 300 }, result.Slices.Select(slice => slice.PitchShiftCents).Distinct().ToArray());
    }

    [TestMethod]
    public void EmptyAndZeroCounts_ProduceNeutralRests()
    {
        foreach (var counts in new[] { new Dictionary<string, int>(), new Dictionary<string, int> { ["0000"] = 0 } })
        {
            var result = this.mapper.Map("qasm", counts, TimeSpan.Zero);
            CollectionAssert.AreEqual(Enumerable.Range(0, 16).Select(step => new DrumSliceTrigger(step, false, 0, 0, 1, 0)).ToArray(), result.Slices.ToArray());
            Assert.AreEqual(0, result.RawCounts.Count);
        }
    }

    [TestMethod]
    public void ReturnedCollections_AreDefensiveReadOnlySnapshots()
    {
        var source = new Dictionary<string, int> { ["1001"] = 1024 };
        var result = this.mapper.Map("qasm", source, TimeSpan.Zero);
        source["1001"] = 1;
        Assert.AreEqual(1024, result.RawCounts["1001"]);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IDictionary<string, int>)result.RawCounts)["1001"] = 2);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<DrumSliceTrigger>)result.Slices)[0] = result.Slices[0]);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(5)]
    [DataRow(15)]
    [DataRow(1024)]
    public void ArbitraryGridSizesAndLargeCounts_StayInRange(int steps)
    {
        var result = this.mapper.Map("qasm", new Dictionary<string, int>
        {
            ["1001001001001001"] = int.MaxValue, ["0110110110110110"] = int.MaxValue
        }, TimeSpan.Zero, steps);
        Assert.AreEqual(steps, result.Slices.Count);
        CollectionAssert.AreEqual(Enumerable.Range(0, steps).ToArray(), result.Slices.Select(slice => slice.StepIndex).ToArray());
        Assert.IsTrue(result.Slices.All(slice => slice.Velocity is >= 0 and <= 1 && slice.TimeStretchFactor is >= 0.5 and <= 2 &&
            slice.PitchShiftCents is >= -1200 and <= 1200 && slice.SliceCutOffsetMs is >= 0 and <= 100));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("010")]
    [DataRow("0120")]
    [DataRow("0x01")]
    [DataRow("00000000000000000")]
    public void InvalidBitstrings_AreRejected(string bits) =>
        Assert.ThrowsExactly<ArgumentException>(() => this.mapper.Map("qasm", new Dictionary<string, int> { [bits] = 1 }, TimeSpan.Zero));

    [TestMethod]
    public void NegativeCountsAndMixedWidths_AreRejected()
    {
        Assert.ThrowsExactly<ArgumentException>(() => this.mapper.Map("qasm", new Dictionary<string, int> { ["0000"] = -1 }, TimeSpan.Zero));
        Assert.ThrowsExactly<ArgumentException>(() => this.mapper.Map("qasm", new Dictionary<string, int> { ["0000"] = 1, ["00000"] = 1 }, TimeSpan.Zero));
        Assert.ThrowsExactly<ArgumentNullException>(() => this.mapper.Map("qasm", null!, TimeSpan.Zero));
    }

    [TestMethod]
    public void InvalidMetadataAndStepCounts_AreRejected()
    {
        Assert.ThrowsExactly<ArgumentException>(() => this.mapper.Map(" ", new Dictionary<string, int>(), TimeSpan.Zero));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => this.mapper.Map("qasm", new Dictionary<string, int>(), TimeSpan.FromTicks(-1)));
        foreach (int steps in new[] { 0, 1025 })
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => this.mapper.Map("qasm", new Dictionary<string, int>(), TimeSpan.Zero, steps));
    }
}
