using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace ModularAudience.Audio.Processors_V1
{
    public static class PitchShifter
    {
        public static async Task<IEnumerable<List<AudioObj>>> CreatePitchShiftsBatchAsync(IEnumerable<AudioObj> samples, int keysRange = 8, float semitoneDelta = 1.0f, bool withoutTimestretch = true, IProgress<double>? progress = null)
        {
            if (samples == null)
            {
                throw new ArgumentNullException(nameof(samples));
            }

            var sampleList = samples.ToList();
            int total = Math.Max(1, sampleList.Count);

            var tasks = new List<Task<List<AudioObj>>>(sampleList.Count);
            for (int i = 0; i < sampleList.Count; i++)
            {
                int idx = i;
                var s = sampleList[idx];
                IProgress<double>? childProgress = null;
                if (progress != null)
                {
                    childProgress = new Progress<double>(p =>
                    {
                        double overall = (idx + Math.Clamp(p, 0.0, 1.0)) / (double)total;
                        progress.Report(Math.Clamp(overall, 0.0, 1.0));
                    });
                }

                tasks.Add(Task.Run(async () =>
                {
                    var list = ((await (withoutTimestretch ?
                        CreatePitchShiftsWithoutTimestretchAsync(s, keysRange, semitoneDelta, childProgress) :
                        CreatePitchShiftsAsync(s, keysRange, semitoneDelta, childProgress)
                    ).ConfigureAwait(false)).ToList());
                    childProgress?.Report(1.0);
                    return list;
                }));
            }

            var results = await Task.WhenAll(tasks).ConfigureAwait(false);

            progress?.Report(1.0);

            return results.ToList();
        }

        public static async Task<IEnumerable<AudioObj>> CreatePitchShiftsAsync(AudioObj sample, int keysRange = 8, float semitoneDelta = 1.0f, IProgress<double>? progress = null)
        {
            if (sample == null)
            {
                throw new ArgumentNullException(nameof(sample));
            }

            keysRange = Math.Max(0, keysRange);
            semitoneDelta = Math.Max(0.01f, semitoneDelta);

            // Build shifts list (-keysRange .. keysRange stepping by semitoneDelta)
            var shifts = new List<float>();
            for (float s = -keysRange; s <= keysRange + 0.000001f; s += semitoneDelta)
            {
                float rounded = (float)Math.Round(s, 6);
                if (!shifts.Contains(rounded))
                {
                    shifts.Add(rounded);
                }
            }

            int count = shifts.Count;
            var results = new AudioObj[count];

            var progressLock = new object();
            double[] elementProgress = new double[count];

            string baseName = string.IsNullOrWhiteSpace(sample.Name) ? "sample" : sample.Name;

            var tasks = Enumerable.Range(0, count).Select(index =>
                Task.Run(async () =>
                {
                    float semitones = shifts[index];
                    AudioObj result;

                    if (Math.Abs(semitones) < 1e-6f)
                    {
                        // no pitch change: clone original (preserve all metadata)
                        result = sample.Clone();
                        result.Name = baseName; // keep base name for unshifted
                    }
                    else
                    {
                        result = await PitchShiftOneAsync(sample, semitones, new Progress<double>(p =>
                        {
                            lock (progressLock)
                            {
                                elementProgress[index] = Math.Clamp(p, 0.0, 1.0);
                                double overall = elementProgress.Sum() / count;
                                progress?.Report(overall);
                            }
                        })).ConfigureAwait(false);

                        // Ensure clone inherits important non-audio metadata from original
                        try
                        {
                            result.Bpm = sample.Bpm;
                        }
                        catch { }
                    }

                    // Ensure name format: baseName + " +Nst" or " -Nst" (omit for 0)
                    if (Math.Abs(semitones) < 1e-6f)
                    {
                        result.Name = baseName;
                    }
                    else
                    {
                        string sign = semitones > 0 ? "+" : "-";
                        float absSteps = Math.Abs(semitones);
                        result.Name = $"{baseName}{sign}{absSteps:0.##}st";
                    }

                    // Preserve BPM and other metadata fields from sample if present
                    try { result.SampleRate = sample.SampleRate; } catch { }
                    try { result.Channels = sample.Channels; } catch { }
                    try { result.BitDepth = sample.BitDepth; } catch { }
                    try { result.ScannedBpm = sample.ScannedBpm; } catch { }
                    try { result.ScannedTiming = sample.ScannedTiming; } catch { }
                    try { result.ScannedKey = sample.ScannedKey; } catch { }
                    try { result.Timing = sample.Timing; } catch { }
                    try { result.Volume = sample.Volume; } catch { }
                    try { result.ChunkSize = sample.ChunkSize; } catch { }
                    try { result.OverlapSize = sample.OverlapSize; } catch { }
                    try { result.StretchFactor = sample.StretchFactor; } catch { }
                    try { result.SampleTag = sample.SampleTag; } catch { }
                    try { result.ScrollOffset = sample.ScrollOffset; } catch { }
                    try { result.StartingOffset = sample.StartingOffset; } catch { }
                    try { result.SelectionStart = sample.SelectionStart; result.SelectionEnd = sample.SelectionEnd; } catch { }
                    try { result.LoopEnabled = sample.LoopEnabled; } catch { }

                    // Copy metrics if available
                    try
                    {
                        result.Metrics.Clear();
                        foreach (var kv in sample.Metrics)
                        {
                            result.Metrics[kv.Key] = kv.Value;
                        }
                    }
                    catch { }

                    results[index] = result;

                })).ToArray();

            await Task.WhenAll(tasks).ConfigureAwait(false);

            progress?.Report(1.0);

            return results.Where(r => r != null).ToList();
        }

        public static async Task<IEnumerable<AudioObj>> CreatePitchShiftsWithoutTimestretchAsync(AudioObj sample, int keysRange = 8, float semitoneDelta = 1.0f, IProgress<double>? progress = null)
        {
            if (sample == null) throw new ArgumentNullException(nameof(sample));

            keysRange = Math.Max(0, keysRange);
            semitoneDelta = Math.Max(0.01f, semitoneDelta);

            // Build shifts list
            var shifts = new List<float>();
            for (float s = -keysRange; s <= keysRange + 0.000001f; s += semitoneDelta)
            {
                float rounded = (float)Math.Round(s, 6);
                if (!shifts.Contains(rounded)) shifts.Add(rounded);
            }

            int count = shifts.Count;
            var results = new AudioObj[count];

            var progressLock = new object();
            double[] elementProgress = new double[count];

            string baseName = string.IsNullOrWhiteSpace(sample.Name) ? "sample" : sample.Name;

            int originalFrames = (int)Math.Max(0, (sample.Data?.LongLength ?? 0) / Math.Max(1, sample.Channels));

            var tasks = Enumerable.Range(0, count).Select(index => Task.Run(async () =>
            {
                float semitones = shifts[index];
                AudioObj result;

                if (Math.Abs(semitones) < 1e-6f)
                {
                    result = sample.Clone();
                    result.Name = baseName;
                }
                else
                {
                    // 1) Resample (changes duration)
                    var resampled = await PitchShiftOneAsync(sample, semitones, new Progress<double>(p =>
                    {
                        lock (progressLock)
                        {
                            elementProgress[index] = Math.Clamp(p * 0.5, 0.0, 1.0); // first half
                            double overall = elementProgress.Sum() / count;
                            progress?.Report(overall);
                        }
                    })).ConfigureAwait(false);

                    // 2) Time-stretch resampled back to original frame count
                    int resampledFrames = (int)Math.Max(0, (resampled.Data?.LongLength ?? 0) / Math.Max(1, resampled.Channels));
                    int targetFrames = Math.Max(1, originalFrames);

                    float[] stretchedData;
                    if (resampledFrames == 0)
                    {
                        stretchedData = [];
                    }
                    else if (resampledFrames == targetFrames)
                    {
                        stretchedData = resampled.Data ?? [];
                    }
                    else
                    {
                        // perform phase-vocoder based time-stretch per channel
                        stretchedData = PhaseVocoderTimeStretch(resampled.Data ?? [], resampled.Channels, resampledFrames, targetFrames, new Progress<double>(p =>
                        {
                            lock (progressLock)
                            {
                                // map second half of progress (0.0..1.0) to 0.5..1.0 portion for this element
                                elementProgress[index] = Math.Clamp(0.5 + p * 0.5, 0.0, 1.0);
                                double overall = elementProgress.Sum() / count;
                                progress?.Report(overall);
                            }
                        }));
                    }

                    // build result clone and set data
                    result = resampled.Clone();
                    result.Data = stretchedData;
                    result.Length = stretchedData.LongLength;

                    // Update duration
                    try
                    {
                        int sr = Math.Max(1, result.SampleRate);
                        result.Duration = TimeSpan.FromSeconds((double)(result.Length / Math.Max(1, result.Channels)) / sr);
                    }
                    catch { }

                    // ensure BPM copied
                    try { result.Bpm = sample.Bpm; } catch { }
                }

                // Name
                if (Math.Abs(semitones) < 1e-6f)
                {
                    results[index] = sample.Clone();
                    results[index].Name = baseName;
                }
                else
                {
                    string sign = semitones > 0 ? "+" : "-";
                    float absSteps = Math.Abs(semitones);
                    var r = results[index] = new AudioObj();
                    // Already have 'result' constructed; copy into results[index]
                    r = result; // reuse
                    r.Name = $"{baseName}{sign}{absSteps:0.##}st";
                    results[index] = r;
                }

            })).ToArray();

            await Task.WhenAll(tasks).ConfigureAwait(false);

            progress?.Report(1.0);

            return results.Where(r => r != null).ToList();
        }

        private static Task<AudioObj> PitchShiftOneAsync(AudioObj sample, float semitones, IProgress<double>? progress = null)
        {
            return Task.Run(() =>
            {
                if (sample == null)
                {
                    throw new ArgumentNullException(nameof(sample));
                }

                float pitchFactor = (float)Math.Pow(2.0, semitones / 12.0);

                float[] inData = sample.Data ?? [];
                int channels = Math.Max(1, sample.Channels);
                long totalSamples = inData.LongLength;
                long inputFrames = Math.Max(0L, totalSamples / channels);
                if (inputFrames == 0)
                {
                    var emptyClone = sample.Clone();
                    emptyClone.Data = [];
                    emptyClone.Length = 0;
                    return emptyClone;
                }

                long outputFrames = Math.Max(1L, (long)Math.Round(inputFrames / pitchFactor));

                const long MaxFrames = 20_000_000;
                if (outputFrames > MaxFrames)
                {
                    outputFrames = MaxFrames;
                }

                var inChannels = new float[channels][];
                for (int c = 0; c < channels; c++)
                {
                    inChannels[c] = new float[inputFrames];
                }

                for (long f = 0; f < inputFrames; f++)
                {
                    for (int c = 0; c < channels; c++)
                    {
                        long srcIndex = f * channels + c;
                        inChannels[c][f] = inData[srcIndex];
                    }
                }

                var outChannels = new float[channels][];
                for (int c = 0; c < channels; c++)
                {
                    outChannels[c] = new float[outputFrames];
                }

                const int a = 8;
                int kernelRadius = a;

                int maxDegreeOfParallelism = Math.Max(1, Math.Min(Environment.ProcessorCount, 8));

                int partitionCount = maxDegreeOfParallelism;
                var ranges = PartitionRange(0L, outputFrames, partitionCount);

                long processed = 0;
                object progLock = new object();

                Parallel.For(0, ranges.Length, new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism }, pi =>
                {
                    var r = ranges[pi];
                    long start = r.start;
                    long end = r.end;

                    for (long outFrame = start; outFrame < end; outFrame++)
                    {
                        double srcPos = outFrame * (inputFrames / (double)outputFrames);
                        long srcIndexFloor = (long)Math.Floor(srcPos);
                        double frac = srcPos - srcIndexFloor;

                        int left = (int)Math.Max(0, srcIndexFloor - kernelRadius + 1);
                        int right = (int)Math.Min(inputFrames - 1, srcIndexFloor + kernelRadius);

                        for (int c = 0; c < channels; c++)
                        {
                            double sum = 0.0;
                            double wsum = 0.0;
                            var ch = inChannels[c];

                            for (int j = left; j <= right; j++)
                            {
                                double x = srcPos - j;
                                double w = LanczosWindowedSinc(x, a);
                                sum += ch[j] * w;
                                wsum += Math.Abs(w);
                            }

                            float sampleValue = wsum > 1e-12 ? (float)(sum / wsum) : 0f;
                            outChannels[c][outFrame] = sampleValue;
                        }

                        if (progress != null)
                        {
                            bool report = false;
                            lock (progLock)
                            {
                                processed++;
                                if (processed % 2048 == 0 || outFrame == end - 1)
                                {
                                    report = true;
                                }
                            }
                            if (report)
                            {
                                double p;
                                lock (progLock)
                                {
                                    p = Math.Clamp(processed / (double)outputFrames, 0.0, 1.0);
                                }
                                try { progress.Report(p); } catch { }
                            }
                        }
                    }
                });

                long outputTotalSamples = outputFrames * channels;
                var outData = new float[outputTotalSamples];
                for (long f = 0; f < outputFrames; f++)
                {
                    for (int c = 0; c < channels; c++)
                    {
                        outData[f * channels + c] = outChannels[c][f];
                    }
                }

                var clone = sample.Clone();
                clone.Data = outData;
                clone.Length = outData.LongLength;

                // Preserve many metadata fields from original
                try { clone.SampleRate = sample.SampleRate; } catch { }
                try { clone.Channels = sample.Channels; } catch { }
                try { clone.BitDepth = sample.BitDepth; } catch { }
                try { clone.Bpm = sample.Bpm; } catch { }
                try { clone.ScannedBpm = sample.ScannedBpm; } catch { }
                try { clone.ScannedTiming = sample.ScannedTiming; } catch { }
                try { clone.ScannedKey = sample.ScannedKey; } catch { }
                try { clone.Timing = sample.Timing; } catch { }
                try { clone.Volume = sample.Volume; } catch { }
                try { clone.ChunkSize = sample.ChunkSize; } catch { }
                try { clone.OverlapSize = sample.OverlapSize; } catch { }
                try { clone.StretchFactor = sample.StretchFactor; } catch { }
                try { clone.SampleTag = sample.SampleTag; } catch { }
                try { clone.ScrollOffset = sample.ScrollOffset; } catch { }
                try { clone.StartingOffset = sample.StartingOffset; } catch { }
                try { clone.SelectionStart = sample.SelectionStart; clone.SelectionEnd = sample.SelectionEnd; } catch { }
                try { clone.LoopEnabled = sample.LoopEnabled; } catch { }

                try
                {
                    clone.Metrics.Clear();
                    foreach (var kv in sample.Metrics)
                    {
                        clone.Metrics[kv.Key] = kv.Value;
                    }
                }
                catch { }

                // Update duration
                try
                {
                    int sr = Math.Max(1, clone.SampleRate);
                    clone.Duration = TimeSpan.FromSeconds((double)outputFrames / sr);
                }
                catch { }

                // Name will be set by caller to follow naming convention

                progress?.Report(1.0);

                return clone;
            });
        }

        // Phase vocoder time-stretch: operates on interleaved float[] data
        private static float[] PhaseVocoderTimeStretch(float[] data, int channels, int inputFrames, int targetFrames, IProgress<double>? progress = null)
        {
            if (channels <= 0) channels = 1;
            if (inputFrames <= 0 || data == null || data.Length == 0) return [];

            // Deinterleave
            var inChannels = new float[channels][];
            for (int c = 0; c < channels; c++)
            {
                inChannels[c] = new float[inputFrames];
            }

            for (int f = 0; f < inputFrames; f++)
            {
                for (int c = 0; c < channels; c++)
                {
                    inChannels[c][f] = data[f * channels + c];
                }
            }

            var outChannels = new float[channels][];

            for (int c = 0; c < channels; c++)
            {
                outChannels[c] = PhaseVocoderStretchChannel(inChannels[c], inputFrames, targetFrames, progress == null ? null : new Progress<double>(p => progress.Report(p)));
            }

            // Interleave back
            var outData = new float[(long)targetFrames * channels];
            for (int f = 0; f < targetFrames; f++)
            {
                for (int c = 0; c < channels; c++)
                {
                    outData[f * channels + c] = outChannels[c].Length > f ? outChannels[c][f] : 0f;
                }
            }

            progress?.Report(1.0);
            return outData;
        }

        // Stretch a single channel using phase vocoder
        private static float[] PhaseVocoderStretchChannel(float[] input, int inputFrames, int targetFrames, IProgress<double>? progress = null)
        {
            // Parameters
            int N = 2048; // window size (power of two)
            if (N > inputFrames) N = 1 << (int)Math.Ceiling(Math.Log2(Math.Max(256, inputFrames)));
            int Ha = N / 4; // analysis hop

            double stretchRatio = (double)targetFrames / Math.Max(1, inputFrames);
            double HsD = Ha * stretchRatio; // synthesis hop (may be fractional)

            var window = HannWindow(N);

            // number of analysis frames
            int frames = Math.Max(1, (int)Math.Ceiling((inputFrames - N) / (double)Ha)) + 1;

            // pad input to fit
            int padded = (frames - 1) * Ha + N;
            var x = new double[padded];
            for (int i = 0; i < padded; i++) x[i] = (i < inputFrames) ? input[i] : 0.0;

            // Prepare arrays
            var magnitudes = new double[frames][];
            var phases = new double[frames][];

            // fft buffers
            Complex[] fftBuf = new Complex[N];

            for (int m = 0; m < frames; m++)
            {
                int pos = m * Ha;
                for (int n = 0; n < N; n++)
                {
                    double v = x[pos + n] * window[n];
                    fftBuf[n] = new Complex(v, 0.0);
                }

                Fourier.Forward(fftBuf, FourierOptions.Matlab);

                magnitudes[m] = new double[N / 2 + 1];
                phases[m] = new double[N / 2 + 1];
                for (int k = 0; k <= N / 2; k++)
                {
                    var c = fftBuf[k];
                    magnitudes[m][k] = c.Magnitude;
                    phases[m][k] = Math.Atan2(c.Imaginary, c.Real);
                }

                progress?.Report(m / (double)frames * 0.2); // small report
            }

            // Phase vocoder processing
            var synthesisPhases = new double[N / 2 + 1];
            var prevPhases = phases[0];

            // angular frequencies
            double[] omega = new double[N / 2 + 1];
            for (int k = 0; k <= N / 2; k++) omega[k] = 2.0 * Math.PI * k / N;

            // prepare output length estimate
            int estOutLen = (int)Math.Ceiling((frames - 1) * HsD + N);
            var y = new double[estOutLen + N];

            double synthesisTime = 0.0;
            int outPos = 0;

            for (int m = 0; m < frames; m++)
            {
                double[] mag = magnitudes[m];
                double[] ph = phases[m];

                if (m == 0)
                {
                    for (int k = 0; k <= N / 2; k++) synthesisPhases[k] = ph[k];
                }
                else
                {
                    int prev = m - 1;
                    double[] prevPh = prevPhases;

                    // phase advance
                    for (int k = 0; k <= N / 2; k++)
                    {
                        double delta = ph[k] - prevPh[k] - omega[k] * Ha;
                        delta = WrapPhase(delta);
                        double trueFreq = omega[k] + delta / Ha;
                        synthesisPhases[k] += trueFreq * HsD;
                    }

                    prevPhases = ph;
                }

                // Construct complex spectrum for synthesis
                for (int k = 0; k <= N / 2; k++)
                {
                    double magv = mag[k];
                    double phv = synthesisPhases[k];
                    fftBuf[k] = Complex.FromPolarCoordinates(magv, phv);
                }
                // Mirror for negative frequencies
                for (int k = N / 2 + 1; k < N; k++) fftBuf[k] = Complex.Conjugate(fftBuf[N - k]);

                // IFFT
                Fourier.Inverse(fftBuf, FourierOptions.Matlab);

                // overlap-add
                for (int n = 0; n < N; n++)
                {
                    int idx = outPos + n;
                    if (idx >= 0 && idx < y.Length)
                    {
                        y[idx] += fftBuf[n].Real * window[n];
                    }
                }

                outPos = (int)Math.Round(++synthesisTime * HsD);

                // progress
                progress?.Report(0.2 + m / (double)frames * 0.7);
            }

            // Trim or pad to targetFrames
            var output = new float[targetFrames];
            for (int i = 0; i < targetFrames; i++)
            {
                if (i < y.Length) output[i] = (float)Math.Clamp(y[i], -1.0, 1.0);
                else output[i] = 0f;
            }

            progress?.Report(1.0);
            return output;
        }

        private static double WrapPhase(double phase)
        {
            while (phase > Math.PI) phase -= 2.0 * Math.PI;
            while (phase < -Math.PI) phase += 2.0 * Math.PI;
            return phase;
        }

        private static double[] HannWindow(int N)
        {
            var w = new double[N];
            for (int n = 0; n < N; n++) w[n] = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * n / (N - 1)));
            return w;
        }

        private static double LanczosWindowedSinc(double x, int a)
        {
            x = Math.Abs(x);
            if (x < 1e-12)
            {
                return 1.0;
            }

            if (x >= a)
            {
                return 0.0;
            }

            double piX = Math.PI * x;
            double sinc1 = Math.Sin(piX) / piX;
            double piXOverA = piX / a;
            double sinc2 = Math.Sin(piXOverA) / (piXOverA == 0.0 ? 1.0 : piXOverA);
            return sinc1 * sinc2;
        }

        private static (long start, long end)[] PartitionRange(long start0, long end0, int parts)
        {
            var list = new List<(long, long)>();
            long total = Math.Max(0, end0 - start0);
            if (parts <= 1 || total == 0)
            {
                list.Add((start0, end0));
                return list.ToArray();
            }

            long baseSize = total / parts;
            long remainder = total % parts;
            long cur = start0;
            for (int i = 0; i < parts; i++)
            {
                long size = baseSize + (i < remainder ? 1 : 0);
                long s = cur;
                long e = cur + size;
                if (s < e)
                {
                    list.Add((s, e));
                }
                cur = e;
            }

            if (list.Count == 0)
            {
                list.Add((start0, end0));
            }

            return list.ToArray();
        }
    }
}
