namespace ModularAudience.Audio.Processors_V4
{
    public static class EqualSliceProcessor_V4
    {
        public static async Task<IReadOnlyList<AudioObj>> SliceAsync(AudioObj source, int partCount, long? startSample = null, long? endSample = null, string? baseName = null)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (partCount < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(partCount));
            }

            if (source.Data == null || source.Data.Length == 0 || source.Channels <= 0)
            {
                return [];
            }

            int channels = Math.Max(1, source.Channels);
            long totalSamples = source.Data.LongLength;
            long rangeStart = Math.Clamp(startSample ?? 0L, 0L, totalSamples);
            long rangeEnd = Math.Clamp(endSample ?? totalSamples, rangeStart, totalSamples);

            rangeStart -= rangeStart % channels;
            if (rangeEnd > rangeStart)
            {
                rangeEnd -= (rangeEnd - rangeStart) % channels;
            }

            long totalRangeSamples = Math.Max(0L, rangeEnd - rangeStart);
            long totalFrames = totalRangeSamples / channels;
            if (totalFrames < partCount)
            {
                return [];
            }

            string resolvedBaseName = string.IsNullOrWhiteSpace(baseName) ? (string.IsNullOrWhiteSpace(source.Name) ? "Audio" : source.Name.Trim()) : baseName.Trim();
            AudioObj working = await source.CloneAsync().ConfigureAwait(false);
            List<AudioObj> slices = new(partCount);

            try
            {
                long baseFramesPerSlice = totalFrames / partCount;
                long remainderFrames = totalFrames % partCount;
                long currentFrame = rangeStart / channels;

                for (int i = 0; i < partCount; i++)
                {
                    long sliceFrames = baseFramesPerSlice + (i < remainderFrames ? 1 : 0);
                    if (sliceFrames <= 0)
                    {
                        continue;
                    }

                    long sliceStartSample = currentFrame * channels;
                    long sliceEndSample = (currentFrame + sliceFrames) * channels;
                    working.SelectionStart = sliceStartSample;
                    working.SelectionEnd = sliceEndSample;
                    AudioObj? slice = await working.CloneFromSelectionAsync().ConfigureAwait(false);
                    if (slice != null)
                    {
                        string name = $"{resolvedBaseName}_Part{i + 1:D2}of{partCount:D2}";
                        slice.Rename(name);
                        slice.FilePath = source.FilePath;
                        slices.Add(slice);
                    }

                    currentFrame += sliceFrames;
                }
            }
            finally
            {
                working.Dispose();
            }

            return slices;
        }
    }
}
