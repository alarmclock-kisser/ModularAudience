using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Forms.Settings;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class ApplicationPreferencesTests
{
    [TestMethod]
    public void MissingFile_CreatesWav16AndDefaultWorkspaceAndZoom()
    {
        using var files = new PreferenceFiles();

        ApplicationPreferences settings = files.Store.Load();

        Assert.AreEqual(".wav", settings.ExportFormat);
        Assert.AreEqual(16, settings.ExportBits);
        Assert.IsNull(settings.WorkingDirectory);
        Assert.AreEqual(8f, settings.LogFontSize);
        Assert.IsTrue(settings.KeepStructure);
        Assert.IsFalse(settings.AllInOneBag);
        Assert.IsTrue(settings.EnableToolTips);
        Assert.IsTrue(File.Exists(files.Path));
    }

    [TestMethod]
    public void ChangedValues_RestoreInAnotherStoreInstance()
    {
        using var files = new PreferenceFiles();
        string directory = System.IO.Path.Combine(files.Directory, "Audio with spaces");
        files.Store.SetExport(".mp3", 320);
        files.Store.SetWorkingDirectory(directory);
        files.Store.SetLogFontSize(13f);
        files.Store.SetKeepStructure(false);
        files.Store.SetAllInOneBag(true);
        files.Store.SetEnableToolTips(false);

        ApplicationPreferences settings = new ApplicationPreferencesStore(files.Path).Load();

        Assert.AreEqual(".mp3", settings.ExportFormat);
        Assert.AreEqual(320, settings.ExportBits);
        Assert.AreEqual(directory, settings.WorkingDirectory);
        Assert.AreEqual(13f, settings.LogFontSize);
        Assert.IsFalse(settings.KeepStructure);
        Assert.IsTrue(settings.AllInOneBag);
        Assert.IsFalse(settings.EnableToolTips);
    }

    [TestMethod]
    public async Task ConcurrentFieldChanges_PreserveOtherInstancesPreferences()
    {
        using var files = new PreferenceFiles();
        files.Store.Load();
        string directory = System.IO.Path.Combine(files.Directory, "Workspace");

        await Task.WhenAll(
            Task.Run(() => new ApplicationPreferencesStore(files.Path).SetExport(".mp3", 256)),
            Task.Run(() => new ApplicationPreferencesStore(files.Path).SetWorkingDirectory(directory)),
            Task.Run(() => new ApplicationPreferencesStore(files.Path).SetLogFontSize(15f)),
            Task.Run(() => new ApplicationPreferencesStore(files.Path).SetKeepStructure(false)),
            Task.Run(() => new ApplicationPreferencesStore(files.Path).SetAllInOneBag(true)),
            Task.Run(() => new ApplicationPreferencesStore(files.Path).SetEnableToolTips(false)));

        ApplicationPreferences settings = files.Store.Load();
        Assert.AreEqual(".mp3", settings.ExportFormat);
        Assert.AreEqual(256, settings.ExportBits);
        Assert.AreEqual(directory, settings.WorkingDirectory);
        Assert.AreEqual(15f, settings.LogFontSize);
        Assert.IsFalse(settings.EnableToolTips);
        Assert.IsFalse(settings.KeepStructure);
        Assert.IsTrue(settings.AllInOneBag);
    }

    [TestMethod]
    public void UnknownJsonFields_ArePreservedByPreferenceUpdates()
    {
        using var files = new PreferenceFiles();
        File.WriteAllText(files.Path, """{"ExportFormat":".wav","ExportBits":24,"FutureSetting":{"Enabled":true}}""");

        files.Store.SetLogFontSize(12f);

        using var json = JsonDocument.Parse(File.ReadAllText(files.Path));
        Assert.IsTrue(json.RootElement.GetProperty("FutureSetting").GetProperty("Enabled").GetBoolean());
        Assert.AreEqual(24, json.RootElement.GetProperty("ExportBits").GetInt32());
    }

    [TestMethod]
    public void MalformedJson_IsNotOverwrittenByLoadOrChange()
    {
        using var files = new PreferenceFiles();
        const string invalid = "{invalid preferences";
        File.WriteAllText(files.Path, invalid);

        Assert.ThrowsExactly<JsonException>(() => files.Store.Load());
        Assert.ThrowsExactly<JsonException>(() => files.Store.SetLogFontSize(11f));

        Assert.AreEqual(invalid, File.ReadAllText(files.Path));
    }

    [TestMethod]
    public void RelativeWorkspace_IsRejectedWithoutOverwritingPreferences()
    {
        using var files = new PreferenceFiles();
        files.Store.Load();
        string before = File.ReadAllText(files.Path);

        Assert.ThrowsExactly<InvalidDataException>(() => files.Store.SetWorkingDirectory("relative-audio"));

        Assert.AreEqual(before, File.ReadAllText(files.Path));
    }

    [TestMethod]
    [DataRow("wav", 24, ".wav", 24)]
    [DataRow("MP3", 320, ".mp3", 320)]
    [DataRow(".wav", 192, ".wav", 16)]
    [DataRow(".mp3", 16, ".mp3", 192)]
    [DataRow(".unknown", 0, ".wav", 16)]
    public void ExportPreferenceNormalization_UsesSupportedFormatSpecificValues(string format, int bits, string expectedFormat, int expectedBits)
    {
        ApplicationPreferences settings = new ApplicationPreferences { ExportFormat = format, ExportBits = bits }.Normalize();

        Assert.AreEqual(expectedFormat, settings.ExportFormat);
        Assert.AreEqual(expectedBits, settings.ExportBits);
    }

    [TestMethod]
    [DataRow(2f, 6f)]
    [DataRow(22f, 20f)]
    [DataRow(12.5f, 12.5f)]
    public void LogZoom_RestoresSupportedPointSize(float requested, float expected)
    {
        using var files = new PreferenceFiles();

        files.Store.SetLogFontSize(requested);

        Assert.AreEqual(expected, new ApplicationPreferencesStore(files.Path).Load().LogFontSize);
    }

    [TestMethod]
    public async Task FileMonitor_ObservesAtomicChangesFromAnotherInstance()
    {
        using var files = new PreferenceFiles();
        files.Store.Load();
        using var monitor = new ApplicationPreferencesMonitor(files.Path);
        var changed = new TaskCompletionSource<ApplicationPreferences>(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.Changed += (_, _) => changed.TrySetResult(files.Store.Load());

        new ApplicationPreferencesStore(files.Path).SetExport(".mp3", 192);

        ApplicationPreferences settings = await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(".mp3", settings.ExportFormat);
        Assert.AreEqual(192, settings.ExportBits);
    }

    [TestMethod]
    public async Task DisposedMonitor_DoesNotPublishChanges()
    {
        using var files = new PreferenceFiles();
        files.Store.Load();
        using var monitor = new ApplicationPreferencesMonitor(files.Path);
        int changes = 0;
        monitor.Changed += (_, _) => Interlocked.Increment(ref changes);
        monitor.Dispose();

        files.Store.SetLogFontSize(12f);
        await Task.Delay(200);

        Assert.AreEqual(0, Volatile.Read(ref changes));
    }

    private sealed class PreferenceFiles : IDisposable
    {
        internal string Directory { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ModularAudience-preferences-test-" + Guid.NewGuid().ToString("N"));
        internal string Path { get; }
        internal ApplicationPreferencesStore Store { get; }

        internal PreferenceFiles()
        {
            System.IO.Directory.CreateDirectory(this.Directory);
            this.Path = System.IO.Path.Combine(this.Directory, "application-settings.json");
            this.Store = new ApplicationPreferencesStore(this.Path);
        }

        public void Dispose() => System.IO.Directory.Delete(this.Directory, recursive: true);
    }
}
