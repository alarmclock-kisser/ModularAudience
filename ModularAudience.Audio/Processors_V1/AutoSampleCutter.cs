using ModularAudience.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ModularAudience.Audio.Processors_V1
{
    public static class AutoSampleCutter
    {
        public static Task<IReadOnlyList<AudioObj>> CutAutoSamplesAsync(
            AudioObj audio,
            int minDurationMs,
            int maxDurationMs,
            int silenceDurationMs,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (audio == null)
            {
                throw new ArgumentNullException(nameof(audio));
            }

            if (audio.Data == null || audio.Data.Length == 0)
            {
                return Task.FromResult<IReadOnlyList<AudioObj>>([]);
            }

            minDurationMs = Math.Max(10, minDurationMs);
            maxDurationMs = Math.Max(minDurationMs, maxDurationMs);
            silenceDurationMs = Math.Max(10, silenceDurationMs);

            return Task.Run(() => ExtractInternal(audio, minDurationMs, maxDurationMs, silenceDurationMs, progress, cancellationToken), cancellationToken);
        }

        private static IReadOnlyList<AudioObj> ExtractInternal(
            AudioObj audio,
            int minDurationMs,
            int maxDurationMs,
            int silenceDurationMs,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int channels = Math.Max(1, audio.Channels);
            float[] data = audio.Data ?? [];
            long totalFrames = data.LongLength / channels;
            if (totalFrames <= 0)
            {
                return [];
            }

            double framesPerMs = Math.Max(1d, audio.SampleRate / 1000d);
            int minFrames = (int) Math.Max(1, Math.Round(framesPerMs * minDurationMs));
            int maxFrames = (int) Math.Max(minFrames, Math.Round(framesPerMs * maxDurationMs));
            int silenceFrames = (int) Math.Max(1, Math.Round(framesPerMs * silenceDurationMs));

            int analysisWindowFrames = Math.Max((int) Math.Round(framesPerMs * 5), 32); // ~5ms window for envelope
            int blockCount = (int) Math.Ceiling(totalFrames / (double) analysisWindowFrames);
            float[] envelope = new float[blockCount];

            System.Threading.Tasks.Parallel.For(0, blockCount, new ParallelOptions { CancellationToken = cancellationToken }, blockIndex =>
            {
                long frameStart = (long) blockIndex * analysisWindowFrames;
                long frameEnd = Math.Min(totalFrames, frameStart + analysisWindowFrames);
                if (frameStart >= frameEnd)
                {
                    return;
                }

                float maxAmplitude = 0f;
                for (long frame = frameStart; frame < frameEnd; frame++)
                {
                    long baseIdx = frame * channels;
                    for (int ch = 0; ch < channels; ch++)
                    {
                        float sample = Math.Abs(data[baseIdx + ch]);
                        if (sample > maxAmplitude)
                        {
                            maxAmplitude = sample;
                        }
                    }
                }
                envelope[blockIndex] = maxAmplitude;
            });

            progress?.Report(0.2);

            float[] nonZeroEnvelope = envelope.Where(v => v > 0f).DefaultIfEmpty(0.0001f).ToArray();
            float peak = nonZeroEnvelope.Max();
            float percentile = Percentile(nonZeroEnvelope, 0.75f);
            float median = Percentile(nonZeroEnvelope, 0.5f);
            float threshold = MathF.Max(peak * 0.12f, MathF.Max(median * 1.25f, percentile * 0.85f));
            threshold = MathF.Min(threshold, peak * 0.9f);
            if (threshold <= 0f)
            {
                threshold = 0.01f;
            }

            int silenceBlocks = Math.Max(1, silenceFrames / analysisWindowFrames);
            var segments = new List<(int StartFrame, int EndFrame)>();
            bool inside = false;
            int startBlock = 0;
            int trailingSilence = 0;

            for (int block = 0; block < blockCount; block++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                float value = envelope[block];
                bool loud = value >= threshold;
                if (loud)
                {
                    if (!inside)
                    {
                        inside = true;
                        startBlock = block;
                    }
                    trailingSilence = 0;
                }
                else if (inside)
                {
                    trailingSilence++;
                    if (trailingSilence >= silenceBlocks)
                    {
                        int endBlockExclusive = block - trailingSilence;
                        AppendSegment(segments, startBlock, endBlockExclusive, analysisWindowFrames, (int) totalFrames, minFrames, maxFrames);
                        inside = false;
                        trailingSilence = 0;
                    }
                }

                if (progress != null)
                {
                    progress.Report(0.2 + 0.3 * (block + 1) / blockCount);
                }
            }

            if (inside)
            {
                AppendSegment(segments, startBlock, blockCount - 1, analysisWindowFrames, (int) totalFrames, minFrames, maxFrames);
            }

            if (segments.Count == 0)
            {
                return [];
            }

            AudioObj[] clips = new AudioObj[segments.Count];
            System.Threading.Tasks.Parallel.For(0, segments.Count, new ParallelOptions { CancellationToken = cancellationToken }, i =>
            {
                var segment = segments[i];
                int frameLength = segment.EndFrame - segment.StartFrame;
                if (frameLength <= 0)
                {
                    return;
                }

                int sampleLength = frameLength * channels;
                float[] clipData = new float[sampleLength];
                Buffer.BlockCopy(
                    src: data,
                    srcOffset: segment.StartFrame * channels * sizeof(float),
                    dst: clipData,
                    dstOffset: 0,
                    count: sampleLength * sizeof(float));

                var clip = new AudioObj
                {
                    Name = $"{audio.Name}_auto_{i + 1:D3}",
                    Data = clipData,
                    SampleRate = audio.SampleRate,
                    Channels = audio.Channels,
                    BitDepth = audio.BitDepth,
                    Bpm = audio.Bpm,
                    Timing = audio.Timing,
                    Volume = audio.Volume,
                    Length = clipData.LongLength,
                    Duration = TimeSpan.FromSeconds(frameLength / (double) audio.SampleRate)
                };

                clips[i] = clip;
                if (progress != null)
                {
                    progress.Report(0.5 + 0.5 * (i + 1) / segments.Count);
                }
            });

            return clips.Where(c => c != null).ToArray()!;
        }

        private static void AppendSegment(
            List<(int StartFrame, int EndFrame)> segments,
            int startBlock,
            int endBlockExclusive,
            int windowFrames,
            int totalFrames,
            int minFrames,
            int maxFrames)
        {
            if (endBlockExclusive <= startBlock)
            {
                return;
            }

            int segmentStart = startBlock * windowFrames;
            int segmentEnd = Math.Min(totalFrames, (endBlockExclusive + 1) * windowFrames);
            int duration = segmentEnd - segmentStart;
            if (duration < minFrames)
            {
                return;
            }

            int remaining = duration;
            int cursor = segmentStart;
            while (remaining > 0)
            {
                int take = Math.Min(remaining, maxFrames);
                if (take < minFrames)
                {
                    // Attach residual to previous segment if possible
                    if (segments.Count > 0)
                    {
                        var last = segments[^1];
                        segments[^1] = (last.StartFrame, last.EndFrame + remaining);
                    }
                    break;
                }

                segments.Add((cursor, cursor + take));
                cursor += take;
                remaining -= take;
            }
        }

        private static float Percentile(IReadOnlyList<float> values, double percentile)
        {
            if (values.Count == 0)
            {
                return 0f;
            }

            float[] buffer = values.ToArray();
            Array.Sort(buffer);
            double index = Math.Clamp(percentile, 0d, 1d) * (buffer.Length - 1);
            int lower = (int) Math.Floor(index);
            int upper = (int) Math.Ceiling(index);
            if (lower == upper)
            {
                return buffer[lower];
            }

            float fraction = (float) (index - lower);
            return buffer[lower] + fraction * (buffer[upper] - buffer[lower]);
        }



    }
}
