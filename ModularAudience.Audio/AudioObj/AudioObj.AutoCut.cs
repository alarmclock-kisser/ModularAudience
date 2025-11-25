using ModularAudience.Audio;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ModularAudience.Audio
{
    public partial class AudioObj
    {
        /// <summary>
        /// Automatically cuts the audio into atomic samples based on silence detection and amplitude thresholding.
        /// Splits continuous regions longer than maxDurationMs into multiple sub-samples.
        /// </summary>
        /// <param name="threshold">Minimum amplitude threshold for sample detection (0.0-1.0).</param>
        /// <param name="minDurationMs">Minimum duration in milliseconds for a valid sample.</param>
        /// <param name="maxDurationMs">Maximum duration in milliseconds for a valid sample.</param>
        /// <param name="silenceWindowMs">Silence window duration in milliseconds for detecting gaps between samples.</param>
        /// <param name="mergeSimilarThreshold">Optional: merge samples with similar RMS within this threshold (0.0-1.0). Null disables merging.</param>
        /// <param name="onePaletteLoop">If true, creates a single AudioObj with all samples separated by silence. If false, creates individual AudioObj instances.</param>
        /// <returns>List of newly created AudioObj instances.</returns>
        public async Task<List<AudioObj>> AutoCutAsync(
            float threshold = 0.001f,
            int minDurationMs = 50,
            int maxDurationMs = 650,
            int silenceWindowMs = 180,
            float? mergeSimilarThreshold = null,
            bool onePaletteLoop = false)
        {
            return await Task.Run(() => this.AutoCutInternal(threshold, minDurationMs, maxDurationMs, silenceWindowMs, mergeSimilarThreshold, onePaletteLoop));
        }

        private List<AudioObj> AutoCutInternal(
            float threshold,
            int minDurationMs,
            int maxDurationMs,
            int silenceWindowMs,
            float? mergeSimilarThreshold,
            bool onePaletteLoop)
        {
            if (this.Data == null || this.Data.Length == 0 || this.SampleRate <= 0 || this.Channels <= 0)
            {
                return [];
            }

            // Detect atomic sample regions
            var regions = this.DetectSampleRegions(threshold, minDurationMs, maxDurationMs, silenceWindowMs);

            if (regions.Count == 0)
            {
                LogCollection.Log("No valid samples detected.");
                return [];
            }

            // Optional: merge similar consecutive samples
            if (mergeSimilarThreshold.HasValue && mergeSimilarThreshold.Value > 0f)
            {
                regions = this.MergeSimilarRegions(regions, mergeSimilarThreshold.Value);
            }

            var results = new List<AudioObj>();

            if (onePaletteLoop)
            {
                // Create single AudioObj with all samples separated by silence gaps
                var paletteAudio = this.CreatePaletteLoop(regions, silenceWindowMs);
                if (paletteAudio != null)
                {
                    results.Add(paletteAudio);
                }
            }
            else
            {
                // Create individual AudioObj for each region
                for (int i = 0; i < regions.Count; i++)
                {
                    var region = regions[i];
                    var sample = this.ExtractRegion(region.Start, region.End, $"{this.Name}_Sample{i + 1:D3}");
                    if (sample != null)
                    {
                        results.Add(sample);
                    }
                }
            }

            LogCollection.Log($"AutoCut: Created {results.Count} audio object(s) from {regions.Count} detected regions.");
            return results;
        }

        private List<SampleRegion> DetectSampleRegions(float threshold, int minDurationMs, int maxDurationMs, int silenceWindowMs)
        {
            var regions = new List<SampleRegion>();
            if (this.Data == null || this.Data.Length == 0)
            {
                return regions;
            }

            int minDurationSamples = (int) (minDurationMs * this.SampleRate * this.Channels / 1000.0);
            int maxDurationSamples = (int) (maxDurationMs * this.SampleRate * this.Channels / 1000.0);
            int silenceWindowSamples = (int) (silenceWindowMs * this.SampleRate * this.Channels / 1000.0);

            bool inSample = false;
            int sampleStart = 0;
            int silenceCounter = 0;

            for (int i = 0; i < this.Data.Length; i++)
            {
                float absValue = Math.Abs(this.Data[i]);

                if (absValue >= threshold)
                {
                    if (!inSample)
                    {
                        // Start new sample
                        sampleStart = i;
                        inSample = true;
                        silenceCounter = 0;
                    }
                    else
                    {
                        // Continue sample, reset silence counter
                        silenceCounter = 0; // reset silence gap while active
                        int currentLen = i - sampleStart;
                        // Split overly long continuous regions proactively
                        if (currentLen >= maxDurationSamples)
                        {
                            int segmentEnd = sampleStart + maxDurationSamples;
                            int segmentLen = segmentEnd - sampleStart;
                            if (segmentLen >= minDurationSamples)
                            {
                                regions.Add(new SampleRegion { Start = sampleStart, End = segmentEnd });
                            }
                            // start next segment at current position to avoid losing continuity
                            sampleStart = segmentEnd; // move start forward
                        }
                    }
                }
                else if (inSample)
                {
                    silenceCounter++;
                    if (silenceCounter >= silenceWindowSamples)
                    {
                        // End of sample detected
                        int sampleEnd = i - silenceCounter; // exclude trailing silence
                        int sampleLength = sampleEnd - sampleStart;

                        if (sampleLength >= minDurationSamples)
                        {
                            // If region still exceeds maxDuration split it
                            if (sampleLength > maxDurationSamples)
                            {
                                int start = sampleStart;
                                while (start + maxDurationSamples <= sampleEnd)
                                {
                                    regions.Add(new SampleRegion { Start = start, End = start + maxDurationSamples });
                                    start += maxDurationSamples;
                                }
                                if (sampleEnd - start >= minDurationSamples && sampleEnd - start <= maxDurationSamples)
                                {
                                    regions.Add(new SampleRegion { Start = start, End = sampleEnd });
                                }
                            }
                            else if (sampleLength <= maxDurationSamples)
                            {
                                regions.Add(new SampleRegion { Start = sampleStart, End = sampleEnd });
                            }
                        }

                        inSample = false;
                        silenceCounter = 0;
                    }
                }
            }

            // Handle case where sample extends to end of audio
            if (inSample)
            {
                int sampleEnd = this.Data.Length;
                int sampleLength = sampleEnd - sampleStart;
                if (sampleLength >= minDurationSamples)
                {
                    if (sampleLength > maxDurationSamples)
                    {
                        int start = sampleStart;
                        while (start + maxDurationSamples <= sampleEnd)
                        {
                            regions.Add(new SampleRegion { Start = start, End = start + maxDurationSamples });
                            start += maxDurationSamples;
                        }
                        if (sampleEnd - start >= minDurationSamples && sampleEnd - start <= maxDurationSamples)
                        {
                            regions.Add(new SampleRegion { Start = start, End = sampleEnd });
                        }
                    }
                    else
                    {
                        regions.Add(new SampleRegion { Start = sampleStart, End = sampleEnd });
                    }
                }
            }

            return regions;
        }

        private List<SampleRegion> MergeSimilarRegions(List<SampleRegion> regions, float threshold)
        {
            if (regions.Count <= 1 || this.Data == null)
            {
                return regions;
            }

            var merged = new List<SampleRegion>();
            var current = regions[0];

            for (int i = 1; i < regions.Count; i++)
            {
                var next = regions[i];
                float currentRms = this.CalculateRms(current.Start, current.End);
                float nextRms = this.CalculateRms(next.Start, next.End);
                float rmsDiff = Math.Abs(currentRms - nextRms);

                if (rmsDiff <= threshold)
                {
                    // Merge: extend current region to include next
                    current.End = next.End;
                }
                else
                {
                    // No merge: add current and move to next
                    merged.Add(current);
                    current = next;
                }
            }

            // Add final region
            merged.Add(current);
            return merged;
        }

        private float CalculateRms(int startSample, int endSample)
        {
            if (this.Data == null || startSample >= endSample || startSample < 0 || endSample > this.Data.Length)
            {
                return 0f;
            }

            double sumSquares = 0.0;
            int count = endSample - startSample;

            for (int i = startSample; i < endSample; i++)
            {
                float val = this.Data[i];
                sumSquares += val * val;
            }

            return (float) Math.Sqrt(sumSquares / count);
        }

        private AudioObj? ExtractRegion(int startSample, int endSample, string name)
        {
            if (this.Data == null || startSample >= endSample || startSample < 0 || endSample > this.Data.Length)
            {
                return null;
            }

            int length = endSample - startSample;
            var extractedData = new float[length];
            Array.Copy(this.Data, startSample, extractedData, 0, length);

            var extracted = new AudioObj
            {
                Name = name,
                FilePath = this.FilePath,
                Data = extractedData,
                SampleRate = this.SampleRate,
                Channels = this.Channels,
                BitDepth = this.BitDepth,
                Length = length,
                Duration = TimeSpan.FromSeconds(length / (double) (this.SampleRate * this.Channels)),
                Bpm = this.Bpm,
                Volume = this.Volume
            };

            return extracted;
        }

        private AudioObj? CreatePaletteLoop(List<SampleRegion> regions, int silenceGapMs)
        {
            if (regions.Count == 0 || this.Data == null)
            {
                return null;
            }

            int silenceGapSamples = (int) (silenceGapMs * this.SampleRate * this.Channels / 1000.0);
            int totalLength = regions.Sum(r => r.End - r.Start) + Math.Max(0, (regions.Count - 1) * silenceGapSamples);
            var paletteData = new float[totalLength];

            int writePos = 0;
            for (int i = 0; i < regions.Count; i++)
            {
                var region = regions[i];
                int regionLength = region.End - region.Start;
                Array.Copy(this.Data, region.Start, paletteData, writePos, regionLength);
                writePos += regionLength;

                // Add silence gap between samples (except after last sample)
                if (i < regions.Count - 1)
                {
                    Array.Clear(paletteData, writePos, silenceGapSamples);
                    writePos += silenceGapSamples;
                }
            }

            var palette = new AudioObj
            {
                Name = $"{this.Name}_Palette",
                FilePath = this.FilePath,
                Data = paletteData,
                SampleRate = this.SampleRate,
                Channels = this.Channels,
                BitDepth = this.BitDepth,
                Length = totalLength,
                Duration = TimeSpan.FromSeconds(totalLength / (double) (this.SampleRate * this.Channels)),
                Bpm = this.Bpm,
                Volume = this.Volume
            };

            return palette;
        }

        private struct SampleRegion
        {
            public int Start;
            public int End;
        }
    }
}
