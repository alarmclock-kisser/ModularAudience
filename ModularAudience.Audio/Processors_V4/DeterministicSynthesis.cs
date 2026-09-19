using MathNet.Numerics.IntegralTransforms;
using System.Numerics;

namespace ModularAudience.Audio.Processors_V4
{
    internal static class DeterministicSynthesis
    {
        internal static void AddBlock(DeterministicAudioSnapshot source, DeterministicSeparationSettings settings,
            DeterministicSpectrogram block, float[][][] masks, int[] selected, float[][] output,
            int firstFrame, int frameCount, double[] window, CancellationToken token)
        {
            long offset = (long) firstFrame * settings.HopSize - settings.WindowSize / 2;
            int length = checked((frameCount - 1) * settings.HopSize + settings.WindowSize);
            double[] normalization = Normalization(source, settings, offset, length, window, token);
            ParallelOptions options = new() { MaxDegreeOfParallelism = settings.Threads, CancellationToken = token };
            Parallel.For(0, selected.Length * source.Channels, options, worker =>
            {
                int stem = worker / source.Channels;
                int channel = worker % source.Channels;
                double[] accumulation = Accumulate(settings, block, masks, selected[stem], channel,
                    firstFrame, frameCount, length, window, token);
                Write(output[stem], source.Channels, channel, offset, accumulation, normalization, token);
            });
        }

        private static double[] Normalization(DeterministicAudioSnapshot source, DeterministicSeparationSettings settings,
            long offset, int length, double[] window, CancellationToken token)
        {
            double[] normalization = new double[length];
            int frames = DeterministicSpectrogram.FrameCount(source, settings);
            for (int index = 0; index < length; index++)
            {
                if ((index & 4095) == 0) token.ThrowIfCancellationRequested();
                long sample = offset + index;
                if (sample < 0 || sample >= source.Samples.Length / source.Channels) continue;
                int center = (int) (sample / settings.HopSize);
                for (int frame = Math.Max(0, center - 2); frame <= Math.Min(frames - 1, center + 2); frame++)
                {
                    long position = sample - (long) frame * settings.HopSize + settings.WindowSize / 2;
                    if (position >= 0 && position < window.Length)
                    {
                        normalization[index] += window[position] * window[position];
                    }
                }
            }
            return normalization;
        }

        private static double[] Accumulate(DeterministicSeparationSettings settings, DeterministicSpectrogram block,
            float[][][] masks, int sourceIndex, int channel, int firstFrame, int frameCount,
            int length, double[] window, CancellationToken token)
        {
            double[] accumulation = new double[length];
            Complex[] spectrum = new Complex[settings.WindowSize];
            for (int frame = 0; frame < frameCount; frame++)
            {
                token.ThrowIfCancellationRequested();
                int local = firstFrame + frame - block.FirstFrame;
                for (int bin = 0; bin < spectrum.Length; bin++)
                {
                    int positiveBin = bin <= spectrum.Length / 2 ? bin : spectrum.Length - bin;
                    spectrum[bin] = block.Spectra[local][channel][bin] * masks[local][sourceIndex][positiveBin];
                }
                Fourier.Inverse(spectrum, FourierOptions.Matlab);
                int start = frame * settings.HopSize;
                for (int index = 0; index < spectrum.Length; index++)
                {
                    accumulation[start + index] += spectrum[index].Real * window[index];
                }
            }
            return accumulation;
        }

        private static void Write(float[] output, int channels, int channel, long offset,
            double[] accumulation, double[] normalization, CancellationToken token)
        {
            for (int index = 0; index < accumulation.Length; index++)
            {
                if ((index & 4095) == 0) token.ThrowIfCancellationRequested();
                long frame = offset + index;
                if (frame < 0 || frame >= output.Length / channels) continue;
                if (normalization[index] <= 0) throw new ArithmeticException("Uncovered sample in overlap-add synthesis.");
                int sample = (int) (frame * channels + channel);
                output[sample] += (float) (accumulation[index] / normalization[index]);
                if (!float.IsFinite(output[sample])) throw new ArithmeticException("Non-finite overlap-add reconstruction.");
            }
        }
    }
}
