using System.Numerics;
using MathNet.Numerics.Providers.FourierTransform;

namespace ModularAudience.Audio.Effects.AutoTune;

/// <summary>Time-varying, duration-preserving spectral pitch shift with normalized window overlap-add.</summary>
internal static class AutoTunePhaseVocoder
{
    internal static float[] Render(float[] source, int sampleRate, int channels, AutoTuneSettings settings,
        AutoTuneCorrectionCurve curve, int maximumThreads, IProgress<double>? progress, CancellationToken token)
    {
        int frames = source.Length / channels, size = settings.FftSize, hop = size / settings.Overlap, bins = size / 2 + 1;
        int lastCenter = frames + size / 2;
        double[] window = Enumerable.Range(0, size).Select(i => 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / size)).ToArray();
        float[] weights = new float[frames], output = new float[source.Length];
        for (long center = 0; center <= lastCenter; center += hop)
        {
            token.ThrowIfCancellationRequested();
            for (int i = 0; i < size; i++)
            {
                long frame = center - size / 2 + i;
                if (frame >= 0 && frame < frames) weights[(int)frame] += (float)(window[i] * window[i]);
            }
        }
        long totalWindows = channels * ((long)lastCenter / hop + 1), finished = 0;
        void RenderChannel(int channel)
        {
            var fourier = new ManagedFourierTransformProvider();
            Complex[] spectrum = new Complex[size];
            double[] lastPhase = new double[bins], synthPhase = new double[bins], magnitude = new double[bins], phase = new double[bins];
            double[] frequencies = new double[bins], mappedMagnitude = new double[bins], mappedFrequency = new double[bins];
            double[] mappedReal = new double[bins], mappedImaginary = new double[bins], prefix = new double[bins + 1];
            bool[] wasPresent = new bool[bins];
            int envelopeRadius = Math.Max(1, (int)Math.Round(settings.FormantSmoothingHz * size / sampleRate));
            bool first = true;
            for (long center = 0; center <= lastCenter; center += hop)
            {
                token.ThrowIfCancellationRequested();
                for (int i = 0; i < size; i++)
                {
                    long frame = center - size / 2 + i;
                    spectrum[i] = new Complex(frame >= 0 && frame < frames ? source[(int)frame * channels + channel] * window[i] : 0, 0);
                }
                fourier.Forward(spectrum, FourierTransformScaling.NoScaling);
                token.ThrowIfCancellationRequested();
                prefix[0] = 0;
                for (int bin = 0; bin < bins; bin++)
                {
                    magnitude[bin] = spectrum[bin].Magnitude;
                    phase[bin] = Math.Atan2(spectrum[bin].Imaginary, spectrum[bin].Real);
                    double expected = 2 * Math.PI * bin * hop / size;
                    double residual = first ? 0 : Wrap(phase[bin] - lastPhase[bin] - expected);
                    frequencies[bin] = 2 * Math.PI * bin / size + residual / hop;
                    lastPhase[bin] = phase[bin];
                    prefix[bin + 1] = prefix[bin] + Math.Log(Math.Max(1e-12, magnitude[bin]));
                }
                Array.Clear(mappedMagnitude); Array.Clear(mappedFrequency); Array.Clear(mappedReal); Array.Clear(mappedImaginary);
                double ratio = Math.Pow(2, curve.ShiftAt((int)Math.Min(center, frames - 1)) / 12);
                for (int bin = 0; bin < bins; bin++)
                {
                    int target = (int)Math.Round(bin * ratio);
                    if (target >= bins) continue; // Discard above-Nyquist content instead of folding it.
                    double amplitude = magnitude[bin];
                    if (settings.FormantPreservationPercent > 0)
                    {
                        double difference = Envelope(prefix, target, envelopeRadius, bins) - Envelope(prefix, bin, envelopeRadius, bins);
                        amplitude *= Math.Clamp(Math.Exp(difference * settings.FormantPreservationPercent / 100), 0.25, 4);
                    }
                    mappedMagnitude[target] += amplitude;
                    mappedFrequency[target] += amplitude * frequencies[bin] * ratio;
                    mappedReal[target] += amplitude * Math.Cos(phase[bin]);
                    mappedImaginary[target] += amplitude * Math.Sin(phase[bin]);
                }
                Array.Clear(spectrum);
                for (int bin = 0; bin < bins; bin++)
                {
                    bool present = mappedMagnitude[bin] > 1e-12;
                    if (present)
                    {
                        synthPhase[bin] = first || !wasPresent[bin]
                            ? Math.Atan2(mappedImaginary[bin], mappedReal[bin])
                            : Wrap(synthPhase[bin] + mappedFrequency[bin] / mappedMagnitude[bin] * hop);
                        Complex value = Complex.FromPolarCoordinates(mappedMagnitude[bin], synthPhase[bin]);
                        spectrum[bin] = bin is 0 || bin == bins - 1 ? new Complex(value.Real, 0) : value;
                        if (bin > 0 && bin < bins - 1) spectrum[size - bin] = Complex.Conjugate(value);
                    }
                    wasPresent[bin] = present;
                }
                fourier.Backward(spectrum, FourierTransformScaling.BackwardScaling);
                token.ThrowIfCancellationRequested();
                for (int i = 0; i < size; i++)
                {
                    long frame = center - size / 2 + i;
                    if (frame >= 0 && frame < frames) output[(int)frame * channels + channel] += (float)(spectrum[i].Real * window[i]);
                }
                first = false;
                long completed = Interlocked.Increment(ref finished);
                if ((completed & 15) == 0) progress?.Report(0.55 + 0.35 * completed / totalWindows);
            }
        }
        if (channels == 1 || maximumThreads == 1)
        {
            for (int channel = 0; channel < channels; channel++) RenderChannel(channel);
        }
        else
        {
            Parallel.For(0, channels, new ParallelOptions
            {
                CancellationToken = token,
                MaxDegreeOfParallelism = Math.Min(channels, maximumThreads)
            }, RenderChannel);
        }
        for (int frame = 0; frame < frames; frame++)
        {
            if ((frame & 2047) == 0) token.ThrowIfCancellationRequested();
            double weight = Math.Max(1e-12, weights[frame]);
            for (int channel = 0; channel < channels; channel++) output[frame * channels + channel] /= (float)weight;
        }
        return output;
    }

    private static double Envelope(double[] prefix, int bin, int radius, int bins)
    {
        int start = Math.Max(0, bin - radius), end = Math.Min(bins, bin + radius + 1);
        return (prefix[end] - prefix[start]) / (end - start);
    }

    private static double Wrap(double angle) => Math.IEEERemainder(angle, 2 * Math.PI);
}
