namespace ModularAudience.Audio.Processors_V4
{
    internal sealed record DeterministicComponentFeatures(int Index, double Energy, double Harmonic,
        double Percussive, double Pan, double FundamentalHz, double PitchScore, double CentroidHz,
        double[] Envelope, double[] Activation);

    internal static class DeterministicSourceFeatures
    {
        internal static DeterministicComponentFeatures[] Measure(DeterministicNmfFit fit,
            DeterministicTrainingData data, int sampleRate, DeterministicSeparationSettings settings, CancellationToken token)
        {
            int rank = fit.Dictionary[0].Length;
            double[][] bands = BandDictionary(fit.Dictionary, data.BandOfBin, token);
            double[][] prediction = BandPrediction(bands, fit.Activations, token);
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
                result[component] = new(component, activation.Sum(), h, p, pan, frequency, score, centroid, envelope, activation);
            });
            return result;
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
