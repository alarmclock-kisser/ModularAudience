using System.Numerics;

namespace ModularAudience.Audio.Processors_V4
{
    /// <summary>
    /// An invertible, periodic, slice-based frequency-domain NSGT constant-Q filterbank.
    /// This is not the standardized sliCQT slicing/overlap protocol. The caller owns slice boundaries.
    /// Bands are ordered DC, ascending positive centers, Nyquist, then negative centers from -high to -low.
    /// Interior full support bandwidth is f * (r - 1/r), with r = 2^(1/binsPerOctave).
    /// </summary>
    /// <remarks>
    /// All transform scratch is per call. Coefficient arrays belong to the caller and must not be
    /// mutated concurrently with a read. Cancellation is checked around each bounded MathNet FFT
    /// and throughout surrounding loops; a running MathNet FFT cannot itself be interrupted.
    /// </remarks>
    internal sealed class ConstantQTransform
    {
        private const int MaximumLength = 1 << 20;
        private const double ImaginaryTolerance = 1e-10;
        private readonly ConstantQBand[] bands;
        private readonly double[] normalization;

        private ConstantQTransform(int sampleRate, int length, ConstantQBand[] bands, double[] normalization)
        {
            this.SampleRate = sampleRate;
            this.Length = length;
            this.bands = bands;
            this.normalization = normalization;
            this.Bands = Array.AsReadOnly(bands);
        }

        internal int Length { get; }
        internal int SampleRate { get; }
        internal IReadOnlyList<ConstantQBand> Bands { get; }

        internal static ConstantQTransform Create(int sampleRate, int binsPerOctave, double minimumHz)
            => Create(sampleRate, binsPerOctave, minimumHz, CancellationToken.None);

        internal static ConstantQTransform Create(int sampleRate, int binsPerOctave,
            double minimumHz, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            ValidateSettings(sampleRate, binsPerOctave, minimumHz);
            int length = ResolveLength(sampleRate, binsPerOctave, minimumHz);
            ConstantQBand[] bands = ConstantQFilterBank.Create(length, sampleRate, binsPerOctave, minimumHz, token);
            double[] normalization = ConstantQFilterBank.CreateNormalization(bands, length, token);
            token.ThrowIfCancellationRequested();
            return new ConstantQTransform(sampleRate, length, bands, normalization);
        }

        private static void ValidateSettings(int sampleRate, int binsPerOctave, double minimumHz)
        {
            if (sampleRate <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate), "The sample rate must be positive.");
            }
            if (binsPerOctave is not (12 or 24 or 36))
            {
                throw new ArgumentOutOfRangeException(nameof(binsPerOctave), "Supported CQT resolutions are 12, 24, and 36 bins per octave.");
            }
            if (!double.IsFinite(minimumHz) || minimumHz <= 0 || minimumHz >= sampleRate / 2.0)
            {
                throw new ArgumentOutOfRangeException(nameof(minimumHz), "The minimum frequency must be finite, positive, and below Nyquist.");
            }
        }

        private static int ResolveLength(int sampleRate, int binsPerOctave, double minimumHz)
        {
            double spacingHz = minimumHz * (Math.Pow(2, 1.0 / binsPerOctave) - 1);
            double required = Math.Ceiling(2.0 * sampleRate / spacingHz);
            if (!double.IsFinite(required) || required > MaximumLength)
            {
                throw new ArgumentOutOfRangeException(nameof(minimumHz), minimumHz,
                    $"Resolving the lowest CQT spacing requires more than {MaximumLength} samples. Raise minimumHz or reduce binsPerOctave; resolution will not be silently reduced.");
            }
            return (int)BitOperations.RoundUpToPowerOf2((uint)required);
        }

        /// <summary>
        /// Computes c_b = IFFT_M(g_b * FFT_N(samples)), with signed support offsets mapped modulo M.
        /// Coefficient j is at circular sample time j*N/M. Mirrored bands have conjugate coefficients
        /// at the same j; DC/Nyquist coefficients are real up to floating-point roundoff.
        /// </summary>
        internal Complex[][] Forward(double[] samples, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Complex[] spectrum = this.CopySamples(samples, token);
            ConstantQFourier.Forward(spectrum, token);
            Complex[][] coefficients = new Complex[this.bands.Length][];
            for (int band = 0; band < this.bands.Length; band++)
            {
                token.ThrowIfCancellationRequested();
                coefficients[band] = this.bands[band].Analyze(spectrum, token);
            }
            token.ThrowIfCancellationRequested();
            return coefficients;
        }

        private Complex[] CopySamples(double[] samples, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(samples);
            if (samples.Length != this.Length)
            {
                throw new ArgumentException($"A CQT slice must contain exactly {this.Length} real samples.", nameof(samples));
            }
            Complex[] spectrum = new Complex[this.Length];
            for (int i = 0; i < samples.Length; i++)
            {
                if ((i & 1023) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }
                if (!double.IsFinite(samples[i]))
                {
                    throw new ArgumentException($"CQT samples must be finite; invalid value at index {i}.", nameof(samples));
                }
                spectrum[i] = new Complex(samples[i], 0);
            }
            return spectrum;
        }

        /// <summary>
        /// Sums FFT_M(c_b) * g_b / sum_b(g_b²) at the stored global bins, then applies IFFT_N.
        /// Identical real masks at matching coefficient indices of positive/negative partners preserve
        /// reality. Each positive band b has partner Bands.Count-b; endpoint masks must also be real.
        /// An imaginary peak exceeding 1e-10 of the real peak is rejected, not discarded.
        /// </summary>
        internal double[] Inverse(Complex[][] coefficients, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            this.ValidateContainer(coefficients);
            for (int band = 0; band < this.bands.Length; band++)
            {
                token.ThrowIfCancellationRequested();
                this.ValidateSeries(coefficients, band);
            }
            Complex[] spectrum = new Complex[this.Length];
            for (int band = 0; band < this.bands.Length; band++)
            {
                token.ThrowIfCancellationRequested();
                this.bands[band].Synthesize(coefficients[band], spectrum, this.normalization, token);
            }
            ConstantQFourier.Inverse(spectrum, token);
            return ExtractReal(spectrum, token);
        }

        private static double[] ExtractReal(Complex[] samples, CancellationToken token)
        {
            double[] result = new double[samples.Length];
            double realPeak = 0;
            double imaginaryPeak = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                if ((i & 1023) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }
                result[i] = samples[i].Real;
                realPeak = Math.Max(realPeak, Math.Abs(samples[i].Real));
                imaginaryPeak = Math.Max(imaginaryPeak, Math.Abs(samples[i].Imaginary));
            }
            token.ThrowIfCancellationRequested();
            if (imaginaryPeak > ImaginaryTolerance * realPeak)
            {
                throw new InvalidOperationException(FormattableString.Invariant(
                    $"CQT synthesis is not real: imaginary peak {imaginaryPeak:G17}, real peak {realPeak:G17}. Apply matching real masks to conjugate bands and real masks to DC/Nyquist."));
            }
            return result;
        }

        /// <summary>
        /// Linearly interpolates |c|² * (M/N)², not complex coefficients, at samplePosition modulo N.
        /// This removes coefficient-rate gain, but not window attenuation or the two-sided tone split.
        /// Only the requested series shape and the coefficient values used by this query are checked;
        /// Inverse validates all series. Finite values outside the slice wrap in either direction.
        /// </summary>
        internal double Power(Complex[][] coefficients, int band, double samplePosition)
        {
            this.ValidateContainer(coefficients);
            ArgumentOutOfRangeException.ThrowIfNegative(band);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(band, this.bands.Length);
            Complex[] series = this.ValidateSeries(coefficients, band);
            if (!double.IsFinite(samplePosition))
            {
                throw new ArgumentOutOfRangeException(nameof(samplePosition), "The CQT sample position must be finite.");
            }
            double position = samplePosition % this.Length;
            if (position < 0)
            {
                position += this.Length;
            }
            double coordinate = position / this.Length * series.Length;
            coordinate = coordinate >= series.Length ? 0 : coordinate;
            int first = (int)coordinate;
            double fraction = coordinate - first;
            double scale = (double)series.Length / this.Length;
            double firstPower = NormalizedPower(series[first], scale);
            double secondPower = NormalizedPower(series[(first + 1) & (series.Length - 1)], scale);
            return firstPower + fraction * (secondPower - firstPower);
        }

        private static double NormalizedPower(Complex value, double scale)
        {
            if (!ConstantQFourier.IsFinite(value))
            {
                throw new ArgumentException("CQT power requires finite coefficients.", "coefficients");
            }
            double real = value.Real * scale;
            double imaginary = value.Imaginary * scale;
            double power = real * real + imaginary * imaginary;
            if (!double.IsFinite(power))
            {
                throw new ArithmeticException("CQT coefficient power exceeds the finite double range. Reduce the input or mask amplitude.");
            }
            return power;
        }

        private void ValidateContainer(Complex[][] coefficients)
        {
            ArgumentNullException.ThrowIfNull(coefficients);
            if (coefficients.Length != this.bands.Length)
            {
                throw new ArgumentException($"Expected {this.bands.Length} CQT coefficient series.", nameof(coefficients));
            }
        }

        private Complex[] ValidateSeries(Complex[][] coefficients, int band)
        {
            Complex[]? series = coefficients[band];
            if (series is null || series.Length != this.bands[band].CoefficientCount)
            {
                throw new ArgumentException($"CQT band {band} requires exactly {this.bands[band].CoefficientCount} coefficients.", nameof(coefficients));
            }
            return series;
        }
    }
}
