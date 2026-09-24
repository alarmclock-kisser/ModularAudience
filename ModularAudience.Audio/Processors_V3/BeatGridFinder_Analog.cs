using System;
using System.Linq;
using System.Threading.Tasks;
using ModularAudience.Audio.Processors_V2;

namespace ModularAudience.Audio.Processors_V3
{
    /// <summary>
    /// Simple, rigid beat grid generator. Virtually trims silence at start/end,
    /// then draws a rigid 4th-note grid based on the configured BPM (or ScannedBpm fallback).
    /// The onset (leading edge) of the strongest audio attack anchors the phase; the fixed
    /// interval is propagated in both directions from that single anchor, with no further analysis.
    /// </summary>
    public static class BeatGridFinder_Analog
    {
        private const double MinimumBpm = 30.0;
        private const double MaximumBpm = 300.0;

        public static async Task<bool[]> GenerateBeatGridAsync(AudioObj audio, bool set = true, int granularity = 4)
        {
            if (audio == null || audio.Data == null || audio.Data.Length == 0 || audio.SampleRate <= 0)
            {
                return [];
            }

            VirtualTrim trim = await FindVirtualTrimAsync(audio).ConfigureAwait(false);

            return await Task.Run(() =>
            {
                bool[] grid = GenerateBeatGrid(audio, trim, granularity, out double bpm, out string bpmSource);
                if (set)
                {
                    audio.BeatGrid = grid;
                }

                int beatCount = grid.Count(value => value);
                double trimStartSeconds = trim.StartFrame / (double)audio.SampleRate;
                double trimEndSeconds = trim.EndFrame / (double)audio.SampleRate;
                LogCollection.Log($"Beat grid Analog finished. Beats: {beatCount}, trim={trimStartSeconds:F2}-{trimEndSeconds:F2}s, bpm={bpm:F2} ({bpmSource})");
                return grid;
            }).ConfigureAwait(false);
        }

        private static async Task<VirtualTrim> FindVirtualTrimAsync(AudioObj audio)
        {
            TimeSpan startSilence = await BeatGridFinder_V2.FindSilenceDurationStartAsync(audio, minDurationMs: 50).ConfigureAwait(false);
            TimeSpan endSilence = await BeatGridFinder_V2.FindSilenceDurationEndAsync(audio, minDurationMs: 50).ConfigureAwait(false);

            int channels = Math.Max(1, audio.Channels);
            int totalFrames = audio.Data!.Length / channels;
            int startFrame = (int)Math.Round(startSilence.TotalSeconds * audio.SampleRate);
            int trailingFrames = (int)Math.Round(endSilence.TotalSeconds * audio.SampleRate);
            startFrame = Math.Clamp(startFrame, 0, totalFrames);
            int endFrame = Math.Clamp(totalFrames - trailingFrames, startFrame, totalFrames);

            if (endFrame <= startFrame)
            {
                return new VirtualTrim(0, totalFrames);
            }

            return new VirtualTrim(startFrame, endFrame);
        }

        private static bool[] GenerateBeatGrid(AudioObj audio, VirtualTrim trim, int granularity, out double bpm, out string bpmSource)
        {
            int channels = Math.Max(1, audio.Channels);
            int sampleRate = audio.SampleRate;
            int totalFrames = audio.Data!.Length / channels;
            var emptyGrid = new bool[totalFrames];

            if (!TryGetBpm(audio, out bpm, out bpmSource))
            {
                return emptyGrid;
            }

            if (bpm <= 0.0)
            {
                return emptyGrid;
            }

            // granularity=4 is a quarter note; 1, 2, 8 and 16 select whole,
            // half, eighth and sixteenth notes respectively.
            double intervalFrames = sampleRate * 60.0 / bpm * 4.0 / Math.Clamp(granularity, 1, 16);
            int trimLengthFrames = trim.EndFrame - trim.StartFrame;

            if (trimLengthFrames < sampleRate / 2)
            {
                return emptyGrid;
            }

            // Anchor on the onset (leading edge) of a clear attack, then
            // propagate the fixed BPM interval in both directions.
            double quarterNoteFrames = sampleRate * 60.0 / bpm;
            int anchorFrame = FindAttackOnset(audio.Data, channels, trim.StartFrame, trim.EndFrame, sampleRate, quarterNoteFrames);
            double phaseOffsetFrames = anchorFrame - trim.StartFrame;

            // Extend the rigid grid both forward and backward from the anchor.
            var grid = new bool[totalFrames];
            long firstIndex = (long)Math.Ceiling(-phaseOffsetFrames / intervalFrames);
            long lastIndex = (long)Math.Floor((trimLengthFrames - 1 - phaseOffsetFrames) / intervalFrames);
            for (long beatIndex = firstIndex; beatIndex <= lastIndex; beatIndex++)
            {
                double relativeFrame = phaseOffsetFrames + beatIndex * intervalFrames;
                int originalFrame = (int)Math.Round(trim.StartFrame + relativeFrame, MidpointRounding.AwayFromZero);
                if (originalFrame >= trim.StartFrame && originalFrame < trim.EndFrame)
                {
                    grid[originalFrame] = true;
                }
            }

            return grid;
        }

        private static double FindGlobalPhase(
            float[] envelope,
            int trimLengthFrames,
            double intervalFrames,
            int hopFrames,
            int sampleRate)
        {
            double phaseStepFrames = Math.Max(1.0, sampleRate * 0.002); // 2 ms steps
            int phaseWindowSamples = Math.Max(1, (int)Math.Round(0.020 * sampleRate / hopFrames)); // 20 ms window
            double bestScore = double.NegativeInfinity;
            double bestPhase = 0.0;

            for (double phase = 0.0; phase < intervalFrames; phase += phaseStepFrames)
            {
                double score = 0.0;
                int count = 0;
                for (double frame = phase; frame < trimLengthFrames; frame += intervalFrames)
                {
                    int center = (int)Math.Round(frame / hopFrames);
                    int start = Math.Max(0, center - phaseWindowSamples);
                    int end = Math.Min(envelope.Length - 1, center + phaseWindowSamples);

                    // Find the minimum envelope value in the window (the valley).
                    float localMinimum = float.MaxValue;
                    for (int sample = start; sample <= end; sample++)
                    {
                        localMinimum = Math.Min(localMinimum, envelope[sample]);
                    }

                    // Score: lower minimum = better (valley is deeper/clearer).
                    score += -localMinimum;
                    count++;
                }

                if (count > 0)
                {
                    score /= count;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestPhase = phase;
                    }
                }
            }

            return bestPhase;
        }

        private static int FindAttackOnset(
    float[] data,
    int channels,
    int startFrame,
    int endFrame,
    int sampleRate,
    double beatIntervalFrames)
        {
            int analysisFrames = Math.Max(0, endFrame - startFrame);

            if (analysisFrames <= 0)
            {
                return startFrame;
            }

            /*
             * 1 ms resolution.
             *
             * We intentionally use a considerably finer analysis here than
             * the normal 200 Hz envelope because this method establishes the
             * actual phase of the rigid analog grid.
             */
            int hopFrames = Math.Max(
                1,
                (int)Math.Round(sampleRate / 1000.0));

            int analysisCount =
                (analysisFrames + hopFrames - 1) / hopFrames;

            if (analysisCount < 8)
            {
                return startFrame;
            }

            var rms = new float[analysisCount];

            for (int index = 0; index < analysisCount; index++)
            {
                int blockStartFrame =
                    startFrame + index * hopFrames;

                int blockEndFrame =
                    Math.Min(
                        endFrame,
                        blockStartFrame + hopFrames);

                double sum = 0.0;
                int count = 0;

                for (int frame = blockStartFrame;
                     frame < blockEndFrame;
                     frame++)
                {
                    int offset = frame * channels;

                    for (int channel = 0; channel < channels; channel++)
                    {
                        float sample = data[offset + channel];

                        sum += sample * sample;
                        count++;
                    }
                }

                rms[index] = count > 0
                    ? (float)Math.Sqrt(sum / count)
                    : 0f;
            }

            /*
             * Light temporal smoothing.
             *
             * This suppresses single-sample spikes without destroying
             * drum attacks.
             *
             * The window is causal (backward) on purpose: a centered window
             * lags an attack by half its width, which would shift the grid
             * anchor past the onset. A backward window keeps the smoothed
             * value at the attack's leading edge aligned with the onset.
             */
            var smoothed = new float[analysisCount];

            for (int index = 0; index < analysisCount; index++)
            {
                int start = Math.Max(0, index - 4);
                int end = index;

                double sum = 0.0;

                for (int current = start; current <= end; current++)
                {
                    sum += rms[current];
                }

                smoothed[index] =
                    (float)(sum / (end - start + 1));
            }

            float globalMax = smoothed.Max();

            if (globalMax <= 1e-8f)
            {
                return startFrame;
            }

            /*
             * Estimate the noise floor from the lower part of the envelope.
             * A percentile-like approximation using a sorted copy is robust
             * against loud intro transients.
             */
            float[] sorted = smoothed.ToArray();
            Array.Sort(sorted);

            int floorIndex = Math.Clamp(
                (int)Math.Round(sorted.Length * 0.20),
                0,
                sorted.Length - 1);

            float noiseFloor = sorted[floorIndex];

            /*
             * Dynamic threshold.
             *
             * We deliberately don't use only a fixed percentage of the global
             * maximum because a loud later chorus would otherwise make the
             * intro beats disappear.
             */
            float threshold = Math.Max(
                noiseFloor * 1.8f,
                globalMax * 0.08f);

            // Select the strongest plausible attack in the whole trimmed
            // track. Using the first threshold crossing lets an intro click
            // or weak pickup shift every later grid line.
            float strongestRise = 0f;
            int strongestAttackIndex = -1;
            for (int index = 3; index < analysisCount - 2; index++)
            {
                float current = smoothed[index];

                if (current < threshold)
                {
                    continue;
                }

                float previous = smoothed[index - 3];

                float rise = current - previous;

                if (rise <= 0f)
                {
                    continue;
                }

                /*
                 * Require continued growth after the candidate.
                 */
                float future = smoothed[index + 2];

                if (future < current)
                {
                    continue;
                }

                /*
                 * Avoid classifying a slowly increasing ambience as an onset.
                 */
                if (rise < Math.Max(
                        globalMax * 0.015f,
                        noiseFloor * 0.25f))
                {
                    continue;
                }

                if (rise > strongestRise)
                {
                    strongestRise = rise;
                    strongestAttackIndex = index;
                }
            }

            if (strongestAttackIndex >= 0)
            {
                // Anchor the grid on the leading edge (onset) of the strongest
                // attack. The grid line belongs at the attack itself, not at
                // the quiet trough before it; anchoring on the trough shifted
                // the whole rigid grid backward by a beat-dependent amount.
                //
                // The walk-back is bounded to a short pre-attack window (a
                // fraction of the beat interval) so it finds the local trough
                // immediately before the attack — the true onset — instead of
                // walking all the way to the start of the track.
                float baseline = smoothed[Math.Max(0, strongestAttackIndex - 3)];
                float onsetThreshold = baseline + strongestRise * 0.01f;
                int lookback = Math.Max(2, (int)Math.Round(beatIntervalFrames * 0.12 / hopFrames));
                int searchStart = Math.Max(0, strongestAttackIndex - lookback);
                int onsetIndex = strongestAttackIndex;
                while (onsetIndex > searchStart && smoothed[onsetIndex - 1] > onsetThreshold)
                {
                    onsetIndex--;
                }

                return Math.Clamp(startFrame + onsetIndex * hopFrames, startFrame, endFrame - 1);
            }

            // Fallback for material without a threshold-crossing attack:
            // anchor on the leading edge of the largest energy rise.
            int bestIndex = 1;
            for (int index = 2; index < analysisCount; index++)
            {
                if (smoothed[index] - smoothed[index - 1] > smoothed[bestIndex] - smoothed[bestIndex - 1])
                {
                    bestIndex = index;
                }
            }
            return Math.Clamp(startFrame + bestIndex * hopFrames, startFrame, endFrame - 1);
        }

        private static bool TryGetBpm(AudioObj audio, out double bpm, out string source)
        {
            if (IsPlausibleBpm(audio.Bpm))
            {
                bpm = audio.Bpm;
                source = "audio.Bpm";
                return true;
            }

            if (IsPlausibleBpm(audio.ScannedBpm))
            {
                bpm = audio.ScannedBpm;
                source = "audio.ScannedBpm";
                return true;
            }

            bpm = 0.0;
            source = "none";
            return false;
        }

        private static bool IsPlausibleBpm(double bpm)
        {
            return bpm >= MinimumBpm && bpm <= MaximumBpm;
        }

        private readonly record struct VirtualTrim(int StartFrame, int EndFrame);
    }
}
