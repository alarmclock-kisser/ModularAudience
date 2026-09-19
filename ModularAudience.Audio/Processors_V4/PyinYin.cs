using MathNet.Numerics.Providers.FourierTransform;
using System.Numerics;

namespace ModularAudience.Audio.Processors_V4
{
    /// <summary>Reusable worker-local fixed-pair YIN difference and CMNDF calculation.</summary>
    internal sealed class PyinYin
    {
        private readonly PyinSettings settings;
        private readonly ManagedFourierTransformProvider fourier = new();
        private readonly Complex[] referenceSpectrum;
        private readonly Complex[] frameSpectrum;
        private readonly double[] squaredPrefix;

        internal PyinYin(PyinSettings settings)
        {
            this.settings = settings;
            this.referenceSpectrum = new Complex[settings.FrameLength];
            this.frameSpectrum = new Complex[settings.FrameLength];
            this.squaredPrefix = new double[settings.FrameLength + 1];
            this.NormalizedDifference = new double[settings.MaximumLag + 2];
        }

        internal double[] NormalizedDifference { get; }

        internal bool Analyze(float[] mono, long center, CancellationToken token)
        {
            double peak = this.CopyCenteredFrame(mono, center, token);
            if (peak == 0)
            {
                return false;
            }
            double mean = this.NormalizeAndGetMean(peak, token);
            if (this.PrepareSpectra(mean, token) == 0)
            {
                return false;
            }
            this.CrossCorrelate(token);
            this.CalculateDifference(token);
            return true;
        }

        private double CopyCenteredFrame(float[] mono, long center, CancellationToken token)
        {
            long start = center - this.frameSpectrum.Length / 2;
            double peak = 0;
            for (int i = 0; i < this.frameSpectrum.Length; i++)
            {
                if ((i & 1023) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }
                long source = start + i;
                double value = source >= 0 && source < mono.Length ? mono[(int)source] : 0;
                this.frameSpectrum[i] = new Complex(value, 0);
                peak = Math.Max(peak, Math.Abs(value));
            }
            return peak;
        }

        private double NormalizeAndGetMean(double peak, CancellationToken token)
        {
            double sum = 0;
            for (int i = 0; i < this.frameSpectrum.Length; i++)
            {
                if ((i & 1023) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }
                // Divide in double before squaring, including for subnormal float samples.
                // No absolute amplitude gate: finite rescaling leaves the CMNDF unchanged.
                double value = this.frameSpectrum[i].Real / peak;
                this.frameSpectrum[i] = new Complex(value, 0);
                sum += value;
            }
            return sum / this.frameSpectrum.Length;
        }

        private double PrepareSpectra(double mean, CancellationToken token)
        {
            int pairs = this.frameSpectrum.Length / 2;
            this.squaredPrefix[0] = 0;
            for (int i = 0; i < this.frameSpectrum.Length; i++)
            {
                if ((i & 1023) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }
                double value = this.frameSpectrum[i].Real - mean;
                this.frameSpectrum[i] = new Complex(value, 0);
                this.referenceSpectrum[i] = i < pairs ? this.frameSpectrum[i] : Complex.Zero;
                this.squaredPrefix[i + 1] = this.squaredPrefix[i] + value * value;
            }
            return this.squaredPrefix[this.frameSpectrum.Length];
        }

        private void CrossCorrelate(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            this.fourier.Forward(this.referenceSpectrum, FourierTransformScaling.NoScaling);
            token.ThrowIfCancellationRequested();
            this.fourier.Forward(this.frameSpectrum, FourierTransformScaling.NoScaling);
            token.ThrowIfCancellationRequested();
            for (int i = 0; i < this.frameSpectrum.Length; i++)
            {
                if ((i & 1023) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }
                this.frameSpectrum[i] *= Complex.Conjugate(this.referenceSpectrum[i]);
            }
            this.fourier.Backward(this.frameSpectrum, FourierTransformScaling.BackwardScaling);
            token.ThrowIfCancellationRequested();
        }

        private void CalculateDifference(CancellationToken token)
        {
            int pairs = this.frameSpectrum.Length / 2;
            double cumulative = 0;
            this.NormalizedDifference[0] = 1;
            for (int lag = 1; lag < this.NormalizedDifference.Length; lag++)
            {
                if ((lag & 1023) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }
                // IFFT(conj(FFT(reference)) * FFT(frame))[lag] = sum_j x[j]*x[j+lag].
                // reference is zero outside [0,M); lag <= M-1, so j+lag never wraps.
                // These are exactly M pairs at EVERY lag, not circular autocorrelation or
                // a shrinking-overlap difference. Prefix sums supply both energy boundaries.
                double energies = this.squaredPrefix[pairs]
                    + (this.squaredPrefix[lag + pairs] - this.squaredPrefix[lag]);
                double difference = Math.Max(0, energies - 2 * this.frameSpectrum[lag].Real);
                if (!double.IsFinite(difference))
                {
                    throw new ArithmeticException("The managed pYIN FFT produced a non-finite squared difference.");
                }
                cumulative += difference;
                // A zero at a periodic lag is a genuine trough when previous differences
                // are positive. An entirely zero denominator is not evidence of periodicity.
                this.NormalizedDifference[lag] = cumulative > 0 ? difference * lag / cumulative : 1;
            }
            token.ThrowIfCancellationRequested();
        }
    }
}
