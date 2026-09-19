using System.Numerics;

namespace ModularAudience.Audio.Processors_V4
{
    internal sealed class ConstantQBand
    {
        private readonly WindowBin[] support;

        private ConstantQBand(double centerHz, double bandwidthHz, int coefficientCount, WindowBin[] support)
        {
            this.CenterHz = centerHz;
            this.BandwidthHz = bandwidthHz;
            this.CoefficientCount = coefficientCount;
            this.support = support;
        }

        internal double CenterHz { get; }

        /// <summary>The full continuous support width, not the half-power bandwidth.</summary>
        internal double BandwidthHz { get; }

        internal int CoefficientCount { get; }

        internal static ConstantQBand Create(double centerHz, double centerBin,
            double leftWidth, double rightWidth, int length, int sampleRate, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!(leftWidth > 0) || !(rightWidth > 0))
            {
                throw new InvalidOperationException("CQT window neighbors must have distinct frequency positions.");
            }
            int anchor = (int)Math.Round(centerBin);
            double fractionalCenter = centerBin - anchor;
            int firstOffset = (int)Math.Floor(fractionalCenter - leftWidth) + 1;
            int lastOffset = (int)Math.Ceiling(fractionalCenter + rightWidth) - 1;
            int width = lastOffset - firstOffset + 1;
            if (width < 1 || width > length)
            {
                throw new InvalidOperationException("A CQT window has empty or aliased FFT support.");
            }
            int count = (int)BitOperations.RoundUpToPowerOf2((uint)width);
            WindowBin[] support = CreateSupport(anchor, fractionalCenter, firstOffset,
                width, leftWidth, rightWidth, length, count, token);
            double bandwidthHz = (leftWidth + rightWidth) * sampleRate / length;
            return new ConstantQBand(centerHz, bandwidthHz, count, support);
        }

        /// <summary>
        /// Each half-window is cos²(pi * distance / (2 * neighbor distance)).
        /// Consecutive support offsets occupy distinct bins modulo M because M is at least their span.
        /// The integer FFT anchor demodulates the band without rounding its window center.
        /// </summary>
        private static WindowBin[] CreateSupport(int anchor, double fractionalCenter, int firstOffset,
            int width, double leftWidth, double rightWidth, int length, int count, CancellationToken token)
        {
            WindowBin[] support = new WindowBin[width];
            for (int i = 0; i < width; i++)
            {
                if ((i & 1023) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }
                int offset = firstOffset + i;
                double distance = offset - fractionalCenter;
                double relative = Math.Abs(distance) / (distance < 0 ? leftWidth : rightWidth);
                double cosine = relative >= 1 ? 0 : Math.Cos(0.5 * Math.PI * relative);
                support[i] = new WindowBin((anchor + offset) & (length - 1),
                    offset & (count - 1), cosine * cosine);
            }
            return support;
        }

        internal ConstantQBand Mirror(int length, CancellationToken token)
        {
            WindowBin[] mirrored = new WindowBin[this.support.Length];
            for (int i = 0; i < mirrored.Length; i++)
            {
                if ((i & 1023) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }
                WindowBin bin = this.support[i];
                mirrored[i] = new WindowBin((-bin.GlobalIndex) & (length - 1),
                    (-bin.LocalIndex) & (this.CoefficientCount - 1), bin.Weight);
            }
            return new ConstantQBand(-this.CenterHz, this.BandwidthHz, this.CoefficientCount, mirrored);
        }

        internal void AccumulateWindowEnergy(double[] normalization, CancellationToken token)
        {
            for (int i = 0; i < this.support.Length; i++)
            {
                if ((i & 1023) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }
                WindowBin bin = this.support[i];
                normalization[bin.GlobalIndex] += bin.Weight * bin.Weight;
            }
        }

        internal Complex[] Analyze(Complex[] spectrum, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Complex[] coefficients = new Complex[this.CoefficientCount];
            for (int i = 0; i < this.support.Length; i++)
            {
                if ((i & 1023) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }
                WindowBin bin = this.support[i];
                coefficients[bin.LocalIndex] = spectrum[bin.GlobalIndex] * bin.Weight;
            }
            ConstantQFourier.Inverse(coefficients, token);
            return coefficients;
        }

        /// <summary>
        /// FFT_M cancels the analysis IFFT_M, so the frequency-domain dual is g / sum(g²).
        /// No additional M/N factor belongs in synthesis; that factor only normalizes power queries.
        /// </summary>
        internal void Synthesize(Complex[] coefficients, Complex[] spectrum,
            double[] normalization, CancellationToken token)
        {
            Complex[] local = CopyCoefficients(coefficients, token);
            ConstantQFourier.Forward(local, token);
            for (int i = 0; i < this.support.Length; i++)
            {
                if ((i & 1023) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }
                WindowBin bin = this.support[i];
                double dual = bin.Weight / normalization[bin.GlobalIndex];
                spectrum[bin.GlobalIndex] += local[bin.LocalIndex] * dual;
            }
        }

        private static Complex[] CopyCoefficients(Complex[] coefficients, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Complex[] local = new Complex[coefficients.Length];
            for (int i = 0; i < coefficients.Length; i++)
            {
                if ((i & 1023) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }
                if (!ConstantQFourier.IsFinite(coefficients[i]))
                {
                    throw new ArgumentException($"CQT coefficients must be finite; invalid value at local index {i}.", nameof(coefficients));
                }
                local[i] = coefficients[i];
            }
            return local;
        }

        private readonly record struct WindowBin(int GlobalIndex, int LocalIndex, double Weight);
    }
}
