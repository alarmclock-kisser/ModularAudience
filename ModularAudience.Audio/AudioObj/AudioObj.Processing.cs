using System.Collections.Generic;
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
    }
}
