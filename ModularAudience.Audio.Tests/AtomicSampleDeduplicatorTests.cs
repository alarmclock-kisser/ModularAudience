using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Processors_V4;

namespace ModularAudience.Audio.Tests
{
    [TestClass]
    public sealed class AtomicSampleDeduplicatorTests
    {
        [TestMethod]
        public void RemovesExactAndGainScaledCopies()
        {
            using AudioTestScope scope = new();
            float[] hit = AudioTestData.Hit(16000, 200);
            List<AudioObj> atomics = [scope.Create(hit), scope.Create((float[]) hit.Clone()),
                scope.Create(hit.Select(value => value * 0.4f).ToArray())];
            List<AudioObj> result = Deduplicate(atomics);
            Assert.AreEqual(1, result.Count);
        }

        [TestMethod]
        public void PreservesThreeDifferentDecayVariants()
        {
            using AudioTestScope scope = new();
            List<AudioObj> atomics = [scope.Create(AudioTestData.Hit(16000, 240, decayMs: 25)),
                scope.Create(AudioTestData.Hit(16000, 240, decayMs: 45)),
                scope.Create(AudioTestData.Hit(16000, 240, decayMs: 80))];
            Assert.AreEqual(3, Deduplicate(atomics).Count);
        }

        [TestMethod]
        public void CapsSimilarVariantsAtThree()
        {
            using AudioTestScope scope = new();
            List<AudioObj> atomics = new[] { 30, 40, 50, 65, 80 }
                .Select(decay => scope.Create(AudioTestData.Hit(16000, 240, decayMs: decay))).ToList();
            List<AudioObj> result = Deduplicate(atomics);
            Assert.AreEqual(3, result.Count);
            Assert.IsTrue(result.Contains(atomics[0]) || result.Contains(atomics[^1]), "Selection should retain distinct decay extremes.");
        }

        [TestMethod]
        public void DoesNotCapDifferentSoundsAtThreeGlobally()
        {
            using AudioTestScope scope = new();
            List<AudioObj> atomics = new[] { 60, 180, 540, 1620, 4500 }
                .Select(frequency => scope.Create(AudioTestData.Hit(16000, 200, frequency))).ToList();
            Assert.AreEqual(5, Deduplicate(atomics).Count);
        }

        [TestMethod]
        public void KeepsDifferentAttacksWithIdenticalTails()
        {
            using AudioTestScope scope = new();
            float[] first = AudioTestData.Hit(16000, 350, 180, 90);
            float[] second = (float[])first.Clone();
            for (int i = 0; i < 160; i++)
            {
                second[i] = (float)(0.65 * Math.Sin(2 * Math.PI * 2100 * i / 16000));
            }

            Assert.AreEqual(2, Deduplicate([scope.Create(first), scope.Create(second)]).Count);
        }

        [TestMethod]
        public void DistinguishesOppositePhaseStereoSounds()
        {
            using AudioTestScope scope = new();
            AudioObj first = scope.Create(AudioTestData.Stereo(AudioTestData.Hit(16000, frequency: 90), true), channels: 2);
            AudioObj second = scope.Create(AudioTestData.Stereo(AudioTestData.Hit(16000, frequency: 1800), true), channels: 2);
            Assert.AreEqual(2, Deduplicate([first, second]).Count);
        }

        [TestMethod]
        public void DoesNotCollapseDifferentBassNotes()
        {
            using AudioTestScope scope = new();
            AudioObj first = scope.Create(AudioTestData.Hit(48000, 500, 55, 150), 48000);
            AudioObj second = scope.Create(AudioTestData.Hit(48000, 500, 55 * Math.Pow(2, 1.0 / 12), 150), 48000);
            Assert.AreEqual(2, Deduplicate([first, second]).Count);
        }

        private static List<AudioObj> Deduplicate(List<AudioObj> atomics) =>
            AtomicSampleDeduplicator.Deduplicate(atomics, LoopAtomizerSettings.Default.ClusterSimilarityThreshold, null);
    }
}
