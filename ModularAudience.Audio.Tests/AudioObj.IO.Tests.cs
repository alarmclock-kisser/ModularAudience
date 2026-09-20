using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Threading.Tasks;
using ModularAudience.Audio;
using NAudio.Wave;

namespace ModularAudience.Audio.Tests
{
    [TestClass]
    public sealed class AudioObjIOTests
    {
        [TestMethod]
        public void Test_LoadAudioFile_Stereo_LoadsFullInterleavedSamples()
        {
            // Regression: stereo imports were truncated to half the samples because the
            // expected sample count was derived from the float BlockAlign (4 * channels)
            // instead of sizeof(float), so only the per-channel frame count was read.
            const int sampleRate = 16000;
            const int channels = 2;
            const int frames = 1000;
            string path = Path.Combine(Path.GetTempPath(), $"stereo_import_{Guid.NewGuid():N}.wav");

            try
            {
                using (var writer = new WaveFileWriter(path, new WaveFormat(sampleRate, 16, channels)))
                {
                    short[] pcm = new short[frames * channels];
                    for (int i = 0; i < pcm.Length; i++)
                    {
                        pcm[i] = (short) (i % 1000);
                    }
                    writer.WriteSamples(pcm, 0, pcm.Length);
                }

                var audio = new AudioObj { FilePath = path };
                Assert.IsTrue(audio.LoadAudioFile(), "The stereo WAV must load successfully.");

                Assert.AreEqual(channels, audio.Channels, "Channel count must be preserved.");
                Assert.AreEqual(frames * channels, audio.Data.Length,
                    "Stereo import must load the full interleaved sample count, not half.");
                Assert.AreEqual(frames, audio.Data.Length / channels,
                    "Total frame count must match the source frame count.");
                audio.Dispose();
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [TestMethod]
        public void Test_AudioObj_Dispose()
        {
            using (var scope = new AudioTestScope())
            {
                var audio = scope.Create(new float[] { 0.1f, 0.2f }, 44100, 1);
                audio.Dispose();
                
                Assert.IsFalse(audio.Playing);
                Assert.IsFalse(audio.Paused);
                Assert.AreEqual(0, audio.Data.Length);
            }
        }

        [TestMethod]
        public void Test_AudioObj_Dispose_MultipleTimes()
        {
            using (var scope = new AudioTestScope())
            {
                var audio = scope.Create(new float[] { 0.1f, 0.2f }, 44100, 1);
                audio.Dispose();
                audio.Dispose(); // Should not throw
            }
        }

        [TestMethod]
        public void Test_AudioObj_LoadNonExistentFile()
        {
            var audio = new AudioObj();
            audio.FilePath = "non_existent_file.wav";
            bool result = audio.LoadAudioFile();
            Assert.IsFalse(result);
        }
    }
}