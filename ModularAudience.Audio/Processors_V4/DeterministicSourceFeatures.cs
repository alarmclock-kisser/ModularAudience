using System.Numerics;

namespace ModularAudience.Audio.Processors_V4
{
    internal sealed record DeterministicComponentFeatures(int Index, double Energy, double Harmonic,
        double Percussive, double Pan, double FundamentalHz, double PitchScore, double CentroidHz,
        double[] Envelope, double[] Activation, double CqtEnergy = 0, double PyinPitchHz = 0, double PyinVoiced = 0);

    internal static class DeterministicSourceFeatures
    {
        internal static DeterministicComponentFeatures[] Measure(DeterministicAudioSnapshot? source, DeterministicNmfFit fit,
            DeterministicTrainingData data, int sampleRate, DeterministicSeparationSettings settings, CancellationToken token)
        {
            int rank = fit.Dictionary[0].Length;
            double bins = fit.Dictionary.Length;
            double[][] bands = BandDictionary(fit.Dictionary, data.BandOfBin, token);
            double[][] prediction = BandPrediction(bands, fit.Activations, token);

            // Pre-compute optional advanced evidence on the actual source signal (bounded window).
            double[]? cqtEnergyPerBin = null;
            double pyinPitch = 0, pyinVoiced = 0;
            if (source != null && settings.UseCqtAnalysis)
                cqtEnergyPerBin = MeasureCqtEnergy(source, bins, sampleRate, settings, token);
            if (source != null && settings.UsePyin)
            {
                (pyinPitch, pyinVoiced) = MeasurePyinEvidence(source, sampleRate, settings, token);
            }

            DeterministicComponentFeatures[] result = new DeterministicComponentFeatures[rank];
            ParallelOptions options = new() { MaxDegreeOfParallelism = settings.Threads, CancellationToken = token };
            Parallel.For(0, rank, options, component =>
            {
                double[] spectrum = fit.Dictionary.Select(row => row[component]).ToArray();
                double[] activation = fit.Activations.Select(row => row[component]).ToArray();
                double[] envelope = bands.Select(row => row[component]).ToArray();
                (double h, double p, double pan) = Character(data, bands, prediction, activation, component, token);
                (double frequency, double score) = DeterministicPitchFeatures.Estimate(spectrum, sampleRate, settings.WindowSize, token);
                if (h < 0.35 || p > h) frequency = 0;
                double centroid = 0;
                for (int bin = 0; bin < spectrum.Length; bin++) centroid += spectrum[bin] * bin * sampleRate / settings.WindowSize;

                // CQT evidence: correlate component spectrum with aggregate constant-Q band energy.
                double cqtEnergy = 0;
                if (cqtEnergyPerBin != null)
                {
                    double specNorm = 0;
                    for (int bin = 0; bin < spectrum.Length; bin++) specNorm += spectrum[bin];
                    if (specNorm > 0)
                    {
                        for (int bin = 0; bin < spectrum.Length; bin++)
                            cqtEnergy += spectrum[bin] / specNorm * cqtEnergyPerBin[bin];
                    }
                }

                // pYIN evidence: pitch compatibility boost when component fundamental matches tracked pitch.
                double compPyinPitch = 0, compPyinVoiced = pyinVoiced;
                if (pyinPitch > 0 && frequency > 0)
                {
                    double ratio = Math.Max(frequency, pyinPitch) / Math.Min(frequency, pyinPitch);
                    double cents = 1200 * Math.Log2(ratio);
                    if (cents < 50) compPyinPitch = pyinPitch;
                }
                else if (pyinPitch > 0 && frequency == 0)
                {
                    // Component has no estimated fundamental; use pYIN pitch if component is tonal.
                    if (h >= 0.35) compPyinPitch = pyinPitch;
                }

                result[component] = new(component, activation.Sum(), h, p, pan,
                    compPyinPitch > 0 ? compPyinPitch : frequency, score, centroid, envelope, activation,
                    cqtEnergy, compPyinPitch, compPyinVoiced);
            });
            return result;
        }

        /// <summary>
        /// Analyzes contiguous source samples with CQT, aggregates positive-band energy,
        /// and maps to NMF FFT bins via log-normal kernel at each band center frequency.
        /// Retains stereo energy without anti-phase mono cancellation.
        /// </summary>
        private static double[] MeasureCqtEnergy(DeterministicAudioSnapshot source, double nBins,
            int sampleRate, DeterministicSeparationSettings settings, CancellationToken token)
        {
            ConstantQTransform cqt = ConstantQTransform.Create(sampleRate, settings.CqtBinsPerOctave,
                settings.CqtMinimumHz, token);
            int length = cqt.Length;

            // Extract one CQT-length slice from the center of the source (bounded window).
            int channels = source.Channels;
            long totalSamples = source.Samples.LongLength / channels;
            long startSample = Math.Max(0, (totalSamples - length) / 2);
            int sliceLength = (int)Math.Min(length, totalSamples - startSample);

            double[] mono = new double[sliceLength];
            for (int i = 0; i < sliceLength; i++)
            {
                long idx = (startSample + i) * channels;
                mono[i] = source.Samples[idx];
                if (channels == 2) mono[i] += source.Samples[idx + 1];
            }

            Complex[][] coefficients = cqt.Forward(mono, token);

            // Compute aggregate power per positive CQT band (average across time, coefficient-rate scaled).
            int numPositive = (cqt.Bands.Count - 1) / 2;
            double[] bandPower = new double[numPositive];
            for (int b = 1; b <= numPositive; b++)
            {
                double sum = 0;
                Complex[] series = coefficients[b];
                int m = series.Length, n = length;
                double scale = (double)m * m / ((double)n * n);
                for (int t = 0; t < m; t++)
                {
                    double re = series[t].Real * scale;
                    double im = series[t].Imaginary * scale;
                    sum += re * re + im * im;
                }
                bandPower[b - 1] = sum / m;
            }

            // Map CQT band energy to NMF FFT bins using log-normal kernel at each band center.
            double[] cqtEnergy = new double[(int)nBins];
            double binHz = (double)sampleRate / length;
            for (int bin = 1; bin < cqtEnergy.Length; bin++)
            {
                double freq = bin * binHz;
                if (freq <= 0) continue;
                double energy = 0;
                for (int b = 0; b < numPositive; b++)
                {
                    ConstantQBand band = cqt.Bands[b + 1];
                    double logDist = Math.Log2(freq / Math.Abs(band.CenterHz));
                    // Account for band coefficient time rate: positive band b has coefficient count M_b,
                    // so the effective energy density scales with M_b/N.
                    double timeRate = (double)cqt.Bands[b + 1].CoefficientCount / length;
                    double sigma = 0.04 / timeRate;
                    energy += bandPower[b] * Math.Exp(-0.5 * logDist * logDist / (sigma * sigma));
                }
                cqtEnergy[bin] = energy;
            }
            return cqtEnergy;
        }

        /// <summary>
        /// Applies pYIN to the dominant-channel mono trajectory of the source.
        /// Returns (dominant pitch Hz, voiced probability) across all frames.
        /// Monophonic: does not claim all voices in a polyphonic mix.
        /// </summary>
        private static (double PitchHz, double Voiced) MeasurePyinEvidence(DeterministicAudioSnapshot source,
            int sampleRate, DeterministicSeparationSettings settings, CancellationToken token)
        {
            int channels = source.Channels;
            float[] mono = new float[source.Samples.LongLength / channels];
            for (long i = 0; i < mono.Length; i++)
            {
                long idx = i * channels;
                mono[(int)i] = source.Samples[idx];
                if (channels == 2) mono[(int)i] += source.Samples[idx + 1];
            }

            PyinPitchFrame[] frames = PyinPitchTracker.Track(mono, sampleRate,
                Math.Max(64, sampleRate / 100), settings.PyinMinimumHz, settings.PyinMaximumHz,
                Math.Max(1, Environment.ProcessorCount / 2), token);

            // Aggregate: dominant pitch (most frequent voiced frequency within 20 cents) and average voiced mass.
            double totalVoiced = 0;
            Dictionary<double, double> pitchMass = new();
            foreach (PyinPitchFrame frame in frames)
            {
                if (frame.FrequencyHz <= 0) continue;
                totalVoiced += frame.VoicedProbability;
                // Quantize to nearest semitone for aggregation.
                double semitone = Math.Round(12 * Math.Log2(frame.FrequencyHz / 27.5));
                double quantized = 27.5 * Math.Pow(2, semitone / 12);
                pitchMass[quantized] = pitchMass.GetValueOrDefault(quantized) + frame.VoicedProbability;
            }

            if (totalVoiced <= 0) return (0, 0);
            double avgVoiced = totalVoiced / frames.Length;
            double bestPitch = 0;
            double bestMass = 0;
            foreach (var kvp in pitchMass)
            {
                if (kvp.Value > bestMass)
                {
                    bestMass = kvp.Value;
                    bestPitch = kvp.Key;
                }
            }
            return (bestPitch, avgVoiced);
        }

        private static double[][] BandDictionary(double[][] dictionary, int[] bandMap, CancellationToken token)
        {
            double[][] result = DeterministicSpectrogram.Allocate(bandMap[^1] + 1, dictionary[0].Length);
            for (int bin = 0; bin < dictionary.Length; bin++)
            {
                if ((bin & 255) == 0) token.ThrowIfCancellationRequested();
                for (int component = 0; component < dictionary[bin].Length; component++)
                {
                    result[bandMap[bin]][component] += dictionary[bin][component];
                }
            }
            return result;
        }

        private static double[][] BandPrediction(double[][] bands, double[][] activations, CancellationToken token)
        {
            double[][] result = DeterministicSpectrogram.Allocate(activations.Length, bands.Length);
            for (int frame = 0; frame < activations.Length; frame++)
            {
                token.ThrowIfCancellationRequested();
                for (int band = 0; band < bands.Length; band++) result[frame][band] = DeterministicNmf.Dot(bands[band], activations[frame]);
            }
            return result;
        }

        private static (double Harmonic, double Percussive, double Pan) Character(DeterministicTrainingData data,
            double[][] bands, double[][] prediction, double[] activation, int component, CancellationToken token)
        {
            double evidence = 0;
            double harmonic = 0;
            double percussive = 0;
            double pan = 0;
            for (int frame = 0; frame < activation.Length; frame++)
            {
                token.ThrowIfCancellationRequested();
                for (int band = 0; band < bands.Length; band++)
                {
                    double responsibility = bands[band][component] * activation[frame] / Math.Max(data.Floor, prediction[frame][band]);
                    double weight = data.BandPower[frame][band] * Math.Min(1, responsibility);
                    evidence += weight;
                    harmonic += weight * data.Harmonic[frame][band];
                    percussive += weight * data.Percussive[frame][band];
                    pan += weight * data.Pan[frame][band];
                }
            }
            return evidence > 0 ? (harmonic / evidence, percussive / evidence, pan / evidence) : (0, 0, 0);
        }

        internal static double EnvelopeSimilarity(double[] first, double[] second)
        {
            double product = 0;
            double firstNorm = 0;
            double secondNorm = 0;
            for (int band = 0; band < first.Length; band++)
            {
                product += first[band] * second[band];
                firstNorm += first[band] * first[band];
                secondNorm += second[band] * second[band];
            }
            double denominator = Math.Sqrt(firstNorm * secondNorm);
            return denominator > 0 ? Math.Clamp(product / denominator, 0, 1) : 0;
        }

        internal static double ActivationCorrelation(double[] first, double[] second, CancellationToken token)
        {
            if (first.Length < 3) return 0;
            double firstMean = first.Average();
            double secondMean = second.Average();
            double covariance = 0;
            double firstVariance = 0;
            double secondVariance = 0;
            for (int frame = 0; frame < first.Length; frame++)
            {
                if ((frame & 255) == 0) token.ThrowIfCancellationRequested();
                double a = first[frame] - firstMean;
                double b = second[frame] - secondMean;
                covariance += a * b;
                firstVariance += a * a;
                secondVariance += b * b;
            }
            // Constant activations do not provide evidence of correlated temporal variation.
            double denominator = Math.Sqrt(firstVariance * secondVariance);
            return denominator > 1e-20 * firstMean * secondMean * first.Length
                ? Math.Clamp(covariance / denominator, -1, 1) : 0;
        }
    }
}
