using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Effects.AutoTune;
using ModularAudience.Forms.Effects;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class AutoTuneBuiltInPresetsTests
{
    [TestMethod]
    public void Catalog_HasDistinctValidSettingsAndStableUniqueIdentities()
    {
        Assert.AreEqual(13, AutoTuneBuiltInPresets.All.Count);
        Assert.AreEqual(AutoTuneBuiltInPresets.All.Count, AutoTuneBuiltInPresets.All.Select(preset => preset.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.AreEqual(AutoTuneBuiltInPresets.All.Count, AutoTuneBuiltInPresets.All.Select(preset => preset.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.AreEqual(AutoTuneBuiltInPresets.All.Count, AutoTuneBuiltInPresets.All.Select(preset => preset.Parameters).Distinct().Count());
        foreach (AutoTuneBuiltInPreset preset in AutoTuneBuiltInPresets.All)
        {
            preset.Parameters.Validate(8000);
            Assert.IsFalse(string.IsNullOrWhiteSpace(preset.Description));
            Assert.IsTrue(preset.ToPreset().IsBuiltIn);
            StringAssert.StartsWith(preset.ToPreset().ToString(), "[Default] ");
        }
    }

    [TestMethod]
    public void SoftMediumStrongAndInstrumentProfiles_DifferMeaningfully()
    {
        AutoTuneSettings soft = Settings("soft"), medium = Settings("medium"), strong = Settings("strong");
        Assert.IsTrue(soft.IntensityPercent < medium.IntensityPercent && medium.IntensityPercent < strong.IntensityPercent);
        Assert.IsTrue(soft.RetuneMilliseconds > medium.RetuneMilliseconds && medium.RetuneMilliseconds > strong.RetuneMilliseconds);
        Assert.IsTrue(soft.VibratoPreservationPercent > strong.VibratoPreservationPercent);
        Assert.AreEqual(0d, Settings("vocals-robotic").RetuneMilliseconds);
        Assert.IsTrue(Settings("vocals-natural").VibratoPreservationPercent > Settings("vocals-robotic").VibratoPreservationPercent);
        Assert.IsTrue(Settings("instrument-bass").MinimumFrequencyHz < Settings("vocals-high").MinimumFrequencyHz);
        Assert.IsTrue(Settings("ensemble-unison").WetPercent < soft.WetPercent);
        StringAssert.Contains(AutoTuneBuiltInPresets.Description("ensemble-unison")!, "NOT independent");
        Assert.AreEqual(AutoTuneScale.NaturalMinor, Settings("creative-c-minor").Scale);
    }

    [TestMethod]
    public void AvailableList_AddsDefaultsToExistingJsonWithoutReplacingUserParameters()
    {
        using var files = new PresetFiles();
        files.Store.Save("My Voice", AutoTunePresetStoreTests.DetailedSettings);

        IReadOnlyList<AutoTunePreset> available = files.Store.ListAvailable();

        Assert.AreEqual(AutoTuneBuiltInPresets.All.Count + 1, available.Count);
        Assert.AreEqual(AutoTunePresetStoreTests.DetailedSettings, files.Store.Load("My Voice").Parameters);
        Assert.HasCount(1, files.Store.List());
        using var json = JsonDocument.Parse(File.ReadAllText(files.Path));
        Assert.AreEqual(available.Count, json.RootElement.GetArrayLength());
        Assert.AreEqual(AutoTuneBuiltInPresets.All.Count,
            json.RootElement.EnumerateArray().Count(preset => preset.TryGetProperty("BuiltInId", out _)));
        Assert.IsTrue(json.RootElement.EnumerateArray().All(preset => preset.TryGetProperty("Parameters", out _)));
        foreach (JsonElement preset in json.RootElement.EnumerateArray())
        {
            Assert.IsFalse(preset.TryGetProperty("IsBuiltIn", out _));
            JsonElement parameters = preset.GetProperty("Parameters");
            Assert.IsFalse(parameters.TryGetProperty("MaximumThreads", out _));
            Assert.IsFalse(parameters.TryGetProperty("AnalysisThreads", out _));
            Assert.IsFalse(parameters.TryGetProperty("MinimumNoteSeconds", out _));
        }
        IReadOnlyList<AutoTunePreset> restored = new AutoTunePresetStore(files.Path).ListAvailable();
        foreach (AutoTuneBuiltInPreset definition in AutoTuneBuiltInPresets.All)
            Assert.AreEqual(definition.Parameters, restored.Single(preset => preset.BuiltInId == definition.Id).Parameters, definition.Id);
    }

    [TestMethod]
    public void RepeatedInitialization_DoesNotDuplicateOrRewritePresetValues()
    {
        using var files = new PresetFiles();
        files.Store.ListAvailable();
        string before = File.ReadAllText(files.Path);

        IReadOnlyList<AutoTunePreset> available = new AutoTunePresetStore(files.Path).ListAvailable();

        Assert.AreEqual(AutoTuneBuiltInPresets.All.Count, available.Count);
        Assert.AreEqual(before, File.ReadAllText(files.Path));
    }

    [TestMethod]
    public async Task ConcurrentInitialization_SeedsEachDefaultOnlyOnceAndPreservesUsers()
    {
        using var files = new PresetFiles();
        files.Store.Save("My Voice", new());

        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() => new AutoTunePresetStore(files.Path).ListAvailable())));

        IReadOnlyList<AutoTunePreset> available = files.Store.ListAvailable();
        Assert.AreEqual(AutoTuneBuiltInPresets.All.Count + 1, available.Count);
        Assert.AreEqual(AutoTuneBuiltInPresets.All.Count, available.Where(preset => preset.IsBuiltIn).Select(preset => preset.BuiltInId).Distinct().Count());
        Assert.AreEqual("My Voice", files.Store.List().Single().Name);
    }

    [TestMethod]
    public void MissingDefault_IsRestoredWhileExistingJsonValuesStayUnchanged()
    {
        using var files = new PresetFiles();
        files.Store.ListAvailable();
        files.Store.Save("User", AutoTunePresetStoreTests.DetailedSettings);
        JsonArray json = JsonNode.Parse(File.ReadAllText(files.Path))!.AsArray();
        JsonNode missing = json.Single(preset => preset?["BuiltInId"]?.GetValue<string>() == "soft")!;
        json.Remove(missing);
        JsonNode medium = json.Single(preset => preset?["BuiltInId"]?.GetValue<string>() == "medium")!;
        medium["Parameters"]!["IntensityPercent"] = 61;
        File.WriteAllText(files.Path, json.ToJsonString());

        IReadOnlyList<AutoTunePreset> available = files.Store.ListAvailable();

        Assert.AreEqual(Settings("soft"), available.Single(preset => preset.BuiltInId == "soft").Parameters);
        Assert.AreEqual(61d, available.Single(preset => preset.BuiltInId == "medium").Parameters.IntensityPercent);
        Assert.AreEqual(AutoTunePresetStoreTests.DetailedSettings, files.Store.Load("User").Parameters);
    }

    [TestMethod]
    public void SameNamedUserPreset_IsSeparateFromProtectedDefault()
    {
        using var files = new PresetFiles();
        files.Store.Save("Soft", AutoTunePresetStoreTests.DetailedSettings);
        files.Store.ListAvailable();

        files.Store.Save("SOFT", new() { IntensityPercent = 12 });

        Assert.AreEqual(12d, files.Store.Load("soft").Parameters.IntensityPercent);
        Assert.AreEqual(Settings("soft"), files.Store.ListAvailable().Single(preset => preset.BuiltInId == "soft").Parameters);
        Assert.IsTrue(files.Store.Delete("Soft"));
        Assert.IsFalse(files.Store.Delete("Soft"));
        Assert.IsTrue(files.Store.ListAvailable().Any(preset => preset.BuiltInId == "soft"));
    }

    [TestMethod]
    public void LegacyJsonWithoutOriginMetadata_StillLoadsAndReceivesDefaults()
    {
        using var files = new PresetFiles();
        File.WriteAllText(files.Path, """[{"Name":"Old preset","Parameters":{"IntensityPercent":42}}]""");

        IReadOnlyList<AutoTunePreset> available = files.Store.ListAvailable();

        Assert.AreEqual(42d, files.Store.Load("Old preset").Parameters.IntensityPercent);
        Assert.AreEqual(AutoTuneBuiltInPresets.All.Count + 1, available.Count);
    }

    private static AutoTuneSettings Settings(string id) => AutoTuneBuiltInPresets.All.Single(preset => preset.Id == id).Parameters;

    private sealed class PresetFiles : IDisposable
    {
        internal string Directory { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ModularAudience-autotune-defaults-test-" + Guid.NewGuid().ToString("N"));
        internal string Path { get; }
        internal AutoTunePresetStore Store { get; }
        internal PresetFiles()
        {
            System.IO.Directory.CreateDirectory(this.Directory);
            this.Path = System.IO.Path.Combine(this.Directory, "AutoTunePresets.json");
            this.Store = new AutoTunePresetStore(this.Path);
        }
        public void Dispose() => System.IO.Directory.Delete(this.Directory, recursive: true);
    }
}
