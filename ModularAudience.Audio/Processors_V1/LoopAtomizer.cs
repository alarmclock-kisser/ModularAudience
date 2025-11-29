using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace ModularAudience.Audio.Processors_V1
{
    public static class LoopAtomizer
    {
        // Public entrypoint - signature unchanged
        public static Task<IEnumerable<AudioObj>> AtomizeLoopAsync(AudioObj audioObj, float? similarityThreshold = null)
        {
            // run on threadpool, returns task - caller API unchanged
            return Task.Run(() => AtomizeInternal(audioObj, similarityThreshold));
        }

        // Internal pipeline - everything automatic
        private static IEnumerable<AudioObj> AtomizeInternal(AudioObj audioObj, float? similarityHint)
        {
            if (audioObj == null)
            {
                throw new ArgumentNullException(nameof(audioObj));
            }

            var interleaved = audioObj.Data ?? Array.Empty<float>();
            int channels = Math.Max(1, audioObj.Channels);
            int sr = Math.Max(1, audioObj.SampleRate);
            if (interleaved.Length == 0)
            {
                return Array.Empty<AudioObj>();
            }

            // 1) Analysis: mono mix
            var mono = ToMono(interleaved, channels);

            // 2) Derive adaptive parameters from content (and detect jungle mode)
            var ap = DeriveAdaptiveParams(mono, sr, similarityHint, out bool jungleMode);

            // 3) Onset detection (energy + spectral flux blended)
            var onsets = DetectOnsets(mono, sr, ap);

            // 4) Jungle densification: if jungleMode, add micro-onsets
            if (jungleMode)
            {
                var extras = DensifyOnsetsForJungle(mono, sr, onsets, ap);
                onsets = SortedUnique(onsets.Concat(extras));
            }

            // 5) Build raw candidate segments centered on onsets
            var rawSegments = BuildCandidateSegments(onsets, mono.Length, sr, ap);

            // 6) Smart merge & split to avoid overfragmentation (and adapt to jungleMode)
            var merged = MergeAndSplitSmart(rawSegments, mono, sr, ap, jungleMode);

            // 7) Post-processing: hi-hat merging, trimming silence, zero-cross alignment
            var refined = PostProcessSegments(merged, mono, sr, ap, jungleMode);

            // 8) Ensure fallback single atom if nothing sensible found
            if (refined.Count == 0)
            {
                refined.Add((0, mono.Length));
            }

            // 9) Extract candidates preserving channels and metadata
            var candidates = refined.Select(seg => ExtractMultiChannelSlice(interleaved, channels, seg.start, seg.end, sr, audioObj.Name)).ToList();

            // 10) If only one candidate: isolate/trim and return
            if (candidates.Count <= 1)
            {
                var list = new List<AudioObj>();
                foreach (var c in candidates)
                {
                    list.Add(IsolateAndFinalize(c, sr));
                }

                return list;
            }

            // 11) Fingerprint & clustering
            var fps = candidates.Select(c => ComputeSpectralFingerprint(c.Data, c.Channels, sr, bins: ap.FingerprintBins)).ToList();

            float clusterThreshold = ap.ClusterSimilarityThreshold;
            if (similarityHint.HasValue)
            {
                clusterThreshold = Math.Clamp(similarityHint.Value, 0.6f, 0.98f);
            }
            else
            {
                clusterThreshold = DetermineAdaptiveClusterThreshold(fps, clusterThreshold);
            }

            var clusters = GreedyClusterBySimilarity(fps, clusterThreshold);

            // 12) Choose representative (highest peak-rms) per cluster and finalize
            var representatives = new List<AudioObj>();
            for (int ci = 0; ci < clusters.Count; ci++)
            {
                var idxs = clusters[ci];
                int bestIdx = idxs[0];
                double bestScore = -1;
                foreach (var id in idxs)
                {
                    double score = ComputePeakRms(candidates[id].Data, candidates[id].Channels);
                    if (score > bestScore) { bestScore = score; bestIdx = id; }
                }
                var rep = IsolateAndFinalize(candidates[bestIdx], sr);
                rep.Name = $"{(string.IsNullOrWhiteSpace(audioObj.Name) ? "atom" : audioObj.Name)}_atom_{ci + 1:D2}";
                representatives.Add(rep);
            }

            return representatives;
        }

        // ----------------------------
        // Adaptive params & heuristics (with Jungle detection)
        // ----------------------------
        private class AdaptiveParams
        {
            public int Window = 2048;
            public int Hop = 256;
            public int MinSpacingMs = 20;
            public int MinAtomMs = 30;
            public int MaxAtomMs = 2200;
            public bool UseSpectralFlux = true;
            public int FingerprintBins = 128;
            public float ClusterSimilarityThreshold = 0.88f;
            public int HiHatMergeMs = 60;
            public float HiHatCentroidHz = 3800f;
        }

        // Derives adaptive params and sets jungleMode if loop looks like amen/jungle (high transient density, fast tempo).
        private static AdaptiveParams DeriveAdaptiveParams(float[] mono, int sr, float? similarityHint, out bool jungleMode)
        {
            var p = new AdaptiveParams();
            jungleMode = false;

            // Quick energy / rms estimate
            double rms = 0; foreach (var v in mono)
            {
                rms += v * v;
            }

            rms = Math.Sqrt(rms / Math.Max(1, mono.Length));

            // If very short -> smaller windows
            if (mono.Length < sr * 2) { p.Window = 1024; p.Hop = 128; p.FingerprintBins = 64; }

            // coarse tempo & transient density estimation via energy autocorrelation and peak-count
            try
            {
                int win = Math.Clamp(mono.Length / 16, 256, 4096);
                int hop = Math.Max(64, win / 4);
                var energy = new List<double>();
                for (int i = 0; i + win < mono.Length; i += hop)
                {
                    double s = 0; for (int j = 0; j < win; j++) { double x = mono[i + j]; s += x * x; }
                    energy.Add(Math.Sqrt(s / win));
                }
                if (energy.Count > 8)
                {
                    // transient count
                    double medianE = Median(energy.ToArray());
                    int transients = energy.Count(x => x > medianE * 1.25);

                    // autocorr to estimate periodicity / tempo
                    int maxLag = Math.Min(120, energy.Count / 3);
                    double best = 0; int bestLag = 0;
                    for (int lag = 1; lag <= maxLag; lag++)
                    {
                        double corr = 0;
                        for (int k = 0; k + lag < energy.Count; k++)
                        {
                            corr += energy[k] * energy[k + lag];
                        }

                        if (corr > best) { best = corr; bestLag = lag; }
                    }

                    if (bestLag > 0)
                    {
                        double secondsPerBeat = (bestLag * hop) / (double) sr;
                        if (secondsPerBeat > 0)
                        {
                            double bpm = 60.0 / secondsPerBeat;
                            if (bpm > 150) { p.Window = 1024; p.Hop = 128; p.MinAtomMs = 18; p.HiHatMergeMs = 40; p.FingerprintBins = 64; }
                            else if (bpm > 100) { p.Window = 1536; p.Hop = 192; p.MinAtomMs = 24; p.HiHatMergeMs = 48; p.FingerprintBins = 80; }
                            else { p.Window = 2048; p.Hop = 256; p.MinAtomMs = 36; p.HiHatMergeMs = 60; p.FingerprintBins = 128; }
                        }

                        // Jungle heuristic: high transient density relative to energy frames indicates amen-like loop
                        double transientDensity = energy.Count > 0 ? transients / (double) energy.Count : 0.0;
                        if (transientDensity > 0.28 || (mono.Length < sr * 10 && transientDensity > 0.18))
                        {
                            // trigger jungle mode
                            jungleMode = true;
                            p.MinAtomMs = Math.Max(8, p.MinAtomMs / 2);
                            p.HiHatMergeMs = Math.Max(20, p.HiHatMergeMs / 2);
                            p.FingerprintBins = Math.Min(64, p.FingerprintBins);
                        }
                    }
                }
            }
            catch { /* best-effort only */ }

            // If very quiet or noisy, disable flux to avoid false triggers
            if (rms < 1e-5)
            {
                p.UseSpectralFlux = false;
            }

            if (similarityHint.HasValue)
            {
                p.ClusterSimilarityThreshold = Math.Clamp(similarityHint.Value, 0.6f, 0.98f);
            }

            return p;
        }

        // ----------------------------
        // Onset detection
        // ----------------------------
        private static List<int> DetectOnsets(float[] mono, int sr, AdaptiveParams p)
        {
            int win = Math.Max(256, p.Window);
            int hop = Math.Max(64, p.Hop);
            int frames = Math.Max(1, 1 + (mono.Length - win) / hop);

            var energy = new double[frames];
            var flux = new double[frames];
            int fftN = 1; while (fftN < win)
            {
                fftN <<= 1;
            }

            var buf = new Complex[fftN];
            double[]? prevMag = null;
            var w = new double[win];
            for (int i = 0; i < win; i++)
            {
                w[i] = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (win - 1)));
            }

            for (int f = 0; f < frames; f++)
            {
                int pos = f * hop;
                double e = 0;
                for (int i = 0; i < win; i++)
                {
                    double s = (pos + i) < mono.Length ? mono[pos + i] * w[i] : 0.0;
                    e += s * s;
                    buf[i] = new Complex(s, 0.0);
                }
                for (int i = win; i < fftN; i++)
                {
                    buf[i] = Complex.Zero;
                }

                energy[f] = Math.Sqrt(e / Math.Max(1, win));

                if (p.UseSpectralFlux)
                {
                    Fourier.Forward(buf, FourierOptions.Matlab);
                    var mag = new double[fftN / 2];
                    for (int k = 0; k < mag.Length; k++)
                    {
                        mag[k] = buf[k].Magnitude;
                    }

                    if (prevMag != null)
                    {
                        double fl = 0;
                        int mlen = Math.Min(prevMag.Length, mag.Length);
                        for (int k = 0; k < mlen; k++)
                        {
                            double d = mag[k] - prevMag[k]; if (d > 0)
                            {
                                fl += d;
                            }
                        }
                        flux[f] = fl;
                    }
                    prevMag = mag;
                }
            }

            NormalizeInPlace(energy);
            if (p.UseSpectralFlux)
            {
                NormalizeInPlace(flux);
            }

            var smoothE = MedianFilter(energy, 3);
            var smoothF = p.UseSpectralFlux ? MedianFilter(flux, 3) : null;

            // Combine signals: energy dominates but flux helps with timbral transients
            var score = new double[frames];
            for (int i = 0; i < frames; i++)
            {
                double s = smoothE[i];
                if (p.UseSpectralFlux)
                {
                    s = 0.65 * s + 0.35 * smoothF![i];
                }

                score[i] = s;
            }

            // Peak picking - local maxima above adaptive threshold
            double medianScore = Median(score);
            double std = StdDev(score);
            double threshold = Math.Max(0.01, medianScore + 0.25 * std); // adaptive
            int minSpacingFrames = Math.Max(1, (int) Math.Round((p.MinSpacingMs / 1000.0) * sr / hop));
            var peaks = PickPeaksWithThreshold(score, neighbor: 2, minSpacingFrames, threshold);

            var onsets = peaks.Select(x => Math.Clamp(x * hop, 0, mono.Length - 1)).ToList();

            // If too few onsets, fallback to coarse partitioning (preserve musicality)
            if (onsets.Count < 2)
            {
                onsets = FallbackPartition(mono.Length, sr);
            }

            return onsets;
        }

        // ----------------------------
        // Jungle densification - produce extra micro-onsets for dense breaks
        // ----------------------------
        private static List<int> DensifyOnsetsForJungle(float[] mono, int sr, List<int> baseOnsets, AdaptiveParams ap)
        {
            var extra = new List<int>();

            // Use a smaller window to find micro peaks
            int win = Math.Clamp(ap.Window / 2, 256, 2048);
            int hop = Math.Max(32, win / 8);
            int frames = Math.Max(1, 1 + (mono.Length - win) / hop);
            var env = new double[frames];
            var w = new double[win];
            for (int i = 0; i < win; i++)
            {
                w[i] = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (win - 1)));
            }

            for (int f = 0; f < frames; f++)
            {
                int pos = f * hop;
                double s = 0;
                for (int i = 0; i < win; i++)
                {
                    s += Math.Abs((pos + i) < mono.Length ? mono[pos + i] * w[i] : 0.0);
                }

                env[f] = s / win;
            }
            NormalizeInPlace(env);

            for (int f = 1; f < frames - 1; f++)
            {
                if (env[f] > env[f - 1] && env[f] > env[f + 1] && env[f] > 0.30)
                {
                    int sample = Math.Clamp(f * hop, 0, mono.Length - 1);
                    extra.Add(sample);
                }
            }

            // Keep extras not too close to base onsets (e.g. > 8 ms)
            int minGap = (int) (0.008 * sr);
            var filtered = new List<int>();
            foreach (var s in extra)
            {
                bool near = baseOnsets.Any(o => Math.Abs(o - s) < minGap);
                if (!near)
                {
                    filtered.Add(s);
                }
            }
            return filtered;
        }

        // ----------------------------
        // Candidate segments
        // ----------------------------
        private static List<(int start, int end)> BuildCandidateSegments(List<int> onsets, int totalSamples, int sr, AdaptiveParams p)
        {
            var list = new List<(int start, int end)>();
            if (onsets.Count == 0)
            {
                return list;
            }

            // median inter-onset
            var diffs = new List<int>();
            for (int i = 1; i < onsets.Count; i++)
            {
                diffs.Add(onsets[i] - onsets[i - 1]);
            }

            int medianInterval = diffs.Count > 0 ? (int) Median(diffs.Select(d => (double) d).ToArray()) : Math.Max(1, totalSamples / Math.Max(1, onsets.Count));
            int atomLen = Math.Clamp((int) Math.Round(medianInterval * 1.05), (int) (p.MinAtomMs * sr / 1000.0), (int) (p.MaxAtomMs * sr / 1000.0));
            int half = Math.Max(1, atomLen / 2);

            foreach (var o in onsets)
            {
                int s = Math.Max(0, o - half);
                int e = Math.Min(totalSamples, o + half);
                // align to zero crossings to reduce clicks
                s = FindNearestZeroCrossingIndexSafe(s, -Math.Min(128, half), totalSamples);
                e = FindNearestZeroCrossingIndexSafe(e, Math.Min(128, half), totalSamples);
                if (e <= s)
                {
                    continue;
                }

                list.Add((s, e));
            }

            // Merge tiny overlaps simply here - deeper merging later
            return MergeOverlapping(list, toleranceSamples: Math.Max(1, (int) (0.01 * sr)));
        }

        // ----------------------------
        // Merge & split smart
        // ----------------------------
        private static List<(int start, int end)> MergeAndSplitSmart(List<(int start, int end)> segments, float[] mono, int sr, AdaptiveParams p, bool jungleMode)
        {
            if (segments == null || segments.Count == 0)
            {
                return new List<(int, int)>();
            }
            // Merge tight neighbors first
            var merged = MergeOverlapping(segments, toleranceSamples: Math.Max(1, (int) (0.01 * sr)));

            // Enforce minimum length; merge if too small; split very long segments at energy valleys
            var output = new List<(int start, int end)>();
            foreach (var seg in merged)
            {
                int len = seg.end - seg.start;
                int minSamps = (int) (p.MinAtomMs * sr / 1000.0);
                int maxSamps = (int) (p.MaxAtomMs * sr / 1000.0);

                if (len < Math.Max(1, minSamps / 2))
                {
                    // try to merge with previous or next
                    if (output.Count > 0)
                    {
                        var prev = output.Last();
                        output[output.Count - 1] = (prev.start, seg.end);
                        continue;
                    }
                    else
                    {
                        // keep but ensure min len by expanding
                        int expand = Math.Min(maxSamps, seg.start + minSamps) - seg.start;
                        int newEnd = Math.Min(mono.Length, seg.end + expand);
                        output.Add((seg.start, newEnd));
                        continue;
                    }
                }
                if (len > maxSamps)
                {
                    // split at internal valleys
                    var splits = SplitAtValleys(mono, seg.start, seg.end, sr, minSegmentSamples: minSamps);
                    output.AddRange(splits);
                }
                else
                {
                    output.Add(seg);
                }
            }

            // Jungle-mode grouping: optionally group very short neighbors into micro-phrases
            if (jungleMode && output.Count > 1)
            {
                var merged2 = new List<(int, int)>();
                int i = 0;
                while (i < output.Count)
                {
                    int s = output[i].start, e = output[i].end;
                    int j = i + 1;
                    while (j < output.Count && (output[j].end - s) <= (int) (0.15 * sr))
                    {
                        e = output[j].end; j++;
                    }
                    merged2.Add((s, e));
                    i = j;
                }
                return merged2;
            }

            return output;
        }

        // Split long segments by internal energy valleys
        private static List<(int start, int end)> SplitAtValleys(float[] mono, int s, int e, int sr, int minSegmentSamples)
        {
            var outList = new List<(int, int)>();
            int region = e - s;
            if (region <= minSegmentSamples) { outList.Add((s, e)); return outList; }

            int win = Math.Clamp(region / 12, 256, 2048);
            int hop = Math.Max(64, win / 4);
            var energies = new List<(int pos, double val)>();
            for (int pos = s; pos + win <= e; pos += hop)
            {
                double sum = 0;
                for (int i = 0; i < win; i++) { double v = mono[pos + i]; sum += v * v; }
                energies.Add((pos, Math.Sqrt(sum / win)));
            }
            if (energies.Count < 2) { outList.Add((s, e)); return outList; }

            var vals = energies.Select(x => x.val).ToArray();
            double med = Median(vals);
            var valleyPositions = new List<int>();
            for (int i = 1; i < energies.Count - 1; i++)
            {
                if (energies[i].val < med * 0.6 && energies[i].val <= energies[i - 1].val && energies[i].val <= energies[i + 1].val)
                {
                    valleyPositions.Add(energies[i].pos);
                }
            }

            if (valleyPositions.Count == 0) { outList.Add((s, e)); return outList; }

            // Cut at valleys but ensure min length
            int last = s;
            foreach (var v in valleyPositions)
            {
                if (v - last >= minSegmentSamples)
                {
                    outList.Add((last, v));
                    last = v;
                }
            }
            if (e - last >= minSegmentSamples)
            {
                outList.Add((last, e));
            }
            else
            {
                // append to last
                if (outList.Count > 0)
                {
                    var lastSeg = outList[outList.Count - 1];
                    outList[outList.Count - 1] = (lastSeg.Item1, e);
                }
                else
                {
                    outList.Add((s, e));
                }
            }
            return outList;
        }

        // ----------------------------
        // Post processing: hi-hat merging & trimming
        // ----------------------------
        private static List<(int start, int end)> PostProcessSegments(List<(int start, int end)> segments, float[] mono, int sr, AdaptiveParams p, bool jungleMode)
        {
            if (segments == null || segments.Count == 0)
            {
                return new List<(int, int)>();
            }
            // compute spectral centroid per segment
            var centroids = segments.Select(seg => ComputeSpectralCentroid(mono, seg.start, seg.end, sr)).ToArray();

            // Merge consecutive segments that look like hi-hats (high centroid) and are very close
            var merged = new List<(int start, int end)>();
            int i = 0;
            while (i < segments.Count)
            {
                int s = segments[i].start, e = segments[i].end;
                double c = centroids[i];
                int j = i + 1;
                while (j < segments.Count)
                {
                    int gap = segments[j].start - e;
                    if (gap <= (int) (p.HiHatMergeMs / 1000.0 * sr) && centroids[j] >= p.HiHatCentroidHz && c >= p.HiHatCentroidHz)
                    {
                        // merge
                        e = segments[j].end;
                        c = (c + centroids[j]) / 2.0;
                        j++;
                    }
                    else
                    {
                        break;
                    }
                }
                // Trim leading/trailing silence lightly
                (int ns, int ne) = TrimSilenceAround(mono, s, e, sr);
                ns = FindNearestZeroCrossingIndexSafe(ns, -Math.Min(256, ns), mono.Length);
                ne = FindNearestZeroCrossingIndexSafe(ne, Math.Min(256, mono.Length - ne), mono.Length);
                if (ne > ns)
                {
                    merged.Add((ns, ne));
                }

                i = j;
            }

            return merged;
        }

        // Trim low-energy edges (percentile-based)
        private static (int start, int end) TrimSilenceAround(float[] mono, int s, int e, int sr)
        {
            int len = e - s;
            if (len <= 4)
            {
                return (s, e);
            }

            int win = Math.Clamp(len / 16, 32, 1024);
            var env = new List<double>();
            for (int pos = s; pos + win <= e; pos += win) { double sum = 0; for (int i = 0; i < win; i++) { double v = mono[pos + i]; sum += Math.Abs(v); } env.Add(sum / win); }
            if (env.Count == 0)
            {
                return (s, e);
            }

            var sorted = env.ToArray(); Array.Sort(sorted);
            double threshold = sorted[Math.Clamp((int) (0.08 * sorted.Length), 0, sorted.Length - 1)] * 0.8 + 1e-8;

            int ns = s, ne = e;
            // find first env window above threshold
            int idx = 0;
            for (int i = 0; i < env.Count; i++)
            {
                if (env[i] >= threshold) { idx = i; break; }
            }
            ns = Math.Min(e, s + idx * win);
            // trailing
            int lastIdx = env.Count - 1;
            for (int i = env.Count - 1; i >= 0; i--)
            {
                if (env[i] >= threshold) { lastIdx = i; break; }
            }
            ne = Math.Min(e, s + (lastIdx + 1) * win);
            return (ns, Math.Max(ns + 1, ne));
        }

        // ----------------------------
        // Utility helpers
        // ----------------------------
        private static float[] ToMono(float[] interleaved, int channels)
        {
            if (channels <= 1)
            {
                return interleaved ?? Array.Empty<float>();
            }

            int frames = interleaved.Length / channels;
            var mono = new float[frames];
            for (int f = 0; f < frames; f++)
            {
                double s = 0;
                for (int c = 0; c < channels; c++)
                {
                    s += interleaved[f * channels + c];
                }

                mono[f] = (float) (s / channels);
            }
            return mono;
        }

        private static void NormalizeInPlace(double[] arr)
        {
            double mx = arr.Max();
            double mn = arr.Min();
            double span = mx - mn;
            if (span <= 1e-9)
            {
                return;
            }

            for (int i = 0; i < arr.Length; i++)
            {
                arr[i] = (arr[i] - mn) / span;
            }
        }

        private static double Median(double[] values)
        {
            if (values == null || values.Length == 0)
            {
                return 0.0;
            }

            var copy = (double[]) values.Clone(); Array.Sort(copy);
            int m = copy.Length / 2;
            return (copy.Length % 2 == 0) ? ((copy[m - 1] + copy[m]) / 2.0) : copy[m];
        }

        private static double StdDev(double[] arr)
        {
            if (arr == null || arr.Length == 0)
            {
                return 0;
            }

            double mean = arr.Average();
            double s = 0; foreach (var v in arr)
            {
                s += (v - mean) * (v - mean);
            }

            return Math.Sqrt(s / arr.Length);
        }

        private static List<int> PickPeaksWithThreshold(double[] arr, int neighbor, int minSpacingFrames, double threshold)
        {
            var candidates = new List<int>();
            int n = arr.Length;
            for (int i = 0; i < n; i++)
            {
                double v = arr[i];
                if (v < threshold)
                {
                    continue;
                }

                bool isPeak = true;
                for (int d = 1; d <= neighbor; d++)
                {
                    if (i - d >= 0 && arr[i - d] > v) { isPeak = false; break; }
                    if (i + d < n && arr[i + d] > v) { isPeak = false; break; }
                }
                if (isPeak)
                {
                    candidates.Add(i);
                }
            }
            // enforce minSpacingFrames - keep strongest in sliding windows
            var filtered = new List<int>();
            int last = -minSpacingFrames - 1;
            foreach (var p in candidates)
            {
                if (p - last <= minSpacingFrames)
                {
                    // replace if stronger
                    if (filtered.Count > 0 && arr[p] > arr[filtered.Last()])
                    {
                        filtered[filtered.Count - 1] = p;
                    }
                }
                else { filtered.Add(p); last = p; }
            }
            return filtered;
        }

        private static List<int> FallbackPartition(int totalSamples, int sr)
        {
            // fallback: try 2-8 partitions depending on length
            int seconds = Math.Max(1, totalSamples / sr);
            int parts = Math.Clamp(seconds * 2, 1, 8);
            var res = new List<int>();
            for (int i = 0; i < parts; i++)
            {
                res.Add(Math.Clamp((int) Math.Round(i * (totalSamples / (double) parts)), 0, totalSamples - 1));
            }

            return res;
        }

        private static int FindNearestZeroCrossingIndexSafe(int pos, int offset, int totalSamples)
        {
            int target = pos + offset;
            if (target < 0)
            {
                target = 0;
            }

            if (target >= totalSamples)
            {
                target = totalSamples - 1;
            }

            return Math.Clamp(target, 0, totalSamples - 1);
        }

        private static List<(int start, int end)> MergeOverlapping(List<(int start, int end)> segs, int toleranceSamples = 0)
        {
            if (segs == null || segs.Count == 0)
            {
                return new List<(int, int)>();
            }

            var ordered = segs.OrderBy(s => s.start).ToList();
            var outL = new List<(int start, int end)>();
            var cur = ordered[0];
            for (int i = 1; i < ordered.Count; i++)
            {
                var s = ordered[i];
                if (s.start <= cur.end + toleranceSamples)
                {
                    cur.end = Math.Max(cur.end, s.end);
                }
                else
                {
                    outL.Add(cur);
                    cur = s;
                }
            }
            outL.Add(cur);
            return outL;
        }

        // ----------------------------
        // Extraction & finalization
        // ----------------------------
        private static AudioObj ExtractMultiChannelSlice(float[] interleaved, int channels, int startFrame, int endFrame, int sampleRate, string baseName)
        {
            int frames = Math.Max(0, endFrame - startFrame);
            var outData = new float[frames * channels];
            for (int f = 0; f < frames; f++)
            {
                for (int c = 0; c < channels; c++)
                {
                    int src = (startFrame + f) * channels + c;
                    outData[f * channels + c] = src < interleaved.Length ? interleaved[src] : 0f;
                }
            }
            var obj = new AudioObj();
            obj.Data = outData;
            obj.Channels = channels;
            obj.SampleRate = sampleRate;
            obj.Length = outData.LongLength;
            try { obj.Name = (string.IsNullOrWhiteSpace(baseName) ? "slice" : baseName); } catch { }
            return obj;
        }

        private static AudioObj IsolateAndFinalize(AudioObj sample, int sampleRate)
        {
            // trim silence edges using percentile envelope, align to zero crossings, apply tiny fades and gentle peak normalize
            if (sample == null)
            {
                throw new ArgumentNullException(nameof(sample));
            }

            var data = sample.Data ?? Array.Empty<float>();
            int channels = Math.Max(1, sample.Channels);
            if (data.Length == 0)
            {
                return sample.Clone();
            }

            var mono = ToMono(data, channels);
            int sr = Math.Max(1, sample.SampleRate);
            // envelope over small windows
            int envWin = Math.Clamp((int) (0.006 * sr), 32, 512);
            int nframes = Math.Max(1, mono.Length / envWin);
            var env = new double[nframes];
            for (int i = 0; i < nframes; i++)
            {
                int pos = Math.Min(mono.Length - envWin, i * envWin);
                double s = 0; for (int j = 0; j < envWin; j++) { double v = mono[pos + j]; s += Math.Abs(v); }
                env[i] = s / envWin;
            }
            var sorted = (double[]) env.Clone(); Array.Sort(sorted);
            double threshold = sorted[Math.Clamp((int) (0.08 * sorted.Length), 0, sorted.Length - 1)] * 0.9 + 1e-9;

            int startFrame = 0, endFrame = mono.Length;
            for (int i = 0; i < env.Length; i++) { if (env[i] >= threshold) { startFrame = Math.Max(0, i * envWin - envWin); break; } }
            for (int i = env.Length - 1; i >= 0; i--) { if (env[i] >= threshold) { endFrame = Math.Min(mono.Length, (i + 1) * envWin + envWin); break; } }
            if (endFrame <= startFrame) { startFrame = 0; endFrame = mono.Length; }

            // zero crossing align
            startFrame = ClampZeroCrossing(mono, startFrame, -Math.Min(256, startFrame));
            endFrame = ClampZeroCrossing(mono, endFrame, Math.Min(256, mono.Length - endFrame));

            // map back to interleaved slice
            int outFrames = Math.Max(0, endFrame - startFrame);
            var outData = new float[outFrames * channels];
            for (int f = 0; f < outFrames; f++)
            {
                for (int c = 0; c < channels; c++)
                {
                    int src = (startFrame + f) * channels + c;
                    outData[f * channels + c] = src < data.Length ? data[src] : 0f;
                }
            }

            // tiny fades (3-12ms)
            int fadeMs = outFrames < (sampleRate / 10) ? 3 : 8;
            int fadeSamples = Math.Min(outFrames / 2, (int) (fadeMs * sampleRate / 1000.0));
            ApplyLinearFade(outData, channels, fadeSamples);

            // gentle peak normalize to 0.95 if above
            float peak = 0f; for (int i = 0; i < outData.Length; i++)
            {
                peak = MathF.Max(peak, MathF.Abs(outData[i]));
            }

            const float targetPeak = 0.95f;
            if (peak > 1e-8f && peak > targetPeak)
            {
                float scale = targetPeak / peak;
                for (int i = 0; i < outData.Length; i++)
                {
                    outData[i] *= scale;
                }
            }

            var clone = sample.Clone();
            clone.Data = outData;
            clone.Length = outData.LongLength;
            clone.Channels = channels;
            clone.SampleRate = sampleRate;
            return clone;
        }

        private static int ClampZeroCrossing(float[] mono, int pos, int maxOffset)
        {
            int best = Math.Clamp(pos, 0, mono.Length - 1);
            int step = Math.Sign(maxOffset);
            int maxAbs = Math.Abs(maxOffset);
            if (step == 0)
            {
                return best;
            }

            for (int d = 0; d <= maxAbs; d++)
            {
                int p = pos + d * step;
                if (p <= 0 || p >= mono.Length)
                {
                    break;
                }

                if (mono[p] == 0f) { best = p; break; }
                float a = mono[Math.Clamp(p - 1, 0, mono.Length - 1)];
                float b = mono[p];
                if ((a >= 0 && b < 0) || (a <= 0 && b > 0)) { best = p; break; }
            }
            return Math.Clamp(best, 0, mono.Length - 1);
        }

        // ----------------------------
        // Fingerprinting & clustering
        // ----------------------------
        private static double[] ComputeSpectralFingerprint(float[] interleaved, int channels, int sampleRate, int bins = 128)
        {
            var mono = ToMono(interleaved, channels);
            int N = 1;
            while (N < mono.Length && N < 8192)
            {
                N <<= 1;
            }

            if (N < 256)
            {
                N = 256;
            }

            var window = new double[N];
            for (int i = 0; i < N; i++)
            {
                window[i] = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (N - 1)));
            }

            var buf = new Complex[N];
            int offset = Math.Max(0, mono.Length / 2 - N / 2);
            for (int i = 0; i < N; i++)
            {
                buf[i] = new Complex((i + offset) < mono.Length ? mono[i + offset] * window[i] : 0.0, 0.0);
            }

            Fourier.Forward(buf, FourierOptions.Matlab);
            int half = N / 2;
            var mags = new double[half];
            double max = 1e-12;
            for (int k = 0; k < half; k++)
            {
                mags[k] = buf[k].Magnitude; if (mags[k] > max)
                {
                    max = mags[k];
                }
            }
            var fp = new double[bins];
            for (int b = 0; b < bins; b++)
            {
                int a = (int) Math.Round(b * (half / (double) bins));
                int bb = (int) Math.Round((b + 1) * (half / (double) bins));
                a = Math.Clamp(a, 0, half - 1); bb = Math.Clamp(bb, a + 1, half);
                double sum = 0; for (int k = a; k < bb; k++)
                {
                    sum += mags[k];
                }

                fp[b] = sum / Math.Max(1, bb - a) / max;
            }
            return fp;
        }

        private static float DetermineAdaptiveClusterThreshold(List<double[]> fps, float defaultThreshold)
        {
            if (fps == null || fps.Count < 2)
            {
                return defaultThreshold;
            }

            var sims = new List<double>();
            for (int i = 0; i < fps.Count; i++)
            {
                for (int j = i + 1; j < fps.Count; j++)
                {
                    sims.Add(CosineSimilarity(fps[i], fps[j]));
                }
            }

            if (sims.Count == 0)
            {
                return defaultThreshold;
            }

            double mean = sims.Average();
            double std = Math.Sqrt(sims.Average(v => (v - mean) * (v - mean)));
            double thr = mean + 0.35 * std;
            return (float) Math.Clamp(thr, 0.65, Math.Max(defaultThreshold, 0.95));
        }

        private static List<List<int>> GreedyClusterBySimilarity(List<double[]> fps, float threshold)
        {
            int n = fps.Count;
            var assigned = new bool[n];
            var clusters = new List<List<int>>();
            for (int i = 0; i < n; i++)
            {
                if (assigned[i])
                {
                    continue;
                }

                var cluster = new List<int> { i };
                assigned[i] = true;
                for (int j = i + 1; j < n; j++)
                {
                    if (assigned[j])
                    {
                        continue;
                    }

                    double sim = CosineSimilarity(fps[i], fps[j]);
                    if (sim >= threshold) { cluster.Add(j); assigned[j] = true; }
                }
                clusters.Add(cluster);
            }
            return clusters;
        }

        private static double CosineSimilarity(double[] a, double[] b)
        {
            int n = Math.Min(a.Length, b.Length);
            double da = 0, db = 0, dot = 0;
            for (int i = 0; i < n; i++) { dot += a[i] * b[i]; da += a[i] * a[i]; db += b[i] * b[i]; }
            if (da <= 0 || db <= 0)
            {
                return 0.0;
            }

            return dot / (Math.Sqrt(da) * Math.Sqrt(db));
        }

        // ----------------------------
        // Misc helpers
        // ----------------------------
        private static double ComputePeakRms(float[] interleaved, int channels)
        {
            if (interleaved == null || interleaved.Length == 0)
            {
                return 0.0;
            }

            var mono = ToMono(interleaved, channels);
            double s = 0; foreach (var v in mono)
            {
                s += v * v;
            }

            return Math.Sqrt(s / Math.Max(1, mono.Length));
        }

        private static void ApplyLinearFade(float[] interleaved, int channels, int fadeSamples)
        {
            if (fadeSamples <= 0)
            {
                return;
            }

            int frames = interleaved.Length / channels;
            fadeSamples = Math.Min(fadeSamples, frames / 2);
            for (int f = 0; f < fadeSamples; f++)
            {
                float g = f / (float) fadeSamples;
                for (int c = 0; c < channels; c++)
                {
                    interleaved[f * channels + c] *= g;
                }
            }
            for (int f = 0; f < fadeSamples; f++)
            {
                int idx = frames - fadeSamples + f;
                float g = 1.0f - f / (float) fadeSamples;
                for (int c = 0; c < channels; c++)
                {
                    interleaved[idx * channels + c] *= g;
                }
            }
        }

        private static double[] MedianFilter(double[] src, int win)
        {
            int n = src.Length;
            var outArr = new double[n];
            int r = win / 2;
            for (int i = 0; i < n; i++)
            {
                int a = Math.Max(0, i - r);
                int b = Math.Min(n - 1, i + r);
                var slice = new double[b - a + 1];
                Array.Copy(src, a, slice, 0, slice.Length);
                Array.Sort(slice);
                outArr[i] = slice[slice.Length / 2];
            }
            return outArr;
        }

        private static double ComputeSpectralCentroid(float[] mono, int s, int e, int sr)
        {
            int len = e - s;
            if (len < 32)
            {
                return 0.0;
            }

            int N = 1;
            while (N < len)
            {
                N <<= 1;
            }

            if (N > 8192)
            {
                N = 8192;
            }

            var buf = new Complex[N];
            for (int i = 0; i < N; i++)
            {
                buf[i] = new Complex((s + i) < mono.Length ? mono[s + i] : 0.0, 0.0);
            }

            Fourier.Forward(buf, FourierOptions.Matlab);
            double num = 0, den = 0;
            int half = N / 2;
            for (int k = 0; k < half; k++)
            {
                double mag = buf[k].Magnitude;
                double freq = (k * (double) sr) / N;
                num += freq * mag; den += mag;
            }
            return den > 0 ? num / den : 0.0;
        }

        // ----------------------------
        // Small utilities used above
        // ----------------------------
        private static List<int> SortedUnique(IEnumerable<int> seq)
        {
            var a = seq.Distinct().ToList();
            a.Sort();
            return a;
        }
    }
}
