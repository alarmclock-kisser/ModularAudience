using NAudience.Core;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace ModularAudience.Audio.Processing
{
    internal static class AudioWindowProcessor
    {
        public static async Task<float[]> GetCurrentWindowAsync(AudioObj audio, int windowSize, int lookingRange, bool mono, bool lookBackwards)
        {
            if (audio.Data == null || audio.Data.Length == 0 || audio.SampleRate <= 0 || audio.Channels <= 0)
            {
                return Array.Empty<float>();
            }

            windowSize = Math.Max(1, windowSize);
            lookingRange = Math.Max(1, lookingRange);
            windowSize = (int) Math.Pow(2, Math.Ceiling(Math.Log(windowSize, 2)));

            long posFrames = audio.Position;
            int halfWindowFrames = (windowSize * lookingRange) / 2;
            int fullWindowFrames = halfWindowFrames * 2;
            if (fullWindowFrames <= 0)
            {
                return Array.Empty<float>();
            }

            if (mono)
            {
                float[] data = await AudioConversionProcessor.ConvertToMonoAsync(audio, set: false, maxWorkers: Environment.ProcessorCount);
                if (data.Length == 0)
                {
                    return Array.Empty<float>();
                }

                long startFrame = posFrames - (lookBackwards ? halfWindowFrames : 0);
                long endFrameExclusive = startFrame + fullWindowFrames;

                while (endFrameExclusive > data.Length)
                {
                    startFrame -= windowSize;
                    endFrameExclusive -= windowSize;
                }

                while (startFrame < 0)
                {
                    startFrame += windowSize;
                    endFrameExclusive += windowSize;
                }

                if (endFrameExclusive > data.LongLength)
                {
                    return Array.Empty<float>();
                }

                float[] current = new float[fullWindowFrames];
                await Task.Run(() => Array.Copy(data, (int) startFrame, current, 0, fullWindowFrames)).ConfigureAwait(false);
                return current;
            }
            else
            {
                float[] data = audio.Data;
                long startFloatIndex = (posFrames - (lookBackwards ? halfWindowFrames : 0)) * audio.Channels;
                long endFloatIndexExclusive = startFloatIndex + ((long) fullWindowFrames * audio.Channels);

                while (endFloatIndexExclusive > data.Length)
                {
                    startFloatIndex -= windowSize * audio.Channels;
                    endFloatIndexExclusive -= windowSize * audio.Channels;
                }

                while (startFloatIndex < 0)
                {
                    startFloatIndex += windowSize * audio.Channels;
                    endFloatIndexExclusive += windowSize * audio.Channels;
                }

                if (endFloatIndexExclusive > data.LongLength || startFloatIndex < 0)
                {
                    Debug.WriteLine("GetCurrentWindow: Out of bounds access prevented.");
                    return Array.Empty<float>();
                }

                int lengthFloats = fullWindowFrames * audio.Channels;
                float[] current = new float[lengthFloats];
                await Task.Run(() => Array.Copy(data, (int) startFloatIndex, current, 0, lengthFloats)).ConfigureAwait(false);
                return current;
            }
        }
    }
}
