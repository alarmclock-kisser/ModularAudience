using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Quantum.Models;
using ModularAudience.Quantum.Serialization;
using ModularAudience.Quantum.Services;

namespace ModularAudience.Quantum.Tests.Services;

[TestClass]
public sealed class QuantumStudioCoreTests
{
    [TestMethod]
    [DataRow("Bell break")]
    [DataRow("Weighted groove")]
    [DataRow("GHZ / correlated")]
    [DataRow("Independent superposition")]
    [DataRow("Empty")]
    public void Presets_BuildValidQasmForEverySupportedWidth(string name)
    {
        foreach (int width in new[] { 4, 8, 16 })
        {
            var preset = QuantumCircuitComposer.CreatePreset(name, width);
            string qasm = QuantumCircuitComposer.Build(preset);
            StringAssert.Contains(qasm, $"qreg q[{width}];");
            StringAssert.EndsWith(qasm, "measure q -> c;\n");
            Assert.IsTrue(preset.Gates.Count <= 512);
        }
    }

    [TestMethod]
    public void Composer_HonorsGateOrderEnableStateAndQasmVersion()
    {
        var circuit = new QuantumCircuitDefinition(4,
        [new(QuantumGateKind.Hadamard, 0), new(QuantumGateKind.BitFlip, 1, Enabled: false),
            new(QuantumGateKind.ControlledNot, 0, 1), new(QuantumGateKind.RotationY, 2, AngleRadians: 0.5)], OpenQasmVersion.Version3);
        string qasm = QuantumCircuitComposer.Build(circuit);
        StringAssert.Contains(qasm, "h q[0];\ncx q[0], q[1];\nry(0.5) q[2];");
        Assert.IsFalse(qasm.Contains("x q[1];\n", StringComparison.Ordinal));
        StringAssert.Contains(qasm, "OPENQASM 3.0;");
    }

    [TestMethod]
    public void ComposerAndRandomizer_RejectMalformedInput()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => QuantumCircuitComposer.Build(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => QuantumCircuitComposer.Build(new(4, null!)));
        Assert.ThrowsExactly<ArgumentException>(() => QuantumCircuitComposer.Build(new(4, Enumerable.Repeat(new QuantumGateOperation(QuantumGateKind.Hadamard, 0), 513).ToArray())));
        QuantumGateOperation[] invalid = [null!, new((QuantumGateKind)99, 0), new(QuantumGateKind.Hadamard, -1),
            new(QuantumGateKind.Hadamard, 4), new(QuantumGateKind.ControlledNot, 0, 0), new(QuantumGateKind.ControlledNot, 0, 4),
            new(QuantumGateKind.RotationY, 0, AngleRadians: double.NaN)];
        foreach (var gate in invalid) Assert.ThrowsExactly<ArgumentException>(() => QuantumCircuitComposer.Build(new(4, [gate])));
        Assert.ThrowsExactly<ArgumentException>(() => QuantumCircuitComposer.CreatePreset("unknown", 4));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => QuantumCircuitComposer.CreatePreset("Weighted groove", 4, double.NaN));
        Assert.ThrowsExactly<ArgumentNullException>(() => QuantumCircuitComposer.Randomize(4, null!));
        QuantumCircuitRandomization[] bad = [new(Layers: 0), new(HadamardChance: 2), new(EntanglementChance: double.NaN),
            new(MinimumAngleRadians: -10), new(MaximumAngleRadians: 10), new(MinimumAngleRadians: 2, MaximumAngleRadians: 1)];
        foreach (var settings in bad) Assert.ThrowsExactly<ArgumentException>(() => QuantumCircuitComposer.Randomize(4, settings));
    }

    [TestMethod]
    public void Randomization_IsSeededBoundedAndUsesBothHAndRy()
    {
        var options = new QuantumCircuitRandomization(Layers: 16, HadamardChance: 0.5, EntanglementChance: 1);
        var first = QuantumCircuitComposer.Randomize(16, options);
        var second = QuantumCircuitComposer.Randomize(16, options);
        Assert.AreEqual(512, first.Gates.Count);
        CollectionAssert.AreEqual(first.Gates.ToArray(), second.Gates.ToArray());
        Assert.IsTrue(first.Gates.Any(gate => gate.Kind == QuantumGateKind.RotationY));
        Assert.IsTrue(first.Gates.Any(gate => gate.Kind == QuantumGateKind.Hadamard));
        Assert.IsTrue(first.Gates.Where(gate => gate.Kind == QuantumGateKind.ControlledNot).All(gate => gate.Qubit != gate.Target));
    }

    [TestMethod]
    public void SharedMeasurements_PreserveAntiPhaseTracksAndRepeatBars()
    {
        var mapper = new QuantumArrangementMapper();
        var options = new QuantumBeatOptions { Bars = 2, StepsPerBar = 8, Density = 1, QuantumVelocity = false,
            Arrangement = QuantumBarArrangement.RepeatFirstBar, PitchEnabled = true, TimeStretchEnabled = true,
            VarispeedEnabled = true, VaryDuration = true, CutEnabled = true, ReverseEnabled = true, Swing = 0.2 };
        var tracks = new[] { new QuantumTrackMapping("Kick", 0), new QuantumTrackMapping("Snare", 1) };
        var response = new QuantumJobResponse("test", new Dictionary<string, int> { ["1001"] = 512, ["0110"] = 512 }, TimeSpan.FromMilliseconds(20));
        var result = mapper.Map("qasm", response, 4, options, tracks, CancellationToken.None);
        Assert.AreEqual(16, result.Notes.Count);
        Assert.AreEqual(16, result.Notes.Select(note => (int)note.StartStep).Distinct().Count());
        Assert.IsTrue(result.Notes.All(note => note.TrackIndex == 0 ? note.Measurement == "1001" : note.Measurement == "0110"));
        foreach (var note in result.Notes)
        {
            Assert.AreEqual(note.Measurement == "1001" ? 0d : -200d, note.PitchShiftCents);
            Assert.AreEqual(note.Measurement == "1001" ? 1.25 : 1, note.TimeStretchFactor, 1e-10);
            Assert.AreEqual(note.Measurement == "1001", note.Reverse);
            Assert.AreEqual(1, note.Velocity);
        }
        var firstBar = result.Notes.Take(8).ToArray(); var secondBar = result.Notes.Skip(8).ToArray();
        for (int i = 0; i < firstBar.Length; i++) Assert.AreEqual(firstBar[i] with { StartStep = firstBar[i].StartStep + 8 }, secondBar[i]);
        Assert.AreEqual(response.ExecutionLatency, result.ExecutionLatency);
    }

    [TestMethod]
    public void Mapping_HandlesGhostsOffsetsDisabledTracksAndInterleaving()
    {
        var mapper = new QuantumArrangementMapper();
        var response = new QuantumJobResponse("test", new Dictionary<string, int> { ["0000"] = 1024 }, TimeSpan.Zero);
        var options = new QuantumBeatOptions { Bars = 3, StepsPerBar = 4, Density = 1, GhostNotes = true, GhostChance = 1,
            QuantumVelocity = true, GhostVelocityScale = 0.2, Arrangement = QuantumBarArrangement.AlternateTwoBars, Interleaved = true };
        var tracks = new[] { new QuantumTrackMapping("A", 0, StepOffset: -1), new QuantumTrackMapping("B", 1, InvertTrigger: true),
            new QuantumTrackMapping("Off", 0, Enabled: false), new QuantumTrackMapping("Silent", 0, Gain: 0) };
        var result = mapper.Map("qasm", response, 4, options, tracks, CancellationToken.None);
        Assert.AreEqual(12, result.Notes.Count);
        Assert.IsTrue(result.Notes.All(note => note.TrackIndex == 1 && !note.IsGhost && note.Velocity == 1));
        var ghosts = mapper.Map("qasm", response, 4, options with { Interleaved = false }, tracks, CancellationToken.None);
        Assert.AreEqual(24, ghosts.Notes.Count);
        Assert.IsTrue(ghosts.Notes.Where(note => note.TrackIndex == 0).All(note => note.IsGhost && note.Velocity == 0.2));
        Assert.AreEqual(0, mapper.Map("qasm", response, 4, options with { Density = 0 }, tracks, CancellationToken.None).Notes.Count);
    }

    [TestMethod]
    public void Mapping_IsIndependentOfDictionaryOrderAndHandlesEmptyCounts()
    {
        var mapper = new QuantumArrangementMapper(); var options = new QuantumBeatOptions { Density = 1 };
        var tracks = new[] { new QuantumTrackMapping("Kick", 0), new QuantumTrackMapping("Snare", 1) };
        var first = mapper.Map("qasm", new("a", new Dictionary<string, int> { ["1001"] = int.MaxValue, ["0110"] = int.MaxValue }, TimeSpan.Zero), 4, options, tracks, CancellationToken.None);
        var second = mapper.Map("qasm", new("b", new Dictionary<string, int> { ["0110"] = int.MaxValue, ["1001"] = int.MaxValue }, TimeSpan.Zero), 4, options, tracks, CancellationToken.None);
        CollectionAssert.AreEqual(first.Notes.ToArray(), second.Notes.ToArray());
        Assert.AreEqual(0, mapper.Map("qasm", new("empty", new Dictionary<string, int>(), TimeSpan.Zero), 4, options, tracks, CancellationToken.None).Notes.Count);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => mapper.Map("qasm", new("empty", new Dictionary<string, int>(), TimeSpan.Zero), 4, options, tracks, cts.Token));
        Assert.ThrowsExactly<ArgumentException>(() => mapper.Map("qasm", new("bad", new Dictionary<string, int>(), TimeSpan.FromTicks(-1)), 4, options, tracks, CancellationToken.None));
    }

    [TestMethod]
    public void BeatOptions_ValidateEveryNumericRangeAndTrackBoundary()
    {
        var good = new QuantumBeatOptions(); var tracks = new[] { new QuantumTrackMapping("Kick", 0) };
        QuantumBeatOptions[] invalid = [good with { Bars = 0 }, good with { StepsPerBar = 65 }, good with { Bpm = double.NaN }, good with { Density = -1 },
            good with { Swing = 0.5 }, good with { Arrangement = (QuantumBarArrangement)99 }, good with { ModifierQubit = 4 },
            good with { MinimumVelocity = -1 }, good with { MaximumVelocity = 0.1 }, good with { GhostChance = 2 }, good with { GhostVelocityScale = 2 },
            good with { MinimumDurationSteps = 0 }, good with { MaximumDurationSteps = 0 }, good with { MinimumPitchCents = -1201 }, good with { MaximumPitchCents = -1201 },
            good with { PitchStepCents = 0 }, good with { MinimumStretch = 0 }, good with { MaximumStretch = 0 }, good with { MinimumSpeed = 0 },
            good with { MaximumSpeed = 0 }, good with { MaximumCutOffsetMs = -1 }, good with { OutputGainDb = 13 }];
        foreach (var options in invalid) Assert.Throws<ArgumentException>(() => options.Validate(4, tracks));
        Assert.Throws<ArgumentException>(() => good.Validate(3, tracks));
        Assert.Throws<ArgumentException>(() => good.Validate(4, []));
        foreach (var track in new QuantumTrackMapping[] { null!, new("", 0), new("Kick", 4), new("Kick", 0, StepOffset: int.MinValue),
            new("Kick", 0, DensityScale: 3), new("Kick", 0, Gain: 2), new("Kick", 0, PitchOffsetCents: 1201) })
            Assert.Throws<ArgumentException>(() => good.Validate(4, [track]));
    }

    [TestMethod]
    public void ProjectSerializer_RoundTripsWithoutReflectionAndRejectsBrokenDocuments()
    {
        Assert.IsFalse(JsonSerializer.IsReflectionEnabledByDefault);
        var document = new QuantumExperimentDocument(1, QuantumCircuitComposer.CreatePreset("Bell break", 4), new QuantumBeatOptions(),
            [new("Kick", 0)], new QuantumCircuitRandomization());
        string json = QuantumExperimentSerializer.Serialize(document);
        var restored = QuantumExperimentSerializer.Deserialize(json);
        Assert.AreEqual(QuantumCircuitComposer.Build(document.Circuit), QuantumCircuitComposer.Build(restored.Circuit));
        Assert.AreEqual(document.Options, restored.Options);
        Assert.IsFalse(json.Contains("ApiKey", StringComparison.Ordinal));
        Assert.ThrowsExactly<ArgumentException>(() => QuantumExperimentSerializer.Deserialize(new string(' ', 1_048_577) + "{}"));
        Assert.ThrowsExactly<JsonException>(() => QuantumExperimentSerializer.Deserialize("null"));
        Assert.ThrowsExactly<ArgumentException>(() => QuantumExperimentSerializer.Serialize(document with { Version = 2 }));
        Assert.ThrowsExactly<ArgumentException>(() => QuantumExperimentSerializer.Serialize(document with { Circuit = null! }));
    }
}
