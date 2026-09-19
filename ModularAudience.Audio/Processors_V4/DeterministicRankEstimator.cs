using MathNet.Numerics.LinearAlgebra.Double;

namespace ModularAudience.Audio.Processors_V4
{
    internal static class DeterministicRankEstimator
    {
        internal static int Estimate(DeterministicTrainingData data, DeterministicSeparationSettings settings,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (data.IsSilent) return 0;
            int frames = data.Power.Length;
            if (frames < 3) return 1;
            int dimensions = Math.Min(data.BandPower[0].Length, frames - 1);
            double[][] features = CreateFeatures(data, dimensions, cancellationToken);
            double[,] scatter = Scatter(features, settings.Threads, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            // The small symmetric scatter matrix has nonnegative singular values (its eigenvalues).
            double[] eigenvalues = DenseMatrix.OfArray(scatter).Svd(false).S.ToArray();
            cancellationToken.ThrowIfCancellationRequested();
            Array.Sort(eigenvalues);
            Array.Reverse(eigenvalues);
            int cap = Math.Min(settings.MaxComponents, Math.Min(dimensions - 1, frames - 1));
            return SelectMdl(eigenvalues, frames, cap, cancellationToken);
        }

        private static double[][] CreateFeatures(DeterministicTrainingData data, int dimensions, CancellationToken token)
        {
            int frames = data.Power.Length;
            int bands = data.BandPower[0].Length;
            int[] widths = new int[dimensions];
            foreach (int band in data.BandOfBin) widths[band * dimensions / bands]++;
            double mean = data.Energy.Sum() / frames / data.Power[0].Length;
            double[][] result = DeterministicSpectrogram.Allocate(frames, dimensions);
            for (int frame = 0; frame < frames; frame++)
            {
                token.ThrowIfCancellationRequested();
                for (int band = 0; band < bands; band++)
                {
                    result[frame][band * dimensions / bands] += data.BandPower[frame][band];
                }
                for (int dimension = 0; dimension < dimensions; dimension++)
                {
                    double reference = Math.Max(data.Floor, mean * widths[dimension]);
                    result[frame][dimension] = Math.Log(1 + result[frame][dimension] / reference);
                }
            }
            return result;
        }

        private static double[,] Scatter(double[][] features, int threads, CancellationToken token)
        {
            int dimensions = features[0].Length;
            double[,] result = new double[dimensions, dimensions];
            ParallelOptions options = new() { MaxDegreeOfParallelism = threads, CancellationToken = token };
            Parallel.For(0, dimensions, options, row =>
            {
                for (int column = 0; column < dimensions; column++)
                {
                    token.ThrowIfCancellationRequested();
                    double sum = 0;
                    for (int frame = 0; frame < features.Length; frame++)
                    {
                        sum += features[frame][row] * features[frame][column];
                    }
                    result[row, column] = sum / features.Length;
                }
            });
            return result;
        }

        private static int SelectMdl(double[] eigenvalues, int observations, int cap, CancellationToken token)
        {
            // Real-Gaussian covariance MDL assumes independent feature noise with equal noise eigenvalues.
            // The uncentered second moment retains a stationary mean spectrum as a signal direction.
            // Log-band features and overlapping windows violate exact Gaussian/independence assumptions:
            // this selects an effective spectral model order, NOT an instrument count or probability.
            // Nonzero input requires one mean-spectrum atom; MDL compares the nonempty models.
            double ridge = Math.Max(1e-15, eigenvalues[0] * 1e-9);
            double best = double.PositiveInfinity;
            int selected = 1;
            for (int rank = 1; rank <= cap; rank++)
            {
                token.ThrowIfCancellationRequested();
                double sum = 0;
                double logSum = 0;
                for (int index = rank; index < eigenvalues.Length; index++)
                {
                    double value = Math.Max(ridge, eigenvalues[index]);
                    sum += value;
                    logSum += Math.Log(value);
                }
                int noiseDimensions = eigenvalues.Length - rank;
                double likelihood = 0.5 * observations * noiseDimensions * (Math.Log(sum / noiseDimensions) - logSum / noiseDimensions);
                double parameters = rank * eigenvalues.Length - rank * (rank - 1) / 2.0;
                double penalty = 0.5 * parameters * Math.Log(observations);
                double mdl = Math.Max(0, likelihood) + penalty;
                if (mdl >= best) continue;
                best = mdl;
                selected = rank;
            }
            return selected;
        }
    }
}
