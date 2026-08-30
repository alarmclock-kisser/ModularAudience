using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MathNet.Numerics.IntegralTransforms;
using ModularAudience.Audio;

namespace ModularAudience.Audio.Processing
{
    public static class DropManager
    {
        private const double InterleaveSpacingSeconds = 0.25;
        private const double AnnounceAdvanceSeconds = 0.125;

        public enum DropType
        {
            // AlignedAll: All drops are aligned to the same point in time across all audio tracks.
            AlignedAll = 0,
            // InterleavedAll: Drops are interleaved across all audio tracks, creating a staggered effect.
            InterleavedAll = 1,
            // LoudestSolo: The beats will align and fade in / out to only play the loudest drop.
            LoudestSolo = 2,
            // AnnouncingMostEnergetic: The stronger tracks drop is announced by the weaker track(s).
            AnnouncingMostEnergetic = 3,

        }

        // Peak detection mode handled via bool parameter in GetDropOffsetAsync: peakLocalMax (true=local maxima, false=strongest)

        private static double SamplesToSeconds(AudioObj audio, int interleavedSampleIndex)
        {
            int sampleRate = Math.Max(1, audio.SampleRate);
            int channels = Math.Max(1, audio.Channels <= 0 ? 1 : audio.Channels);
            return interleavedSampleIndex / (double) channels / sampleRate;
        }

        private static int SecondsToSamples(AudioObj audio, double seconds)
        {
            int sampleRate = Math.Max(1, audio.SampleRate);
            int channels = Math.Max(1, audio.Channels <= 0 ? 1 : audio.Channels);
            return Math.Max(0, (int) Math.Round(Math.Max(0.0, seconds) * sampleRate) * channels);
        }

        private static int PickClosestToSeconds(AudioObj audio, IReadOnlyDictionary<int, float> candidates, double targetSeconds, int fallback)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return Math.Max(0, fallback);
            }

            return candidates.Keys
                .OrderBy(sample => Math.Abs(SamplesToSeconds(audio, sample) - targetSeconds))
                .First();
        }

        public async static Task<Dictionary<Guid, int>> TimeManageDropsAsync(IEnumerable<AudioObj> audios, DropType dropType = 0, bool monoProcessing = true, int? maxWorkers = null)
        {
            var list = audios?
                .Where(audio => audio != null && audio.Data != null && audio.Data.Length > 0 && audio.SampleRate > 0)
                .DistinctBy(audio => audio.Id)
                .ToList() ?? [];

            if (list.Count == 0)
            {
                LogCollection.Log("No audio objects provided for drop management.");
                return [];
            }

            Dictionary<Guid, int> timings = dropType switch
            {
                DropType.AlignedAll => await AlignAllDropsAsync(list, monoProcessing, maxWorkers).ConfigureAwait(false),
                DropType.InterleavedAll => await InterleaveAllDropsAsync(list, monoProcessing, maxWorkers).ConfigureAwait(false),
                DropType.LoudestSolo => await LoudestSoloDropAsync(list, monoProcessing, maxWorkers).ConfigureAwait(false),
                DropType.AnnouncingMostEnergetic => await AnnounceMostEnergeticDropAsync(list, monoProcessing, maxWorkers).ConfigureAwait(false),
                _ => throw new ArgumentOutOfRangeException(nameof(dropType), "Invalid drop type specified.")
            };

            return timings;
        }

        private static async Task<Dictionary<Guid, int>> AlignAllDropsAsync(IEnumerable<AudioObj> audios, bool monoProcessing, int? maxWorkers)
        {
            var list = audios?.ToList() ?? [];
            var result = new Dictionary<Guid, int>();
            if (list.Count == 0)
            {
                return result;
            }

            // get candidates for all audios in parallel
            var tasks = list.Select(a => GetDropOffsetAsync(a, 0.5f, monoProcessing, 1, true, maxWorkers)).ToArray();
            await Task.WhenAll(tasks).ConfigureAwait(false);

            // best candidate per audio
            var bestPer = new Dictionary<AudioObj, (int Offset, float Conf)>();
            for (int i = 0; i < list.Count; i++)
            {
                var a = list[i];
                var dict = tasks[i].Result;
                if (dict != null && dict.Count > 0)
                {
                    var best = dict.OrderByDescending(kv => kv.Value).First();
                    bestPer[a] = (best.Key, best.Value);
                }
                else
                {
                    bestPer[a] = (0, 0f);
                }
            }

            // choose global anchor: the candidate with highest confidence, measured in seconds
            var anchorPair = bestPer.OrderByDescending(kv => kv.Value.Conf).First();
            double anchorSeconds = SamplesToSeconds(anchorPair.Key, anchorPair.Value.Offset);

            // for each audio pick the candidate closest to anchor
            for (int i = 0; i < list.Count; i++)
            {
                var a = list[i];
                var dict = tasks[i].Result;
                int chosen = bestPer[a].Offset;
                if (dict != null && dict.Count > 0)
                {
                    // choose candidate closest to the anchor time, independent of sample-rate/channel count
                    chosen = PickClosestToSeconds(a, dict, anchorSeconds, chosen);
                }
                result[a.Id] = Math.Max(0, chosen);
            }

            return result;
        }

        private static async Task<Dictionary<Guid, int>> InterleaveAllDropsAsync(IEnumerable<AudioObj> audios, bool monoProcessing, int? maxWorkers)
        {
            var list = audios?.ToList() ?? [];
            var result = new Dictionary<Guid, int>();
            if (list.Count == 0)
            {
                return result;
            }

            // compute candidates
            var tasks = list.Select(a => GetDropOffsetAsync(a, 0.5f, monoProcessing, 1, true, maxWorkers)).ToArray();
            await Task.WhenAll(tasks).ConfigureAwait(false);

            // choose anchor as earliest best candidate among all
            var bestPer = new List<(AudioObj A, int Offset, float Conf)>();
            for (int i = 0; i < list.Count; i++)
            {
                var a = list[i];
                var dict = tasks[i].Result;
                if (dict != null && dict.Count > 0)
                {
                    var best = dict.OrderByDescending(kv => kv.Value).First();
                    bestPer.Add((a, best.Key, best.Value));
                }
                else
                {
                    bestPer.Add((a, 0, 0f));
                }
            }

            // anchor: choose the strongest candidate among all, measured in seconds
            var anchorItem = bestPer.OrderByDescending(x => x.Conf).First();
            double anchorSeconds = SamplesToSeconds(anchorItem.A, anchorItem.Offset);

            // order audios by strength descending to stagger stronger first
            var ordered = bestPer.OrderByDescending(x => x.Conf).ToArray();
            for (int i = 0; i < ordered.Length; i++)
            {
                var entry = ordered[i];
                double targetSeconds = anchorSeconds + i * InterleaveSpacingSeconds;
                var dict = tasks[list.IndexOf(entry.A)].Result;
                int chosen = entry.Offset;
                if (dict != null && dict.Count > 0)
                {
                    chosen = PickClosestToSeconds(entry.A, dict, targetSeconds, chosen);
                }
                result[entry.A.Id] = Math.Max(0, chosen);
            }

            return result;
        }

        private static async Task<Dictionary<Guid, int>> LoudestSoloDropAsync(IEnumerable<AudioObj> audios, bool monoProcessing, int? maxWorkers)
        {
            var list = audios?.ToList() ?? [];
            var result = new Dictionary<Guid, int>();
            if (list.Count == 0)
            {
                return result;
            }

            // get candidates and peak amplitudes
            var candidateTasks = list.Select(a => GetDropOffsetAsync(a, 0.5f, monoProcessing, 1, true, maxWorkers)).ToArray();
            var peakTasks = list.Select(a => a.GetPeakAmplitudeAsync(maxWorkers ?? Environment.ProcessorCount)).ToArray();
            var all = candidateTasks.Cast<Task>().Concat(peakTasks.Cast<Task>()).ToArray();
            await Task.WhenAll(all).ConfigureAwait(false);

            // combine confidence * amplitude to pick loudest energetic drop
            var bestPer = new List<(AudioObj A, int Offset, float Conf, float Peak)>();
            for (int i = 0; i < list.Count; i++)
            {
                var a = list[i];
                var dict = candidateTasks[i].Result;
                float peak = 0f;
                try { peak = peakTasks[i].Result; } catch { peak = 0f; }
                if (dict != null && dict.Count > 0)
                {
                    var best = dict.OrderByDescending(kv => kv.Value).First();
                    bestPer.Add((a, best.Key, best.Value, peak));
                }
                else
                {
                    bestPer.Add((a, 0, 0f, peak));
                }
            }

            var chosen = bestPer.OrderByDescending(x => x.Conf * (x.Peak + 1e-6f)).First();
            double anchorSeconds = SamplesToSeconds(chosen.A, chosen.Offset);

            // align all to anchor (others may need nearest candidate)
            for (int i = 0; i < list.Count; i++)
            {
                var a = list[i];
                var dict = candidateTasks[i].Result;
                int pick = chosen.Offset;
                if (dict != null && dict.Count > 0)
                {
                    pick = PickClosestToSeconds(a, dict, anchorSeconds, pick);
                }
                result[a.Id] = Math.Max(0, pick);
            }

            return result;
        }

        private static async Task<Dictionary<Guid, int>> AnnounceMostEnergeticDropAsync(IEnumerable<AudioObj> audios, bool monoProcessing, int? maxWorkers)
        {
            var list = audios?.ToList() ?? [];
            var result = new Dictionary<Guid, int>();
            if (list.Count == 0)
            {
                return result;
            }

            var candidateTasks = list.Select(a => GetDropOffsetAsync(a, 0.5f, monoProcessing, 1, true, maxWorkers)).ToArray();
            var peakTasks = list.Select(a => a.GetPeakAmplitudeAsync(maxWorkers ?? Environment.ProcessorCount)).ToArray();
            var all = candidateTasks.Cast<Task>().Concat(peakTasks.Cast<Task>()).ToArray();
            await Task.WhenAll(all).ConfigureAwait(false);

            var bestPer = new List<(AudioObj A, int Offset, float Conf, float Peak)>();
            for (int i = 0; i < list.Count; i++)
            {
                var a = list[i];
                var dict = candidateTasks[i].Result;
                float peak = 0f;
                try { peak = peakTasks[i].Result; } catch { peak = 0f; }
                if (dict != null && dict.Count > 0)
                {
                    var best = dict.OrderByDescending(kv => kv.Value).First();
                    bestPer.Add((a, best.Key, best.Value, peak));
                }
                else
                {
                    bestPer.Add((a, 0, 0f, peak));
                }
            }

            // primary anchor: most energetic (conf * peak)
            var primary = bestPer.OrderByDescending(x => x.Conf * (x.Peak + 1e-6f)).First();
            double anchorSeconds = SamplesToSeconds(primary.A, primary.Offset);

            foreach (var p in bestPer.OrderByDescending(x => x.Conf * (x.Peak + 1e-6f)))
            {
                if (p.A.Id == primary.A.Id)
                {
                    result[p.A.Id] = Math.Max(0, primary.Offset);
                    continue;
                }

                var dict = candidateTasks[list.IndexOf(p.A)].Result;
                int chosen = p.Offset;
                // prefer candidate slightly before anchor if available
                if (dict != null && dict.Count > 0)
                {
                    double targetSeconds = Math.Max(0.0, anchorSeconds - AnnounceAdvanceSeconds);
                    var before = dict.Keys
                        .Where(k => SamplesToSeconds(p.A, k) <= targetSeconds)
                        .OrderByDescending(k => SamplesToSeconds(p.A, k))
                        .ToArray();
                    if (before.Length > 0)
                    {
                        chosen = before[0];
                    }
                    else
                    {
                        chosen = PickClosestToSeconds(p.A, dict, anchorSeconds, chosen);
                    }
                }

                result[p.A.Id] = Math.Max(0, chosen);
            }

            return result;
        }



        public static async Task<Dictionary<int, float>> GetDropOffsetAsync(
            AudioObj audio,
            float threshold = 0.5f,
            bool mono = true,
            int bands = 1,
            bool peakLocalMax = true,
            int? maxWorkers = null)
        {
            if (audio == null || audio.Data == null || audio.Data.Length == 0 || audio.SampleRate <= 0)
            {
                return [];
            }

            // clamp threshold
            threshold = Math.Clamp(threshold, 0f, 1f);

            int workers = Math.Clamp(maxWorkers ?? Environment.ProcessorCount, 1, Environment.ProcessorCount);

            // run heavy work off the UI thread
            return await Task.Run(async () =>
            {
                // Trim silence at start/end using shared silence trimmer (returns indices in audio.Data units)
                int maxW = Math.Max(1, maxWorkers ?? Environment.ProcessorCount);
                var (trimStart, trimEnd) = await audio.TrimSilenceAsync(null, maxW).ConfigureAwait(false);

                float[] fullMono = await audio.GetMonoSamplesAsync(set: false).ConfigureAwait(false);

                int sampleRate = Math.Max(1, audio.SampleRate);
                int channels = Math.Max(1, audio.Channels <= 0 ? 1 : audio.Channels);

                int startMono = (int) Math.Clamp(trimStart / Math.Max(1, channels), 0, fullMono.Length);
                int endMono = (int) Math.Clamp(trimEnd / Math.Max(1, channels), 0, fullMono.Length);
                if (endMono <= startMono) { startMono = 0; endMono = fullMono.Length; }

                // build monoSamples slice
                int monoLen = endMono - startMono;
                if (monoLen <= 0)
                {
                    return [];
                }

                var monoSamples = new float[monoLen];
                Array.Copy(fullMono, startMono, monoSamples, 0, monoLen);

                int frameSize = 2048;
                int hop = Math.Max(64, frameSize / 4);

                if (monoSamples == null || monoSamples.Length < frameSize)
                {
                    return [];
                }

                int totalFrames = 1 + Math.Max(0, (monoSamples.Length - frameSize) / hop);

                var window = HannWindow(frameSize);

                var po = new ParallelOptions { MaxDegreeOfParallelism = workers };

                // energy per band per frame
                int bandCount = Math.Max(1, bands);
                var bandEnergies = new float[bandCount][];
                for (int b = 0; b < bandCount; b++)
                {
                    bandEnergies[b] = new float[totalFrames];
                }

                Parallel.For(0, totalFrames, po, fi =>
                {
                    int start = fi * hop;
                    var buffer = new Complex[frameSize];
                    for (int i = 0; i < frameSize; i++)
                    {
                        buffer[i] = new Complex(monoSamples[start + i] * window[i], 0.0);
                    }

                    Fourier.Forward(buffer, FourierOptions.Matlab);

                    int nyquistBin = frameSize / 2;
                    // if single band and we want lowband bias, use up to 200Hz
                    if (bandCount == 1)
                    {
                        int maxBin = Math.Min(nyquistBin, (int) (200.0 * frameSize / sampleRate));
                        double sum = 0.0;
                        for (int b = 0; b <= maxBin; b++)
                        {
                            double m = buffer[b].Magnitude;
                            sum += m * m;
                        }
                        bandEnergies[0][fi] = (float) sum;
                    }
                    else
                    {
                        // split full spectrum into equal-width bands (linear)
                        for (int bi = 0; bi < bandCount; bi++)
                        {
                            int startBin = (int) ((bi / (double) bandCount) * nyquistBin);
                            int endBin = (int) (((bi + 1) / (double) bandCount) * nyquistBin);
                            startBin = Math.Clamp(startBin, 0, nyquistBin);
                            endBin = Math.Clamp(endBin, startBin, nyquistBin);
                            double sum = 0.0;
                            for (int b = startBin; b <= endBin; b++)
                            {
                                double m = buffer[b].Magnitude;
                                sum += m * m;
                            }
                            bandEnergies[bi][fi] = (float) sum;
                        }
                    }
                });

                // compute per-band positive flux and combine
                var combinedFlux = new float[totalFrames];
                for (int bi = 0; bi < bandCount; bi++)
                {
                    var be = bandEnergies[bi];
                    var flux = new float[totalFrames];
                    for (int i = 1; i < totalFrames; i++)
                    {
                        float d = be[i] - be[i - 1];
                        flux[i] = d > 0f ? d : 0f;
                    }
                    // optional weight: favor lower bands slightly
                    float weight = 1f / (1f + (bi * 0.25f));
                    for (int i = 0; i < totalFrames; i++)
                    {
                        combinedFlux[i] += flux[i] * weight;
                    }
                }

                combinedFlux = MovingAverage(combinedFlux, 3);

                // prepare detection thresholds
                float maxFlux = combinedFlux.Max();
                float detectThr = threshold * maxFlux;

                // If BPM available, bias flux toward beat-aligned frames
                float bpm = audio.Bpm > 0f ? audio.Bpm : audio.ScannedBpm;
                if (bpm > 0f && totalFrames > 0)
                {
                    double samplesPerBeat = sampleRate * (60.0 / bpm);
                    double beatTol = Math.Max(1.0, samplesPerBeat * 0.08); // ~8% of beat or at least 1 sample
                    for (int i = 0; i < totalFrames; i++)
                    {
                        // center position in monoSamples relative to overall audio: add startMono
                        double centerMono = startMono + (i * hop + frameSize / 2);
                        // distance to nearest beat (phase relative to startMono)
                        double phase = (centerMono) % samplesPerBeat;
                        double d = Math.Min(phase, samplesPerBeat - phase);
                        if (d <= beatTol)
                        {
                            double factor = 1.0 + 0.6 * (1.0 - d / beatTol); // up to +60% boost
                            combinedFlux[i] = (float) (combinedFlux[i] * factor);
                        }
                    }
                    // re-normalize slightly
                    float nf = combinedFlux.Max();
                    if (nf > 0f && nf != 1f)
                    {
                        for (int i = 0; i < combinedFlux.Length; i++)
                        {
                            combinedFlux[i] /= nf;
                        }
                        // scale back to maxFlux reference
                        maxFlux = combinedFlux.Max();
                        detectThr = threshold * maxFlux;
                    }
                }
                if (maxFlux <= 1e-12f)
                {
                    return [];
                }

                var peaks = new List<(int Frame, float Value)>();
                if (peakLocalMax)
                {
                    for (int i = 1; i < totalFrames - 1; i++)
                    {
                        if (combinedFlux[i] > combinedFlux[i - 1] && combinedFlux[i] >= combinedFlux[i + 1] && combinedFlux[i] >= detectThr)
                        {
                            peaks.Add((i, combinedFlux[i]));
                        }
                    }
                }
                else // Strongest
                {
                    var ordered = combinedFlux.Select((v, idx) => (idx, v)).OrderByDescending(t => t.v).Where(t => t.v >= detectThr).ToArray();
                    peaks.AddRange(ordered.Select(t => (t.idx, t.v)));
                }

                if (peaks.Count == 0 && threshold <= 0f)
                {
                    var ordered = combinedFlux.Select((v, idx) => (idx, v)).OrderByDescending(t => t.v).Take(8);
                    peaks.AddRange(ordered.Select(t => (t.idx, t.v)));
                }

                // Convert peaks to sample indices and enforce minimum spacing
                int minSpacingSamples = (int) (sampleRate * 0.25);
                var timeOrdered = peaks.OrderBy(p => p.Frame).ToList();
                var candidates = new List<(int Sample, float Value)>();
                int lastMonoSample = -minSpacingSamples * 2;
                foreach (var p in timeOrdered)
                {
                    int centerMono = p.Frame * hop + frameSize / 2;
                    int originalMonoSample = startMono + centerMono;
                    if (originalMonoSample - lastMonoSample < minSpacingSamples)
                    {
                        if (candidates.Count > 0)
                        {
                            var prev = candidates[^1];
                            // compare values
                            if (p.Value > prev.Value)
                            {
                                candidates[^1] = (originalMonoSample * channels, p.Value);
                                lastMonoSample = originalMonoSample;
                            }
                        }
                        continue;
                    }
                    candidates.Add((originalMonoSample * channels, p.Value));
                    lastMonoSample = originalMonoSample;
                }

                // Keep strongest candidates, then return them in time order. Previous code used
                // time-order Take(maxKeep), which accidentally discarded later stronger drops.
                int maxKeep = Math.Max(1, (int) Math.Ceiling((1.0 - threshold) * 32));
                var final = candidates
                    .GroupBy(c => c.Sample)
                    .Select(g => g.OrderByDescending(c => c.Value).First())
                    .OrderByDescending(c => c.Value)
                    .Take(maxKeep)
                    .OrderBy(c => c.Sample)
                    .ToArray();

                // build dictionary offset->confidence (0..1)
                var dict = new Dictionary<int, float>();
                foreach (var c in final)
                {
                    float conf = Math.Clamp(c.Value / maxFlux, 0f, 1f);
                    dict[c.Sample] = conf;
                }

                return dict;
            }).ConfigureAwait(false);
        }

        private static float[] HannWindow(int n)
        {
            var w = new float[n];
            for (int i = 0; i < n; i++)
            {
                w[i] = 0.5f * (1f - (float) Math.Cos(2.0 * Math.PI * i / (n - 1)));
            }
            return w;
        }

        private static float[] MovingAverage(float[] data, int win)
        {
            if (data == null || data.Length == 0 || win <= 1)
            {
                return data ?? [];
            }

            var outArr = new float[data.Length];
            int half = win / 2;
            for (int i = 0; i < data.Length; i++)
            {
                int s = Math.Max(0, i - half);
                int e = Math.Min(data.Length - 1, i + half);
                double sum = 0.0;
                for (int j = s; j <= e; j++)
                {
                    sum += data[j];
                }

                outArr[i] = (float) (sum / (e - s + 1));
            }
            return outArr;
        }


    }
}
