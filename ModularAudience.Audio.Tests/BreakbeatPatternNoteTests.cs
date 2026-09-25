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

        [TestMethod]
        public void RemoveRetriggersCoveredByStretchedNote_RemovesOnlyHitsOnSameTrackInsideDuration()
        {
            BreakbeatPatternNote earlierHit = new(TrackIndex: 0, StartTick: 0, DurationTicks: 64);
            BreakbeatPatternNote stretchedNote = new(TrackIndex: 0, StartTick: 64, DurationTicks: 256, TimeStretch: true);
            BreakbeatPatternNote coveredHit = new(TrackIndex: 0, StartTick: 128, DurationTicks: 64);
            BreakbeatPatternNote hitAtEnd = new(TrackIndex: 0, StartTick: 320, DurationTicks: 64);
            BreakbeatPatternNote otherTrackHit = new(TrackIndex: 1, StartTick: 128, DurationTicks: 64);

            List<BreakbeatPatternNote> result = BreakbeatGenerator_V2.RemoveRetriggersCoveredByStretchedNote(
                [earlierHit, stretchedNote, coveredHit, hitAtEnd, otherTrackHit],
                stretchedNote);

            CollectionAssert.AreEqual(new[] { earlierHit, stretchedNote, hitAtEnd, otherTrackHit }, result);
        }

        [TestMethod]
        public async Task RenderPatternNotesAsync_StretchesShortHitThroughRequestedTail()
        {
            using AudioObj sample = CreateToneSample(sampleRate: 22050, frequency: 440, durationSeconds: 0.25);
            List<AudioObj> samples = [sample];
            BreakbeatPatternNote note = new(TrackIndex: 0, StartTick: 0, DurationTicks: 128, TimeStretch: true);

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
        public async Task RenderPatternNotesAsync_StretchesBeatNormalizedSampleIncludingPaddedSilence()
        {
            using AudioObj shortestSample = CreateToneSample(sampleRate: 44100, frequency: 440, durationSeconds: 0.125);
            using AudioObj sample = CreateToneSample(sampleRate: 44100, frequency: 440, durationSeconds: 0.2);
            List<AudioObj> samples = [shortestSample, sample];
            BreakbeatPatternNote note = new(TrackIndex: 1, StartTick: 0, DurationTicks: 128, TimeStretch: true);

            using AudioObj rendered = await BreakbeatGenerator_V2.RenderPatternNotesAsync(
                [note],
                samples,
                bars: 1,
                bpm: 60,
                resolution: 4,
                swing: 0);

            float earlyRms = MeasureRms(rendered, 0.3, 0.38);
            float lateRms = MeasureRms(rendered, 0.45, 0.49);

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
    }
}