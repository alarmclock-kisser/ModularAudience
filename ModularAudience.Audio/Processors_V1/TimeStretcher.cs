using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;
using ModularAudience.Audio;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ModularAudience.Audio.Processors_V1
{
    public static class TimeStretcher
    {
        public static async Task<AudioObj> TimeStretchAllThreadsAsync(
    AudioObj obj,
    int chunkSize = 16384,
    float overlap = 0.5f,
    double factor = 1.000,
    bool keepData = false,
    float normalize = 1.0f,
    int? maxWorkers = null,
    IProgress<double>? progress = null,
    bool offload = false)
        {
            if (maxWorkers == null)
            {
                maxWorkers = Environment.ProcessorCount;
            }
            else
            {
                maxWorkers = Math.Clamp(maxWorkers.Value, 1, Environment.ProcessorCount);
            }

            // FIX: Offload aktivieren auch bei benutzerdefinierten Thread-Anzahlen
            if (offload)
            {
                return await TimeStretchOffloadedAsync(
                    obj,
                    chunkSize,
                    overlap,
                    factor,
                    keepData,
                    normalize,
                    maxWorkers.Value,
                    progress,
                    adjustBpm: true);
            }

            if (maxWorkers != Environment.ProcessorCount)
            {
                return await TimeStretchMostThreadsAsync(
                    obj,
                    chunkSize,
                    overlap,
                    factor,
                    keepData,
                    normalize,
                    maxWorkers.Value,
                    progress,
                    offload: false);
            }

            LogCollection.Log("TimeStretchAllThreadsAsync: Starting time stretch with maxWorkers = " + maxWorkers.Value);

            float[] backupData = obj.Data;
            int sampleRate = obj.SampleRate;
            int overlapSize = obj.OverlapSize;

            double totalMs = 0;
            var sw = Stopwatch.StartNew();

            var chunkEnumerable = await obj.GetChunksAsync(chunkSize, overlap, keepData, maxWorkers.Value);
            var chunks = chunkEnumerable as IList<float[]> ?? chunkEnumerable.ToList();
            if (chunks.Count == 0)
            {
                obj.Data = backupData;
                return obj;
            }

            var tracker = CreateTracker(progress, chunks.Count, normalize > 0);
            tracker?.ReportWork(chunks.Count);
            obj["chunk"] = sw.Elapsed.TotalMilliseconds;
            totalMs += sw.Elapsed.TotalMilliseconds;
            sw.Restart();

            var allOpts = new ParallelOptions { MaxDegreeOfParallelism = maxWorkers.Value };

            var fftChunks = new Complex[chunks.Count][];
            await Parallel.ForEachAsync(
                Enumerable.Range(0, chunks.Count),
                allOpts,
                (i, _) =>
                {
                    WithLowPriority(() => fftChunks[i] = FourierTransformForwardCore(chunks[i], tracker));
                    return ValueTask.CompletedTask;
                });
            if (fftChunks.Length == 0)
            {
                obj.Data = backupData;
                return obj;
            }
            obj["fft"] = sw.Elapsed.TotalMilliseconds;
            totalMs += sw.Elapsed.TotalMilliseconds;
            sw.Restart();

            var stretchChunks = new Complex[fftChunks.Length][];
            await Parallel.ForEachAsync(
                Enumerable.Range(0, fftChunks.Length),
                allOpts,
                (i, _) =>
                {
                    WithLowPriority(() => stretchChunks[i] = StretchChunkCore(fftChunks[i], chunkSize, overlapSize, sampleRate, factor, tracker));
                    return ValueTask.CompletedTask;
                });
            if (stretchChunks.Length == 0)
            {
                obj.Data = backupData;
                return obj;
            }
            obj["stretch"] = sw.Elapsed.TotalMilliseconds;
            totalMs += sw.Elapsed.TotalMilliseconds;
            sw.Restart();

            obj.StretchFactor = factor;

            var ifftChunks = new float[stretchChunks.Length][];
            await Parallel.ForEachAsync(
                Enumerable.Range(0, stretchChunks.Length),
                allOpts,
                (i, _) =>
                {
                    WithLowPriority(() => ifftChunks[i] = FourierTransformInverseCore(stretchChunks[i], tracker));
                    return ValueTask.CompletedTask;
                });
            if (ifftChunks.Length == 0)
            {
                obj.Data = backupData;
                return obj;
            }
            obj["ifft"] = (float) sw.Elapsed.TotalMilliseconds;
            totalMs += sw.Elapsed.TotalMilliseconds;
            sw.Restart();

            await obj.AggregateStretchedChunksAsync(ifftChunks, obj.StretchFactor, maxWorkers.Value);
            tracker?.ReportWork(chunks.Count);
            if (obj.Data.LongLength <= 0)
            {
                obj.Data = backupData;
                return obj;
            }
            obj["aggregate"] = sw.Elapsed.TotalMilliseconds;
            totalMs += sw.Elapsed.TotalMilliseconds;

            obj.Bpm = (float) (obj.Bpm / factor);
            obj.Length = obj.Data.LongLength;
            obj.Duration = TimeSpan.FromSeconds(obj.Length / (double) (sampleRate * obj.Channels));

            sw.Restart();

            if (normalize > 0)
            {
                await obj.NormalizeAsync(normalize, maxWorkers.Value);
                tracker?.ReportWork(chunks.Count);
            }

            obj["normalize"] = sw.Elapsed.TotalMilliseconds;
            totalMs += sw.Elapsed.TotalMilliseconds;
            sw.Restart();

            tracker?.Complete();

            return obj;
        }


        internal static async Task<AudioObj> TimeStretchMostThreadsAsync(
            AudioObj obj,
            int chunkSize = 16384,
            float overlap = 0.5f,
            double factor = 1.000,
            bool keepData = false,
            float normalize = 1.0f,
            int? maxWorkers = null,
            IProgress<double>? progress = null,
            bool offload = false)
        {
            if (maxWorkers == null)
            {
                maxWorkers = Environment.ProcessorCount;
            }
            else
            {
                maxWorkers = Math.Clamp(maxWorkers.Value, 1, Environment.ProcessorCount);
            }

            // FIX 1: Offload ZUERST prüfen, bevor Thread-Anzahl geprüft wird
            if (offload)
            {
                return await TimeStretchOffloadedAsync(
                    obj,
                    chunkSize,
                    overlap,
                    factor,
                    keepData,
                    normalize,
                    maxWorkers.Value,
                    progress,
                    adjustBpm: true);
            }

            if (maxWorkers != Environment.ProcessorCount)
            {
                return await TimeStretchMostThreadsAsync(
                    obj,
                    chunkSize,
                    overlap,
                    factor,
                    keepData,
                    normalize,
                    maxWorkers.Value,
                    progress,
                    offload: false);
            }

            LogCollection.Log("TimeStretchAllThreadsAsync: Starting time stretch with maxWorkers = " + maxWorkers.Value);

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = maxWorkers.Value > 0 ? maxWorkers.Value : Environment.ProcessorCount
            };

            float[] backupData = obj.Data;
            int sampleRate = obj.SampleRate;
            int overlapSize = obj.OverlapSize;

            var sw = Stopwatch.StartNew();

            var chunkEnumerable = await obj.GetChunksAsync(chunkSize, overlap, keepData, maxWorkers.Value);
            var chunks = chunkEnumerable as IList<float[]> ?? chunkEnumerable.ToList();
            if (chunks.Count == 0)
            {
                obj.Data = backupData;
                return obj;
            }

            var tracker = CreateTracker(progress, chunks.Count, normalize > 0);
            tracker?.ReportWork(chunks.Count);
            obj["chunk"] = sw.Elapsed.TotalMilliseconds;
            sw.Restart();

            var fftChunks = new Complex[chunks.Count][];
            await Parallel.ForEachAsync(
                Enumerable.Range(0, chunks.Count),
                parallelOptions,
                (i, _) =>
                {
                    WithLowPriority(() => fftChunks[i] = FourierTransformForwardCore(chunks[i], tracker));
                    return ValueTask.CompletedTask;
                });

            if (fftChunks.Length == 0)
            {
                obj.Data = backupData;
                return obj;
            }
            obj["fft"] = sw.Elapsed.TotalMilliseconds;
            sw.Restart();

            var stretchChunks = new Complex[fftChunks.Length][];
            await Parallel.ForEachAsync(
                Enumerable.Range(0, fftChunks.Length),
                parallelOptions,
                (i, _) =>
                {
                    WithLowPriority(() => stretchChunks[i] = StretchChunkCore(fftChunks[i], chunkSize, overlapSize, sampleRate, factor, tracker));
                    return ValueTask.CompletedTask;
                });

            if (stretchChunks.Length == 0)
            {
                obj.Data = backupData;
                return obj;
            }
            obj["stretch"] = sw.Elapsed.TotalMilliseconds;
            sw.Restart();

            obj.StretchFactor = factor;

            var ifftChunks = new float[stretchChunks.Length][];
            await Parallel.ForEachAsync(
                Enumerable.Range(0, stretchChunks.Length),
                parallelOptions,
                (i, _) =>
                {
                    WithLowPriority(() => ifftChunks[i] = FourierTransformInverseCore(stretchChunks[i], tracker));
                    return ValueTask.CompletedTask;
                });

            if (ifftChunks.Length == 0)
            {
                obj.Data = backupData;
                return obj;
            }
            obj["ifft"] = (float) sw.Elapsed.TotalMilliseconds;
            sw.Restart();

            await obj.AggregateStretchedChunksAsync(ifftChunks.ToList(), obj.StretchFactor, maxWorkers.Value);
            tracker?.ReportWork(chunks.Count);
            if (obj.Data.LongLength <= 0)
            {
                obj.Data = backupData;
                return obj;
            }
            obj["aggregate"] = sw.Elapsed.TotalMilliseconds;
            sw.Restart();

            if (normalize > 0)
            {
                await obj.NormalizeAsync(normalize);
                tracker?.ReportWork(chunks.Count);
            }
            obj["normalize"] = sw.Elapsed.TotalMilliseconds;
            sw.Restart();

            obj.Bpm = obj.Bpm / (float) factor;
            obj.Length = obj.Data.LongLength;
            obj.Duration = TimeSpan.FromSeconds(obj.Length / (double) (sampleRate * obj.Channels));

            tracker?.Complete();

            return obj;
        }

        internal static async Task<AudioObj> TimeStretchIterativelyAsync(AudioObj obj, int iterationSize = 1, int chunkSize = 16384, float overlap = 0.5f, bool keepData = false, float normalize = 1.0f, int? maxWorkers = null, IProgress<double>? progress = null)
        {
            if (maxWorkers == null)
            {
                maxWorkers = Environment.ProcessorCount;
            }
            else
            {
                maxWorkers = Math.Clamp(maxWorkers.Value, 1, Environment.ProcessorCount);
            }

            var chunkEnumerable = await obj.GetChunksAsync(chunkSize, overlap, keepData, maxWorkers.Value);
            var chunks = chunkEnumerable as IList<float[]> ?? chunkEnumerable.ToList();
            if (chunks.Count == 0)
            {
                return obj;
            }
            var tracker = CreateTracker(progress, chunks.Count, normalize > 0);
            tracker?.ReportWork(chunks.Count);

            int sampleRate = obj.SampleRate;
            int overlapSize = obj.OverlapSize;

            // STOPWATCH
            double totalMs = 0;
            Stopwatch sw = Stopwatch.StartNew();

            // Iteratively process chunks in groups of iterationSize
            for (int i = 0; i < chunks.Count; i += iterationSize)
            {
                var currentChunks = chunks.Skip(i).Take(iterationSize).ToArray();
                if (!currentChunks.Any())
                {
                    continue;
                }

                // FFT on current chunks
                var fftTasks = currentChunks.Select(chunk => FourierTransformForwardAsync(chunk, tracker));
                var fftChunks = await Task.WhenAll(fftTasks);
                if (fftChunks.Length == 0)
                {
                    return obj;
                }
                obj["fft"] = sw.Elapsed.TotalMilliseconds;
                totalMs += sw.Elapsed.TotalMilliseconds;
                sw.Restart();

                // Stretch on current fftChunks
                var stretchTasks = fftChunks.Select(transformedChunk => StretchChunkAsync(transformedChunk, chunkSize, overlapSize, sampleRate, obj.StretchFactor, tracker));
                var stretchChunks = await Task.WhenAll(stretchTasks);
                if (stretchChunks.Length == 0)
                {
                    return obj;
                }
                obj["stretch"] = sw.Elapsed.TotalMilliseconds;
                totalMs += sw.Elapsed.TotalMilliseconds;
                sw.Restart();
                // IFFT on current stretchChunks
                var ifftTasks = stretchChunks.Select(stretchChunk => FourierTransformInverseAsync(stretchChunk, tracker));
                var ifftChunks = await Task.WhenAll(ifftTasks);
                if (ifftChunks.Length == 0)
                {
                    return obj;
                }
                obj["ifft"] = (float) sw.Elapsed.TotalMilliseconds;
                totalMs += sw.Elapsed.TotalMilliseconds;
                sw.Restart();

                await obj.AggregateStretchedChunksAsync(ifftChunks.ToList(), obj.StretchFactor, maxWorkers.Value);
                tracker?.ReportWork(currentChunks.Length);
                if (obj.Data.LongLength <= 0)
                {
                    return obj;
                }
                obj["aggregate"] = sw.Elapsed.TotalMilliseconds;
                totalMs += sw.Elapsed.TotalMilliseconds;
                sw.Restart();

                // Collect garbage
                GC.Collect();
            }

            if (normalize > 0)
            {
                await obj.NormalizeAsync(normalize);
                tracker?.ReportWork(chunks.Count);
            }
            obj["normalize"] = sw.Elapsed.TotalMilliseconds;
            totalMs += sw.Elapsed.TotalMilliseconds;
            sw.Restart();

            tracker?.Complete();

            return obj;
        }





        private static Task<Complex[]> FourierTransformForwardAsync(float[] samples, ProgressTracker? tracker = null)
        {
            return RunLowPriorityAsync(() => FourierTransformForwardCore(samples, tracker));
        }

        private static Task<float[]> FourierTransformInverseAsync(Complex[] samples, ProgressTracker? tracker = null)
        {
            return RunLowPriorityAsync(() => FourierTransformInverseCore(samples, tracker));
        }

        private static Task<Complex[]> StretchChunkAsync(Complex[] samples, int chunkSize, int overlapSize, int sampleRate, double factor, ProgressTracker? tracker = null)
        {
            return RunLowPriorityAsync(() => StretchChunkCore(samples, chunkSize, overlapSize, sampleRate, factor, tracker));
        }

        private static ProgressTracker? CreateTracker(IProgress<double>? progress, int chunkCount, bool includeNormalize)
        {
            if (progress == null)
            {
                return null;
            }

            int safeChunkCount = Math.Max(1, chunkCount);
            int stageCount = 5; // chunking, FFT, stretch, IFFT, aggregate
            if (includeNormalize)
            {
                stageCount++;
            }

            double totalWork = safeChunkCount * stageCount;
            return new ProgressTracker(progress, totalWork);
        }

        private sealed class ProgressTracker
        {
            private readonly Lock gate = new();
            private readonly IProgress<double> progress;
            private readonly double totalWork;
            private double completed;

            internal ProgressTracker(IProgress<double> progress, double totalWork)
            {
                this.progress = progress;
                this.totalWork = Math.Max(1.0, totalWork);
            }

            internal void ReportWork(double workUnits)
            {
                if (workUnits <= 0)
                {
                    return;
                }

                double normalized;
                lock (this.gate)
                {
                    this.completed += workUnits;
                    normalized = Math.Clamp(this.completed / this.totalWork, 0.0, 1.0);
                }

                this.progress.Report(normalized);
            }

            internal void Complete()
            {
                this.progress.Report(1.0);
            }
        }

        // ---- Low-priority execution helpers (prevent starvation of audio playback threads) ----

        private static void WithLowPriority(Action work)
        {
            var prev = Thread.CurrentThread.Priority;
            Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
            try
            {
                work();
            }
            finally
            {
                Thread.CurrentThread.Priority = prev;
            }
        }

        private static Task<T> RunLowPriorityAsync<T>(Func<T> work)
        {
            return Task.Run(() =>
            {
                var prev = Thread.CurrentThread.Priority;
                Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
                try
                {
                    return work();
                }
                finally
                {
                    Thread.CurrentThread.Priority = prev;
                }
            });
        }

        // ---- Synchronous core methods (used by Parallel.ForEachAsync and async wrappers) ----

        private static Complex[] FourierTransformForwardCore(float[] samples, ProgressTracker? tracker = null)
        {
            var complexSamples = samples.Select(s => new Complex(s, 0)).ToArray();
            Fourier.Forward(complexSamples, FourierOptions.Matlab);
            tracker?.ReportWork(1);
            return complexSamples;
        }

        private static float[] FourierTransformInverseCore(Complex[] samples, ProgressTracker? tracker = null)
        {
            Fourier.Inverse(samples, FourierOptions.Matlab);
            tracker?.ReportWork(1);
            return samples.Select(c => (float) c.Real).ToArray();
        }

        private static Complex[] StretchChunkCore(Complex[] samples, int chunkSize, int overlapSize, int sampleRate, double factor, ProgressTracker? tracker = null)
        {
            int hopIn = chunkSize - overlapSize;
            int hopOut = (int) (hopIn * factor + 0.5);

            int totalBins = chunkSize;
            int totalChunks = samples.Length / chunkSize;

            var output = new Complex[samples.Length];

            for (int chunk = 0; chunk < totalChunks; chunk++)
            {
                for (int bin = 0; bin < totalBins; bin++)
                {
                    int idx = chunk * chunkSize + bin;
                    int prevIdx = (chunk > 0) ? (chunk - 1) * chunkSize + bin : idx;

                    if (bin >= totalBins || chunk == 0)
                    {
                        output[idx] = samples[idx];
                        continue;
                    }

                    Complex cur = samples[idx];
                    Complex prev = samples[prevIdx];

                    float phaseCur = (float) Math.Atan2(cur.Imaginary, cur.Real);
                    float phasePrev = (float) Math.Atan2(prev.Imaginary, prev.Real);
                    float mag = (float) Math.Sqrt(cur.Real * cur.Real + cur.Imaginary * cur.Imaginary);

                    float deltaPhase = phaseCur - phasePrev;
                    float freqPerBin = (float) sampleRate / chunkSize;
                    float expectedPhaseAdv = 2.0f * (float) Math.PI * freqPerBin * bin * hopIn / sampleRate;

                    float delta = deltaPhase - expectedPhaseAdv;
                    delta = (float) (delta + Math.PI) % (2.0f * (float) Math.PI) - (float) Math.PI;

                    float phaseOut = phasePrev + expectedPhaseAdv + (float) (delta * factor);

                    output[idx] = new Complex(mag * Math.Cos(phaseOut), mag * Math.Sin(phaseOut));
                }
            }

            tracker?.ReportWork(1);
            return output;
        }


        private static async Task<AudioObj> TimeStretchOffloadedAsync(
    AudioObj obj,
    int chunkSize,
    float overlap,
    double factor,
    bool keepData,
    float normalize,
    int maxWorkers,
    IProgress<double>? progress,
    bool adjustBpm)
        {
            float[] backupData = obj.Data;
            int sampleRate = obj.SampleRate;
            int overlapSize = obj.OverlapSize;

            if (backupData == null || backupData.Length == 0 || sampleRate <= 0)
            {
                return obj;
            }

            string baseTemp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MA_TimeStretch");
            string tempDir = System.IO.Path.Combine(baseTemp, "TS_" + Guid.NewGuid().ToString("N"));
            var tempFiles = new List<string>();

            try
            {
                System.IO.Directory.CreateDirectory(tempDir);

                // FIX 2: Streaming statt alles in RAM laden
                var chunkEnumerable = obj.GetChunksEnumerable(chunkSize, overlap, keepData);
                int index = 0;
                int totalChunks = (int) Math.Ceiling((double) backupData.Length / (chunkSize * (1.0 - overlap)));

                // FIX 3: Chunks einzeln verarbeiten
                foreach (var chunk in chunkEnumerable)
                {
                    if (chunk == null || chunk.Length == 0)
                    {
                        continue;
                    }

                    // Verarbeitung
                    Complex[] fft = null;
                    Complex[] stretched = null;
                    float[] ifft = null;

                    await Task.Run(() =>
                    {
                        fft = FourierTransformForwardCore(chunk, null);
                    });

                    if (fft == null || fft.Length == 0)
                    {
                        continue;
                    }

                    await Task.Run(() =>
                    {
                        stretched = StretchChunkCore(fft, chunkSize, overlapSize, sampleRate, factor, null);
                    });

                    // FIX 4: Speicher freigeben
                    fft = null;

                    if (stretched == null || stretched.Length == 0)
                    {
                        continue;
                    }

                    await Task.Run(() =>
                    {
                        ifft = FourierTransformInverseCore(stretched, null);
                    });

                    stretched = null;

                    if (ifft == null || ifft.Length == 0)
                    {
                        continue;
                    }

                    // Auf Disk schreiben
                    string filePath = System.IO.Path.Combine(tempDir, index.ToString("D6") + ".bin");
                    using (var fs = new System.IO.FileStream(
                               filePath,
                               System.IO.FileMode.Create,
                               System.IO.FileAccess.Write,
                               System.IO.FileShare.None,
                               81920, // Größerer Buffer
                               System.IO.FileOptions.SequentialScan))
                    using (var bw = new System.IO.BinaryWriter(fs, Encoding.UTF8, false))
                    {
                        bw.Write(ifft.Length);
                        for (int i = 0; i < ifft.Length; i++)
                        {
                            bw.Write(ifft[i]);
                        }
                    }

                    ifft = null;
                    tempFiles.Add(filePath);
                    index++;

                    // Progress
                    if (progress != null && totalChunks > 0)
                    {
                        double frac = (double) index / (totalChunks * 2.0);
                        progress.Report(Math.Clamp(frac, 0.0, 0.5));
                    }

                    // FIX 5: GC alle 10 Chunks
                    if (index % 10 == 0)
                    {
                        GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, false);
                    }
                }

                if (tempFiles.Count == 0)
                {
                    obj.Data = backupData;
                    return obj;
                }

                obj.StretchFactor = factor;
                if (adjustBpm && obj.Bpm > 0f)
                {
                    obj.Bpm = (float) (obj.Bpm / factor);
                }

                // FIX 6: Kleinere Batches
                const int batchSize = 3;
                var batchList = new List<float[]>(batchSize);
                int processedFiles = 0;
                int totalFiles = tempFiles.Count;

                foreach (var path in tempFiles.ToList())
                {
                    float[] data = null;
                    using (var fs = new System.IO.FileStream(
                               path,
                               System.IO.FileMode.Open,
                               System.IO.FileAccess.Read,
                               System.IO.FileShare.Read,
                               81920,
                               System.IO.FileOptions.SequentialScan))
                    using (var br = new System.IO.BinaryReader(fs, Encoding.UTF8, false))
                    {
                        int len = br.ReadInt32();
                        if (len <= 0)
                        {
                            continue;
                        }

                        data = new float[len];
                        for (int i = 0; i < len; i++)
                        {
                            data[i] = br.ReadSingle();
                        }
                    }

                    batchList.Add(data);
                    processedFiles++;

                    if (batchList.Count >= batchSize)
                    {
                        await obj.AggregateStretchedChunksAsync(batchList, obj.StretchFactor, maxWorkers);
                        batchList.Clear();
                        GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, false);
                    }

                    // FIX 7: Datei sofort löschen
                    try
                    {
                        System.IO.File.Delete(path);
                    }
                    catch { }

                    if (progress != null && totalFiles > 0)
                    {
                        double frac = 0.5 + ((double) processedFiles / totalFiles) * 0.5;
                        progress.Report(Math.Clamp(frac, 0.5, 1.0));
                    }
                }

                if (batchList.Count > 0)
                {
                    await obj.AggregateStretchedChunksAsync(batchList, obj.StretchFactor, maxWorkers);
                    batchList.Clear();
                }

                if (obj.Data == null || obj.Data.LongLength <= 0)
                {
                    obj.Data = backupData;
                    return obj;
                }

                if (normalize > 0)
                {
                    await obj.NormalizeAsync(normalize, maxWorkers);
                    progress?.Report(1.0);
                }

                return obj;
            }
            finally
            {
                // FIX 8: Robustes Cleanup
                try
                {
                    foreach (var f in tempFiles)
                    {
                        try
                        {
                            if (System.IO.File.Exists(f))
                            {
                                System.IO.File.Delete(f);
                            }
                        }
                        catch { }
                    }

                    if (System.IO.Directory.Exists(tempDir))
                    {
                        try
                        {
                            System.IO.Directory.Delete(tempDir, true);
                        }
                        catch
                        {
                            await Task.Delay(100);
                            try
                            {
                                System.IO.Directory.Delete(tempDir, true);
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }
        }

    }
}
