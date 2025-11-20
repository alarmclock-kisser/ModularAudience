using NAudience.Core;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ModularAudience.Audio.Processing
{
    internal static class AudioSilenceProcessor
    {
        public static async Task<(long StartIndex, long EndIndex)> TrimSilenceAsync(AudioObj audio, float? threshold, int maxWorkers)
        {
            if (audio.Data == null || audio.Data.Length == 0)
            {
                return (0, 0);
            }

            maxWorkers = Math.Clamp(maxWorkers, 1, Environment.ProcessorCount);

            int blockSize = (int) (audio.SampleRate * audio.Channels * 0.01);
            if (blockSize == 0)
            {
                blockSize = Math.Max(1, audio.Channels);
            }

            int numBlocks = (int) Math.Ceiling((double) audio.Data.Length / blockSize);
            float[] rmsBlocks = new float[numBlocks];

            await Task.Run(() =>
            {
                Parallel.For(0, numBlocks, new ParallelOptions { MaxDegreeOfParallelism = maxWorkers }, i =>
                {
                    long start = (long) i * blockSize;
                    long end = Math.Min(start + blockSize, audio.Data.LongLength);

                    double sumOfSquares = 0.0;
                    long count = end - start;
                    for (long s = start; s < end; s++)
                    {
                        sumOfSquares += audio.Data[s] * audio.Data[s];
                    }

                    rmsBlocks[i] = count > 0 ? (float) Math.Sqrt(sumOfSquares / count) : 0.0f;
                });
            }).ConfigureAwait(false);

            float maxRms = rmsBlocks.Any() ? rmsBlocks.Max() : 0.0f;
            float finalThreshold = threshold ?? (maxRms * 0.01f);

            int startBlock = -1;
            for (int i = 0; i < rmsBlocks.Length; i++)
            {
                if (rmsBlocks[i] > finalThreshold)
                {
                    startBlock = i;
                    break;
                }
            }

            int endBlock = -1;
            for (int i = rmsBlocks.Length - 1; i >= 0; i--)
            {
                if (rmsBlocks[i] > finalThreshold)
                {
                    endBlock = i;
                    break;
                }
            }

            if (startBlock == -1 || endBlock == -1 || startBlock >= endBlock)
            {
                return (0, audio.Data.LongLength);
            }

            long startIndex = (long) startBlock * blockSize;
            long endIndex = Math.Min((long) (endBlock + 1) * blockSize, audio.Data.LongLength);
            return (startIndex, endIndex);
        }
    }
}
