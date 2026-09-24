namespace ModularAudience.Audio.Processors_V4
{
    internal static class AtomicTransientDetector
    {
        public static List<int> AddTransientOnsets(List<int> onsets, float[] mono, float[] envelope,
            int sampleRate, float silenceThreshold, LoopAtomizerSettings settings)
        {
            int minimumMs = settings.Sensitivity switch
            {
                AtomizeSensitivity.Conservative => 16,
                AtomizeSensitivity.Aggressive => 8,
                _ => 10
            };
            int minimumDistance = Math.Max(1, sampleRate * minimumMs / 1000);
            List<int> transients = FindQuietStarts(mono, envelope, sampleRate, silenceThreshold, minimumDistance);
            float[] bandEnvelope = BuildHighBandEnvelope(mono, sampleRate);
            foreach (int onset in FindBandAttacks(bandEnvelope, envelope, sampleRate, silenceThreshold, minimumDistance))
            {
                if (transients.All(existing => Math.Abs(existing - onset) >= minimumDistance))
                {
                    transients.Add(onset);
                }
            }

            int replacementRadius = Math.Max(minimumDistance, sampleRate / 60);
            List<int> result = onsets.Where(onset =>
                transients.All(transient => Math.Abs(transient - onset) > replacementRadius)).ToList();
            result.AddRange(transients);
            return result.Distinct().OrderBy(onset => onset).ToList();
        }

        private static List<int> FindQuietStarts(float[] mono, float[] envelope, int sampleRate,
            float silenceThreshold, int minimumDistance)
        {
            int quietLength = Math.Max(1, sampleRate / 1000);
            int quiet = quietLength;
            int guard = Math.Max(1, sampleRate / 4000);
            float threshold = Math.Max(0.000001f, silenceThreshold * 0.5f);
            List<int> onsets = [];
            for (int i = 0; i < mono.Length; i++)
            {
                if (Math.Abs(mono[i]) <= threshold)
                {
                    quiet++;
                    continue;
                }

                if (quiet >= quietLength && (onsets.Count == 0 || i - onsets[^1] >= minimumDistance) &&
                    Mean(envelope, i, i + Math.Max(1, sampleRate / 250)) > silenceThreshold * 1.5)
                {
                    onsets.Add(Math.Max(0, i - guard));
                }

                quiet = 0;
            }

            return onsets;
        }

        private static float[] BuildHighBandEnvelope(float[] mono, int sampleRate)
        {
            float[] envelope = new float[mono.Length];
            int window = Math.Max(1, sampleRate / 500);
            double[] powers = new double[window];
            double coefficient = Math.Exp(-2.0 * Math.PI * Math.Min(300.0, sampleRate / 8.0) / sampleRate);
            double first = 0.0;
            double second = 0.0;
            double previous = 0.0;
            double previousFirst = 0.0;
            double sum = 0.0;
            for (int i = 0; i < mono.Length; i++)
            {
                first = coefficient * (first + mono[i] - previous);
                second = coefficient * (second + first - previousFirst);
                previous = mono[i];
                previousFirst = first;
                int slot = i % window;
                sum -= powers[slot];
                powers[slot] = second * second;
                sum += powers[slot];
                envelope[i] = (float)Math.Sqrt(Math.Max(0.0, sum) / Math.Min(i + 1, window));
            }

            return envelope;
        }

        private static List<int> FindBandAttacks(float[] band, float[] envelope, int sampleRate,
            float silenceThreshold, int minimumDistance)
        {
            int hop = Math.Max(1, sampleRate / 1000);
            List<int> onsets = [];
            int lastPeak = -minimumDistance;
            for (int i = 0; i < band.Length; i += hop)
            {
                if (i - lastPeak < minimumDistance) continue;
                double before = Mean(band, i - (6 * hop), i - (2 * hop));
                double after = Mean(band, i, i + (2 * hop));
                double fullBand = Mean(envelope, i, i + (2 * hop));
                double floor = Math.Max(silenceThreshold * 0.5, fullBand * 0.12);
                if (after <= Math.Max(floor, before * 1.65)) continue;

                onsets.Add(FindBandAttackStart(band, i, sampleRate, Math.Max(silenceThreshold * 0.25, before * 1.1)));
                lastPeak = i;
            }

            return onsets;
        }

        private static int FindBandAttackStart(float[] band, int peak, int sampleRate, double threshold)
        {
            int guard = Math.Max(1, sampleRate / 500);
            int first = Math.Max(0, peak - Math.Max(1, sampleRate / 125));
            int frame = Math.Min(band.Length - 1, peak + guard);
            while (frame > first && band[frame] > threshold)
            {
                frame--;
            }

            return Math.Max(0, frame - guard);
        }

        private static double Mean(float[] values, int start, int end)
        {
            start = Math.Clamp(start, 0, values.Length);
            end = Math.Clamp(end, start, values.Length);
            double sum = 0.0;
            for (int i = start; i < end; i++)
            {
                sum += values[i];
            }

            return sum / Math.Max(1, end - start);
        }
    }
}
