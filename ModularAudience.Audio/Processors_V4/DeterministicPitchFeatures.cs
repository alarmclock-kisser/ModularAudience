namespace ModularAudience.Audio.Processors_V4
{
    internal static class DeterministicPitchFeatures
    {
        internal static (double Frequency, double Score) Estimate(double[] spectrum, int sampleRate,
            int windowSize, CancellationToken token)
        {
            double energy = spectrum.Sum();
            if (energy <= 0) return (0, 0);
            int[] peaks = FindPeaks(spectrum, token);
            double resolution = (double)sampleRate / windowSize;
            double maximum = spectrum.Max();
            double best = 0;
            double frequency = 0;
            foreach (int peak in peaks)
            {
                token.ThrowIfCancellationRequested();
                double measured = (peak + QifftOffset(spectrum, peak)) * resolution;
                for (int divisor = 1; divisor <= 6; divisor++)
                {
                    double candidate = measured / divisor;
                    if (candidate < Math.Max(25, 1.5 * resolution) || candidate > Math.Min(3000, sampleRate / 4.0)) continue;
                    double score = TemplateScore(spectrum, candidate / resolution, energy, maximum);
                    if (score <= best) continue;
                    best = score;
                    frequency = candidate;
                }
            }
            return best >= 0.25 ? (frequency, best) : (0, best);
        }

        private static int[] FindPeaks(double[] spectrum, CancellationToken token)
        {
            List<int> peaks = [];
            double threshold = spectrum.Max() * 0.01;
            for (int bin = 1; bin < spectrum.Length - 1; bin++)
            {
                if ((bin & 255) == 0) token.ThrowIfCancellationRequested();
                if (spectrum[bin] >= threshold && spectrum[bin] > spectrum[bin - 1] && spectrum[bin] >= spectrum[bin + 1])
                {
                    peaks.Add(bin);
                }
            }
            return peaks.OrderByDescending(bin => spectrum[bin]).ThenBy(bin => bin).Take(12).ToArray();
        }

        private static double QifftOffset(double[] spectrum, int peak)
        {
            // Quadratic interpolation of three log-power bins; this is QIFFT, not pYIN.
            double floor = Math.Max(1e-300, spectrum[peak] * 1e-12);
            double left = Math.Log(Math.Max(floor, spectrum[peak - 1]));
            double center = Math.Log(Math.Max(floor, spectrum[peak]));
            double right = Math.Log(Math.Max(floor, spectrum[peak + 1]));
            double curvature = left - 2 * center + right;
            return curvature < -1e-12 ? Math.Clamp(0.5 * (left - right) / curvature, -0.5, 0.5) : 0;
        }

        private static double TemplateScore(double[] spectrum, double fundamentalBin, double energy, double maximum)
        {
            double captured = 0;
            double fundamental = 0;
            int lastBin = 0;
            for (int harmonic = 1; harmonic <= 8; harmonic++)
            {
                int center = (int)Math.Round(fundamentalBin * harmonic);
                if (center >= spectrum.Length - 1) break;
                double local = 0;
                for (int bin = Math.Max(lastBin + 1, center - 1); bin <= center + 1; bin++) local += spectrum[bin];
                lastBin = center + 1;
                captured += local / Math.Sqrt(harmonic);
                if (harmonic == 1) fundamental = Math.Min(1, local / Math.Max(maximum, 1e-300));
            }
            return Math.Clamp(captured / energy * (0.5 + 0.5 * fundamental), 0, 1);
        }
    }
}
