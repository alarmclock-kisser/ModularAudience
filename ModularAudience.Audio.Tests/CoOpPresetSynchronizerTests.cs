using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Effects;
using ModularAudience.Audio.Processing;
using ModularAudience.CoOp.Protocol;
using ModularAudience.Forms;
using ModularAudience.Forms.Effects;
using ModularAudience.Forms.Quantum;
using ModularAudience.Quantum.Models;
using ModularAudience.Quantum.Services;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class CoOpPresetSynchronizerTests
{
    [TestMethod]
    public void Import_AddsConflictingPresetWithoutOverwritingAndDeduplicatesByContent()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            var store = new SampleModellerPresetStore(Path.Combine(root, "SampleModellerPresets"));
            store.Save("Lead", CreateSamplePreset("Lead", gainDb: 3));
            var synchronizer = new CoOpPresetSynchronizer(root);
            SampleModellerSettings incoming = CreateSamplePreset("Lead", gainDb: 6);

            CoOpPresetSyncResult first = synchronizer.Import(
                "Host A",
                [new CoOpPresetSyncItem("sample-modeller", JsonSerializer.Serialize(incoming))]);

            Assert.AreEqual(1, first.CreatedCount);
            Assert.AreEqual(0, first.DuplicatesSkipped);
            Assert.AreEqual(3d, store.Load("Lead").Effects.Single().GainDb);
            Assert.AreEqual(6d, store.Load("Lead (Host A)").Effects.Single().GainDb);

            incoming.Name = "A different name";
            CoOpPresetSyncResult duplicate = synchronizer.Import(
                "Host B",
                [new CoOpPresetSyncItem("sample-modeller", JsonSerializer.Serialize(incoming))]);

            Assert.AreEqual(0, duplicate.CreatedCount);
            Assert.AreEqual(1, duplicate.DuplicatesSkipped);
            CollectionAssert.AreEquivalent(new[] { "Lead", "Lead (Host A)" }, store.Names());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Capture_IncludesUserPresetsButExcludesBuiltInsAndGeneralSettings()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            string effectsDirectory = Path.Combine(root, "Effects");
            string gainPresetPath = Path.Combine(effectsDirectory, "GainPresets.json");
            var effectStore = new EffectPresetStore(AudioEffectKind.Gain, gainPresetPath);
            _ = effectStore.ListAvailable();
            AudioEffectSettings settings = AudioEffectCatalog.Get(AudioEffectKind.Gain).CreateDefaults();
            effectStore.Save("User Gain", settings);
            effectStore.Save("Same Gain", settings);

            Directory.CreateDirectory(Path.Combine(root, "SampleSynthesizer"));
            File.WriteAllText(Path.Combine(root, "application-settings.json"), "{}");
            File.WriteAllText(Path.Combine(root, "SampleSynthesizer", "settings.json"), "{}");

            IReadOnlyList<CoOpPresetSyncItem> presets = new CoOpPresetSynchronizer(root).Capture();

            Assert.AreEqual(1, presets.Count);
            Assert.AreEqual("effects:Gain", presets[0].StoreId);
            StringAssert.Contains(presets[0].Json, "User Gain");
            Assert.IsFalse(presets[0].Json.Contains("Balanced", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void QuantumExperiments_AreCapturedAdditivelyAndDeduplicatedByContent()
    {
        string sourceRoot = CreateTemporaryDirectory();
        string targetRoot = CreateTemporaryDirectory();
        try
        {
            var sourceStore = new QuantumStudioStore(Path.Combine(sourceRoot, "QuantumBeatEngine"));
            sourceStore.SaveWorkspace(CreateQuantumWorkspace(shots: 4096));
            sourceStore.SaveExperiment("Amen", CreateQuantumWorkspace(shots: 2048));

            var targetStore = new QuantumStudioStore(Path.Combine(targetRoot, "QuantumBeatEngine"));
            targetStore.SaveExperiment("Amen", CreateQuantumWorkspace(shots: 1024));

            IReadOnlyList<CoOpPresetSyncItem> captured = new CoOpPresetSynchronizer(sourceRoot).Capture();
            Assert.AreEqual(1, captured.Count);
            Assert.AreEqual("quantum-experiments", captured[0].StoreId);

            var synchronizer = new CoOpPresetSynchronizer(targetRoot);
            CoOpPresetSyncResult imported = synchronizer.Import("Host A", captured);

            Assert.AreEqual(1, imported.CreatedCount);
            Assert.AreEqual(0, imported.DuplicatesSkipped);
            Assert.AreEqual(1024, targetStore.LoadExperiment("Amen").Shots);
            Assert.AreEqual(2048, targetStore.LoadExperiment("Amen (Host A)").Shots);

            JsonObject renamedPreset = JsonNode.Parse(captured[0].Json)!.AsObject();
            renamedPreset["Name"] = "A different name";
            CoOpPresetSyncResult duplicate = synchronizer.Import(
                "Host B",
                [captured[0] with { Json = renamedPreset.ToJsonString() }]);

            Assert.AreEqual(0, duplicate.CreatedCount);
            Assert.AreEqual(1, duplicate.DuplicatesSkipped);
            CollectionAssert.AreEquivalent(
                new[] { "Amen", "Amen (Host A)" },
                targetStore.ExperimentNames());
        }
        finally
        {
            Directory.Delete(sourceRoot, recursive: true);
            Directory.Delete(targetRoot, recursive: true);
        }
    }

    private static SampleModellerSettings CreateSamplePreset(string name, double gainDb)
    {
        SampleEffect gain = SampleEffect.Create(SampleEffectKind.Gain);
        gain.GainDb = gainDb;
        return new SampleModellerSettings { Name = name, Effects = [gain] };
    }

    private static QuantumStudioWorkspace CreateQuantumWorkspace(int shots) => new(
        new(1, QuantumCircuitComposer.CreatePreset("Bell break", 4), new QuantumBeatOptions(),
            [new("Kick", 0), new("Snare", 1)], new QuantumCircuitRandomization()),
        Shots: shots,
        AutoRender: false);

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "ModularAudience-PresetSync-Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
