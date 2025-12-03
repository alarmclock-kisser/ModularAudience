// TimeStretcher_V2.cs
// High-quality, optimized FFT-based time-stretcher (phase-vocoder) with automatic chunking/overlap
// Focus: maximum audible quality, minimal allocations, re-used buffers, parallel processing where sensible.
// Uses MathNet.Numerics.IntegralTransforms.Fourier for FFT/IFFT.

// ASSUMPTIONS (match user's existing AudioObj from TimeStretcher v1):
// - AudioObj.Data : float[] (mono interleaved samples)
// - AudioObj.SampleRate : int
// - AudioObj.OverlapSize : int (optional; not required)
// - AudioObj.SetData(float[]) or AudioObj.Data = ... to write back
// If your AudioObj is structured differently (stereo interleaved, per-channel arrays), adapt the I/O lines.

using MathNet.Numerics.IntegralTransforms;
using ModularAudience.Audio;
using System;
using System.Buffers;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

public static class TimeStretcher_V2
{
    /// <summary>
    /// High-quality and optimized phase-vocoder timestretch. No pitch-shift. Async, reports progress.
    /// Signature kept as requested: nullable chunkSize and overlap are auto-chosen when null.
    /// </summary>
    /// <param name="track">AudioObj with Data (float[] mono)</param>
    /// <param name="stretchFactor">>0.0 (1.0 = no change)</param>
    /// <param name="chunkSize">FFT size (power-of-two). If null auto-chosen.</param>
    /// <param name="overlap">Overlap 0..0.95. If null auto-chosen.</param>
    /// <param name="progress">Reports 0.0..1.0</param>
    public static async Task Timestretch_V2Async(AudioObj track, double stretchFactor, int? chunkSize = null, float? overlap = null, IProgress<double>? progress = null, CancellationToken? ct = null)
    {
        if (track == null)
        {
            throw new ArgumentNullException(nameof(track));
        }

        if (stretchFactor <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(stretchFactor));
        }

        CancellationToken ctLocal = ct ?? CancellationToken.None;

        await Task.Run(() =>
        {
            ctLocal.ThrowIfCancellationRequested();

            float[] input = track.Data ?? [];
            int sampleRate = track.SampleRate;
            int samples = input.Length;
            if (samples == 0)
            {
                return;
            }

            int frameSize = chunkSize ?? AutoChooseFrameSize(sampleRate, samples);
            frameSize = NextPowerOfTwo(frameSize);
            float ov = overlap ?? AutoChooseOverlap(frameSize, sampleRate);
            ov = Math.Clamp(ov, 0.0f, 0.95f);


            int hopAnalysis = Math.Max(1, (int) (frameSize * (1.0 - ov)));
            int hopSynthesis = Math.Max(1, (int) Math.Round(hopAnalysis * stretchFactor));


            // Use Blackman-Harris window for better sidelobe suppression
            double[] window = CreateBlackmanHarrisWindow(frameSize);


            long estimatedOut = (long) Math.Ceiling(samples * stretchFactor) + frameSize * 2;
            var pool = ArrayPool<double>.Shared;
            double[] outBuffer = pool.Rent((int) Math.Min(int.MaxValue, estimatedOut + frameSize + 16));
            double[] winSum = pool.Rent((int) Math.Min(int.MaxValue, estimatedOut + frameSize + 16));
            Array.Clear(outBuffer, 0, outBuffer.Length);
            Array.Clear(winSum, 0, winSum.Length);


            int fftSize = frameSize;
            double[] omega = new double[fftSize];
            for (int k = 0; k < fftSize; k++)
            {
                omega[k] = 2.0 * Math.PI * k / fftSize;
            }

            int idealSegmentFrames = Math.Max(1, Environment.ProcessorCount * 4);
            int samplesPerSegment = Math.Max(fftSize, hopAnalysis * idealSegmentFrames);
            int segments = (samples + samplesPerSegment - 1) / samplesPerSegment;


            long totalFrames = 0;
            for (int s = 0; s + fftSize <= samples; s += hopAnalysis)
            {
                totalFrames++;
            }

            long processedFrames = 0;


            object progLock = new();


            // region locks array
            object[] regionLocks = Enumerable.Range(0, 64).Select(_ => new object()).ToArray();

            try
            {
                // Process segments in parallel; we call the helper that includes phase-locking
                var popt = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = ctLocal };

                try
                {
                    Parallel.For(0, segments, popt, segIndex =>
                    {
                        if (ctLocal.IsCancellationRequested)
                        {
                            return;
                        }

                        int segStartSample = segIndex * samplesPerSegment;
                        int segEndSample = Math.Min(samples, segStartSample + samplesPerSegment + fftSize);
                        if (segStartSample >= segEndSample)
                        {
                            return;
                        }

                        ProcessSegmentWithPhaseLocking(
                        segIndex,
                        segStartSample,
                        segEndSample,
                        input,
                        samples,
                        fftSize,
                        hopAnalysis,
                        hopSynthesis,
                        window,
                        omega,
                        outBuffer,
                        winSum,
                        (float) stretchFactor,
                        regionLocks,
                        ref processedFrames,
                        totalFrames,
                        progress,
                        progLock,
                        ctLocal);
                    });
                }
                catch (OperationCanceledException) when (ctLocal.IsCancellationRequested)
                {
                    // Abbruch erwartet: sauber zurückkehren
                    return;
                }


                // Final normalization and trim
                int finalLen = outBuffer.Length;
                int lastNonZero = finalLen - 1;
                while (lastNonZero >= 0 && Math.Abs(outBuffer[lastNonZero]) < 1e-8 && winSum[lastNonZero] < 1e-8)
                {
                    lastNonZero--;
                }

                int trimmedLen = Math.Max(1, lastNonZero + 1);


                float[] finalOut = new float[trimmedLen];
                for (int i = 0; i < trimmedLen; i++)
                {
                    double s = (winSum[i] > 1e-8) ? outBuffer[i] / winSum[i] : outBuffer[i];
                    if (s > 1.0)
                    {
                        s = 1.0 + (s - 1.0) / (1.0 + (s - 1.0));
                    }

                    if (s < -1.0)
                    {
                        s = -1.0 + (s + 1.0) / (1.0 - (s + 1.0));
                    }

                    finalOut[i] = (float) s;
                }


                ctLocal.ThrowIfCancellationRequested();

                track.Data = finalOut;
                track.Bpm = (float) ((double) track.Bpm * stretchFactor);

                progress?.Report(1.0);
            }
            finally
            {
                try { pool.Return(outBuffer, clearArray: true); } catch { }
                try { pool.Return(winSum, clearArray: true); } catch { }
            }
        }, ctLocal).ConfigureAwait(false);
    }
    // --- Lightweight region locking helper ---
    // Create a small array of lock objects to reduce contention and overhead; map index -> lockObj via modulo
    static readonly object[] regionLocks = Enumerable.Range(0, 64).Select(_ => new object()).ToArray();
    static object GetRegionLock(int index) => regionLocks[(index & 0x3F)];

    // --- Helpers ---
    static void EnsureCapacityForIndex(ref double[] outBuffer, ref double[] winSum, int required)
    {
        if (required <= outBuffer.Length)
        {
            return;
        }

        int newSize = outBuffer.Length;
        while (newSize < required)
        {
            newSize = newSize * 3 / 2 + 256;
        }

        Array.Resize(ref outBuffer, newSize);
        Array.Resize(ref winSum, newSize);
    }

    static int AutoChooseFrameSize(int sampleRate, int frames)
    {
        if (sampleRate >= 96000)
        {
            LogCollection.Log("Using very large frame size (16384) for high sample rate (>= 96000)");
            return 16384;
        }

        if (sampleRate >= 48000)
        {
            LogCollection.Log("Using large frame size (8192) for sample rate (>= 48000)");
            return 8192;
        }

        if (sampleRate >= 32000)
        {
            LogCollection.Log("Using medium-large frame size (4196) for sample rate (>= 32000)");
            return 4196;
        }

        if (sampleRate >= 22050)
        {
            LogCollection.Log("Using medium frame size (2048) for sample rate (>= 22050)");
            return 2048;
        }

        LogCollection.Log("Using standard frame size (1024) for low sample rate (< 22050)");
        return 1024;
    }

    static float AutoChooseOverlap(int frameSize, int sampleRate)
    {
        if (frameSize >= 8192)
        {
            LogCollection.Log("Using high overlap (0.88) for large frame size (>= 8192)");
            return 0.80f;
        }

        if (frameSize >= 4096)
        {
            LogCollection.Log("Using medium-high overlap (0.8) for frame size >= 4096");
            return 0.75f;
        }

        if (frameSize >= 2048)
        {
            LogCollection.Log("Using medium overlap (0.75) for frame size >= 2048");
            return 0.66f;
        }

        LogCollection.Log("Using standard overlap (0.5) for small frame size (< 2048)");
        return 0.5f;
    }

    static int NextPowerOfTwo(int v)
    {
        if (v < 1)
        {
            return 1;
        }

        int p = 1;
        while (p < v)
        {
            p <<= 1;
        }

        return p;
    }

    static double[] CreateHannWindow(int n)
    {
        double[] w = new double[n];
        for (int i = 0; i < n; i++)
        {
            w[i] = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / n));
        }

        return w;
    }

    static double[] CreateBlackmanHarrisWindow(int n)
    {
        // 4-term Blackman-Harris window (good sidelobe suppression)
        // w[n] = a0 - a1*cos(2pi n/N) + a2*cos(4pi n/N) - a3*cos(6pi n/N)
        const double a0 = 0.35875;
        const double a1 = 0.48829;
        const double a2 = 0.14128;
        const double a3 = 0.01168;

        double[] w = new double[n];
        double N = (double) n;
        for (int i = 0; i < n; i++)
        {
            double phase = 2.0 * Math.PI * i / N;
            w[i] = a0
                 - a1 * Math.Cos(phase)
                 + a2 * Math.Cos(2.0 * phase)
                 - a3 * Math.Cos(3.0 * phase);
        }
        return w;
    }


    static double PrincipalArgument(double phase)
    {
        double v = phase;
        while (v <= -Math.PI)
        {
            v += 2.0 * Math.PI;
        }

        while (v > Math.PI)
        {
            v -= 2.0 * Math.PI;
        }

        return v;
    }



    static double EstimateTransientThreshold(double[] mags, int sampleRate)
    {
        if (mags == null || mags.Length == 0)
        {
            return 1e-9;
        }

        var copy = mags.Where(x => !double.IsNaN(x)).ToArray();
        if (copy.Length == 0)
        {
            return 1e-9;
        }

        Array.Sort(copy);
        double median = copy[copy.Length / 2];


        double[] dev = new double[copy.Length];
        for (int i = 0; i < copy.Length; i++)
        {
            dev[i] = Math.Abs(copy[i] - median);
        }

        Array.Sort(dev);
        double mad = dev[dev.Length / 2];


        double sigma = Math.Max(1e-9, mad * 1.4826);


        double k = 6.0; // conservative
        double thresh = median + k * sigma;
        thresh = Math.Max(thresh, 1e-9);
        return thresh;
    }

    static void ProcessSegmentWithPhaseLocking(
int segIndex,
int segStartSample,
int segEndSample,
float[] input,
int samples,
int fftSize,
int hopAnalysis,
int hopSynthesis,
double[] window,
double[] omega,
double[] outBufferShared,
double[] winSumShared,
float stretchFactorFloat,
object[] regionLocks,
ref long processedFramesCounter,
long totalFrames,
IProgress<double>? progress,
object progLock,
CancellationToken ct)
    {
        Complex[] aBuf = new Complex[fftSize];
        Complex[] sBuf = new Complex[fftSize];
        double[] prevPhase = new double[fftSize];
        double[] sumPhase = new double[fftSize];
        double[] prevMag = new double[fftSize];


        for (int pos = segStartSample; pos + fftSize <= segEndSample && pos + fftSize <= samples; pos += hopAnalysis)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
            }
            catch
            {
                return;
            }

            for (int n = 0; n < fftSize; n++)
            {
                double v = (pos + n < samples) ? input[pos + n] * window[n] : 0.0;
                aBuf[n] = new Complex(v, 0.0);
            }


            Fourier.Forward(aBuf, FourierOptions.Matlab);


            double[] mag = new double[fftSize];
            double[] phase = new double[fftSize];
            double fluxSum = 0.0;
            for (int k = 0; k < fftSize; k++)
            {
                double re = aBuf[k].Real;
                double im = aBuf[k].Imaginary;
                double m = Math.Sqrt(re * re + im * im);
                double ph = Math.Atan2(im, re);
                mag[k] = m;
                phase[k] = ph;


                double diff = m - prevMag[k];
                if (diff > 0)
                {
                    fluxSum += diff;
                }

                prevMag[k] = m;
            }


            bool isTransient = fluxSum > EstimateTransientThreshold(mag, 0);


            // find peaks
            List<int> peakBins = [];
            for (int k = 1; k < fftSize - 1; k++)
            {
                if (mag[k] > mag[k - 1] && mag[k] >= mag[k + 1] && mag[k] > 1e-6)
                {
                    peakBins.Add(k);
                }
            }


            for (int k = 0; k < fftSize; k++)
            {
                double delta = PrincipalArgument(phase[k] - prevPhase[k] - omega[k] * hopAnalysis);
                double trueFreq = omega[k] + delta / hopAnalysis;


                bool nearPeak = false;
                const int radius = 2;
                foreach (var pk in peakBins)
                {
                    if (Math.Abs(pk - k) <= radius) { nearPeak = true; break; }
                }


                if (isTransient)
                {
                    sumPhase[k] = phase[k];
                }
                else if (nearPeak && peakBins.Count > 0)
                {
                    int best = -1; int minDiff = int.MaxValue;
                    foreach (var pk in peakBins)
                    {
                        int d = Math.Abs(pk - k);
                        if (d < minDiff) { minDiff = d; best = pk; }
                    }
                    if (best >= 0)
                    {
                        sumPhase[k] = phase[best];
                    }
                    else
                    {
                        sumPhase[k] += trueFreq * hopSynthesis;
                    }
                }
                else
                {
                    sumPhase[k] += trueFreq * hopSynthesis;
                }


                double reS = mag[k] * Math.Cos(sumPhase[k]);
                double imS = mag[k] * Math.Sin(sumPhase[k]);
                sBuf[k] = new Complex(reS, imS);
                prevPhase[k] = phase[k];
            }


            Fourier.Inverse(sBuf, FourierOptions.Matlab);


            int writePos = (int) Math.Round(pos * stretchFactorFloat);
            object rlock = regionLocks[(writePos & 0x3F)];
            lock (rlock)
            {
                int needed = writePos + fftSize;
                if (needed > outBufferShared.Length)
                {
                    needed = Math.Min(needed, outBufferShared.Length);
                }

                for (int n = 0; n < fftSize && writePos + n < outBufferShared.Length; n++)
                {
                    double val = sBuf[n].Real * window[n];
                    outBufferShared[writePos + n] += val;
                    winSumShared[writePos + n] += window[n];
                }
            }


            lock (progLock)
            {
                processedFramesCounter++;
                if (progress != null)
                {
                    double p = Math.Min(1.0, (double) processedFramesCounter / Math.Max(1.0, (double) totalFrames));
                    progress.Report(p);
                }
            }
        }
    }
}
