namespace ModularAudience.Audio.Processors_V4
{
    internal static class DeterministicNmfInitialization
    {
        internal static double[][] Dictionary(double[][] power, double[] energy, int rank, double floor,
            ParallelOptions options)
        {
            int bins = power[0].Length;
            double[][] dictionary = DeterministicSpectrogram.Allocate(bins, rank);
            double[] nearest = new double[power.Length];
            bool[] selected = new bool[power.Length];
            int seed = Array.IndexOf(energy, energy.Max());
            for (int component = 0; component < rank; component++)
            {
                options.CancellationToken.ThrowIfCancellationRequested();
                selected[seed] = true;
                double sum = 0;
                for (int bin = 0; bin < bins; bin++) sum += Math.Max(floor, power[seed][bin]);
                for (int bin = 0; bin < bins; bin++) dictionary[bin][component] = Math.Max(floor, power[seed][bin]) / sum;
                int current = component;
                Parallel.For(0, power.Length, options, frame =>
                    UpdateDistance(power[frame], energy[frame], dictionary, current, nearest, frame, options.CancellationToken));
                seed = NextSeed(nearest, selected, energy);
            }
            return dictionary;
        }

        private static void UpdateDistance(double[] spectrum, double energy, double[][] dictionary,
            int component, double[] nearest, int frame, CancellationToken token)
        {
            double affinity = 0;
            if (energy > 0)
            {
                for (int bin = 0; bin < spectrum.Length; bin++)
                {
                    if ((bin & 255) == 0) token.ThrowIfCancellationRequested();
                    affinity += Math.Sqrt(spectrum[bin] / energy * dictionary[bin][component]);
                }
            }
            nearest[frame] = Math.Max(nearest[frame], Math.Clamp(affinity, 0, 1));
        }

        private static int NextSeed(double[] nearest, bool[] selected, double[] energy)
        {
            int next = 0;
            double best = double.NegativeInfinity;
            for (int frame = 0; frame < nearest.Length; frame++)
            {
                if (selected[frame] || energy[frame] <= 0) continue;
                double distance = 1 - nearest[frame];
                if (distance <= best) continue;
                best = distance;
                next = frame;
            }
            // If fewer nonzero observations than rank are available, a repeated seed is safe.
            return double.IsNegativeInfinity(best) ? Array.IndexOf(energy, energy.Max()) : next;
        }

        internal static double[][] Activations(double[][] power, double[][] dictionary, double floor,
            bool warm, ParallelOptions options)
        {
            int rank = dictionary[0].Length;
            double[] norms = new double[rank];
            foreach (double[] row in dictionary)
            {
                for (int component = 0; component < rank; component++) norms[component] += row[component] * row[component];
            }
            double[][] activations = DeterministicSpectrogram.Allocate(power.Length, rank);
            Parallel.For(0, power.Length, options, frame =>
                Project(power[frame], dictionary, norms, floor, activations[frame], options.CancellationToken));
            if (warm) WarmStart(power, activations, options.CancellationToken);
            return activations;
        }

        private static void Project(double[] power, double[][] dictionary, double[] norms, double floor,
            double[] activation, CancellationToken token)
        {
            double energy = 0;
            for (int bin = 0; bin < power.Length; bin++)
            {
                if ((bin & 255) == 0) token.ThrowIfCancellationRequested();
                double value = Math.Max(floor, power[bin]);
                energy += value;
                for (int component = 0; component < activation.Length; component++)
                {
                    activation[component] += dictionary[bin][component] * value;
                }
            }
            double sum = 0;
            for (int component = 0; component < activation.Length; component++)
            {
                activation[component] /= Math.Max(1e-30, norms[component]);
                sum += activation[component];
            }
            for (int component = 0; component < activation.Length; component++)
            {
                activation[component] = Math.Max(1e-30, activation[component] / Math.Max(sum, 1e-30) * energy);
            }
        }

        private static void WarmStart(double[][] power, double[][] activations, CancellationToken token)
        {
            for (int frame = 1; frame < power.Length; frame++)
            {
                token.ThrowIfCancellationRequested();
                double history = 0.2 * (1 - Onset(power[frame], power[frame - 1]));
                double energy = activations[frame].Sum();
                double previous = Math.Max(1e-30, activations[frame - 1].Sum());
                for (int component = 0; component < activations[frame].Length; component++)
                {
                    double prior = activations[frame - 1][component] / previous * energy;
                    activations[frame][component] = (1 - history) * activations[frame][component] + history * prior;
                }
            }
        }

        internal static double Onset(double[] current, double[] previous)
        {
            double flux = 0;
            double energy = 0;
            for (int bin = 0; bin < current.Length; bin++)
            {
                flux += Math.Max(0, current[bin] - previous[bin]);
                energy += current[bin];
            }
            return energy > 0 ? Math.Clamp(2 * flux / energy, 0, 1) : 0;
        }
    }
}
