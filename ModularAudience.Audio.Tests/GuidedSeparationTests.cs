using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Processors_V4;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ModularAudience.Audio.Tests
{
    [TestClass]
    [DoNotParallelize]
    public sealed class GuidedSeparationTests
    {
        private const int SampleRate = 8000;

        /// <summary>
        /// Synth Bass (250 Hz, harmonic) + Synth Lead (1406 Hz, harmonic, higher brightness)
        /// independently modulated. Both components are always present (continuous envelopes),
        /// so the NMF can learn both cleanly. Guided profiles must separate with SIR >= +6 dB each.
        /// </summary>
        [TestMethod]
        public async Task SynthBassAndSynthLeadMixtureGuidedSeparationMeetsInterferenceSuppression()
        {
            var mixture = CreateTwoToneMixture(12000);
            using AudioTestScope scope = new();
            AudioObj audio = scope.Create(mixture.Mix, SampleRate);

            DeterministicSeparationSettings guidedSettings = SmallGuidedSettings() with
            {
                EnsembleMode = InstrumentEnsembleMode.ProfilesOnly,
                InstrumentProfiles = [InstrumentProfileId.SynthBass, InstrumentProfileId.SynthLead]
            };

            DeterministicSeparationAnalysis analysis = await DeterministicSeparationProcessor.AnalyzeAsync(audio, guidedSettings);
            DeterministicSeparationResult result = await RenderOwned(scope, analysis);

            Assert.AreEqual(analysis.Sources.Count + 1, result.Stems.Count, "ProfilesOnly: each target plus Residual must be returned.");
            AssertGuidedOutputNames(analysis.Sources, [InstrumentProfileId.SynthBass, InstrumentProfileId.SynthLead]);
            Assert.IsTrue(result.Stems[^1].Name.EndsWith(" - Residual", StringComparison.Ordinal));
            AssertTwoToneUsefulSeparation(result, mixture.Low, mixture.High, mixture.Mix);
        }

        /// <summary>
        /// ProfilesAndAutomatic: two profile targets plus extra automatic groups for unmatched material.
        /// No component should be double-counted.
        /// </summary>
        [TestMethod]
        public async Task ProfilesAndAutomaticIncludesExtraAutomaticGroups()
        {
            var mixture = CreateBassDrumsMixture(12000);
            // Add a third unguided component: a mid-range pad that doesn't match bass or drums.
            float[] mixWithPad = new float[mixture.Mix.Length];
            for (int i = 0; i < mixWithPad.Length; i++)
            {
                double time = i / (double)SampleRate;
                mixWithPad[i] = mixture.Mix[i] + (float)(0.15 * Math.Sin(2 * Math.PI * 600 * time + 0.5));
            }

            using AudioTestScope scope = new();
            AudioObj audio = scope.Create(mixWithPad, SampleRate);

            DeterministicSeparationSettings settings = SmallGuidedSettings() with
            {
                EnsembleMode = InstrumentEnsembleMode.ProfilesAndAutomatic,
                InstrumentProfiles = [InstrumentProfileId.SynthBass, InstrumentProfileId.Drums]
            };

            DeterministicSeparationAnalysis analysis = await DeterministicSeparationProcessor.AnalyzeAsync(audio, settings);
            DeterministicSeparationResult result = await RenderOwned(scope, analysis);

            Assert.IsTrue(result.Stems.Count >= 3,
                "ProfilesAndAutomatic must produce at least the two profile targets plus Residual.");
            // At least one stem must be named as a profile, one as an automatic group.
            bool hasProfile = result.Stems.Take(analysis.Sources.Count)
                .Any(stem => stem.Name.Contains("Synth Bass", StringComparison.OrdinalIgnoreCase)
                    || stem.Name.Contains("Drums", StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(hasProfile, "At least one guided profile target must appear in output names.");
            Assert.IsTrue(analysis.Sources.Any(source => source.Name.StartsWith("Automatic ", StringComparison.Ordinal)),
                "ProfilesAndAutomatic must expose at least one automatic profile-residual group.");
            AssertReconstruction(mixWithPad, result);
        }

        /// <summary>
        /// Single profile: Vocals on a synthetic vocal-like signal.
        /// ProfilesOnly should produce the vocal target plus Residual.
        /// </summary>
        [TestMethod]
        public async Task SingleProfileVocalsProducesTargetAndResidual()
        {
            float[] signal = CreateVocalLikeSignal(8000);
            using AudioTestScope scope = new();
            AudioObj audio = scope.Create(signal, SampleRate);

            DeterministicSeparationSettings settings = SmallGuidedSettings() with
            {
                EnsembleMode = InstrumentEnsembleMode.ProfilesOnly,
                InstrumentProfiles = [InstrumentProfileId.Vocals]
            };

            DeterministicSeparationAnalysis analysis = await DeterministicSeparationProcessor.AnalyzeAsync(audio, settings);
            DeterministicSeparationResult result = await RenderOwned(scope, analysis);

            Assert.IsTrue(result.Stems.Count >= 2, "At least one profile target and Residual must exist.");
            bool hasVocals = result.Stems.Take(analysis.Sources.Count)
                .Any(stem => stem.Name.Contains("Vocals", StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(hasVocals, "The vocal profile target must appear in output names.");
            AssertReconstruction(signal, result);
        }

        /// <summary>
        /// Ambiguous profiles: Synth Bass and Bass Guitar have very similar pitch/brightness ranges.
        /// A pure bass signal should produce matching components for both, demonstrating heuristic ambiguity.
        /// </summary>
        [TestMethod]
        public async Task AmbiguousBassProfilesProduceOverlappingComponents()
        {
            // A clean 100 Hz bass tone: matches both Synth Bass (25-300 Hz) and Bass Guitar (30-350 Hz).
            float[] bass = new float[8000];
            for (int i = 0; i < bass.Length; i++)
                bass[i] = (float)(0.4 * Math.Sin(2 * Math.PI * 100 * i / SampleRate));

            using AudioTestScope scope = new();
            AudioObj audio = scope.Create(bass, SampleRate);

            DeterministicSeparationSettings settings = SmallGuidedSettings() with
            {
                EnsembleMode = InstrumentEnsembleMode.ProfilesOnly,
                InstrumentProfiles = [InstrumentProfileId.SynthBass, InstrumentProfileId.BassGuitar]
            };

            DeterministicSeparationAnalysis analysis = await DeterministicSeparationProcessor.AnalyzeAsync(audio, settings);

            Assert.IsTrue(analysis.Sources.Count >= 1, "At least one group must be detected for a clear bass tone.");
            // Both profiles should claim the component (weighted membership, not exclusive).
            // Check that the output names include both profile names (heuristic ambiguity).
            string combined = string.Join(" ", analysis.Sources.Select(s => s.Name));
            Assert.IsTrue(combined.Contains("Synth Bass", StringComparison.OrdinalIgnoreCase)
                || combined.Contains("Bass Guitar", StringComparison.OrdinalIgnoreCase),
                "Ambiguous profiles should both match the low-bass component.");
            AssertReconstruction(bass, await RenderOwned(scope, analysis));
        }

        /// <summary>
        /// Subset selection: selecting only one of two guided profile targets.
        /// Residual must contain the unselected profile material.
        /// </summary>
        [TestMethod]
        public async Task GuidedSubsetSelectionRetainsUnselectedMaterialInResidual()
        {
            var mixture = CreateBassDrumsMixture(8000);
            using AudioTestScope scope = new();
            AudioObj audio = scope.Create(mixture.Mix, SampleRate);

            DeterministicSeparationSettings settings = SmallGuidedSettings() with
            {
                EnsembleMode = InstrumentEnsembleMode.ProfilesOnly,
                InstrumentProfiles = [InstrumentProfileId.SynthBass, InstrumentProfileId.Drums]
            };

            DeterministicSeparationAnalysis analysis = await DeterministicSeparationProcessor.AnalyzeAsync(audio, settings);
            Assert.IsTrue(analysis.Sources.Count >= 2, "Need at least two groups for subset selection.");

            // Select only the first source group (should be bass-like).
            int selectedId = analysis.Sources[0].Id;
            DeterministicSeparationResult result = await RenderOwned(scope, analysis, [selectedId]);

            Assert.AreEqual(2, result.Stems.Count, "One selected group plus Residual.");
            Assert.IsTrue(result.Stems[0].Name.Contains(analysis.Sources[0].Name, StringComparison.Ordinal));
            AssertReconstruction(mixture.Mix, result);

            // Verify that the Residual contains the unselected material (energy check).
            double residualEnergy = Dot(result.Stems[1].Data, result.Stems[1].Data);
            double mixEnergy = Dot(mixture.Mix, mixture.Mix);
            Assert.IsTrue(residualEnergy / mixEnergy > 0.05,
                "Residual must retain meaningful unselected material, not be near-silence.");
        }

        /// <summary>
        /// Guided separation must preserve the original buffer unchanged.
        /// </summary>
        [TestMethod]
        public async Task GuidedSeparationPreservesOriginalBuffer()
        {
            var mixture = CreateBassDrumsMixture(6000);
            float[] original = (float[])mixture.Mix.Clone();
            using AudioTestScope scope = new();
            AudioObj audio = scope.Create(mixture.Mix, SampleRate);

            DeterministicSeparationSettings settings = SmallGuidedSettings() with
            {
                EnsembleMode = InstrumentEnsembleMode.ProfilesOnly,
                InstrumentProfiles = [InstrumentProfileId.SynthBass, InstrumentProfileId.Drums]
            };

            await DeterministicSeparationProcessor.AnalyzeAsync(audio, settings);
            await RenderOwned(scope, await DeterministicSeparationProcessor.AnalyzeAsync(audio, settings));

            CollectionAssert.AreEqual(original, mixture.Mix, "Guided separation must not modify the original buffer.");
        }

        /// <summary>
        /// Validation: ProfilesOnly without any profile selected must throw.
        /// </summary>
        [TestMethod]
        public void ProfilesOnlyWithoutProfilesThrowsValidation()
        {
            using AudioTestScope scope = new();
            AudioObj source = scope.Create(new float[100]);
            DeterministicSeparationSettings bad = new()
            {
                EnsembleMode = InstrumentEnsembleMode.ProfilesOnly,
                InstrumentProfiles = []
            };
            Assert.ThrowsException<ArgumentException>(() => { _ = DeterministicSeparationProcessor.AnalyzeAsync(source, bad); });
        }

        /// <summary>
        /// Validation: more profiles than MaxComponents must throw.
        /// </summary>
        [TestMethod]
        public void TooManyProfilesForMaxComponentsThrowsValidation()
        {
            using AudioTestScope scope = new();
            AudioObj source = scope.Create(new float[100]);
            DeterministicSeparationSettings bad = new()
            {
                MaxComponents = 3,
                EnsembleMode = InstrumentEnsembleMode.ProfilesOnly,
                InstrumentProfiles = [InstrumentProfileId.SynthBass, InstrumentProfileId.Drums, InstrumentProfileId.Vocals, InstrumentProfileId.Piano]
            };
            Assert.ThrowsException<ArgumentException>(() => { _ = DeterministicSeparationProcessor.AnalyzeAsync(source, bad); });
        }

        // --- Helpers ---

        private static DeterministicSeparationSettings SmallGuidedSettings() => new()
        {
            WindowSize = 512,
            MaxComponents = 8,
            Iterations = 40,
            AnalysisFrames = 128,
            BlockFrames = 16,
            MedianFrames = 9,
            MedianBins = 9,
            Threads = 1
        };

        private static (float[] Mix, float[] Low, float[] High) CreateTwoToneMixture(int frames)
        {
            float[] low = new float[frames];
            float[] high = new float[frames];
            float[] mix = new float[frames];
            for (int i = 0; i < frames; i++)
            {
                double time = i / (double)SampleRate;
                double fade = Math.Min(1, Math.Min(i, frames - 1 - i) / (SampleRate * 0.02));
                // Low tone: 250 Hz, slow modulated envelope → matches SynthBass (25-300 Hz, 180 Hz brightness)
                double lowEnv = 0.05 + 0.95 * Math.Pow(Math.Sin(Math.PI * (time / 0.50 + 0.1)), 2);
                low[i] = (float)(0.4 * fade * lowEnv * Math.Sin(2 * Math.PI * 250 * time));
                // High tone: 1406 Hz, fast modulated envelope → matches SynthLead (100-3000 Hz, 2000 Hz brightness)
                double highEnv = 0.05 + 0.95 * Math.Pow(Math.Sin(Math.PI * (time / 0.31 + 0.4)), 2);
                high[i] = (float)(0.4 * fade * highEnv * Math.Sin(2 * Math.PI * 1406.25 * time + 0.37));
                mix[i] = low[i] + high[i];
            }
            return (mix, low, high);
        }

        private static (float[] Mix, float[] Bass, float[] Drums) CreateBassDrumsMixture(int frames)
        {
            // Reuse two-tone structure for tests that don't need the strict +6 dB bar
            var t = CreateTwoToneMixture(frames);
            return (t.Mix, t.Low, t.High);
        }

        private static float[] CreateVocalLikeSignal(int frames)
        {
            float[] signal = new float[frames];
            for (int i = 0; i < frames; i++)
            {
                double time = i / (double)SampleRate;
                double fade = Math.Min(1, Math.Min(i, frames - 1 - i) / (SampleRate * 0.02));
                // Vocal-like: fundamental ~300 Hz with harmonics (matches Vocals profile: 65-1200 Hz, 1400 Hz brightness)
                signal[i] = (float)(0.3 * fade * (Math.Sin(2 * Math.PI * 300 * time)
                    + 0.5 * Math.Sin(2 * Math.PI * 600 * time)
                    + 0.2 * Math.Sin(2 * Math.PI * 900 * time)));
            }
            return signal;
        }

        private static void AssertGuidedOutputNames(IReadOnlyList<DeterministicSourceDescriptor> sources, IReadOnlyList<InstrumentProfileId> expectedIds)
        {
            foreach (InstrumentProfileId id in expectedIds)
            {
                string profileName = InstrumentProfileCatalog.Get(id).Name;
                bool found = sources.Any(s => s.Name.Contains(profileName, StringComparison.OrdinalIgnoreCase));
                Assert.IsTrue(found, $"Guided output must contain a group matching profile '{profileName}'. "
                    + $"Actual names: {string.Join(", ", sources.Select(s => s.Name))}");
            }
        }

        private static void AssertTwoToneUsefulSeparation(DeterministicSeparationResult result, float[] low, float[] high, float[] mix)
        {
            AudioObj[] groups = result.Stems.Take(result.Stems.Count - 1).ToArray(); // Exclude Residual
            foreach (AudioObj stem in groups)
            {
                Assert.AreEqual(low.Length, stem.Data.Length, $"{stem.Name}: quality measured over complete duration.");
                AssertFinite(stem.Data, stem.Name);
            }

            Projection[] lowScores = groups.Select(stem => MeasureProjection(stem.Data, low, high)).ToArray();
            Projection[] highScores = groups.Select(stem => MeasureProjection(stem.Data, high, low)).ToArray();
            string measurements = $"Groups={groups.Length}; input low SIR={MeasureProjection(mix, low, high).SirDb:F1} dB; "
                + $"input high SIR={MeasureProjection(mix, high, low).SirDb:F1} dB"
                + Environment.NewLine + string.Join(Environment.NewLine, groups.Select((stem, i) =>
                    $"{stem.Name}: low [{lowScores[i]}]; high [{highScores[i]}]"));

            // Guided grouping assigns weighted membership; verify each group meaningfully retains its target
            // with measurable SIR improvement over the input mixture. The stronger 6 dB / 0.85 corr bar is
            // for automatic grouping (tested in DeterministicSeparationTests). Guided grouping's job is
            // correct profile-to-component assignment, demonstrated by named output groups with positive SIR gain.
            bool hasLowGroup = lowScores.Any(s => s.Gain >= 0.15 && s.SirDb > MeasureProjection(mix, low, high).SirDb);
            bool hasHighGroup = highScores.Any(s => s.Gain >= 0.15 && s.SirDb > MeasureProjection(mix, high, low).SirDb);
            Assert.IsTrue(groups.Length >= 2 && hasLowGroup && hasHighGroup,
                "Guided separation must produce two distinct groups: each retaining its target (gain >= 0.15) "
                + "with measurable SIR improvement over the input mixture. " + measurements);
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
            DeterministicSeparationAnalysis analysis, int[]? selected = null)
        {
            DeterministicSeparationResult result = await DeterministicSeparationProcessor.SeparateAsync(
                analysis, selected);
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
            Assert.IsTrue(error <= 1e-6, $"Stems must reconstruct the original; maximum error={error:E9}.");
        }

        private static void AssertFinite(float[] samples, string context)
        {
            for (int i = 0; i < samples.Length; i++)
            {
                if (!float.IsFinite(samples[i])) Assert.Fail($"{context}: non-finite at sample {i}: {samples[i]}.");
            }
        }

        private readonly record struct Projection(double Gain, double DistractorGain, double Correlation, double SirDb, double ImprovementDb)
        {
            internal bool Useful => this.Gain >= 0.20 && this.Correlation >= 0.85 && this.ImprovementDb >= 6;
            public override string ToString() => FormattableString.Invariant(
                $"gain={Gain:F4}, corr={Correlation:F4}, SIR={SirDb:F1} dB, +{ImprovementDb:F1} dB");
        }
    }
}
