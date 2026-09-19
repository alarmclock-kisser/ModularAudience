using System.Numerics;

namespace ModularAudience.Audio.Processors_V4
{
    /// <summary>Determined two-source Gaussian ILRMA (independent rank-K IS-NMF models), with sequential IP rows.</summary>
    internal sealed class IlrmaSpatialModel
    {
        private const double RelativeLoading = 1e-8;
        private readonly IlrmaTrainingData data;
        private readonly IlrmaMatrix[] demixing;
        private readonly IlrmaNmf[] sources;
        private readonly bool[] informative;
        private readonly ParallelOptions options;

        private IlrmaSpatialModel(IlrmaTrainingData data, DeterministicSeparationSettings settings, CancellationToken token)
        {
            this.data = data;
            this.options = new() { MaxDegreeOfParallelism = settings.Threads, CancellationToken = token };
            this.informative = data.BinEnergy.Select(energy => energy > IlrmaNumerics.Floor).ToArray();
            this.demixing = new IlrmaMatrix[data.Bins];
            double cosine = Math.Cos(0.31), sine = Math.Sin(0.31);
            for (int bin = 0; bin < data.Bins; bin++)
                this.demixing[bin] = this.informative[bin] ? new(cosine, sine, -sine, cosine) : IlrmaMatrix.Identity;
            this.sources = [new(data, settings.IlrmaComponents, this.informative, token),
                new(data, settings.IlrmaComponents, this.informative, token)];
        }

        internal static IlrmaMatrix[] Train(IlrmaTrainingData data, int sampleRate,
            DeterministicSeparationSettings settings, IProgress<DeterministicSeparationProgress>? progress,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            progress?.Report(new(0.21, "Initializing independent nonsymmetric ILRMA variance models"));
            IlrmaSpatialModel fit = new(data, settings, token);
            fit.RefreshPower();
            for (int source = 0; source < 2; source++)
            {
                InstrumentProfile? profile = settings.EnsembleMode != InstrumentEnsembleMode.Automatic
                    && source < settings.InstrumentProfiles.Length ? InstrumentProfileCatalog.Get(settings.InstrumentProfiles[source]) : null;
                fit.sources[source].Initialize(source, sampleRate, settings.WindowSize, profile, fit.options);
            }
            fit.Iterate(settings.IlrmaIterations, progress, token);
            return fit.demixing;
        }

        private void Iterate(int iterations, IProgress<DeterministicSeparationProgress>? progress, CancellationToken token)
        {
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                token.ThrowIfCancellationRequested();
                progress?.Report(new(0.22 + 0.57 * iteration / iterations, $"ILRMA {iteration + 1}/{iterations}: IS-NMF variance updates"));
                foreach (IlrmaNmf source in this.sources) source.Update(this.options);
                progress?.Report(new(0.22 + 0.57 * (iteration + 0.5) / iterations, $"ILRMA {iteration + 1}/{iterations}: complex iterative projection"));
                this.Project();
                this.RefreshPower();
                this.NormalizeSources();
                this.CheckObjective();
            }
            token.ThrowIfCancellationRequested();
            for (int bin = 0; bin < this.demixing.Length; bin++)
            {
                token.ThrowIfCancellationRequested();
                this.demixing[bin].Inverse();
            }
            progress?.Report(new(0.80, "ILRMA demixing fixed; measuring projected source images"));
        }

        private void RefreshPower()
        {
            Parallel.For(0, this.data.Bins, this.options, bin =>
            {
                for (int frame = 0; frame < this.data.Frames; frame++)
                {
                    if ((frame & 255) == 0) this.options.CancellationToken.ThrowIfCancellationRequested();
                    for (int source = 0; source < 2; source++)
                    {
                        double power = IlrmaNumerics.Power(this.demixing[bin].Apply(source, this.data.Left[bin][frame], this.data.Right[bin][frame]));
                        if (!double.IsFinite(power)) throw new ArithmeticException("ILRMA demixed source power is non-finite.");
                        this.sources[source].Power[bin][frame] = power;
                    }
                }
            });
        }

        private void Project()
        {
            Parallel.For(0, this.data.Bins, this.options, bin =>
            {
                if (!this.informative[bin]) return;
                // Each row sees the already updated previous row of this frequency's W.
                for (int source = 0; source < 2; source++)
                {
                    this.options.CancellationToken.ThrowIfCancellationRequested();
                    IlrmaMatrix covariance = this.WeightedCovariance(bin, source);
                    IlrmaMatrix inverseW = this.demixing[bin].Inverse();
                    IlrmaMatrix inverseU = covariance.Inverse();
                    // (W U) w = e_n, solved as U w = W^-1 e_n without forming the ill-scaled product.
                    Complex q0 = inverseW.Element(0, source), q1 = inverseW.Element(1, source);
                    Complex w0 = inverseU.Apply(0, q0, q1), w1 = inverseU.Apply(1, q0, q1);
                    this.SetNormalizedRow(bin, source, covariance, w0, w1);
                }
            });
        }

        private IlrmaMatrix WeightedCovariance(int bin, int source)
        {
            double left = 0, right = 0;
            Complex cross = Complex.Zero;
            for (int frame = 0; frame < this.data.Frames; frame++)
            {
                if ((frame & 255) == 0) this.options.CancellationToken.ThrowIfCancellationRequested();
                Complex x0 = this.data.Left[bin][frame], x1 = this.data.Right[bin][frame];
                double weight = 1 / this.sources[source].Variance[bin][frame];
                left += IlrmaNumerics.Power(x0) * weight;
                right += IlrmaNumerics.Power(x1) * weight;
                cross += x0 * Complex.Conjugate(x1) * weight;
            }
            left /= this.data.Frames;
            right /= this.data.Frames;
            cross /= this.data.Frames;
            double trace = left + right;
            if (!(trace > 0) || !double.IsFinite(trace) || !IlrmaNumerics.IsFinite(cross))
                throw new ArithmeticException("ILRMA weighted spatial covariance is not finite and positive.");
            // Trace-relative Tikhonov loading bounds amplification in unoccupied spatial directions.
            // No per-frequency rank rejection: even a rank-one occupied bin is processed here.
            double loading = RelativeLoading * trace / 2;
            return new(left + loading, cross, Complex.Conjugate(cross), right + loading);
        }

        private void SetNormalizedRow(int bin, int source, IlrmaMatrix covariance, Complex w0, Complex w1)
        {
            double scale = Math.Max(w0.Magnitude, w1.Magnitude);
            if (!(scale > 0) || !double.IsFinite(scale)) throw new ArithmeticException("ILRMA IP solve has an invalid vector scale.");
            w0 /= scale;
            w1 /= scale;
            Complex quadratic = Complex.Conjugate(w0) * covariance.Apply(0, w0, w1)
                + Complex.Conjugate(w1) * covariance.Apply(1, w0, w1);
            if (!(quadratic.Real > 0) || !IlrmaNumerics.IsFinite(quadratic))
                throw new ArithmeticException("ILRMA IP normalization w^H U w is not finite and positive.");
            double norm = Math.Sqrt(quadratic.Real);
            this.demixing[bin] = this.demixing[bin].WithRow(source, Complex.Conjugate(w0 / norm), Complex.Conjugate(w1 / norm));
        }

        private void NormalizeSources()
        {
            for (int source = 0; source < 2; source++)
            {
                double sum = 0;
                int bins = 0;
                for (int bin = 0; bin < this.data.Bins; bin++)
                {
                    this.options.CancellationToken.ThrowIfCancellationRequested();
                    if (!this.informative[bin]) continue;
                    bins++;
                    for (int frame = 0; frame < this.data.Frames; frame++) sum += this.sources[source].Power[bin][frame];
                }
                double mean = sum / bins / this.data.Frames;
                if (!(mean > 0) || !double.IsFinite(mean))
                    throw new ArithmeticException("An ILRMA source collapsed on the representative frames; two informative spatial sources could not be fitted.");
                double scale = 1 / Math.Sqrt(mean);
                for (int bin = 0; bin < this.data.Bins; bin++)
                    if (this.informative[bin]) this.demixing[bin] = this.demixing[bin].ScaleRow(source, scale);
                // W_n /= sqrt(mean), |Y_n|² /= mean and T_n /= mean together preserve the objective.
                this.sources[source].Rescale(mean, this.options);
            }
        }

        private void CheckObjective()
        {
            double objective = 0;
            for (int bin = 0; bin < this.data.Bins; bin++)
            {
                this.options.CancellationToken.ThrowIfCancellationRequested();
                if (!this.informative[bin]) continue;
                for (int source = 0; source < 2; source++)
                    for (int frame = 0; frame < this.data.Frames; frame++)
                    {
                        if ((frame & 255) == 0) this.options.CancellationToken.ThrowIfCancellationRequested();
                        double variance = this.sources[source].Variance[bin][frame];
                        objective += this.sources[source].Power[bin][frame] / variance + Math.Log(variance);
                    }
                objective -= 2 * this.data.Frames * this.demixing[bin].LogAbsDeterminant();
            }
            if (!double.IsFinite(objective)) throw new ArithmeticException("The ILRMA log-determinant objective is non-finite.");
        }
    }
}
