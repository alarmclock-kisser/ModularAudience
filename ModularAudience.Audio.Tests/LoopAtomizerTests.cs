using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Processing;
using ModularAudience.Audio.Processors_V4;

namespace ModularAudience.Audio.Tests
{
    [TestClass]
    public sealed class LoopAtomizerTests
    {
        private static readonly LoopAtomizerSettings WithoutDeduplication = LoopAtomizerSettings.Default with { EnableDeduplication = false };

        [TestMethod]
        [DataRow(8000)]
        [DataRow(16000)]
        [DataRow(44100)]
        [DataRow(48000)]
        [DataRow(96000)]
        public async Task SeparatesHitsAndRemovesLeadingSilence(int sampleRate)
        {
            using AudioTestScope scope = new();
            float[] hit = AudioTestData.Hit(sampleRate);
            AudioObj source = scope.Create(AudioTestData.Track(sampleRate, 800, (100, hit, 1f), (450, hit, 1f)), sampleRate);
            LoopAtomizerResult result = await LoopAtomizer_V4.AtomizeAsync(source, WithoutDeduplication);
            scope.Own(result.Atomics);
            Assert.AreEqual(2, result.Atomics.Count, Describe(result.Atomics));
            foreach (AudioObj atomic in result.Atomics)
            {
                AssertTightStart(atomic);
                Assert.AreEqual(1, CountAttacks(atomic));
                Assert.IsTrue(atomic.Data.Max(Math.Abs) > 0.4f, "The attack peak must be preserved.");
            }
        }

        [TestMethod]
        public async Task KeepsShortCloselySpacedHitsSeparate()
        {
            using AudioTestScope scope = new();
            float[] hit = AudioTestData.Hit(16000, 18, 500, 5);
            AudioObj source = scope.Create(AudioTestData.Track(16000, 400, (100, hit, 1f), (150, hit, 1f), (210, hit, 1f)));
            LoopAtomizerResult result = await LoopAtomizer_V4.AtomizeAsync(source, WithoutDeduplication);
            scope.Own(result.Atomics);
            Assert.AreEqual(3, result.Atomics.Count, Describe(result.Atomics));
            foreach (AudioObj atomic in result.Atomics)
            {
                AssertTightStart(atomic);
                Assert.AreEqual(1, CountAttacks(atomic));
                Assert.IsTrue(atomic.Duration.TotalMilliseconds < 65, "Short hits must not be extended or merged to meet MinSliceMs.");
            }
        }

        [TestMethod]
        public async Task SeparatesQuieterHitInsidePreviousDecay()
        {
            using AudioTestScope scope = new();
            float[] loud = AudioTestData.Hit(16000, 180, 90, 20);
            float[] quiet = AudioTestData.Hit(16000, 65, 600, 15);
            AudioObj source = scope.Create(AudioTestData.Track(16000, 450, (100, loud, 1f), (175, quiet, 0.2f)));
            LoopAtomizerResult result = await LoopAtomizer_V4.AtomizeAsync(source, WithoutDeduplication);
            scope.Own(result.Atomics);
            Assert.AreEqual(2, result.Atomics.Count, Describe(result.Atomics));
            Assert.IsTrue(result.Atomics[1].Data.Max(Math.Abs) > 0.08f, "The quieter attack must survive.");
        }

        [TestMethod]
        [DataRow(40)]
        [DataRow(60)]
        [DataRow(110)]
        [DataRow(330)]
        public async Task KeepsLongSustainedToneIntact(int frequency)
        {
            using AudioTestScope scope = new();
            float[] tone = AudioTestData.Tone(16000, 2200, frequency, 15, 80);
            AudioObj source = scope.Create(AudioTestData.Track(16000, 2400, (100, tone, 1f)));
            LoopAtomizerResult result = await LoopAtomizer_V4.AtomizeAsync(source, WithoutDeduplication);
            scope.Own(result.Atomics);
            Assert.AreEqual(1, result.Atomics.Count, Describe(result.Atomics));
            Assert.IsTrue(result.Atomics[0].Duration.TotalMilliseconds >= 2190, "Sustains must not be capped at 1.5 seconds.");
            AssertTightStart(result.Atomics[0]);
        }

        [TestMethod]
        [DataRow(40)]
        [DataRow(80)]
        [DataRow(120)]
        [DataRow(220)]
        public async Task DoesNotSplitASlowAttackIntoMultipleHits(int attackMs)
        {
            using AudioTestScope scope = new();
            float[] tone = AudioTestData.Tone(16000, 650, 220, attackMs, 180);
            AudioObj source = scope.Create(AudioTestData.Track(16000, 900, (100, tone, 1f)));
            LoopAtomizerResult result = await LoopAtomizer_V4.AtomizeAsync(source, WithoutDeduplication);
            scope.Own(result.Atomics);
            Assert.AreEqual(1, result.Atomics.Count, Describe(result.Atomics));
            Assert.IsTrue(result.Atomics[0].Duration.TotalMilliseconds >= 640, "The soft attack must be retained.");
        }

        [TestMethod]
        public async Task DetectsOppositePhaseStereoWithoutChangingChannels()
        {
            using AudioTestScope scope = new();
            float[] hit = AudioTestData.Hit(16000);
            float[] track = AudioTestData.Track(16000, 800, (100, hit, 1f), (450, hit, 1f));
            AudioObj source = scope.Create(AudioTestData.Stereo(track, true), channels: 2);
            LoopAtomizerResult result = await LoopAtomizer_V4.AtomizeAsync(source, WithoutDeduplication);
            scope.Own(result.Atomics);
            Assert.AreEqual(2, result.Atomics.Count, Describe(result.Atomics));
            foreach (AudioObj atomic in result.Atomics)
            {
                Assert.AreEqual(2, atomic.Channels);
                for (int i = 0; i < atomic.Data.Length; i += 2)
                {
                    Assert.AreEqual(-atomic.Data[i], atomic.Data[i + 1]);
                }
            }
        }

        [TestMethod]
        public async Task WorkflowRespectsDisabledDeduplication()
        {
            using AudioTestScope scope = new();
            float[] hit = AudioTestData.Hit(16000);
            AudioObj source = scope.Create(AudioTestData.Track(16000, 800, (100, hit, 1f), (450, hit, 1f)));
            AudioAtomizeResult result = await AudioAtomizerWorkflow.AtomizeAsync(source, WithoutDeduplication);
            scope.Own(result.Atomics);
            Assert.AreEqual(2, result.Atomics.Count, Describe(result.Atomics));
        }

        [TestMethod]
        public async Task DefaultWorkflowRemovesRepeatedCopies()
        {
            using AudioTestScope scope = new();
            float[] hit = AudioTestData.Hit(16000);
            AudioObj source = scope.Create(AudioTestData.Track(16000, 1200, (100, hit, 1f), (450, hit, 1f), (800, hit, 1f)));
            AudioAtomizeResult result = await AudioAtomizerWorkflow.AtomizeAsync(source, LoopAtomizerSettings.Default);
            scope.Own(result.Atomics);
            Assert.AreEqual(1, result.Atomics.Count, Describe(result.Atomics));
        }

        [TestMethod]
        public async Task SilenceProducesNoAtomics()
        {
            using AudioTestScope scope = new();
            LoopAtomizerResult result = await LoopAtomizer_V4.AtomizeAsync(scope.Create(new float[16000]));
            scope.Own(result.Atomics);
            Assert.AreEqual(0, result.Atomics.Count);
        }

        [TestMethod]
        public async Task PreservesInputAndReportsMonotonicProgress()
        {
            using AudioTestScope scope = new();
            float[] hit = AudioTestData.Hit(16000);
            AudioObj source = scope.Create(AudioTestData.Track(16000, 800, (100, hit, 1f), (450, hit, 1f)));
            float[] original = (float[]) source.Data.Clone();
            source.SelectionStart = 12;
            source.SelectionEnd = 240;
            InlineProgress progress = new();
            LoopAtomizerResult result = await LoopAtomizer_V4.AtomizeAsync(source, progress: progress);
            scope.Own(result.Atomics);
            CollectionAssert.AreEqual(original, source.Data);
            Assert.AreEqual(12L, source.SelectionStart);
            Assert.AreEqual(240L, source.SelectionEnd);
            Assert.AreEqual(1.0, progress.Values[^1]);
            Assert.IsTrue(progress.Values.Zip(progress.Values.Skip(1), (left, right) => right >= left).All(value => value));
        }

        [TestMethod]
        public async Task SupportsHitsAtTheSelectionEdges()
        {
            using AudioTestScope scope = new();
            float[] hit = AudioTestData.Hit(16000, 100);
            float[] track = AudioTestData.Track(16000, 500, (0, hit, 1f), (420, hit, 1f));
            AudioObj source = scope.Create(track);
            LoopAtomizerResult result = await LoopAtomizer_V4.AtomizeAsync(source, WithoutDeduplication);
            scope.Own(result.Atomics);
            Assert.AreEqual(2, result.Atomics.Count, Describe(result.Atomics));
            Assert.AreEqual(track[^1], result.Atomics[^1].Data[^1]);
        }

        private static void AssertTightStart(AudioObj audio)
        {
            int first = Array.FindIndex(audio.Data, value => Math.Abs(value) > 0.00001f) / audio.Channels;
            Assert.IsTrue(first >= 0 && first <= audio.SampleRate / 1000, $"Leading silence: {first} frames.");
        }

        private static int CountAttacks(AudioObj audio)
        {
            float threshold = audio.Data.Max(Math.Abs) * 0.03f;
            int gap = audio.SampleRate / 60;
            int silence = gap;
            int attacks = 0;
            for (int i = 0; i < audio.Data.Length; i += audio.Channels)
            {
                if (Math.Abs(audio.Data[i]) >= threshold)
                {
                    if (silence >= gap) attacks++;
                    silence = 0;
                }
                else silence++;
            }

            return attacks;
        }

        private static string Describe(IReadOnlyList<AudioObj> atomics) =>
            string.Join(", ", atomics.Select(audio => $"{audio.Name}: {audio.Duration.TotalMilliseconds:F2} ms"));

        private sealed class InlineProgress : IProgress<double>
        {
            public List<double> Values { get; } = [];
            public void Report(double value) => this.Values.Add(value);
        }
    }
}
