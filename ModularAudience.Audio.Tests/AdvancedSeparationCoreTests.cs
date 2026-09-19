using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Processors_V4;

namespace ModularAudience.Audio.Tests
{
    [TestClass]
    public sealed class AdvancedSeparationCoreTests
    {
        [DataTestMethod]
        [DataRow(12)]
        [DataRow(24)]
        [DataRow(36)]
        public void CqtRoundTripPreservesEdgesDcNyquistAndMaskedGain(int binsPerOctave)
        {
            ConstantQTransform transform = ConstantQTransform.Create(8000, binsPerOctave, 220);
            double[] input = new double[transform.Length];
            for (int i = 0; i < input.Length; i++)
                input[i] = 0.125 + 0.2 * Math.Sin(2 * Math.PI * 440 * i / 8000) + (i % 2 == 0 ? 0.1 : -0.1);
            input[0] += 0.3;
            input[^1] -= 0.2;
            var coefficients = transform.Forward(input, CancellationToken.None);
            Assert.IsTrue(transform.Bands.Select(band => band.CoefficientCount).Distinct().Count() > 1,
                "Constant-Q bands must have different time resolutions.");
            AssertError(input, transform.Inverse(coefficients, CancellationToken.None), 1, 1e-9);
            foreach (var band in coefficients)
                for (int i = 0; i < band.Length; i++) band[i] *= 0.5;
            AssertError(input, transform.Inverse(coefficients, CancellationToken.None), 0.5, 1e-9);
        }

        [TestMethod]
        public void PyinTracksKnownPitchRejectsSilenceAndIsRepeatable()
        {
            float[] tone = new float[4000];
            for (int i = 0; i < tone.Length; i++) tone[i] = (float) (0.4 * Math.Sin(2 * Math.PI * 220 * i / 8000));
            PyinPitchFrame[] first = PyinPitchTracker.Track(tone, 8000, 128, 100, 500, 1, CancellationToken.None);
            PyinPitchFrame[] repeated = PyinPitchTracker.Track(tone, 8000, 128, 100, 500,
                Math.Min(2, Environment.ProcessorCount), CancellationToken.None);
            CollectionAssert.AreEqual(first, repeated, "pYIN must not depend on worker scheduling.");
            PyinPitchFrame[] middle = first.Skip(8).Take(16).ToArray();
            Assert.IsTrue(middle.Count(frame => frame.FrequencyHz > 0) >= 14, "The stable tone must be voiced.");
            foreach (PyinPitchFrame frame in middle.Where(frame => frame.FrequencyHz > 0))
            {
                double cents = Math.Abs(1200 * Math.Log2(frame.FrequencyHz / 220));
                Assert.IsTrue(cents < 20, $"Known 220 Hz tone: estimated {frame.FrequencyHz:F3} Hz ({cents:F2} cents).");
                Assert.IsTrue(frame.VoicedProbability is >= 0 and <= 1);
            }
            PyinPitchFrame[] silence = PyinPitchTracker.Track(new float[4000], 8000, 128, 100, 500, 1, CancellationToken.None);
            Assert.IsTrue(silence.All(frame => frame.FrequencyHz == 0), "Silence must not become a voiced pitch.");
        }

        [TestMethod]
        public void PyinAllowsGeometryBeyondTheFormerBackpointerLimit()
        {
            PyinSettings settings = PyinSettings.Create(6_000_000, 8000, 80, 100, 500, 1);

            Assert.IsTrue((long) settings.FrameCount * settings.StateCount * sizeof(int) > 128L * 1024 * 1024,
                "The test geometry must exceed the former 128 MiB backpointer limit.");
        }

        [TestMethod]
        public void AdvancedCoresHonorPreCancelledTokens()
        {
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            Assert.ThrowsException<OperationCanceledException>(() => ConstantQTransform.Create(8000, 12, 220, cancellation.Token));
            Assert.ThrowsException<OperationCanceledException>(() =>
                PyinPitchTracker.Track(new float[100], 8000, 128, 100, 500, 1, cancellation.Token));
        }

        [TestMethod]
        public void AllAdvancedOptionsAreIntegrated()
        {
            using AudioTestScope scope = new();
            AudioObj source = scope.Create(new float[100]);
            DeterministicSeparationSettings baseline = new();
            // All four advanced DSP cores are now integrated.
            // ILRMA requires stereo input; mono will throw ArgumentException at runtime (not NotSupportedException).
            DeterministicSeparationSettings[] allAdvanced =
            [
                baseline with { UseCqtAnalysis = true },
                baseline with { UsePyin = true },
                baseline with { UseCqtSynthesis = true },
                baseline with { UseCqtAnalysis = true, UsePyin = true, UseCqtSynthesis = true }
            ];
            foreach (var settings in allAdvanced)
                _ = DeterministicSeparationProcessor.AnalyzeAsync(source, settings).GetAwaiter().GetResult();
        }

        [TestMethod]
        public void IlrmaRejectsMonoInput()
        {
            using AudioTestScope scope = new();
            AudioObj source = scope.Create(new float[8000], 8000); // mono
            DeterministicSeparationSettings settings = new() { UseIlrma = true };
            // ILRMA requires stereo; the processor must reject mono at runtime.
            var ex = Assert.ThrowsException<ArgumentException>(() =>
                { _ = DeterministicSeparationProcessor.AnalyzeAsync(source, settings).GetAwaiter().GetResult(); });
            Assert.IsTrue(ex.Message.Contains("stereo") || ex.Message.Contains("channel"), "ILRMA must reject mono input.");
        }

        private static void AssertError(double[] expected, double[] actual, double gain, double tolerance)
        {
            Assert.AreEqual(expected.Length, actual.Length);
            double maximum = expected.Select((sample, index) => Math.Abs(sample * gain - actual[index])).Max();
            Assert.IsTrue(double.IsFinite(maximum) && maximum < tolerance,
                $"Direct CQT reconstruction error {maximum:G8} exceeds {tolerance:G8}; no Residual correction was used.");
        }
    }
}
