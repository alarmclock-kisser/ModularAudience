using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Effects.AutoTune;
using ModularAudience.Forms.Effects;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class AutoTunePresetStoreTests
{
    [TestMethod]
    public void SaveAndLoad_RoundTripEveryParameterWithoutWritingAudio()
    {
        using var files = new PresetFiles();
        AutoTuneSettings settings = DetailedSettings;

        AutoTunePreset saved = files.Store.Save("Voice", settings);
        AutoTunePreset restored = new AutoTunePresetStore(files.Path).Load("voice");

        Assert.AreEqual("Voice", saved.Name);
        Assert.AreEqual(settings, restored.Parameters);
        using var json = JsonDocument.Parse(File.ReadAllText(files.Path));
        Assert.AreEqual(JsonValueKind.Array, json.RootElement.ValueKind);
        Assert.AreEqual("Custom", json.RootElement[0].GetProperty("Parameters").GetProperty("Scale").GetString());
        Assert.IsFalse(json.RootElement[0].GetProperty("Parameters").TryGetProperty("MinimumNoteSeconds", out _));
        Assert.IsFalse(json.RootElement[0].GetProperty("Parameters").TryGetProperty("MaximumThreads", out _));
        Assert.IsFalse(json.RootElement[0].GetProperty("Parameters").TryGetProperty("AnalysisThreads", out _));
        Assert.HasCount(1, Directory.GetFiles(files.Directory));
    }

    [TestMethod]
    public void BlankNames_UseFirstUnusedGenericNumberCaseInsensitively()
    {
        using var files = new PresetFiles();
        files.Store.Save("preset#1", new());
        files.Store.Save("Preset#3", new());

        Assert.AreEqual("Preset#2", files.Store.Save(" ", new()).Name);
        Assert.AreEqual("Preset#4", files.Store.Save(null, new()).Name);
    }

    [TestMethod]
    public async Task ConcurrentGenericSaves_DoNotLoseEntriesOrDuplicateNames()
    {
        using var files = new PresetFiles();

        AutoTunePreset[] presets = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            new AutoTunePresetStore(files.Path).Save(null, new AutoTuneSettings()))));

        Assert.AreEqual(8, presets.Select(preset => preset.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.HasCount(8, files.Store.List());
    }

    [TestMethod]
    public void NamedSave_UpdatesOnlyMatchingPresetAndDeleteRemovesOnlySelectedEntry()
    {
        using var files = new PresetFiles();
        files.Store.Save("Voice", new());
        files.Store.Save("Instrument", new());

        files.Store.Save("VOICE", new() { IntensityPercent = 35 });
        Assert.HasCount(2, files.Store.List());
        Assert.AreEqual(35d, files.Store.Load("Voice").Parameters.IntensityPercent);
        Assert.IsTrue(files.Store.Delete("voice"));
        Assert.AreEqual("Instrument", files.Store.List().Single().Name);
    }

    [TestMethod]
    public void MalformedPresetFile_IsNotOverwritten()
    {
        using var files = new PresetFiles();
        File.WriteAllText(files.Path, "{broken JSON");

        Assert.ThrowsExactly<JsonException>(() => files.Store.Save("New", new()));

        Assert.AreEqual("{broken JSON", File.ReadAllText(files.Path));
    }

    [TestMethod]
    public void InvalidSettingsAndNames_DoNotCreatePresetFile()
    {
        using var files = new PresetFiles();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => files.Store.Save("Broken", new() { IntensityPercent = 101 }));
        Assert.ThrowsExactly<InvalidDataException>(() => files.Store.Save("Bad\nName", new()));

        Assert.IsFalse(File.Exists(files.Path));
    }

    internal static AutoTuneSettings DetailedSettings => new()
    {
        Scale = AutoTuneScale.Custom, RootNote = 7, CustomNotesMask = 0x295,
        IntensityPercent = 73, MinimumNoteLength = "1/32", TempoBpm = 135.5,
        MaximumCorrectionSemitones = 4.5, RetuneMilliseconds = 31, ReleaseMilliseconds = 92,
        PitchSmoothingMilliseconds = 18, HysteresisCents = 12, DeadZoneCents = 3.5,
        VibratoPreservationPercent = 43, VibratoWindowMilliseconds = 160,
        ReferenceA4Hz = 442.5, FineTuneCents = -8.5, MinimumFrequencyHz = 100, MaximumFrequencyHz = 1800,
        MinimumConfidence = 0.73, SilenceThresholdDb = -48, AnalysisHopMilliseconds = 15,
        AnalysisChannel = AutoTuneAnalysisChannel.Right, FftSize = 4096, Overlap = 8,
        VoicingFadeMilliseconds = 17, FormantPreservationPercent = 60, FormantSmoothingHz = 650,
        WetPercent = 88, OutputGainDb = -1.5, BoundaryFadeMilliseconds = 12, ProtectPeak = false
    };

    private sealed class PresetFiles : IDisposable
    {
        internal string Directory { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ModularAudience-autotune-presets-test-" + Guid.NewGuid().ToString("N"));
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
