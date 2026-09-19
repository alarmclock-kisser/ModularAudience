using MathNet.Numerics.IntegralTransforms;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;

namespace ModularAudience.Audio.Processors_V4
{
    public sealed record AnalogSeparationBand(string Name, double LowHz, double HighHz);

    public sealed record AnalogSeparationSettings(
        int WindowSize,
        double Overlap,
        int Sharpness,
        bool SubtractFromOriginal,
        int Threads,
        bool TimbreAware = true,
        bool HarmonicAware = true)
    {
        public static AnalogSeparationSettings Default => new(4096, 0.5, 4, false, Math.Clamp(Environment.ProcessorCount / 2, 1, Environment.ProcessorCount));
    }

    public sealed record AnalogSeparationResult(IReadOnlyList<AudioObj> Stems, float[] MonoSource);

    public static class AnalogSeparationProcessor
    {
        public static async Task<AnalogSeparationResult> SeparateAsync(
            AudioObj source,
            IReadOnlyList<AnalogSeparationBand> bands,
            AnalogSeparationSettings settings,
            IProgress<double>? progress = null)
        {
            if (source == null || source.Data == null || source.Data.Length == 0 || source.SampleRate <= 0)
            {
                return new AnalogSeparationResult([], []);
            }

            if (bands == null || bands.Count == 0)
            {
                return new AnalogSeparationResult([], []);
            }

            return await Task.Run(() => SeparateCore(source, bands, settings, progress)).ConfigureAwait(false);
        }

        public static async Task<IReadOnlyList<AnalogSeparationBand>> AnalyzeBandsAsync(
            AudioObj source,
            int maxBands = 8,
            int windowSize = 4096,
            int threads = 2)
        {
            if (source == null || source.Data == null || source.Data.Length == 0 || source.SampleRate <= 0)
            {
                return [];
            }

            return await Task.Run(() => AnalyzeBandsCore(source, maxBands, windowSize, threads)).ConfigureAwait(false);
        }

        public static async Task<IReadOnlyList<AnalogSeparationBand>> AnalyzeSelectionBandsAsync(
            AudioObj source,
            long selectionStartSamples,
            long selectionEndSamples,
            int maxBands = 8,
            int windowSize = 4096,
            int threads = 2)
        {
            if (source == null || source.Data == null || source.Data.Length == 0 || source.SampleRate <= 0 || source.Channels <= 0)
            {
                return [];
            }

            return await Task.Run(() =>
            {
                int channels = source.Channels;
                long totalFrames = source.Data.LongLength / channels;
                long startFrame = Math.Clamp(selectionStartSamples / channels, 0, totalFrames);
                long endFrame = Math.Clamp(selectionEndSamples / channels, startFrame, totalFrames);
                int frameCount = (int) Math.Min(int.MaxValue, endFrame - startFrame);
                if (frameCount <= 0)
                {
                    return (IReadOnlyList<AnalogSeparationBand>) [];
                }

                float[] mono = new float[frameCount];
                Parallel.For(0, frameCount, new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(threads, 1, Environment.ProcessorCount) }, frame =>
                {
                    long sampleOffset = (startFrame + frame) * channels;
                    double sum = 0.0;
                    for (int channel = 0; channel < channels; channel++)
                    {
                        sum += source.Data[sampleOffset + channel];
                    }

                    mono[frame] = (float) (sum / channels);
                });

                return AnalyzeBandsCore(mono, source.SampleRate, maxBands, windowSize, threads);
            }).ConfigureAwait(false);
        }

        private static IReadOnlyList<AnalogSeparationBand> AnalyzeBandsCore(AudioObj source, int maxBands, int windowSize, int threads)
        {
            return AnalyzeBandsCore(ConvertToMono(source), source.SampleRate, maxBands, windowSize, threads);
        }

        private static IReadOnlyList<AnalogSeparationBand> AnalyzeBandsCore(float[] mono, int sampleRate, int maxBands, int windowSize, int threads)
        {
            maxBands = Math.Clamp(maxBands, 2, 12);
            int fftSize = 1;
            while (fftSize * 2 <= windowSize && fftSize * 2 <= mono.Length)
            {
                fftSize *= 2;
            }

            if (fftSize < 256)
            {
                return CreateFallbackBands(sampleRate, maxBands);
            }

            double lowHz = Math.Max(20.0, sampleRate * 4.0 / fftSize);
            double highHz = Math.Min(20000.0, sampleRate * 0.49);
            if (highHz <= lowHz)
            {
                return CreateFallbackBands(sampleRate, maxBands);
            }

            const int analysisBandCount = 48;
            double[] energy = new double[analysisBandCount];
            double[] window = new double[fftSize];
            for (int i = 0; i < fftSize; i++)
            {
                window[i] = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (fftSize - 1)));
            }

            int hop = fftSize / 2;
            int frameCount = Math.Max(1, (mono.Length - fftSize) / hop + 1);
            int sampledFrames = Math.Min(96, frameCount);
            int workerCount = Math.Clamp(threads, 1, Environment.ProcessorCount);
            double[][] frameEnergies = new double[sampledFrames][];
            Parallel.For(0, sampledFrames, new ParallelOptions { MaxDegreeOfParallelism = workerCount }, sampledFrame =>
            {
                int frame = sampledFrames == 1
                    ? 0
                    : (int) ((long) sampledFrame * (frameCount - 1) / (sampledFrames - 1));
                int offset = frame * hop;
                Complex[] spectrum = new Complex[fftSize];
                double mean = 0.0;
                for (int i = 0; i < fftSize; i++)
                {
                    mean += mono[offset + i];
                }

                mean /= fftSize;
                for (int i = 0; i < fftSize; i++)
                {
                    spectrum[i] = new Complex((mono[offset + i] - mean) * window[i], 0.0);
                }

                Fourier.Forward(spectrum, FourierOptions.Matlab);
                double[] sampledEnergy = new double[analysisBandCount];
                for (int analysisBand = 0; analysisBand < analysisBandCount; analysisBand++)
                {
                    double bandStart = lowHz * Math.Pow(highHz / lowHz, (double) analysisBand / analysisBandCount);
                    double bandEnd = lowHz * Math.Pow(highHz / lowHz, (double) (analysisBand + 1) / analysisBandCount);
                    int firstBin = Math.Max(1, (int) Math.Ceiling(bandStart / sampleRate * fftSize));
                    int lastBin = Math.Min(fftSize / 2 - 1, (int) Math.Floor(bandEnd / sampleRate * fftSize));
                    double power = 0.0;
                    int count = 0;
                    for (int bin = firstBin; bin <= lastBin; bin++)
                    {
                        double magnitude = spectrum[bin].Magnitude;
                        power += magnitude * magnitude;
                        count++;
                    }

                    sampledEnergy[analysisBand] = count > 0 ? Math.Sqrt(power) : 0.0;
                }
                frameEnergies[sampledFrame] = sampledEnergy;
            });

            for (int sampledFrame = 0; sampledFrame < sampledFrames; sampledFrame++)
            {
                for (int i = 0; i < energy.Length; i++)
                {
                    energy[i] += frameEnergies[sampledFrame][i] / sampledFrames;
                }
            }

            double[] smoothed = new double[energy.Length];
            for (int i = 0; i < energy.Length; i++)
            {
                double sum = energy[i];
                int count = 1;
                if (i > 0)
                {
                    sum += energy[i - 1];
                    count++;
                }

                if (i + 1 < energy.Length)
                {
                    sum += energy[i + 1];
                    count++;
                }

                smoothed[i] = sum / count;
            }

            double maximum = smoothed.Max();
            if (maximum <= 0.000001)
            {
                return CreateFallbackBands(sampleRate, maxBands);
            }

            double activityThreshold = maximum * 0.15;
            int activeStart = Array.FindIndex(smoothed, value => value >= activityThreshold);
            int activeEnd = Array.FindLastIndex(smoothed, value => value >= activityThreshold);
            if (activeStart < 0 || activeEnd <= activeStart)
            {
                return CreateFallbackBands(sampleRate, maxBands);
            }

            List<int> peaks = [];
            for (int i = 1; i + 1 < smoothed.Length; i++)
            {
                if (i >= activeStart && i <= activeEnd && smoothed[i] >= smoothed[i - 1] && smoothed[i] >= smoothed[i + 1] && smoothed[i] >= maximum * 0.18)
                {
                    peaks.Add(i);
                }
            }

            List<int> selectedPeaks = [];
            foreach (int peak in peaks.OrderByDescending(index => smoothed[index]))
            {
                if (selectedPeaks.All(selected => Math.Abs(selected - peak) >= 3))
                {
                    selectedPeaks.Add(peak);
                }

                if (selectedPeaks.Count >= maxBands)
                {
                    break;
                }
            }

            selectedPeaks.Sort();
            if (selectedPeaks.Count < 2)
            {
                return CreateFallbackBands(sampleRate, maxBands);
            }

            double activeLowHz = lowHz * Math.Pow(highHz / lowHz, (double) activeStart / analysisBandCount);
            double activeHighHz = lowHz * Math.Pow(highHz / lowHz, (double) (activeEnd + 1) / analysisBandCount);
            List<double> boundaries = [activeLowHz];
            for (int i = 0; i + 1 < selectedPeaks.Count; i++)
            {
                int start = selectedPeaks[i];
                int end = selectedPeaks[i + 1];
                int valley = start;
                for (int candidate = start + 1; candidate < end; candidate++)
                {
                    if (smoothed[candidate] < smoothed[valley])
                    {
                        valley = candidate;
                    }
                }

                boundaries.Add(lowHz * Math.Pow(highHz / lowHz, (valley + 0.5) / analysisBandCount));
            }

            boundaries.Add(activeHighHz);
            return CreateBandsFromBoundaries(boundaries);
        }

        private static IReadOnlyList<AnalogSeparationBand> CreateFallbackBands(int sampleRate, int maxBands)
        {
            double lowHz = 20.0;
            double highHz = Math.Min(20000.0, sampleRate * 0.49);
            int count = Math.Min(5, maxBands);
            List<double> boundaries = [];
            for (int i = 0; i <= count; i++)
            {
                boundaries.Add(lowHz * Math.Pow(highHz / lowHz, (double) i / count));
            }

            return CreateBandsFromBoundaries(boundaries);
        }

        private static IReadOnlyList<AnalogSeparationBand> CreateBandsFromBoundaries(IReadOnlyList<double> boundaries)
        {
            List<AnalogSeparationBand> bands = [];
            for (int i = 0; i + 1 < boundaries.Count; i++)
            {
                double low = Math.Round(boundaries[i]);
                double high = Math.Round(boundaries[i + 1]);
                if (high <= low)
                {
                    continue;
                }

                double center = Math.Sqrt(low * high);
                bands.Add(new AnalogSeparationBand(GetBandName(center, i + 1), low, high));
            }

            return bands;
        }

        private static string GetBandName(double centerHz, int index)
        {
            string family = centerHz switch
            {
                < 120 => "SubBass",
                < 250 => "Bass",
                < 800 => "LowMid",
                < 2500 => "Mid",
                < 6000 => "Presence",
                _ => "High"
            };
            return $"{family} {index}";
        }

        private static AnalogSeparationResult SeparateCore(
            AudioObj source,
            IReadOnlyList<AnalogSeparationBand> bands,
            AnalogSeparationSettings settings,
            IProgress<double>? progress)
        {
            float[] mono = ConvertToMono(source);
            if (mono.Length == 0)
            {
                return new AnalogSeparationResult([], mono);
            }

            double sourcePeak = 0.0;
            for (int i = 0; i < mono.Length; i++)
            {
                if (float.IsFinite(mono[i]))
                {
                    sourcePeak = Math.Max(sourcePeak, Math.Abs(mono[i]));
                }
            }

            double minimumBandPeak = Math.Max(0.000001, sourcePeak * 0.001);

            int windowSize = (int) Math.Pow(2, Math.Ceiling(Math.Log(Math.Max(2, settings.WindowSize), 2)));
            double overlap = Math.Clamp(settings.Overlap, 0.0, 0.99);
            int sharpness = Math.Max(1, settings.Sharpness);
            int threads = Math.Clamp(settings.Threads, 1, Environment.ProcessorCount);

            int step = (int) (windowSize * (1.0 - overlap));
            if (step <= 0)
            {
                step = windowSize / 2;
            }

            // Only start frames whose full window fits inside the signal.
            // This guarantees mono[offset + i] and acc[offset + i] stay in bounds.
            int numFrames = mono.Length >= windowSize ? (mono.Length - windowSize) / step + 1 : 0;
            if (numFrames == 0)
            {
                return new AnalogSeparationResult([], mono);
            }

            double[] window = new double[windowSize];
            for (int i = 0; i < windowSize; i++)
            {
                window[i] = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (windowSize - 1)));
            }

            // Pre-compute the per-frame windowed spectra once, in parallel.
            // Every band reuses this, so the FFT cost is paid a single time.
            Complex[][] frameSpectra = new Complex[numFrames][];
            for (int frame = 0; frame < numFrames; frame++)
            {
                frameSpectra[frame] = new Complex[windowSize];
            }

            Parallel.For(0, numFrames, new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(threads, 1, numFrames) }, frame =>
            {
                int offset = frame * step;
                Complex[] spectrum = frameSpectra[frame];
                double mean = 0.0;
                for (int i = 0; i < windowSize; i++)
                {
                    mean += mono[offset + i];
                }

                mean /= windowSize;
                for (int i = 0; i < windowSize; i++)
                {
                    spectrum[i] = new Complex((mono[offset + i] - mean) * window[i], 0.0);
                }

                Fourier.Forward(spectrum, FourierOptions.Matlab);
            });

            int[] bandLows = new int[bands.Count];
            int[] bandHighs = new int[bands.Count];
            for (int bandIndex = 0; bandIndex < bands.Count; bandIndex++)
            {
                bandLows[bandIndex] = Math.Max(1, (int) Math.Ceiling(bands[bandIndex].LowHz / source.SampleRate * windowSize));
                bandHighs[bandIndex] = Math.Min(windowSize / 2 - 1, (int) Math.Floor(bands[bandIndex].HighHz / source.SampleRate * windowSize));
            }

            double[][] frameWeightSums = new double[numFrames][];
            Parallel.For(0, numFrames, new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(threads, 1, numFrames) }, frame =>
            {
                Complex[] sourceSpectrum = frameSpectra[frame];
                double[] weightSums = new double[windowSize];
                for (int i = 0; i < windowSize; i++)
                {
                    int positiveBin = i <= windowSize / 2 ? i : windowSize - i;
                    double totalWeight = 0.0;
                    for (int otherBandIndex = 0; otherBandIndex < bands.Count; otherBandIndex++)
                    {
                        if (bandHighs[otherBandIndex] <= bandLows[otherBandIndex])
                        {
                            continue;
                        }

                        totalWeight += GetFeatureWeightedBandMask(
                            sourceSpectrum,
                            positiveBin,
                            bandLows[otherBandIndex],
                            bandHighs[otherBandIndex],
                            settings,
                            sharpness,
                            windowSize);
                    }

                    weightSums[i] = totalWeight;
                }

                frameWeightSums[frame] = weightSums;
            });

            double[] acc = new double[mono.Length];
            double[] norm = new double[mono.Length];
            List<AudioObj> stems = [];

            for (int bandIndex = 0; bandIndex < bands.Count; bandIndex++)
            {
                var band = bands[bandIndex];
                int low = bandLows[bandIndex];
                int high = bandHighs[bandIndex];

                if (high > low)
                {
                    Array.Clear(acc, 0, acc.Length);
                    Array.Clear(norm, 0, norm.Length);

                    // Copy each original spectrum before masking so every band starts from the same FFT data.
                    Complex[][] bandSpectra = new Complex[numFrames][];
                    Parallel.For(0, numFrames, new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(threads, 1, numFrames) }, frame =>
                    {
                        Complex[] spectrum = new Complex[windowSize];
                        Complex[] sourceSpectrum = frameSpectra[frame];
                        Array.Copy(sourceSpectrum, spectrum, windowSize);
                        for (int i = 0; i < windowSize; i++)
                        {
                            int positiveBin = i <= windowSize / 2 ? i : windowSize - i;
                            double targetWeight = GetFeatureWeightedBandMask(
                                sourceSpectrum,
                                positiveBin,
                                low,
                                high,
                                settings,
                                sharpness,
                                windowSize);
                            double totalWeight = frameWeightSums[frame][i];
                            double mask = totalWeight > 1e-12 ? targetWeight / totalWeight : 0.0;
                            spectrum[i] *= Math.Clamp(mask, 0.0, 1.0);
                        }

                        Fourier.Inverse(spectrum, FourierOptions.Matlab);
                        bandSpectra[frame] = spectrum;
                    });

                    // Overlap-add with normalization.
                    for (int frame = 0; frame < numFrames; frame++)
                    {
                        int offset = frame * step;
                        Complex[] spectrum = bandSpectra[frame];
                        int end = Math.Min(windowSize, mono.Length - offset);
                        for (int i = 0; i < end; i++)
                        {
                            acc[offset + i] += spectrum[i].Real * window[i];
                            norm[offset + i] += window[i] * window[i];
                        }
                    }

                    double maximumNorm = norm.Max();
                    double minimumNorm = maximumNorm * 0.01;
                    double filteredPeak = 0.0;
                    for (int i = 0; i < mono.Length; i++)
                    {
                        acc[i] = norm[i] >= minimumNorm ? acc[i] / norm[i] : 0.0;
                        if (!double.IsFinite(acc[i]))
                        {
                            acc[i] = 0.0;
                        }

                        filteredPeak = Math.Max(filteredPeak, Math.Abs(acc[i]));
                    }

                    if (filteredPeak <= minimumBandPeak)
                    {
                        progress?.Report((double) (bandIndex + 1) / bands.Count);
                        continue;
                    }

                    if (settings.SubtractFromOriginal)
                    {
                        for (int i = 0; i < mono.Length; i++)
                        {
                            acc[i] = mono[i] - acc[i];
                        }
                    }

                    double peak = 0.0;
                    for (int i = 0; i < mono.Length; i++)
                    {
                        peak = Math.Max(peak, Math.Abs(acc[i]));
                    }

                    double gain = peak > 0.000001 ? 0.95 / peak : 1.0;
                    float[] result = new float[mono.Length];
                    for (int i = 0; i < mono.Length; i++)
                    {
                        result[i] = (float) Math.Clamp(acc[i] * gain, -1.0, 1.0);
                    }

                    AudioObj stem = source.Clone();
                    stem.Data = result;
                    stem.Channels = 1;
                    stem.Length = result.LongLength;
                    stem.Duration = TimeSpan.FromSeconds(result.LongLength / (double) Math.Max(1, stem.SampleRate));
                    stem.Rename($"{source.OriginalName}_{band.Name}");
                    stems.Add(stem);
                }

                progress?.Report((double) (bandIndex + 1) / bands.Count);
            }

            return new AnalogSeparationResult(stems, mono);
        }

        private static double GetBandMask(int bin, int low, int high, int sharpness)
        {
            if (bin >= low && bin <= high)
            {
                return 1.0;
            }

            double distance = Math.Min(Math.Abs(bin - low), Math.Abs(bin - high));
            if (distance >= sharpness)
            {
                return 0.0;
            }

            double t = 1.0 - distance / sharpness;
            return t * t * (3.0 - 2.0 * t);
        }

        private static double GetFeatureWeightedBandMask(
            Complex[] spectrum,
            int positiveBin,
            int low,
            int high,
            AnalogSeparationSettings settings,
            int sharpness,
            int windowSize)
        {
            int transitionBins = Math.Max(sharpness, Math.Max(8, positiveBin / 16));
            double baseMask = GetBandMask(positiveBin, low, high, transitionBins);
            if (baseMask <= 0.0)
            {
                return 0.0;
            }

            double featureWeight = 1.0;
            if (settings.TimbreAware)
            {
                featureWeight *= 0.85 + (0.3 * GetTimbrePeakMask(spectrum, positiveBin));
            }

            if (settings.HarmonicAware)
            {
                featureWeight *= GetHarmonicBandWeight(spectrum, positiveBin, low, high);
            }

            return baseMask * featureWeight;
        }

        private static double GetTimbrePeakMask(Complex[] spectrum, int positiveBin)
        {
            if (positiveBin <= 1 || positiveBin >= spectrum.Length / 2 - 1)
            {
                return 1.0;
            }

            double magnitude = spectrum[positiveBin].Magnitude;
            double left = spectrum[positiveBin - 1].Magnitude;
            double right = spectrum[positiveBin + 1].Magnitude;

            double neighborhood = (left + right) * 0.5;
            double contrast = magnitude / Math.Max(1e-12, neighborhood);
            double peakContribution = Math.Clamp((contrast - 0.65) / 1.35, 0.0, 1.0);
            return 0.5 + (0.5 * peakContribution);
        }

        private static double GetHarmonicBandWeight(Complex[] spectrum, int positiveBin, int low, int high)
        {
            if (positiveBin <= 1 || positiveBin >= spectrum.Length / 2 - 1)
            {
                return 1.0;
            }

            double currentMagnitude = spectrum[positiveBin].Magnitude;
            if (currentMagnitude <= 1e-12)
            {
                return 1.0;
            }

            double familySupport = 0.0;
            int familyCount = 0;
            for (int divisor = 2; divisor <= 6; divisor++)
            {
                int fundamentalBin = positiveBin / divisor;
                if (fundamentalBin <= 1 || fundamentalBin < low || fundamentalBin > high)
                {
                    continue;
                }

                double fundamentalMagnitude = GetNeighborhoodMagnitude(spectrum, fundamentalBin);
                familySupport += Math.Min(2.0, fundamentalMagnitude / currentMagnitude);
                familyCount++;
            }

            for (int multiplier = 2; multiplier <= 6; multiplier++)
            {
                int harmonicBin = positiveBin * multiplier;
                if (harmonicBin >= spectrum.Length / 2 || harmonicBin < low || harmonicBin > high)
                {
                    continue;
                }

                double harmonicMagnitude = GetNeighborhoodMagnitude(spectrum, harmonicBin);
                familySupport += Math.Min(2.0, harmonicMagnitude / currentMagnitude);
                familyCount++;
            }

            if (familyCount == 0)
            {
                return 1.0;
            }

            double support = Math.Clamp(familySupport / familyCount, 0.0, 1.0);
            return 1.0 + (0.75 * support);
        }

        private static double GetNeighborhoodMagnitude(Complex[] spectrum, int bin)
        {
            int lastPositiveBin = (spectrum.Length / 2) - 1;
            int clampedBin = Math.Clamp(bin, 1, lastPositiveBin);
            double magnitude = spectrum[clampedBin].Magnitude;
            if (clampedBin > 1)
            {
                magnitude = Math.Max(magnitude, spectrum[clampedBin - 1].Magnitude * 0.75);
            }

            if (clampedBin < lastPositiveBin)
            {
                magnitude = Math.Max(magnitude, spectrum[clampedBin + 1].Magnitude * 0.75);
            }

            return magnitude;
        }

        private static float[] ConvertToMono(AudioObj audio)
        {
            if (audio.Data == null || audio.Data.Length == 0 || audio.Channels <= 0)
            {
                return [];
            }

            int monoSampleCount = audio.Data.Length / audio.Channels;
            float[] monoData = new float[monoSampleCount];
            int channels = audio.Channels;
            float[] data = audio.Data;

            Parallel.For(0, monoSampleCount, i =>
            {
                float sum = 0.0f;
                for (int channel = 0; channel < channels; channel++)
                {
                    sum += data[i * channels + channel];
                }

                monoData[i] = sum / channels;
            });

            return monoData;
        }
    }
}
