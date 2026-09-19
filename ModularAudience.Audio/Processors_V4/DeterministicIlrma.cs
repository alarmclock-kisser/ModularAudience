using MathNet.Numerics.IntegralTransforms;
using System.Numerics;

namespace ModularAudience.Audio.Processors_V4
{
    /// <summary>
    /// Orchestrates stereo ILRMA training and projection-back rendering.
    /// Requires exactly two channels and at most two source profiles.
    /// ILRMA is incompatible with CQT synthesis.
    /// </summary>
    internal static class DeterministicIlrma
    {
        internal static void Render(DeterministicAudioSnapshot source, DeterministicSeparationSettings settings,
            int[] selected, float[][] output, IProgress<DeterministicSeparationProgress>? progress,
            CancellationToken token)
        {
            if (source.Channels != 2)
                throw new ArgumentException("ILRMA requires exactly two input channels.", nameof(source));
            if (selected.Length > 2)
                throw new ArgumentException("ILRMA supports at most two output sources.", nameof(selected));

            int winSize = settings.WindowSize;
            int hopSize = settings.HopSize;
            int bins = winSize / 2 + 1;
            long monoLen = source.Samples.LongLength / 2;
            int totalFrames = DeterministicSpectrogram.FrameCount(source, settings);
            double[] window = DeterministicSpectrogram.CreateWindow(winSize);

            // Step 1: Train ILRMA spatial model on representative frames.
            IlrmaTrainingData data = IlrmaTrainingData.Read(source, settings, progress, token);
            if (data.IsSilent) return;

            progress?.Report(new(0.15, "Training ILRMA spatial model"));
            IlrmaMatrix[] demixing = IlrmaSpatialModel.Train(data, source.SampleRate, settings, progress, token);

            // Step 2: Build normalization accumulator for OLA.
            int normLen = (int)Math.Min(monoLen, int.MaxValue);
            double[] norm = new double[normLen];
            for (int s = 0; s < normLen; s++)
            {
                if ((s & 4095) == 0) token.ThrowIfCancellationRequested();
                int center = s / hopSize;
                for (int fr = Math.Max(0, center - 2); fr <= Math.Min(center + 2, totalFrames - 1); fr++)
                {
                    long pos = s - (long)fr * hopSize + winSize / 2;
                    if (pos >= 0 && pos < winSize) norm[s] += window[(int)pos] * window[(int)pos];
                }
            }

            // Step 3: Render per source — STFT, demix, IFFT, OLA.
            for (int src = 0; src < selected.Length; src++)
            {
                token.ThrowIfCancellationRequested();
                progress?.Report(new(0.15 + 0.75 * src / selected.Length,
                    $"ILRMA rendering source {src + 1}/{selected.Length}"));

                float[] buf = output[src];
                Complex[] lSpec = new Complex[winSize];
                Complex[] rSpec = new Complex[winSize];
                Complex[] oSpec = new Complex[winSize];
                double[] lBuf = new double[winSize];
                double[] rBuf = new double[winSize];

                for (int fr = 0; fr < totalFrames; fr++)
                {
                    token.ThrowIfCancellationRequested();
                    long off = (long)fr * hopSize - winSize / 2;

                    // Read and window samples for both channels.
                    for (int i = 0; i < winSize; i++)
                    {
                        long s = off + i;
                        lBuf[i] = (s >= 0 && s < monoLen) ? source.Samples[2 * (int)s] * window[i] : 0;
                        rBuf[i] = (s >= 0 && s < monoLen) ? source.Samples[2 * (int)s + 1] * window[i] : 0;
                    }

                    // Forward FFT per channel.
                    for (int i = 0; i < winSize; i++)
                    {
                        lSpec[i] = new Complex(lBuf[i], 0);
                        rSpec[i] = new Complex(rBuf[i], 0);
                    }
                    IlrmaNumerics.Transform(lSpec, false, token);
                    IlrmaNumerics.Transform(rSpec, false, token);

                    // Apply demixing per bin to extract this source.
                    for (int bin = 0; bin < bins; bin++)
                    {
                        int dBin = Math.Min(bin, demixing.Length - 1);
                        oSpec[bin] = demixing[dBin].Apply(src, lSpec[bin], rSpec[bin]);
                    }
                    // Conjugate symmetry for negative frequencies.
                    for (int bin = bins; bin < winSize; bin++)
                        oSpec[bin] = Complex.Conjugate(oSpec[winSize - bin]);

                    // Inverse FFT.
                    IlrmaNumerics.Transform(oSpec, true, token);

                    // OLA accumulate (stereo output, both channels same).
                    for (int i = 0; i < winSize; i++)
                    {
                        int idx = (int)(off + i);
                        if (idx < 0 || idx * 2 + 1 >= buf.Length) continue;
                        double v = oSpec[i].Real * window[i];
                        buf[idx * 2] += (float)v;
                        buf[idx * 2 + 1] += (float)v;
                    }
                }
            }

            // Normalize by accumulated window squares.
            for (int src = 0; src < selected.Length; src++)
            {
                float[] buf = output[src];
                for (int s = 0; s < normLen; s++)
                {
                    if (norm[s] > 0)
                    {
                        float inv = 1f / (float)norm[s];
                        buf[s * 2] *= inv;
                        buf[s * 2 + 1] *= inv;
                    }
                }
            }

            progress?.Report(new(0.96, "ILRMA rendering complete"));
        }
    }
}
