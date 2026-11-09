using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace ModularAudience.Audio.Effects;

public static partial class AudioEffectProcessor
{
    private static AudioEffectResult RemoveHum(float[] source, AudioEffectSettings settings, RenderContext context)
    {
        double fundamental = settings.Choice("Mains") switch { 0 => 50, 1 => 60, _ => settings["FrequencyHz"] };
        int harmonics = Math.Min(settings.Choice("Harmonics"), (int)Math.Floor(context.Rate * 0.45 / fundamental));
        var filters = Enumerable.Range(0, context.Channels).Select(_ => Enumerable.Range(1, harmonics)
            .Select(harmonic => Biquad.Notch(fundamental * harmonic, context.Rate, settings["Q"])).ToArray()).ToArray();
        var dc = Enumerable.Range(0, context.Channels).Select(_ => Biquad.HighPass(15, context.Rate)).ToArray();
        var output = new float[source.Length];
        for (int frame = 0; frame < source.Length / context.Channels; frame++)
        {
            context.Check(frame, source.Length / context.Channels);
            for (int channel = 0; channel < context.Channels; channel++)
            {
                int index = frame * context.Channels + channel; double value = source[index];
                if (settings.Flag("RemoveDc")) value = dc[channel].Process(value);
                for (int harmonic = 0; harmonic < harmonics; harmonic++) value = filters[channel][harmonic].Process(value);
                output[index] = (float)MixSample(source[index], value, settings);
            }
        }
        return new(output, context.Channels);
    }

    private static AudioEffectResult ReduceNoise(float[] source, AudioEffectSettings settings, RenderContext context)
    {
        int channels = context.Channels, frames = source.Length / channels;
        if (settings["AmountPercent"] == 0 || settings["ReductionDb"] == 0) return new(source, channels);
        int size = 1024 << settings.Choice("FftSize"), hop = size / 4, bins = size / 2 + 1;
        int windowCount = (int)Math.Ceiling(frames / (double)hop) + 1;
        double[] window = Enumerable.Range(0, size).Select(i => 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / size)).ToArray();
        double windowEnergy = window.Sum(value => value * value);
        var spectra = Enumerable.Range(0, channels).Select(_ => new Complex[size]).ToArray();
        var profile = new double[bins];
        void Analyze(int index)
        {
            context.Token.ThrowIfCancellationRequested();
            int start = index * hop - size / 2;
            for (int channel = 0; channel < channels; channel++)
            {
                Complex[] spectrum = spectra[channel];
                for (int i = 0; i < size; i++)
                {
                    int frame = start + i;
                    spectrum[i] = new Complex(frame >= 0 && frame < frames ? source[frame * channels + channel] * window[i] : 0, 0);
                }
                Fourier.Forward(spectrum, FourierOptions.Matlab);
            }
        }

        if (settings.Choice("Profile") == 2)
            Array.Fill(profile, Math.Pow(DbToGain(settings["NoiseFloorDb"]), 2) * windowEnergy);
        else
        {
            int wanted = Math.Clamp((int)Math.Ceiling(settings["ProfileMs"] * context.Rate / (1000 * hop)), 1, Math.Min(windowCount, 512));
            IEnumerable<int> indices;
            if (settings.Choice("Profile") == 1) indices = Enumerable.Range(0, wanted);
            else
            {
                var energies = new List<(int Index, double Energy)>();
                for (int index = 0; index < windowCount; index++)
                {
                    context.Token.ThrowIfCancellationRequested();
                    int start = Math.Max(0, index * hop - size / 2), end = Math.Min(frames, index * hop + size / 2);
                    // Prefer complete windows so zero padding isn't mistaken for a noise-only interval.
                    if (frames >= size && end - start < size) continue;
                    double energy = 0;
                    for (int frame = start; frame < end; frame++)
                        for (int channel = 0; channel < channels; channel++)
                        {
                            double sample = source[frame * channels + channel]; energy += sample * sample;
                        }
                    energies.Add((index, energy / Math.Max(1, end - start)));
                }
                indices = energies.OrderBy(pair => pair.Energy).Take(wanted).Select(pair => pair.Index).ToArray();
            }
            int count = 0;
            foreach (int index in indices)
            {
                Analyze(index); count++;
                for (int bin = 0; bin < bins; bin++)
                    for (int channel = 0; channel < channels; channel++)
                        profile[bin] += spectra[channel][bin].Magnitude * spectra[channel][bin].Magnitude / channels;
            }
            if (count > 0) for (int bin = 0; bin < bins; bin++) profile[bin] /= count;
        }

        // Double accumulation and squared-window normalization preserve the exact
        // timing and edges. A shared gain spectrum keeps stereo phase/coherence.
        var accumulated = new double[source.Length]; var weights = new double[frames];
        var previousGain = Enumerable.Repeat(1d, bins).ToArray(); var targets = new double[bins];
        double minimumGain = DbToGain(-settings["ReductionDb"]), amount = settings["AmountPercent"] / 100;
        double smoothing = settings["SmoothingPercent"] / 100;
        for (int index = 0; index < windowCount; index++)
        {
            Analyze(index); context.Progress?.Report(index / (double)windowCount);
            for (int bin = 0; bin < bins; bin++)
            {
                double power = 0;
                for (int channel = 0; channel < channels; channel++) power += spectra[channel][bin].Magnitude * spectra[channel][bin].Magnitude / channels;
                targets[bin] = Math.Max(minimumGain, Math.Sqrt(Math.Max(0, power - amount * profile[bin]) / Math.Max(power, 1e-24)));
            }
            for (int bin = 0; bin < bins; bin++)
            {
                double target = (targets[Math.Max(0, bin - 1)] + targets[bin] * 2 + targets[Math.Min(bins - 1, bin + 1)]) / 4;
                double gain = target >= previousGain[bin] ? target : target + smoothing * (previousGain[bin] - target);
                previousGain[bin] = gain;
                for (int channel = 0; channel < channels; channel++)
                {
                    spectra[channel][bin] *= gain;
                    if (bin > 0 && bin < size / 2) spectra[channel][size - bin] = Complex.Conjugate(spectra[channel][bin]);
                }
            }
            int start = index * hop - size / 2;
            for (int channel = 0; channel < channels; channel++) Fourier.Inverse(spectra[channel], FourierOptions.Matlab);
            for (int i = 0; i < size; i++)
            {
                int frame = start + i;
                if (frame < 0 || frame >= frames) continue;
                weights[frame] += window[i] * window[i];
                for (int channel = 0; channel < channels; channel++) accumulated[frame * channels + channel] += spectra[channel][i].Real * window[i];
            }
        }
        var output = new float[source.Length];
        for (int frame = 0; frame < frames; frame++)
        {
            context.Check(frame, frames);
            for (int channel = 0; channel < channels; channel++)
            {
                int i = frame * channels + channel;
                double clean = weights[frame] > 1e-12 ? accumulated[i] / weights[frame] : source[i];
                output[i] = (float)MixSample(source[i], clean, settings);
            }
        }
        return new(output, channels);
    }
}
