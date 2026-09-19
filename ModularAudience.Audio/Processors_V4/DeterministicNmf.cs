namespace ModularAudience.Audio.Processors_V4
{
    internal sealed record DeterministicNmfFit(double[][] Dictionary, double[][] Activations);

    internal static class DeterministicNmf
    {
        internal static DeterministicNmfFit Train(DeterministicTrainingData data, int rank,
            DeterministicSeparationSettings settings, DeterministicModelProgress progress, CancellationToken token)
        {
            ParallelOptions options = new() { MaxDegreeOfParallelism = settings.Threads, CancellationToken = token };
            double[][] dictionary = DeterministicNmfInitialization.Dictionary(data.Power, data.Energy, rank, data.Floor, options);
            double[][] activations = DeterministicNmfInitialization.Activations(data.Power, dictionary, data.Floor, false, options);
            double[][] prediction = DeterministicSpectrogram.Allocate(data.Power.Length, dictionary.Length);
            for (int iteration = 0; iteration < settings.Iterations; iteration++)
            {
                token.ThrowIfCancellationRequested();
                UpdateActivations(data.Power, dictionary, activations, data.Floor, options);
                Predict(dictionary, activations, prediction, options);
                UpdateDictionary(data.Power, dictionary, activations, prediction, data.Floor, options);
                Normalize(dictionary, activations, options);
                progress.Report(0.34 + 0.52 * (iteration + 1) / settings.Iterations, "Learning deterministic IS-NMF dictionary");
            }
            // Match the final activations to the last dictionary update before measuring energy/features.
            UpdateActivations(data.Power, dictionary, activations, data.Floor, options);
            return new DeterministicNmfFit(dictionary, activations);
        }

        internal static double[][] Fit(double[][] power, double[][] dictionary, double floor,
            DeterministicSeparationSettings settings, CancellationToken token)
        {
            ParallelOptions options = new() { MaxDegreeOfParallelism = settings.Threads, CancellationToken = token };
            double[][] activations = DeterministicNmfInitialization.Activations(power, dictionary, floor, true, options);
            for (int iteration = 0; iteration < settings.Iterations; iteration++)
            {
                token.ThrowIfCancellationRequested();
                UpdateActivations(power, dictionary, activations, floor, options);
            }
            return activations;
        }

        private static void UpdateActivations(double[][] power, double[][] dictionary,
            double[][] activations, double floor, ParallelOptions options)
        {
            Parallel.For(0, power.Length, options, frame =>
                UpdateFrame(power[frame], dictionary, activations[frame], floor, options.CancellationToken));
        }

        private static void UpdateFrame(double[] power, double[][] dictionary, double[] activation,
            double floor, CancellationToken token)
        {
            double[] numerator = new double[activation.Length];
            double[] denominator = new double[activation.Length];
            for (int bin = 0; bin < power.Length; bin++)
            {
                if ((bin & 127) == 0) token.ThrowIfCancellationRequested();
                double prediction = Math.Max(1e-30, Dot(dictionary[bin], activation));
                double inverse = 1 / prediction;
                double ratio = Math.Max(floor, power[bin]) * inverse * inverse;
                for (int component = 0; component < activation.Length; component++)
                {
                    numerator[component] += dictionary[bin][component] * ratio;
                    denominator[component] += dictionary[bin][component] * inverse;
                }
            }
            for (int component = 0; component < activation.Length; component++)
            {
                activation[component] = MmUpdate(activation[component], numerator[component], denominator[component]);
            }
        }

        private static void Predict(double[][] dictionary, double[][] activations, double[][] prediction,
            ParallelOptions options)
        {
            Parallel.For(0, activations.Length, options, frame =>
            {
                for (int bin = 0; bin < dictionary.Length; bin++)
                {
                    if ((bin & 127) == 0) options.CancellationToken.ThrowIfCancellationRequested();
                    prediction[frame][bin] = Math.Max(1e-30, Dot(dictionary[bin], activations[frame]));
                }
            });
        }

        private static void UpdateDictionary(double[][] power, double[][] dictionary,
            double[][] activations, double[][] prediction, double floor, ParallelOptions options)
        {
            Parallel.For(0, dictionary.Length, options, bin =>
                UpdateBin(power, dictionary[bin], activations, prediction, bin, floor, options.CancellationToken));
        }

        private static void UpdateBin(double[][] power, double[] weights, double[][] activations,
            double[][] prediction, int bin, double floor, CancellationToken token)
        {
            double[] numerator = new double[weights.Length];
            double[] denominator = new double[weights.Length];
            for (int frame = 0; frame < power.Length; frame++)
            {
                if ((frame & 63) == 0) token.ThrowIfCancellationRequested();
                double inverse = 1 / prediction[frame][bin];
                double ratio = Math.Max(floor, power[frame][bin]) * inverse * inverse;
                for (int component = 0; component < weights.Length; component++)
                {
                    numerator[component] += activations[frame][component] * ratio;
                    denominator[component] += activations[frame][component] * inverse;
                }
            }
            for (int component = 0; component < weights.Length; component++)
            {
                weights[component] = MmUpdate(weights[component], numerator[component], denominator[component]);
            }
        }

        private static double MmUpdate(double value, double numerator, double denominator)
        {
            // IS (beta=0) majorization-minimization requires exponent 1/2, not a KL/Euclidean update.
            double ratio = Math.Clamp(numerator / Math.Max(denominator, 1e-100), 1e-60, 1e60);
            double updated = value * Math.Sqrt(ratio);
            if (!double.IsFinite(updated)) throw new ArithmeticException("Non-finite IS-NMF update.");
            return Math.Clamp(updated, 1e-30, 1e100);
        }

        private static void Normalize(double[][] dictionary, double[][] activations, ParallelOptions options)
        {
            // Unit-sum W columns are coupled to H: this changes the factorization gauge, not WH or stem gain.
            Parallel.For(0, dictionary[0].Length, options, component =>
            {
                double sum = 0;
                for (int bin = 0; bin < dictionary.Length; bin++) sum += dictionary[bin][component];
                for (int bin = 0; bin < dictionary.Length; bin++) dictionary[bin][component] /= sum;
                for (int frame = 0; frame < activations.Length; frame++)
                {
                    if ((frame & 127) == 0) options.CancellationToken.ThrowIfCancellationRequested();
                    activations[frame][component] *= sum;
                }
            });
        }

        internal static double Dot(double[] left, double[] right)
        {
            double sum = 0;
            for (int index = 0; index < left.Length; index++) sum += left[index] * right[index];
            return sum;
        }
    }
}
