using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;
using NAudience.Core;
using System.Diagnostics;

namespace ModularAudience.Audio
{
    public static class BeatScanner
    {
        public static async Task<double> ScanBpmAsync(AudioObj obj, int windowSize = 65536, int lookingRange = 2, int? minBpm = null, int? maxBpm = null, bool autoGetTiming = false)
        {
            Stopwatch sw = Stopwatch.StartNew();

            try
            {
                if (obj == null || obj.Data == null || obj.Data.Length <= 0)
                {
                    return -1.0f;
                }

                // Use a wider window for BPM robustness when range is auto
                int effectiveWindow = windowSize;
                if (minBpm == null && maxBpm == null)
                {
                    // double the window to improve low-BPM sensitivity
                    effectiveWindow = Math.Max(windowSize, 131072);
                }

                var monoData = await obj.GetCurrentWindowAsync(effectiveWindow, lookingRange, true, true);
                if (monoData == null || monoData.Length == 0)
                {
                    return -1.0f;
                }

                // Optionally pre-scan timing to stabilize BPM selection (phase/measure bias)
                float timing = 1.0f;
                if (autoGetTiming)
                {
                    try { timing = await ScanTimingAsync(obj, windowSize, lookingRange); } catch { timing = 1.0f; }
                }

                // Auto-range if not provided
                var (minEff, maxEff) = ResolveTempoBounds(minBpm, maxBpm, obj.ScannedBpm > 0 ? (double?) obj.ScannedBpm : null);

                double bpm = await EstimateBpmAsync(monoData, obj.SampleRate, minEff, maxEff).ConfigureAwait(false);

                sw.Stop();
                obj["beatScan"] = (float) sw.Elapsed.TotalMilliseconds;

                if (bpm <= 0.0)
                {
                    return -1.0f;
                }

                obj.ScannedBpm = (float) bpm;

                return bpm * timing;
            }
            catch (Exception ex)
            {
                LogCollection.Log(ex);
                return 0.0;
            }
            finally
            {
                sw.Stop();
                obj["BpmScan"] = sw.Elapsed.TotalMilliseconds;
            }
        }

        public static async Task<float> ScanTimingAsync(AudioObj obj, int windowSize = 65536, int lookingRange = 2)
        {
            Stopwatch sw = Stopwatch.StartNew();

            try
            {
                if (obj == null || obj.Data == null || obj.Data.Length <= 0)
                {
                    return -1.0f;
                }

                var monoData = await obj.GetCurrentWindowAsync(windowSize, lookingRange, true, false);
                if (monoData == null || monoData.Length == 0 || obj.SampleRate <= 0)
                {
                    return -1.0f;
                }

                // Prefer BPM if already known to derive beatLag robustly
                double bpmHint = obj.ScannedBpm > 0 ? obj.ScannedBpm : 0.0;
                if (bpmHint <= 0.0)
                {
                    // Quick BPM estimation with generous bounds
                    var (minEff, maxEff) = ResolveTempoBounds(null, null, null);
                    try { bpmHint = await EstimateBpmAsync(monoData, obj.SampleRate, minEff, maxEff); } catch { bpmHint = 0.0; }
                }

                // Compute spectral-flux novelty at moderate resolution for timing
                int fftSize = ChooseFftSize(obj.SampleRate);
                int hopSize = Math.Max(256, fftSize / 4);
                var novelty = ComputeSpectralFluxNovelty(monoData, obj.SampleRate, fftSize, hopSize);
                if (novelty.Length < 16)
                {
                    return -1.0f;
                }

                // Normalize novelty
                NormalizeZeroMeanUnitVar(novelty);

                // If BPM known, compute beatLag in novelty-frames domain
                int noveltyRate = Math.Max(1, obj.SampleRate / hopSize);
                int beatLag = -1;
                if (bpmHint > 0)
                {
                    double lagF = (noveltyRate * 60.0) / bpmHint;
                    beatLag = (int) Math.Round(lagF);
                    // clamp
                    beatLag = Math.Clamp(beatLag, 2, Math.Max(2, novelty.Length / 8));
                }
                else
                {
                    // fallback: take ACF peak in default range 50..200 bpm
                    var acf = ComputeAcf(novelty);
                    var (minEff, maxEff) = ResolveTempoBounds(null, null, null);
                    beatLag = BestLagFromAcf(acf, noveltyRate, minEff, maxEff);
                }

                if (beatLag <= 0)
                {
                    return -1.0f;
                }

                // Derive periodic meter K using phase-binned beat strengths, same logic as before but on novelty
                int n = novelty.Length;
                int nBeats = Math.Max(0, n / Math.Max(1, beatLag));
                if (nBeats < 4)
                {
                    return -1.0f;
                }

                // Compute beat-strengths around each grid tick
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

                // Evaluate candidate meters
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

                    // Periodicity
                    double ac = 0.0; int c = 0;
                    for (int i = 0; i + K < nBeats; i++) { ac += beatStrengths[i] * beatStrengths[i + K]; c++; }
                    if (c > 0)
                    {
                        ac /= c;
                    }

                    // Phase averages
                    double[] phaseSum = new double[K]; int[] phaseCnt = new int[K];
                    for (int i = 0; i < nBeats; i++) { int r = i % K; phaseSum[r] += beatStrengths[i]; phaseCnt[r]++; }
                    double[] phaseAvg = new double[K];
                    for (int r = 0; r < K; r++)
                    {
                        phaseAvg[r] = phaseCnt[r] > 0 ? phaseSum[r] / phaseCnt[r] : 0.0;
                    }

                    double meanPhase = phaseAvg.Average();
                    double maxPhase = phaseAvg.Max();
                    double contrast = maxPhase - meanPhase;

                    double[] tpl = BuildMeterTemplate(K);
                    double tplCorr = MaxCircularCorrelation(phaseAvg, tpl);

                    double nBars = (double) nBeats / K;
                    double coverage = Math.Min(1.0, nBars / 4.0);

                    double score = 0.55 * ac + 0.20 * contrast + 0.50 * tplCorr;
                    score *= (0.7 + 0.3 * coverage);

                    if (K == 2)
                    {
                        score *= 0.92;
                    }

                    if (K == 4)
                    {
                        score *= 1.04;
                    }

                    if (K % 3 == 0)
                    {
                        score *= 1.06;
                    }

                    scoreByK[K] = (score, tplCorr);
                    if (score > bestScoreK) { bestScoreK = score; bestK = K; }
                }

                // Harmonic preference as before
                double relClose = 0.12;
                int[] chain2 = [2, 4, 8, 12];
                var present2 = chain2.Where(k => scoreByK.ContainsKey(k)).ToArray();
                if (present2.Length > 0)
                {
                    double maxInChain = present2.Max(k => scoreByK[k].score);
                    int prefer = present2.Where(k => scoreByK[k].score >= maxInChain * (1.0 - relClose)).Max();
                    if (scoreByK.ContainsKey(bestK) && present2.Contains(bestK))
                    {
                        bestK = prefer;
                    }
                    else if (!present2.Contains(bestK) && scoreByK.ContainsKey(prefer))
                    {
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
                    int prefer = present3.Where(k => scoreByK[k].score >= maxInChain * (1.0 - relClose)).Min();
                    if (present3.Contains(bestK))
                    {
                        bestK = prefer;
                    }
                }

                // Normalize to [0.125..1.0]
                double normalized = bestK <= 4 ? bestK / 4.0 : (bestK <= 8 ? bestK / 8.0 : bestK / 12.0);
                normalized = Math.Clamp(normalized, 0.125, 1.0);

                // Persist timing
                obj.Timing = (float) normalized;

                return (float) normalized;
            }
            catch (Exception ex)
            {
                LogCollection.Log(ex);
                return 0.0f;
            }
            finally
            {
                sw.Stop();
                obj["TimingScan"] = sw.Elapsed.TotalMilliseconds;
            }
        }

        public static async Task<string> ScanKeyAsync(AudioObj obj, int windowSize = 65536, int lookingRange = 2)
        {
            Stopwatch sw = Stopwatch.StartNew();

            try
            {
                if (obj == null || obj.Data == null || obj.Data.Length <= 0)
                {
                    return "Unknown";
                }

                // 1) Mono-Fenster nahe aktueller Position holen
                var monoData = await obj.GetCurrentWindowAsync(windowSize, lookingRange, true, false);
                if (monoData == null || monoData.Length == 0)
                {
                    return "Unknown";
                }

                // 2) Parameter für STFT/Chromagram
                int fs = Math.Max(1, obj.SampleRate);
                int fftSize = ChooseFftSize(fs);
                int hopSize = fftSize / 2; // 50% Overlap
                double minFreq = 50.0;     // Hz
                double maxFreq = Math.Min(5000.0, fs / 2.0 - 1.0); // Hz

                // 3) Chroma (HPCP-ähnlich) berechnen (parallelisiert)
                double[] chroma = await Task.Run(() =>
                    ComputeChromaSTFT(monoData, fs, fftSize, hopSize, minFreq, maxFreq, harmonics: 5, whitening: true, perFrameNormalize: true)
                ).ConfigureAwait(false);

                // 4) Key mittels Krumhansl-Schmuckler Templates schätzen
                var (keyIndex, isMinor, confidence) = EstimateKeyFromChroma(chroma);

                // Optional: Metrik speichern
                try { obj["KeyConfidence"] = confidence * 100.0; } catch { }

                if (keyIndex < 0)
                {
                    return "Unknown";
                }

                return KeyNameFromIndex(keyIndex, isMinor);
            }
            catch (Exception ex)
            {
                LogCollection.Log(ex);
                return string.Empty;
            }
            finally
            {
                sw.Stop();
                obj["KeyScan"] = sw.Elapsed.TotalMilliseconds;
            }
        }



        // --- Improved BPM estimator using spectral-flux novelty + ACF (auto-range aware) ---
        internal static async Task<double> EstimateBpmAsync(float[] samples, int sampleRate, int minBpm, int maxBpm)
        {
            if (samples == null || samples.Length == 0 || sampleRate <= 0 || minBpm <= 0 || maxBpm <= minBpm)
            {
                return 0.0;
            }

            return await Task.Run(() =>
            {
                // Spectral-flux novelty
                int fftSize = ChooseFftSize(sampleRate);
                int hopSize = Math.Max(256, fftSize / 4);
                var novelty = ComputeSpectralFluxNovelty(samples, sampleRate, fftSize, hopSize);
                if (novelty.Length < 8)
                {
                    return 0.0;
                }

                // Normalize novelty
                NormalizeZeroMeanUnitVar(novelty);

                // ACF of novelty
                var acf = ComputeAcf(novelty);
                int noveltyRate = Math.Max(1, sampleRate / hopSize);

                // Best lag selection with parabolic interpolation + harmonic disambiguation
                int bestLag = BestLagFromAcf(acf, noveltyRate, minBpm, maxBpm);
                if (bestLag <= 0)
                {
                    return 0.0;
                }

                double baseBpm = 60.0 * noveltyRate / bestLag;

                // Harmonic check: consider x0.5 and x2 BPMs within bounds; choose with stronger peak
                double bpmHalf = baseBpm / 2.0;
                double bpmDouble = baseBpm * 2.0;
                double bestBpm = baseBpm;
                double bestPeak = PeakAtBpm(acf, noveltyRate, baseBpm);

                void TryCandidate(double cand)
                {
                    if (cand < minBpm || cand > maxBpm)
                    {
                        return;
                    }

                    double pv = PeakAtBpm(acf, noveltyRate, cand);
                    // prefer simpler (lower) BPM when peaks are close
                    if (pv > bestPeak * 1.05 || (pv > bestPeak * 0.95 && cand < bestBpm))
                    {
                        bestPeak = pv;
                        bestBpm = cand;
                    }
                }
                TryCandidate(bpmHalf);
                TryCandidate(bpmDouble);

                // Light smoothing to common ranges (round to 0.1 bpm)
                bestBpm = Math.Round(bestBpm * 10.0) / 10.0;
                return bestBpm;
            }).ConfigureAwait(false);
        }

        // Helpers for BPM and Timing
        private static (int minBpm, int maxBpm) ResolveTempoBounds(int? minBpm, int? maxBpm, double? hint)
        {
            int minDefault = 50;
            int maxDefault = 200;

            int minEff = minBpm ?? 0;
            int maxEff = maxBpm ?? 0;

            if (minEff <= 0 && maxEff <= 0)
            {
                if (hint.HasValue && hint.Value > 0)
                {
                    // Use a band around hint
                    double h = hint.Value;
                    minEff = (int) Math.Max(30, Math.Round(h / 2.0));
                    maxEff = (int) Math.Min(260, Math.Round(h * 2.0));
                }
                else
                {
                    minEff = minDefault;
                    maxEff = maxDefault;
                }
            }
            else if (minEff <= 0)
            {
                minEff = Math.Max(30, (int) Math.Round((maxEff / 2.0)));
            }
            else if (maxEff <= 0)
            {
                maxEff = Math.Min(260, (int) Math.Round(minEff * 2.0));
            }

            if (minEff > maxEff)
            {
                (minEff, maxEff) = (maxEff, minEff);
            }

            // ensure reasonable span
            if (maxEff - minEff < 10)
            {
                int pad = 5;
                minEff = Math.Max(30, minEff - pad);
                maxEff = Math.Min(260, maxEff + pad);
            }

            return (minEff, maxEff);
        }

        private static double[] ComputeSpectralFluxNovelty(float[] samples, int sampleRate, int fftSize, int hopSize)
        {
            int n = samples.Length;
            if (n <= 0)
            {
                return Array.Empty<double>();
            }

            double[] window = HannWindow(fftSize);
            int nFrames = 1 + Math.Max(0, (n - fftSize) / hopSize);
            if (nFrames <= 1)
            {
                // Zero-pad
                nFrames = 1;
            }

            double[] flux = new double[nFrames];
            float[] prevMag = new float[fftSize / 2];

            for (int f = 0; f < nFrames; f++)
            {
                int start = f * hopSize;
                MathNet.Numerics.Complex32[] fft = new MathNet.Numerics.Complex32[fftSize];
                for (int i = 0; i < fftSize; i++)
                {
                    float s = (start + i) < n ? samples[start + i] : 0f;
                    fft[i] = new MathNet.Numerics.Complex32((float) (s * window[i]), 0f);
                }

                Fourier.Forward(fft, FourierOptions.Matlab);

                double sumPos = 0.0;
                int nyq = fftSize / 2;
                for (int k = 1; k < nyq; k++)
                {
                    float mag = fft[k].Magnitude;
                    float diff = mag - prevMag[k];
                    if (diff > 0)
                    {
                        sumPos += diff;
                    }

                    prevMag[k] = mag;
                }
                flux[f] = sumPos;
            }

            // Post-process: log compression + high-pass (subtract moving average)
            for (int i = 0; i < flux.Length; i++)
            {
                flux[i] = Math.Log10(1.0 + flux[i]);
            }

            int ma = Math.Max(1, flux.Length / 64);
            double[] running = new double[flux.Length];
            double acc = 0.0;
            for (int i = 0; i < flux.Length; i++)
            {
                acc += flux[i];
                if (i >= ma)
                {
                    acc -= flux[i - ma];
                }

                running[i] = acc / Math.Min(i + 1, ma);
            }
            for (int i = 0; i < flux.Length; i++)
            {
                flux[i] = Math.Max(0.0, flux[i] - running[i]);
            }

            return flux;
        }

        private static double[] ComputeAcf(double[] x)
        {
            int n = x.Length;
            int L = 1; while (L < 2 * n)
            {
                L <<= 1;
            }

            var fft = new MathNet.Numerics.Complex32[L];
            for (int i = 0; i < n; i++)
            {
                fft[i] = new MathNet.Numerics.Complex32((float) x[i], 0f);
            }

            for (int i = n; i < L; i++)
            {
                fft[i] = MathNet.Numerics.Complex32.Zero;
            }

            Fourier.Forward(fft, FourierOptions.Matlab);
            for (int i = 0; i < L; i++) { var v = fft[i]; fft[i] = new MathNet.Numerics.Complex32(v.Magnitude * v.Magnitude, 0f); }
            Fourier.Inverse(fft, FourierOptions.Matlab);
            double r0 = Math.Max(fft[0].Real, 1e-12f);
            double[] acf = new double[n];
            for (int i = 0; i < n; i++)
            {
                acf[i] = fft[i].Real / r0;
            }

            return acf;
        }

        private static int BestLagFromAcf(double[] acf, int frameRate, int minBpm, int maxBpm)
        {
            if (acf == null || acf.Length < 4)
            {
                return -1;
            }

            int n = acf.Length;
            int minLag = Math.Clamp((int) Math.Round(frameRate * 60.0 / Math.Max(maxBpm, 1)), 1, n - 1);
            int maxLag = Math.Clamp((int) Math.Round(frameRate * 60.0 / Math.Max(minBpm, 1)), minLag, n - 1);

            int bestLag = -1; double bestVal = double.NegativeInfinity;
            for (int k = minLag; k <= maxLag; k++)
            {
                double val = acf[k];
                if (val > bestVal)
                {
                    bestVal = val; bestLag = k;
                }
            }
            if (bestLag <= 0)
            {
                return -1;
            }

            // Parabolic interpolation around bestLag
            if (bestLag > minLag && bestLag < maxLag)
            {
                double y1 = acf[bestLag - 1];
                double y2 = acf[bestLag];
                double y3 = acf[bestLag + 1];
                double denom = (y1 - 2 * y2 + y3);
                if (Math.Abs(denom) > 1e-12)
                {
                    double delta = 0.5 * (y1 - y3) / denom;
                    double lagF = Math.Clamp(bestLag + delta, minLag, maxLag);
                    bestLag = (int) Math.Round(lagF);
                }
            }
            return bestLag;
        }

        private static double PeakAtBpm(double[] acf, int frameRate, double bpm)
        {
            if (bpm <= 0)
            {
                return 0.0;
            }

            double lag = (frameRate * 60.0) / bpm;
            int k = (int) Math.Round(lag);
            k = Math.Clamp(k, 1, acf.Length - 1);
            return Math.Max(0.0, acf[k]);
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

        private static int ChooseFftSize(int sampleRate)
        {
            // Wähle FFT-Größe zwischen 4096..16384 abhängig von Fs (2er-Potenz)
            int target = sampleRate <= 32000 ? 4096 : sampleRate <= 48000 ? 8192 : 16384;
            return NextPow2(target);
        }

        private static int NextPow2(int n)
        {
            int p = 1;
            while (p < n)
            {
                p <<= 1;
            }

            return p;
        }

        private static double[] HannWindow(int n)
        {
            double[] w = new double[n];
            for (int i = 0; i < n; i++)
            {
                w[i] = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / Math.Max(1, (n - 1))));
            }
            return w;
        }

        private static double[] ComputeChromaSTFT(float[] mono, int sampleRate, int fftSize, int hopSize, double minFreq, double maxFreq, int harmonics = 4, bool whitening = true, bool perFrameNormalize = true)
        {
            if (mono.Length < fftSize)
            {
                // Zero-Pad auf fftSize
                Array.Resize(ref mono, fftSize);
            }

            double[] window = HannWindow(fftSize);
            int nFrames = 1 + (mono.Length - fftSize) / Math.Max(1, hopSize);
            if (nFrames <= 0)
            {
                nFrames = 1;
            }

            // Globale Chromasumme
            double[] chromaSum = new double[12];

            // Parallelisierte Rahmenschleife mit Thread-lokalen Akkus
            System.Threading.Tasks.Parallel.For(0, nFrames,
                () => new double[12],
                (frameIdx, state, localChroma) =>
                {
                    int start = frameIdx * hopSize;
                    int end = Math.Min(start + fftSize, mono.Length);

                    // Fenster kopieren und fenstern
                    MathNet.Numerics.Complex32[] fft = new MathNet.Numerics.Complex32[fftSize];
                    for (int i = 0; i < fftSize; i++)
                    {
                        float s = (start + i) < mono.Length ? mono[start + i] : 0f;
                        fft[i] = new MathNet.Numerics.Complex32((float) (s * window[i]), 0f);
                    }

                    // FFT
                    Fourier.Forward(fft, FourierOptions.Matlab);

                    // Magnituden -> Pitch-Classes (HPCP-ähnlich)
                    // Einfaches Whitening: Frequenzgewichtung ~ sqrt(f) gegen 1/f-Spektralschräge
                    int nyq = fftSize / 2;
                    for (int k = 1; k < nyq; k++)
                    {
                        double freq = (double) k * sampleRate / fftSize;
                        if (freq < minFreq || freq > maxFreq)
                        {
                            continue;
                        }

                        double mag2 = fft[k].Magnitude * fft[k].Magnitude;
                        if (whitening)
                        {
                            mag2 *= Math.Sqrt(Math.Max(1e-9, freq));
                        }

                        // Harmonics summieren (verteilte Zuordnung auf Pitch-Classes)
                        for (int h = 1; h <= Math.Max(1, harmonics); h++)
                        {
                            double fH = freq * h;
                            if (fH > maxFreq)
                            {
                                break;
                            }

                            // MIDI (A4=440Hz -> 69)
                            double midi = 69.0 + 12.0 * Math.Log(fH / 440.0, 2.0);
                            int mLow = (int) Math.Floor(midi);
                            int mHigh = mLow + 1;
                            double wHigh = midi - mLow;
                            double wLow = 1.0 - wHigh;

                            int pcLow = ((mLow % 12) + 12) % 12;
                            int pcHigh = ((mHigh % 12) + 12) % 12;

                            // Gewichte mit 1/h abschwächen
                            double contrib = mag2 / h;
                            localChroma[pcLow] += contrib * wLow;
                            localChroma[pcHigh] += contrib * wHigh;
                        }
                    }

                    // Per-Frame Normalisierung (Robustheit)
                    if (perFrameNormalize)
                    {
                        NormalizeVectorInPlace(localChroma);
                    }

                    return localChroma;
                },
                localChroma =>
                {
                    lock (chromaSum)
                    {
                        for (int i = 0; i < 12; i++)
                        {
                            chromaSum[i] += localChroma[i];
                        }
                    }
                }
            );

            // Mittelwert über Frames
            for (int i = 0; i < 12; i++)
            {
                chromaSum[i] = Math.Max(0.0, chromaSum[i]);
            }
            NormalizeVectorInPlace(chromaSum);

            return chromaSum;
        }

        private static void NormalizeVectorInPlace(double[] v)
        {
            double sum = 0.0;
            for (int i = 0; i < v.Length; i++)
            {
                if (double.IsFinite(v[i]))
                {
                    sum += Math.Abs(v[i]);
                }
            }
            if (sum <= 1e-12)
            {
                return;
            }

            for (int i = 0; i < v.Length; i++)
            {
                v[i] = v[i] / sum;
            }
        }

        private static (int key, bool isMinor, double confidence) EstimateKeyFromChroma(double[] chroma)
        {
            if (chroma == null || chroma.Length != 12)
            {
                return (-1, false, 0.0);
            }

            // Krumhansl Templates
            double[] majorTpl = NormalizeTemplate(new double[] { 6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88 });
            double[] minorTpl = NormalizeTemplate(new double[] { 6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17 });

            int bestKey = -1;
            bool bestIsMinor = false;
            double bestScore = double.NegativeInfinity;
            double secondBest = double.NegativeInfinity;

            for (int shift = 0; shift < 12; shift++)
            {
                double scoreMaj = Dot(chroma, Rotate(majorTpl, shift));
                if (scoreMaj > bestScore)
                {
                    secondBest = bestScore;
                    bestScore = scoreMaj;
                    bestKey = shift;
                    bestIsMinor = false;
                }
                else if (scoreMaj > secondBest)
                {
                    secondBest = scoreMaj;
                }

                double scoreMin = Dot(chroma, Rotate(minorTpl, shift));
                if (scoreMin > bestScore)
                {
                    secondBest = bestScore;
                    bestScore = scoreMin;
                    bestKey = shift;
                    bestIsMinor = true;
                }
                else if (scoreMin > secondBest)
                {
                    secondBest = scoreMin;
                }
            }

            double confidence = bestScore > 0 ? Math.Max(0.0, (bestScore - Math.Max(0.0, secondBest)) / Math.Max(1e-9, bestScore)) : 0.0;

            return (bestKey, bestIsMinor, confidence);
        }

        private static double[] NormalizeTemplate(double[] tpl)
        {
            double sum = tpl.Sum();
            if (sum <= 1e-12)
            {
                return tpl;
            }

            double[] r = new double[tpl.Length];
            for (int i = 0; i < tpl.Length; i++)
            {
                r[i] = tpl[i] / sum;
            }
            return r;
        }

        private static double Dot(double[] a, double[] b)
        {
            double s = 0.0;
            for (int i = 0; i < a.Length; i++)
            {
                s += a[i] * b[i];
            }
            return s;
        }

        private static string KeyNameFromIndex(int key, bool isMinor)
        {
            // 0=C, 1=C#, ..., 11=B
            string[] names = new[] { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
            string root = names[((key % 12) + 12) % 12];
            return isMinor ? $"{root} minor" : $"{root} major";
        }
    }
}
