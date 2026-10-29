using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Ytdlp;
using System.Reflection;
using System.Text;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class YtdlpClientOutputTests
{
    [TestMethod]
    public async Task SaveAsync_PublishesClosedTitleNamedFile_AndWatcherCanTagBeforeWaitExpires()
    {
        string root = Path.Combine(Path.GetTempPath(), "ytdlp-publish-" + Guid.NewGuid().ToString("N"));
        string output = Path.Combine(root, "output");
        Directory.CreateDirectory(output);
        string source = Path.Combine(root, "source.wav");
        try
        {
            WriteTinyWav(source);
            const string title = "Wan bushi - Combo whore";
            string expected = Path.Combine(output, title + ".wav");
            var tagged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var watcher = new FileSystemWatcher(output, "*.wav") { NotifyFilter = NotifyFilters.FileName };
            watcher.Created += (_, args) =>
            {
                try
                {
                    Assert.AreEqual(expected, args.FullPath);
                    using (var exclusive = new FileStream(args.FullPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                        Assert.AreEqual(new FileInfo(source).Length, exclusive.Length, "The entire file must be present before Created.");
                    Assert.IsTrue(AudioObj.WriteBpmTagToFile(args.FullPath, 128));
                    tagged.TrySetResult();
                }
                catch (Exception error) { tagged.TrySetException(error); }
            };
            watcher.EnableRaisingEvents = true;
            var save = typeof(YtdlpClient).GetMethod("SaveAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
            string actual = await (Task<string>)save.Invoke(null,
                [source, output, new YtdlpVideo { Title = title, Id = "Rb678vL_D40" }, "wav", CancellationToken.None])!;
            Assert.AreEqual(expected, actual);
            await tagged.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsTrue(await ModularAudience.Audio.Processing.YoutubeAudioImporter.WaitForBpmTagAsync(actual,
                TimeSpan.Zero, CancellationToken.None));
            Assert.HasCount(0, Directory.GetDirectories(output));
            byte[] existing = File.ReadAllBytes(actual);
            await Assert.ThrowsAsync<IOException>(async () => await (Task<string>)save.Invoke(null,
                [source, output, new YtdlpVideo { Title = title }, "wav", CancellationToken.None])!);
            CollectionAssert.AreEqual(existing, File.ReadAllBytes(actual), "A title collision must not overwrite an existing file.");
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void SafeTitle_PreservesLongUnicodeTitlesAndOnlyAdaptsInvalidWindowsNames()
    {
        var safe = typeof(YtdlpClient).GetMethod("SafeTitle", BindingFlags.Static | BindingFlags.NonPublic)!;
        string title = "Ä ♪ " + new string('x', 100);
        Assert.AreEqual(title, safe.Invoke(null, [title]));
        Assert.AreEqual("A_B_C", safe.Invoke(null, ["A:B/C"]));
        Assert.AreEqual("_CON", safe.Invoke(null, ["CON"]));
    }

    [TestMethod]
    public void ValidateDownloadedPath_MarkedWavWithTrailingDiagnostics_AcceptsOnlyJobLocalOutput()
    {
        string root = Path.Combine(Path.GetTempPath(), "ModularAudience-YtdlpOutput-" + Guid.NewGuid().ToString("N"));
        string job = Path.Combine(root, "job");
        Directory.CreateDirectory(job);
        try
        {
            string local = Path.Combine(job, "audio.wav");
            string external = Path.Combine(root, "external.wav");
            WriteTinyWav(local);
            WriteTinyWav(external);
            var validate = typeof(YtdlpClient).GetMethod("ValidateDownloadedPath", BindingFlags.Static | BindingFlags.NonPublic)!;
            string marked = "[ExtractAudio] Destination: audio.wav\n__MA_OUTPUT__:" + local +
                "\n[debug] Cleanup finished\nWarning: trailing diagnostic\n";

            Assert.AreEqual(local, validate.Invoke(null, [marked, job, "wav", null]));
            foreach (string output in new[] { local + "\n[debug] Done", "__MA_OUTPUT__:" + external + "\n[debug] Done" })
            {
                var error = Assert.ThrowsExactly<TargetInvocationException>(
                    () => validate.Invoke(null, [output, job, "wav", null]), output);
                Assert.IsInstanceOfType<InvalidDataException>(error.InnerException, output);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteTinyWav(string path)
    {
        using var writer = new BinaryWriter(File.Create(path), Encoding.ASCII);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(38);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(8000);
        writer.Write(16000);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(2);
        writer.Write((short)0);
    }
}
