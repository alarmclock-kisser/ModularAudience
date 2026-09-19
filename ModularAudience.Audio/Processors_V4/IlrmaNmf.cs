namespace ModularAudience.Audio.Processors_V4
{
    /// <summary>One independent IS-NMF variance model; temporal activations are shared across all frequencies.</summary>
    internal sealed class IlrmaNmf
    {
        private readonly double[][] basis;
        private readonly double[][] activation;
        private readonly bool[] informative;
        private readonly int components;
        private readonly int frames;

        internal IlrmaNmf(IlrmaTrainingData data, int components, bool[] informative, CancellationToken token)
        {
            this.components = components;
            this.frames = data.Frames;
            this.informative = informative;
            this.Power = Allocate(data.Bins, data.Frames, token);
            this.Variance = Allocate(data.Bins, data.Frames, token);
            this.basis = Allocate(data.Bins, components, token);
            this.activation = Allocate(components, data.Frames, token);
        }

        internal double[][] Power { get; }
        internal double[][] Variance { get; }

        private static double[][] Allocate(int rows, int columns, CancellationToken token)
        {
            double[][] result = new double[rows][];
            for (int row = 0; row < rows; row++)
            {
                token.ThrowIfCancellationRequested();
                result[row] = new double[columns];
            }
            return result;
        }

        internal void Initialize(int sourceIndex, int sampleRate, int windowSize,
            InstrumentProfile? profile, ParallelOptions options)
        {
            for (int bin = 0; bin < this.basis.Length; bin++)
            {
                options.CancellationToken.ThrowIfCancellationRequested();
                if (!this.informative[bin]) continue;
                double mean = this.Power[bin].Sum() / this.frames;
                double guide = profile?.SpectralWeight((double)bin * sampleRate / windowSize) ?? 1;
                for (int component = 0; component < this.components; component++)
                    this.basis[bin][component] = Math.Max(IlrmaNumerics.Floor,
                        Math.Sqrt(mean) * guide * (0.25 + IlrmaNumerics.Seed(sourceIndex, component, bin, 0x51ed270bu)));
            }
            for (int frame = 0; frame < this.frames; frame++)
            {
                options.CancellationToken.ThrowIfCancellationRequested();
                double energy = 0;
                for (int bin = 0; bin < this.Power.Length; bin++)
                    if (this.informative[bin]) energy += this.Power[bin][frame];
                for (int component = 0; component < this.components; component++)
                    this.activation[component][frame] = Math.Max(IlrmaNumerics.Floor,
                        energy / this.components * (0.25 + IlrmaNumerics.Seed(sourceIndex, component, frame, 0x68bc21ebu)));
            }
            // Initialize each spectral template to unit mass without changing the activation seed.
            for (int component = 0; component < this.components; component++)
            {
                double sum = 0;
                for (int bin = 0; bin < this.basis.Length; bin++) sum += this.basis[bin][component];
                for (int bin = 0; bin < this.basis.Length; bin++) this.basis[bin][component] /= sum;
            }
            this.RefreshVariance(options);
        }

        internal void Update(ParallelOptions options)
        {
            this.UpdateBasis(options);
            this.RefreshVariance(options);
            this.UpdateActivation(options);
            this.NormalizeBasis(options.CancellationToken);
            this.RefreshVariance(options);
        }

        private void UpdateBasis(ParallelOptions options)
        {
            Parallel.For(0, this.basis.Length, options, bin =>
            {
                if (!this.informative[bin]) return;
                for (int component = 0; component < this.components; component++)
                {
                    double numerator = 0, denominator = 0;
                    for (int frame = 0; frame < this.frames; frame++)
                    {
                        if ((frame & 255) == 0) options.CancellationToken.ThrowIfCancellationRequested();
                        double inverse = 1 / this.Variance[bin][frame];
                        double weighted = this.activation[component][frame] * inverse;
                        numerator += weighted * (this.Power[bin][frame] * inverse);
                        denominator += weighted;
                    }
                    // T_fk <- T_fk sqrt(sum_t V_kt |Y_ft|²/R_ft² / sum_t V_kt/R_ft).
                    this.basis[bin][component] = Updated(this.basis[bin][component], numerator, denominator);
                }
            });
        }

        private void UpdateActivation(ParallelOptions options)
        {
            Parallel.For(0, this.frames, options, frame =>
            {
                for (int component = 0; component < this.components; component++)
                {
                    double numerator = 0, denominator = 0;
                    for (int bin = 0; bin < this.basis.Length; bin++)
                    {
                        if ((bin & 255) == 0) options.CancellationToken.ThrowIfCancellationRequested();
                        if (!this.informative[bin]) continue;
                        double inverse = 1 / this.Variance[bin][frame];
                        double weighted = this.basis[bin][component] * inverse;
                        numerator += weighted * (this.Power[bin][frame] * inverse);
                        denominator += weighted;
                    }
                    // V_kt <- V_kt sqrt(sum_f T_fk |Y_ft|²/R_ft² / sum_f T_fk/R_ft).
                    // Frequency reduction order is fixed, regardless of worker count.
                    this.activation[component][frame] = Updated(this.activation[component][frame], numerator, denominator);
                }
            });
        }

        private static double Updated(double previous, double numerator, double denominator)
        {
            if (!(denominator > 0) || numerator < 0 || !double.IsFinite(numerator) || !double.IsFinite(denominator))
                throw new ArithmeticException("ILRMA IS-NMF has an invalid multiplicative update.");
            return IlrmaNumerics.Positive(previous * Math.Sqrt(numerator / denominator), "IS-NMF update");
        }

        private void NormalizeBasis(CancellationToken token)
        {
            for (int component = 0; component < this.components; component++)
            {
                token.ThrowIfCancellationRequested();
                double sum = 0;
                for (int bin = 0; bin < this.basis.Length; bin++) sum += this.basis[bin][component];
                if (!(sum > 0) || !double.IsFinite(sum)) throw new ArithmeticException("ILRMA has an invalid NMF basis scale.");
                for (int bin = 0; bin < this.basis.Length; bin++) this.basis[bin][component] /= sum;
                for (int frame = 0; frame < this.frames; frame++)
                {
                    this.activation[component][frame] *= sum;
                    if (!double.IsFinite(this.activation[component][frame]))
                        throw new ArithmeticException("ILRMA NMF activation normalization overflowed.");
                }
            }
        }

        internal void RefreshVariance(ParallelOptions options)
        {
            Parallel.For(0, this.basis.Length, options, bin =>
            {
                for (int frame = 0; frame < this.frames; frame++)
                {
                    if ((frame & 255) == 0) options.CancellationToken.ThrowIfCancellationRequested();
                    double value = 0;
                    for (int component = 0; component < this.components; component++)
                        value += this.basis[bin][component] * this.activation[component][frame];
                    this.Variance[bin][frame] = IlrmaNumerics.Positive(value, "variance reconstruction");
                }
            });
        }

        internal void Rescale(double powerScale, ParallelOptions options)
        {
            Parallel.For(0, this.basis.Length, options, bin =>
            {
                if (!this.informative[bin]) return;
                for (int component = 0; component < this.components; component++)
                    this.basis[bin][component] /= powerScale;
                for (int frame = 0; frame < this.frames; frame++)
                {
                    if ((frame & 255) == 0) options.CancellationToken.ThrowIfCancellationRequested();
                    this.Power[bin][frame] /= powerScale;
                }
            });
            this.RefreshVariance(options);
        }
    }
}
