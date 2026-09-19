using System.Numerics;

namespace ModularAudience.Audio.Processors_V4
{
    internal static class CqtSliceRenderer
    {
        internal static void Render(DeterministicAudioSnapshot source, DeterministicSeparationSettings settings,
            DeterministicSourceModel model, int[] selected, float[][] output,
            IProgress<DeterministicSeparationProgress>? progress, CancellationToken token)
        {
            int sampleRate = source.SampleRate;
            int channels = source.Channels;
            long monoLen = source.Samples.LongLength / channels;

            ConstantQTransform cqt = ConstantQTransform.Create(sampleRate, settings.CqtBinsPerOctave,
                settings.CqtMinimumHz, token);
            int sliceLen = cqt.Length;
            int hop = sliceLen / 4;
            int numBands = cqt.Bands.Count;

            double[] win = CreateOuterHann(sliceLen);
            int normLen = (int)Math.Min(monoLen, int.MaxValue);
            double[] norm = new double[normLen];
            long total = (monoLen + hop - 1) / hop;

            float[][][] bMasks = BuildMasks(cqt, sampleRate, settings, model, selected, token);

            for (long fr = 0; fr * hop < monoLen; fr++)
            {
                token.ThrowIfCancellationRequested();
                progress?.Report(new(0.94 * fr / total, $"CQT slice {fr + 1}/{total}"));
                int st = (int)(fr * hop);
                int act = (int)Math.Min(sliceLen, monoLen - st);

                double[][] slices = new double[channels][];
                Complex[][][] coeff = new Complex[channels][][];
                for (int ch = 0; ch < channels; ch++)
                {
                    slices[ch] = new double[sliceLen];
                    for (int i = 0; i < act; i++)
                        slices[ch][i] = source.Samples[(st + i) * channels + ch];
                    coeff[ch] = cqt.Forward(slices[ch], token);
                }

                for (int src = 0; src < selected.Length; src++)
                {
                    for (int ch = 0; ch < channels; ch++)
                    {
                        ApplyMasks(coeff[ch], bMasks[src], numBands, token);
                        double[] rec = cqt.Inverse(coeff[ch], token);
                        for (int i = 0; i < sliceLen && st + i < normLen; i++)
                        {
                            int idx = (st + i) * channels + ch;
                            output[src][idx] += (float)(rec[i] * win[i]);
                            norm[st + i] += win[i] * win[i];
                        }
                        coeff[ch] = cqt.Forward(slices[ch], token);
                    }
                }
            }

            for (int src = 0; src < selected.Length; src++)
                for (int i = 0; i < normLen; i++)
                    if (norm[i] > 0)
                        for (int ch = 0; ch < channels; ch++)
                            output[src][i * channels + ch] /= (float)norm[i];

            progress?.Report(new(0.96, "CQT synthesis complete"));
        }

        private static double[] CreateOuterHann(int n)
        {
            double[] w = new double[n];
            for (int i = 0; i < n; i++)
                w[i] = Math.Cos(Math.PI * i / n) * Math.Cos(Math.PI * i / n);
            return w;
        }

        private static float[][][] BuildMasks(ConstantQTransform cqt, int sampleRate,
            DeterministicSeparationSettings settings, DeterministicSourceModel model,
            int[] selected, CancellationToken token)
        {
            int nb = cqt.Bands.Count;
            int np = (nb - 1) / 2;
            int bins = settings.WindowSize / 2 + 1;
            int rank = model.Dictionary[0].Length;
            float[][][] m = new float[selected.Length][][];
            for (int s = 0; s < selected.Length; s++)
            {
                m[s] = new float[nb][];
                for (int b = 0; b < nb; b++) m[s][b] = new float[cqt.Bands[b].CoefficientCount];
            }
            for (int b = 1; b <= np; b++)
            {
                if ((b & 15) == 0) token.ThrowIfCancellationRequested();
                int bin = Math.Clamp((int)Math.Round(Math.Abs(cqt.Bands[b].CenterHz) * cqt.Length / sampleRate), 0, bins - 1);
                double[] sp = new double[selected.Length];
                for (int r = 0; r < rank; r++)
                    for (int s = 0; s < selected.Length; s++) sp[s] += model.Dictionary[bin][r];
                double t = 0; for (int s = 0; s < selected.Length; s++) t += sp[s];
                if (t <= 0) continue;
                int c = cqt.Bands[b].CoefficientCount;
                for (int s = 0; s < selected.Length; s++)
                {
                    float v = (float)(sp[s] / t);
                    for (int i = 0; i < c; i++) { m[s][b][i] = v; m[s][nb - b][i] = v; }
                }
            }
            float u = selected.Length > 0 ? 1f / selected.Length : 0;
            for (int s = 0; s < selected.Length; s++)
            { m[s][0][0] = u; if (nb > 1) m[s][nb / 2][0] = u; }
            return m;
        }

        private static void ApplyMasks(Complex[][] c, float[][] m, int nb, CancellationToken token)
        {
            for (int b = 0; b < nb; b++)
            {
                if ((b & 31) == 0) token.ThrowIfCancellationRequested();
                for (int i = 0; i < c[b].Length; i++) c[b][i] *= m[b][i];
            }
        }
    }
}
