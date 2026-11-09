using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Forms.Helpers;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class AudioCollectionExportTests
{
    [TestMethod]
    public void ExistingCollectionFolder_IsReusedInsteadOfAddingFolderCounters()
    {
        using var files = new ExportFiles();
        string first = AudioCollectionViewHelpers.GetCollectionExportDirectory(files.Directory, "Drums");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(Path.Combine(files.Directory, "Drums_2"));

        string next = AudioCollectionViewHelpers.GetCollectionExportDirectory(files.Directory, "Drums");

        Assert.AreEqual(first, next);
        Assert.AreEqual(Path.Combine(files.Directory, "Drums"), next);
        Assert.IsFalse(Directory.Exists(Path.Combine(files.Directory, "Drums_3")));
    }

    [TestMethod]
    [DataRow("..")]
    [DataRow(".")]
    [DataRow("A/B:C")]
    public void CollectionFolder_SanitizesNamesAndCannotEscapeExportRoot(string collectionName)
    {
        using var files = new ExportFiles();

        string folder = AudioCollectionViewHelpers.GetCollectionExportDirectory(files.Directory, collectionName);

        Assert.AreEqual(files.Directory, Path.GetDirectoryName(folder));
    }

    [TestMethod]
    public async Task ParallelWavExports_MergeIntoSameCollectionFolderAndPreserveExistingFiles()
    {
        using var files = new ExportFiles();
        string folder = AudioCollectionViewHelpers.GetCollectionExportDirectory(files.Directory, "Drums");
        Directory.CreateDirectory(folder);
        string requested = Path.Combine(folder, "Kick.wav");
        File.WriteAllText(requested, "existing audio must remain untouched");
        var exporter = new AudioExporter(files.Directory);
        using AudioObj first = Audio("Kick");
        using AudioObj second = Audio("Kick");
        using AudioObj third = Audio("Kick");

        string?[] paths = await Task.WhenAll(new[] { first, second, third }.Select(audio =>
            exporter.ExportWavAsync(audio, 16, folder, writeBpmTag: false, customFilePath: requested, overwriteExisting: false)));

        CollectionAssert.AreEquivalent(new[] { "Kick (1).wav", "Kick (2).wav", "Kick (3).wav" }, paths.Select(Path.GetFileName).ToArray());
        Assert.AreEqual(paths[0], first.LastExportPath);
        Assert.AreEqual(paths[1], second.LastExportPath);
        Assert.AreEqual(paths[2], third.LastExportPath);
        Assert.AreEqual("existing audio must remain untouched", File.ReadAllText(requested));
        foreach (string? path in paths)
        {
            Assert.IsNotNull(path);
            Assert.AreEqual(folder, Path.GetDirectoryName(path));
            Assert.AreEqual("RIFF", Encoding.ASCII.GetString(File.ReadAllBytes(path), 0, 4));
        }

        string? repeated = await exporter.ExportWavAsync(first, 16, folder, writeBpmTag: false, customFilePath: requested, overwriteExisting: false);
        Assert.AreEqual("Kick (4).wav", Path.GetFileName(repeated));
        Assert.AreEqual(repeated, first.LastExportPath);
        Assert.HasCount(1, Directory.GetDirectories(files.Directory));
    }

    [TestMethod]
    public async Task ParallelMp3Exports_KeepExistingFileAndUseNumberedNames()
    {
        using var files = new ExportFiles();
        string requested = Path.Combine(files.Directory, "Kick.mp3");
        File.WriteAllText(requested, "existing mp3");
        var exporter = new AudioExporter(files.Directory);
        using AudioObj first = Audio("Kick");
        using AudioObj second = Audio("Kick");

        string?[] paths = await Task.WhenAll(new[] { first, second }.Select(audio =>
            exporter.ExportMp3Async(audio, bitrate: 128, maxWorkers: 1, outDir: requested, writeBpmTag: false)));

        Assert.IsTrue(paths.All(path => path is not null), "The MP3 encoder must produce both exports.");
        Assert.AreEqual(paths[0], first.LastExportPath);
        Assert.AreEqual(paths[1], second.LastExportPath);
        CollectionAssert.AreEquivalent(new[] { "Kick (1).mp3", "Kick (2).mp3" }, paths.Select(Path.GetFileName).ToArray());
        Assert.AreEqual("existing mp3", File.ReadAllText(requested));
        Assert.IsTrue(paths.All(path => new FileInfo(path!).Length > 0));
    }

    [TestMethod]
    public async Task ExplicitWavOverwrite_RemainsAvailableForConfirmedSaveAndRecordingFinalization()
    {
        using var files = new ExportFiles();
        string requested = Path.Combine(files.Directory, "Recording.wav");
        File.WriteAllText(requested, "old recording");
        var exporter = new AudioExporter(files.Directory);
        using AudioObj audio = Audio("Recording");

        string? path = await exporter.ExportWavAsync(audio, 16, writeBpmTag: false, customFilePath: requested, overwriteExisting: true);

        Assert.AreEqual(requested, path);
        Assert.AreEqual("RIFF", Encoding.ASCII.GetString(File.ReadAllBytes(requested), 0, 4));
        Assert.HasCount(1, Directory.GetFiles(files.Directory));
    }

    [TestMethod]
    public async Task AtomicReservations_ProtectAgainstParallelDifferentBaseNames()
    {
        using var files = new ExportFiles();
        string requested = Path.Combine(files.Directory, "Kick.wav");
        File.WriteAllText(requested, "existing audio");

        string[] paths = await Task.WhenAll(new[] { requested, Path.Combine(files.Directory, "Kick (1).wav") }.Select(path => Task.Run(() =>
        {
            using AudioExportFile target = AudioExportFile.CreateUnique(path);
            target.Stream.Write([1, 2, 3]);
            target.Complete();
            return target.FilePath;
        })));

        Assert.AreEqual(2, paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.AreEqual("existing audio", File.ReadAllText(requested));
        Assert.IsTrue(paths.All(path => new FileInfo(path).Length == 3));
    }

    [TestMethod]
    public void FailedReservation_CleansOnlyItsOwnIncompleteFile()
    {
        using var files = new ExportFiles();
        string requested = Path.Combine(files.Directory, "Kick.wav");
        File.WriteAllText(requested, "existing audio");
        string reserved;
        using (AudioExportFile target = AudioExportFile.CreateUnique(requested))
        {
            reserved = target.FilePath;
            target.Stream.WriteByte(1);
        }

        Assert.IsFalse(File.Exists(reserved));
        Assert.AreEqual("existing audio", File.ReadAllText(requested));
    }

    private static AudioObj Audio(string name) => new()
    {
        Name = name, OriginalName = name, Data = Enumerable.Repeat(0.1f, 4410).ToArray(),
        SampleRate = 44100, Channels = 1, BitDepth = 16, Length = 4410, Duration = TimeSpan.FromMilliseconds(100)
    };

    private sealed class ExportFiles : IDisposable
    {
        internal string Directory { get; } = Path.Combine(Path.GetTempPath(), "ModularAudience-export-test-" + Guid.NewGuid().ToString("N"));
        internal ExportFiles() => System.IO.Directory.CreateDirectory(this.Directory);
        public void Dispose() => System.IO.Directory.Delete(this.Directory, recursive: true);
    }
}
