namespace ModularAudience.Audio.Processors_V4
{
    internal static class DeterministicSourceMasks
    {
        internal static float[][][] Build(DeterministicSpectrogram block, double[][] dictionary,
            DeterministicSourceGroup[] groups, DeterministicSeparationSettings settings, CancellationToken token)
        {
            float[][][] masks = Allocate(block.Frames, groups.Length, dictionary.Length, token);
            if (groups.Length == 0 || block.Frames == 0) return masks;
            double[][] power = CopyPower(block.Power, token);
            double maximum = DeterministicTrainingData.Maximum(power, token);
            if (maximum == 0) return masks;
            // A scalar block rescale improves conditioning without changing fixed-W responsibilities.
            double floor = DeterministicTrainingData.NormalizePower(power, maximum, token);
            double[][] activations = DeterministicNmf.Fit(power, dictionary, floor, settings, token);
            ParallelOptions options = new() { MaxDegreeOfParallelism = settings.Threads, CancellationToken = token };
            Parallel.For(0, block.Frames, options, frame =>
                FrameMasks(block, power[frame], dictionary, activations[frame], groups, masks[frame], frame, token));
            Smooth(masks, power, settings.TransientPreservation, token);
            Parallel.For(0, block.Frames, options, frame => ApplyFloor(masks[frame], power[frame], settings.MaskFloor, token));
            return masks;
        }

        private static float[][][] Allocate(int frames, int sources, int bins, CancellationToken token)
        {
            float[][][] result = new float[frames][][];
            for (int frame = 0; frame < frames; frame++)
            {
                token.ThrowIfCancellationRequested();
                result[frame] = new float[sources][];
                for (int source = 0; source < sources; source++) result[frame][source] = new float[bins];
            }
            return result;
        }

        private static double[][] CopyPower(double[][] original, CancellationToken token)
        {
            double[][] copy = new double[original.Length][];
            for (int frame = 0; frame < original.Length; frame++)
            {
                token.ThrowIfCancellationRequested();
                copy[frame] = new double[original[frame].Length];
                for (int bin = 0; bin < original[frame].Length; bin++) copy[frame][bin] = DeterministicTrainingData.Positive(original[frame][bin]);
            }
            return copy;
        }

        private static void FrameMasks(DeterministicSpectrogram block, double[] power, double[][] dictionary,
            double[] activation, DeterministicSourceGroup[] groups, float[][] masks, int frame, CancellationToken token)
        {
            for (int bin = 0; bin < power.Length; bin++)
            {
                if ((bin & 127) == 0) token.ThrowIfCancellationRequested();
                if (power[bin] <= 0) continue;
                double modeled = DeterministicNmf.Dot(dictionary[bin], activation);
                double h = DeterministicTrainingData.Unit(block.Harmonic[frame][bin]);
                double p = DeterministicTrainingData.Unit(block.Percussive[frame][bin]);
                double pan = DeterministicTrainingData.SignedUnit(block.Pan[frame][bin]);
                double uncertainty = Math.Clamp(1 - h - p, 0, 1);
                double residual = Math.Abs(power[bin] - modeled) + power[bin] * (0.02 + 0.20 * uncertainty);
                double denominator = Math.Max(1e-300, modeled + residual);
                for (int source = 0; source < groups.Length; source++)
                {
                    double groupPower = 0;
                    foreach (int component in groups[source].Components)
                        groupPower += groups[source].WeightFor(component) * dictionary[bin][component] * activation[component];
                    double affinity = Affinity(groups[source], h, p, pan);
                    masks[source][bin] = (float)DeterministicTrainingData.Unit(groupPower / denominator * affinity);
                }
            }
        }

        private static double Affinity(DeterministicSourceGroup group, double harmonic, double percussive, double pan)
        {
            double character = Math.Clamp(group.Harmonic * harmonic + group.Percussive * percussive, 0, 1);
            double stereo = 1 - 0.15 * Math.Abs(Math.Clamp(group.Pan, -1, 1) - pan);
            return (0.65 + 0.35 * character) * stereo;
        }

        private static void Smooth(float[][][] masks, double[][] power, double transientPreservation, CancellationToken token)
        {
            for (int frame = 1; frame < masks.Length; frame++)
            {
                token.ThrowIfCancellationRequested();
                double onset = DeterministicNmfInitialization.Onset(power[frame], power[frame - 1]);
                // Strong attacks reset history when preservation=1; otherwise this is a convex crossfade.
                double history = 0.24 * (1 - transientPreservation * onset);
                for (int bin = 0; bin < power[frame].Length; bin++)
                {
                    if ((bin & 127) == 0) token.ThrowIfCancellationRequested();
                    for (int source = 0; source < masks[frame].Length; source++)
                    {
                        masks[frame][source][bin] = power[frame][bin] > 0
                            ? (float)((1 - history) * masks[frame][source][bin] + history * masks[frame - 1][source][bin]) : 0;
                    }
                }
            }
        }

        private static void ApplyFloor(float[][] masks, double[] power, double floor, CancellationToken token)
        {
            // Include the implicit residual in a convex uniform mixture. This allows bleed, not a guaranteed floor.
            double denominator = 1 + (masks.Length + 1) * floor;
            for (int bin = 0; bin < power.Length; bin++)
            {
                if ((bin & 127) == 0) token.ThrowIfCancellationRequested();
                if (power[bin] <= 0) continue;
                double sum = 0;
                for (int source = 0; source < masks.Length; source++)
                {
                    masks[source][bin] = (float)((masks[source][bin] + floor) / denominator);
                    sum += masks[source][bin];
                }
                if (sum <= 1) continue;
                // Only a simplex roundoff guard, never independent normalization of stem loudness.
                for (int source = 0; source < masks.Length; source++) masks[source][bin] = (float)(masks[source][bin] * ((1 - 1e-6) / sum));
            }
        }
    }
}
