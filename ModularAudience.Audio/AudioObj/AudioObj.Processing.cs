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

        public Task AggregateStretchedChunksAsync(IEnumerable<float[]> chunks, double stretchFactor = 1.0, int maxWorkers = 4)
        {
            return AudioChunkProcessor.AggregateStretchedChunksAsync(this, chunks, stretchFactor, maxWorkers);
        }

        public Task NormalizeAsync(float maxAmplitude = 1.0f, int maxWorkers = 4)
        {
            return AudioAmplitudeProcessor.NormalizeAsync(this, maxAmplitude, maxWorkers);
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

                string fraction = "1/" + ((int)(1 / this.LoopFraction)).ToString();
                double loopStartTime = (double) startSample.Value / this.SampleRate / this.Channels;

                clone.Rename($"{this.OriginalName} (Looped {fraction} at {loopStartTime:F1}");
            }

            return clone;
        }
    }
}
