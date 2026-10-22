using ModularAudience.Audio.Processors_V1;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ModularAudience.Audio.Processing
{
    public static class YoutubeAudioImporter
    {
        /// <summary>
        /// Imports a local download without taking ownership of its file.
        /// Callers should normally supply a five-second cooldown.
        /// </summary>
        public static async Task<AudioObj> ImportAsync(
            string filePath, string title, bool temporary, TimeSpan cooldown,
            PlaylistStretchSettings? stretchSettings, IProgress<string>? progress,
            CancellationToken cancellationToken)
        {
            ValidateArguments(filePath, title, cooldown, stretchSettings);
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
