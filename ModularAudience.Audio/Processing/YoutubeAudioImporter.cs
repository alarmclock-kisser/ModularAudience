using ModularAudience.Audio.Processors_V1;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ModularAudience.Audio.Processing
{
    public static class YoutubeAudioImporter
    {
        public static async Task<bool> WaitForBpmTagAsync(string filePath, TimeSpan maximumWait,
            CancellationToken cancellationToken) =>
            await WaitForBpmValueAsync(filePath, maximumWait, cancellationToken).ConfigureAwait(false) is not null;

        /// <summary>Zero means no timeout. Cancellation always stops polling.</summary>
        public static async Task<float?> WaitForBpmValueAsync(string filePath, TimeSpan maximumWait,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            if (maximumWait < TimeSpan.Zero || maximumWait > TimeSpan.FromSeconds(300))
                throw new ArgumentOutOfRangeException(nameof(maximumWait));
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                float? bpm = await Task.Run(() => ReadSharedBpmTag(filePath), cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (bpm is > 0 && float.IsFinite(bpm.Value)) return bpm;
                if (maximumWait == TimeSpan.Zero)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
                    continue;
                }
                var remaining = maximumWait - clock.Elapsed;
                if (remaining <= TimeSpan.Zero) return null;
                await Task.Delay(remaining < TimeSpan.FromMilliseconds(250) ? remaining : TimeSpan.FromMilliseconds(250),
                    cancellationToken).ConfigureAwait(false);
            }
        }

        private static float? ReadSharedBpmTag(string filePath)
        {
            try
            {
                // No duration/audio scanning and no exclusive reader while an external tagger works.
                using var file = TagLib.File.Create(new SharedTagFile(filePath), TagLib.ReadStyle.None);
                if (file.GetTag(TagLib.TagTypes.Id3v2, false) is TagLib.Id3v2.Tag id3)
                {
                    foreach (string? text in new[]
                    {
                        TagLib.Id3v2.TextInformationFrame.Get(id3, "TBPM", false)?.Text.FirstOrDefault(),
                        TagLib.Id3v2.UserTextInformationFrame.Get(id3, "BPM", false)?.Text.FirstOrDefault()
                    })
                        if (float.TryParse(text?.Replace(',', '.'), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float bpm) && float.IsFinite(bpm) && bpm > 0)
                            return bpm;
                }
                if (file.Tag.BeatsPerMinute > 0) return file.Tag.BeatsPerMinute;
            }
            // Partial tags and temporary write locks are expected while the external tagger saves.
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or TagLib.CorruptFileException or TagLib.UnsupportedFormatException) { }
            return null;
        }

        private sealed class SharedTagFile(string path) : TagLib.File.IFileAbstraction
        {
            public string Name => path;
            public Stream ReadStream => new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            public Stream WriteStream => throw new NotSupportedException("BPM polling is read-only.");
            public void CloseStream(Stream stream) => stream.Dispose();
        }

        /// <summary>
        /// Imports a local download without taking ownership of its file.
        /// Callers should normally supply a five-second cooldown.
        /// </summary>
        public static async Task<AudioObj> ImportAsync(
            string filePath, string title, bool temporary, TimeSpan cooldown,
            PlaylistStretchSettings? stretchSettings, IProgress<string>? progress,
            CancellationToken cancellationToken, float? taggedBpm = null)
        {
            ValidateArguments(filePath, title, cooldown, stretchSettings);
            if (taggedBpm is { } value && !IsValidBpm(value)) throw new ArgumentOutOfRangeException(nameof(taggedBpm));
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report("Waiting for the import cooldown.");
            await Task.Delay(cooldown, cancellationToken).ConfigureAwait(false);

            AudioObj? audio = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report("Loading local audio.");
                audio = await Task.Run(() => AudioObj.FromFile(filePath), cancellationToken)
                    .ConfigureAwait(false);
                if (audio == null)
                {
                    throw new InvalidDataException("The local audio file could not be loaded.");
                }

                if (temporary) audio.FilePath = string.Empty;
                cancellationToken.ThrowIfCancellationRequested();
                RequireAudioData(audio);
                // Keep the exact value observed before import, even if an external tagger
                // is replacing the file's metadata while AudioObj reads it again.
                if (taggedBpm is { } observed) audio.Bpm = observed;
                audio.Rename(title);
                if (stretchSettings != null)
                {
                    await StretchAsync(audio, stretchSettings, progress, cancellationToken).ConfigureAwait(false);
                }

                audio.Rename(title);
                progress?.Report("Audio import completed.");
                cancellationToken.ThrowIfCancellationRequested();
                return audio;
            }
            catch
            {
                audio?.Dispose();
                throw;
            }
        }

        public static double CalculateStretchFactor(float sourceBpm, float targetBpm)
        {
            if (!IsValidBpm(sourceBpm))
            {
                throw new ArgumentOutOfRangeException(nameof(sourceBpm), "Source BPM must be finite and positive.");
            }

            if (!IsValidBpm(targetBpm))
            {
                throw new ArgumentOutOfRangeException(nameof(targetBpm), "Target BPM must be finite and positive.");
            }

            return (double)sourceBpm / targetBpm;
        }

        private static void ValidateArguments(
            string filePath, string title, TimeSpan cooldown, PlaylistStretchSettings? settings)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            ArgumentException.ThrowIfNullOrWhiteSpace(title);
            if (cooldown < TimeSpan.Zero || cooldown > TimeSpan.FromSeconds(300))
            {
                throw new ArgumentOutOfRangeException(nameof(cooldown), "Cooldown must be between zero and 300 seconds.");
            }

            if (settings != null)
            {
                _ = CalculateStretchFactor(1f, settings.TargetBpm);
            }
        }

        private static bool IsValidBpm(float bpm) => float.IsFinite(bpm) && bpm > 0f;

        private static void RequireAudioData(AudioObj audio)
        {
            if (audio.Data == null || audio.Data.Length == 0)
            {
                throw new InvalidDataException("The imported audio contains no samples.");
            }
        }

        private static async Task<float> GetSourceBpmAsync(
            AudioObj audio, IProgress<string>? progress, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsValidBpm(audio.Bpm)) return audio.Bpm;
            if (IsValidBpm(audio.ScannedBpm)) return audio.ScannedBpm;

            progress?.Report("Scanning source BPM.");
            cancellationToken.ThrowIfCancellationRequested();
            // The scanner has no cancellation token; await it before releasing the audio.
            double scanned = await Task.Run(() => BeatScanner.ScanBpmAsync(audio)).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            float sourceBpm = (float)scanned;
            if (!IsValidBpm(sourceBpm))
            {
                throw new InvalidDataException("A finite, positive source BPM could not be determined.");
            }

            return sourceBpm;
        }

        private static async Task StretchAsync(
            AudioObj audio, PlaylistStretchSettings settings, IProgress<string>? progress,
            CancellationToken cancellationToken)
        {
            float sourceBpm = await GetSourceBpmAsync(audio, progress, cancellationToken).ConfigureAwait(false);
            double factor = CalculateStretchFactor(sourceBpm, settings.TargetBpm);
            audio.Bpm = sourceBpm;
            audio.ScannedBpm = sourceBpm;
            cancellationToken.ThrowIfCancellationRequested();
            float originalPeak = await audio.GetPeakAmplitudeAsync(settings.Threads).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report("Stretching audio to the target BPM.");
            await ApplyStretchAsync(audio, settings, factor, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (settings.Trim)
            {
                progress?.Report("Trimming silence.");
                await BeatGridFinder.TrimSilenceAsync(audio).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }

            RequireAudioData(audio);
            if (originalPeak > 0f)
            {
                await audio.NormalizeAsync(originalPeak, settings.Threads).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }

            audio.Bpm = settings.TargetBpm;
            audio.ScannedBpm = settings.TargetBpm;
        }

        private static async Task ApplyStretchAsync(
            AudioObj audio, PlaylistStretchSettings settings, double factor,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (settings.UseV2)
            {
                await global::TimeStretcher_V2.Timestretch_V2Async(
                    audio, factor, settings.AutoChunking ? null : settings.ChunkSize,
                    settings.AutoChunking ? null : settings.Overlap, null, cancellationToken).ConfigureAwait(false);
                return;
            }

            // V1 cannot be cancelled; never abandon its task while it owns audio data.
            AudioObj result = await Task.Run(() => TimeStretcher.TimeStretchAllThreadsAsync(
                audio, settings.ChunkSize, settings.Overlap, factor, false, 1.0f,
                settings.Threads, null, settings.Offload, settings.Channeled)).ConfigureAwait(false);
            if (!ReferenceEquals(audio, result))
            {
                try
                {
                    audio.ReplaceWith(result, disposeSource: true);
                }
                catch
                {
                    result.Dispose();
                    throw;
                }
            }
        }
    }
}
