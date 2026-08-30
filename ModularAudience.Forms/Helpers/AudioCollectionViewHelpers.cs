using ModularAudience.Audio;
using ModularAudience.Generators;
using System.Globalization;

namespace ModularAudience.Forms.Helpers
{
    internal static class AudioCollectionViewHelpers
    {
        private static readonly HashSet<char> InvalidFileNameChars = new(Path.GetInvalidFileNameChars());

        internal static string SanitizePathSegment(string? value)
        {
            string source = string.IsNullOrWhiteSpace(value) ? "Audio" : value!;
            char[] sanitized = source
                .Select(ch => InvalidFileNameChars.Contains(ch) ? '_' : ch)
                .ToArray();
            string result = new string(sanitized).Trim('_', ' ');
            return string.IsNullOrWhiteSpace(result) ? "Audio" : result;
        }

        internal static string EnsureAudioFileExtension(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return filePath;
            }

            return string.Equals(Path.GetExtension(filePath), "." + WindowMain.GlobalExportFormat, StringComparison.OrdinalIgnoreCase)
                ? filePath
                : Path.ChangeExtension(filePath, WindowMain.GlobalExportFormat);
        }

        internal static bool TryMapAtomizedLabelToDrumsetElement(string? label, out DrumsetElement element)
        {
            return Enum.TryParse(label, ignoreCase: true, out element);
        }

        internal static string FormatDurationText(AudioObj audio)
        {
            TimeSpan duration = ResolveDuration(audio);
            if (duration.TotalMilliseconds > 0 && duration.TotalMilliseconds < 8000)
            {
                int ms = Math.Max(1, (int) Math.Round(duration.TotalMilliseconds));
                return ms.ToString("0", CultureInfo.InvariantCulture) + " ms";
            }

            int minutes = Math.Max(0, (int) duration.TotalMinutes);
            int seconds = Math.Clamp(duration.Seconds, 0, 59);
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", minutes, seconds);
        }

        internal static TimeSpan ResolveDuration(AudioObj audio)
        {
            if (audio.Duration > TimeSpan.Zero)
            {
                return audio.Duration;
            }

            if (audio.Data != null && audio.Data.Length > 0 && audio.SampleRate > 0)
            {
                int channels = Math.Max(1, audio.Channels);
                double totalFrames = audio.Data.LongLength / (double) channels;
                double seconds = totalFrames / audio.SampleRate;
                if (seconds > 0)
                {
                    return TimeSpan.FromSeconds(seconds);
                }
            }

            return TimeSpan.Zero;
        }
    }
}
