using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ModularAudience.Audio.Tests;

[TestClass]
[DoNotParallelize]
public sealed class AudioStoragePathsTests
{
    [TestMethod]
    public void WindowsMusicLocation_TakesPrecedenceAndSupportsRedirectedFolders()
    {
        string music = Path.Combine(Path.GetTempPath(), "RedirectedMusic");
        string profile = Path.Combine(Path.GetTempPath(), "UserProfile");

        Assert.AreEqual(Path.GetFullPath(music), AudioStoragePaths.ResolveMusicDirectory(music, profile));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("Music")]
    public void MissingKnownMusicLocation_UsesProfileMusicNotCurrentDirectoryOrAppData(string missingMusic)
    {
        string profile = Path.Combine(Path.GetTempPath(), "ExampleProfile");

        string result = AudioStoragePaths.ResolveMusicDirectory(missingMusic, profile);

        Assert.AreEqual(Path.Combine(profile, "Music"), result);
        Assert.IsTrue(Path.IsPathFullyQualified(result));
    }

    [TestMethod]
    public void MissingMusicAndProfile_FailsInsteadOfFallingBackToTemporaryStorage()
    {
        Assert.ThrowsExactly<DirectoryNotFoundException>(() => AudioStoragePaths.ResolveMusicDirectory(null, null));
    }

    [TestMethod]
    public void DefaultCollectionAndExporter_ShareMusicExportsAndRecordingsDirectories()
    {
        using var collection = new AudioCollection();
        var exporter = new AudioExporter();

        Assert.AreEqual(Path.Combine(AudioStoragePaths.MusicDirectory, "ModularAudience"), collection.WorkingDirectory);
        Assert.AreEqual(Path.Combine(collection.WorkingDirectory, "_Exports"), collection.ExportPath);
        Assert.AreEqual(Path.Combine(collection.WorkingDirectory, "_Recordings"), collection.RecordPath);
        Assert.AreEqual(collection.ExportPath, collection.Exporter.ExportDirectory);
        Assert.AreEqual(collection.ExportPath, exporter.ExportDirectory);
        Assert.IsTrue(Path.IsPathFullyQualified(collection.WorkingDirectory));
    }

    [TestMethod]
    public void ExplicitWorkspace_IsRespectedWithoutMovingOrDeletingLegacyRecordings()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ModularAudience-storage-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "_Records"));
        string legacyFile = Path.Combine(directory, "_Records", "existing.wav");
        File.WriteAllText(legacyFile, "existing recording");
        try
        {
            using var collection = new AudioCollection(directory);

            Assert.AreEqual(directory, collection.WorkingDirectory);
            Assert.AreEqual(Path.Combine(directory, "_Exports"), collection.Exporter.ExportDirectory);
            Assert.IsTrue(Directory.Exists(Path.Combine(directory, "_Recordings")));
            Assert.AreEqual("existing recording", File.ReadAllText(legacyFile));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public void WorkspaceChange_UpdatesExistingDefaultCollectionsAndNewOnesWithoutMovingFiles()
    {
        string root = Path.Combine(Path.GetTempPath(), "ModularAudience-workspace-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            AudioStoragePaths.SetWorkingDirectory(Path.Combine(root, "First"));
            using var existing = new AudioCollection();
            string oldFile = Path.Combine(existing.ExportPath, "existing.wav");
            File.WriteAllText(oldFile, "existing export");
            using var custom = new AudioCollection(Path.Combine(root, "Custom"));

            string second = Path.Combine(root, "Second");
            AudioStoragePaths.SetWorkingDirectory(second);
            using var newlyCreated = new AudioCollection();

            Assert.AreEqual(second, existing.WorkingDirectory);
            Assert.AreEqual(Path.Combine(second, "_Exports"), existing.Exporter.ExportDirectory);
            Assert.AreEqual(Path.Combine(second, "_Recordings"), existing.RecordPath);
            Assert.AreEqual(second, newlyCreated.WorkingDirectory);
            Assert.AreEqual(Path.Combine(root, "Custom"), custom.WorkingDirectory);
            Assert.AreEqual("existing export", File.ReadAllText(oldFile));
        }
        finally
        {
            AudioStoragePaths.SetWorkingDirectory(null);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void ExplicitCollectionDirectoryChange_KeepsExporterInSyncAndOptsOutOfGlobalDefault()
    {
        string root = Path.Combine(Path.GetTempPath(), "ModularAudience-custom-workspace-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var collection = new AudioCollection();
            string custom = Path.Combine(root, "Custom");

            collection.WorkingDirectory = custom;
            AudioStoragePaths.SetWorkingDirectory(Path.Combine(root, "Default"));

            Assert.AreEqual(custom, collection.WorkingDirectory);
            Assert.AreEqual(Path.Combine(custom, "_Exports"), collection.Exporter.ExportDirectory);
        }
        finally
        {
            AudioStoragePaths.SetWorkingDirectory(null);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
