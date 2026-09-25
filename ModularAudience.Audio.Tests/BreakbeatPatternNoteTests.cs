using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio;
using ModularAudience.Generators;

namespace ModularAudience.Audio.Tests
{
    [TestClass]
    public sealed class BreakbeatPatternNoteTests
    {
        [TestMethod]
        public void CreatePatternNotes_PadsIncompatibleSampleLengthsToCommonHitUnits()
        {
            List<AudioObj> samples =
            [
                CreateSample(250),
                CreateSample(400),
                CreateSample(500)
            ];

            try
            {
                List<bool[]> pattern = Enumerable.Range(0, samples.Count)
                    .Select(_ => new[] { true, false, false, false })
                    .ToList();

                List<BreakbeatPatternNote> notes = BreakbeatGenerator_V2.CreatePatternNotesFromGrid(
                    pattern,
                    samples,
                    sourceResolution: 4,
                    bpm: 60,
                    gridResolution: 16);

                Assert.AreEqual(64, notes[0].DurationTicks);
                Assert.AreEqual(128, notes[1].DurationTicks);
                Assert.AreEqual(128, notes[2].DurationTicks);
                Assert.IsFalse(notes[1].TimeStretch);
            }
            finally
            {
                foreach (AudioObj sample in samples)
                {
                    sample.Dispose();
                }
            }
        }

        [TestMethod]
        public void CreatePatternNotes_UsesFourStepEditorGridWithoutMovingSourceHits()
        {
            using AudioObj sample = CreateSample(250);
            bool[] sourcePattern = new bool[16];
            sourcePattern[1] = true;

            List<BreakbeatPatternNote> notes = BreakbeatGenerator_V2.CreatePatternNotesFromGrid(
                [sourcePattern],
                [sample],
                sourceResolution: 16,
                bpm: 60,
                gridResolution: 4);

            Assert.AreEqual(1, notes.Count);
            Assert.AreEqual(64, notes[0].StartTick);
            Assert.AreEqual(256, notes[0].DurationTicks);
        }

        [DataTestMethod]
        [DataRow(128)]
        [DataRow(256)]
        public void RemoveRetriggersCoveredByStretchedNote_RemovesOnlyHitsOnSameTrackInsideDuration(int stretchedDurationTicks)
        {
            BreakbeatPatternNote earlierHit = new(TrackIndex: 0, StartTick: 0, DurationTicks: 64);
            BreakbeatPatternNote stretchedNote = new(TrackIndex: 0, StartTick: 64, DurationTicks: stretchedDurationTicks, TimeStretch: true);
            BreakbeatPatternNote coveredHit = new(TrackIndex: 0, StartTick: 128, DurationTicks: 64);
            BreakbeatPatternNote hitAtEnd = new(TrackIndex: 0, StartTick: 64 + stretchedDurationTicks, DurationTicks: 64);
            BreakbeatPatternNote otherTrackHit = new(TrackIndex: 1, StartTick: 128, DurationTicks: 64);

            List<BreakbeatPatternNote> result = BreakbeatGenerator_V2.RemoveRetriggersCoveredByStretchedNote(
                [earlierHit, stretchedNote, coveredHit, hitAtEnd, otherTrackHit],
                stretchedNote);

            CollectionAssert.AreEqual(new[] { earlierHit, stretchedNote, hitAtEnd, otherTrackHit }, result);
        }

        [TestMethod]
        public void RemoveRetriggersCoveredByStretchedNote_UsesLengthNotVarispeedMode()
        {
            BreakbeatPatternNote extendedVarispeed = new(
                TrackIndex: 0,
                StartTick: 0,
                DurationTicks: 128,
                Varispeed: true,
                ManuallyResized: true,
                OriginalDurationTicks: 64);
            BreakbeatPatternNote coveredHit = new(TrackIndex: 0, StartTick: 64, DurationTicks: 32);
            BreakbeatPatternNote shortenedVarispeed = extendedVarispeed with { DurationTicks = 32 };

            List<BreakbeatPatternNote> extendedResult = BreakbeatGenerator_V2.RemoveRetriggersCoveredByStretchedNote(
                [extendedVarispeed, coveredHit],
                extendedVarispeed);
            List<BreakbeatPatternNote> shortenedResult = BreakbeatGenerator_V2.RemoveRetriggersCoveredByStretchedNote(
                [shortenedVarispeed, coveredHit],
                shortenedVarispeed);

            CollectionAssert.AreEqual(new[] { extendedVarispeed }, extendedResult);
            CollectionAssert.AreEqual(new[] { shortenedVarispeed, coveredHit }, shortenedResult);
        }

        [TestMethod]
        public async Task RenderPatternNotesAsync_StretchesShortHitThroughRequestedTail()
        {
            using AudioObj sample = CreateToneSample(sampleRate: 22050, frequency: 440, durationSeconds: 0.25);
            List<AudioObj> samples = [sample];
            BreakbeatPatternNote note = new(TrackIndex: 0, StartTick: 0, DurationTicks: 512, TimeStretch: true);

            using AudioObj rendered = await BreakbeatGenerator_V2.RenderPatternNotesAsync(
                [note],
                samples,
                bars: 1,
                bpm: 60,
                resolution: 4,
                swing: 0);

            float peak = rendered.Data.Max(Math.Abs);
            float lateRms = MeasureRms(rendered, 0.45, 0.49);

            Assert.IsTrue(peak > 0.1f, "Stretched audio should remain audible across the extended note.");
            Assert.IsTrue(lateRms > 0.01f, $"Expected audio through the stretched tail, got RMS {lateRms:F4}.");
        }

        [TestMethod]
        public async Task RenderPatternNotesAsync_VarispeedLengtheningLowersPitchAndShorteningRaisesPitch()
        {
            using AudioObj sample = CreateToneSample(sampleRate: 44100, frequency: 440, durationSeconds: 0.25);
            List<AudioObj> samples = [sample];
            BreakbeatPatternNote lengthened = new(
                TrackIndex: 0,
                StartTick: 0,
                DurationTicks: 512,
                Varispeed: true,
                ManuallyResized: true,
                OriginalDurationTicks: 256);
            BreakbeatPatternNote shortened = lengthened with { DurationTicks = 128 };

            using AudioObj slowRendered = await BreakbeatGenerator_V2.RenderPatternNotesAsync(
                [lengthened], samples, bars: 1, bpm: 60, resolution: 4, swing: 0);
            using AudioObj fastRendered = await BreakbeatGenerator_V2.RenderPatternNotesAsync(
                [shortened], samples, bars: 1, bpm: 60, resolution: 4, swing: 0);

            Assert.IsTrue(MeasureToneAmplitude(slowRendered, 220, 0.05, 0.45) > MeasureToneAmplitude(slowRendered, 440, 0.05, 0.45) * 5f);
            Assert.IsTrue(MeasureToneAmplitude(fastRendered, 880, 0.02, 0.11) > MeasureToneAmplitude(fastRendered, 440, 0.02, 0.11) * 5f);
        }

        [TestMethod]
        public async Task RenderPatternNotesAsync_CustomPitchAddsToVarispeedPitchWhileKeepingItsDuration()
        {
            using AudioObj sample = CreateToneSample(sampleRate: 44100, frequency: 440, durationSeconds: 0.25);
            BreakbeatPatternNote note = new(
                TrackIndex: 0,
                StartTick: 0,
                DurationTicks: 512,
                Varispeed: true,
                ManuallyResized: true,
                OriginalDurationTicks: 256,
                PitchSemitones: 12f);

            using AudioObj rendered = await BreakbeatGenerator_V2.RenderPatternNotesAsync(
                [note], [sample], bars: 1, bpm: 60, resolution: 4, swing: 0);

            float restoredPitch = MeasureToneAmplitude(rendered, 440, 0.05, 0.45);
            float varispeedPitch = MeasureToneAmplitude(rendered, 220, 0.05, 0.45);
            Assert.IsTrue(restoredPitch > varispeedPitch * 4f,
                $"Expected the custom octave shift to add to Varispeed, got 440 Hz amplitude {restoredPitch:F4} and 220 Hz amplitude {varispeedPitch:F4}.");
        }

        [TestMethod]
        public async Task RenderPatternNotesAsync_VarispeedPreservesStereoChannelSeparation()
        {
            const int sampleRate = 44100;
            const int sourceFrames = sampleRate / 4;
            float[] data = new float[sourceFrames * 2];
            for (int frame = 0; frame < sourceFrames; frame++)
            {
                data[frame * 2] = 0.5f * MathF.Sin(2f * MathF.PI * 440f * frame / sampleRate);
                data[frame * 2 + 1] = 0.5f * MathF.Sin(2f * MathF.PI * 880f * frame / sampleRate);
            }

            using AudioObj sample = new()
            {
                Data = data,
                SampleRate = sampleRate,
                Channels = 2,
                Duration = TimeSpan.FromSeconds(sourceFrames / (double)sampleRate),
                Length = data.Length
            };
            BreakbeatPatternNote note = new(
                TrackIndex: 0,
                StartTick: 0,
                DurationTicks: 512,
                Varispeed: true,
                ManuallyResized: true,
                OriginalDurationTicks: 256);

            using AudioObj rendered = await BreakbeatGenerator_V2.RenderPatternNotesAsync(
                [note], [sample], bars: 1, bpm: 60, resolution: 4, swing: 0);

            Assert.IsTrue(MeasureToneAmplitude(rendered, 220, 0.05, 0.45, channel: 0) > MeasureToneAmplitude(rendered, 440, 0.05, 0.45, channel: 0) * 5f);
            Assert.IsTrue(MeasureToneAmplitude(rendered, 440, 0.05, 0.45, channel: 1) > MeasureToneAmplitude(rendered, 880, 0.05, 0.45, channel: 1) * 5f);
        }

        [TestMethod]
        public void ExtractPatternBars_ConcatenatesOnlySelectedBarsInTimelineOrder()
        {
            const int sampleRate = 4;
            const int channels = 2;
            float[] data = Enumerable.Range(0, sampleRate * 4 * 5 * channels).Select(value => (float)value).ToArray();
            using AudioObj source = new()
            {
                Data = data,
                SampleRate = sampleRate,
                Channels = channels,
                Duration = TimeSpan.FromSeconds(data.Length / (double)(sampleRate * channels)),
                Length = data.Length
            };

            using AudioObj selected = BreakbeatGenerator_V2.ExtractPatternBars(source, [4, 0, 2], bpm: 60);

            float[] expected = data
                .Take(sampleRate * 4 * channels)
                .Concat(data.Skip(sampleRate * 4 * 2 * channels).Take(sampleRate * 4 * channels))
                .Concat(data.Skip(sampleRate * 4 * 4 * channels).Take(sampleRate * 4 * channels))
                .ToArray();
            CollectionAssert.AreEqual(expected, selected.Data);
            Assert.AreEqual(TimeSpan.FromSeconds(12), selected.Duration);
        }

        [TestMethod]
        public async Task RenderPatternNotesAsync_StretchedTransientRetainsNormalizedPeak()
        {
            const int sampleRate = 44100;
            float[] transientData = new float[sampleRate / 5];
            for (int frame = 0; frame < transientData.Length; frame++)
            {
                float envelope = MathF.Exp(-frame / (sampleRate * 0.015f));
                transientData[frame] = envelope * MathF.Sin(2f * MathF.PI * 2400f * frame / sampleRate);
            }

            using AudioObj shortestSample = CreateToneSample(sampleRate, 440, 0.125);
            using AudioObj transientSample = new()
            {
                Data = transientData,
                SampleRate = sampleRate,
                Channels = 1,
                Duration = TimeSpan.FromSeconds(transientData.Length / (double)sampleRate),
                Length = transientData.Length
            };
            BreakbeatPatternNote note = new(TrackIndex: 1, StartTick: 0, DurationTicks: 512, TimeStretch: true);

            using AudioObj rendered = await BreakbeatGenerator_V2.RenderPatternNotesAsync(
                [note],
                [shortestSample, transientSample],
                bars: 1,
                bpm: 60,
                resolution: 4,
                swing: 0);

            float peak = rendered.Data.Max(Math.Abs);
            Assert.IsTrue(peak > 0.7f, $"Expected the stretched transient to retain the normalized 0.8 peak, got {peak:F4}.");
        }

        [DataTestMethod]
        [DataRow(512, 0.3, 0.38, 0.45, 0.49)]
        [DataRow(1024, 0.6, 0.75, 0.9, 0.98)]
        public async Task RenderPatternNotesAsync_StretchesBeatNormalizedSampleIncludingPaddedSilence(
            int durationTicks,
            double bodyStartSeconds,
            double bodyEndSeconds,
            double tailStartSeconds,
            double tailEndSeconds)
        {
            using AudioObj shortestSample = CreateToneSample(sampleRate: 44100, frequency: 440, durationSeconds: 0.125);
            using AudioObj sample = CreateToneSample(sampleRate: 44100, frequency: 440, durationSeconds: 0.2);
            List<AudioObj> samples = [shortestSample, sample];
            BreakbeatPatternNote note = new(TrackIndex: 1, StartTick: 0, DurationTicks: durationTicks, TimeStretch: true);

            using AudioObj rendered = await BreakbeatGenerator_V2.RenderPatternNotesAsync(
                [note],
                samples,
                bars: 1,
                bpm: 60,
                resolution: 4,
                swing: 0);

            float earlyRms = MeasureRms(rendered, bodyStartSeconds, bodyEndSeconds);
            float lateRms = MeasureRms(rendered, tailStartSeconds, tailEndSeconds);

            Assert.IsTrue(earlyRms > 0.1f, $"Expected the normalized sample body to remain audible, got RMS {earlyRms:F4}.");
            Assert.IsTrue(lateRms < earlyRms * 0.1f, $"Expected padded source silence to stretch into the note tail, got RMS {lateRms:F4} versus body RMS {earlyRms:F4}.");
        }

        private static AudioObj CreateSample(int durationMilliseconds)
        {
            const int sampleRate = 1000;
            return new AudioObj
            {
                Data = new float[durationMilliseconds],
                SampleRate = sampleRate,
                Channels = 1,
                Duration = TimeSpan.FromMilliseconds(durationMilliseconds),
                Length = durationMilliseconds
            };
        }

        private static AudioObj CreateToneSample(int sampleRate, int frequency, double durationSeconds)
        {
            int frameCount = (int)(sampleRate * durationSeconds);
            float[] data = new float[frameCount];
            for (int frame = 0; frame < frameCount; frame++)
            {
                data[frame] = 0.5f * MathF.Sin(2f * MathF.PI * frequency * frame / sampleRate);
            }

            return new AudioObj
            {
                Data = data,
                SampleRate = sampleRate,
                Channels = 1,
                Duration = TimeSpan.FromSeconds(durationSeconds),
                Length = data.Length
            };
        }

        private static float MeasureRms(AudioObj audio, double startSeconds, double endSeconds)
        {
            int firstFrame = (int)(startSeconds * audio.SampleRate);
            int lastFrame = (int)(endSeconds * audio.SampleRate);
            double sumSquares = 0;
            for (int frame = firstFrame; frame < lastFrame; frame++)
            {
                float sample = audio.Data[frame * audio.Channels];
                sumSquares += sample * sample;
            }

            return (float)Math.Sqrt(sumSquares / Math.Max(1, lastFrame - firstFrame));
        }

        private static float MeasureToneAmplitude(AudioObj audio, int frequency, double startSeconds, double endSeconds, int channel = 0)
        {
            int firstFrame = (int)(startSeconds * audio.SampleRate);
            int lastFrame = Math.Min((int)(endSeconds * audio.SampleRate), audio.Data.Length / audio.Channels);
            double sineSum = 0;
            double cosineSum = 0;
            for (int frame = firstFrame; frame < lastFrame; frame++)
            {
                double phase = 2.0 * Math.PI * frequency * frame / audio.SampleRate;
                float value = audio.Data[frame * audio.Channels + channel];
                sineSum += value * Math.Sin(phase);
                cosineSum += value * Math.Cos(phase);
            }

            return (float)(2.0 * Math.Sqrt(sineSum * sineSum + cosineSum * cosineSum) / Math.Max(1, lastFrame - firstFrame));
        }
    }
}