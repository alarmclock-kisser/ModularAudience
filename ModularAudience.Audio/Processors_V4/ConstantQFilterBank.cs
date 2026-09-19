namespace ModularAudience.Audio.Processors_V4
{
    internal static class ConstantQFilterBank
    {
        internal static ConstantQBand[] Create(int length, int sampleRate, int binsPerOctave,
            double minimumHz, CancellationToken token)
        {
            List<double> centers = CreateCenters(sampleRate, binsPerOctave, minimumHz, token);
            List<ConstantQBand> bands = new(2 * centers.Count + 2);
            double firstBin = centers[0] * length / sampleRate;
            bands.Add(ConstantQBand.Create(0, 0, firstBin, firstBin, length, sampleRate, token));
            for (int i = 0; i < centers.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                bands.Add(CreatePositiveBand(centers, i, length, sampleRate, token));
            }
            double nyquist = sampleRate / 2.0;
            double radius = (nyquist - centers[^1]) * length / sampleRate;
            bands.Add(ConstantQBand.Create(nyquist, length / 2, radius, radius, length, sampleRate, token));
            for (int i = centers.Count; i >= 1; i--)
            {
                token.ThrowIfCancellationRequested();
                bands.Add(bands[i].Mirror(length, token));
            }
            token.ThrowIfCancellationRequested();
            return bands.ToArray();
        }

        private static List<double> CreateCenters(int sampleRate, int binsPerOctave,
            double minimumHz, CancellationToken token)
        {
            List<double> centers = [];
            double ratio = Math.Pow(2, 1.0 / binsPerOctave);
            for (double center = minimumHz; center < sampleRate / 2.0; center *= ratio)
            {
                token.ThrowIfCancellationRequested();
                centers.Add(center);
            }
            return centers;
        }

        private static ConstantQBand CreatePositiveBand(List<double> centers, int index,
            int length, int sampleRate, CancellationToken token)
        {
            double center = centers[index] * length / sampleRate;
            double left = index == 0 ? 0 : centers[index - 1] * length / sampleRate;
            double right = index == centers.Count - 1
                ? length / 2.0 : centers[index + 1] * length / sampleRate;
            return ConstantQBand.Create(centers[index], center, center - left, right - center,
                length, sampleRate, token);
        }

        /// <summary>
        /// Neighboring Hann halves add to one, hence their squared weights sum to at least 1/2.
        /// DC and the circular Nyquist window complete this partition on every global FFT bin.
        /// </summary>
        internal static double[] CreateNormalization(ConstantQBand[] bands, int length, CancellationToken token)
        {
            double[] normalization = new double[length];
            foreach (ConstantQBand band in bands)
            {
                token.ThrowIfCancellationRequested();
                band.AccumulateWindowEnergy(normalization, token);
            }
            for (int i = 0; i < length; i++)
            {
                if ((i & 1023) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }
                if (!double.IsFinite(normalization[i]) || normalization[i] < 0.25)
                {
                    throw new InvalidOperationException($"The CQT filterbank has missing or ill-conditioned coverage at FFT bin {i}.");
                }
            }
            token.ThrowIfCancellationRequested();
            return normalization;
        }
    }
}
