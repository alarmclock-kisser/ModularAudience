using NAudience.Core;
using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ModularAudience.Audio
{
    public static class BeatScanner
    {
        public static async Task<double> ScanBpmAsync(AudioObj obj, int windowSize = 65536, int lookingRange = 2, int minBpm = 60, int maxBpm = 200, bool autoGetTiming = false)
        {
            if (obj == null || obj.Data == null || obj.Data.Length <= 0)
            {
                return -1.0f;
            }

            Stopwatch sw = Stopwatch.StartNew();

            var monoData = await obj.GetCurrentWindowAsync(windowSize, lookingRange, true, true);

            // Check if timing already scanned
            float timing = 1.0f;
            if (autoGetTiming)
            {
                timing = await ScanTimingAsync(obj, windowSize, lookingRange);
            }

            double bpm = await EstimateBpmAsync(monoData, obj.SampleRate, minBpm, maxBpm);

            sw.Stop();
            obj["beatScan"] = (float) sw.Elapsed.TotalMilliseconds;

            if (bpm <= 0.0)
            {
                return -1.0f;
            }

            obj.ScannedBpm = (float) bpm;

            return bpm * timing;
        }

        public static async Task<float> ScanTimingAsync(AudioObj obj, int windowSize = 65536, int lookingRange = 2)
        {
            if (obj == null || obj.Data == null || obj.Data.Length <= 0)
            {
                return -1.0f;
            }

            var monoData = await obj.GetCurrentWindowAsync(windowSize, lookingRange, true, false);

            float timing = await EstimateTimingAsync(monoData, obj.SampleRate);

            obj.Timing = timing;

            return timing;
        }

        internal static async Task<double> EstimateBpmAsync(float[] samples, int sampleRate, int minBpm = 60, int maxBpm = 200)
        {
            if (samples == null || samples.Length == 0 || sampleRate <= 0 || minBpm <= 0 || maxBpm <= minBpm)
            {
                return 0.0;
            }

            return await Task.Run(() =>
            {
                int n = samples.Length;

                // 1) Vorverarbeitung: Hüllkurve/Onset-ähnliches Signal
                //    Absolutwert -> schnelle & langsame gleitende Mittelwerte -> Halbwellendetektion
                double[] abs = new double[n];
                for (int i = 0; i < n; i++)
                {
                    abs[i] = Math.Abs(samples[i]);
                }

                // Fenster für schnelle/slow MA (10ms/400ms, gekappt auf Datenlänge)
                int fastWin = Math.Clamp(sampleRate / 100, 1, n);   // ~10 ms
                int slowWin = Math.Clamp(sampleRate / 2, fastWin + 1, n); // ~0.5 s

                double[] fast = new double[n];
                double[] slow = new double[n];

                // Rolling sums für O(n) Moving Average
                double sumFast = 0, sumSlow = 0;
                for (int i = 0; i < n; i++)
                {
                    sumFast += abs[i];
                    if (i >= fastWin)
                    {
                        sumFast -= abs[i - fastWin];
                    }

                    fast[i] = sumFast / Math.Min(i + 1, fastWin);

                    sumSlow += abs[i];
                    if (i >= slowWin)
                    {
                        sumSlow -= abs[i - slowWin];
                    }

                    slow[i] = sumSlow / Math.Min(i + 1, slowWin);
                }

                double[] novelty = new double[n];
                for (int i = 0; i < n; i++)
                {
                    novelty[i] = Math.Max(0.0, fast[i] - slow[i]);
                }

                // DC entfernen und auf Varianz normieren
                double mean = novelty.Average();
                double var = 0.0;
                for (int i = 0; i < n; i++)
                {
                    novelty[i] -= mean;
                    var += novelty[i] * novelty[i];
                }
                if (var <= 1e-12)
                {
                    return 0.0;
                }

                double invStd = 1.0 / Math.Sqrt(var / n);
                for (int i = 0; i < n; i++)
                {
                    novelty[i] *= invStd;
                }

                // 2) Autokorrelation via FFT: r = IFFT(|FFT(x)|^2)
                int L = 1;
                while (L < 2 * n)
                {
                    L <<= 1;
                }

                var fft = new MathNet.Numerics.Complex32[L];
                for (int i = 0; i < n; i++)
                {
                    fft[i] = new MathNet.Numerics.Complex32((float) novelty[i], 0f);
                }

                for (int i = n; i < L; i++)
                {
                    fft[i] = Complex32.Zero;
                }

                Fourier.Forward(fft, FourierOptions.Matlab);

                for (int i = 0; i < L; i++)
                {
                    // |X|^2 = X * conj(X)
                    var v = fft[i];
                    fft[i] = new MathNet.Numerics.Complex32(v.Magnitude * v.Magnitude, 0f);
                }

                Fourier.Inverse(fft, FourierOptions.Matlab);

                // Realteil, 0..n-1 relevant (lineare Autokorrelation)
                double r0 = Math.Max(fft[0].Real, 1e-12f);
                // 3) Lag-Suchbereich aus BPM-Grenzen
                int minLag = (int) Math.Round(sampleRate * 60.0 / Math.Max(maxBpm, 1));
                int maxLag = (int) Math.Round(sampleRate * 60.0 / Math.Max(minBpm, 1));
                minLag = Math.Clamp(minLag, 1, n - 1);
                maxLag = Math.Clamp(maxLag, minLag, n - 1);

                // 4) Bestes Lag im Bereich wählen (größte normalisierte Autokorrelation)
                int bestLag = -1;
                double bestVal = double.NegativeInfinity;

                for (int k = minLag; k <= maxLag; k++)
                {
                    double val = fft[k].Real / r0; // Normalisierung
                    if (val > bestVal)
                    {
                        bestVal = val;
                        bestLag = k;
                    }
                }

                if (bestLag <= 0 || bestVal < 0.02) // zu schwaches Signal
                {
                    return 0.0;
                }

                // 5) Parabolische Interpolation um Maximum (Sub-Sample-Schätzung)
                double lag = bestLag;
                if (bestLag > minLag && bestLag < maxLag)
                {
                    double y1 = fft[bestLag - 1].Real;
                    double y2 = fft[bestLag].Real;
                    double y3 = fft[bestLag + 1].Real;
                    double denom = (y1 - 2 * y2 + y3);
                    if (Math.Abs(denom) > 1e-12)
                    {
                        double delta = 0.5 * (y1 - y3) / denom; // in [-1,1]
                        delta = Math.Max(-1.0, Math.Min(1.0, delta));
                        lag = bestLag + delta;
                    }
                }

                // 6) BPM berechnen
                double bpm = 60.0 * sampleRate / lag;

                // Optional: auf Bereich [minBpm,maxBpm] falten (x2 / x0.5 Heuristik)
                while (bpm < minBpm && bpm > 0)
                {
                    bpm *= 2.0;
                }

                while (bpm > maxBpm)
                {
                    bpm /= 2.0;
                }

                return (bpm >= minBpm && bpm <= maxBpm) ? bpm : 0.0;
            });
        }

        internal static async Task<float> EstimateTimingAsync(float[] samples, int sampleRate)
        {
            if (samples == null || samples.Length == 0 || sampleRate <= 0)
            {
                return -1.0f;
            }

            return await Task.Run(() =>
            {
                int n = samples.Length;

                // 1) Novelty (Onset-Hüllkurve)
                double[] abs = new double[n];
                for (int i = 0; i < n; i++)
                {
                    abs[i] = Math.Abs(samples[i]);
                }

                int fastWin = Math.Clamp(sampleRate / 100, 1, n);        // ~10 ms
                int slowWin = Math.Clamp(sampleRate / 2, fastWin + 1, n); // ~0.5 s

                double[] fast = new double[n];
                double[] slow = new double[n];
                double sumFast = 0, sumSlow = 0;

                for (int i = 0; i < n; i++)
                {
                    sumFast += abs[i];
                    if (i >= fastWin)
                    {
                        sumFast -= abs[i - fastWin];
                    }
                    fast[i] = sumFast / Math.Min(i + 1, fastWin);

                    sumSlow += abs[i];
                    if (i >= slowWin)
                    {
                        sumSlow -= abs[i - slowWin];
                    }
                    slow[i] = sumSlow / Math.Min(i + 1, slowWin);
                }

                double[] novelty = new double[n];
                for (int i = 0; i < n; i++)
                {
                    novelty[i] = Math.Max(0.0, fast[i] - slow[i]);
                }

                double mean = novelty.Average();
                double var = 0.0;
                for (int i = 0; i < n; i++)
                {
                    novelty[i] -= mean;
                    var += novelty[i] * novelty[i];
                }
                if (var <= 1e-12)
                {
                    return -1.0f;
                }

                double invStd = 1.0 / Math.Sqrt(var / n);
                for (int i = 0; i < n; i++)
                {
                    novelty[i] *= invStd;
                }

                // 2) Beat-Lag via FFT-ACF
                int L = 1;
                while (L < 2 * n)
                {
                    L <<= 1;
                }

                var fft = new MathNet.Numerics.Complex32[L];
                for (int i = 0; i < n; i++)
                {
                    fft[i] = new MathNet.Numerics.Complex32((float) novelty[i], 0f);
                }
                for (int i = n; i < L; i++)
                {
                    fft[i] = Complex32.Zero;
                }

                Fourier.Forward(fft, FourierOptions.Matlab);
                for (int i = 0; i < L; i++)
                {
                    var v = fft[i];
                    fft[i] = new MathNet.Numerics.Complex32(v.Magnitude * v.Magnitude, 0f);
                }
                Fourier.Inverse(fft, FourierOptions.Matlab);

                double r0 = Math.Max(fft[0].Real, 1e-12f);
                int minBpm = 50, maxBpm = 200;
                int minLag = Math.Clamp((int) Math.Round(sampleRate * 60.0 / Math.Max(maxBpm, 1)), 1, n - 1);
                int maxLag = Math.Clamp((int) Math.Round(sampleRate * 60.0 / Math.Max(minBpm, 1)), minLag, n - 1);

                int bestLag = -1;
                double bestVal = double.NegativeInfinity;
                for (int k = minLag; k <= maxLag; k++)
                {
                    double val = fft[k].Real / r0;
                    if (val > bestVal)
                    {
                        bestVal = val;
                        bestLag = k;
                    }
                }
                if (bestLag <= 0)
                {
                    return -1.0f;
                }

                int beatLag = bestLag; // in Samples

                // 3) Beat-Stärken
                int nBeats = Math.Max(0, n / beatLag);
                if (nBeats < 4)
                {
                    return -1.0f;
                }

                double[] beatStrengths = new double[nBeats];
                int half = Math.Max(1, beatLag / 4);
                for (int b = 0; b < nBeats; b++)
                {
                    int center = b * beatLag;
                    int a = Math.Max(0, center - half);
                    int e = Math.Min(n - 1, center + half);
                    double sum = 0.0;
                    for (int i = a; i <= e; i++)
                    {
                        sum += novelty[i];
                    }
                    beatStrengths[b] = sum / Math.Max(1, (e - a + 1));
                }

                // 4) Kandidaten scoren: Periodizität + Kontrast + Template-Korrelation (mit Phasen-Shift)
                int[] candidates = [2, 3, 4, 5, 6, 7, 8, 9, 12];
                var scoreByK = new Dictionary<int, (double score, double tplCorr)>();
                int bestK = 4;
                double bestScoreK = double.NegativeInfinity;

                for (int idx = 0; idx < candidates.Length; idx++)
                {
                    int K = candidates[idx];
                    if (K >= nBeats)
                    {
                        continue;
                    }

                    // Periodizität über Lag K
                    double ac = 0.0;
                    int c = 0;
                    for (int i = 0; i + K < nBeats; i++)
                    {
                        ac += beatStrengths[i] * beatStrengths[i + K];
                        c++;
                    }
                    if (c > 0)
                    {
                        ac /= c;
                    }

                    // Phasenmittelwerte
                    double[] phaseSum = new double[K];
                    int[] phaseCnt = new int[K];
                    for (int i = 0; i < nBeats; i++)
                    {
                        int r = i % K;
                        phaseSum[r] += beatStrengths[i];
                        phaseCnt[r]++;
                    }
                    double[] phaseAvg = new double[K];
                    for (int r = 0; r < K; r++)
                    {
                        phaseAvg[r] = phaseCnt[r] > 0 ? phaseSum[r] / phaseCnt[r] : 0.0;
                    }

                    // Kontrast (Downbeat-Betonung)
                    double meanPhase = phaseAvg.Average();
                    double maxPhase = phaseAvg.Max();
                    double contrast = maxPhase - meanPhase;

                    // Template-basiertes Matching (mit maximaler zirkularer Korrelation)
                    double[] tpl = BuildMeterTemplate(K);
                    double tplCorr = MaxCircularCorrelation(phaseAvg, tpl);

                    // Bars-Abdeckung (mehr Bars => vertrauenswürdiger)
                    double nBars = (double) nBeats / K;
                    double coverage = Math.Min(1.0, nBars / 4.0); // bis 4 Takte hochskalieren

                    // Gewichte: Template etwas stärker, Kontrast etwas schwächer
                    double score = 0.55 * ac + 0.20 * contrast + 0.50 * tplCorr;
                    score *= (0.7 + 0.3 * coverage);

                    // Subharmonik-Penalties/Boosts
                    if (K == 2)
                    {
                        score *= 0.92;      // 2/4 leicht benachteiligen
                    }

                    if (K == 4)
                    {
                        score *= 1.04;      // 4/4 leicht bevorzugen
                    }

                    if (K % 3 == 0)
                    {
                        score *= 1.06;  // 3er-Gruppen (3/4, 6/8, 12/8) fördern
                    }

                    scoreByK[K] = (score, tplCorr);

                    if (score > bestScoreK)
                    {
                        bestScoreK = score;
                        bestK = K;
                    }
                }

                // 4b) Harmonische Nachkorrektur:
                // - 2/4 vs 4/4/8/12: bevorzuge den größeren K, wenn Score nah dran (<=12% schlechter)
                // - 3/4 vs 6/8/12: bevorzuge den kleineren K, wenn Scores nah dran (<=12% schlechter)
                double relClose = 0.12;

                int[] chain2 = [2, 4, 8, 12];
                var present2 = chain2.Where(k => scoreByK.ContainsKey(k)).ToArray();
                if (present2.Length > 0)
                {
                    double maxInChain = present2.Max(k => scoreByK[k].score);
                    // wähle größten K, der nahe am Max liegt
                    int prefer = present2
                        .Where(k => scoreByK[k].score >= maxInChain * (1.0 - relClose))
                        .Max(); // größter K
                    if (scoreByK.ContainsKey(bestK) && present2.Contains(bestK))
                    {
                        bestK = prefer;
                    }
                    else if (!present2.Contains(bestK) && scoreByK.ContainsKey(prefer))
                    {
                        // falls der globale Sieger nicht in der Kette ist, aber 2/4 Problem sichtbar: wenn 2 knapp vor 4 liegt -> auf 4 heben
                        if (scoreByK.ContainsKey(2) && scoreByK.ContainsKey(4) && scoreByK[4].score >= scoreByK[2].score * (1.0 - relClose))
                        {
                            bestK = 4;
                        }
                    }
                }

                int[] chain3 = [3, 6, 12];
                var present3 = chain3.Where(k => scoreByK.ContainsKey(k)).ToArray();
                if (present3.Length > 0)
                {
                    double maxInChain = present3.Max(k => scoreByK[k].score);
                    // wähle kleinsten K, der nahe am Max liegt (3 bevorzugen)
                    int prefer = present3
                        .Where(k => scoreByK[k].score >= maxInChain * (1.0 - relClose))
                        .Min(); // kleinster K
                    if (present3.Contains(bestK))
                    {
                        bestK = prefer;
                    }
                }

                // 5) Normalisieren auf [0.125, 1.0]
                double normalized;
                if (bestK <= 4)
                {
                    normalized = bestK / 4.0;
                }
                else
                {
                    normalized = bestK <= 8 ? bestK / 8.0 : bestK / 12.0;
                }

                normalized = Math.Clamp(normalized, 0.125, 1.0);
                return (float) normalized;
            });
        }

        private static double[] BuildMeterTemplate(int K)
        {
            // Basis: Downbeat stark, ansonsten schwach
            double[] tpl = Enumerable.Repeat(0.25, K).ToArray();
            tpl[0] = 1.0;

            // 4/4-ähnlich: Akzent auf 1 und 3
            if (K % 4 == 0)
            {
                tpl[K / 2] = Math.Max(tpl[K / 2], 0.6);
            }

            // 3er-Gruppierung (3/4, 6/8, 12/x): Akzente bei 1/3 und 2/3 stärker
            if (K % 3 == 0)
            {
                tpl[K / 3] = Math.Max(tpl[K / 3], 0.8);
                tpl[(2 * K) / 3] = Math.Max(tpl[(2 * K) / 3], 0.7);
            }

            return tpl;
        }

        private static double MaxCircularCorrelation(double[] x, double[] y)
        {
            // Maximale Pearson-Korrelation über alle Zirkular-Phasen
            if (x.Length != y.Length || x.Length == 0)
            {
                return -1.0;
            }

            int K = x.Length;

            // z-Normalisierung
            double[] xn = (double[]) x.Clone();
            double[] yn = (double[]) y.Clone();
            NormalizeZeroMeanUnitVar(xn);
            NormalizeZeroMeanUnitVar(yn);

            double best = double.NegativeInfinity;
            for (int s = 0; s < K; s++)
            {
                double corr = PearsonCorr(xn, Rotate(yn, s));
                if (corr > best)
                {
                    best = corr;
                }
            }
            return best;
        }

        private static double[] Rotate(double[] arr, int shift)
        {
            int n = arr.Length;
            double[] r = new double[n];
            for (int i = 0; i < n; i++)
            {
                r[i] = arr[(i + shift) % n];
            }
            return r;
        }

        private static void NormalizeZeroMeanUnitVar(double[] v)
        {
            double m = v.Average();
            double s2 = v.Select(a => (a - m) * (a - m)).Sum() / Math.Max(1, v.Length);
            double s = s2 > 1e-12 ? Math.Sqrt(s2) : 1.0;
            for (int i = 0; i < v.Length; i++)
            {
                v[i] = (v[i] - m) / s;
            }
        }

        private static double PearsonCorr(double[] a, double[] b)
        {
            double sum = 0.0;
            for (int i = 0; i < a.Length; i++)
            {
                sum += a[i] * b[i];
            }

            return sum / Math.Max(1, a.Length);
        }
    }
}
