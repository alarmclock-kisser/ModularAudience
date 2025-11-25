using ModularAudience.Audio;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace ModularAudience.Audio.Processing
{
    internal static class AudioAmplitudeProcessor
    {
        public static async Task NormalizeAsync(AudioObj audio, float maxAmplitude, int maxWorkers)
        {
            maxWorkers = Math.Clamp(maxWorkers, 1, Environment.ProcessorCount);

            if (audio.Data == null || audio.Data.Length == 0)
            {
                return;
            }

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = maxWorkers
            };

            Stopwatch sw = Stopwatch.StartNew();

            float globalMax = await Task.Run(() =>
            {
                float max = 0f;
                Parallel.For(0, audio.Data.Length, parallelOptions,
                    () => 0f,
                    (i, _, localMax) => Math.Max(Math.Abs(audio.Data[i]), localMax),
                    localMax => { lock (audio) { max = Math.Max(max, localMax); } });
                return max;
            }).ConfigureAwait(false);

            if (globalMax == 0f)
            {
                return;
            }

            float scale = maxAmplitude / globalMax;
            await Task.Run(() =>
            {
                Parallel.For(0, audio.Data.Length, parallelOptions, i =>
                {
                    audio.Data[i] *= scale;
                });
            }).ConfigureAwait(false);

            sw.Stop();
            audio["Normalize"] = sw.Elapsed.TotalMilliseconds;
        }
    }
}
