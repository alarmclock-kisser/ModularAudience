using MathNet.Numerics.IntegralTransforms;
using System.Numerics;

namespace ModularAudience.Audio.Processors_V4
{
    internal sealed class DeterministicSpectrogram
    {
        private DeterministicSpectrogram(int firstFrame, int frames, int bins, bool keepSpectra)
        {
            this.FirstFrame = firstFrame;
            this.Power = Allocate(frames, bins);
            this.Pan = Allocate(frames, bins);
            this.Harmonic = Allocate(frames, bins);
            this.Percussive = Allocate(frames, bins);
            this.Spectra = keepSpectra ? new Complex[frames][][] : [];
        }

        internal int FirstFrame { get; }
        internal int Frames => this.Power.Length;
        internal int Bins => this.Power[0].Length;
        internal double[][] Power { get; }
        internal double[][] Pan { get; }
        internal double[][] Harmonic { get; }
        internal double[][] Percussive { get; }
        internal Complex[][][] Spectra { get; }

        internal static int FrameCount(DeterministicAudioSnapshot source, DeterministicSeparationSettings settings)
            => checked(source.Samples.Length / source.Channels / settings.HopSize + 1);

        internal static double[] CreateWindow(int size)
        {
            double[] window = new double[size];
            for (int i = 0; i < size; i++)
            {
                window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / size);
            }
            return window;
        }

        internal static DeterministicSpectrogram Read(DeterministicAudioSnapshot source,
            DeterministicSeparationSettings settings, int firstFrame, int frameCount,
            bool keepSpectra, CancellationToken cancellationToken)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(firstFrame);
            ArgumentOutOfRangeException.ThrowIfLessThan(frameCount, 1);
            DeterministicSpectrogram block = new(firstFrame, frameCount, settings.WindowSize / 2 + 1, keepSpectra);
            double[] window = CreateWindow(settings.WindowSize);
            ParallelOptions options = new() { MaxDegreeOfParallelism = settings.Threads, CancellationToken = cancellationToken };
            Parallel.For(0, frameCount, options, frame => block.ReadFrame(source, settings, frame, window, cancellationToken));
            block.SplitHarmonicPercussive(settings, options);
            return block;
        }

        private void ReadFrame(DeterministicAudioSnapshot source, DeterministicSeparationSettings settings,
            int frame, double[] window, CancellationToken cancellationToken)
        {
            int offset = checked((this.FirstFrame + frame) * settings.HopSize - settings.WindowSize / 2);
            Complex[][] channels = new Complex[source.Channels][];
            for (int channel = 0; channel < source.Channels; channel++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                channels[channel] = Transform(source, channel, offset, window);
                for (int bin = 0; bin < this.Bins; bin++)
                {
                    Complex value = channels[channel][bin];
                    double power = value.Real * value.Real + value.Imaginary * value.Imaginary;
                    this.Power[frame][bin] += power / source.Channels;
                    if (source.Channels == 2)
                    {
                        this.Pan[frame][bin] += channel == 0 ? -power : power;
                    }
                }
            }
            for (int bin = 0; bin < this.Bins; bin++)
            {
                this.Pan[frame][bin] = this.Power[frame][bin] > 0
                    ? this.Pan[frame][bin] / (source.Channels * this.Power[frame][bin]) : 0;
            }
            if (this.Spectra.Length > 0)
            {
                this.Spectra[frame] = channels;
            }
        }

        private static Complex[] Transform(DeterministicAudioSnapshot source, int channel, int offset, double[] window)
        {
            Complex[] spectrum = new Complex[window.Length];
            int sampleFrames = source.Samples.Length / source.Channels;
            for (int i = 0; i < window.Length; i++)
            {
                int index = offset + i;
                if (index >= 0 && index < sampleFrames)
                {
                    spectrum[i] = new Complex(source.Samples[index * source.Channels + channel] * window[i], 0);
                }
            }
            Fourier.Forward(spectrum, FourierOptions.Matlab);
            return spectrum;
        }

        private void SplitHarmonicPercussive(DeterministicSeparationSettings settings, ParallelOptions options)
        {
            Parallel.For(0, this.Frames, options, frame =>
            {
                double[] temporal = new double[settings.MedianFrames];
                double[] spectral = new double[settings.MedianBins];
                for (int bin = 0; bin < this.Bins; bin++)
                {
                    if ((bin & 127) == 0)
                    {
                        options.CancellationToken.ThrowIfCancellationRequested();
                    }
                    for (int i = 0; i < temporal.Length; i++)
                    {
                        temporal[i] = this.Power[Math.Clamp(frame + i - temporal.Length / 2, 0, this.Frames - 1)][bin];
                    }
                    for (int i = 0; i < spectral.Length; i++)
                    {
                        spectral[i] = this.Power[frame][Math.Clamp(bin + i - spectral.Length / 2, 0, this.Bins - 1)];
                    }
                    Array.Sort(temporal);
                    Array.Sort(spectral);
                    double h = temporal[temporal.Length / 2];
                    double p = spectral[spectral.Length / 2];
                    double margin = settings.SeparationMargin * settings.SeparationMargin;
                    this.Harmonic[frame][bin] = h > 0 ? h / (h + margin * p) : 0;
                    this.Percussive[frame][bin] = p > 0 ? p / (p + margin * h) : 0;
                }
            });
        }

        internal static double[][] Allocate(int rows, int columns)
        {
            double[][] result = new double[rows][];
            for (int row = 0; row < rows; row++)
            {
                result[row] = new double[columns];
            }
            return result;
        }
    }
}
