using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Processors_V4;

namespace ModularAudience.Audio.Tests
{
    [TestClass]
    [DoNotParallelize]
    public sealed class DeterministicSeparationTests
    {
        private const int SampleRate = 8000;

        [DataTestMethod]
        [DataRow(1, 1, "impulses")]
        [DataRow(1, 2, "antiphase")]
        [DataRow(47, 1, "mixed")]
        [DataRow(47, 2, "independent")]
        [DataRow(2087, 1, "impulses")]
        [DataRow(2087, 2, "impulses")]
        [DataRow(2087, 1, "dc")]
        [DataRow(2087, 2, "dc")]
        [DataRow(2087, 1, "nyquist")]
        [DataRow(2087, 2, "nyquist")]
        [DataRow(2087, 2, "antiphase")]
        [DataRow(2087, 2, "independent")]
        public void UnityMaskReconstructsCenteredHannAcrossCoreBlocks(int frames, int channels, string signal)
        {
            float[] samples = ReconstructionSignal(frames, channels, signal);
            float[] original = (float[]) samples.Clone();
            DeterministicAudioSnapshot snapshot = new(samples, SampleRate, channels, signal, 0, string.Empty);
            DeterministicSeparationSettings settings = SmallSettings() with { WindowSize = 256, BlockFrames = 8 };
            double[] window = DeterministicSpectrogram.CreateWindow(settings.WindowSize);
            Assert.AreEqual(0.0, window[0], "The centered analysis must use a periodic Hann window.");
            Assert.AreEqual(1.0, window[128], 1e-15, "The window center must have unity gain.");
            Assert.AreEqual(0.5, window[64], 1e-15, "The quarter-window Hann coefficient is incorrect.");
            Assert.IsTrue(window[^1] > 0, "A symmetric Hann endpoint must not replace the periodic window.");

            float[] reconstructed = ReconstructUnity(snapshot, settings, window);

            AssertSamplesClose(original, reconstructed, 3e-6, $"Unity mask: {signal}, {channels} channels, {frames} frames");
            CollectionAssert.AreEqual(original, samples, "STFT/synthesis must not modify the snapshot.");
        }

        [TestMethod]
        public async Task IndependentlyModulatedTonesProduceTwoUsefulSeparatedGroups()
        {
            var sources = KnownMixture(12000);
            using AudioTestScope scope = new();
            AudioObj audio = scope.Create(sources.Mix, SampleRate);
            DeterministicSeparationAnalysis analysis = await DeterministicSeparationProcessor.AnalyzeAsync(
                audio, SmallSettings() with { Iterations = 40 });
            DeterministicSeparationResult result = await RenderOwned(scope, analysis);

            Assert.AreEqual(analysis.Sources.Count + 1, result.Stems.Count, "Each acoustic group and Residual must be returned.");
            Assert.IsTrue(result.Stems[^1].Name.EndsWith(" - Residual", StringComparison.Ordinal), "The last stem must be Residual.");
            AssertUsefulSeparation(analysis, result, sources.Low, sources.High, sources.Mix);
        }

        [TestMethod]
        public async Task RepeatedRunsAndThreadCountsPreserveDescriptorsSamplesAndInput()
        {
            using AudioTestScope scope = new();
            float[] input = AudioTestData.Stereo(KnownMixture(4099).Mix, true);
            float[] original = (float[]) input.Clone();
            AudioObj source = scope.Create(input, SampleRate, 2);
            DeterministicSeparationSettings settings = SmallSettings();
            int globalThreads = MathNet.Numerics.Control.MaxDegreeOfParallelism;
            DeterministicSeparationAnalysis baseline = await DeterministicSeparationProcessor.AnalyzeAsync(source, settings);
            DeterministicSeparationResult expected = await RenderOwned(scope, baseline);
            Assert.IsTrue(baseline.Sources.Count > 0, "Antiphase stereo must not disappear during source analysis.");

            foreach (int threads in new[] { 1, Math.Min(2, Environment.ProcessorCount) })
            {
                DeterministicSeparationAnalysis repeated = await DeterministicSeparationProcessor.AnalyzeAsync(
                    source, settings with { Threads = threads });
                AssertSameAnalysis(baseline, repeated);
                AssertSameStems(expected, await RenderOwned(scope, repeated), $"Repeated analysis, Threads={threads}");
                CollectionAssert.AreEqual(original, input, "Analysis and rendering must leave the caller's buffer unchanged.");
                Assert.AreSame(input, source.Data, "Separation must not replace the caller's buffer.");
                Assert.AreEqual(globalThreads, MathNet.Numerics.Control.MaxDegreeOfParallelism,
                    "Per-operation thread settings must not modify MathNet's process-wide configuration.");
            }
        }

        [DataTestMethod]
        [DataRow(1)]
        [DataRow(2)]
        public async Task SnapshotSurvivesCallerMutationAndEverySelectionOwnsFreshStems(int channels)
        {
            using AudioTestScope scope = new();
            float[] mono = KnownMixture(4099).Mix;
            float[] input = channels == 1 ? mono : AudioTestData.Stereo(mono, true);
            float[] original = (float[]) input.Clone();
            AudioObj source = scope.Create(input, SampleRate, channels);
            source.Name = "Snapshot source";
            source.Bpm = 123;
            source.Key = "Cm";
            DeterministicSeparationAnalysis analysis = await DeterministicSeparationProcessor.AnalyzeAsync(source, SmallSettings());
            Assert.IsTrue(analysis.Sources.Count > 0, "The selection regression requires at least one detected group.");
            Array.Fill(input, 0.99f);
            source.Data = [-0.25f];
            source.Name = "Changed caller";
            source.SampleRate = 16000;
            source.Channels = 1;
            source.Length = 1;
            source.Duration = TimeSpan.FromSeconds(99);
            source.Bpm = 7;
            source.Key = "F#";

            DeterministicSeparationResult none = await RenderOwned(scope, analysis, []);
            DeterministicSeparationResult again = await RenderOwned(scope, analysis, []);
            DeterministicSeparationResult subset = await RenderOwned(scope, analysis, [analysis.Sources[0].Id]);

            AssertResidualExactly(original, none);
            AssertResidualExactly(original, again);
            Assert.AreEqual(2, subset.Stems.Count, "A one-group selection must produce one group plus Residual.");
            Assert.AreEqual($"Snapshot source - {analysis.Sources[0].Name}", subset.Stems[0].Name);
            AssertReconstruction(original, subset);
            AudioObj[] stems = [.. none.Stems, .. again.Stems, .. subset.Stems];
            AssertSnapshotMetadata(analysis, stems, original.Length, channels, source.Id);
            Assert.AreNotSame(none.Stems[0].Data, again.Stems[0].Data, "Repeated renders must own different buffers.");
            none.Stems[0].Data[0] = 0.875f;
            CollectionAssert.AreEqual(original, again.Stems[0].Data, "Mutating a returned stem must not affect another render.");
            CollectionAssert.AreEqual(original, analysis.Source.Samples, "Returned stems must not alias the analyzed snapshot.");
        }

        [DataTestMethod]
        [DataRow(1, 1, true)]
        [DataRow(37, 2, true)]
        [DataRow(2087, 1, true)]
        [DataRow(2087, 2, true)]
        [DataRow(1, 1, false)]
        [DataRow(1, 2, false)]
        [DataRow(43, 1, false)]
        [DataRow(43, 2, false)]
        public async Task SilenceAndSubWindowAudioRemainFiniteAndReconstruct(int frames, int channels, bool silent)
        {
            using AudioTestScope scope = new();
            float[] data = silent ? new float[frames * channels] : ReconstructionSignal(frames, channels, "antiphase");
            AudioObj source = scope.Create(data, SampleRate, channels);
            DeterministicSeparationAnalysis analysis = await DeterministicSeparationProcessor.AnalyzeAsync(
                source, SmallSettings() with { WindowSize = 256, Iterations = 4 });
            DeterministicSeparationResult result = await RenderOwned(scope, analysis);
            AssertReconstruction(data, result);
            foreach (DeterministicSourceDescriptor descriptor in analysis.Sources)
            {
                Assert.IsTrue(double.IsFinite(descriptor.Confidence) && double.IsFinite(descriptor.EnergyFraction)
                    && double.IsFinite(descriptor.FundamentalHz) && double.IsFinite(descriptor.Pan),
                    $"Short-signal descriptor contains a non-finite value: {descriptor}");
            }
            if (silent)
            {
                Assert.AreEqual(0, analysis.EstimatedSignalRank, "Silence must have rank zero.");
                Assert.AreEqual(0, analysis.Sources.Count, "Silence must not create artificial source groups.");
                AssertResidualExactly(data, result);
            }
            else
            {
                Assert.IsTrue(analysis.EstimatedSignalRank > 0, "A nonzero short signal must not be classified as silence.");
            }
        }

        [DataTestMethod]
        [DataRow("nan", null)]
        [DataRow("positive-infinity", null)]
        [DataRow("negative-infinity", null)]
        [DataRow("empty", null)]
        [DataRow("unaligned-stereo", null)]
        [DataRow("zero-channels", null)]
        [DataRow("three-channels", null)]
        [DataRow("zero-sample-rate", null)]
        [DataRow("fft-small", "WindowSize")]
        [DataRow("fft-non-power", "WindowSize")]
        [DataRow("fft-large", "WindowSize")]
        [DataRow("median-frames-even", "MedianFrames")]
        [DataRow("median-frames-small", "MedianFrames")]
        [DataRow("median-bins-even", "MedianBins")]
        [DataRow("median-bins-large", "MedianBins")]
        public async Task InvalidAudioAndFftOrMedianSettingsAreExplicitlyRejected(string scenario, string? parameter)
        {
            using AudioTestScope scope = new();
            float value = scenario switch
            {
                "nan" => float.NaN,
                "positive-infinity" => float.PositiveInfinity,
                "negative-infinity" => float.NegativeInfinity,
                _ => 0.25f
            };
            float[] samples = scenario == "empty" ? [] : [value, 0.125f, -0.25f];
            AudioObj source = scope.Create(samples, SampleRate);
            source.Channels = scenario switch { "unaligned-stereo" => 2, "zero-channels" => 0, "three-channels" => 3, _ => 1 };
            if (scenario == "zero-sample-rate") source.SampleRate = 0;
            Func<Task> analyze = async () => { await DeterministicSeparationProcessor.AnalyzeAsync(source, InvalidSettings(scenario)); };
            if (parameter is null)
            {
                await Assert.ThrowsExactlyAsync<ArgumentException>(analyze, $"Invalid audio was accepted: {scenario}");
            }
            else
            {
                ArgumentOutOfRangeException error = await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
                    analyze, $"Invalid settings were accepted: {scenario}");
                Assert.AreEqual(parameter, error.ParamName, "Validation must identify the invalid setting.");
            }
        }

        [DataTestMethod]
        [DataRow("analysis", true)]
        [DataRow("analysis", false)]
        [DataRow("render", true)]
        [DataRow("render", false)]
        public async Task CancellationBeforeAndDuringWorkReturnsNoPartialResultAndModelRemainsReusable(string phase, bool before)
        {
            using AudioTestScope scope = new();
            AudioObj source = scope.Create(KnownMixture(4099).Mix, SampleRate);
            DeterministicSeparationSettings settings = SmallSettings();
            DeterministicSeparationAnalysis analysis = await DeterministicSeparationProcessor.AnalyzeAsync(source, settings);
            DeterministicSeparationResult reference = await RenderOwned(scope, analysis);
            Assert.IsTrue(analysis.Sources.Count > 0, "Cancellation must exercise active source rendering, not a residual-only shortcut.");
            using CancellationTokenSource cancellation = new();
            CancelAtProgress progress = new(cancellation, phase);
            if (before) cancellation.Cancel();
            DeterministicSeparationAnalysis? partialAnalysis = null;
            DeterministicSeparationResult? partialResult = null;

            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            {
                if (phase == "analysis")
                    partialAnalysis = await DeterministicSeparationProcessor.AnalyzeAsync(source, settings, progress, cancellation.Token);
                else
                    partialResult = await RenderOwned(scope, analysis, progress: progress, cancellationToken: cancellation.Token);
            }, $"Cancellation was ignored: phase={phase}, before={before}, last progress={progress.CancelledAt}");

            Assert.IsNull(partialAnalysis, "Canceled analysis must not publish a partial model.");
            Assert.IsNull(partialResult, "Canceled rendering must not publish partial stems.");
            Assert.IsTrue(cancellation.IsCancellationRequested, "The cancellation trigger was not reached.");
            if (before) Assert.AreEqual(0, progress.ReportCount, "A pre-canceled task must not start processing.");
            else Assert.IsNotNull(progress.CancelledAt, "Cancellation must occur synchronously inside an active processing stage.");
            AssertSameStems(reference, await RenderOwned(scope, analysis), "Model reuse after cancellation");
            if (phase == "analysis")
            {
                DeterministicSeparationAnalysis retry = await DeterministicSeparationProcessor.AnalyzeAsync(source, settings);
                AssertSameAnalysis(analysis, retry);
            }
        }

        private static DeterministicSeparationSettings SmallSettings() => new()
        {
            WindowSize = 512,
            MaxComponents = 6,
            Iterations = 10,
            AnalysisFrames = 128,
            BlockFrames = 16,
            MedianFrames = 9,
            MedianBins = 9,
            Threads = 1
        };

        private static DeterministicSeparationSettings InvalidSettings(string scenario)
        {
            DeterministicSeparationSettings settings = SmallSettings();
            return scenario switch
            {
                "fft-small" => settings with { WindowSize = 128 },
                "fft-non-power" => settings with { WindowSize = 300 },
                "fft-large" => settings with { WindowSize = 32768 },
                "median-frames-even" => settings with { MedianFrames = 4 },
                "median-frames-small" => settings with { MedianFrames = 1 },
                "median-bins-even" => settings with { MedianBins = 4 },
                "median-bins-large" => settings with { MedianBins = 67 },
                _ => settings
            };
        }

        private static float[] ReconstructionSignal(int frames, int channels, string signal)
        {
            float[] data = new float[frames * channels];
            for (int frame = 0; frame < frames; frame++)
            {
                double mixed = 0.125 + 0.3 * Math.Sin(2 * Math.PI * 437 * frame / SampleRate) + (frame % 2 == 0 ? 0.0625 : -0.0625);
                float left = signal switch
                {
                    "impulses" => frame == 0 ? 0.75f : frame == frames - 1 ? -0.625f : frame is 511 or 512 or 513 ? 0.5f : 0,
                    "dc" => 0.375f,
                    "nyquist" => frame % 2 == 0 ? 0.5f : -0.5f,
                    _ => (float) mixed
                };
                data[frame * channels] = left;
                if (channels == 2)
                {
                    data[frame * channels + 1] = signal switch
                    {
                        "antiphase" => -left,
                        "independent" => (float) (0.21 * Math.Cos(2 * Math.PI * 1800 * frame / SampleRate) - 0.08),
                        _ => 0.4f * left
                    };
                }
            }
            return data;
        }

        private static float[] ReconstructUnity(DeterministicAudioSnapshot source, DeterministicSeparationSettings settings, double[] window)
        {
            float[][] output = [new float[source.Samples.Length]];
            int total = DeterministicSpectrogram.FrameCount(source, settings);
            for (int start = 0; start < total; start += settings.BlockFrames)
            {
                int count = Math.Min(settings.BlockFrames, total - start);
                int first = Math.Max(0, start - 3);
                int end = Math.Min(total, start + count + 3);
                DeterministicSpectrogram block = DeterministicSpectrogram.Read(source, settings, first, end - first,
                    keepSpectra: true, cancellationToken: CancellationToken.None);
                float[][][] masks = Enumerable.Range(0, block.Frames)
                    .Select(_ => new[] { Enumerable.Repeat(1f, settings.WindowSize / 2 + 1).ToArray() }).ToArray();
                DeterministicSynthesis.AddBlock(source, settings, block, masks, [0], output,
                    start, count, window, CancellationToken.None);
            }
            return output[0];
        }

        private static (float[] Mix, float[] Low, float[] High) KnownMixture(int frames)
        {
            float[] low = new float[frames];
            float[] high = new float[frames];
            float[] mix = new float[frames];
            for (int frame = 0; frame < frames; frame++)
            {
                double time = frame / (double) SampleRate;
                double fade = Math.Min(1, Math.Min(frame, frames - 1 - frame) / (SampleRate * 0.02));
                double lowEnvelope = 0.05 + 0.95 * Math.Pow(Math.Sin(Math.PI * (time / 0.50 + 0.1)), 2);
                double highEnvelope = 0.05 + 0.95 * Math.Pow(Math.Sin(Math.PI * (time / 0.31 + 0.4)), 2);
                low[frame] = (float) (0.4 * fade * lowEnvelope * Math.Sin(2 * Math.PI * 250 * time));
                high[frame] = (float) (0.4 * fade * highEnvelope * Math.Sin(2 * Math.PI * 1406.25 * time + 0.37));
                mix[frame] = low[frame] + high[frame];
            }
            return (mix, low, high);
        }

        private static void AssertUsefulSeparation(DeterministicSeparationAnalysis analysis, DeterministicSeparationResult result,
            float[] low, float[] high, float[] mix)
        {
            AudioObj[] groups = result.Stems.Take(analysis.Sources.Count).ToArray();
            foreach (AudioObj stem in groups)
            {
                Assert.AreEqual(low.Length, stem.Data.Length, $"{stem.Name}: source quality must be measured over the complete input duration.");
                AssertFinite(stem.Data, stem.Name);
            }
            Projection[] lowScores = groups.Select(stem => MeasureProjection(stem.Data, low, high)).ToArray();
            Projection[] highScores = groups.Select(stem => MeasureProjection(stem.Data, high, low)).ToArray();
            string measurements = FormattableString.Invariant($"Rank={analysis.EstimatedSignalRank}, groups={groups.Length}; ")
                + $"input low: {MeasureProjection(mix, low, high)}; input high: {MeasureProjection(mix, high, low)}"
                + Environment.NewLine + string.Join(Environment.NewLine, groups.Select((stem, index) =>
                    $"{stem.Name}: low [{lowScores[index]}]; high [{highScores[index]}]"));
            bool distinctUsefulGroups = Enumerable.Range(0, groups.Length).Any(lowIndex => lowScores[lowIndex].Useful
                && Enumerable.Range(0, groups.Length).Any(highIndex => highIndex != lowIndex && highScores[highIndex].Useful));
            Assert.IsTrue(groups.Length >= 2 && distinctUsefulGroups,
                "Two distinct non-Residual groups must each retain target gain >= 0.20, correlation >= 0.85, "
                + "and improve target/distractor SIR by >= 6 dB over the input mixture. " + measurements);
        }

        private static Projection MeasureProjection(float[] stem, float[] target, float[] distractor)
        {
            double targetEnergy = Dot(target, target);
            double distractorEnergy = Dot(distractor, distractor);
            double cross = Dot(target, distractor);
            double targetDot = Dot(stem, target);
            double distractorDot = Dot(stem, distractor);
            double determinant = targetEnergy * distractorEnergy - cross * cross;
            Assert.IsTrue(determinant > 0, "Known source references must be linearly independent.");
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
            for (int index = 0; index < first.Length; index++) sum += (double) first[index] * second[index];
            return sum;
        }

        private static async Task<DeterministicSeparationResult> RenderOwned(AudioTestScope scope,
            DeterministicSeparationAnalysis analysis, int[]? selected = null,
            IProgress<DeterministicSeparationProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            DeterministicSeparationResult result = await DeterministicSeparationProcessor.SeparateAsync(
                analysis, selected, progress, cancellationToken);
            scope.Own(result.Stems);
            return result;
        }

        private static void AssertSameAnalysis(DeterministicSeparationAnalysis expected, DeterministicSeparationAnalysis actual)
        {
            Assert.AreEqual(expected.EstimatedSignalRank, actual.EstimatedSignalRank, "Effective rank must be deterministic.");
            CollectionAssert.AreEqual(expected.Sources.ToArray(), actual.Sources.ToArray(),
                "Source IDs, ordering, names and numeric descriptors must be deterministic.");
        }

        private static void AssertSameStems(DeterministicSeparationResult expected, DeterministicSeparationResult actual, string context)
        {
            Assert.AreEqual(expected.Stems.Count, actual.Stems.Count, $"{context}: stem count changed.");
            for (int index = 0; index < expected.Stems.Count; index++)
            {
                Assert.AreEqual(expected.Stems[index].Name, actual.Stems[index].Name, $"{context}: stem ordering changed.");
                AssertSamplesClose(expected.Stems[index].Data, actual.Stems[index].Data, 1e-7, $"{context}: {actual.Stems[index].Name}");
            }
        }

        private static void AssertSnapshotMetadata(DeterministicSeparationAnalysis analysis, AudioObj[] stems,
            int sampleCount, int channels, Guid sourceId)
        {
            Assert.AreEqual(SampleRate, analysis.SampleRate, "Analysis sample rate must be snapshotted.");
            Assert.AreEqual(channels, analysis.Channels, "Analysis channel count must be snapshotted.");
            Assert.AreEqual((long) sampleCount, analysis.SampleCount, "Analysis length must be snapshotted.");
            Assert.AreEqual("Snapshot source", analysis.SourceName, "Analysis name must be snapshotted.");
            HashSet<Guid> ids = [sourceId];
            foreach (AudioObj stem in stems)
            {
                Assert.IsTrue(stem.Id != Guid.Empty && ids.Add(stem.Id), "Every returned AudioObj must have a fresh unique ID.");
                Assert.AreEqual(SampleRate, stem.SampleRate, "Stem sample rate must come from the snapshot.");
                Assert.AreEqual(channels, stem.Channels, "Stem channel count must come from the snapshot.");
                Assert.AreEqual((long) sampleCount, stem.Length, "Stem length must come from the snapshot.");
                Assert.AreEqual(sampleCount, stem.Data.Length, "Stem buffer length must match its metadata.");
                Assert.AreEqual(TimeSpan.FromSeconds(sampleCount / (double) (SampleRate * channels)), stem.Duration,
                    "Stem duration must retain the original sample-frame count.");
                Assert.AreEqual(123f, stem.Bpm, "Stem tempo must come from the snapshot.");
                Assert.AreEqual("Cm", stem.Key, "Stem key must come from the snapshot.");
                Assert.AreEqual(32, stem.BitDepth, "Stems must retain float sample precision.");
                Assert.AreNotSame(analysis.Source.Samples, stem.Data, "A returned buffer must not alias the analyzed snapshot.");
            }
        }

        private static void AssertResidualExactly(float[] original, DeterministicSeparationResult result)
        {
            Assert.AreEqual(1, result.Stems.Count, "Selecting no groups must return Residual only.");
            Assert.IsTrue(result.Stems[0].Name.EndsWith(" - Residual", StringComparison.Ordinal), "The unselected output must be named Residual.");
            CollectionAssert.AreEqual(original, result.Stems[0].Data, "Residual-only output must exactly preserve the snapshot.");
            Assert.AreEqual(0.0, result.ReconstructionError, "Residual-only reconstruction must have zero error.");
        }

        private static void AssertReconstruction(float[] original, DeterministicSeparationResult result)
        {
            double[] sum = new double[original.Length];
            foreach (AudioObj stem in result.Stems)
            {
                Assert.AreEqual(original.Length, stem.Data.Length, $"{stem.Name}: sample count changed.");
                AssertFinite(stem.Data, stem.Name);
                for (int sample = 0; sample < sum.Length; sample++) sum[sample] += stem.Data[sample];
            }
            double error = Enumerable.Range(0, sum.Length).Max(sample => Math.Abs(sum[sample] - original[sample]));
            Assert.IsTrue(error <= 1e-6, $"Selected groups plus Residual must reconstruct the snapshot; maximum error={error:E9}.");
            Assert.IsTrue(double.IsFinite(result.ReconstructionError) && result.ReconstructionError <= 1e-6,
                $"Reported reconstruction error is invalid: {result.ReconstructionError:E9}.");
        }

        private static void AssertSamplesClose(float[] expected, float[] actual, double tolerance, string context)
        {
            Assert.AreEqual(expected.Length, actual.Length, $"{context}: sample count changed.");
            AssertFinite(actual, context);
            double maximum = 0;
            int worst = 0;
            for (int index = 0; index < expected.Length; index++)
            {
                double error = Math.Abs((double) expected[index] - actual[index]);
                if (error <= maximum) continue;
                maximum = error;
                worst = index;
            }
            Assert.IsTrue(maximum <= tolerance, FormattableString.Invariant(
                $"{context}: maximum error={maximum:E9}, limit={tolerance:E9}, interleaved sample={worst}, expected={expected[worst]:G9}, actual={actual[worst]:G9}."));
        }

        private static void AssertFinite(float[] samples, string context)
        {
            for (int index = 0; index < samples.Length; index++)
            {
                if (!float.IsFinite(samples[index])) Assert.Fail($"{context}: non-finite output at interleaved sample {index}: {samples[index]}.");
            }
        }

        private readonly record struct Projection(double Gain, double DistractorGain, double Correlation, double SirDb, double ImprovementDb)
        {
            internal bool Useful => this.Gain >= 0.20 && this.Correlation >= 0.85 && this.ImprovementDb >= 6;
            public override string ToString() => FormattableString.Invariant(
                $"gain={this.Gain:F6}, distractor gain={this.DistractorGain:F6}, correlation={this.Correlation:F6}, SIR={this.SirDb:F3} dB, improvement={this.ImprovementDb:F3} dB");
        }

        private sealed class CancelAtProgress(CancellationTokenSource cancellation, string phase) : IProgress<DeterministicSeparationProgress>
        {
            internal int ReportCount { get; private set; }
            internal DeterministicSeparationProgress? CancelledAt { get; private set; }

            public void Report(DeterministicSeparationProgress value)
            {
                this.ReportCount++;
                bool active = phase == "analysis"
                    ? value.Stage == "Learning deterministic IS-NMF dictionary" && value.Fraction >= 0.5 && value.Fraction < 0.86
                    : value.Stage.StartsWith("Separating frames ", StringComparison.Ordinal) && value.Fraction >= 0.1 && value.Fraction < 0.94;
                if (!active || this.CancelledAt != null) return;
                this.CancelledAt = value;
                cancellation.Cancel();
            }
        }
    }
}
