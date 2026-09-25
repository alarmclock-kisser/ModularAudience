using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio;
using ModularAudience.Audio.Processors_V1;

namespace ModularAudience.Audio.Tests
{
    [TestClass]
    public sealed class TimeStretcherV1Tests
    {
        [TestMethod]
        public async Task TimeStretchAllThreadsAsync_FastPathNormalizesToRequestedPeak()
        {
            const int sampleRate = 44100;
            float[] sourceData = Enumerable.Repeat(0.25f, 8192).ToArray();
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
                chunkSize: 1024,
                overlap: 0.5f,
                factor: 0.5,
                normalize: 0.75f,
                maxWorkers: 2);

            Assert.AreEqual(0.75f, audio.Data.Max(Math.Abs), 0.0001f);
        }

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

        [TestMethod]
        public async Task TimeStretchAllThreadsAsync_HalfSpeedTransientRetainsDurationAndPeak()
        {
            const int sampleRate = 44100;
            float[] sourceData = new float[sampleRate / 5];
            for (int frame = 0; frame < sourceData.Length; frame++)
            {
                float envelope = MathF.Exp(-frame / (sampleRate * 0.015f));
                sourceData[frame] = 0.8f * envelope * MathF.Sin(2f * MathF.PI * 2400f * frame / sampleRate);
            }

            float sourcePeak = sourceData.Max(Math.Abs);
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
                chunkSize: 4096,
                overlap: 0.5f,
                factor: 0.5,
                normalize: 0f,
                maxWorkers: 2,
                channeled: true);

            float outputPeak = audio.Data.Max(Math.Abs);
            Assert.AreEqual(0.1, audio.Duration.TotalSeconds, 1.0 / sampleRate);
            Assert.IsTrue(outputPeak >= sourcePeak * 0.85f, $"Expected transient peak {sourcePeak:F4} to be preserved, got {outputPeak:F4}.");

            float earlyRms = GetWindowRms(audio.Data, 0, sampleRate / 200);
            float lateRms = GetWindowRms(audio.Data, sampleRate / 20, sampleRate * 9 / 100);
            Assert.IsTrue(lateRms < earlyRms * 0.05f, $"Expected the transient decay to compress with the signal, got early RMS {earlyRms:F4} and late RMS {lateRms:F4}.");
        }

        [TestMethod]
        public async Task TimeStretchAllThreadsAsync_ChanneledStretchDoesNotLeakBetweenStereoChannels()
        {
            const int sampleRate = 44100;
            const int sourceFrames = sampleRate / 5;
            float[] sourceData = new float[sourceFrames * 2];
            for (int frame = 0; frame < sourceFrames; frame++)
            {
                float envelope = MathF.Exp(-frame / (sampleRate * 0.015f));
                sourceData[frame * 2] = 0.8f * envelope * MathF.Sin(2f * MathF.PI * 440f * frame / sampleRate);
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
                chunkSize: 4096,
                overlap: 0.5f,
                factor: 0.5,
                normalize: 0f,
                maxWorkers: 2,
                channeled: true);

            float rightChannelPeak = audio.Data
                .Where((_, index) => index % 2 == 1)
                .Max(Math.Abs);
            Assert.IsTrue(rightChannelPeak < 0.001f, $"Expected silence to remain in the right channel, got peak {rightChannelPeak:F4}.");
        }

        [TestMethod]
        public async Task TimeStretchAllThreadsAsync_HalfSpeedKeepsMidClipSnareDecayForward()
        {
            const int sampleRate = 44100;
            const int sourceFrames = sampleRate * 5;
            const int snareStartFrame = sampleRate * 5 / 2;
            const int snareFrames = sampleRate * 18 / 100;
            float[] sourceData = new float[sourceFrames];
            for (int frame = 0; frame < snareFrames; frame++)
            {
                float envelope = MathF.Exp(-frame / (sampleRate * 0.06f));
                float body = MathF.Sin(2f * MathF.PI * 110f * frame / sampleRate);
                float snap = MathF.Sin(2f * MathF.PI * 3100f * frame / sampleRate);
                sourceData[snareStartFrame + frame] = envelope * (0.65f * body + 0.35f * snap);
            }

            using AudioObj audio = new()
            {
                Data = sourceData,
                SampleRate = sampleRate,
                Channels = 1,
                Duration = TimeSpan.FromSeconds(5),
                Length = sourceData.Length
            };

            await TimeStretcher.TimeStretchAllThreadsAsync(
                audio,
                chunkSize: 4096,
                overlap: 0.5f,
                factor: 0.5,
                normalize: 0f,
                maxWorkers: 2,
                channeled: true);

            int outputSnareStartFrame = snareStartFrame / 2;
            float earlyRms = GetWindowRms(audio.Data, outputSnareStartFrame + sampleRate / 100, outputSnareStartFrame + sampleRate * 2 / 100);
            float lateRms = GetWindowRms(audio.Data, outputSnareStartFrame + sampleRate * 7 / 100, outputSnareStartFrame + sampleRate * 8 / 100);

            Assert.AreEqual(2.5, audio.Duration.TotalSeconds, 1.0 / sampleRate);
            Assert.IsTrue(earlyRms > 0.05f, $"Expected the stretched snare attack near the mapped onset, got RMS {earlyRms:F4}.");
            Assert.IsTrue(lateRms < earlyRms * 0.5f, $"Expected a forward-decaying snare tail, got early RMS {earlyRms:F4} and late RMS {lateRms:F4}.");
        }

        [TestMethod]
        public async Task TimeStretchAllThreadsAsync_ChanneledStretchKeepsStereoSignalsIndependent()
        {
            const int sampleRate = 44100;
            const int sourceFrames = sampleRate / 2;
            float[] sourceData = new float[sourceFrames * 2];
            for (int frame = 0; frame < sourceFrames; frame++)
            {
                sourceData[frame * 2] = 0.6f * MathF.Sin(2f * MathF.PI * 440f * frame / sampleRate);
                sourceData[frame * 2 + 1] = 0.6f * MathF.Sin(2f * MathF.PI * 1760f * frame / sampleRate);
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
                chunkSize: 4096,
                overlap: 0.5f,
                factor: 0.5,
                normalize: 0f,
                maxWorkers: 2,
                channeled: true);

            const int startFrame = sampleRate / 10;
            const int frameCount = sampleRate / 10;
            float leftAt440 = MeasureToneAmplitude(audio.Data, 2, 0, 440, startFrame, frameCount, sampleRate);
            float leftAt1760 = MeasureToneAmplitude(audio.Data, 2, 0, 1760, startFrame, frameCount, sampleRate);
            float rightAt440 = MeasureToneAmplitude(audio.Data, 2, 1, 440, startFrame, frameCount, sampleRate);
            float rightAt1760 = MeasureToneAmplitude(audio.Data, 2, 1, 1760, startFrame, frameCount, sampleRate);

            Assert.IsTrue(leftAt440 > leftAt1760 * 5f, $"Left channel tone leaked into its other frequency: 440 Hz {leftAt440:F4}, 1760 Hz {leftAt1760:F4}.");
            Assert.IsTrue(rightAt1760 > rightAt440 * 5f, $"Right channel tone leaked into its other frequency: 1760 Hz {rightAt1760:F4}, 440 Hz {rightAt440:F4}.");
        }

        private static float GetWindowRms(float[] samples, int startFrame, int endFrame)
        {
            double sumSquares = 0;
            for (int frame = startFrame; frame < Math.Min(samples.Length, endFrame); frame++)
            {
                sumSquares += samples[frame] * samples[frame];
            }

            return (float)Math.Sqrt(sumSquares / Math.Max(1, endFrame - startFrame));
        }

        private static float MeasureToneAmplitude(float[] samples, int channels, int channel, int frequency, int startFrame, int frameCount, int sampleRate)
        {
            double sineSum = 0;
            double cosineSum = 0;
            for (int offset = 0; offset < frameCount; offset++)
            {
                int frame = startFrame + offset;
                double phase = 2.0 * Math.PI * frequency * frame / sampleRate;
                float value = samples[(frame * channels) + channel];
                sineSum += value * Math.Sin(phase);
                cosineSum += value * Math.Cos(phase);
            }

            return (float)(2.0 * Math.Sqrt((sineSum * sineSum) + (cosineSum * cosineSum)) / frameCount);
        }
    }
}
