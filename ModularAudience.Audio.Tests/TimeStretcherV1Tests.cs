using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio;
using ModularAudience.Audio.Processors_V1;

namespace ModularAudience.Audio.Tests
{
    [TestClass]
    public sealed class TimeStretcherV1Tests
    {
        [DataTestMethod]
        [DataRow(2.0, 1024)]
        [DataRow(2.0, 2048)]
        [DataRow(2.0, 4096)]
        [DataRow(2.0, 8192)]
        [DataRow(4.0, 1024)]
        [DataRow(4.0, 2048)]
        [DataRow(4.0, 4096)]
        [DataRow(4.0, 8192)]
        [DataRow(8.0, 1024)]
        [DataRow(8.0, 2048)]
        [DataRow(8.0, 4096)]
        [DataRow(8.0, 8192)]
        public async Task TimeStretchAllThreadsAsync_SlowFactorsDoNotLeaveSilentSynthesisGaps(double factor, int chunkSize)
        {
            const int sampleRate = 44100;
            float[] sourceData = Enumerable.Repeat(0.5f, chunkSize * 8).ToArray();
            using AudioObj audio = new()
            {
                Data = sourceData,
                SampleRate = sampleRate,
                Channels = 1,
                Duration = TimeSpan.FromSeconds(sourceData.Length / (double)sampleRate),
                Length = sourceData.Length
            };

            await TimeStretcher.TimeStretchAllThreadsAsync(
                audio,
                chunkSize: chunkSize,
                overlap: 0.5f,
                factor: factor,
                keepData: false,
                normalize: 0f,
                maxWorkers: 2,
                channeled: false);

            float peakInFirstSynthesisGap = audio.Data
                .Skip(chunkSize)
                .Take(chunkSize)
                .Max(Math.Abs);

            Assert.AreEqual((int)(sourceData.Length * factor), audio.Data.Length);
            Assert.IsTrue(
                peakInFirstSynthesisGap > 0.1f,
                $"Expected continuous audio at factor {factor:F1} with chunk size {chunkSize}, got peak {peakInFirstSynthesisGap:F4}.");
        }

        [TestMethod]
        public async Task TimeStretchAllThreadsAsync_FactorEightKeepsSteadyToneContinuous()
        {
            const int chunkSize = 4096;
            const int sampleRate = 40960;
            const int sourceLength = 4096 * 8;
            float[] sourceData = new float[sourceLength];
            for (int index = 0; index < sourceData.Length; index++)
            {
                sourceData[index] = 0.5f * MathF.Sin(2f * MathF.PI * 440f * index / sampleRate);
            }

            using AudioObj audio = new()
            {
                Data = sourceData,
                SampleRate = sampleRate,
                Channels = 1,
                Duration = TimeSpan.FromSeconds(sourceData.Length / (double)sampleRate),
                Length = sourceData.Length
            };

            await TimeStretcher.TimeStretchAllThreadsAsync(
                audio,
                chunkSize: chunkSize,
                overlap: 0.5f,
                factor: 8.0,
                keepData: false,
                normalize: 0f,
                maxWorkers: 2,
                channeled: false);

            float minimumWindowRms = float.MaxValue;
            float maximumWindowRms = 0f;
            int minimumWindowStart = 0;
            int maximumWindowStart = 0;
            float minimumTailWindowRms = float.MaxValue;
            const int windowSize = 4096;
            int stableRegionEnd = audio.Data.Length - (chunkSize * 8);
            for (int start = chunkSize; start + windowSize < audio.Data.Length - (chunkSize * 2); start += windowSize / 2)
            {
                double sumSquares = 0;
                for (int index = start; index < start + windowSize; index++)
                {
                    float sample = audio.Data[index];
                    sumSquares += sample * sample;
                }

                float rms = (float)Math.Sqrt(sumSquares / windowSize);
                if (start + windowSize < stableRegionEnd)
                {
                    if (rms < minimumWindowRms)
                    {
                        minimumWindowRms = rms;
                        minimumWindowStart = start;
                    }

                    if (rms > maximumWindowRms)
                    {
                        maximumWindowRms = rms;
                        maximumWindowStart = start;
                    }
                }
                else
                {
                    minimumTailWindowRms = Math.Min(minimumTailWindowRms, rms);
                }
            }

            Assert.IsTrue(minimumWindowRms > 0.2f, $"The stretched tone contains a low-energy break at {minimumWindowStart / (double)sampleRate:F3}s: minimum window RMS {minimumWindowRms:F4}.");
            Assert.IsTrue(maximumWindowRms / minimumWindowRms < 1.5f, $"The stretched tone pulses between chunks: RMS {minimumWindowRms:F4} at {minimumWindowStart / (double)sampleRate:F3}s .. {maximumWindowRms:F4} at {maximumWindowStart / (double)sampleRate:F3}s.");
            Assert.IsTrue(minimumTailWindowRms > 0.2f, $"The stretched tone fades into a low-energy break near the clip end: minimum tail window RMS {minimumTailWindowRms:F4}.");
        }

        [DataTestMethod]
        [DataRow(0.5, 4096)]
        [DataRow(0.5, 8192)]
        [DataRow(0.25, 4096)]
        [DataRow(0.25, 8192)]
        [DataRow(0.5, 8192, false)]
        [DataRow(0.25, 8192, false)]
        public async Task TimeStretchAllThreadsAsync_FastStereoFactorsKeepExactDurationAndSourceTail(double factor, int chunkSize, bool channeled = true)
        {
            const int sampleRate = 44100;
            const int sourceFrames = 8114;
            float[] sourceData = new float[sourceFrames * 2];
            for (int frame = 0; frame < sourceFrames; frame++)
            {
                float value = frame < sourceFrames / 2 ? 0.05f : 0.5f;
                sourceData[frame * 2] = value;
                sourceData[frame * 2 + 1] = value;
            }

            using AudioObj audio = new()
            {
                Data = sourceData,
                SampleRate = sampleRate,
                Channels = 2,
                Duration = TimeSpan.FromSeconds(sourceFrames / (double)sampleRate),
                Length = sourceData.Length
            };

            await TimeStretcher.TimeStretchAllThreadsAsync(
                audio,
                chunkSize: chunkSize,
                overlap: 0.5f,
                factor: factor,
                keepData: false,
                normalize: 0f,
                maxWorkers: 2,
                channeled: channeled);

            int expectedFrames = (int)Math.Round(sourceFrames * factor, MidpointRounding.AwayFromZero);
            Assert.AreEqual(expectedFrames * 2, audio.Data.Length);
            Assert.AreEqual(TimeSpan.FromSeconds(expectedFrames / (double)sampleRate), audio.Duration);

            int tailFrames = Math.Min(256, expectedFrames);
            double tailSumSquares = 0;
            for (int frame = expectedFrames - tailFrames; frame < expectedFrames; frame++)
            {
                tailSumSquares += audio.Data[frame * 2] * audio.Data[frame * 2];
                tailSumSquares += audio.Data[frame * 2 + 1] * audio.Data[frame * 2 + 1];
            }

            float tailRms = (float)Math.Sqrt(tailSumSquares / (tailFrames * 2));
            Assert.IsTrue(tailRms > 0.2f, $"Expected late source audio to remain present, got tail RMS {tailRms:F4} at factor {factor} with chunk size {chunkSize}.");
        }
    }
}
