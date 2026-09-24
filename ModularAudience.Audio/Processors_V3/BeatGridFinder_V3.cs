using System;
using System.Linq;
using System.Threading.Tasks;
using ModularAudience.Audio.Processors_V2;

namespace ModularAudience.Audio.Processors_V3
{
    public static class BeatGridFinder_V3
    {
        private const int AnalysisRateHz = 200;
        private const int AttackAnalysisRateHz = 1000;
        private const double MinimumBpm = 55.0;
        private const double MaximumBpm = 190.0;
        private const double MinimumMetadataBpm = 30.0;
        private const double MaximumMetadataBpm = 300.0;
        private const double PhaseStepSeconds = 0.002;
        private const double MaximumBeatRefinementRatio = 0.20;
        private const double EnvelopeAttackRatio = 0.05;
        private const float AttackThresholdRatio = 0.25f;
        private const float MinimumRefinementOnset = 0.08f;

        public static async Task<bool[]> GenerateBeatGridAsync(AudioObj audio, bool set = true, int granularity = 4)
        {
            if (audio == null || audio.Data == null || audio.Data.Length == 0 || audio.SampleRate <= 0)
            {
                return [];
            }

            VirtualTrim trim = await FindVirtualTrimAsync(audio).ConfigureAwait(false);

            // Metadata takes precedence. When absent, use the established scanners
            // before the local fallback estimator in the grid analysis.
            if (!TryGetConfiguredBpm(audio, out _, out _))
            {
                double scannedBpm = -1.0;
                try
                {
                    scannedBpm = await BeatScanner_V2.ScanBpmAsync_V2(audio).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LogCollection.Log(ex);
                }

                if (!IsPlausibleBpm(scannedBpm))
                {
                    try
                    {
                        scannedBpm = await ModularAudience.Audio.Processors_V1.BeatScanner.ScanBpmAsync(audio).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        LogCollection.Log(ex);
                    }
                }

                if (IsPlausibleBpm(scannedBpm))
                {
                    audio.ScannedBpm = (float)scannedBpm;
                }
            }

            return await Task.Run(() =>
            {
                bool[] grid = GenerateBeatGrid(audio, trim, granularity, out double bpm, out double phaseMs, out string bpmSource);
                if (set)
                {
                    audio.BeatGrid = grid;
                }

                int beatCount = grid.Count(value => value);
                double trimStartSeconds = trim.StartFrame / (double)audio.SampleRate;
                double trimEndSeconds = trim.EndFrame / (double)audio.SampleRate;
                LogCollection.Log($"Beat grid V3 finished. Beats: {beatCount}, trim={trimStartSeconds:F2}-{trimEndSeconds:F2}s, bpm={bpm:F2} ({bpmSource}), phase from trim={phaseMs:F1}ms");
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

        private static bool[] GenerateBeatGrid(
    AudioObj audio,
    VirtualTrim trim,
    int granularity,
    out double bpm,
    out double phaseMs,
    out string bpmSource)
        {
            int channels = Math.Max(1, audio.Channels);
            int sampleRate = audio.SampleRate;
            int totalFrames = audio.Data!.Length / channels;

            var emptyGrid = new bool[totalFrames];

            int trimLengthFrames =
                trim.EndFrame - trim.StartFrame;

            if (trimLengthFrames < sampleRate / 2)
            {
                bpm = 0.0;
                phaseMs = 0.0;
                bpmSource = "none";
                return emptyGrid;
            }

            int hopFrames = Math.Max(
                1,
                (int)Math.Round(
                    sampleRate /
                    (double)AnalysisRateHz));

            BeatAnalysis tempoAnalysis =
                BuildBeatAnalysis(
                    audio.Data,
                    channels,
                    trim.StartFrame,
                    trim.EndFrame,
                    hopFrames);

            float[] onset = tempoAnalysis.Onset;

            if (onset.Length < 32 ||
                onset.Max() <= 0f)
            {
                bpm = 0.0;
                phaseMs = 0.0;
                bpmSource = "none";
                return emptyGrid;
            }

            /*
             * Prefer configured BPM exactly as before.
             * Only detect BPM when neither metadata property is usable.
             */
            if (!TryGetConfiguredBpm(
                    audio,
                    out bpm,
                    out bpmSource))
            {
                bpm = EstimateBpm(
                    onset,
                    sampleRate,
                    hopFrames);

                bpmSource =
                    bpm > 0.0
                        ? "detectedBpm"
                        : "none";
            }

            if (bpm <= 0.0)
            {
                phaseMs = 0.0;
                return emptyGrid;
            }

            granularity = granularity is 1 or 2 or 4 or 8 or 16 ? granularity : 4;
            double intervalFrames = sampleRate * 60.0 / bpm * 4.0 / granularity;
            // Keep quarter notes as the phase anchors for every finer grid,
            // then interpolate eighth/sixteenth marks between them. Searching
            // the phase again at eighth-note spacing can choose the offbeat
            // as the phase and shift the entire 1/16 grid.
            int anchorGranularity = granularity >= 8 ? 4 : granularity;
            double anchorIntervalFrames = sampleRate * 60.0 / bpm * 4.0 / anchorGranularity;

            /*
             * Phase is now explicitly based on valleys followed by attacks.
             */
            double phaseOffsetFrames =
                FindGlobalPhase(
                    tempoAnalysis.Envelope,
                    onset,
                    trimLengthFrames,
                    anchorIntervalFrames,
                    hopFrames,
                    sampleRate);

            phaseMs =
                phaseOffsetFrames *
                1000.0 /
                sampleRate;

            /*
             * High-resolution attack analysis.
             *
             * This is only necessary for the local refinement step.
             */
            int attackHopFrames = Math.Max(
                1,
                (int)Math.Round(
                    sampleRate /
                    (double)AttackAnalysisRateHz));

            BeatAnalysis attackAnalysis =
                attackHopFrames == hopFrames
                    ? tempoAnalysis
                    : BuildBeatAnalysis(
                        audio.Data,
                        channels,
                        trim.StartFrame,
                        trim.EndFrame,
                        attackHopFrames);

            var grid = new bool[totalFrames];

            /*
             * -------------------------------------------------------------
             * PASS 1
             *
             * Establish the rigid mathematical grid.
             * -------------------------------------------------------------
             */
            int estimatedBeatCount =
                (int)Math.Ceiling(
                    trimLengthFrames /
                    anchorIntervalFrames) + 2;

            var expectedPositions =
                new double[Math.Max(
                    1,
                    estimatedBeatCount)];

            int positionCount = 0;

            for (
                double relativeFrame = phaseOffsetFrames;
                relativeFrame < trimLengthFrames;
                relativeFrame += anchorIntervalFrames)
            {
                if (positionCount >= expectedPositions.Length)
                {
                    Array.Resize(
                        ref expectedPositions,
                        expectedPositions.Length * 2);
                }

                expectedPositions[positionCount++] =
                    relativeFrame;
            }

            /*
             * -------------------------------------------------------------
             * PASS 2
             *
             * Refine only high-confidence positions.
             *
             * Weak beats remain exactly on the rigid mathematical grid.
             * -------------------------------------------------------------
             */
            var refinedAnchors = new double[positionCount];
            for (int index = 0; index < positionCount; index++)
            {
                double expectedRelativeFrame =
                    expectedPositions[index];

                double refinedRelativeFrame =
                    RefineBeatPosition(
                        attackAnalysis.Envelope,
                        attackAnalysis.Onset,
                        audio.Data,
                        channels,
                        trim.StartFrame,
                        expectedRelativeFrame,
                        anchorIntervalFrames,
                        attackHopFrames,
                        sampleRate);

                refinedAnchors[index] = refinedRelativeFrame;
            }

            int subdivisionsPerAnchor = Math.Max(1, (int)Math.Round(anchorIntervalFrames / intervalFrames));
            for (int index = 0; index < refinedAnchors.Length; index++)
            {
                double anchor = refinedAnchors[index];
                double nextAnchor = index + 1 < refinedAnchors.Length
                    ? refinedAnchors[index + 1]
                    : anchor + anchorIntervalFrames;
                for (int subdivision = 0; subdivision < subdivisionsPerAnchor; subdivision++)
                {
                    double relativeFrame = anchor + (nextAnchor - anchor) * subdivision / subdivisionsPerAnchor;
                    int originalFrame = (int)Math.Round(trim.StartFrame + relativeFrame, MidpointRounding.AwayFromZero);
                    if (originalFrame >= trim.StartFrame && originalFrame < trim.EndFrame)
                    {
                        grid[originalFrame] = true;
                    }
                }
            }

            return grid;
        }

        private static double RefineBeatPosition(
    float[] envelope,
    float[] onset,
    float[] audioData,
    int channels,
    int trimStartFrame,
    double expectedRelativeFrame,
    double intervalFrames,
    int hopFrames,
    int sampleRate)
        {
            if (envelope.Length == 0 ||
                onset.Length == 0)
            {
                return expectedRelativeFrame;
            }

            /*
             * Search substantially more before the attack than the old 10 ms
             * window. A beat's decay can easily be longer than that.
             *
             * The resulting position is nevertheless constrained strongly
             * around the mathematical grid position.
             */
            double maximumOffsetFrames = intervalFrames * 0.12;

            int expectedIndex = Math.Clamp(
                (int)Math.Round(
                    expectedRelativeFrame / hopFrames),
                0,
                onset.Length - 1);

            int offsetSamples = Math.Max(
                1,
                (int)Math.Ceiling(
                    maximumOffsetFrames / hopFrames));

            /*
             * We search for the strongest attack in a window AFTER the
             * mathematical grid position.
             *
             * The grid itself is expected to lie before this attack.
             */
            int attackSearchStart = Math.Max(
                0,
                expectedIndex -
                Math.Max(
                    1,
                    (int)Math.Round(
                        intervalFrames * 0.01 / hopFrames)));

            int attackSearchEnd = Math.Min(
                onset.Length - 1,
                expectedIndex + offsetSamples);

            float strongestOnset = 0f;
            int strongestOnsetIndex = expectedIndex;

            for (
                int index = attackSearchStart;
                index <= attackSearchEnd;
                index++)
            {
                if (onset[index] > strongestOnset)
                {
                    strongestOnset = onset[index];
                    strongestOnsetIndex = index;
                }
            }

            /*
             * No meaningful attack nearby:
             *
             * IMPORTANT:
             * do not move the mathematical grid.
             *
             * This is exactly the hybrid behaviour requested:
             * weak/ambiguous regions fall back to the rigid grid.
             */
            if (strongestOnset < MinimumRefinementOnset)
            {
                return expectedRelativeFrame;
            }

            /*
             * Search backwards from the attack for the actual valley.
             *
             * This is intentionally much wider than the old search window.
             */
            int valleySearchStart = Math.Max(
                0,
                strongestOnsetIndex -
                Math.Max(
                    offsetSamples * 2,
                    (int)Math.Round(
                        intervalFrames * 0.12 /
                        hopFrames)));

            int valleySearchEnd =
                strongestOnsetIndex;

            // Select the last local low immediately before the rise. A broad
            // absolute-minimum search can jump back to an earlier quiet point,
            // while the beat boundary is the trough next to this attack.
            int valleyIndex = FindValleyBeforeOnset(
                envelope,
                strongestOnsetIndex,
                valleySearchStart,
                intervalFrames,
                hopFrames);
            if (valleyIndex < valleySearchStart || valleyIndex > valleySearchEnd)
            {
                return expectedRelativeFrame;
            }
            float minimumEnvelope = envelope[valleyIndex];

            double valleyFrame =
                valleyIndex * (double)hopFrames;

            /*
             * The valley must still be close enough to the rigid grid.
             */
            if (Math.Abs(
                    valleyFrame -
                    expectedRelativeFrame) >
                maximumOffsetFrames)
            {
                return expectedRelativeFrame;
            }

            /*
             * Confidence check:
             *
             * We want the valley to be followed by an actual rise.
             */
            int riseEnd = Math.Min(
                envelope.Length - 1,
                valleyIndex +
                Math.Max(
                    1,
                    (int)Math.Round(
                        sampleRate * 0.015 / hopFrames)));

            float postValleyRise =
                envelope[riseEnd] -
                envelope[valleyIndex];

            /*
             * The attack must also actually occur after the valley.
             */
            if (strongestOnsetIndex <= valleyIndex ||
                postValleyRise < 0.01f)
            {
                return expectedRelativeFrame;
            }

            /*
             * Confidence becomes a continuous quantity rather than a binary
             * "onset exists" decision.
             */
            double valleyQuality =
                1.0 -
                Math.Clamp(
                    minimumEnvelope,
                    0f,
                    1f);

            double attackQuality =
                Math.Clamp(
                    strongestOnset,
                    0f,
                    1f);

            double riseQuality =
                Math.Clamp(
                    postValleyRise * 5.0,
                    0.0,
                    1.0);

            double confidence =
                valleyQuality * 0.35 +
                attackQuality * 0.45 +
                riseQuality * 0.20;

            /*
             * Only perform waveform refinement when there is convincing
             * evidence. Otherwise retain the mathematically perfect grid.
             */
            if (confidence < 0.45)
            {
                return expectedRelativeFrame;
            }

            // The analysis envelope is sampled at ~1 kHz and smoothed across
            // several cells. Re-check the original samples around that trough
            // so the marker lands on the actual transition valley, not a few
            // envelope samples into the attack.
            long coarseAbsoluteFrame = trimStartFrame + (long)Math.Round(valleyFrame);
            long fineAbsoluteFrame = FindSampleAccurateValley(
                audioData,
                channels,
                coarseAbsoluteFrame,
                sampleRate);
            double fineRelativeFrame = fineAbsoluteFrame - trimStartFrame;
            return Math.Abs(fineRelativeFrame - expectedRelativeFrame) <= maximumOffsetFrames
                ? fineRelativeFrame
                : valleyFrame;
        }

        private static long FindSampleAccurateValley(float[] data, int channels, long centerFrame, int sampleRate)
        {
            int totalFrames = data.Length / Math.Max(1, channels);
            int radius = Math.Max(1, (int)Math.Round(sampleRate * 0.006));
            int window = Math.Max(1, (int)Math.Round(sampleRate * 0.002));
            int halfWindow = window / 2;
            int first = (int)Math.Clamp(centerFrame - radius, halfWindow, totalFrames - window + halfWindow - 1L);
            int last = (int)Math.Clamp(centerFrame + radius, first, totalFrames - window + halfWindow - 1L);

            double bestEnergy = double.PositiveInfinity;
            int bestFrame = (int)Math.Clamp(centerFrame, first, last);
            for (int frame = first; frame <= last; frame++)
            {
                int windowStart = Math.Clamp(frame - halfWindow, 0, Math.Max(0, totalFrames - window));
                double energy = 0.0;
                int count = 0;
                for (int sampleFrame = windowStart; sampleFrame < windowStart + window; sampleFrame++)
                {
                    int offset = sampleFrame * channels;
                    for (int channel = 0; channel < channels; channel++)
                    {
                        float sample = data[offset + channel];
                        energy += sample * sample;
                        count++;
                    }
                }
                if (count > 0 && energy / count < bestEnergy)
                {
                    bestEnergy = energy / count;
                    bestFrame = frame;
                }
            }

            // A tiny leading bias compensates for the centered RMS window.
            return Math.Max(0, bestFrame - Math.Max(1, (int)Math.Round(sampleRate * 0.00007)));
        }

        private static int FindValleyBeforeOnset(
    float[] envelope,
    int onsetPeakIndex,
    int searchStart,
    double beatIntervalFrames,
    int hopFrames)
        {
            if (envelope.Length == 0 ||
                onsetPeakIndex <= searchStart ||
                onsetPeakIndex >= envelope.Length)
            {
                return -1;
            }

            /*
             * The desired point is the local minimum immediately before the
             * attack, not simply the first point below a threshold.
             */
            int searchEnd = onsetPeakIndex;

            /*
             * Limit the search to a musically useful region. The old method
             * could effectively select a much older low-energy region.
             */
            int minimumSearchDistance = Math.Max(1, (int)Math.Round(beatIntervalFrames * 0.01 / hopFrames));

            int localStart = Math.Max(
                searchStart,
                onsetPeakIndex -
                Math.Max(
                    minimumSearchDistance,
                    (int)Math.Round(beatIntervalFrames * 0.08 / hopFrames)));

            float bestEnergy = float.MaxValue;
            int bestIndex = -1;

            for (
                int index = localStart;
                index <= searchEnd;
                index++)
            {
                if (envelope[index] < bestEnergy)
                {
                    bestEnergy = envelope[index];
                    bestIndex = index;
                }
            }

            if (bestIndex < 0)
            {
                return -1;
            }

            /*
             * Prefer a valley that is actually followed by a rise.
             *
             * Search a few candidates from right to left and select the first
             * convincing valley-to-attack transition.
             */
            for (
                int index = searchEnd - 1;
                index >= localStart;
                index--)
            {
                if (envelope[index] > envelope[index + 1])
                {
                    continue;
                }

                int lookAhead = Math.Min(
                    searchEnd,
                    index + Math.Max(1, (int)Math.Round(beatIntervalFrames * 0.01 / hopFrames)));

                float futureMaximum = envelope[index];

                for (
                    int future = index + 1;
                    future <= lookAhead;
                    future++)
                {
                    futureMaximum =
                        Math.Max(
                            futureMaximum,
                            envelope[future]);
                }

                float rise =
                    futureMaximum -
                    envelope[index];

                if (rise >= 0.01f)
                {
                    return index;
                }
            }

            return bestIndex;
        }

        private static bool TryGetConfiguredBpm(AudioObj audio, out double bpm, out string source)
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
            source = "detectedBpm";
            return false;
        }

        private static bool IsPlausibleBpm(double bpm)
        {
            return bpm >= MinimumMetadataBpm && bpm <= MaximumMetadataBpm;
        }

        private static BeatAnalysis BuildBeatAnalysis(
    float[] data,
    int channels,
    int startFrame,
    int endFrame,
    int hopFrames)
        {
            int analysisFrames = Math.Max(0, endFrame - startFrame);

            int analysisCount =
                (analysisFrames + hopFrames - 1) / hopFrames;

            if (analysisCount <= 0)
            {
                return new BeatAnalysis([], []);
            }

            var rms = new float[analysisCount];
            var waveformChange = new float[analysisCount];

            /*
             * RMS envelope.
             */
            for (int index = 0; index < analysisCount; index++)
            {
                int blockStartFrame =
                    startFrame + index * hopFrames;

                int blockEndFrame =
                    Math.Min(
                        endFrame,
                        blockStartFrame + hopFrames);

                double sum = 0.0;
                double differenceSum = 0.0;
                int count = 0;

                for (int frame = blockStartFrame;
                     frame < blockEndFrame;
                     frame++)
                {
                    int offset = frame * channels;

                    for (int channel = 0; channel < channels; channel++)
                    {
                        float sample = data[offset + channel];
                        float previous = frame > 0 ? data[offset - channels + channel] : 0f;

                        sum += sample * sample;
                        float difference = sample - previous;
                        differenceSum += difference * difference;
                        count++;
                    }
                }

                rms[index] = count > 0
                    ? (float)Math.Sqrt(sum / count)
                    : 0f;
                waveformChange[index] = count > 0
                    ? (float)Math.Sqrt(differenceSum / count)
                    : 0f;
            }

            /*
             * Robust smoothing.
             *
             * The envelope is supposed to describe musical energy rather than
             * individual waveform oscillations.
             */
            var envelope = new float[analysisCount];

            for (int index = 0; index < analysisCount; index++)
            {
                int start = Math.Max(0, index - 2);
                int end = Math.Min(
                    analysisCount - 1,
                    index + 2);

                double sum = 0.0;

                for (int current = start; current <= end; current++)
                {
                    sum += rms[current];
                }

                envelope[index] =
                    (float)(sum / (end - start + 1));
            }

            float envelopeMaximum = envelope.Max();

            if (envelopeMaximum <= 1e-8f)
            {
                return new BeatAnalysis(envelope, new float[analysisCount]);
            }

            /*
             * Normalize the envelope.
             */
            for (int index = 0; index < envelope.Length; index++)
            {
                envelope[index] /= envelopeMaximum;
            }

            // Track first-difference energy as well as amplitude. A genuine
            // phase/frequency reset can be clear in waveform continuity even
            // when the overall RMS envelope changes only slightly.
            var changeEnvelope = new float[analysisCount];
            float changeMaximum = waveformChange.Max();
            if (changeMaximum > 1e-8f)
            {
                for (int index = 0; index < analysisCount; index++)
                {
                    int start = Math.Max(0, index - 2);
                    int end = Math.Min(analysisCount - 1, index + 2);
                    double sum = 0.0;
                    for (int current = start; current <= end; current++)
                    {
                        sum += waveformChange[current];
                    }
                    changeEnvelope[index] = (float)(sum / (end - start + 1) / changeMaximum);
                }
            }

            var onset = new float[analysisCount];

            /*
             * Multi-scale attack detector.
             *
             * A single-frame difference is much too susceptible to waveform
             * noise. We compare short and medium envelope differences.
             */
            for (int index = 1; index < analysisCount; index++)
            {
                float shortRise =
                    envelope[index] - envelope[index - 1];

                int mediumOffset = Math.Min(4, index);

                float mediumRise =
                    envelope[index] -
                    envelope[index - mediumOffset];

                float changeShortRise = changeEnvelope[index] - changeEnvelope[index - 1];
                float changeMediumRise = changeEnvelope[index] - changeEnvelope[index - mediumOffset];

                shortRise = Math.Max(0f, shortRise);
                mediumRise = Math.Max(0f, mediumRise);
                changeShortRise = Math.Max(0f, changeShortRise);
                changeMediumRise = Math.Max(0f, changeMediumRise);

                /*
                 * Weight the immediate attack strongly, while retaining a
                 * little information about the preceding rise.
                 */
                onset[index] =
                    (shortRise * 0.70f + mediumRise * 0.30f) * 0.65f +
                    (changeShortRise * 0.70f + changeMediumRise * 0.30f) * 0.35f;
            }

            /*
             * Adaptive noise rejection.
             */
            float maximumOnset = onset.Max();

            if (maximumOnset <= 1e-8f)
            {
                return new BeatAnalysis(envelope, onset);
            }

            float[] sortedOnset = onset.ToArray();
            Array.Sort(sortedOnset);

            int noiseIndex = Math.Clamp(
                (int)Math.Round(sortedOnset.Length * 0.65),
                0,
                sortedOnset.Length - 1);

            float onsetNoise = sortedOnset[noiseIndex];

            float onsetFloor = Math.Max(
                maximumOnset * 0.025f,
                onsetNoise * 1.5f);

            for (int index = 0; index < onset.Length; index++)
            {
                onset[index] = Math.Max(
                    0f,
                    onset[index] - onsetFloor);
            }

            maximumOnset = onset.Max();

            if (maximumOnset > 1e-8f)
            {
                for (int index = 0; index < onset.Length; index++)
                {
                    onset[index] /= maximumOnset;
                }
            }

            /*
             * Suppress isolated one-cell spikes.
             *
             * We don't delete legitimate attacks aggressively; instead a spike
             * only survives if it has at least some neighbouring energy.
             */
            for (int index = 1; index < onset.Length - 1; index++)
            {
                float neighbours =
                    Math.Max(
                        onset[index - 1],
                        onset[index + 1]);

                if (onset[index] > neighbours * 4.0f &&
                    onset[index] < 0.20f)
                {
                    onset[index] *= 0.35f;
                }
            }

            return new BeatAnalysis(envelope, onset);
        }

        private static double EstimateBpm(float[] onset, int sampleRate, int hopFrames)
        {
            double analysisRate = sampleRate / (double)hopFrames;
            int minLag = Math.Max(2, (int)Math.Round(analysisRate * 60.0 / MaximumBpm));
            int maxLag = Math.Min(
                onset.Length / 2,
                Math.Max(minLag + 1, (int)Math.Round(analysisRate * 60.0 / MinimumBpm)));
            if (maxLag <= minLag)
            {
                return 0.0;
            }

            double mean = onset.Average();
            var centered = new float[onset.Length];
            for (int index = 0; index < onset.Length; index++)
            {
                centered[index] = Math.Max(0f, onset[index] - (float)mean);
            }

            double bestScore = double.NegativeInfinity;
            int bestLag = 0;
            for (int lag = minLag; lag <= maxLag; lag++)
            {
                double score = NormalizedCorrelation(centered, lag);
                if (lag * 2 < centered.Length)
                {
                    score = score * 0.75 + NormalizedCorrelation(centered, lag * 2) * 0.25;
                }

                double bpm = 60.0 * analysisRate / lag;
                double tempoPrior = Math.Exp(-Math.Pow((bpm - 120.0) / 55.0, 2.0) * 0.5);
                score *= 0.85 + 0.15 * tempoPrior;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestLag = lag;
                }
            }

            return bestLag > 0 ? 60.0 * analysisRate / bestLag : 0.0;
        }

        private static double NormalizedCorrelation(float[] values, int lag)
        {
            if (lag <= 0 || lag >= values.Length)
            {
                return 0.0;
            }

            double dot = 0.0;
            double leftEnergy = 0.0;
            double rightEnergy = 0.0;
            for (int index = lag; index < values.Length; index++)
            {
                double left = values[index];
                double right = values[index - lag];
                dot += left * right;
                leftEnergy += left * left;
                rightEnergy += right * right;
            }

            double divisor = Math.Sqrt(leftEnergy * rightEnergy);
            return divisor > 1e-12 ? dot / divisor : 0.0;
        }

        private static double FindGlobalPhase(
    float[] envelope,
    float[] onset,
    int trimLengthFrames,
    double intervalFrames,
    int hopFrames,
    int sampleRate)
        {
            if (envelope.Length == 0 ||
                onset.Length == 0 ||
                intervalFrames <= 0.0 ||
                hopFrames <= 0)
            {
                return 0.0;
            }

            /*
             * We search the complete beat interval at fine resolution.
             *
             * A 2 ms phase resolution is retained because it is cheap compared
             * with the audio analysis and gives a stable sample-domain phase.
             */
            double phaseStepFrames =
                Math.Max(
                    1.0,
                    sampleRate * PhaseStepSeconds);

            /*
             * The important change:
             *
             * phase positions are evaluated as candidate VALLEYS.
             *
             * We then look forward for an attack. This means the grid line
             * represents:
             *
             *     previous beat decay
             *              ↓
             *          [ GRID ]
             *              ↗
             *          next attack
             */
            int valleyWindow = Math.Max(1, (int)Math.Round(intervalFrames * 0.02 / hopFrames));

            int attackLookAhead =
                Math.Max(
                    valleyWindow * 2,
                    (int)Math.Round(intervalFrames * 0.08 / hopFrames));

            double bestScore = double.NegativeInfinity;
            double bestPhase = 0.0;

            for (
                double phase = 0.0;
                phase < intervalFrames;
                phase += phaseStepFrames)
            {
                double totalScore = 0.0;
                int candidateCount = 0;
                int observations = 0;

                for (
                    double relativeFrame = phase;
                    relativeFrame < trimLengthFrames;
                    relativeFrame += intervalFrames)
                {
                    candidateCount++;
                    int centerIndex = Math.Clamp(
                        (int)Math.Round(relativeFrame / hopFrames),
                        0,
                        envelope.Length - 1);

                    /*
                     * Find the local valley around the candidate position.
                     */
                    int valleyStart = Math.Max(
                        0,
                        centerIndex - valleyWindow);

                    int valleyEnd = Math.Min(
                        envelope.Length - 1,
                        centerIndex + valleyWindow);

                    float localMinimum = float.MaxValue;
                    int valleyIndex = centerIndex;

                    for (
                        int index = valleyStart;
                        index <= valleyEnd;
                        index++)
                    {
                        if (envelope[index] < localMinimum)
                        {
                            localMinimum = envelope[index];
                            valleyIndex = index;
                        }
                    }

                    /*
                     * Search forward from the valley for the next attack.
                     *
                     * This explicitly encodes the visual relationship we want:
                     *
                     * valley -> rising attack -> beat
                     */
                    int attackStart = valleyIndex;

                    int attackEnd = Math.Min(
                        onset.Length - 1,
                        valleyIndex + attackLookAhead);

                    float strongestAttack = 0f;

                    for (
                        int index = attackStart;
                        index <= attackEnd;
                        index++)
                    {
                        strongestAttack =
                            Math.Max(
                                strongestAttack,
                                onset[index]);
                    }

                    /*
                     * Valley quality:
                     *
                     * lower envelope = better.
                     */
                    double valleyScore =
                        1.0 - Math.Clamp(localMinimum, 0f, 1f);

                    /*
                     * Attack quality:
                     *
                     * stronger following onset = better.
                     */
                    double attackScore =
                        Math.Clamp(strongestAttack, 0f, 1f);

                    /*
                     * Also inspect the envelope slope after the valley.
                     *
                     * A genuine beat transition should generally move upward
                     * after the valley.
                     */
                    int slopeIndex = Math.Min(
                        envelope.Length - 1,
                        valleyIndex + Math.Max(1, valleyWindow / 2));

                    float slope =
                        envelope[slopeIndex] -
                        envelope[valleyIndex];

                    double slopeScore =
                        Math.Clamp(
                            slope * 4.0,
                            0.0,
                            1.0);

                    /*
                     * Composite confidence.
                     *
                     * Attack is intentionally weighted slightly more than
                     * absolute quietness. Otherwise a silent gap can win the
                     * phase search even though no beat follows it.
                     */
                    double confidence =
                        valleyScore * 0.40 +
                        attackScore * 0.45 +
                        slopeScore * 0.15;

                    /*
                     * Ignore observations that have neither a valley nor a
                     * meaningful attack. They should not dominate the phase.
                     */
                    if (attackScore >= 0.05)
                    {
                        totalScore += confidence;
                        observations++;
                    }
                }

                if (observations == 0)
                {
                    continue;
                }

                /*
                 * Normalize by number of actual observations.
                 */
                // Penalize candidate phases that only match a few transients;
                // genuine beat phase should find an attack after most valleys.
                double score = totalScore / Math.Max(1, candidateCount);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestPhase = phase;
                }
            }

            return bestPhase;
        }

        private readonly record struct BeatAnalysis(float[] Envelope, float[] Onset);
        private readonly record struct VirtualTrim(int StartFrame, int EndFrame);
    }
}
