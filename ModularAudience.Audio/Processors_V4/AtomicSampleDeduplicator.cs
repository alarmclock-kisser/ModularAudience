using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace ModularAudience.Audio.Processors_V4
{
    internal static class AtomicSampleDeduplicator
    {
        private const int SpectralBins = 72;

        public static List<AudioObj> Deduplicate(List<AudioObj> atomics, float similarityThreshold,
            IProgress<double>? progress, int maxVariantsPerCluster = 2)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxVariantsPerCluster);
            Fingerprint[] fingerprints = new Fingerprint[atomics.Count];
            Parallel.For(0, atomics.Count, AtomizerParallelism.OptionsFor(atomics.Count), i =>
            {
                fingerprints[i] = CreateFingerprint(atomics[i]);
                progress?.Report(0.70 + (0.15 * (i + 1) / atomics.Count));
            });

            double threshold = float.IsFinite(similarityThreshold) ? Math.Clamp(similarityThreshold, 0.90f, 0.999f) : 0.94;
            List<List<int>> clusters = CreateClusters(fingerprints, threshold);
            HashSet<int> selected = [];
            for (int i = 0; i < clusters.Count; i++)
            {
                selected.UnionWith(SelectVariants(clusters[i], fingerprints, maxVariantsPerCluster));
                progress?.Report(0.85 + (0.14 * (i + 1) / clusters.Count));
            }

            List<AudioObj> result = [];
            for (int i = 0; i < atomics.Count; i++)
            {
                if (selected.Contains(i)) result.Add(atomics[i]);
                else atomics[i].Dispose();
            }

            return result;
        }

        public static List<AudioObj> SelectDistinctRepresentatives(IReadOnlyList<AudioObj> atomics)
        {
            Fingerprint[] fingerprints = new Fingerprint[atomics.Count];
            Parallel.For(0, atomics.Count, AtomizerParallelism.OptionsFor(atomics.Count), i =>
            {
                fingerprints[i] = CreateFingerprint(atomics[i]);
            });

            List<int> selected = [];
            double loudestRms = fingerprints.Length == 0 ? 0.0 : fingerprints.Max(fingerprint => fingerprint.Rms);
            double minimumRms = Math.Max(0.01, loudestRms * 0.20);
            foreach (int candidate in Enumerable.Range(0, fingerprints.Length)
                .OrderByDescending(index => fingerprints[index].Quality)
                .ThenBy(index => index))
            {
                if (fingerprints[candidate].Rms >= minimumRms &&
                    !selected.Any(index => AreDuplicates(fingerprints[candidate], fingerprints[index])))
                {
                    selected.Add(candidate);
                }
            }

            if (selected.Count == 0 && fingerprints.Length > 0)
            {
                selected.Add(Enumerable.Range(0, fingerprints.Length)
                    .OrderByDescending(index => fingerprints[index].Quality)
                    .ThenBy(index => index)
                    .First());
            }

            return selected.Select(index => atomics[index]).ToList();
        }

        private static Fingerprint CreateFingerprint(AudioObj audio)
        {
            int frames = audio.Data.Length / audio.Channels;
            double[] spectrum = BuildSpectrum(audio, frames);
            double[] envelope = BuildEnvelope(audio, frames);
            double peak = audio.Data.Max(value => Math.Abs((double)value));
            double totalEnergy = 0.0;
            foreach (float sample in audio.Data)
            {
                totalEnergy += (double)sample * sample;
            }

            double rms = Math.Sqrt(totalEnergy / Math.Max(1, audio.Data.Length));
            int clipped = audio.Data.Count(value => Math.Abs(value) >= 0.999f);
            int active = audio.Data.Count(value => Math.Abs(value) >= peak * 0.02);
            double secondaryTransientPenalty = GetSecondaryTransientPenalty(audio, frames, peak);
            double quality = ((0.75 + (0.25 * active / Math.Max(1, audio.Data.Length))) /
                (1.0 + (50.0 * clipped / Math.Max(1, audio.Data.Length)))) /
                (1.0 + (3.0 * secondaryTransientPenalty)) *
                (0.70 + (0.30 * Math.Clamp(rms / 0.1, 0.0, 1.0)));
            audio.CustomTags["AtomizeQuality"] = quality.ToString("F6", System.Globalization.CultureInfo.InvariantCulture);
            return new Fingerprint(spectrum, spectrum[..SpectralBins], envelope, rms, frames / (double)audio.SampleRate,
                GetSpectralCentroid(spectrum, audio.SampleRate), quality);
        }

        private static double GetSecondaryTransientPenalty(AudioObj audio, int frames, double peak)
        {
            int windowFrames = Math.Max(1, audio.SampleRate / 100);
            int windowCount = (frames + windowFrames - 1) / windowFrames;
            if (windowCount < 5 || peak <= 0.0) return 0.0;

            double[] levels = new double[windowCount];
            for (int window = 0; window < windowCount; window++)
            {
                int start = window * windowFrames;
                int end = Math.Min(frames, start + windowFrames);
                double energy = 0.0;
                for (int frame = start; frame < end; frame++)
                {
                    for (int channel = 0; channel < audio.Channels; channel++)
                    {
                        double value = audio.Data[(frame * audio.Channels) + channel];
                        energy += value * value;
                    }
                }

                levels[window] = Math.Sqrt(energy / Math.Max(1, (end - start) * audio.Channels));
            }

            double peakLevel = levels.Max();
            int firstActive = Array.FindIndex(levels, level => level >= peakLevel * 0.12);
            if (firstActive < 0) return 0.0;

            double penalty = 0.0;
            for (int window = Math.Max(firstActive + 3, 2); window < levels.Length; window++)
            {
                double baseline = (levels[window - 1] + levels[window - 2]) * 0.5;
                double level = levels[window];
                if (level >= peakLevel * 0.12 && level > baseline * 1.6)
                {
                    penalty += (level - baseline) / peakLevel;
                }
            }

            return Math.Clamp(penalty, 0.0, 1.0);
        }

        private static double[] BuildSpectrum(AudioObj audio, int frames)
        {
            int fftSize = 256;
            while (fftSize < Math.Min(frames, audio.SampleRate / 8) && fftSize < 8192)
            {
                fftSize <<= 1;
            }

            double[] spectrum = new double[SpectralBins * 3];
            for (int section = 0; section < 3; section++)
            {
                int length = Math.Min(frames, section == 0 ? Math.Min(fftSize, Math.Max(1, audio.SampleRate / 25)) : fftSize);
                int start = section switch
                {
                    0 => 0,
                    1 => Math.Max(0, (frames - length) / 2),
                    _ => Math.Max(0, frames - length)
                };
                double[] bands = ComputeBands(audio, start, length, fftSize, section == 0);
                Normalize(bands);
                double weight = Math.Sqrt(section switch { 0 => 0.55, 1 => 0.30, _ => 0.15 });
                for (int bin = 0; bin < SpectralBins; bin++)
                {
                    spectrum[(section * SpectralBins) + bin] = bands[bin] * weight;
                }
            }

            return spectrum;
        }

        private static double[] ComputeBands(AudioObj audio, int start, int length, int fftSize, bool attack)
        {
            double[] bands = new double[SpectralBins];
            Complex[] buffer = new Complex[fftSize];
            for (int channel = 0; channel < audio.Channels; channel++)
            {
                Array.Clear(buffer);
                for (int i = 0; i < length; i++)
                {
                    double position = i / (double)Math.Max(1, length - 1);
                    double window = attack ? 0.5 * (1.0 + Math.Cos(Math.PI * position)) :
                        0.5 * (1.0 - Math.Cos(2.0 * Math.PI * position));
                    buffer[i] = new Complex(audio.Data[((start + i) * audio.Channels) + channel] * window, 0.0);
                }

                Fourier.Forward(buffer, FourierOptions.Matlab);
                AccumulateBands(buffer, bands, audio.SampleRate);
            }

            for (int bin = 0; bin < bands.Length; bin++)
            {
                bands[bin] = Math.Sqrt(bands[bin] / audio.Channels);
            }

            return bands;
        }

        private static void AccumulateBands(Complex[] buffer, double[] bands, int sampleRate)
        {
            double frequencyRange = Math.Log(Math.Max(21.0, sampleRate / 2.0) / 20.0);
            for (int bin = 1; bin <= buffer.Length / 2; bin++)
            {
                double frequency = bin * (double)sampleRate / buffer.Length;
                int band = Math.Clamp((int)(Math.Log(Math.Max(20.0, frequency) / 20.0) / frequencyRange * SpectralBins), 0, SpectralBins - 1);
                double magnitude = buffer[bin].Magnitude;
                bands[band] += magnitude * magnitude;
            }
        }

        private static double[] BuildEnvelope(AudioObj audio, int frames)
        {
            double[] envelope = new double[32];
            for (int block = 0; block < envelope.Length; block++)
            {
                int start = (int)((long)block * frames / envelope.Length);
                int end = Math.Min(frames, Math.Max(start + 1, (int)((long)(block + 1) * frames / envelope.Length)));
                double energy = 0.0;
                for (int frame = start; frame < end; frame++)
                {
                    for (int channel = 0; channel < audio.Channels; channel++)
                    {
                        double value = audio.Data[(frame * audio.Channels) + channel];
                        energy += value * value;
                    }
                }

                envelope[block] = Math.Sqrt(energy / Math.Max(1, (end - start) * audio.Channels));
            }

            Normalize(envelope);
            return envelope;
        }

        private static double GetSpectralCentroid(double[] spectrum, int sampleRate)
        {
            double frequencyRange = Math.Log(Math.Max(21.0, sampleRate / 2.0) / 20.0);
            double sum = 0.0;
            double weighted = 0.0;
            for (int bin = 0; bin < SpectralBins; bin++)
            {
                double magnitude = spectrum[SpectralBins + bin];
                double frequency = 20.0 * Math.Exp((bin + 0.5) / SpectralBins * frequencyRange);
                sum += magnitude;
                weighted += magnitude * frequency;
            }

            return sum > 0.0 ? weighted / sum : 0.0;
        }

        private static List<List<int>> CreateClusters(Fingerprint[] fingerprints, double threshold)
        {
            List<List<int>> clusters = [];
            foreach (int index in Enumerable.Range(0, fingerprints.Length)
                .OrderByDescending(candidate => fingerprints[candidate].Quality)
                .ThenBy(candidate => candidate))
            {
                List<int>? cluster = clusters.FirstOrDefault(group =>
                    BelongToSameFamily(fingerprints[index], fingerprints[group[0]], threshold));
                if (cluster == null) clusters.Add([index]);
                else cluster.Add(index);
            }

            return clusters;
        }

        private static bool BelongToSameFamily(Fingerprint left, Fingerprint right, double threshold)
        {
            return Ratio(left.Duration, right.Duration) >= 0.25 &&
                Similarity(left.AttackSpectrum, right.AttackSpectrum) >= Math.Max(0.85, threshold - 0.08) &&
                Similarity(left.Spectrum, right.Spectrum) >= Math.Max(0.82, threshold - 0.12) &&
                Similarity(left.Envelope, right.Envelope) >= 0.55;
        }

        private static List<int> SelectVariants(List<int> cluster, Fingerprint[] fingerprints, int maxVariantsPerCluster)
        {
            List<int> selected = [];
            foreach (int candidate in cluster.OrderByDescending(index => fingerprints[index].Quality).ThenBy(index => index))
            {
                if (selected.Count >= maxVariantsPerCluster)
                {
                    break;
                }

                if (!selected.Any(index => AreDuplicates(fingerprints[candidate], fingerprints[index])))
                {
                    selected.Add(candidate);
                }
            }

            return selected;
        }

        private static bool AreDuplicates(Fingerprint left, Fingerprint right)
        {
            return Ratio(left.Duration, right.Duration) >= 0.65 &&
                Ratio(left.Centroid, right.Centroid) >= 0.975 &&
                Similarity(left.AttackSpectrum, right.AttackSpectrum) >= 0.98 &&
                Similarity(left.Spectrum, right.Spectrum) >= 0.94 &&
                Similarity(left.Envelope, right.Envelope) >= 0.995;
        }

        private static double Ratio(double left, double right)
        {
            double maximum = Math.Max(left, right);
            return maximum > 0.0 ? Math.Min(left, right) / maximum : 1.0;
        }

        private static double Similarity(double[] left, double[] right)
        {
            double dot = 0.0;
            double leftEnergy = 0.0;
            double rightEnergy = 0.0;
            for (int i = 0; i < left.Length; i++)
            {
                dot += left[i] * right[i];
                leftEnergy += left[i] * left[i];
                rightEnergy += right[i] * right[i];
            }

            return leftEnergy > 0.0 && rightEnergy > 0.0 ? dot / Math.Sqrt(leftEnergy * rightEnergy) : 0.0;
        }

        private static void Normalize(double[] values)
        {
            double length = Math.Sqrt(values.Sum(value => value * value));
            if (length <= 0.0) return;
            for (int i = 0; i < values.Length; i++)
            {
                values[i] /= length;
            }
        }

        private sealed record Fingerprint(double[] Spectrum, double[] AttackSpectrum, double[] Envelope,
            double Rms, double Duration, double Centroid, double Quality);
    }
}
