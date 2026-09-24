using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio;
using ModularAudience.Audio.Processors_V3;
using System.Linq;

namespace ModularAudience.Audio.Tests
{
    [TestClass]
    public class BeatGridV3Tests
    {
        [TestMethod]
        public async Task UsesBpmAndVirtualTrimWithoutChangingAudio()
        {
            AudioObj audio = CreatePulseAudio(bpm: 120f, scannedBpm: 90f);
            float[] originalData = audio.Data;

            bool[] grid = await BeatGridFinder_V3.GenerateBeatGridAsync(audio, set: false);
            int[] frames = GetGridFrames(grid);

            Assert.AreEqual(8000, audio.Data.Length);
            Assert.AreSame(originalData, audio.Data);
            Assert.IsTrue(frames.Length >= 8);
            // The pulse starts at beatFrame (1500), the valley is before it with a phase offset.
            Assert.IsTrue(frames[0] >= 1460 && frames[0] <= 1500, $"Unexpected first beat frame: {frames[0]}");
            Assert.IsTrue(grid.Take(1400).All(value => !value));
            Assert.IsTrue(grid.Skip(6700).All(value => !value));

            // Gaps should be close to the beat interval (500 frames), allowing for phase offset.
            for (int index = 1; index < frames.Length; index++)
            {
                int gap = frames[index] - frames[index - 1];
                Assert.IsTrue(gap >= 480 && gap <= 520, $"Unexpected gap: {gap} between frames {frames[index - 1]} and {frames[index]}");
            }
        }

        [TestMethod]
        public async Task FollowsLocalAudioShiftInsteadOfForcingBpmPosition()
        {
            AudioObj audio = CreatePulseAudio(bpm: 120f, scannedBpm: 0f, shiftedBeatFrame: 2020);

            bool[] grid = await BeatGridFinder_V3.GenerateBeatGridAsync(audio, set: false);
            int[] frames = GetGridFrames(grid);

            // The shifted pulse starts at 2020, the valley is before it (around 1985).
            Assert.IsTrue(frames.Any(frame => frame >= 1970 && frame <= 2000), $"No locally shifted beat found: {string.Join(", ", frames)}");
            Assert.IsFalse(frames.Contains(2030), "The shifted beat was forced back onto the rigid BPM position.");
        }

        [TestMethod]
        public async Task UsesEnergyBoundaryBeforeLaterBodyPeak()
        {
            AudioObj audio = CreateAttackAudio();

            bool[] grid = await BeatGridFinder_V3.GenerateBeatGridAsync(audio, set: false);
            int[] frames = GetGridFrames(grid);
            int markerNearSecondBeat = frames.OrderBy(frame => Math.Abs(frame - 2000)).First();

            // The attack starts at beatFrame (2000), the valley is before it with a small phase offset.
            Assert.IsTrue(markerNearSecondBeat >= 1980 && markerNearSecondBeat <= 2010,
                $"Beat marker did not stay at the valley before the attack: {markerNearSecondBeat}");
            Assert.IsFalse(frames.Contains(2035), "The beat marker followed the later body peak.");
        }

        [TestMethod]
        public async Task UsesScannedBpmWhenAudioBpmIsUnavailable()
        {
            AudioObj audio = CreatePulseAudio(bpm: 0f, scannedBpm: 120f);

            bool[] grid = await BeatGridFinder_V3.GenerateBeatGridAsync(audio, set: false);
            int[] frames = GetGridFrames(grid);

            Assert.IsTrue(frames.Length >= 8);
            // Gaps should be close to the beat interval (500 frames), allowing for phase offset.
            int gap = frames[1] - frames[0];
            Assert.IsTrue(gap >= 480 && gap <= 520, $"Unexpected gap: {gap}");
        }

        [TestMethod]
        public async Task DetectsBpmWhenNoBpmMetadataExists()
        {
            AudioObj audio = CreatePulseAudio(bpm: 0f, scannedBpm: 0f);

            bool[] grid = await BeatGridFinder_V3.GenerateBeatGridAsync(audio, set: false);
            int[] frames = GetGridFrames(grid);

            Assert.IsTrue(frames.Length >= 8);
            // The pulse starts at beatFrame (1500), the valley is before it with a phase offset.
            Assert.IsTrue(frames[0] >= 1460 && frames[0] <= 1500, $"Unexpected first beat frame: {frames[0]}");
            Assert.IsTrue(frames.Skip(1).Zip(frames, (current, previous) => current - previous).All(gap => gap >= 480 && gap <= 520));
        }

        private static AudioObj CreatePulseAudio(float bpm, float scannedBpm, int shiftedBeatFrame = -1)
        {
            const int sampleRate = 1000;
            const int totalFrames = 8000;
            const int firstBeatFrame = 1500;
            const int lastBeatFrame = 6500;
            const int beatIntervalFrames = 500;
            const int pulseWidthFrames = 20;

            var data = new float[totalFrames];
            for (int expectedBeatFrame = firstBeatFrame; expectedBeatFrame <= lastBeatFrame; expectedBeatFrame += beatIntervalFrames)
            {
                int actualBeatFrame = expectedBeatFrame == 2000 && shiftedBeatFrame >= 0
                    ? shiftedBeatFrame
                    : expectedBeatFrame;
                for (int offset = 0; offset < pulseWidthFrames; offset++)
                {
                    data[actualBeatFrame + offset] = 0.8f * (1f - offset / (float)pulseWidthFrames);
                }
            }

            return new AudioObj
            {
                Data = data,
                SampleRate = sampleRate,
                Channels = 1,
                Bpm = bpm,
                ScannedBpm = scannedBpm
            };
        }

        private static AudioObj CreateAttackAudio()
        {
            const int sampleRate = 1000;
            const int totalFrames = 8000;
            const int firstBeatFrame = 1500;
            const int lastBeatFrame = 6500;
            const int beatIntervalFrames = 500;

            var data = new float[totalFrames];
            for (int beatFrame = firstBeatFrame; beatFrame <= lastBeatFrame; beatFrame += beatIntervalFrames)
            {
                for (int offset = 0; offset < 80; offset++)
                {
                    float amplitude = offset < 20
                        ? 0.05f
                        : offset < 35
                            ? 0.25f
                            : offset < 40
                                ? 0.9f
                                : 0.7f;
                    data[beatFrame + offset] = amplitude;
                }
            }

            return new AudioObj
            {
                Data = data,
                SampleRate = sampleRate,
                Channels = 1,
                Bpm = 120f
            };
        }

        private static int[] GetGridFrames(bool[] grid)
        {
            return Enumerable.Range(0, grid.Length)
                .Where(index => grid[index])
                .ToArray();
        }
    }
}