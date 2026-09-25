using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Processing;
using ModularAudience.Audio.Processors_V4;

namespace ModularAudience.Audio.Tests
{
    [TestClass]
    public sealed class LoopAtomizerTests
    {
        private static readonly LoopAtomizerSettings WithoutDeduplication = LoopAtomizerSettings.Default with
        {
            EnableDeduplication = false,
            MinimumRmsLevel = 0f,
            MinimumAtomicDurationMs = 0
        };

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
            AudioObj source = scope.Create(AudioTestData.Track(16000, 450, (100, loud, 1f), (175, quiet, 0.05f)));
            LoopAtomizerResult result = await LoopAtomizer_V4.AtomizeAsync(source, WithoutDeduplication);
            scope.Own(result.Atomics);
            Assert.AreEqual(2, result.Atomics.Count, Describe(result.Atomics));
            Assert.IsTrue(result.Atomics[1].Data.Max(Math.Abs) > 0.02f, "The quieter attack must survive.");
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
        public async Task PreservesQuietHitsWithoutAnAbsoluteRmsCutoff()
        {
            using AudioTestScope scope = new();
            float[] hit = AudioTestData.Hit(16000);
            AudioObj source = scope.Create(AudioTestData.Track(16000, 800,
                (100, hit, 0.02f), (450, hit, 0.02f)));
            LoopAtomizerResult result = await LoopAtomizer_V4.AtomizeAsync(source,
                LoopAtomizerSettings.Default with { EnableDeduplication = false, MinimumRmsLevel = 0f });
            scope.Own(result.Atomics);
            Assert.AreEqual(2, result.Atomics.Count, Describe(result.Atomics));
            Assert.IsTrue(result.Atomics.All(atomic => atomic.Data.Max(Math.Abs) > 0.01f),
                "Quiet hits must retain their attack peaks.");
        }

        [TestMethod]
        public async Task DropsHitsShorterThanFortyMilliseconds()
        {
            using AudioTestScope scope = new();
            const int sampleRate = 16000;
            float[] shortHit = AudioTestData.Hit(sampleRate, 30, 850, 8);
            float[] fullHit = AudioTestData.Hit(sampleRate, 80, 180, 25);
            AudioObj source = scope.Create(AudioTestData.Track(sampleRate, 400,
                (80, shortHit, 1f), (240, fullHit, 1f)));
            LoopAtomizerResult result = await LoopAtomizer_V4.AtomizeAsync(source,
                LoopAtomizerSettings.Default with { EnableDeduplication = false });
            scope.Own(result.Atomics);
            Assert.AreEqual(1, result.Atomics.Count, Describe(result.Atomics));
            Assert.IsTrue(result.Atomics[0].Duration.TotalMilliseconds >= 20,
                "The retained atomic must meet the 20-ms minimum duration.");
        }

        [TestMethod]
        public async Task DropsHitsBelowTheOverallRmsFloor()
        {
            using AudioTestScope scope = new();
            const int sampleRate = 16000;
            float[] quietHit = AudioTestData.Hit(sampleRate, 120, 180, 40)
                .Select(value => value * 0.0005f).ToArray();
            AudioObj source = scope.Create(AudioTestData.Track(sampleRate, 400, (100, quietHit, 1f)));
            LoopAtomizerResult result = await LoopAtomizer_V4.AtomizeAsync(source,
                LoopAtomizerSettings.Default with { EnableDeduplication = false });
            scope.Own(result.Atomics);
            Assert.AreEqual(0, result.Atomics.Count, "An inaudibly quiet atomic should be discarded by its total RMS level.");
        }

        [TestMethod]
        public async Task DropsWeakHitDilutedByLongTrailingSilence()
        {
            using AudioTestScope scope = new();
            const int sampleRate = 16000;
            float[] weakHit = AudioTestData.Hit(sampleRate, 120, 180, 40)
                .Select(value => value * 0.01f).ToArray();
            AudioObj source = scope.Create(AudioTestData.Track(sampleRate, 2000, (100, weakHit, 1f)));
            LoopAtomizerResult result = await LoopAtomizer_V4.AtomizeAsync(source,
                LoopAtomizerSettings.Default with { EnableDeduplication = false });
            scope.Own(result.Atomics);
            Assert.AreEqual(0, result.Atomics.Count,
                "A faint short hit diluted by a long silent segment must not be returned as a long atomic.");
        }

        [TestMethod]
        public async Task FadesAnAbruptlyTruncatedTail()
        {
            using AudioTestScope scope = new();
            const int sampleRate = 16000;
            float[] sourceData = new float[sampleRate / 2];
            Array.Fill(sourceData, 0.3f, sampleRate / 10, sourceData.Length - (sampleRate / 10));
            AudioObj source = scope.Create(sourceData, sampleRate);
            LoopAtomizerResult result = await LoopAtomizer_V4.AtomizeAsync(source,
                LoopAtomizerSettings.Default with { EnableDeduplication = false });
            scope.Own(result.Atomics);
            Assert.AreEqual(1, result.Atomics.Count, Describe(result.Atomics));
            AudioObj atomic = result.Atomics[0];
            Assert.AreEqual(0f, atomic.Data[^1], "A hard-cut tail must fade to silence.");
            Assert.IsTrue(Math.Abs(atomic.Data[^(sampleRate / 200)]) > Math.Abs(atomic.Data[^1]),
                "The fade must taper the final samples rather than silence the entire edge.");
        }

        [TestMethod]
        public async Task TrimsLowLevelNoiseAfterLongAtomic()
        {
            using AudioTestScope scope = new();
            const int sampleRate = 16000;
            float[] sourceData = new float[sampleRate * 2];
            float[] hit = AudioTestData.Hit(sampleRate, 120, 180, 40);
            Array.Copy(hit, 0, sourceData, sampleRate / 10, hit.Length);
            Random random = new(71);
            for (int frame = sampleRate / 3; frame < sourceData.Length; frame++)
            {
                sourceData[frame] = (float)((random.NextDouble() * 2.0 - 1.0) * 0.01);
            }

            AudioObj source = scope.Create(sourceData, sampleRate);
            LoopAtomizerResult result = await LoopAtomizer_V4.AtomizeAsync(source, WithoutDeduplication);
            scope.Own(result.Atomics);
            Assert.AreEqual(1, result.Atomics.Count, Describe(result.Atomics));
            Assert.IsTrue(result.Atomics[0].Duration.TotalMilliseconds < 300,
                "Low-level noise after the hit must not extend a long atomic to the track end.");
        }

        [TestMethod]
        public async Task KeepsQuietLongSustainIntact()
        {
            using AudioTestScope scope = new();
            const int sampleRate = 16000;
            float[] tone = AudioTestData.Tone(sampleRate, 1500, 220, 15, 15)
                .Select(value => value * 0.02f).ToArray();
            AudioObj source = scope.Create(AudioTestData.Track(sampleRate, 1800, (100, tone, 1f)), sampleRate);
            LoopAtomizerResult result = await LoopAtomizer_V4.AtomizeAsync(source,
                WithoutDeduplication with { MinimumRmsLevel = LoopAtomizerSettings.Default.MinimumRmsLevel });
            scope.Own(result.Atomics);
            Assert.AreEqual(1, result.Atomics.Count, Describe(result.Atomics));
            Assert.IsTrue(result.Atomics[0].Duration.TotalMilliseconds >= 1490,
                "Tail trimming must remain relative to the sample peak and preserve a quiet sustained sound.");
        }

            [TestMethod]
            public void BindingResetPreservesExplicitSampleNamesWhenIndexingIsOff()
            {
                string workingDirectory = Path.Combine(Path.GetTempPath(), "ModularAudience.Audio.Tests", Guid.NewGuid().ToString("N"));
                using AudioCollection collection = new(workingDirectory, workingDirectory);
                AudioObj audio = new();
                audio.Rename("Source");
                audio.Name = "Source_Atomic_Kick";
                collection.Audios.Add(audio);

                collection.Audios.ResetBindings();

                Assert.AreEqual("Source_Atomic_Kick", audio.Name);
                collection.Dispose();
                Directory.Delete(workingDirectory, recursive: true);
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
            float[] original = (float[])source.Data.Clone();
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
            Assert.AreEqual(0f, result.Atomics[^1].Data[^1], "The track-edge tail must fade out cleanly.");
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
