using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Processors_V4;

namespace ModularAudience.Audio.Tests
{
    [TestClass]
    public sealed class LoopAtomizerTransientTests
    {
        private static readonly LoopAtomizerSettings Settings = LoopAtomizerSettings.Default with { EnableDeduplication = false };

        [TestMethod]
        [DataRow(16000, 16)]
        [DataRow(16000, 24)]
        [DataRow(16000, 32)]
        [DataRow(44100, 24)]
        [DataRow(48000, 20)]
        [DataRow(48000, 32)]
        public async Task SeparatesRapidElectronicHits(int sampleRate, int spacingMs)
        {
            using AudioTestScope scope = new();
            var hits = Enumerable.Range(0, 5).Select(index => (
                StartMs: 100.0 + (index * spacingMs),
                Samples: AudioTestData.Hit(sampleRate, Math.Min(12.0, spacingMs * 0.5), 650 + (index * 170), 2.5),
                Gain: index == 2 ? 0.55f : 1f)).ToArray();
            AudioObj source = scope.Create(AudioTestData.Track(sampleRate, 400, hits), sampleRate);
            LoopAtomizerResult result = await LoopAtomizer_V4.AtomizeAsync(source, Settings);
            scope.Own(result.Atomics);
            Assert.AreEqual(hits.Length, result.Atomics.Count, Describe(result.Atomics));
            for (int i = 0; i < result.Atomics.Count - 1; i++)
            {
                int nextStart = (int) (hits[i + 1].StartMs * sampleRate / 1000);
                AssertEndsBefore(source, result.Atomics[i], nextStart);
            }
        }

        [TestMethod]
        [DataRow(16000, 3)]
        [DataRow(16000, 8)]
        [DataRow(44100, 3)]
        [DataRow(44100, 8)]
        [DataRow(48000, 3)]
        [DataRow(48000, 8)]
        public async Task DoesNotIncludeTheNextAttackAtTheTail(int sampleRate, int attackMs)
        {
            using AudioTestScope scope = new();
            const double nextStartMs = 240.375;
            float[] first = AudioTestData.Hit(sampleRate, 120, 110, 22);
            float[] second = AudioTestData.Tone(sampleRate, 65, 750, attackMs, 35);
            AudioObj source = scope.Create(AudioTestData.Track(sampleRate, 450,
                (100, first, 1f), (nextStartMs, second, 1f)), sampleRate);
            LoopAtomizerResult result = await LoopAtomizer_V4.AtomizeAsync(source, Settings);
            scope.Own(result.Atomics);
            Assert.AreEqual(2, result.Atomics.Count, Describe(result.Atomics));
            int nextStart = (int) (nextStartMs * sampleRate / 1000);
            AssertEndsBefore(source, result.Atomics[0], nextStart);
            int secondStart = FindSourceStart(source, result.Atomics[1]);
            Assert.IsTrue(secondStart <= nextStart + Math.Max(1, sampleRate / 4000),
                $"The next attack was truncated by {secondStart - nextStart} frames.");
        }

        [TestMethod]
        [DataRow(16000)]
        [DataRow(44100)]
        [DataRow(48000)]
        public async Task CutsBeforeAnAttackOverAnExistingDecay(int sampleRate)
        {
            for (int phase = 0; phase < 16; phase++)
            {
                using AudioTestScope scope = new();
                double nextStartMs = 180.0 + (phase * 0.75);
                float[] first = AudioTestData.Hit(sampleRate, 300, 70, 95);
                float[] second = AudioTestData.Tone(sampleRate, 65, 960, 5, 35);
                AudioObj source = scope.Create(AudioTestData.Track(sampleRate, 500,
                    (100, first, 1f), (nextStartMs, second, 1f)), sampleRate);
                LoopAtomizerResult result = await LoopAtomizer_V4.AtomizeAsync(source, Settings);
                scope.Own(result.Atomics);
                Assert.AreEqual(2, result.Atomics.Count, $"Phase {phase}: {Describe(result.Atomics)}");
                AssertEndsBefore(source, result.Atomics[0], (int) (nextStartMs * sampleRate / 1000));
            }
        }

        [TestMethod]
        [DataRow(16000, 7)]
        [DataRow(44100, 23)]
        [DataRow(48000, 91)]
        [DataRow(48000, 1234)]
        public async Task DoesNotSplitANoisyCymbalDecay(int sampleRate, int seed)
        {
            using AudioTestScope scope = new();
            Random random = new(seed);
            float[] cymbal = new float[sampleRate * 3 / 4];
            for (int i = 0; i < cymbal.Length; i++)
            {
                double attack = Math.Min(1.0, i / (sampleRate * 0.001));
                cymbal[i] = (float) ((random.NextDouble() * 2 - 1) * 0.7 * attack * Math.Exp(-i / (sampleRate * 0.16)));
            }

            AudioObj source = scope.Create(AudioTestData.Track(sampleRate, 1000, (100, cymbal, 1f)), sampleRate);
            LoopAtomizerResult result = await LoopAtomizer_V4.AtomizeAsync(source, Settings);
            scope.Own(result.Atomics);
            Assert.AreEqual(1, result.Atomics.Count, Describe(result.Atomics));
            Assert.IsTrue(result.Atomics[0].Duration.TotalMilliseconds >= 740, "The decay must remain intact.");
        }

        [TestMethod]
        [DataRow(25)]
        [DataRow(35)]
        [DataRow(70)]
        [DataRow(140)]
        public async Task DoesNotSplitBassDrumOscillations(int frequency)
        {
            using AudioTestScope scope = new();
            float[] hit = AudioTestData.Hit(48000, 450, frequency, 110);
            AudioObj source = scope.Create(AudioTestData.Track(48000, 650, (100, hit, 1f)), 48000);
            LoopAtomizerResult result = await LoopAtomizer_V4.AtomizeAsync(source, Settings);
            scope.Own(result.Atomics);
            Assert.AreEqual(1, result.Atomics.Count, Describe(result.Atomics));
            Assert.IsTrue(result.Atomics[0].Duration.TotalMilliseconds >= 440, "Ringing is not a sequence of new hits.");
        }

        [TestMethod]
        [DataRow(0.05f)]
        [DataRow(0.25f)]
        [DataRow(1f)]
        public async Task PreservesRapidHitsAtDifferentLevels(float gain)
        {
            using AudioTestScope scope = new();
            const int sampleRate = 44100;
            float[] hit = AudioTestData.Hit(sampleRate, 10, 850, 2.5);
            AudioObj source = scope.Create(AudioTestData.Track(sampleRate, 350,
                (100, hit, gain), (124, hit, gain), (148, hit, gain)), sampleRate);
            LoopAtomizerResult result = await LoopAtomizer_V4.AtomizeAsync(source, Settings);
            scope.Own(result.Atomics);
            Assert.AreEqual(3, result.Atomics.Count, Describe(result.Atomics));
            foreach (AudioObj atomic in result.Atomics)
            {
                Assert.IsTrue(atomic.Data.Max(Math.Abs) >= hit.Max(Math.Abs) * gain * 0.95f,
                    "The individual attack peak must be retained.");
            }
        }

        [TestMethod]
        public async Task DetectsRapidHitsAlternatingBetweenStereoChannels()
        {
            using AudioTestScope scope = new();
            const int sampleRate = 48000;
            float[] hit = AudioTestData.Hit(sampleRate, 10, 850, 2.5);
            float[] left = AudioTestData.Track(sampleRate, 350, (100, hit, 1f), (148, hit, 1f));
            float[] right = AudioTestData.Track(sampleRate, 350, (124, hit, 1f), (172, hit, 1f));
            float[] stereo = new float[left.Length * 2];
            for (int i = 0; i < left.Length; i++)
            {
                stereo[i * 2] = left[i];
                stereo[(i * 2) + 1] = right[i];
            }

            AudioObj source = scope.Create(stereo, sampleRate, 2);
            LoopAtomizerResult result = await LoopAtomizer_V4.AtomizeAsync(source, Settings);
            scope.Own(result.Atomics);
            Assert.AreEqual(4, result.Atomics.Count, Describe(result.Atomics));
            Assert.IsTrue(result.Atomics.All(atomic => atomic.Channels == 2));
        }

        private static void AssertEndsBefore(AudioObj source, AudioObj atomic, int nextStart)
        {
            int end = FindSourceStart(source, atomic) + (atomic.Data.Length / atomic.Channels);
            Assert.IsTrue(end <= nextStart, $"The atomic includes {end - nextStart} frames of the next attack.");
        }

        private static int FindSourceStart(AudioObj source, AudioObj atomic)
        {
            int start = source.Data.AsSpan().IndexOf(atomic.Data.AsSpan());
            Assert.IsTrue(start >= 0, "The atomic must be an unchanged, contiguous part of its source.");
            return start / source.Channels;
        }

        private static string Describe(IReadOnlyList<AudioObj> atomics) =>
            string.Join(", ", atomics.Select(audio => $"{audio.Name}: {audio.Duration.TotalMilliseconds:F2} ms"));
    }
}
