namespace ModularAudience.Audio.Processors_V4
{
    public static class LoopAtomizer_V4
    {
        public static async Task<LoopAtomizerResult> AtomizeAsync(AudioObj source, LoopAtomizerSettings? settings = null)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            settings ??= LoopAtomizerSettings.Default;
            AtomizeAnalysis analysis = await Task.Run(() => AnalyzeAtomicSegments(source, settings)).ConfigureAwait(false);
            List<AudioObj> atomics = await CreateAtomicSamplesAsync(source, analysis).ConfigureAwait(false);
            return new LoopAtomizerResult(atomics, analysis.IsLikelyDrumLoop);
        }

        private static async Task<List<AudioObj>> CreateAtomicSamplesAsync(AudioObj source, AtomizeAnalysis analysis)
        {
            if (analysis.Segments.Count == 0)
            {
                return [];
            }

            AudioObj working = await source.CloneAsync().ConfigureAwait(false);
            List<AudioObj> atomics = new(analysis.Segments.Count);

            try
            {
                int channels = Math.Max(1, working.Channels);
                for (int i = 0; i < analysis.Segments.Count; i++)
                {
                    AtomicSegment segment = analysis.Segments[i];
                    long startSample = (long) segment.StartFrame * channels;
                    long endSample = (long) segment.EndFrame * channels;
                    if (endSample <= startSample)
                    {
                        continue;
                    }

                    working.SelectionStart = startSample;
                    working.SelectionEnd = endSample;
                    AudioObj? atomic = await working.CloneFromSelectionAsync().ConfigureAwait(false);
                    if (atomic == null)
                    {
                        continue;
                    }

                    string baseName = string.IsNullOrWhiteSpace(source.Name) ? "Audio" : source.Name.Trim();
                    string suffix = string.IsNullOrWhiteSpace(segment.Label) ? string.Empty : $"_{segment.Label}";
                    atomic.Rename($"{baseName}_Atomic{i + 1:D3}{suffix}");
                    atomic.FilePath = source.FilePath;
                    atomic.SampleTag = analysis.IsLikelyDrumLoop && !string.IsNullOrWhiteSpace(segment.Label) ? segment.Label : string.Empty;
                    atomic.Tag = analysis.IsLikelyDrumLoop ? segment.Label : null;
                    atomic.CustomTags["AtomizeConfidence"] = segment.Confidence.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
                    atomics.Add(atomic);
                }
            }
            finally
            {
                working.Dispose();
            }

            return atomics;
        }

        private static AtomizeAnalysis AnalyzeAtomicSegments(AudioObj source, LoopAtomizerSettings settings)
        {
            if (source.Data == null || source.Data.Length == 0 || source.SampleRate <= 0 || source.Channels <= 0)
            {
                return new AtomizeAnalysis([], false);
            }

            float[] mono = ConvertToMono(source.Data, source.Channels);
            if (mono.Length <= 1)
            {
                return new AtomizeAnalysis([], false);
            }

            float[] envelope = BuildEnvelope(mono, source.SampleRate, settings);
            float[] fastEnvelope = BuildFastEnvelope(mono, source.SampleRate, settings);
            float floor = EstimatePercentile(envelope, 0.18);
            float loud = EstimatePercentile(envelope, 0.92);
            float silenceThreshold = Math.Max(0.00035f, floor + ((loud - floor) * 0.07f));

            List<int> onsets = DetectOnsets(mono, envelope, fastEnvelope, source.SampleRate, silenceThreshold, settings);
            List<AtomicSegment> segments = BuildAtomicSegments(mono, envelope, onsets, source.SampleRate, silenceThreshold, settings);
            if (segments.Count == 0 && settings.AllowSingleAtomFallback)
            {
                int first = FindFirstActiveFrame(envelope, silenceThreshold * 1.10f);
                int last = FindLastActiveFrame(envelope, silenceThreshold * 1.10f);
                if (first >= 0 && last > first)
                {
                    segments = [new AtomicSegment(first, last + 1, null, 0.0)];
                }
            }

            if (segments.Count == 0)
            {
                return new AtomizeAnalysis([], false);
            }

            List<AtomicSegment> classified = ClassifyAtomicSegments(mono, segments, source.SampleRate);
            int classifiedCount = classified.Count(segment => !string.IsNullOrWhiteSpace(segment.Label));
            bool isLikelyDrumLoop = classified.Count >= 3 && classifiedCount >= Math.Max(2, (int) Math.Ceiling(classified.Count * 0.5));

            if (!isLikelyDrumLoop)
            {
                classified = classified.Select(segment => segment with { Label = null, Confidence = 0.0 }).ToList();
            }

            return new AtomizeAnalysis(classified, isLikelyDrumLoop);
        }

        private static float[] ConvertToMono(float[] data, int channels)
        {
            channels = Math.Max(1, channels);
            int frames = data.Length / channels;
            float[] mono = new float[frames];

            for (int frame = 0; frame < frames; frame++)
            {
                int offset = frame * channels;
                float sum = 0f;
                for (int channel = 0; channel < channels; channel++)
                {
                    sum += data[offset + channel];
                }

                mono[frame] = sum / channels;
            }

            return mono;
        }

        private static float[] BuildEnvelope(float[] mono, int sampleRate, LoopAtomizerSettings settings)
        {
            int window = settings.Sensitivity switch
            {
                AtomizeSensitivity.Conservative => Math.Clamp(sampleRate / 520, 20, 320),
                AtomizeSensitivity.Aggressive => Math.Clamp(sampleRate / 980, 8, 160),
                _ => Math.Clamp(sampleRate / 760, 12, 240)
            };

            return BuildMovingAverageEnvelope(mono, window);
        }

        private static float[] BuildFastEnvelope(float[] mono, int sampleRate, LoopAtomizerSettings settings)
        {
            int window = settings.Sensitivity switch
            {
                AtomizeSensitivity.Conservative => Math.Clamp(sampleRate / 1500, 4, 48),
                AtomizeSensitivity.Aggressive => Math.Clamp(sampleRate / 2400, 2, 24),
                _ => Math.Clamp(sampleRate / 2000, 3, 32)
            };

            return BuildMovingAverageEnvelope(mono, window);
        }

        private static float[] BuildMovingAverageEnvelope(float[] mono, int window)
        {
            window = Math.Max(1, window);
            float[] envelope = new float[mono.Length];
            double accumulator = 0.0;
            Queue<float> queue = new(window);

            for (int i = 0; i < mono.Length; i++)
            {
                float absolute = Math.Abs(mono[i]);
                queue.Enqueue(absolute);
                accumulator += absolute;
                if (queue.Count > window)
                {
                    accumulator -= queue.Dequeue();
                }

                envelope[i] = (float) (accumulator / queue.Count);
            }

            return envelope;
        }

        private static float EstimatePercentile(float[] values, double percentile)
        {
            if (values.Length == 0)
            {
                return 0f;
            }

            float[] sorted = (float[]) values.Clone();
            Array.Sort(sorted);
            int index = (int) Math.Round(Math.Clamp(percentile, 0.0, 1.0) * (sorted.Length - 1));
            return sorted[index];
        }

        private static List<int> DetectOnsets(float[] mono, float[] envelope, float[] fastEnvelope, int sampleRate, float silenceThreshold, LoopAtomizerSettings settings)
        {
            int hopSize = Math.Clamp(sampleRate / 280, 24, 256);
            int frameCount = Math.Max(1, 1 + ((mono.Length - 1) / hopSize));
            float[] novelty = new float[frameCount];

            for (int frame = 1; frame < frameCount; frame++)
            {
                int index = Math.Min(mono.Length - 1, frame * hopSize);
                int previous = Math.Max(0, index - hopSize);
                float fastRise = Math.Max(0f, fastEnvelope[index] - envelope[index]);
                float envRise = Math.Max(0f, envelope[index] - envelope[previous]);
                float slope = Math.Abs(mono[index] - mono[Math.Max(0, index - 1)]);
                novelty[frame] = (fastRise * 0.52f) + (envRise * 0.33f) + (slope * 0.15f);
            }

            NormalizeInPlace(novelty);
            novelty = Smooth(novelty, 2);

            int localRadius = settings.Sensitivity switch
            {
                AtomizeSensitivity.Conservative => 12,
                AtomizeSensitivity.Aggressive => 5,
                _ => 8
            };
            int minPeakDistanceSamples = Math.Max((settings.MinSliceMs * sampleRate) / 1000, settings.Sensitivity switch
            {
                AtomizeSensitivity.Conservative => sampleRate / 9,
                AtomizeSensitivity.Aggressive => sampleRate / 24,
                _ => sampleRate / 14
            });
            double stdMultiplier = settings.Sensitivity switch
            {
                AtomizeSensitivity.Conservative => 1.15,
                AtomizeSensitivity.Aggressive => 0.58,
                _ => 0.82
            };
            float noveltyFloor = settings.Sensitivity switch
            {
                AtomizeSensitivity.Conservative => 0.10f,
                AtomizeSensitivity.Aggressive => 0.035f,
                _ => 0.06f
            };

            List<int> onsets = [];
            int lastAcceptedOnset = int.MinValue;

            for (int frame = 1; frame < novelty.Length - 1; frame++)
            {
                if (novelty[frame] < novelty[frame - 1] || novelty[frame] < novelty[frame + 1])
                {
                    continue;
                }

                int localStart = Math.Max(0, frame - localRadius);
                int localEnd = Math.Min(novelty.Length - 1, frame + localRadius);
                double localMean = 0.0;
                for (int i = localStart; i <= localEnd; i++)
                {
                    localMean += novelty[i];
                }

                int count = localEnd - localStart + 1;
                localMean /= Math.Max(1, count);
                double localVariance = 0.0;
                for (int i = localStart; i <= localEnd; i++)
                {
                    double diff = novelty[i] - localMean;
                    localVariance += diff * diff;
                }

                double localStd = Math.Sqrt(localVariance / Math.Max(1, count));
                double threshold = Math.Max(noveltyFloor, localMean + (localStd * stdMultiplier));
                if (novelty[frame] < threshold)
                {
                    continue;
                }

                int approximateOnset = Math.Min(mono.Length - 1, frame * hopSize);
                int refinedOnset = RefineOnset(mono, envelope, fastEnvelope, approximateOnset, sampleRate, settings);
                if (envelope[refinedOnset] < silenceThreshold * 0.92f)
                {
                    continue;
                }

                if (refinedOnset - lastAcceptedOnset < minPeakDistanceSamples)
                {
                    if (onsets.Count > 0 && envelope[refinedOnset] > envelope[onsets[^1]])
                    {
                        onsets[^1] = refinedOnset;
                        lastAcceptedOnset = refinedOnset;
                    }

                    continue;
                }

                onsets.Add(refinedOnset);
                lastAcceptedOnset = refinedOnset;
            }

            foreach (int backupPeak in DetectEnvelopePeaks(envelope, sampleRate, silenceThreshold, settings))
            {
                if (onsets.Count == 0 || onsets.All(existing => Math.Abs(existing - backupPeak) >= minPeakDistanceSamples))
                {
                    onsets.Add(backupPeak);
                }
            }

            int firstActive = FindFirstActiveFrame(envelope, silenceThreshold * 1.20f);
            if (firstActive >= 0 && onsets.Count > 0 && onsets[0] - firstActive > minPeakDistanceSamples)
            {
                onsets.Insert(0, firstActive);
            }

            return ConsolidateOnsets(onsets, envelope, minPeakDistanceSamples);
        }

        private static List<int> DetectEnvelopePeaks(float[] envelope, int sampleRate, float silenceThreshold, LoopAtomizerSettings settings)
        {
            int minDistance = Math.Max((settings.MinSliceMs * sampleRate) / 1000, sampleRate / 30);
            int radius = Math.Clamp(sampleRate / 180, 12, 180);
            float prominenceScale = settings.Sensitivity switch
            {
                AtomizeSensitivity.Conservative => 1.55f,
                AtomizeSensitivity.Aggressive => 1.08f,
                _ => 1.25f
            };
            List<int> peaks = [];

            for (int i = radius; i < envelope.Length - radius; i++)
            {
                float current = envelope[i];
                if (current < silenceThreshold * prominenceScale)
                {
                    continue;
                }

                bool isPeak = true;
                float localMin = current;
                for (int j = i - radius; j <= i + radius; j++)
                {
                    if (envelope[j] > current)
                    {
                        isPeak = false;
                        break;
                    }

                    localMin = Math.Min(localMin, envelope[j]);
                }

                if (!isPeak)
                {
                    continue;
                }

                if ((current - localMin) < silenceThreshold * 0.75f)
                {
                    continue;
                }

                if (peaks.Count > 0 && i - peaks[^1] < minDistance)
                {
                    if (envelope[i] > envelope[peaks[^1]])
                    {
                        peaks[^1] = i;
                    }

                    continue;
                }

                peaks.Add(i);
            }

            return peaks;
        }

        private static List<int> ConsolidateOnsets(IEnumerable<int> onsets, float[] envelope, int minPeakDistanceSamples)
        {
            List<int> ordered = onsets.Distinct().OrderBy(x => x).ToList();
            if (ordered.Count <= 1)
            {
                return ordered;
            }

            List<int> consolidated = [ordered[0]];
            for (int i = 1; i < ordered.Count; i++)
            {
                int current = ordered[i];
                int previous = consolidated[^1];
                if (current - previous < minPeakDistanceSamples)
                {
                    if (envelope[current] > envelope[previous])
                    {
                        consolidated[^1] = current;
                    }

                    continue;
                }

                consolidated.Add(current);
            }

            return consolidated;
        }

        private static void NormalizeInPlace(float[] values)
        {
            float max = values.Length == 0 ? 0f : values.Max();
            if (max <= 0f)
            {
                return;
            }

            for (int i = 0; i < values.Length; i++)
            {
                values[i] /= max;
            }
        }

        private static float[] Smooth(float[] values, int radius)
        {
            if (values.Length == 0 || radius <= 0)
            {
                return values;
            }

            float[] smoothed = new float[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                int start = Math.Max(0, i - radius);
                int end = Math.Min(values.Length - 1, i + radius);
                double sum = 0.0;
                for (int j = start; j <= end; j++)
                {
                    sum += values[j];
                }

                smoothed[i] = (float) (sum / (end - start + 1));
            }

            return smoothed;
        }

        private static int RefineOnset(float[] mono, float[] envelope, float[] fastEnvelope, int approximateOnset, int sampleRate, LoopAtomizerSettings settings)
        {
            int searchRadius = settings.Sensitivity switch
            {
                AtomizeSensitivity.Conservative => Math.Max(24, sampleRate / 180),
                AtomizeSensitivity.Aggressive => Math.Max(10, sampleRate / 260),
                _ => Math.Max(16, sampleRate / 220)
            };
            int searchStart = Math.Max(1, approximateOnset - searchRadius);
            int searchEnd = Math.Min(mono.Length - 1, approximateOnset + searchRadius);
            int bestIndex = approximateOnset;
            float bestScore = float.MinValue;

            for (int i = searchStart; i <= searchEnd; i++)
            {
                float attack = fastEnvelope[i] - envelope[Math.Max(0, i - 1)];
                float slope = Math.Abs(mono[i] - mono[i - 1]);
                float score = (attack * 0.68f) + (slope * 0.20f) + (envelope[i] * 0.12f);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }

            int backtrack = Math.Max(10, sampleRate / 120);
            int earliest = Math.Max(0, bestIndex - backtrack);
            int boundary = bestIndex;
            float bestValue = envelope[bestIndex];
            for (int i = bestIndex; i >= earliest; i--)
            {
                float value = envelope[i] + (Math.Abs(mono[i]) * 0.12f);
                if (value <= bestValue)
                {
                    bestValue = value;
                    boundary = i;
                }
            }

            return SnapToZeroCrossing(mono, boundary, Math.Max(12, sampleRate / 250));
        }

        private static List<AtomicSegment> BuildAtomicSegments(float[] mono, float[] envelope, List<int> onsets, int sampleRate, float silenceThreshold, LoopAtomizerSettings settings)
        {
            if (onsets.Count == 0)
            {
                return [];
            }

            onsets = onsets.OrderBy(x => x).ToList();
            int minFrames = Math.Max((settings.MinSliceMs * sampleRate) / 1000, sampleRate / 50);
            int tailBiasFrames = Math.Max(0, (settings.TailPaddingMs * sampleRate) / 1000);
            int[] boundaries = new int[onsets.Count + 1];
            boundaries[0] = FindLeadingBoundary(mono, envelope, onsets[0], sampleRate, silenceThreshold, settings);

            for (int i = 1; i < onsets.Count; i++)
            {
                boundaries[i] = FindBoundaryBetween(mono, envelope, onsets[i - 1], onsets[i], sampleRate, silenceThreshold, tailBiasFrames, settings);
            }

            boundaries[^1] = FindTrailingBoundary(mono, envelope, onsets[^1], sampleRate, silenceThreshold, settings);

            List<AtomicSegment> segments = [];
            for (int i = 0; i < onsets.Count; i++)
            {
                int start = Math.Clamp(boundaries[i], 0, mono.Length - 1);
                int end = Math.Clamp(boundaries[i + 1], start + 1, mono.Length);
                if ((end - start) < minFrames)
                {
                    if (segments.Count > 0)
                    {
                        AtomicSegment previous = segments[^1];
                        segments[^1] = previous with { EndFrame = end };
                    }

                    continue;
                }

                if (!ContainsMeaningfulEnergy(envelope, start, end, silenceThreshold))
                {
                    continue;
                }

                segments.Add(new AtomicSegment(start, end, null, 0.0));
            }

            return MergeTinySegments(segments, envelope, sampleRate, silenceThreshold, mono.Length, settings);
        }

        private static int FindLeadingBoundary(float[] mono, float[] envelope, int firstOnset, int sampleRate, float silenceThreshold, LoopAtomizerSettings settings)
        {
            int lookBehind = settings.Sensitivity switch
            {
                AtomizeSensitivity.Conservative => Math.Min(firstOnset, sampleRate / 3),
                AtomizeSensitivity.Aggressive => Math.Min(firstOnset, sampleRate / 10),
                _ => Math.Min(firstOnset, sampleRate / 6)
            };
            int searchStart = Math.Max(0, firstOnset - lookBehind);
            int preGuard = Math.Max(6, sampleRate / 500);
            int searchEnd = Math.Max(searchStart, firstOnset - preGuard);
            int bestIndex = searchStart;
            float bestScore = float.MaxValue;

            for (int i = searchStart; i <= searchEnd; i++)
            {
                float score = envelope[i] + (Math.Abs(mono[i]) * 0.20f) - (((float) (i - searchStart) / Math.Max(1, searchEnd - searchStart + 1)) * 0.02f);
                if (envelope[i] <= silenceThreshold)
                {
                    score *= 0.60f;
                }

                if (score < bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }

            return SnapToZeroCrossing(mono, bestIndex, Math.Max(12, sampleRate / 250));
        }

        private static int FindBoundaryBetween(float[] mono, float[] envelope, int leftOnset, int rightOnset, int sampleRate, float silenceThreshold, int tailBiasFrames, LoopAtomizerSettings settings)
        {
            int gap = Math.Max(1, rightOnset - leftOnset);
            int guardAfterLeft = Math.Min(Math.Max(sampleRate / 160, 12), gap / 3);
            int guardBeforeRight = Math.Min(Math.Max(sampleRate / 220, 10), gap / 3);
            int searchStart = Math.Clamp(leftOnset + guardAfterLeft, 0, rightOnset - 1);
            int searchEnd = Math.Clamp(rightOnset - guardBeforeRight, searchStart + 1, envelope.Length - 1);
            if (searchEnd <= searchStart)
            {
                return SnapToZeroCrossing(mono, (leftOnset + rightOnset) / 2, Math.Max(12, sampleRate / 250));
            }

            int bestIndex = searchStart;
            float bestScore = float.MaxValue;
            for (int i = searchStart; i <= searchEnd; i++)
            {
                float normalizedPosition = (float) (i - searchStart) / Math.Max(1, searchEnd - searchStart);
                float slope = i > 0 ? Math.Abs(envelope[i] - envelope[i - 1]) : envelope[i];
                float score = envelope[i] + (slope * 0.22f) - (normalizedPosition * (tailBiasFrames / (float) Math.Max(1, gap)) * 0.12f);
                if (envelope[i] <= silenceThreshold)
                {
                    score *= 0.55f;
                }

                if (score < bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }

            int shifted = Math.Min(searchEnd, bestIndex + Math.Min(tailBiasFrames, Math.Max(0, searchEnd - bestIndex)));
            return SnapToZeroCrossing(mono, shifted, Math.Max(12, sampleRate / 250));
        }

        private static int FindTrailingBoundary(float[] mono, float[] envelope, int lastOnset, int sampleRate, float silenceThreshold, LoopAtomizerSettings settings)
        {
            int maxTail = settings.Sensitivity switch
            {
                AtomizeSensitivity.Conservative => sampleRate * 2,
                AtomizeSensitivity.Aggressive => sampleRate,
                _ => sampleRate + (sampleRate / 2)
            };
            int minTail = Math.Max((settings.MinSliceMs * sampleRate) / 1000, sampleRate / 30);
            int searchStart = Math.Clamp(lastOnset + Math.Max(4, sampleRate / 500), 0, envelope.Length - 1);
            int searchEnd = Math.Clamp(lastOnset + maxTail, searchStart + 1, envelope.Length - 1);
            int releaseThresholdIndex = -1;
            float peak = 0f;
            for (int i = lastOnset; i <= searchEnd; i++)
            {
                peak = Math.Max(peak, envelope[i]);
                if (i - lastOnset < minTail)
                {
                    continue;
                }

                float decayThreshold = Math.Max(silenceThreshold * 1.15f, peak * 0.16f);
                if (envelope[i] <= decayThreshold)
                {
                    releaseThresholdIndex = i;
                    break;
                }
            }

            int candidate = releaseThresholdIndex >= 0 ? releaseThresholdIndex : searchEnd;
            return SnapToZeroCrossing(mono, candidate, Math.Max(12, sampleRate / 250));
        }

        private static int SnapToZeroCrossing(float[] mono, int index, int radius)
        {
            if (mono.Length == 0)
            {
                return 0;
            }

            index = Math.Clamp(index, 0, mono.Length - 1);
            int bestIndex = index;
            float bestAmplitude = Math.Abs(mono[index]);

            for (int distance = 0; distance <= radius; distance++)
            {
                int left = index - distance;
                if (left > 0)
                {
                    if (mono[left] == 0f || (mono[left] >= 0f && mono[left - 1] < 0f) || (mono[left] <= 0f && mono[left - 1] > 0f))
                    {
                        return left;
                    }

                    float amplitude = Math.Abs(mono[left]);
                    if (amplitude < bestAmplitude)
                    {
                        bestAmplitude = amplitude;
                        bestIndex = left;
                    }
                }

                int right = index + distance;
                if (right > 0 && right < mono.Length)
                {
                    if (mono[right] == 0f || (mono[right] >= 0f && mono[right - 1] < 0f) || (mono[right] <= 0f && mono[right - 1] > 0f))
                    {
                        return right;
                    }

                    float amplitude = Math.Abs(mono[right]);
                    if (amplitude < bestAmplitude)
                    {
                        bestAmplitude = amplitude;
                        bestIndex = right;
                    }
                }
            }

            return bestIndex;
        }

        private static List<AtomicSegment> MergeTinySegments(List<AtomicSegment> segments, float[] envelope, int sampleRate, float silenceThreshold, int totalFrames, LoopAtomizerSettings settings)
        {
            int minFrames = Math.Max((settings.MinSliceMs * sampleRate) / 1000, sampleRate / 60);
            List<AtomicSegment> merged = [];

            foreach (AtomicSegment segment in segments)
            {
                int start = Math.Clamp(segment.StartFrame, 0, totalFrames);
                int end = Math.Clamp(segment.EndFrame, start + 1, totalFrames);
                AtomicSegment normalized = new(start, end, null, 0.0);

                if (!ContainsMeaningfulEnergy(envelope, normalized.StartFrame, normalized.EndFrame, silenceThreshold))
                {
                    continue;
                }

                if (merged.Count == 0)
                {
                    merged.Add(normalized);
                    continue;
                }

                if ((normalized.EndFrame - normalized.StartFrame) < minFrames)
                {
                    AtomicSegment previous = merged[^1];
                    merged[^1] = previous with { EndFrame = normalized.EndFrame };
                    continue;
                }

                merged.Add(normalized);
            }

            return merged;
        }

        private static bool ContainsMeaningfulEnergy(float[] envelope, int startFrame, int endFrame, float silenceThreshold)
        {
            float peak = 0f;
            for (int i = startFrame; i < endFrame; i++)
            {
                if (envelope[i] > peak)
                {
                    peak = envelope[i];
                }
            }

            return peak >= silenceThreshold * 1.35f;
        }

        private static int FindFirstActiveFrame(float[] envelope, float threshold)
        {
            for (int i = 0; i < envelope.Length; i++)
            {
                if (envelope[i] >= threshold)
                {
                    return i;
                }
            }

            return -1;
        }

        private static int FindLastActiveFrame(float[] envelope, float threshold)
        {
            for (int i = envelope.Length - 1; i >= 0; i--)
            {
                if (envelope[i] >= threshold)
                {
                    return i;
                }
            }

            return -1;
        }

        private static List<AtomicSegment> ClassifyAtomicSegments(float[] mono, List<AtomicSegment> segments, int sampleRate)
        {
            List<AtomicSegment> classified = new(segments.Count);
            foreach (AtomicSegment segment in segments)
            {
                AtomicHitFeatures features = ExtractHitFeatures(mono, segment.StartFrame, segment.EndFrame, sampleRate);
                (string? label, double confidence) = ClassifyHit(features);
                classified.Add(segment with { Label = label, Confidence = confidence });
            }

            return classified;
        }

        private static AtomicHitFeatures ExtractHitFeatures(float[] mono, int startFrame, int endFrame, int sampleRate)
        {
            int length = Math.Max(1, endFrame - startFrame);
            double totalEnergy = 0.0;
            double peak = 0.0;
            int zeroCrossings = 0;
            double lowState = 0.0;
            double lowEnergy = 0.0;
            double highEnergy = 0.0;
            double dt = 1.0 / Math.Max(1, sampleRate);
            double rc = 1.0 / (2.0 * Math.PI * 320.0);
            double alpha = dt / (rc + dt);

            for (int i = startFrame; i < endFrame; i++)
            {
                double sample = mono[i];
                double abs = Math.Abs(sample);
                peak = Math.Max(peak, abs);
                totalEnergy += sample * sample;

                lowState += alpha * (sample - lowState);
                lowEnergy += lowState * lowState;
                double high = sample - lowState;
                highEnergy += high * high;

                if (i > startFrame)
                {
                    bool crossed = (mono[i - 1] >= 0f && mono[i] < 0f) || (mono[i - 1] < 0f && mono[i] >= 0f);
                    if (crossed)
                    {
                        zeroCrossings++;
                    }
                }
            }

            double rms = Math.Sqrt(totalEnergy / length);
            int tailStart = startFrame + (int) Math.Round(length * 0.65);
            double tailEnergy = 0.0;
            for (int i = tailStart; i < endFrame; i++)
            {
                double sample = mono[i];
                tailEnergy += sample * sample;
            }

            double spectralEnergy = Math.Max(1e-9, lowEnergy + highEnergy);
            double durationMs = length * 1000.0 / Math.Max(1, sampleRate);
            int earlyWindow = Math.Min(length - 2, Math.Max(8, (int) Math.Round(sampleRate * 0.12)));
            int earlyPeakCount = 0;
            double peakThreshold = peak * 0.60;
            int minimumPeakSpacing = Math.Max(1, sampleRate / 250);
            int lastPeakIndex = int.MinValue;

            for (int local = 1; local <= earlyWindow; local++)
            {
                int index = startFrame + local;
                if (index >= endFrame - 1)
                {
                    break;
                }

                double current = Math.Abs(mono[index]);
                if (current < peakThreshold)
                {
                    continue;
                }

                double previous = Math.Abs(mono[index - 1]);
                double next = Math.Abs(mono[index + 1]);
                if (current >= previous && current >= next && index - lastPeakIndex >= minimumPeakSpacing)
                {
                    earlyPeakCount++;
                    lastPeakIndex = index;
                }
            }

            return new AtomicHitFeatures(
                DurationMs: durationMs,
                Peak: peak,
                Rms: rms,
                LowEnergyRatio: lowEnergy / spectralEnergy,
                HighEnergyRatio: highEnergy / spectralEnergy,
                ZeroCrossingRate: zeroCrossings / (double) Math.Max(1, length - 1),
                TailEnergyRatio: tailEnergy / Math.Max(1e-9, totalEnergy),
                CrestFactor: peak / Math.Max(1e-9, rms),
                EarlyPeakCount: earlyPeakCount);
        }

        private static (string? Label, double Confidence) ClassifyHit(AtomicHitFeatures features)
        {
            if (features.Peak < 0.015 || features.Rms < 0.004)
            {
                return (null, 0.0);
            }

            if (features.LowEnergyRatio > 0.68 && features.ZeroCrossingRate < 0.18 && features.DurationMs <= 600)
            {
                return ("Kick", 0.90);
            }

            if (features.EarlyPeakCount >= 3 && features.DurationMs >= 70 && features.DurationMs <= 300 && features.HighEnergyRatio > 0.45)
            {
                return ("Clap", 0.84);
            }

            if (features.HighEnergyRatio > 0.82 && features.DurationMs < 90 && features.CrestFactor > 5.5 && features.TailEnergyRatio < 0.18)
            {
                return ("Rim", 0.78);
            }

            if (features.HighEnergyRatio > 0.80 && features.DurationMs < 160)
            {
                return ("HiHatClosed", 0.82);
            }

            if (features.HighEnergyRatio > 0.72 && features.DurationMs >= 160 && features.DurationMs <= 550)
            {
                return ("HiHatOpen", 0.75);
            }

            if (features.HighEnergyRatio > 0.72 && (features.DurationMs > 550 || features.TailEnergyRatio > 0.52))
            {
                return ("CrashLong", 0.76);
            }

            if (features.HighEnergyRatio > 0.68 && features.DurationMs >= 220 && features.DurationMs <= 550)
            {
                return ("CrashShort", 0.67);
            }

            if (features.LowEnergyRatio > 0.56 && features.DurationMs >= 140)
            {
                if (features.ZeroCrossingRate < 0.10)
                {
                    return ("FloorTom", 0.69);
                }

                if (features.ZeroCrossingRate < 0.14)
                {
                    return ("TomLow", 0.67);
                }

                if (features.ZeroCrossingRate < 0.18)
                {
                    return ("TomMid", 0.65);
                }

                return ("TomHigh", 0.62);
            }

            if (features.HighEnergyRatio > 0.50 && features.DurationMs <= 420)
            {
                if (features.TailEnergyRatio > 0.42)
                {
                    return ("Snare", 0.73);
                }

                return ("SnareRattle", 0.64);
            }

            if (features.HighEnergyRatio > 0.70 && features.EarlyPeakCount >= 4 && features.DurationMs < 260)
            {
                return ("Shaker", 0.58);
            }

            return (null, 0.0);
        }

        private sealed record AtomizeAnalysis(IReadOnlyList<AtomicSegment> Segments, bool IsLikelyDrumLoop);

        private readonly record struct AtomicSegment(int StartFrame, int EndFrame, string? Label, double Confidence);

        private readonly record struct AtomicHitFeatures(
            double DurationMs,
            double Peak,
            double Rms,
            double LowEnergyRatio,
            double HighEnergyRatio,
            double ZeroCrossingRate,
            double TailEnergyRatio,
            double CrestFactor,
            int EarlyPeakCount);
    }

    public enum AtomizeSensitivity
    {
        Conservative,
        Balanced,
        Aggressive
    }

    public sealed record LoopAtomizerSettings
    {
        public static LoopAtomizerSettings Default { get; } = new();
        public AtomizeSensitivity Sensitivity { get; init; } = AtomizeSensitivity.Balanced;
        public int MinSliceMs { get; init; } = 80;
        public int TailPaddingMs { get; init; } = 30;
        public bool AllowSingleAtomFallback { get; init; }
    }

    public sealed record LoopAtomizerResult(IReadOnlyList<AudioObj> Atomics, bool IsLikelyDrumLoop);
}
