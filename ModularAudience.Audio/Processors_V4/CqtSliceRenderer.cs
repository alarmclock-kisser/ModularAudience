using System.Numerics;

namespace ModularAudience.Audio.Processors_V4
{
    internal static class CqtSliceRenderer
    {
        internal static void Render(DeterministicAudioSnapshot source, DeterministicSeparationSettings settings,
            DeterministicSourceModel model, int[] selected, float[][] output,
            IProgress<DeterministicSeparationProgress>? progress, CancellationToken token)
        {
            progress?.Report(new(0.01, "Preparing CQT synthesis"));
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
            int workers = ResolveWorkerCount(cqt, channels, settings.Threads);
            ParallelOptions options = new()
            {
                CancellationToken = token,
                MaxDegreeOfParallelism = workers
            };
            long completedSlices = 0;
            progress?.Report(new(0.04, $"CQT synthesis prepared; processing {total} slices with {workers} worker(s)"));

            // With a hop of one quarter window, slices four positions apart do not overlap.
            // Each phase can therefore write directly into output and norm without synchronization.
            for (int phase = 0; phase < 4; phase++)
            {
                long firstSlice = phase;
                if (firstSlice >= total) break;
                long phaseSlices = (total - firstSlice + 3) / 4;
                long completedPhaseSlices = 0;
                long progressStep = Math.Max(1, phaseSlices / 100);
                Parallel.For(0L, phaseSlices, options, index =>
                {
                    RenderSlice(firstSlice + index * 4, source, cqt, bMasks, numBands, selected,
                        output, norm, win, sliceLen, hop, channels, monoLen, normLen, token);
                    long completed = Interlocked.Increment(ref completedPhaseSlices);
                    if (completed == phaseSlices || completed % progressStep == 0)
                    {
                        progress?.Report(new(0.04 + 0.90 * (completedSlices + completed) / total,
                            $"CQT synthesis {completedSlices + completed}/{total} slices"));
                    }
                });
                completedSlices += phaseSlices;
            }

            for (int src = 0; src < selected.Length; src++)
                for (int i = 0; i < normLen; i++)
                    if (norm[i] > 0)
                        for (int ch = 0; ch < channels; ch++)
                            output[src][i * channels + ch] /= (float)norm[i];

            progress?.Report(new(0.96, "CQT synthesis complete"));
        }

        private static int ResolveWorkerCount(ConstantQTransform cqt, int channels, int requestedThreads)
        {
            long coefficientCount = cqt.Bands.Sum(band => (long) band.CoefficientCount);
            long bytesPerWorker = checked(channels * (48L * cqt.Length + 32L * coefficientCount) + 1048576);
            long available = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            long allocated = GC.GetTotalMemory(false);
            long budget = available > 0
                ? Math.Max(64L * 1024 * 1024, (available - allocated) / 2)
                : 512L * 1024 * 1024;
            int memoryWorkers = Math.Max(1, (int) Math.Min(int.MaxValue, budget / Math.Max(1, bytesPerWorker)));
            return Math.Max(1, Math.Min(requestedThreads, Math.Min(Environment.ProcessorCount, memoryWorkers)));
        }

        private static void RenderSlice(long frame, DeterministicAudioSnapshot source, ConstantQTransform cqt,
            float[][][] masks, int numBands, int[] selected, float[][] output, double[] norm, double[] window,
            int sliceLength, int hop, int channels, long monoLength, int normLength, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            int start = checked((int)(frame * hop));
            int active = (int)Math.Min(sliceLength, monoLength - start);
            double[][] slices = new double[channels][];
            Complex[][][] coefficients = new Complex[channels][][];
            for (int channel = 0; channel < channels; channel++)
            {
                slices[channel] = new double[sliceLength];
                for (int sample = 0; sample < active; sample++)
                    slices[channel][sample] = source.Samples[(start + sample) * channels + channel];
                coefficients[channel] = cqt.Forward(slices[channel], token);
            }

            for (int sample = 0; sample < sliceLength && start + sample < normLength; sample++)
            {
                norm[start + sample] += window[sample] * window[sample];
            }

            for (int sourceIndex = 0; sourceIndex < selected.Length; sourceIndex++)
            {
                for (int channel = 0; channel < channels; channel++)
                {
                    ApplyMasks(coefficients[channel], masks[sourceIndex], numBands, token);
                    double[] reconstructed = cqt.Inverse(coefficients[channel], token);
                    for (int sample = 0; sample < sliceLength && start + sample < normLength; sample++)
                    {
                        int outputIndex = (start + sample) * channels + channel;
                        output[sourceIndex][outputIndex] += (float)(reconstructed[sample] * window[sample]);
                    }
                    coefficients[channel] = cqt.Forward(slices[channel], token);
                }
            }
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
