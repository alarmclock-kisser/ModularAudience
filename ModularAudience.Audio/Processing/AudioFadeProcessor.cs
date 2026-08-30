using Microsoft.VisualBasic.Devices;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace ModularAudience.Audio.Processing
{
    public static class AudioFadeProcessor
    {
        public static async Task FadeInAsync(AudioObj audio, float targetAmplitude = 0.0f)
        {
            // Clamp target
            targetAmplitude = Math.Clamp(targetAmplitude, 0f, 1f);

            // Work on background thread to keep UI responsive
            await Task.Run(() =>
            {
                float[] data = audio.Data ?? [];
                if (data.Length == 0)
                {
                    return;
                }

                int channels = audio.Channels > 0 ? audio.Channels : 1;
                int totalFrames = data.Length / channels;
                if (totalFrames == 0)
                {
                    return;
                }

                long selStartLong = audio.SelectionStart;
                long selEndLong = audio.SelectionEnd;

                int startFrame, endFrame;
                if (selEndLong > selStartLong && selStartLong >= 0)
                {
                    // Selection stored in SAMPLES; convert to FRAMES by dividing by channels
                    startFrame = (int) Math.Max(0, Math.Min((selStartLong / channels), totalFrames - 1));
                    endFrame = (int) Math.Max(0, Math.Min((selEndLong / channels), totalFrames - 1));
                    // ensure sensible order
                    if (endFrame < startFrame)
                    {
                        (startFrame, endFrame) = (endFrame, startFrame);
                    }
                }
                else
                {
                    startFrame = 0;
                    endFrame = totalFrames - 1;
                }

                int lengthFrames = Math.Max(1, endFrame - startFrame + 1);

                for (int f = 0; f < lengthFrames; f++)
                {
                    int frameIndex = startFrame + f;
                    float t = lengthFrames == 1 ? 1f : (float) f / (float) (lengthFrames - 1); // 0..1
                    float scale = targetAmplitude + (1f - targetAmplitude) * t; // fade-in from target -> 1.0

                    int baseIdx = frameIndex * channels;
                    // apply to all channels
                    for (int c = 0; c < channels; c++)
                    {
                        int idx = baseIdx + c;
                        if (idx >= 0 && idx < data.Length)
                        {
                            data[idx] = data[idx] * scale;
                        }
                    }
                }
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Fade the given audio selection (or whole audio if no selection) from full level (1.0) down to <paramref name="targetAmplitude"/>.
        /// targetAmplitude = 0.0f  -> fades out to silence.
        /// </summary>
        public static async Task FadeOutAsync(AudioObj audio, float targetAmplitude = 0.0f)
        {
            // Clamp target
            targetAmplitude = Math.Clamp(targetAmplitude, 0f, 1f);

            // Work on background thread to keep UI responsive
            await Task.Run(() =>
            {
                float[] data = audio.Data ?? [];
                if (data.Length == 0)
                {
                    return;
                }

                int channels = audio.Channels > 0 ? audio.Channels : 1;
                int totalFrames = data.Length / channels;
                if (totalFrames == 0)
                {
                    return;
                }

                long selStartLong = audio.SelectionStart;
                long selEndLong = audio.SelectionEnd;

                int startFrame, endFrame;
                if (selEndLong > selStartLong && selStartLong >= 0)
                {
                    // Selection stored in SAMPLES; convert to FRAMES by dividing by channels
                    startFrame = (int) Math.Max(0, Math.Min((selStartLong / channels), totalFrames - 1));
                    endFrame = (int) Math.Max(0, Math.Min((selEndLong / channels), totalFrames - 1));
                    if (endFrame < startFrame)
                    {
                        (startFrame, endFrame) = (endFrame, startFrame);
                    }
                }
                else
                {
                    startFrame = 0;
                    endFrame = totalFrames - 1;
                }

                int lengthFrames = Math.Max(1, endFrame - startFrame + 1);

                for (int f = 0; f < lengthFrames; f++)
                {
                    int frameIndex = startFrame + f;
                    float t = lengthFrames == 1 ? 1f : (float) f / (float) (lengthFrames - 1); // 0..1
                                                                                               // fade-out from 1.0 -> targetAmplitude
                    float scale = 1f - (1f - targetAmplitude) * t;

                    int baseIdx = frameIndex * channels;
                    for (int c = 0; c < channels; c++)
                    {
                        int idx = baseIdx + c;
                        if (idx >= 0 && idx < data.Length)
                        {
                            data[idx] = data[idx] * scale;
                        }
                    }
                }
            }).ConfigureAwait(false);
        }
    }
}