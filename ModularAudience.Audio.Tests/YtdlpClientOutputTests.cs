using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Ytdlp;
using System.Reflection;
using System.Text;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class YtdlpClientOutputTests
{
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
