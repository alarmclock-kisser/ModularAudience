using ModularAudience.Audio;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ModularAudience.Audio.Processors_V2
{
    public static class AutoSampleCutter_V2
    {
        // Interne Konstanten für die Analyse-Auflösung
        private const int ANALYSIS_WINDOW_SIZE_MS = 5; // 5ms Fenster für RMS
        private const int LOOK_BACK_WINDOW_MS = 15;    // Max Suchbereich VOR dem Peak (für den Attack/Zero-Crossing)

        /// <summary>
        /// Analysiert ein AudioObj und extrahiert atomare Samples basierend auf Energieanstiegen und Zero-Crossings.
        /// Fehlende (null) Parameter werden automatisch und dynamisch ermittelt.
        /// </summary>
        public static async Task<IReadOnlyList<AudioObj>> CutAutoSamplesAsync(
            AudioObj audio,
            float? sensitivity = null,    // Wenn null: Auto-Detect basierend auf Dynamik (Crest Factor)
            float? minVolumeDb = null,    // Wenn null: Auto-Detect basierend auf Noise-Floor
            int? minDurationMs = null,    // Wenn null: Auto-Detect basierend auf BPM (oder 25ms Fallback)
            int? maxDurationMs = null,    // Maximale Länge (Default unendlich/ganzer File)
            int? releaseMs = null,        // Ausklangzeit nach Stille (Default 150ms)
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (audio.Data == null || audio.Data.Length == 0)
            {
                return [];
            }

            // 1. Datenvorbereitung
            float[] data = audio.Data;
            int channels = Math.Max(1, audio.Channels);
            int sampleRate = audio.SampleRate;

            return await Task.Run(() =>
            {
                // 2. Auto-Parameter Berechnung (Der "Number Crunching" Pass)
                var autoParams = AnalyzeAudioCharacteristics(data, channels, sampleRate, audio.Bpm);

                // Werte finalisieren (Entweder User-Input oder Auto-Wert)
                float finalSensitivity = sensitivity ?? autoParams.CalculatedSensitivity;
                float finalThresholdDb = minVolumeDb ?? autoParams.CalculatedThresholdDb;
                int finalMinDurationMs = minDurationMs ?? autoParams.CalculatedMinDurationMs;
                int finalReleaseMs = releaseMs ?? 150; // Standard Release, falls nicht angegeben

                // Parameter in Sample-Frames konvertieren
                int limitMaxSamples = maxDurationMs.HasValue ? (int) (maxDurationMs.Value * sampleRate / 1000.0) : int.MaxValue;
                // Min-Samples muss mindestens 1 Frame sein
                int limitMinSamples = Math.Max(1, (int) (finalMinDurationMs * sampleRate / 1000.0));

                // Threshold in linearen Wert wandeln
                float thresholdLinear = (float) Math.Pow(10, finalThresholdDb / 20.0);
                // Silence Floor (deutlich leiser als Trigger)
                float silenceFloorLinear = thresholdLinear * 0.25f;

                var results = new List<AudioObj>();

                // RMS Envelope berechnen (Energieverlauf, einmalig)
                float[] rmsEnvelope = CalculateRmsEnvelope(data, channels, sampleRate, ANALYSIS_WINDOW_SIZE_MS);

                int windowSamples = (sampleRate * ANALYSIS_WINDOW_SIZE_MS) / 1000;
                int lookBackSamples = (sampleRate * LOOK_BACK_WINDOW_MS) / 1000;
                int releaseSamples = (sampleRate * finalReleaseMs) / 1000;
                int totalFrames = data.Length / channels;

                // 3. Haupt-Schleife: Onset Detection & Cutting
                int lastCutEndFrame = 0;
                int envelopeIndex = 0;

                // Wir iterieren durch den Envelope (schneller als per Sample)
                while (envelopeIndex < rmsEnvelope.Length - 1)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // A. Suche nach Trigger (Onset)
                    float currentE = rmsEnvelope[envelopeIndex];
                    float prevE = envelopeIndex > 0 ? rmsEnvelope[envelopeIndex - 1] : 0;

                    // Trigger Kriterium: Über Noise-Threshold UND plötzlicher Energieanstieg
                    bool isTrigger = (currentE > thresholdLinear) && (currentE > prevE * finalSensitivity);

                    if (isTrigger)
                    {
                        int estimatedStartFrame = envelopeIndex * windowSamples;

                        // Validierung: Liegt der Start zu nah am letzten Cut? (Refractory period)
                        if (estimatedStartFrame > lastCutEndFrame)
                        {
                            // A.1: Exakten Start finden (Backtracking + Zero Crossing)
                            // Suche im Bereich [Max des letzten Endes, Geschätzter Start]
                            int searchStart = Math.Max(lastCutEndFrame, estimatedStartFrame - lookBackSamples);
                            int preciseStart = FindZeroCrossingBackwards(data, channels, estimatedStartFrame, searchStart);

                            // B. Ende finden (Sustain + Release)
                            // Suchbereich beginnt nach der Mindestdauer
                            int searchEndStart = preciseStart + limitMinSamples;
                            int preciseEnd = FindSilenceEnd(data, channels, searchEndStart, totalFrames, silenceFloorLinear, releaseSamples, limitMaxSamples, preciseStart);

                            // B.1 Ende auf Zero Crossing snappen
                            preciseEnd = FindZeroCrossingForwards(data, channels, preciseEnd, totalFrames);

                            // C. Extraktion
                            int length = preciseEnd - preciseStart;
                            if (length >= limitMinSamples)
                            {
                                var slice = ExtractSlice(audio, data, preciseStart, length, results.Count + 1);
                                if (slice != null)
                                {
                                    results.Add(slice);
                                    lastCutEndFrame = preciseEnd;

                                    // Envelope-Index vorspulen, um den nächsten Hit sofort zu finden
                                    envelopeIndex = preciseEnd / windowSamples;
                                }
                            }
                        }
                    }

                    envelopeIndex++;

                    // Progress reporten (grob)
                    if (envelopeIndex % 100 == 0)
                    {
                        progress?.Report((double) envelopeIndex / rmsEnvelope.Length * 100.0);
                    }
                }

                return (IReadOnlyList<AudioObj>) results;

            }, cancellationToken);
        }

        // --- Helper Structs und Methods ---

        private struct AutoAnalysisResult
        {
            public float CalculatedThresholdDb;
            public float CalculatedSensitivity;
            public int CalculatedMinDurationMs; // NEU
        }

        /// <summary>
        /// Scannt das Audiofile, um den Noise Floor, die Dynamik und die minimale Dauer zu ermitteln.
        /// </summary>
        private static AutoAnalysisResult AnalyzeAudioCharacteristics(float[] data, int channels, int sampleRate, float bpm)
        {
            // --- 1. Noise Floor & Sensitivity (wie in V2.1) ---
            int step = channels * 100;
            double sumSquares = 0;
            int count = 0;
            float maxPeak = 0;
            List<float> noiseCandidates = new();

            for (int i = 0; i < data.Length; i += step)
            {
                float val = Math.Abs(data[i]);
                if (val > maxPeak)
                {
                    maxPeak = val;
                }

                sumSquares += val * val;
                count++;

                // Sammle leise Werte für Noise Floor Schätzung
                if (val < 0.05f)
                {
                    noiseCandidates.Add(val);
                }
            }

            double totalRms = Math.Sqrt(sumSquares / Math.Max(1, count));

            float noiseFloorAvg = noiseCandidates.Count > 0 ? noiseCandidates.Average() : 0.0001f;
            float detectedNoiseDb = 20.0f * (float) Math.Log10(noiseFloorAvg + float.Epsilon);
            // Threshold: 12dB über dem Noise Floor, limitiert auf -60dB bis -6dB
            float suggestedThresholdDb = Math.Clamp(detectedNoiseDb + 12.0f, -60.0f, -6.0f);

            double crestFactor = (totalRms > 0) ? (maxPeak / totalRms) : 1.0;
            // Sensitivity: Niedriger Crest Factor (komprimiert) -> Höhere Sensitivity (braucht mehr Veränderung)
            float suggestedSensitivity = (float) Math.Clamp(2.5 - (crestFactor * 0.1), 1.1, 3.0);


            // --- 2. Min Duration (NEU) ---
            int suggestedMinDurationMs;
            if (bpm > 60 && bpm < 300)
            {
                // Musikalische Grundlage: 1/64 Note als Minimum
                float msPer64th = 60000.0f / bpm / 64.0f;
                // Cap es zwischen 15ms (für scharfe Transienten) und 40ms (für langsame Grooves)
                suggestedMinDurationMs = (int) Math.Clamp(msPer64th, 15, 40);
            }
            else
            {
                // Fallback: Konservativer Wert, um Rauschen zu ignorieren
                suggestedMinDurationMs = 25;
            }


            return new AutoAnalysisResult
            {
                CalculatedThresholdDb = suggestedThresholdDb,
                CalculatedSensitivity = suggestedSensitivity,
                CalculatedMinDurationMs = suggestedMinDurationMs
            };
        }

        // --- Restliche Hilfsmethoden bleiben unverändert ---
        // (CalculateRmsEnvelope, FindZeroCrossingBackwards, FindZeroCrossingForwards, FindSilenceEnd, ExtractSlice)
        // ... (Der Code dafür ist wie in der vorherigen Antwort, nur hier gekürzt) ...

        private static float[] CalculateRmsEnvelope(float[] data, int channels, int sampleRate, int windowMs)
        {
            int windowSize = (sampleRate * windowMs) / 1000;
            int blocks = data.Length / channels / windowSize;
            float[] env = new float[blocks + 1];

            Parallel.For(0, blocks, b =>
            {
                int start = b * windowSize * channels;
                int end = Math.Min(data.Length, start + (windowSize * channels));
                double sum = 0;
                for (int i = start; i < end; i++)
                {
                    sum += data[i] * data[i];
                }
                env[b] = (float) Math.Sqrt(sum / (end - start));
            });
            return env;
        }

        private static int FindZeroCrossingBackwards(float[] data, int channels, int startFrame, int limitFrame)
        {
            for (int i = startFrame; i > limitFrame; i--)
            {
                int idxCurrent = i * channels;
                int idxPrev = (i - 1) * channels;
                if (idxPrev < 0)
                {
                    break;
                }

                float curr = data[idxCurrent];
                float prev = data[idxPrev];

                if ((curr >= 0 && prev < 0) || (curr < 0 && prev >= 0))
                {
                    return Math.Abs(curr) < Math.Abs(prev) ? i : i - 1;
                }
            }
            return startFrame;
        }

        private static int FindZeroCrossingForwards(float[] data, int channels, int startFrame, int maxFrame)
        {
            int limit = Math.Min(maxFrame - 1, startFrame + 2000);
            for (int i = startFrame; i < limit; i++)
            {
                int idxCurrent = i * channels;
                int idxNext = (i + 1) * channels;

                float curr = data[idxCurrent];
                float next = data[idxNext];

                if ((curr >= 0 && next < 0) || (curr < 0 && next >= 0))
                {
                    return Math.Abs(curr) < Math.Abs(next) ? i : i + 1;
                }
            }
            return startFrame;
        }

        private static int FindSilenceEnd(float[] data, int channels, int startFrame, int totalFrames, float silenceThreshold, int releaseSamples, int maxDurationSamples, int absoluteStartFrame)
        {
            int silenceCounter = 0;
            int requiredConsecutiveSilence = 100;

            for (int i = startFrame; i < totalFrames; i++)
            {
                if ((i - absoluteStartFrame) >= maxDurationSamples)
                {
                    return i;
                }

                float absSum = 0;
                int baseIdx = i * channels;
                for (int c = 0; c < channels; c++)
                {
                    absSum += Math.Abs(data[baseIdx + c]);
                }

                float amp = absSum / channels;

                if (amp < silenceThreshold)
                {
                    silenceCounter++;
                    if (silenceCounter >= requiredConsecutiveSilence)
                    {
                        return Math.Min(totalFrames, i + releaseSamples);
                    }
                }
                else
                {
                    silenceCounter = 0;
                }
            }
            return totalFrames;
        }

        private static AudioObj? ExtractSlice(AudioObj source, float[] sourceData, int startFrame, int lengthFrames, int index)
        {
            int channels = Math.Max(1, source.Channels);
            long startSampleIdx = (long) startFrame * channels;
            long lengthSamples = (long) lengthFrames * channels;

            if (startSampleIdx >= sourceData.LongLength)
            {
                return null;
            }

            if (startSampleIdx + lengthSamples > sourceData.LongLength)
            {
                lengthSamples = sourceData.LongLength - startSampleIdx;
            }

            if (lengthSamples <= 0)
            {
                return null;
            }

            // Use int indices for Array.Copy (reasonable for typical audio sizes). Cast safe for <2GB arrays.
            int srcIndex = (int) startSampleIdx;
            int copyCount = (int) lengthSamples;
            var newData = new float[copyCount];
            Array.Copy(sourceData, srcIndex, newData, 0, copyCount);

            long framesActual = lengthSamples / channels;

            return new AudioObj
            {
                Name = $"{source.Name}_Sample_{index:000}",
                FilePath = source.FilePath,
                Data = newData,
                SampleRate = source.SampleRate,
                Channels = source.Channels,
                BitDepth = source.BitDepth,
                // IMPORTANT: Length is number of float32 samples (f32), not frames.
                Length = lengthSamples,
                // Duration must reflect actual frames / sampleRate
                Duration = TimeSpan.FromSeconds((double) framesActual / source.SampleRate),
                Bpm = source.Bpm,
                Volume = 100.0f,
                SelectionStart = -1,
                SelectionEnd = -1
            };
        }
    }
}