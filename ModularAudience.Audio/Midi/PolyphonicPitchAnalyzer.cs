using MathNet.Numerics.IntegralTransforms;
using System.Numerics;

namespace ModularAudience.Audio.Midi;

internal sealed record PolyphonicAnalysis(float[][] Strengths, float[] Rms, int HopSize, int SampleCount, int SampleRate);

internal static class PolyphonicPitchAnalyzer
{
    internal const int LowestNote = 36;
    internal const int HighestNote = 96;

    internal static PolyphonicAnalysis Analyze(AudioObj audio, int maxWorkers, CancellationToken cancellationToken, IProgress<double>? progress)
    {
        int channels = Math.Max(1, audio.Channels);
        int sampleCount = audio.Data.Length / channels;
        int hop = Math.Max(1, audio.SampleRate / 100);
        int count = (sampleCount + hop - 1) / hop;
        int windowSize = WindowSize(audio.SampleRate);
        float[][] strengths = new float[count][];
        float[][] balances = new float[count][];
        float[] rms = new float[count];
        int workers = Math.Clamp(maxWorkers, 1, Math.Min(Environment.ProcessorCount, count));
        object progressGate = new();
        int completed = 0;
        ParallelOptions options = new() { MaxDegreeOfParallelism = workers, CancellationToken = cancellationToken };
        Parallel.For(0, workers, options, worker =>
        {
            SpectrumWindow window = new(windowSize);
            for (int frame = worker; frame < count; frame += workers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int start = frame * hop;
                rms[frame] = FrameRms(audio.Data, channels, start, Math.Min(hop, sampleCount - start));
                window.Read(audio.Data, channels, sampleCount, start + hop / 2);
                (strengths[frame], balances[frame]) = window.FindPeaks(audio.SampleRate);
                lock (progressGate)
                {
                    completed++;
                    if (completed % 32 == 0 || completed == count)
                    {
                        progress?.Report(0.70 * completed / count);
                    }
                }
            }
        });
        SuppressHarmonics(strengths, balances, cancellationToken);
        progress?.Report(0.78);
        return new PolyphonicAnalysis(strengths, rms, hop, sampleCount, audio.SampleRate);
    }

    private static int WindowSize(int sampleRate)
    {
        int size = 256;
        while (size < sampleRate * 0.08)
        {
            size *= 2;
        }
        return size;
    }

    private static float FrameRms(float[] data, int channels, int start, int count)
    {
        double energy = 0;
        for (int i = start * channels; i < (start + count) * channels; i++)
        {
            double value = float.IsFinite(data[i]) ? data[i] : 0;
            energy += value * value;
        }
        return (float) Math.Sqrt(energy / Math.Max(1, count * channels));
    }

    private static void SuppressHarmonics(float[][] strengths, float[][] balances, CancellationToken cancellationToken)
    {
        float[][] original = strengths.Select(frame => (float[]) frame.Clone()).ToArray();
        int[][] spans = FindPitchSpans(original, cancellationToken);
        for (int frame = 0; frame < strengths.Length; frame++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int note = LowestNote; note <= HighestNote; note++)
            {
                if (strengths[frame][note] <= 0)
                {
                    continue;
                }
                for (int harmonic = 2; harmonic <= 8; harmonic++)
                {
                    int upper = note + (int) Math.Round(12 * Math.Log2(harmonic));
                    if (upper > HighestNote || original[frame][upper] <= 0
                        || strengths[frame][note] < original[frame][upper] * 0.08f
                        || HaveDisjointStereoPositions(balances[frame][note], balances[frame][upper]))
                    {
                        continue;
                    }
                    int lowerSpan = spans[note][frame];
                    int upperSpan = spans[upper][frame];
                    // A held upper melody must survive shorter accompaniment an octave below it.
                    if (upperSpan > lowerSpan * 1.7 && upperSpan - lowerSpan >= 8)
                    {
                        continue;
                    }
                    bool sustainedFundamental = lowerSpan >= Math.Max(6, upperSpan * 0.8);
                    if (sustainedFundamental || EnvelopeSimilarity(original, frame, note, upper) >= 0.93)
                    {
                        strengths[frame][upper] = 0;
                    }
                }
            }
            KeepStrongestCandidates(strengths[frame]);
        }
    }

    private static double EnvelopeSimilarity(float[][] frames, int center, int lower, int upper)
    {
        double product = 0;
        double lowerEnergy = 0;
        double upperEnergy = 0;
        for (int index = Math.Max(0, center - 10); index <= Math.Min(frames.Length - 1, center + 10); index++)
        {
            double left = frames[index][lower];
            double right = frames[index][upper];
            product += left * right;
            lowerEnergy += left * left;
            upperEnergy += right * right;
        }
        return product / Math.Max(1e-20, Math.Sqrt(lowerEnergy * upperEnergy));
    }

    private static int[][] FindPitchSpans(float[][] frames, CancellationToken cancellationToken)
    {
        int[][] spans = new int[128][];
        float maximum = frames.Max(frame => frame.Max());
        for (int note = LowestNote; note <= HighestNote; note++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            spans[note] = new int[frames.Length];
            float threshold = Math.Max(1e-6f, Math.Max(maximum * 0.01f, frames.Max(frame => frame[note]) * 0.1f));
            int start = -1;
            int last = -1;
            for (int index = 0; index <= frames.Length; index++)
            {
                if (index < frames.Length && frames[index][note] >= threshold)
                {
                    start = start < 0 ? index : start;
                    last = index;
                }
                else if (start >= 0 && (index == frames.Length || index - last > 3))
                {
                    int length = last - start + 1;
                    Array.Fill(spans[note], length, start, length);
                    start = -1;
                }
            }
        }
        return spans;
    }

    private static bool HaveDisjointStereoPositions(float lower, float upper) =>
        (lower < 0.1f && upper > 0.9f) || (lower > 0.9f && upper < 0.1f);

    private static void KeepStrongestCandidates(float[] strengths)
    {
        int[] ordered = Enumerable.Range(LowestNote, HighestNote - LowestNote + 1)
            .Where(note => strengths[note] > 0).OrderByDescending(note => strengths[note]).ToArray();
        for (int index = 6; index < ordered.Length; index++)
        {
            strengths[ordered[index]] = 0;
        }
    }

    private sealed class SpectrumWindow
    {
        private readonly Complex[] buffer;
        private readonly double[] taper;
        private readonly double[] power;
        private readonly double[] firstChannelPower;
        private readonly double[] magnitude;
        private readonly double taperSum;
        private int channels;

        internal SpectrumWindow(int size)
        {
            this.buffer = new Complex[size];
            this.taper = Enumerable.Range(0, size).Select(index => 0.5 - 0.5 * Math.Cos(2 * Math.PI * index / (size - 1))).ToArray();
            this.taperSum = this.taper.Sum();
            this.power = new double[size / 2 + 1];
            this.firstChannelPower = new double[this.power.Length];
            this.magnitude = new double[this.power.Length];
        }

        internal void Read(float[] data, int channels, int sampleCount, int center)
        {
            this.channels = channels;
            Array.Clear(this.power);
            Array.Clear(this.firstChannelPower);
            for (int channel = 0; channel < channels; channel++)
            {
                for (int index = 0; index < this.buffer.Length; index++)
                {
                    int source = center - this.buffer.Length / 2 + index;
                    float value = source >= 0 && source < sampleCount ? data[source * channels + channel] : 0;
                    this.buffer[index] = new Complex(float.IsFinite(value) ? value * this.taper[index] : 0, 0);
                }
                Fourier.Forward(this.buffer, FourierOptions.Matlab);
                for (int bin = 0; bin < this.power.Length; bin++)
                {
                    double energy = this.buffer[bin].Real * this.buffer[bin].Real + this.buffer[bin].Imaginary * this.buffer[bin].Imaginary;
                    this.power[bin] += energy;
                    if (channel == 0)
                    {
                        this.firstChannelPower[bin] = energy;
                    }
                }
            }
            for (int bin = 0; bin < this.power.Length; bin++)
            {
                this.magnitude[bin] = Math.Sqrt(this.power[bin] / channels);
            }
        }

        internal (float[] Strengths, float[] Balances) FindPeaks(int sampleRate)
        {
            float[] strengths = new float[128];
            float[] balances = new float[128];
            double maximum = this.magnitude.Skip(2).Max();
            int minimumBin = Math.Max(2, (int) (60.0 * this.buffer.Length / sampleRate));
            int maximumBin = Math.Min(this.power.Length - 2, (int) (2200.0 * this.buffer.Length / sampleRate));
            for (int bin = minimumBin; bin <= maximumBin; bin++)
            {
                double peak = this.magnitude[bin];
                if (peak < maximum * 0.02 || peak <= 1e-8 || peak <= this.magnitude[bin - 1]
                    || peak < this.magnitude[bin + 1] || peak < this.LocalFloor(bin) * 3.5)
                {
                    continue;
                }
                double frequency = (bin + this.PeakOffset(bin)) * sampleRate / this.buffer.Length;
                double pitch = 69 + 12 * Math.Log2(frequency / 440);
                int note = (int) Math.Round(pitch);
                if (note < LowestNote || note > HighestNote || Math.Abs(pitch - note) > 0.48)
                {
                    continue;
                }
                float strength = (float) (2 * peak / this.taperSum);
                if (strength > strengths[note])
                {
                    strengths[note] = strength;
                    balances[note] = this.channels == 1 ? 1f : (float) (this.firstChannelPower[bin] / Math.Max(1e-20, this.power[bin]));
                }
            }
            return (strengths, balances);
        }

        private double LocalFloor(int bin)
        {
            double total = 0;
            int count = 0;
            for (int index = Math.Max(1, bin - 12); index <= Math.Min(this.magnitude.Length - 1, bin + 12); index++)
            {
                if (Math.Abs(index - bin) > 2)
                {
                    total += this.magnitude[index];
                    count++;
                }
            }
            return total / Math.Max(1, count);
        }

        private double PeakOffset(int bin)
        {
            double left = Math.Log(Math.Max(1e-20, this.magnitude[bin - 1]));
            double center = Math.Log(Math.Max(1e-20, this.magnitude[bin]));
            double right = Math.Log(Math.Max(1e-20, this.magnitude[bin + 1]));
            double denominator = left - 2 * center + right;
            return Math.Abs(denominator) > 1e-12 ? Math.Clamp(0.5 * (left - right) / denominator, -0.5, 0.5) : 0;
        }
    }
}
