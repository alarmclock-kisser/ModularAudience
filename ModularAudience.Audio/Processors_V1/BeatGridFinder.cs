using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using MathNet.Numerics.IntegralTransforms;

namespace ModularAudience.Audio.Processors_V1
{
    public static class BeatGridFinder
    {
        public static async Task<TimeSpan> FindSilenceDurationStartAsync(AudioObj audio, float? threshold = null, int? minDurationMs = null)
        {
            if (audio == null || audio.Data == null || audio.Data.Length == 0 || audio.SampleRate <= 0)
            {
                return TimeSpan.Zero;
            }

            var result = await Task.Run(() => ComputeSilenceDuration(audio, findStart: true, threshold, minDurationMs));
            LogCollection.Log("Detected silence at start: " + result.TotalSeconds.ToString("F1") + " seconds");
            return result;
        }

        public static async Task<TimeSpan> FindSilenceDurationEndAsync(AudioObj audio, float? threshold = null, int? minDurationMs = null)
        {
            if (audio == null || audio.Data == null || audio.Data.Length == 0 || audio.SampleRate <= 0)
            {
                return TimeSpan.Zero;
            }

            var result = await Task.Run(() => ComputeSilenceDuration(audio, findStart: false, threshold, minDurationMs));
            LogCollection.Log("Detected silence at end: " + result.TotalSeconds.ToString("F1") + " seconds");
            return result;
        }

        public static async Task TrimSilenceAsync(AudioObj audio, float? threshold = null, int? minDurationMs = null)
        {
            double startSilenceSeconds = (await FindSilenceDurationStartAsync(audio, threshold, minDurationMs)).TotalSeconds;
            double endSilenceSeconds = (await FindSilenceDurationEndAsync(audio, threshold, minDurationMs)).TotalSeconds;

            LogCollection.Log($"Trimming silence - start: {startSilenceSeconds:F2}s, end: {endSilenceSeconds:F2}s, threshold={(threshold.HasValue ? threshold.Value.ToString("F5") : "auto")}, minDurationMs={(minDurationMs.HasValue ? minDurationMs.Value.ToString() : "default")}");

            audio.SelectionStart = 0;
            audio.SelectionEnd = audio.GetSampleAtSeconds(startSilenceSeconds) * audio.Channels;
            await audio.EraseSelectionAsync();

            audio.SelectionStart = audio.GetSampleAtSeconds(audio.Duration.TotalSeconds - endSilenceSeconds) * audio.Channels;
            audio.SelectionEnd = audio.Length;
            await audio.EraseSelectionAsync();

            audio.SelectionStart = 0;
            audio.SelectionEnd = 0;
        }

        private static TimeSpan ComputeSilenceDuration(AudioObj audio, bool findStart, float? threshold, int? minDurationMs)
        {
            int sampleRate = Math.Max(1, audio.SampleRate);
            int channels = Math.Max(1, audio.Channels <= 0 ? 1 : audio.Channels);
            int minDurMs = minDurationMs ?? 80;
            int minDurSamples = (int) Math.Ceiling(sampleRate * (minDurMs / 1000.0));

            float[] data = audio.Data ?? Array.Empty<float>();
            int totalFrames = Math.Max(0, data.Length / channels);
            if (totalFrames <= 0)
            {
                return TimeSpan.Zero;
            }

            // -----------------------------------
            // 1) Envelope berechnen (RMS, ~1 Sample pro ms)
            // -----------------------------------
            int downsampleFactor = Math.Max(1, sampleRate / 1000); // ~1 ms pro Step
            int envLen = Math.Max(1, totalFrames / downsampleFactor);
            var env = new float[envLen];

            Parallel.For(0, envLen, idx =>
            {
                int frameStart = Math.Min(totalFrames - 1, idx * downsampleFactor);
                int frameEnd = Math.Min(totalFrames - 1, frameStart + downsampleFactor - 1);
                double sumSq = 0;
                int count = 0;

                for (int f = frameStart; f <= frameEnd; f++)
                {
                    int di = f * channels;
                    for (int c = 0; c < channels; c++)
                    {
                        float v = data[di + c];
                        sumSq += v * v;
                        count++;
                    }
                }

                env[idx] = (float) Math.Sqrt(sumSq / Math.Max(1, count));
            });

            // leicht glätten
            int smoothWin = Math.Max(3, Math.Min(21, envLen / 200));
            env = MovingAverage(env, smoothWin);

            // globale Statistik
            var sortedEnv = env.OrderBy(x => x).ToArray();
            int n = sortedEnv.Length;
            float globalMax = sortedEnv[^1];
            float globalMedian = sortedEnv[n / 2];
            float noiseFloor = sortedEnv[Math.Max(0, (int) (n * 0.02f))]; // untere 2%

            if (globalMax <= 1e-7f)
            {
                // praktisch komplette Stille -> nichts automatisch trimmen
                return TimeSpan.Zero;
            }

            // minimal sinnvoller Unterschied
            float eps = 1e-9f;

            // -----------------------------------
            // 2) Anfangs-/Endbereich Charakterisierung
            // -----------------------------------
            int windowFrames = (int) (20.0 * sampleRate / downsampleFactor); // bis zu 20 s
            windowFrames = Math.Max(1, Math.Min(envLen, windowFrames));

            // Anfangsfenster
            float startMax = 0f;
            float startMedian;
            {
                var tmp = new float[windowFrames];
                Array.Copy(env, 0, tmp, 0, windowFrames);
                Array.Sort(tmp);
                startMax = tmp[^1];
                startMedian = tmp[tmp.Length / 2];
            }

            // Endfenster
            float endMax = 0f;
            float endMedian;
            {
                var tmp = new float[windowFrames];
                Array.Copy(env, envLen - windowFrames, tmp, 0, windowFrames);
                Array.Sort(tmp);
                endMax = tmp[^1];
                endMedian = tmp[tmp.Length / 2];
            }

            // Verhältnis Anfang/Ende zur Gesamtenergie
            float startMaxRatio = startMax / Math.Max(globalMax, eps);
            float endMaxRatio = endMax / Math.Max(globalMax, eps);
            float startMedRatio = globalMedian > eps ? (startMedian / globalMedian) : 0f;
            float endMedRatio = globalMedian > eps ? (endMedian / globalMedian) : 0f;

            // -----------------------------------
            // 3) Silence-Schwelle (floor) bestimmen
            //    - threshold (manuell) dominiert
            //    - sonst dynamisch aus Noise-Floor + globalMax
            // -----------------------------------
            float silenceFloorBase;
            if (threshold.HasValue)
            {
                silenceFloorBase = Math.Max(threshold.Value, 1e-7f);
            }
            else
            {
                // sehr nahe am Rauschen, aber nicht 0
                // tracks mit großem Dynamikumfang -> Floor sehr niedrig
                float dynRange = globalMax / Math.Max(noiseFloor, eps);
                float dynDb = 20f * MathF.Log10(Math.Max(1.0001f, dynRange));

                // alpha bestimmt, wie weit wir über dem Noise-Floor liegen
                // großer Dynamikumfang -> kleine alpha (Intro bleibt erhalten)
                float alpha;
                if (dynDb < 20f)
                {
                    alpha = 0.4f;
                }
                else if (dynDb < 40f)
                {
                    alpha = 0.25f;
                }
                else if (dynDb < 60f)
                {
                    alpha = 0.12f;
                }
                else
                {
                    alpha = 0.06f;
                }

                float upperRef = sortedEnv[Math.Max(0, (int) (n * 0.8f))]; // 80%-Perzentil
                float target = noiseFloor + (upperRef - noiseFloor) * alpha;

                // zusätzlich clampen, damit wir wirkliche Stille sauber erwischen
                float minFloor = globalMax * 0.0001f;
                float maxFloor = globalMax * 0.05f;
                silenceFloorBase = Math.Clamp(target, minFloor, maxFloor);
            }

            // digitalere Stille -> noch strenger
            float digitalSilenceFloor = Math.Max(globalMax * 0.00005f, silenceFloorBase * 0.3f);

            // minimal benötigte "Silence-Frames"
            int minSilenceFrames = Math.Max(2, minDurSamples / downsampleFactor);

            // -----------------------------------
            // 4) Start- oder End-Silence bestimmen
            // -----------------------------------
            if (findStart)
            {
                // Ist der Anfang vom Energie-Niveau her "Teil des Songs"?
                // -> dann nur wirklich digitale Nullbereiche wegnehmen
                bool startLooksLikeContent =
                    startMaxRatio >= 0.25f || startMedRatio >= 0.5f;

                float floor = startLooksLikeContent
                    ? digitalSilenceFloor       // nur echte Nullen
                    : Math.Min(silenceFloorBase, globalMax * 0.03f); // "leise, aber wirklich still"

                int i = 0;
                while (i < envLen && env[i] <= floor)
                {
                    i++;
                }

                // zu kurz -> vermutlich nur paar Samples Offset / Dither -> nicht trimmen
                if (i < minSilenceFrames)
                {
                    return TimeSpan.Zero;
                }

                int sampleIdx = Math.Min(totalFrames - 1, i * downsampleFactor);

                // leichte Vorlauf-Sicherheit: paar ms stehen lassen
                sampleIdx = Math.Max(0, sampleIdx - sampleRate / 200); // ~5ms

                double seconds = sampleIdx / (double) sampleRate;
                return TimeSpan.FromSeconds(seconds);
            }
            else
            {
                // Ende: ähnlich wie Anfang, aber Outro darf gerne leiser sein.
                bool endLooksLikeContent =
                    endMaxRatio >= 0.3f || endMedRatio >= 0.6f;

                float floor = endLooksLikeContent
                    ? digitalSilenceFloor
                    : Math.Min(silenceFloorBase * 1.2f, globalMax * 0.04f);

                int i = envLen - 1;
                while (i >= 0 && env[i] <= floor)
                {
                    i--;
                }

                int trailingFrames = (envLen - 1) - i;
                if (trailingFrames < minSilenceFrames)
                {
                    return TimeSpan.Zero;
                }

                int trailingSamples = trailingFrames * downsampleFactor;

                // leichte Sicherheitsreserve: ein paar ms dran lassen
                trailingSamples = Math.Max(0, trailingSamples - sampleRate / 200);

                double seconds = trailingSamples / (double) sampleRate;
                return TimeSpan.FromSeconds(seconds);
            }
        }




        private static float CalculateRobustThreshold(float[] env, int sampleRate)
        {
            // Robust threshold using median + lower quartile noise floor
            if (env == null || env.Length == 0)
            {
                return 1e-5f;
            }

            var sorted = env.OrderBy(x => x).ToArray();
            float q1 = sorted[Math.Max(0, sorted.Length / 4)];
            float median = sorted[sorted.Length / 2];
            float mean = env.Average();
            float max = sorted[^1];

            // conservative but adaptive
            float thr = MathF.Max(q1 * 4f, median * 0.7f);
            thr = Math.Clamp(thr, 1e-6f, MathF.Max(1e-3f, max * 0.5f));
            LogCollection.Log($"Calculated silence threshold: {thr:F6} (q1:{q1:F6}, median:{median:F6}, mean:{mean:F6}, max:{max:F6})");
            return thr;
        }

        private static float[] MovingAverage(float[] data, int win)
        {
            if (win <= 1)
            {
                return data;
            }

            var outArr = new float[data.Length];
            int h = win / 2;
            for (int i = 0; i < data.Length; i++)
            {
                int s = Math.Max(0, i - h);
                int e = Math.Min(data.Length - 1, i + h);
                double sum = 0;
                for (int j = s; j <= e; j++)
                {
                    sum += data[j];
                }

                outArr[i] = (float) (sum / (e - s + 1));
            }
            return outArr;
        }


        // -------------------------
        // BeatGrid generation (public)
        // -------------------------






        // -------------------------
        // Preprocessing utilities
        // -------------------------
        private static (float[] mono, float[] envelope) CreateMonoAndEnvelope(float[] data, int channels, int sampleRate, int totalFrames, int startSample)
        {
            int analysisLen = Math.Max(0, totalFrames - startSample);
            var mono = new float[analysisLen];

            // build mono
            if (channels == 1)
            {
                Array.Copy(data, startSample, mono, 0, analysisLen);
            }
            else
            {
                Parallel.For(0, analysisLen, i =>
                {
                    int baseIdx = (startSample + i) * channels;
                    float sum = 0f;
                    for (int c = 0; c < channels; c++)
                    {
                        sum += data[baseIdx + c];
                    }

                    mono[i] = sum / channels;
                });
            }

            // envelope via simple abs + smoothing (fast)
            var env = new float[analysisLen];
            for (int i = 0; i < analysisLen; i++)
            {
                env[i] = MathF.Abs(mono[i]);
            }

            // smooth with small moving average (faster incremental)
            int w = Math.Max(1, Math.Min(101, sampleRate / 200)); // ~5-10ms smoothing
            env = FastMovingAverage(env, w);

            // compress dynamic range (sqrt)
            Parallel.For(0, env.Length, i => env[i] = MathF.Sqrt(env[i]));

            return (mono, env);
        }

        private static float[] FastMovingAverage(float[] data, int window)
        {
            int n = data.Length;
            if (n == 0 || window <= 1)
            {
                return data;
            }

            var outArr = new float[n];
            double sum = 0;
            int half = window / 2;
            int s = 0, e = -1;
            for (int i = 0; i < n; i++)
            {
                int newE = Math.Min(n - 1, i + half);
                while (e < newE)
                {
                    e++;
                    sum += data[e];
                }
                int newS = Math.Max(0, i - half);
                while (s < newS)
                {
                    sum -= data[s];
                    s++;
                }
                int len = e - s + 1;
                outArr[i] = (float) (sum / Math.Max(1, len));
            }
            return outArr;
        }

        private static float[] BuildOnsetEnvelope(float[] env, int sampleRate, int downsampleHz)
        {
            if (env == null || env.Length == 0)
            {
                return Array.Empty<float>();
            }

            int dsFactor = Math.Max(1, sampleRate / downsampleHz);
            int outLen = Math.Max(1, env.Length / dsFactor);
            var outEnv = new float[outLen];
            for (int i = 0; i < outLen; i++)
            {
                int s = i * dsFactor;
                int e = Math.Min(env.Length - 1, s + dsFactor - 1);
                float maxv = 0f;
                for (int j = s; j <= e; j++)
                {
                    if (env[j] > maxv)
                    {
                        maxv = env[j];
                    }
                }

                outEnv[i] = maxv;
            }
            // normalize
            float max = outEnv.Max();
            if (max > 0)
            {
                for (int i = 0; i < outEnv.Length; i++)
                {
                    outEnv[i] /= max;
                }
            }

            return outEnv;
        }


        // -------------------------
        // Onset detectors (optimized)
        // -------------------------
        private static List<int> DetectOnsetSpectralFlux(float[] envelope, int sampleRate, int startSample)
        {
            // spectral flux auf Envelope ist billig & gut für Percussion
            var results = new List<int>();
            if (envelope == null || envelope.Length < 8)
            {
                return results;
            }

            int fftSize = 512;
            int hop = 128;
            int envLen = envelope.Length;
            int numWindows = Math.Max(0, (envLen - fftSize) / hop);

            // guard - do FFT only if there are enough samples
            if (numWindows < 3)
            {
                // fallback: simple peak picking on envelope
                for (int i = 2; i < envelope.Length - 2; i++)
                {
                    if (envelope[i] > envelope[i - 1] &&
                        envelope[i] > envelope[i + 1] &&
                        envelope[i] > 0.12f)
                    {
                        results.Add(startSample + i);
                    }
                }
                return results;
            }

            // 1) Magnituden-Spektren aller Fenster PARALLEL berechnen
            int bins = fftSize / 2;
            var mags = new float[numWindows][];

            Parallel.For(0, numWindows, w =>
            {
                int idx = w * hop;
                var buf = new Complex[fftSize];
                int limit = Math.Min(fftSize, envLen - idx);

                for (int j = 0; j < limit; j++)
                {
                    float v = envelope[idx + j];
                    // Hann-Fenster
                    float win = 0.5f * (1 - MathF.Cos(2 * MathF.PI * j / (fftSize - 1)));
                    buf[j] = new Complex(v * win, 0);
                }
                for (int j = limit; j < fftSize; j++)
                {
                    buf[j] = Complex.Zero;
                }

                Fourier.Forward(buf, FourierOptions.Matlab);

                var localMag = new float[bins];
                for (int b = 0; b < bins; b++)
                {
                    localMag[b] = (float) buf[b].Magnitude;
                }

                mags[w] = localMag;
            });

            // 2) Serien-Flux-Berechnung zwischen benachbarten Frames (billig)
            var flux = new float[numWindows];
            for (int w = 1; w < numWindows; w++)
            {
                float localFlux = 0f;
                var cur = mags[w];
                var prev = mags[w - 1];
                for (int b = 1; b < bins; b++)
                {
                    float diff = cur[b] - prev[b];
                    if (diff > 0)
                    {
                        localFlux += diff;
                    }
                }
                flux[w] = localFlux;
            }

            // 3) adaptive threshold und Peak-Picking
            var thr = CalculateAdaptiveThreshold(flux) * 1.1f;
            for (int i = 2; i < flux.Length - 2; i++)
            {
                if (flux[i] > thr && flux[i] > flux[i - 1] && flux[i] > flux[i + 1])
                {
                    int pos = startSample + i * hop + fftSize / 2;
                    results.Add(pos);
                }
            }

            return results;
        }


        private static List<int> DetectEnergyPeaks(float[] envelope, int sampleRate, int startSample)
        {
            var beats = new List<int>();
            if (envelope == null || envelope.Length < 8)
            {
                return beats;
            }

            int windowSize = Math.Max(3, sampleRate / 200); // small window in samples
            int hop = Math.Max(1, windowSize / 2);
            int n = Math.Max(0, (envelope.Length - windowSize) / hop);
            if (n < 4)
            {
                // fallback: simple peaks
                for (int i = 2; i < envelope.Length - 2; i++)
                {
                    if (envelope[i] > envelope[i - 1] && envelope[i] > envelope[i + 1] && envelope[i] > 0.08f)
                    {
                        beats.Add(startSample + i);
                    }
                }

                return beats;
            }

            // compute window energies
            var energy = new float[n];
            Parallel.For(0, n, i =>
            {
                int s = i * hop;
                float sum = 0;
                for (int j = 0; j < windowSize && (s + j) < envelope.Length; j++)
                {
                    sum += envelope[s + j] * envelope[s + j];
                }

                energy[i] = sum / windowSize;
            });

            // median local threshold
            int medianWin = Math.Max(3, Math.Min(21, n / 10));
            for (int i = 2; i < n - 2; i++)
            {
                int s = Math.Max(0, i - medianWin);
                int e = Math.Min(n - 1, i + medianWin);
                var window = new List<float>();
                for (int j = s; j <= e; j++)
                {
                    window.Add(energy[j]);
                }

                window.Sort();
                float med = window[window.Count / 2];
                float thr = med * 2.2f + 1e-8f;
                if (energy[i] > thr && energy[i] > energy[i - 1] && energy[i] > energy[i + 1])
                {
                    int pos = startSample + i * hop + windowSize / 2;
                    beats.Add(Math.Min(Math.Max(0, pos), envelope.Length - 1));
                }
            }
            return beats;
        }

        private static List<int> DetectZeroCrossingBeats(float[] mono, float[] zeroCrossing, int sampleRate, int startSample)
        {
            var beats = new List<int>();

            if (mono == null || mono.Length < 64 || zeroCrossing == null || zeroCrossing.Length < 64)
            {
                return beats;
            }

            int windowSize = Math.Max(32, sampleRate / 40);
            int hop = Math.Max(8, windowSize / 2);
            int numWindows = Math.Max(0, (mono.Length - windowSize) / hop);

            for (int i = 0; i < numWindows; i++)
            {
                int s = i * hop;
                // compute avg zcr in window
                float sum = 0;
                for (int j = 0; j < windowSize && (s + j) < zeroCrossing.Length; j++)
                {
                    sum += zeroCrossing[s + j];
                }

                float avg = sum / Math.Max(1, windowSize);
                if (avg < 0.12f) // likely transient region
                {
                    // find max energy in window
                    float maxE = 0;
                    int peak = s + windowSize / 2;
                    for (int j = 0; j < windowSize && (s + j) < mono.Length; j++)
                    {
                        float e = MathF.Abs(mono[s + j]);
                        if (e > maxE) { maxE = e; peak = s + j; }
                    }
                    if (maxE > 0.04f)
                    {
                        beats.Add(startSample + peak);
                    }
                }
            }
            return beats;
        }

        private static float[] CalculateZeroCrossingRate(float[] data, int sampleRate)
        {
            int n = data.Length;
            var zcr = new float[n];
            int window = Math.Min(512, Math.Max(32, sampleRate / 20));

            Parallel.For(0, n, i =>
            {
                int s = Math.Max(0, i - window / 2);
                int e = Math.Min(n - 1, i + window / 2);
                int crossings = 0;
                for (int j = s + 1; j <= e; j++)
                {
                    if (data[j - 1] * data[j] < 0)
                    {
                        crossings++;
                    }
                }
                zcr[i] = (float) crossings / Math.Max(1, (e - s));
            });

            return zcr;
        }



        // -------------------------
        // Combining + tempo + utilities
        // -------------------------
        private static void CombineBeatDetectionsWithFiltering(bool[] beatGrid, List<int> spectralBeats, List<int> energyBeats, List<int> complexBeats, List<int> zeroCrossingBeats, float[] zeroCrossing, int startSample, int sampleRate, int primaryIntervalSamples)
        {
            // All candidate beats -> weighted votes (spectral: high, energy: medium, zcr: low)
            var vote = new ConcurrentDictionary<int, float>();

            void Vote(IEnumerable<int> positions, float weight)
            {
                foreach (var p in positions)
                {
                    if (p < startSample)
                    {
                        continue;
                    }

                    int clamped = Math.Min(Math.Max(0, p), beatGrid.Length - 1);
                    vote.AddOrUpdate(clamped, weight, (_, old) => old + weight);
                }
            }

            Vote(spectralBeats, 3.0f);
            Vote(energyBeats, 2.0f);
            Vote(zeroCrossingBeats, 1.0f);
            Vote(complexBeats, 1.5f);

            if (vote.IsEmpty)
            {
                return;
            }

            // Convert votes to list and cluster near positions
            var candidates = vote.ToArray().OrderByDescending(kv => kv.Value).Select(kv => kv.Key).ToList();

            int minDist = Math.Max(1, sampleRate / 40); // ~25ms min
            var chosen = new List<int>();
            var used = new bool[beatGrid.Length];

            foreach (var cand in candidates)
            {
                if (used[cand])
                {
                    continue;
                }
                // cluster neighborhood and pick best centre
                int s = Math.Max(0, cand - minDist);
                int e = Math.Min(beatGrid.Length - 1, cand + minDist);
                // find max vote in cluster
                int best = cand;
                float bestScore = vote.ContainsKey(cand) ? vote[cand] : 0;
                for (int i = s; i <= e; i++)
                {
                    if (vote.TryGetValue(i, out float sc) && sc > bestScore)
                    {
                        bestScore = sc; best = i;
                    }
                }
                // mark used
                for (int i = Math.Max(0, best - minDist); i <= Math.Min(beatGrid.Length - 1, best + minDist); i++)
                {
                    used[i] = true;
                }

                chosen.Add(best);
            }

            // Place chosen beats into beatGrid
            foreach (var b in chosen)
            {
                if (b >= 0 && b < beatGrid.Length)
                {
                    beatGrid[b] = true;
                }
            }
        }

        private static void ApplyTempoAnalysisAndFill(bool[] beatGrid, int sampleRate, int primaryInterval, int granularity)
        {
            // Get current beats
            var detected = new List<int>();
            for (int i = 0; i < beatGrid.Length; i++)
            {
                if (beatGrid[i])
                {
                    detected.Add(i);
                }
            }

            if (detected.Count < 3 || primaryInterval <= 0)
            {
                return;
            }

            // Compute histogram of intervals (robust)
            var intervals = new List<int>();
            for (int i = 1; i < detected.Count; i++)
            {
                int inter = detected[i] - detected[i - 1];
                if (inter >= sampleRate / 8 && inter <= sampleRate)
                {
                    intervals.Add(inter);
                }
            }
            if (intervals.Count == 0)
            {
                return;
            }

            // primary interval: either provided or modal of intervals
            int modal = intervals.OrderBy(x => x).GroupBy(x => x).OrderByDescending(g => g.Count()).First().Key;
            int interval = primaryInterval > 0 ? primaryInterval : modal;

            // round interval to nearest granularity samples for stability
            granularity = Math.Max(1, Math.Min(64, granularity));
            // round to nearest multiple of granularity
            interval = Math.Max(1, ((interval + (granularity / 2)) / granularity) * granularity);

            // Fill grid from detected beats using interval
            int seed = detected[0];

            // seed may be not on-beat; try to align seed to nearest strong detection if any
            int bestSeed = seed;
            int minDist = Math.Max(1, sampleRate / 40);
            foreach (var d in detected)
            {
                if (Math.Abs(d - seed) < Math.Abs(bestSeed - seed))
                {
                    bestSeed = d;
                }
            }
            seed = bestSeed;

            FillBeatGridWithInterval(beatGrid, seed, interval);
        }


        private static void FillBeatGridWithInterval(bool[] beatGrid, int seed, int interval)
        {
            if (interval <= 0)
            {
                return;
            }

            int cur = seed;
            while (cur < beatGrid.Length)
            {
                beatGrid[cur] = true;
                cur += interval;
            }
            cur = seed - interval;
            while (cur >= 0)
            {
                beatGrid[cur] = true;
                cur -= interval;
            }
        }

        private static int EstimatePrimaryInterval(float[] downsampledEnv, int sampleRateDownsampledHz)
        {
            if (downsampledEnv == null || downsampledEnv.Length < 16)
            {
                return -1;
            }
            // autocorrelation
            int n = downsampledEnv.Length;
            // normalize
            float mean = downsampledEnv.Average();
            var norm = new float[n];
            for (int i = 0; i < n; i++)
            {
                norm[i] = downsampledEnv[i] - mean;
            }

            int maxLag = Math.Min(n / 2, sampleRateDownsampledHz * 2); // consider up to 2 seconds
            double bestVal = double.MinValue;
            int bestLag = -1;
            for (int lag = sampleRateDownsampledHz / 3; lag <= maxLag; lag++) // 0.3s .. max
            {
                double sum = 0;
                for (int i = 0; i < n - lag; i++)
                {
                    sum += norm[i] * norm[i + lag];
                }

                if (sum > bestVal)
                {
                    bestVal = sum;
                    bestLag = lag;
                }
            }
            return bestLag;
        }

        private static int FallbackIntervalFromDetections(List<int>[] detectionLists, int sampleRate, int sampleRateDownsampledHz)
        {
            var all = new List<int>();
            foreach (var l in detectionLists)
            {
                all.AddRange(l);
            }

            all.Sort();
            if (all.Count < 2)
            {
                return sampleRateDownsampledHz; // 1s fallback
            }

            var intervals = new List<int>();
            for (int i = 1; i < all.Count; i++)
            {
                intervals.Add(all[i] - all[i - 1]);
            }

            if (intervals.Count == 0)
            {
                return sampleRateDownsampledHz;
            }

            int med = intervals.OrderBy(x => x).ElementAt(intervals.Count / 2);
            // convert to downsampled units (~100Hz)
            return Math.Max(1, (int) Math.Round(med / (sampleRate / (double) sampleRateDownsampledHz)));
        }



        // -------------------------
        // small helpers used previously kept for compatibility (not all old methods preserved)
        // -------------------------
        private static float CalculateAdaptiveThreshold(float[] values)
        {
            if (values == null || values.Length == 0)
            {
                return 0f;
            }

            var sorted = values.OrderBy(x => x).ToArray();
            float median = sorted[sorted.Length / 2];
            float mean = values.Average();
            return median * 1.8f + mean * 0.2f;
        }

        public static async Task<bool[]> GenerateBeatGridAsync(AudioObj audio, bool set = true, int granularity = 4)
        {
            if (audio == null || audio.Data == null || audio.Data.Length == 0 || audio.SampleRate <= 0)
            {
                return Array.Empty<bool[]>().FirstOrDefault() ?? Array.Empty<bool>();
            }

            return await Task.Run(() =>
            {
                try
                {
                    int sampleRate = audio.SampleRate;
                    int channels = Math.Max(1, audio.Channels);
                    float[] data = audio.Data;
                    int totalFrames = data.Length / channels;

                    TimeSpan startSilence = ComputeSilenceDuration(audio, true, null, 50);
                    int startSample = (int) Math.Round(startSilence.TotalSeconds * sampleRate);
                    startSample = Math.Clamp(startSample, 0, Math.Max(0, totalFrames - 1));

                    var pre = CreateMonoAndEnvelope(data, channels, sampleRate, totalFrames, startSample);
                    float[] mono = pre.mono;
                    float[] envelope = pre.envelope;

                    if (mono.Length < sampleRate / 2)
                    {
                        var emptyGrid = new bool[totalFrames];
                        if (set)
                        {
                            audio.BeatGrid = emptyGrid;
                        }

                        return emptyGrid;
                    }

                    int downsampleHz = 200;
                    float[] envDs = BuildOnsetEnvelope(envelope, sampleRate, downsampleHz);
                    if (envDs == null || envDs.Length < 32)
                    {
                        var emptyGrid = new bool[totalFrames];
                        if (set)
                        {
                            audio.BeatGrid = emptyGrid;
                        }

                        return emptyGrid;
                    }

                    float[] onsetEnv = new float[envDs.Length];
                    onsetEnv[0] = 0f;
                    for (int i = 1; i < envDs.Length; i++)
                    {
                        float d = envDs[i] - envDs[i - 1];
                        onsetEnv[i] = d > 0f ? d : 0f;
                    }

                    int dsFactor = Math.Max(1, sampleRate / downsampleHz);
                    var onsetFrames = DetectOnsetsFromEnvelope(onsetEnv, envDs, sampleRate, downsampleHz, startSample, dsFactor);
                    if (onsetFrames.Count < 4)
                    {
                        var emptyGrid = new bool[totalFrames];
                        if (set)
                        {
                            audio.BeatGrid = emptyGrid;
                        }

                        return emptyGrid;
                    }

                    int intervalFrames = EstimateBeatIntervalFromOnsets(onsetFrames, sampleRate);
                    if (intervalFrames <= 0)
                    {
                        int lag = EstimateBeatLagFromOnsetEnvelope(onsetEnv, downsampleHz);
                        if (lag > 0)
                        {
                            intervalFrames = lag * dsFactor;
                        }
                    }

                    if (intervalFrames <= 0)
                    {
                        var emptyGrid = new bool[totalFrames];
                        if (set)
                        {
                            audio.BeatGrid = emptyGrid;
                        }

                        return emptyGrid;
                    }

                    granularity = Math.Clamp(granularity, 1, 16);
                    int snappedInterval = Math.Max(1, (int) Math.Round((double) intervalFrames / granularity) * granularity);
                    intervalFrames = snappedInterval;

                    int phaseFrame = FindBestPhase(onsetFrames, intervalFrames);

                    var beatGrid = new bool[totalFrames];
                    BuildBeatGridFromTempo(beatGrid, intervalFrames, phaseFrame);

                    AlignBeatGridToEnvelope(beatGrid, envelope, startSample, intervalFrames / 3);

                    for (int i = 0; i < Math.Min(startSample, beatGrid.Length); i++)
                    {
                        beatGrid[i] = false;
                    }

                    int minDist = Math.Max(1, intervalFrames / 4);
                    EnforceMinDistance(beatGrid, minDist);

                    if (set)
                    {
                        audio.BeatGrid = beatGrid;
                    }

                    int beatCount = beatGrid.Count(b => b);
                    double bpm = 60.0 * sampleRate / intervalFrames;
                    LogCollection.Log($"Beat grid detection finished. Beats: {beatCount}, intervalFrames={intervalFrames}, bpm≈{bpm:F1}");
                    return beatGrid;
                }
                catch (Exception ex)
                {
                    LogCollection.Log($"Error in beat grid detection: {ex.Message}");
                    return new bool[audio.Data?.Length ?? 0];
                }
            });
        }

        private static List<int> DetectOnsetsFromEnvelope(float[] onsetEnv, float[] envDs, int sampleRate, int downsampleHz, int startSample, int dsFactor)
        {
            var result = new List<int>();
            if (onsetEnv == null || envDs == null)
            {
                return result;
            }

            if (onsetEnv.Length == 0 || envDs.Length == 0)
            {
                return result;
            }

            if (onsetEnv.Length != envDs.Length)
            {
                return result;
            }

            var pos = onsetEnv.Where(v => v > 0f).ToArray();
            if (pos.Length < 4)
            {
                return result;
            }

            var sortedOn = pos.OrderBy(x => x).ToArray();
            float medOn = sortedOn[sortedOn.Length / 2];
            float q3On = sortedOn[(int) (sortedOn.Length * 0.75)];
            float thrOn = medOn + (q3On - medOn) * 0.5f;
            thrOn = Math.Max(thrOn, medOn * 1.5f);

            var sortedEnv = envDs.OrderBy(x => x).ToArray();
            float envMax = sortedEnv[^1];
            float envMed = sortedEnv[sortedEnv.Length / 2];
            float envThr = Math.Max(envMed * 0.5f, envMax * 0.15f);

            for (int i = 2; i < onsetEnv.Length - 2; i++)
            {
                float v = onsetEnv[i];
                if (v <= thrOn)
                {
                    continue;
                }

                if (!(v > onsetEnv[i - 1] && v > onsetEnv[i + 1]))
                {
                    continue;
                }

                if (envDs[i] < envThr)
                {
                    continue;
                }

                long frame = startSample + (long) i * dsFactor;
                if (frame >= 0)
                {
                    result.Add((int) frame);
                }
            }

            result.Sort();
            return result;
        }

        private static int EstimateBeatIntervalFromOnsets(List<int> onsetFrames, int sampleRate)
        {
            if (onsetFrames == null || onsetFrames.Count < 4)
            {
                return -1;
            }

            onsetFrames.Sort();
            var diffs = new List<int>();
            for (int i = 1; i < onsetFrames.Count; i++)
            {
                int d = onsetFrames[i] - onsetFrames[i - 1];
                if (d <= 0)
                {
                    continue;
                }

                if (d < sampleRate * 0.2 || d > sampleRate * 2.0)
                {
                    continue;
                }

                diffs.Add(d);
            }
            if (diffs.Count == 0)
            {
                return -1;
            }

            int minInterval = (int) (sampleRate * 0.25);
            int maxInterval = (int) (sampleRate * 1.0);
            int range = Math.Max(1, maxInterval - minInterval + 1);
            int bins = 120;
            int binSize = Math.Max(1, range / bins);
            var hist = new int[bins];

            foreach (int d in diffs)
            {
                double val = d;
                while (val < minInterval)
                {
                    val *= 2.0;
                }

                while (val > maxInterval)
                {
                    val /= 2.0;
                }

                int iv = (int) Math.Round(val);
                int idx = (iv - minInterval) / binSize;
                if (idx < 0 || idx >= bins)
                {
                    continue;
                }

                hist[idx]++;
            }

            int bestIdx = -1;
            int bestCount = -1;
            for (int i = 0; i < bins; i++)
            {
                if (hist[i] > bestCount)
                {
                    bestCount = hist[i];
                    bestIdx = i;
                }
            }
            if (bestIdx < 0 || bestCount <= 0)
            {
                return -1;
            }

            int center = minInterval + bestIdx * binSize + binSize / 2;
            var near = diffs.Select(d =>
            {
                double val = d;
                while (val < minInterval)
                {
                    val *= 2.0;
                }

                while (val > maxInterval)
                {
                    val /= 2.0;
                }

                return (int) Math.Round(val);
            }).Where(iv => Math.Abs(iv - center) <= binSize).ToArray();

            if (near.Length == 0)
            {
                return center;
            }

            Array.Sort(near);
            int median = near[near.Length / 2];
            return median;
        }

        private static int EstimateBeatLagFromOnsetEnvelope(float[] onsetEnv, int downsampleHz)
        {
            if (onsetEnv == null || onsetEnv.Length < 32)
            {
                return -1;
            }

            int n = onsetEnv.Length;
            float mean = onsetEnv.Average();
            var norm = new float[n];
            for (int i = 0; i < n; i++)
            {
                norm[i] = onsetEnv[i] - mean;
            }

            int minBpm = 70;
            int maxBpm = 180;
            double minSec = 60.0 / maxBpm;
            double maxSec = 60.0 / minBpm;

            int minLag = (int) Math.Round(minSec * downsampleHz);
            int maxLag = Math.Min(n / 2, (int) Math.Round(maxSec * downsampleHz));
            if (maxLag <= minLag)
            {
                return -1;
            }

            double bestVal = double.MinValue;
            int bestLag = -1;

            for (int lag = minLag; lag <= maxLag; lag++)
            {
                double sum = 0;
                for (int i = lag; i < n; i++)
                {
                    sum += norm[i] * norm[i - lag];
                }

                if (sum > bestVal)
                {
                    bestVal = sum;
                    bestLag = lag;
                }
            }

            if (bestLag <= 0)
            {
                return -1;
            }

            return bestLag;
        }

        private static int FindBestPhase(List<int> onsetFrames, int intervalFrames)
        {
            if (onsetFrames == null || onsetFrames.Count == 0 || intervalFrames <= 0)
            {
                return 0;
            }

            onsetFrames.Sort();
            int steps = 48;
            double bestCost = double.MaxValue;
            int bestPhase = 0;

            for (int s = 0; s < steps; s++)
            {
                double phase = intervalFrames * (s / (double) steps);
                double cost = 0.0;

                foreach (var f in onsetFrames)
                {
                    double r = (f - phase) % intervalFrames;
                    if (r < 0)
                    {
                        r += intervalFrames;
                    }

                    double d = r;
                    if (d > intervalFrames / 2.0)
                    {
                        d = intervalFrames - d;
                    }

                    cost += d;
                }

                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestPhase = (int) Math.Round(phase);
                }
            }

            return bestPhase;
        }

        private static void BuildBeatGridFromTempo(bool[] beatGrid, int intervalFrames, int phaseFrame)
        {
            if (beatGrid == null || beatGrid.Length == 0 || intervalFrames <= 0)
            {
                return;
            }

            int n = beatGrid.Length;
            int start = phaseFrame;
            while (start - intervalFrames >= 0)
            {
                start -= intervalFrames;
            }

            if (start < 0)
            {
                start += intervalFrames;
            }

            for (int pos = start; pos < n; pos += intervalFrames)
            {
                beatGrid[pos] = true;
            }
        }

        private static void AlignBeatGridToEnvelope(bool[] beatGrid, float[] envelope, int startSample, int maxShiftFrames)
        {
            if (beatGrid == null || envelope == null)
            {
                return;
            }

            if (beatGrid.Length == 0 || envelope.Length == 0)
            {
                return;
            }

            int n = beatGrid.Length;
            int envLen = envelope.Length;
            maxShiftFrames = Math.Max(1, maxShiftFrames);

            var newGrid = new bool[n];

            float maxEnv = envelope.Max();
            if (maxEnv <= 0f)
            {
                Array.Copy(beatGrid, newGrid, n);
                Array.Copy(newGrid, beatGrid, n);
                return;
            }

            float thr = maxEnv * 0.12f;

            for (int i = 0; i < n; i++)
            {
                if (!beatGrid[i])
                {
                    continue;
                }

                int center = i - startSample;
                if (center < 0 || center >= envLen)
                {
                    if (i >= 0 && i < n)
                    {
                        newGrid[i] = true;
                    }

                    continue;
                }

                int radius = Math.Min(maxShiftFrames, envLen - 1);
                int s = Math.Max(0, center - radius);
                int e = Math.Min(envLen - 1, center + radius);

                int bestIdx = center;
                float bestVal = envelope[center];

                for (int j = s; j <= e; j++)
                {
                    float v = envelope[j];
                    if (v > bestVal)
                    {
                        bestVal = v;
                        bestIdx = j;
                    }
                }

                if (bestVal < thr)
                {
                    if (i >= 0 && i < n)
                    {
                        newGrid[i] = true;
                    }

                    continue;
                }

                int newFrame = startSample + bestIdx;
                if (newFrame < 0 || newFrame >= n)
                {
                    newFrame = Math.Clamp(i, 0, n - 1);
                }

                newGrid[newFrame] = true;
            }

            Array.Copy(newGrid, beatGrid, n);
        }

        private static void EnforceMinDistance(bool[] beatGrid, int minDistance)
        {
            if (beatGrid == null || beatGrid.Length == 0 || minDistance <= 0)
            {
                return;
            }

            int last = -minDistance - 1;
            for (int i = 0; i < beatGrid.Length; i++)
            {
                if (!beatGrid[i])
                {
                    continue;
                }

                if (i - last < minDistance)
                {
                    beatGrid[i] = false;
                }
                else
                {
                    last = i;
                }
            }
        }
















    }
}
