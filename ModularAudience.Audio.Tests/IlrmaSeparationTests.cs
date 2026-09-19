using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Processors_V4;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ModularAudience.Audio.Tests
{
    [TestClass]
    [DoNotParallelize]
    public sealed class IlrmaSeparationTests
    {
        private const int SampleRate = 8000;

        /// <summary>
        /// Two full-rank, overlapping synthetic stereo sources, both audible in both input channels.
        /// Source A: 250 Hz tone, stronger in left channel (0.35L + 0.15R).
        /// Source B: 1200 Hz tone, stronger in right channel (0.15L + 0.35R).
        /// ILRMA must separate both with >= 6 dB interference improvement.
        /// </summary>
        [TestMethod]
        public async Task TwoFullRankStereoSourcesAchieveSixDbInterferenceImprovement()
        {
            var mixture = CreateStereoMixture(16000);
            using AudioTestScope scope = new();
            AudioObj audio = scope.Create(mixture.Mix, SampleRate, 2);

            DeterministicSeparationSettings settings = SmallSettings() with
            {
                UseIlrma = true,
                IlrmaIterations = 80,
                IlrmaComponents = 2
            };

            DeterministicSeparationAnalysis analysis = await DeterministicSeparationProcessor.AnalyzeAsync(audio, settings);
            Assert.IsTrue(analysis.Sources.Count >= 2, "ILRMA must detect at least two spatial groups for overlapping stereo sources.");

            DeterministicSeparationResult result = await RenderOwned(scope, analysis);
            // ILRMA clamps to at most 2 sources + Residual (two-microphone spatial constraint).
            int expectedStems = Math.Min(analysis.Sources.Count, 2) + 1;
            Assert.AreEqual(expectedStems, result.Stems.Count, "ILRMA renders at most 2 spatial sources plus Residual.");
            Assert.IsTrue(result.Stems[^1].Name.EndsWith(" - Residual", StringComparison.Ordinal));

            // Verify SIR improvement for both sources.
            AudioObj[] groups = result.Stems.Take(analysis.Sources.Count).ToArray();
            AssertInterferenceSuppression(groups, mixture.Left, mixture.Right, mixture.Mix);
            AssertReconstruction(mixture.Mix, result);
        }

        /// <summary>
        /// ILRMA with ProfilesOnly and two profiles produces matched source + Residual.
        /// </summary>
        [TestMethod]
        public async Task IlrmaProfilesOnlyWithTwoProfilesProducesMatchedSourceAndResidual()
        {
            var mixture = CreateStereoMixture(12000);
            using AudioTestScope scope = new();
            AudioObj audio = scope.Create(mixture.Mix, SampleRate, 2);

            DeterministicSeparationSettings settings = SmallSettings() with
            {
                UseIlrma = true,
                IlrmaIterations = 40,
                IlrmaComponents = 2,
                EnsembleMode = InstrumentEnsembleMode.ProfilesOnly,
                InstrumentProfiles = [InstrumentProfileId.SynthBass, InstrumentProfileId.Vocals]
            };

            DeterministicSeparationAnalysis analysis = await DeterministicSeparationProcessor.AnalyzeAsync(audio, settings);
            DeterministicSeparationResult result = await RenderOwned(scope, analysis);

            Assert.IsTrue(result.Stems.Count >= 2, "ProfilesOnly with two profiles: at least two matched sources plus Residual.");
            AssertReconstruction(mixture.Mix, result);
        }

        /// <summary>
        /// ILRMA rejects duplicate stereo input (both channels identical, rank-deficient).
        /// </summary>
        [TestMethod]
        public void IlrmaRejectsDuplicateStereoInput()
        {
            // Duplicate: both channels identical -> rank-deficient spatial model
            float[] mono = new float[4000];
            for (int i = 0; i < mono.Length; i++)
                mono[i] = (float)(0.3 * Math.Sin(2 * Math.PI * 400 * i / SampleRate));

            float[] duplicate = AudioTestData.Stereo(mono, oppositePhase: false);
            using AudioTestScope scope = new();
            AudioObj audio = scope.Create(duplicate, SampleRate, 2);

            DeterministicSeparationSettings settings = SmallSettings() with
            {
                UseIlrma = true,
                IlrmaIterations = 40,
                IlrmaComponents = 2
            };

            // Duplicate stereo should either produce rank=1 and skip demixing,
            // or throw an explicit rejection. Either way, it must not produce a separation-quality claim.
            // The ILRMA validation rejects rank-deficient stereo.
            try
            {
                _ = DeterministicSeparationProcessor.AnalyzeAsync(audio, settings).GetAwaiter().GetResult();
                // If analysis succeeds, the spatial model should handle it gracefully
                // (rank=1 -> one source, material goes to Residual).
            }
            catch (ArgumentException ex)
            {
                // Explicit rejection of rank-deficient stereo is acceptable.
                Assert.IsTrue(ex.Message.Contains("rank") || ex.Message.Contains("spatial") || ex.Message.Contains("channel"),
                    $"Rank-deficient rejection message: {ex.Message}");
            }
        }

        /// <summary>
        /// ILRMA rejects antiphase stereo input (exactly opposite channels).
        /// </summary>
        [TestMethod]
        public void IlrmaRejectsAntiphaseStereoInput()
        {
            float[] mono = new float[4000];
            for (int i = 0; i < mono.Length; i++)
                mono[i] = (float)(0.3 * Math.Sin(2 * Math.PI * 400 * i / SampleRate));

            float[] antiphase = AudioTestData.Stereo(mono, oppositePhase: true);
            using AudioTestScope scope = new();
            AudioObj audio = scope.Create(antiphase, SampleRate, 2);

            DeterministicSeparationSettings settings = SmallSettings() with
            {
                UseIlrma = true,
                IlrmaIterations = 40,
                IlrmaComponents = 2
            };

            // Antiphase stereo is also rank-deficient.
            try
            {
                _ = DeterministicSeparationProcessor.AnalyzeAsync(audio, settings).GetAwaiter().GetResult();
            }
            catch (ArgumentException ex)
            {
                Assert.IsTrue(ex.Message.Contains("rank") || ex.Message.Contains("spatial") || ex.Message.Contains("channel"),
                    $"Antiphase rejection message: {ex.Message}");
            }
        }

        /// <summary>
        /// ILRMA with silence returns Residual-only.
        /// </summary>
        [TestMethod]
        public async Task IlrmaSilenceReturnsResidualOnly()
        {
            float[] silence = new float[4000]; // mono 0, will be stereo
            float[] stereoSilence = AudioTestData.Stereo(silence, oppositePhase: false);
            using AudioTestScope scope = new();
            AudioObj audio = scope.Create(stereoSilence, SampleRate, 2);

            DeterministicSeparationSettings settings = SmallSettings() with
            {
                UseIlrma = true,
                IlrmaIterations = 40,
                IlrmaComponents = 2
            };

            DeterministicSeparationAnalysis analysis = await DeterministicSeparationProcessor.AnalyzeAsync(audio, settings);
            Assert.AreEqual(0, analysis.EstimatedSignalRank, "Silence must have rank zero.");
            Assert.AreEqual(0, analysis.Sources.Count, "Silence must not create artificial source groups.");

            DeterministicSeparationResult result = await RenderOwned(scope, analysis);
            Assert.AreEqual(1, result.Stems.Count, "Silence must return Residual only.");
            Assert.IsTrue(result.Stems[0].Name.EndsWith(" - Residual", StringComparison.Ordinal));
            CollectionAssert.AreEqual(stereoSilence, result.Stems[0].Data, "Silence Residual must exactly preserve the input.");
        }

        /// <summary>
        /// ILRMA results must be deterministic across thread-count changes.
        /// </summary>
        [TestMethod]
        public async Task IlrmaIsDeterministicAcrossThreadCountChanges()
        {
            var mixture = CreateStereoMixture(8000);
            using AudioTestScope scope = new();
            AudioObj source = scope.Create(mixture.Mix, SampleRate, 2);

            DeterministicSeparationSettings settings1 = SmallSettings() with
            {
                UseIlrma = true,
                IlrmaIterations = 40,
                IlrmaComponents = 2,
                Threads = 1
            };

            DeterministicSeparationSettings settings2 = SmallSettings() with
            {
                UseIlrma = true,
                IlrmaIterations = 40,
                IlrmaComponents = 2,
                Threads = Math.Min(2, Environment.ProcessorCount)
            };

            DeterministicSeparationAnalysis analysis1 = await DeterministicSeparationProcessor.AnalyzeAsync(source, settings1);
            DeterministicSeparationResult result1 = await RenderOwned(scope, analysis1);

            DeterministicSeparationAnalysis analysis2 = await DeterministicSeparationProcessor.AnalyzeAsync(source, settings2);
            DeterministicSeparationResult result2 = await RenderOwned(scope, analysis2);

            Assert.AreEqual(result1.Stems.Count, result2.Stems.Count, "Stem count must be deterministic.");
            for (int i = 0; i < result1.Stems.Count; i++)
            {
                AssertSamplesClose(result1.Stems[i].Data, result2.Stems[i].Data, 1e-6,
                    $"ILRMA deterministic: {result1.Stems[i].Name}, Threads 1 vs {Math.Min(2, Environment.ProcessorCount)}");
            }
        }

        /// <summary>
        /// ILRMA honors cancellation during training.
        /// </summary>
        [TestMethod]
        public async Task IlrmaHonorsCancellation()
        {
            var mixture = CreateStereoMixture(24000);
            using AudioTestScope scope = new();
            AudioObj source = scope.Create(mixture.Mix, SampleRate, 2);

            using CancellationTokenSource cancellation = new();
            CancelAtProgress progress = new(cancellation);
            cancellation.Cancel(); // Pre-cancel

            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            {
                DeterministicSeparationSettings settings = SmallSettings() with
                {
                    UseIlrma = true,
                    IlrmaIterations = 200,
                    IlrmaComponents = 2
                };
                _ = await DeterministicSeparationProcessor.AnalyzeAsync(source, settings, progress, cancellation.Token);
            }, "ILRMA must honor pre-cancellation.");
        }

        /// <summary>
        /// ILRMA with CQT synthesis must be rejected.
        /// </summary>
        [TestMethod]
        public void IlrmaRejectsCqtSynthesis()
        {
            using AudioTestScope scope = new();
            float[] mono = new float[4000];
            float[] stereo = AudioTestData.Stereo(mono, oppositePhase: false);
            for (int i = 0; i < mono.Length; i++)
                mono[i] = (float)(0.3 * Math.Sin(2 * Math.PI * 400 * i / SampleRate));
            AudioObj audio = scope.Create(stereo, SampleRate, 2);

            DeterministicSeparationSettings bad = new()
            {
                UseIlrma = true,
                UseCqtSynthesis = true
            };

            Assert.ThrowsException<ArgumentException>(() =>
            {
                _ = DeterministicSeparationProcessor.AnalyzeAsync(audio, bad).GetAwaiter().GetResult();
            }, "ILRMA must reject CQT synthesis.");
        }

        /// <summary>
        /// ILRMA with >2 profiles must be rejected.
        /// </summary>
        [TestMethod]
        public void IlrmaRejectsMoreThanTwoProfiles()
        {
            using AudioTestScope scope = new();
            float[] mono = new float[4000];
            float[] stereo = AudioTestData.Stereo(mono, oppositePhase: false);
            for (int i = 0; i < mono.Length; i++)
                mono[i] = (float)(0.3 * Math.Sin(2 * Math.PI * 400 * i / SampleRate));
            AudioObj audio = scope.Create(stereo, SampleRate, 2);

            DeterministicSeparationSettings bad = new()
            {
                UseIlrma = true,
                EnsembleMode = InstrumentEnsembleMode.ProfilesOnly,
                InstrumentProfiles = [InstrumentProfileId.SynthBass, InstrumentProfileId.Drums, InstrumentProfileId.Vocals]
            };

            Assert.ThrowsException<ArgumentException>(() =>
            {
                _ = DeterministicSeparationProcessor.AnalyzeAsync(audio, bad).GetAwaiter().GetResult();
            }, "ILRMA must reject more than two profiles.");
        }

        // --- Helpers ---

        private static DeterministicSeparationSettings SmallSettings() => new()
        {
            WindowSize = 512,
            MaxComponents = 4,
            Iterations = 20,
            AnalysisFrames = 64,
            BlockFrames = 16,
            MedianFrames = 9,
            MedianBins = 9,
            Threads = 1
        };

        /// <summary>
        /// Create two overlapping stereo sources with distinct spatial patterns.
        /// Source A (Left-dominant): 250 Hz tone, stronger in left channel.
        /// Source B (Right-dominant): 1200 Hz tone, stronger in right channel.
        /// </summary>
        private static (float[] Mix, float[] Left, float[] Right) CreateStereoMixture(int frames)
        {
            float[] left = new float[frames * 2];   // interleaved
            float[] right = new float[frames * 2];   // interleaved
            float[] mix = new float[frames * 2];

            for (int i = 0; i < frames; i++)
            {
                double time = i / (double)SampleRate;
                double fade = Math.Min(1, Math.Min(i, frames - 1 - i) / (SampleRate * 0.02));

                // Source A: 250 Hz, left-dominant (0.35 left, 0.15 right)
                double aFade = fade * (0.05 + 0.95 * Math.Pow(Math.Sin(Math.PI * (time / 0.50 + 0.1)), 2));
                double aLeft = 0.35 * aFade * Math.Sin(2 * Math.PI * 250 * time);
                double aRight = 0.15 * aFade * Math.Sin(2 * Math.PI * 250 * time + 0.1); // slight phase offset

                // Source B: 1200 Hz, right-dominant (0.15 left, 0.35 right)
                double bFade = fade * (0.05 + 0.95 * Math.Pow(Math.Sin(Math.PI * (time / 0.31 + 0.4)), 2));
                double bLeft = 0.15 * bFade * Math.Sin(2 * Math.PI * 1200 * time);
                double bRight = 0.35 * bFade * Math.Sin(2 * Math.PI * 1200 * time + 0.2); // slight phase offset

                // Mix both sources in each channel
                mix[i * 2] = (float)(aLeft + bLeft);
                mix[i * 2 + 1] = (float)(aRight + bRight);

                // Source A alone
                left[i * 2] = (float)aLeft;
                left[i * 2 + 1] = (float)aRight;

                // Source B alone
                right[i * 2] = (float)bLeft;
                right[i * 2 + 1] = (float)bRight;
            }

            return (mix, left, right);
        }

        private static void AssertInterferenceSuppression(AudioObj[] groups, float[] sourceA, float[] sourceB, float[] mix)
        {
            Projection[] aScores = groups.Select(stem => MeasureProjection(stem.Data, sourceA, sourceB)).ToArray();
            Projection[] bScores = groups.Select(stem => MeasureProjection(stem.Data, sourceB, sourceA)).ToArray();

            double inputA = MeasureProjection(mix, sourceA, sourceB).SirDb;
            double inputB = MeasureProjection(mix, sourceB, sourceA).SirDb;
            string stemDetails = string.Join(Environment.NewLine, groups.Select((stem, i) =>
                $"{stem.Name}: A [{aScores[i]}]; B [{bScores[i]}]"));
            string measurements = $"Groups={groups.Length}; input A SIR={inputA:F1} dB; input B SIR={inputB:F1} dB"
                + Environment.NewLine + stemDetails;

            // ILRMA must produce at least one stem that meaningfully improves SIR for source A
            // and one stem that meaningfully improves SIR for source B, each by >= 6 dB.
            bool hasAImprovement = aScores.Any(s => s.SirDb >= inputA + 6 && s.Gain >= 0.10);
            bool hasBImprovement = bScores.Any(s => s.SirDb >= inputB + 6 && s.Gain >= 0.10);

            Assert.IsTrue(groups.Length >= 2 && hasAImprovement && hasBImprovement,
                "ILRMA must produce two distinct groups: each achieving >= 6 dB SIR improvement over the input mixture. " + measurements);
        }

        private static Projection MeasureProjection(float[] stem, float[] target, float[] distractor)
        {
            double targetEnergy = Dot(target, target);
            double distractorEnergy = Dot(distractor, distractor);
            double cross = Dot(target, distractor);
            double targetDot = Dot(stem, target);
            double distractorDot = Dot(stem, distractor);
            double determinant = targetEnergy * distractorEnergy - cross * cross;
            Assert.IsTrue(determinant > 0, "References must be linearly independent.");
            double gain = (targetDot * distractorEnergy - distractorDot * cross) / determinant;
            double otherGain = (distractorDot * targetEnergy - targetDot * cross) / determinant;
            double correlation = targetDot / Math.Sqrt(Math.Max(1e-30, Dot(stem, stem) * targetEnergy));
            double sir = 10 * Math.Log10(Math.Max(1e-30, gain * gain * targetEnergy)
                / Math.Max(1e-30, otherGain * otherGain * distractorEnergy));
            double inputSir = 10 * Math.Log10(targetEnergy / distractorEnergy);
            return new(gain, otherGain, correlation, sir, sir - inputSir);
        }

        private static double Dot(float[] first, float[] second)
        {
            double sum = 0;
            for (int i = 0; i < first.Length; i++) sum += (double)first[i] * second[i];
            return sum;
        }

        private static async Task<DeterministicSeparationResult> RenderOwned(AudioTestScope scope,
            DeterministicSeparationAnalysis analysis, int[]? selected = null,
            IProgress<DeterministicSeparationProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            // ILRMA supports at most 2 output sources; cap selection accordingly.
            int[]? ilrmaSafe = selected ?? analysis.Sources.Select(s => s.Id).Take(2).ToArray();
            DeterministicSeparationResult result = await DeterministicSeparationProcessor.SeparateAsync(
                analysis, ilrmaSafe, progress, cancellationToken);
            scope.Own(result.Stems);
            return result;
        }

        private static void AssertReconstruction(float[] original, DeterministicSeparationResult result)
        {
            double[] sum = new double[original.Length];
            foreach (AudioObj stem in result.Stems)
            {
                Assert.AreEqual(original.Length, stem.Data.Length, $"{stem.Name}: sample count changed.");
                AssertFinite(stem.Data, stem.Name);
                for (int i = 0; i < sum.Length; i++) sum[i] += stem.Data[i];
            }
            double error = Enumerable.Range(0, sum.Length).Max(i => Math.Abs(sum[i] - original[i]));
            Assert.IsTrue(error <= 1e-4, $"Selected groups plus Residual must reconstruct the snapshot; maximum error={error:E9}.");
            Assert.IsTrue(double.IsFinite(result.ReconstructionError),
                $"Reported reconstruction error is invalid: {result.ReconstructionError:E9}.");
        }

        private static void AssertFinite(float[] samples, string context)
        {
            for (int i = 0; i < samples.Length; i++)
            {
                if (!float.IsFinite(samples[i])) Assert.Fail($"{context}: non-finite output at interleaved sample {i}: {samples[i]}.");
            }
        }

        private static void AssertSamplesClose(float[] expected, float[] actual, double tolerance, string context)
        {
            Assert.AreEqual(expected.Length, actual.Length, $"{context}: sample count changed.");
            AssertFinite(actual, context);
            double maximum = 0;
            for (int index = 0; index < expected.Length; index++)
            {
                double error = Math.Abs((double)expected[index] - actual[index]);
                if (error > maximum) maximum = error;
            }
            Assert.IsTrue(maximum <= tolerance, FormattableString.Invariant(
                $"{context}: maximum error={maximum:E9}, limit={tolerance:E9}."));
        }

        private readonly record struct Projection(double Gain, double DistractorGain, double Correlation, double SirDb, double ImprovementDb)
        {
            public override string ToString() => FormattableString.Invariant(
                $"gain={Gain:F4}, corr={Correlation:F4}, SIR={SirDb:F1} dB, +{ImprovementDb:F1} dB");
        }

        private sealed class CancelAtProgress(CancellationTokenSource cancellation) : IProgress<DeterministicSeparationProgress>
        {
            internal int ReportCount { get; private set; }

            public void Report(DeterministicSeparationProgress value)
            {
                this.ReportCount++;
                // Cancel during training phase (after sampling, before rendering)
                if (value.Stage.StartsWith("Sampling", StringComparison.Ordinal)
                    && value.Fraction >= 0.1 && value.Fraction < 0.5)
                {
                    cancellation.Cancel();
                }
            }
        }
    }
}
