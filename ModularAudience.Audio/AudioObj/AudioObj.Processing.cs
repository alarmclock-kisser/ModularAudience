using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using ModularAudience.Audio.Processing;

namespace ModularAudience.Audio
{
    public partial class AudioObj
    {
        public Task<byte[]> GetBytesAsync(int maxWorkers = 4)
        {
            return AudioConversionProcessor.GetBytesAsync(this, maxWorkers);
        }

        public Task<IEnumerable<float[]>> GetChunksAsync(int size = 2048, float overlap = 0.5f, bool keepData = false, int maxWorkers = 4)
        {
            return AudioChunkProcessor.GetChunksAsync(this, size, overlap, keepData, maxWorkers);
        }

        public IEnumerable<float[]> GetChunksEnumerable(int size = 2048, float overlap = 0.5f, bool keepData = false)
        {
            return AudioChunkProcessor.GetChunksEnumerable(this, size, overlap, keepData);
        }

        public Task AggregateStretchedChunksAsync(IEnumerable<float[]> chunks, double stretchFactor = 1.0, int maxWorkers = 4)
        {
            return AudioChunkProcessor.AggregateStretchedChunksAsync(this, chunks, stretchFactor, maxWorkers);
        }

        public Task NormalizeAsync(float maxAmplitude = 1.0f, int maxWorkers = 4)
        {
            return AudioAmplitudeProcessor.NormalizeAsync(this, maxAmplitude, maxWorkers);
        }

        public Task<float> GetPeakAmplitudeAsync(int maxWorkers = 4)
        {
            return AudioAmplitudeProcessor.GetPeakAmplitudeAsync(this, maxWorkers);
        }

        public Task<(long StartIndex, long EndIndex)> TrimSilenceAsync(float? threshold = null, int maxWorkers = 4)
        {
            return AudioSilenceProcessor.TrimSilenceAsync(this, threshold, maxWorkers);
        }

        public Task<float[]> ConvertToMonoAsync(bool set = false, int maxWorkers = 4)
        {
            return AudioConversionProcessor.ConvertToMonoAsync(this, set, maxWorkers);
        }

        public Task<float[]> GetCurrentWindowAsync(int windowSize = 65536, int lookingRange = 2, bool mono = false, bool lookBackwards = false)
        {
            return AudioWindowProcessor.GetCurrentWindowAsync(this, windowSize, lookingRange, mono, lookBackwards);
        }

        public async Task<AudioObj?> CreateLoopAsync(long? startSample = null, long? endSample = null)
        {
            startSample ??= this.loopFractionStartSamples;
            endSample ??= this.loopFractionEndSamples;
            if (startSample.Value > endSample.Value)
            {
                (startSample, endSample) = (endSample, startSample);
            }

            var clone = await this.CloneAsync();
            if (clone != null)
            {
                clone.SelectionStart = startSample.Value;
                clone.SelectionEnd = endSample.Value;
                await clone.EraseSelectionAsync(true);

                // Robust Fraction-Text: bevorzugt "1/n" bei Teilungen, sonst ganzzahlig oder mit einer Dezimalstelle
                string fraction;
                float lf = this.LoopFraction;
                if (lf > 0f && lf < 1f)
                {
                    // Prüfe auf näherungsweise Kehrwert eines Integers (z. B. 0.5 -> 1/2)
                    double recip = 1.0 / lf;
                    int recipInt = (int) System.Math.Round(recip);
                    if (System.Math.Abs(recip - recipInt) < 1e-3 && recipInt > 1)
                    {
                        fraction = "1/" + recipInt.ToString(CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        fraction = lf.ToString("F1", CultureInfo.InvariantCulture);
                    }
                }
                else if (lf >= 1f)
                {
                    int whole = (int) System.Math.Round(lf);
                    fraction = System.Math.Abs(lf - whole) < 1e-3
                        ? whole.ToString(CultureInfo.InvariantCulture)
                        : lf.ToString("F1", CultureInfo.InvariantCulture);
                }
                else
                {
                    // Fallback, falls LoopFraction nicht gesetzt ist
                    long len = endSample.Value - startSample.Value;
                    fraction = len > 0 ? "1" : "0";
                }

                double loopStartTime = (double) startSample.Value / System.Math.Max(1, this.SampleRate) / System.Math.Max(1, this.Channels);

                clone.Rename($"{this.OriginalName} (Looped {fraction} at {loopStartTime:F1}s)");
            }

            return clone;
        }

        public async Task<float[]> GetMonoSamplesAsync(bool set = false)
        {
            return await Task.Run(() =>
            {
                // Validierung
                if (this.Data == null || this.Data.Length == 0 || this.Channels <= 0)
                {
                    return [];
                }

                // Wenn bereits Mono, einfach zurückgeben
                if (this.Channels == 1)
                {
                    return set ? this.Data : (float[]) this.Data.Clone();
                }

                // Berechnung der Anzahl der Samples pro Kanal
                int monoSampleCount = this.Data.Length / this.Channels;
                float[] monoData = new float[monoSampleCount];

                // Konvertierung: Wir nehmen den Durchschnitt der Kanäle (Downmixing)
                // Das ist klanglich neutraler als nur den ersten Kanal zu nehmen.
                for (int i = 0; i < monoSampleCount; i++)
                {
                    float sum = 0;
                    for (int ch = 0; ch < this.Channels; ch++)
                    {
                        // Index berechnen: (Sample-Index * Anzahl der Kanäle) + aktueller Kanal
                        sum += this.Data[i * this.Channels + ch];
                    }
                    monoData[i] = sum / this.Channels;
                }

                // Wenn 'set' true ist, ersetzen wir das Original-Array (Memory Management)
                if (set)
                {
                    this.Data = monoData;
                    // Hinweis: Die Channels-Eigenschaft sollte im restlichen Code 
                    // nach diesem Aufruf auf 1 gesetzt werden.
                    // Falls du das nicht manuell machst, füge hier ein:
                    // this.Channels = 1; 
                }

                return monoData;
            });
        }
    }
}
