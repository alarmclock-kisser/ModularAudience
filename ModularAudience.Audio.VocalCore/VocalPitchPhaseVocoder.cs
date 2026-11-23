using System.Numerics;
using MathNet.Numerics.Providers.FourierTransform;

namespace ModularAudience.Audio.VocalCore.Dsp;

internal static class VocalPitchPhaseVocoder
{
    internal static float[] Render(
        float[] source, int sampleRate, int hop, double[] shifts, float[] wet,
        int size, double formantPreservationPercent, CancellationToken cancellationToken)
    {
        int frames = source.Length;
        if (frames == 0)
        {
            return [];
        }

        int fftHop = size / 8;
        int bins = size / 2 + 1;
        int lastCenter = frames + size / 2;
        double[] window = Enumerable.Range(0, size)
            .Select(index => 0.5 - 0.5 * Math.Cos(2 * Math.PI * index / size)).ToArray();
        float[] weights = new float[frames];
        float[] output = new float[frames];
        for (long center = 0; center <= lastCenter; center += fftHop)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int index = 0; index < size; index++)
            {
                long frame = center - size / 2 + index;
                if (frame >= 0 && frame < frames)
                {
                    weights[(int)frame] += (float)(window[index] * window[index]);
                }
            }
        }

        var fourier = new ManagedFourierTransformProvider();
        Complex[] spectrum = new Complex[size];
        double[] lastPhase = new double[bins];
        double[] synthPhase = new double[bins];
        double[] magnitude = new double[bins];
        double[] phase = new double[bins];
        double[] frequencies = new double[bins];
        double[] mappedMagnitude = new double[bins];
        double[] mappedFrequency = new double[bins];
        double[] mappedReal = new double[bins];
        double[] mappedImaginary = new double[bins];
        double[] prefix = new double[bins + 1];
        bool[] wasPresent = new bool[bins];
        bool first = true;
        for (long center = 0; center <= lastCenter; center += fftHop)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int currentFrame = (int)Math.Min(center, frames - 1);
            int envelopeRadius = Math.Max(1, (int)Math.Round(400 * size / (double)sampleRate));
            for (int index = 0; index < size; index++)
            {
                long frame = center - size / 2 + index;
                spectrum[index] = new Complex(
                    frame >= 0 && frame < frames ? source[(int)frame] * window[index] : 0, 0);
            }
            fourier.Forward(spectrum, FourierTransformScaling.NoScaling);
            cancellationToken.ThrowIfCancellationRequested();
            prefix[0] = 0;
            for (int bin = 0; bin < bins; bin++)
            {
                magnitude[bin] = spectrum[bin].Magnitude;
                phase[bin] = Math.Atan2(spectrum[bin].Imaginary, spectrum[bin].Real);
                double expected = 2 * Math.PI * bin * fftHop / size;
                double residual = first ? 0 : Wrap(phase[bin] - lastPhase[bin] - expected);
                frequencies[bin] = 2 * Math.PI * bin / size + residual / fftHop;
                lastPhase[bin] = phase[bin];
                prefix[bin + 1] = prefix[bin] + Math.Log(Math.Max(1e-12, magnitude[bin]));
            }
            Array.Clear(mappedMagnitude);
            Array.Clear(mappedFrequency);
            Array.Clear(mappedReal);
            Array.Clear(mappedImaginary);
            double shiftPosition = Math.Clamp(center / (double)hop, 0, shifts.Length - 1);
            int shiftLeft = (int)shiftPosition;
            int shiftRight = Math.Min(shiftLeft + 1, shifts.Length - 1);
            double shift = shifts[shiftLeft] + (shifts[shiftRight] - shifts[shiftLeft])
                * (shiftPosition - shiftLeft);
            double ratio = Math.Pow(2, shift / 12);
            for (int bin = 0; bin < bins; bin++)
            {
                int target = (int)Math.Round(bin * ratio);
                if (target >= bins)
                {
                    continue;
                }

                double amplitude = magnitude[bin];
                if (formantPreservationPercent > 0)
                {
                    double envelopeDifference = Envelope(prefix, target, envelopeRadius, bins)
                        - Envelope(prefix, bin, envelopeRadius, bins);
                    amplitude *= Math.Clamp(
                        Math.Exp(envelopeDifference * formantPreservationPercent / 100), 0.25, 4);
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
                        : Wrap(synthPhase[bin] + mappedFrequency[bin] / mappedMagnitude[bin] * fftHop);
                    Complex value = Complex.FromPolarCoordinates(mappedMagnitude[bin], synthPhase[bin]);
                    spectrum[bin] = bin is 0 || bin == bins - 1
                        ? new Complex(value.Real, 0)
                        : value;
                    if (bin > 0 && bin < bins - 1)
                    {
                        spectrum[size - bin] = Complex.Conjugate(value);
                    }
                }
                wasPresent[bin] = present;
            }
            fourier.Backward(spectrum, FourierTransformScaling.BackwardScaling);
            cancellationToken.ThrowIfCancellationRequested();
            for (int index = 0; index < size; index++)
            {
                long frame = center - size / 2 + index;
                if (frame >= 0 && frame < frames)
                {
                    output[(int)frame] += (float)(spectrum[index].Real * window[index]);
                }
            }
            first = false;
        }

        for (int frame = 0; frame < frames; frame++)
        {
            if ((frame & 2047) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            output[frame] /= Math.Max(1e-12f, weights[frame]);
            double wetPosition = Math.Clamp(frame / (double)hop, 0, wet.Length - 1);
            int wetLeft = (int)wetPosition;
            int wetRight = Math.Min(wetLeft + 1, wet.Length - 1);
            double amount = wet[wetLeft] + (wet[wetRight] - wet[wetLeft]) * (wetPosition - wetLeft);
            output[frame] = (float)(source[frame] + (output[frame] - source[frame]) * amount);
        }
        return output;
    }

    private static double Envelope(double[] prefix, int bin, int radius, int bins)
    {
        int start = Math.Max(0, bin - radius);
        int end = Math.Min(bins, bin + radius + 1);
        return (prefix[end] - prefix[start]) / (end - start);
    }

    private static double Wrap(double angle) => Math.IEEERemainder(angle, 2 * Math.PI);
}
