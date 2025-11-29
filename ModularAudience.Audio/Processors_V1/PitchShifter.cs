using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ModularAudience.Audio.Processors_V1
{
    public static class PitchShifter
    {
        public static async Task<IEnumerable<List<AudioObj>>> CreatePitchShiftsBatchAsync(IEnumerable<AudioObj> samples, int keysRange = 8, float semitoneDelta = 1.0f, IProgress<double>? progress = null)
        {
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            var sampleList = samples.ToList();
            int total = Math.Max(1, sampleList.Count);

            // Wenn progress vorhanden, wir wrapen jeden Task-Progress auf Gesamt-Fortschritt (0..1).
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
                        // p in [0,1] für dieses sample -> skaliere in [idx/total, (idx+1)/total)
                        double overall = (idx + Math.Clamp(p, 0.0, 1.0)) / (double)total;
                        progress.Report(Math.Clamp(overall, 0.0, 1.0));
                    });
                }

                tasks.Add(Task.Run(async () =>
                {
                    var list = (await CreatePitchShiftsAsync(s, keysRange, semitoneDelta, childProgress).ConfigureAwait(false)).ToList();
                    // Falls childProgress nicht Null, beim Ende 100% des Subtasks melden
                    childProgress?.Report(1.0);
                    return list;
                }));
            }

            var results = await Task.WhenAll(tasks).ConfigureAwait(false);

            // Gesamt-Fortschritt abschließen
            progress?.Report(1.0);

            return results.ToList();
        }

        /// <summary>
        /// Erstelle Pitch-Varianten für ein einzelnes AudioObj.
        /// Liefert eine Auflistung von neuen AudioObj-Instanzen (Kopien), jeweils mit angewendetem Pitch-Shift.
        /// Der Bereich geht von -keysRange .. +keysRange (inklusive 0). Schritte = semitoneDelta.
        /// </summary>
        public static async Task<IEnumerable<AudioObj>> CreatePitchShiftsAsync(AudioObj sample, int keysRange = 8, float semitoneDelta = 1.0f, IProgress<double>? progress = null)
        {
            if (sample == null) throw new ArgumentNullException(nameof(sample));
            if (keysRange < 0) throw new ArgumentOutOfRangeException(nameof(keysRange));
            if (semitoneDelta <= 0) throw new ArgumentOutOfRangeException(nameof(semitoneDelta));

            // Erzeuge die Liste von Halbton-Schritten (z.B. -8 .. +8 in steps)
            var shifts = new List<float>();
            for (float s = -keysRange; s <= keysRange; s += semitoneDelta)
            {
                // Runde s auf sinnvolle Präzision um Rechenabweichungen zu vermeiden
                float rounded = (float)Math.Round(s, 6);
                shifts.Add(rounded);
            }

            int count = shifts.Count;
            var results = new AudioObj[count];

            // Partitioniere Arbeit und report Fortschritt per element
            var progressLock = new object();
            double[] elementProgress = new double[count];

            var tasks = Enumerable.Range(0, count).Select(index =>
                Task.Run(async () =>
                {
                    float semitones = shifts[index];
                    // Für semitone == 0 können wir evtl. den Original-Klon zurückgeben, ohne Resampling
                    AudioObj result;
                    if (Math.Abs(semitones) < 1e-6f)
                    {
                        // Klone das Original - keine Transformation nötig
                        result = sample.Clone();
                        result.Name = $"{sample.Name}_P0st";
                    }
                    else
                    {
                        result = await PitchShiftOneAsync(sample, semitones, new Progress<double>(p =>
                        {
                            // Mapiere Unterfortschritt zu elementProgress[index]
                            lock (progressLock)
                            {
                                elementProgress[index] = Math.Clamp(p, 0.0, 1.0);
                                double overall = elementProgress.Sum() / count;
                                progress?.Report(overall);
                            }
                        })).ConfigureAwait(false);
                    }

                    results[index] = result;

                })).ToArray();

            await Task.WhenAll(tasks).ConfigureAwait(false);

            // Abschliessender Gesamt-Report
            progress?.Report(1.0);

            return results.Where(r => r != null).ToList();
        }

        /// <summary>
        /// Erzeuge eine einzelne pitch-shifted AudioObj-Kopie.
        /// Algorithmus (vereinfachte, qualitativ gute Offline-Lösung):
        /// - Faktor = 2^(semitones/12)
        /// - Wir führen bandbegrenztes Resampling (Lanczos-windowed sinc) durch:
        ///   Ausgabesamples = Round(inputFrames / factor)  (Dauer verändert sich proportional)
        /// - Verarbeitung pro Kanal, parallelisiert über Partitionen.
        /// - Fortschritt wird über IProgress gemeldet.
        /// </summary>
        private static Task<AudioObj> PitchShiftOneAsync(AudioObj sample, float semitones, IProgress<double>? progress = null)
        {
            return Task.Run(() =>
            {
                if (sample == null) throw new ArgumentNullException(nameof(sample));

                float pitchFactor = (float)Math.Pow(2.0, semitones / 12.0); // >1 = höher, <1 = tiefer

                float[] inData = sample.Data ?? Array.Empty<float>();
                int channels = Math.Max(1, sample.Channels);
                long totalSamples = inData.LongLength;
                long inputFrames = Math.Max(0L, totalSamples / channels);
                if (inputFrames == 0)
                {
                    // Leeres Ergebnis: gib Klon ohne Data zurück
                    var emptyClone = sample.Clone();
                    emptyClone.Data = Array.Empty<float>();
                    emptyClone.Length = 0;
                    return emptyClone;
                }

                // Bestimme Ausgabeframes (resampling): hier werden wir die Anzahl der Frames
                // proportional zu 1/pitchFactor ändern. (Dauer ändert sich.)
                long outputFrames = Math.Max(1L, (long)Math.Round(inputFrames / pitchFactor));

                // Safety: Begrenze max Größe um OOM zu vermeiden (sehr große Samples)
                const long MaxFrames = 20_000_000; // ≈ 20M frames per channel (Anpassbar)
                if (outputFrames > MaxFrames)
                {
                    outputFrames = MaxFrames;
                }

                // Vorbereiten: Kanalweise Arrays
                var inChannels = new float[channels][];
                for (int c = 0; c < channels; c++)
                {
                    inChannels[c] = new float[inputFrames];
                }

                // deinterleave
                for (long f = 0; f < inputFrames; f++)
                {
                    for (int c = 0; c < channels; c++)
                    {
                        long srcIndex = f * channels + c;
                        inChannels[c][f] = inData[srcIndex];
                    }
                }

                // Out channels
                var outChannels = new float[channels][];
                for (int c = 0; c < channels; c++)
                {
                    outChannels[c] = new float[outputFrames];
                }

                // Resampler-Parameter: Lanczos a (Fenstergröße)
                const int a = 8; // Lanczos parameter; 8 ist hochwertig, aber teuer
                int kernelRadius = a;

                // Precompute normalization denominators per output sample? We compute per sample.

                int maxDegreeOfParallelism = Math.Max(1, Math.Min(Environment.ProcessorCount, 8));

                // Partition output frames for parallel processing
                int partitionCount = maxDegreeOfParallelism;
                var ranges = PartitionRange(0L, outputFrames, partitionCount);

                // work
                long processed = 0;
                object progLock = new object();

                Parallel.For(0, ranges.Length, new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism }, pi =>
                {
                    var r = ranges[pi];
                    long start = r.start;
                    long end = r.end; // exclusive

                    for (long outFrame = start; outFrame < end; outFrame++)
                    {
                        // Map output frame to source position in framespace
                        // srcPos = outFrame * (inputFrames / outputFrames)
                        double srcPos = outFrame * (inputFrames / (double)outputFrames);
                        long srcIndexFloor = (long)Math.Floor(srcPos);
                        double frac = srcPos - srcIndexFloor;

                        // For each channel compute sinc-windowed sum
                        int left = (int)Math.Max(0, srcIndexFloor - kernelRadius + 1);
                        int right = (int)Math.Min(inputFrames - 1, srcIndexFloor + kernelRadius);

                        for (int c = 0; c < channels; c++)
                        {
                            double sum = 0.0;
                            double wsum = 0.0;
                            var ch = inChannels[c];

                            // Accumulate sinc-weighted neighbors
                            for (int j = left; j <= right; j++)
                            {
                                double x = srcPos - j; // distance
                                double w = LanczosWindowedSinc(x, a);
                                sum += ch[j] * w;
                                wsum += Math.Abs(w);
                            }

                            // Normalize and assign
                            float sampleValue = wsum > 1e-12 ? (float)(sum / wsum) : 0f;
                            outChannels[c][outFrame] = sampleValue;
                        }

                        // Fortschritt
                        if (progress != null)
                        {
                            // update processed counter and report occasionally
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

                // Interleave outChannels back to float[] outData
                long outputTotalSamples = outputFrames * channels;
                var outData = new float[outputTotalSamples];
                for (long f = 0; f < outputFrames; f++)
                {
                    for (int c = 0; c < channels; c++)
                    {
                        outData[f * channels + c] = outChannels[c][f];
                    }
                }

                // Build resulting AudioObj clone and set fields
                var clone = sample.Clone();
                clone.Data = outData;
                clone.Length = outData.LongLength; // samples (floats)
                clone.SampleRate = sample.SampleRate; // Keep nominal sample rate (duration changed)
                clone.Channels = channels;
                // Update duration if available
                try
                {
                    int sr = Math.Max(1, clone.SampleRate);
                    clone.Duration = TimeSpan.FromSeconds((double)outputFrames / sr);
                }
                catch { /* ignore if properties not present */ }

                // Name the sample to indicate shift
                string sign = semitones >= 0 ? "+" : "";
                clone.Name = $"{sample.Name}_P{sign}{semitones:0.##}st";

                // Final progress report
                progress?.Report(1.0);

                return clone;
            });
        }

        // Lanczos-windowed sinc kernel
        private static double LanczosWindowedSinc(double x, int a)
        {
            x = Math.Abs(x);
            if (x < 1e-12) return 1.0;
            if (x >= a) return 0.0;
            // sinc(pi*x) * sinc(pi*x/a)
            double piX = Math.PI * x;
            double sinc1 = Math.Sin(piX) / piX;
            double piXOverA = piX / a;
            double sinc2 = Math.Sin(piXOverA) / (piXOverA == 0.0 ? 1.0 : piXOverA);
            return sinc1 * sinc2;
        }

        // Partition helper: returns array of (start, endExclusive) ranges for [start0..end0)
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

            if (list.Count == 0) list.Add((start0, end0));
            return list.ToArray();
        }
    }
}
