using ModularAudience.Audio;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ModularAudience.Audio.Processing
{
    internal static class AudioChunkProcessor
    {
        public static async Task<IEnumerable<float[]>> GetChunksAsync(AudioObj audio, int size, float overlap, bool keepData, int maxWorkers)
        {
            maxWorkers = Math.Clamp(maxWorkers, 1, Environment.ProcessorCount);

            if (audio.Data == null || audio.Data.Length == 0)
            {
                return [];
            }

            if (size <= 0 || overlap < 0 || overlap >= 1)
            {
                return [];
            }

            audio.ChunkSize = size;
            audio.OverlapSize = (int) (size * overlap);

            // Align overlap to channel count to keep L-R frame boundaries intact
            int ch = Math.Max(1, audio.Channels);
            if (ch > 1 && audio.OverlapSize % ch != 0)
            {
                audio.OverlapSize = (audio.OverlapSize / ch) * ch;
            }

            int step = size - audio.OverlapSize;
            if (step <= 0)
            {
                return [];
            }

            int numChunks = Math.Max(1, ((audio.Data.Length - size) / step) + 1);
            float[][] chunks = new float[numChunks][];

            await Task.Run(() =>
            {
                Parallel.For(0, numChunks, new ParallelOptions
                {
                    MaxDegreeOfParallelism = maxWorkers
                }, i =>
                {
                    int sourceOffset = i * step;
                    float[] chunk = new float[size];
                    Buffer.BlockCopy(
                        src: audio.Data,
                        srcOffset: sourceOffset * sizeof(float),
                        dst: chunk,
                        dstOffset: 0,
                        count: size * sizeof(float));
                    chunks[i] = chunk;
                });
            }).ConfigureAwait(false);

            if (!keepData)
            {
                audio.Data = [];
            }

            return chunks;
        }

        public static async Task AggregateStretchedChunksAsync(AudioObj audio, IEnumerable<float[]> chunks, double stretchFactor, int maxWorkers)
        {
            if (chunks == null)
            {
                return;
            }

            var chunkList = chunks.ToList();
            if (chunkList.Count == 0)
            {
                return;
            }

            maxWorkers = Math.Clamp(maxWorkers, 1, Environment.ProcessorCount);
            audio.StretchFactor = stretchFactor;

            int chunkSize = audio.ChunkSize;
            int overlapSize = audio.OverlapSize;
            int originalHopSize = chunkSize - overlapSize;
            int stretchedHopSize = (int) Math.Round(originalHopSize * stretchFactor);

            // Align hop to channel count to keep L-R frame boundaries intact
            int ch = Math.Max(1, audio.Channels);
            if (ch > 1 && stretchedHopSize % ch != 0)
            {
                stretchedHopSize = Math.Max(ch, (stretchedHopSize / ch) * ch);
            }

            int outputLength = Math.Max(chunkSize, (chunkList.Count - 1) * stretchedHopSize + chunkSize);

            double[] window = await Task.Run(() =>
                Enumerable.Range(0, chunkSize)
                    .Select(i => 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (chunkSize - 1))))
                    .ToArray()).ConfigureAwait(false);

            Debug.WriteLine($"[AggregateStretchedChunks] Chunks: {chunkList.Count}, ChunkSize: {chunkSize}, OutputLength: {outputLength}");

            double[] outputAccumulator = new double[outputLength];
            double[] weightSum = new double[outputLength];

            await Task.Run(() =>
            {
                var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = maxWorkers };

                Parallel.For(0, chunkList.Count, parallelOptions, chunkIndex =>
                {
                    var chunk = chunkList[chunkIndex];
                    int offset = chunkIndex * stretchedHopSize;

                    for (int j = 0; j < Math.Min(chunkSize, chunk.Length); j++)
                    {
                        int idx = offset + j;
                        if (idx >= outputLength)
                        {
                            break;
                        }

                        double windowedSample = chunk[j] * window[j];
                        Add(ref outputAccumulator[idx], windowedSample);
                        Add(ref weightSum[idx], window[j]);
                    }
                });

                float[] finalOutput = new float[outputLength];
                Parallel.For(0, outputLength, parallelOptions, i =>
                {
                    finalOutput[i] = weightSum[i] > 1e-6
                        ? (float) (outputAccumulator[i] / weightSum[i])
                        : 0.0f;
                });

                audio.Data = finalOutput;
            }).ConfigureAwait(false);

            Debug.WriteLine($"[AggregateStretchedChunks] Output Min: {audio.Data.Min()}, Max: {audio.Data.Max()}, First10: {string.Join(", ", audio.Data.Take(10))}");
        }
        private static void Add(ref double location, double value)
        {
            double initialValue;
            double computedValue;
            do
            {
                initialValue = location;
                computedValue = initialValue + value;
            }
            while (Interlocked.CompareExchange(ref location, computedValue, initialValue) != initialValue);
        }
    }
}
