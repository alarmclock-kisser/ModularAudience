using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Processing;
using NAudio.Wave;

namespace ModularAudience.Audio.Tests
{
    [TestClass]
    [DoNotParallelize]
    public sealed class YoutubeAudioImporterTests
    {
        [TestMethod]
        public void CalculateStretchFactor_SlowerActualSourceBpm_ReturnsPointEight()
        {
            double factor = YoutubeAudioImporter.CalculateStretchFactor(100f, 125f);

            Assert.AreEqual(0.8, factor, 1e-12,
                "The stretch factor must use the actual source BPM, not a fixed reference BPM.");
        }

        [TestMethod]
        public void CalculateStretchFactor_FasterActualSourceBpm_ReturnsOnePointTwo()
        {
            double factor = YoutubeAudioImporter.CalculateStretchFactor(150f, 125f);

            Assert.AreEqual(1.2, factor, 1e-12,
                "The stretch factor must be source BPM divided by target BPM.");
        }

        [TestMethod]
        public void CalculateStretchFactor_NonFiniteOrNonPositiveBpm_RejectsEitherArgument()
        {
            foreach (float bpm in new[] { float.NaN, 0f, -1f, float.PositiveInfinity, float.NegativeInfinity })
            {
                var sourceError = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
                    () => YoutubeAudioImporter.CalculateStretchFactor(bpm, 125f), $"Source BPM: {bpm}");
                Assert.AreEqual("sourceBpm", sourceError.ParamName);
                var targetError = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
                    () => YoutubeAudioImporter.CalculateStretchFactor(100f, bpm), $"Target BPM: {bpm}");
                Assert.AreEqual("targetBpm", targetError.ParamName);
            }
        }

        [TestMethod]
        public async Task ImportAsync_TemporaryWav_LoadsSamplesAndLeavesFileOwnedByCaller()
        {
            string path = NewWavPath();
            short[] samples = [0, 8192, -8192, 16384, -16384, 4096, -4096, 0];
            try
            {
                WriteWav(path, samples);
                byte[] original = File.ReadAllBytes(path);
                using (AudioObj audio = await YoutubeAudioImporter.ImportAsync(
                    path, "Imported title", true, TimeSpan.Zero, null, null, CancellationToken.None))
                {
                    Assert.AreEqual(string.Empty, audio.FilePath);
                    Assert.AreEqual("Imported title", audio.Name);
                    Assert.AreEqual("Imported title", audio.OriginalName);
                    Assert.AreEqual(16000, audio.SampleRate);
                    Assert.AreEqual(2, audio.Channels);
                    Assert.HasCount(samples.Length, audio.Data);
                    for (int i = 0; i < samples.Length; i++)
                        Assert.AreEqual(samples[i] / 32768f, audio.Data[i], 1e-6f, $"Sample {i}");
                    Assert.IsTrue(File.Exists(path), "Import must not delete a caller-owned temporary WAV.");
                }
                Assert.IsTrue(File.Exists(path), "Disposing the audio must not delete the download.");
                CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
                File.Delete(path);
                Assert.IsFalse(File.Exists(path), "The owner must be able to delete the WAV after import.");
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public async Task ImportAsync_AlreadyCanceled_LeavesSourceWavUntouched()
        {
            string path = NewWavPath();
            try
            {
                WriteWav(path, [0, 8192, -8192, 0]);
                byte[] original = File.ReadAllBytes(path);
                DateTime lastWrite = File.GetLastWriteTimeUtc(path);
                using var cancellation = new CancellationTokenSource();
                cancellation.Cancel();

                var error = await Assert.ThrowsExactlyAsync<OperationCanceledException>(
                    async () =>
                    {
                        using AudioObj unexpected = await YoutubeAudioImporter.ImportAsync(
                            path, "Canceled import", true, TimeSpan.FromSeconds(5), null, null, cancellation.Token);
                    });

                Assert.AreEqual(cancellation.Token, error.CancellationToken);
                Assert.IsTrue(File.Exists(path), "Cancellation must leave the source file in place.");
                CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
                Assert.AreEqual(lastWrite, File.GetLastWriteTimeUtc(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public async Task ImportAsync_InvalidCooldown_FailsBeforeLoadingSource()
        {
            string path = NewWavPath();
            try
            {
                foreach (TimeSpan cooldown in new[] { TimeSpan.FromMilliseconds(-1), TimeSpan.FromMilliseconds(300001) })
                {
                    var error = await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
                        async () =>
                        {
                            using AudioObj unexpected = await YoutubeAudioImporter.ImportAsync(
                                path, "Invalid cooldown", true, cooldown, null, null, CancellationToken.None);
                        }, $"Invalid cooldown: {cooldown}");
                    Assert.AreEqual("cooldown", error.ParamName);
                    Assert.IsFalse(File.Exists(path), "Validation must not create a source file.");
                }
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static string NewWavPath() =>
            Path.Combine(Path.GetTempPath(), $"youtube_import_{Guid.NewGuid():N}.wav");

        private static void WriteWav(string path, short[] samples)
        {
            using var writer = new WaveFileWriter(path, new WaveFormat(16000, 16, 2));
            writer.WriteSamples(samples, 0, samples.Length);
        }
    }
}
