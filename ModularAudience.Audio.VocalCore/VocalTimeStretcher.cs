using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace ModularAudience.Audio.Vocal;

internal static class VocalTimeStretcher
{
    internal static float[] Stretch(
        float[] data, int channels, int targetFrames, int fftSize = 2048,
        double overlap = 0.875, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (channels <= 0 || data.Length == 0 || data.Length % channels != 0 || targetFrames < 1
            || fftSize < 256 || fftSize > 16384 || (fftSize & (fftSize - 1)) != 0
            || !double.IsFinite(overlap) || overlap < 0.5 || overlap > 0.95)
        {
            throw new ArgumentException("Invalid phase-vocoder format, frame count or window settings.");
        }

        long outputSamples = (long)targetFrames * channels;
        if (outputSamples > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(targetFrames), "Phase-vocoder output is too large.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        int inputFrames = data.Length / channels;
        if (targetFrames == inputFrames)
        {
            return (float[])data.Clone();
        }

        float[] result = new float[(int)outputSamples];
        for (int channel = 0; channel < channels; channel++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            float[] mono = new float[inputFrames];
            for (int frame = 0; frame < inputFrames; frame++)
            {
                mono[frame] = data[frame * channels + channel];
            }

            float[] stretched = StretchChannel(mono, targetFrames, fftSize, overlap, cancellationToken);
            for (int frame = 0; frame < targetFrames; frame++)
            {
                if ((frame & 2047) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                result[frame * channels + channel] = stretched[frame];
            }
        }
        return result;
    }

    private static float[] StretchChannel(
        float[] input, int targetFrames, int fftSize, double overlap, CancellationToken cancellationToken)
    {
        int inputFrames = input.Length;
        int size = fftSize > inputFrames
            ? 1 << (int)Math.Ceiling(Math.Log2(Math.Max(256, inputFrames)))
            : fftSize;
        int bins = size / 2 + 1;
        int analysisHop = Math.Max(1, (int)Math.Round(size * (1 - overlap)));
        double synthesisHop = analysisHop * targetFrames / (double)inputFrames;
        int center = size / 2;
        int analysisFrames = (int)Math.Ceiling(inputFrames / (double)analysisHop) + 1;
        int outputCapacity = checked((int)Math.Ceiling((analysisFrames - 1) * synthesisHop + size) + size);
        double[] window = Enumerable.Range(0, size)
            .Select(index => 0.5 * (1 - Math.Cos(2 * Math.PI * index / (size - 1)))).ToArray();
        double[] output = new double[outputCapacity];
        double[] weights = new double[outputCapacity];
        double[] previousPhase = new double[bins];
        double[] synthesisPhase = new double[bins];
        double[] frequencies = new double[bins];
        double[] magnitudes = new double[bins];
        Complex[] spectrum = new Complex[size];
        int outputPosition = 0;
        double synthesisTime = 0;

        for (int frame = 0; frame < analysisFrames; frame++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int position = frame * analysisHop - center;
            for (int sample = 0; sample < size; sample++)
            {
                int source = position + sample;
                spectrum[sample] = new Complex(
                    (source >= 0 && source < inputFrames ? input[source] : 0) * window[sample], 0);
            }
            Fourier.Forward(spectrum, FourierOptions.Matlab);
            for (int bin = 0; bin < bins; bin++)
            {
                magnitudes[bin] = spectrum[bin].Magnitude;
                double phase = Math.Atan2(spectrum[bin].Imaginary, spectrum[bin].Real);
                double expected = 2 * Math.PI * bin * analysisHop / size;
                double residual = frame == 0 ? 0 : Wrap(phase - previousPhase[bin] - expected);
                frequencies[bin] = 2 * Math.PI * bin / size + residual / analysisHop;
                previousPhase[bin] = phase;
                if (frame == 0)
                {
                    synthesisPhase[bin] = phase;
                }
                else
                {
                    int previousOutput = (int)Math.Round((frame - 1) * synthesisHop);
                    synthesisPhase[bin] += frequencies[bin] * (outputPosition - previousOutput);
                }
            }
            for (int bin = 0; bin <= size / 2; bin++)
            {
                spectrum[bin] = Complex.FromPolarCoordinates(magnitudes[bin], synthesisPhase[bin]);
            }

            for (int bin = size / 2 + 1; bin < size; bin++)
            {
                spectrum[bin] = Complex.Conjugate(spectrum[size - bin]);
            }

            Fourier.Inverse(spectrum, FourierOptions.Matlab);
            for (int sample = 0; sample < size; sample++)
            {
                int destination = outputPosition + sample;
                if ((uint)destination >= (uint)outputCapacity)
                {
                    continue;
                }

                output[destination] += spectrum[sample].Real * window[sample];
                weights[destination] += window[sample] * window[sample];
            }
            outputPosition = (int)Math.Round(++synthesisTime * synthesisHop);
        }

        float[] result = new float[targetFrames];
        for (int frame = 0; frame < targetFrames; frame++)
        {
            if ((frame & 2047) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            int source = frame + center;
            if (source < outputCapacity && weights[source] > 1e-10)
            {
                result[frame] = (float)(output[source] / weights[source]);
            }
        }
        return result;
    }

    private static double Wrap(double angle) => Math.IEEERemainder(angle, 2 * Math.PI);
}
