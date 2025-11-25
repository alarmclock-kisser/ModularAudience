using ModularAudience.Audio;
using System;
using System.Threading.Tasks;

namespace ModularAudience.Audio.Processing
{
    internal static class AudioConversionProcessor
    {
        public static async Task<byte[]> GetBytesAsync(AudioObj audio, int maxWorkers)
        {
            maxWorkers = Math.Clamp(maxWorkers, 1, Environment.ProcessorCount);

            if (audio.Data == null || audio.Data.Length == 0 || audio.BitDepth <= 0)
            {
                return [];
            }

            int bytesPerSample = audio.BitDepth / 8;
            byte[] result = new byte[audio.Data.Length * bytesPerSample];

            await Task.Run(() =>
            {
                Parallel.For(0, audio.Data.Length, new ParallelOptions
                {
                    MaxDegreeOfParallelism = maxWorkers
                }, i =>
                {
                    float sample = audio.Data[i];
                    switch (audio.BitDepth)
                    {
                        case 8:
                            result[i] = (byte) (sample * 127f);
                            break;
                        case 16:
                            short sample16 = (short) (sample * short.MaxValue);
                            Span<byte> target16 = result.AsSpan(i * 2, 2);
                            BitConverter.TryWriteBytes(target16, sample16);
                            break;
                        case 24:
                            int sample24 = (int) (sample * 8_388_607f);
                            Span<byte> target24 = result.AsSpan(i * 3, 3);
                            target24[0] = (byte) sample24;
                            target24[1] = (byte) (sample24 >> 8);
                            target24[2] = (byte) (sample24 >> 16);
                            break;
                        case 32:
                            Span<byte> target32 = result.AsSpan(i * 4, 4);
                            BitConverter.TryWriteBytes(target32, sample);
                            break;
                    }
                });
            }).ConfigureAwait(false);

            return result;
        }

        public static async Task<float[]> ConvertToMonoAsync(AudioObj audio, bool set, int maxWorkers)
        {
            maxWorkers = Math.Clamp(maxWorkers, 1, Environment.ProcessorCount);

            if (audio.Data == null || audio.Data.Length == 0 || audio.Channels <= 0)
            {
                return [];
            }

            int monoSampleCount = audio.Data.Length / audio.Channels;
            float[] monoData = new float[monoSampleCount];

            await Task.Run(() =>
            {
                Parallel.For(0, monoSampleCount, new ParallelOptions
                {
                    MaxDegreeOfParallelism = maxWorkers
                }, i =>
                {
                    float sum = 0.0f;
                    for (int channel = 0; channel < audio.Channels; channel++)
                    {
                        sum += audio.Data[i * audio.Channels + channel];
                    }
                    monoData[i] = sum / audio.Channels;
                });
            }).ConfigureAwait(false);

            if (set)
            {
                audio.Data = monoData;
                audio.Channels = 1;
            }

            return monoData;
        }
    }
}
