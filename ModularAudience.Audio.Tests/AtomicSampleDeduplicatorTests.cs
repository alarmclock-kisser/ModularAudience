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
            Assert.IsTrue(double.TryParse(result[0].CustomTags["AtomizeQuality"],
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _),
                "Each selected sample must retain its quality score for drumset-best selection.");
        }

        [TestMethod]
        public void SelectsDistinctSoundsWithinOneType()
        {
            using AudioTestScope scope = new();
            float[] low = AudioTestData.Hit(16000, 180, 180, 40);
            float[] high = AudioTestData.Hit(16000, 180, 1800, 40);
            float[] quietDifferent = high.Select(value => value * 0.005f).ToArray();
            List<AudioObj> candidates = [scope.Create(low), scope.Create((float[])low.Clone()),
                scope.Create(low.Select(value => value * 0.5f).ToArray()), scope.Create(high), scope.Create(quietDifferent)];

            IReadOnlyList<AudioObj> selected = LoopAtomizer_V4.SelectDistinctRepresentatives(candidates);

            Assert.AreEqual(2, selected.Count);
            Assert.IsTrue(selected.Contains(candidates[0]) || selected.Contains(candidates[1]) || selected.Contains(candidates[2]));
            Assert.IsTrue(selected.Contains(candidates[3]), "A fundamentally different same-type sound must be retained.");
            Assert.IsFalse(selected.Contains(candidates[4]), "A very quiet alternative should not outrank usable candidates.");
        }

        [TestMethod]
        public void KeepsOneFallbackWhenEveryCandidateIsQuiet()
        {
            using AudioTestScope scope = new();
            float[] first = AudioTestData.Hit(16000, 180, 180, 40).Select(value => value * 0.005f).ToArray();
            float[] second = AudioTestData.Hit(16000, 180, 1800, 40).Select(value => value * 0.004f).ToArray();
            IReadOnlyList<AudioObj> selected = LoopAtomizer_V4.SelectDistinctRepresentatives(
                [scope.Create(first), scope.Create(second)]);

            Assert.AreEqual(1, selected.Count, "Keep the best available fallback instead of returning an empty drumset.");
        }

        [TestMethod]
        public void PreservesThreeDifferentDecayVariants()
        {
            using AudioTestScope scope = new();
            List<AudioObj> atomics = [scope.Create(AudioTestData.Hit(16000, 240, decayMs: 25)),
                scope.Create(AudioTestData.Hit(16000, 240, decayMs: 45)),
                scope.Create(AudioTestData.Hit(16000, 240, decayMs: 80))];
            Assert.AreEqual(3, Deduplicate(atomics, maxVariantsPerCluster: 3).Count);
        }

        [TestMethod]
        public void CapsSimilarVariantsAtThree()
        {
            using AudioTestScope scope = new();
            List<AudioObj> atomics = new[] { 30, 40, 50, 65, 80 }
                .Select(decay => scope.Create(AudioTestData.Hit(16000, 240, decayMs: decay))).ToList();
            List<AudioObj> result = Deduplicate(atomics, maxVariantsPerCluster: 3);
            Assert.AreEqual(3, result.Count);
            Assert.IsTrue(result.Contains(atomics[0]) || result.Contains(atomics[^1]), "Selection should retain distinct decay extremes.");
        }

        [TestMethod]
        public void CapsSimilarVariantsAtTwoByDefault()
        {
            using AudioTestScope scope = new();
            List<AudioObj> atomics = new[] { 30, 40, 50, 65, 80 }
                .Select(decay => scope.Create(AudioTestData.Hit(16000, 240, decayMs: decay))).ToList();
            Assert.AreEqual(2, Deduplicate(atomics).Count);
        }

        [TestMethod]
        public void PrefersTheLessOverlappedHitWithinAFamily()
        {
            using AudioTestScope scope = new();
            float[] clean = AudioTestData.Hit(16000, 240, 180, 90);
            float[] overlapped = (float[])clean.Clone();
            float[] extraHit = AudioTestData.Tone(16000, 12, 2400, 1, 4);
            int overlapStart = 16000 * 80 / 1000;
            for (int i = 0; i < extraHit.Length; i++)
            {
                overlapped[overlapStart + i] += extraHit[i] * 0.8f;
            }

            AudioObj overlappedAtomic = scope.Create(overlapped);
            AudioObj cleanAtomic = scope.Create(clean);
            List<AudioObj> result = Deduplicate([overlappedAtomic, cleanAtomic], maxVariantsPerCluster: 1);
            Assert.AreEqual(1, result.Count);
            Assert.IsTrue(result.Contains(cleanAtomic), "The isolated hit should be preferred over a hit with a later overlapping transient.");
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

        private static List<AudioObj> Deduplicate(List<AudioObj> atomics, int maxVariantsPerCluster = 2) =>
            AtomicSampleDeduplicator.Deduplicate(atomics, LoopAtomizerSettings.Default.ClusterSimilarityThreshold,
                null, maxVariantsPerCluster);
    }
}
