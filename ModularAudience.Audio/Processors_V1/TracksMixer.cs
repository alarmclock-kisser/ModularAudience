using System;
using System.Collections.Generic;
using System.Text;

namespace ModularAudience.Audio.Processors_V1
{
    public static class TracksMixer
    {


        public static async Task<AudioObj?> AggregateMixTracks(IEnumerable<AudioObj> tracks)
        {
            if (tracks == null)
            {
                return null;
            }

            var trackList = new List<AudioObj>();
            foreach (var t in tracks)
            {
                if (t == null)
                {
                    continue;
                }
                if (t.Data == null || t.Data.Length == 0)
                {
                    continue;
                }
                if (t.SampleRate <= 0)
                {
                    continue;
                }

                trackList.Add(t);
            }

            if (trackList.Count == 0)
            {
                return null;
            }

            int targetSampleRate = 0;
            int targetChannels = 1;

            for (int i = 0; i < trackList.Count; i++)
            {
                var tr = trackList[i];
                int sr = tr.AdjustedSampleRate > 0 ? tr.AdjustedSampleRate : tr.SampleRate;
                if (sr > targetSampleRate)
                {
                    targetSampleRate = sr;
                }

                int ch = tr.Channels > 0 ? tr.Channels : 1;
                if (ch > targetChannels)
                {
                    targetChannels = ch;
                }
            }

            if (targetSampleRate <= 0)
            {
                return null;
            }

            long maxFrames = 0;
            for (int i = 0; i < trackList.Count; i++)
            {
                var tr = trackList[i];
                int srcChannels = tr.Channels > 0 ? tr.Channels : 1;
                long frames = tr.Length / Math.Max(1, srcChannels);
                if (frames > maxFrames)
                {
                    maxFrames = frames;
                }
            }

            if (maxFrames <= 0)
            {
                return null;
            }

            long maxSamplesLong = maxFrames * Math.Max(1, targetChannels);
            if (maxSamplesLong > int.MaxValue)
            {
                return null;
            }

            int totalSamples = (int) maxSamplesLong;
            var partialBuffers = new float[trackList.Count][];

            var tasks = new System.Threading.Tasks.Task[trackList.Count];

            for (int i = 0; i < trackList.Count; i++)
            {
                int idx = i;
                var track = trackList[idx];

                tasks[idx] = Task.Run(() =>
                {
                    int srcChannels = track.Channels > 0 ? track.Channels : 1;
                    int srcRate = track.AdjustedSampleRate > 0 ? track.AdjustedSampleRate : track.SampleRate;
                    if (srcRate <= 0)
                    {
                        srcRate = targetSampleRate;
                    }

                    float[] srcData = track.Data ?? [];
                    long srcFrames = srcData.Length / Math.Max(1, srcChannels);

                    var local = new float[totalSamples];

                    if (srcFrames == 0)
                    {
                        partialBuffers[idx] = local;
                        return;
                    }

                    double rate = (double) srcRate / targetSampleRate;
                    if (rate <= 0.0)
                    {
                        rate = 1.0;
                    }

                    long maxDestFrames = maxFrames;
                    long fromSrc = (long) Math.Ceiling(srcFrames / rate);
                    if (fromSrc < maxDestFrames)
                    {
                        maxDestFrames = fromSrc;
                    }
                    if (maxDestFrames < 0)
                    {
                        maxDestFrames = 0;
                    }

                    float vol = track.Volume;
                    if (!(vol > 0f) || float.IsNaN(vol) || float.IsInfinity(vol))
                    {
                        vol = 1f;
                    }

                    for (long destFrame = 0; destFrame < maxDestFrames; destFrame++)
                    {
                        double srcPos = destFrame * rate;
                        long i0 = (long) srcPos;
                        if (i0 >= srcFrames)
                        {
                            break;
                        }

                        double frac = srcPos - i0;
                        long i1 = i0 + 1;
                        if (i1 >= srcFrames)
                        {
                            i1 = i0;
                            frac = 0.0;
                        }

                        for (int ch = 0; ch < targetChannels; ch++)
                        {
                            long outSampleIndex = destFrame * targetChannels + ch;
                            if (outSampleIndex >= totalSamples)
                            {
                                break;
                            }

                            int srcIndex0 = (int) (i0 * srcChannels + (ch % srcChannels));
                            int srcIndex1 = (int) (i1 * srcChannels + (ch % srcChannels));

                            if (srcIndex0 < 0 || srcIndex0 >= srcData.Length)
                            {
                                continue;
                            }
                            if (srcIndex1 < 0 || srcIndex1 >= srcData.Length)
                            {
                                srcIndex1 = srcIndex0;
                                frac = 0.0;
                            }

                            float s0 = srcData[srcIndex0];
                            float s1 = srcData[srcIndex1];
                            float sample = (float) (s0 + (s1 - s0) * frac);

                            local[outSampleIndex] += sample * vol;
                        }
                    }

                    partialBuffers[idx] = local;
                });
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);

            var mix = new float[totalSamples];

            Parallel.For(
                0,
                totalSamples,
                i =>
                {
                    float acc = 0f;
                    for (int t = 0; t < partialBuffers.Length; t++)
                    {
                        var buf = partialBuffers[t];
                        if (buf == null || i >= buf.Length)
                        {
                            continue;
                        }
                        acc += buf[i];
                    }

                    mix[i] = acc;
                });

            float peak = 0f;
            for (int i = 0; i < mix.Length; i++)
            {
                float v = mix[i];
                if (v < 0f)
                {
                    v = -v;
                }
                if (v > peak)
                {
                    peak = v;
                }
            }

            const float targetPeak = 0.95f;
            if (peak > 0f && peak > targetPeak)
            {
                float scale = targetPeak / peak;
                for (int i = 0; i < mix.Length; i++)
                {
                    mix[i] *= scale;
                }
            }

            float bpm = 0f;
            for (int i = 0; i < trackList.Count; i++)
            {
                var tr = trackList[i];
                float candidate = tr.Bpm > 0f ? tr.Bpm : tr.ScannedBpm;
                if (candidate > 0f)
                {
                    if (bpm <= 0f)
                    {
                        bpm = candidate;
                    }
                    else
                    {
                        if (Math.Abs(candidate - bpm) < 0.01f)
                        {
                            bpm = candidate;
                        }
                    }
                }
            }

            var first = trackList[0];

            var result = new AudioObj
            {
                Name = "Mix_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"),
                Data = mix,
                SampleRate = targetSampleRate,
                Channels = targetChannels,
                Duration = TimeSpan.FromSeconds(maxFrames / (double) targetSampleRate),
                Length = mix.Length,
                BitDepth = first.BitDepth > 0 ? first.BitDepth : 32,
                Bpm = bpm
            };

            return result;
        }



    }
}
