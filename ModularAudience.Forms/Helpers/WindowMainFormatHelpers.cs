using ModularAudience.Audio;

namespace ModularAudience.Forms.Helpers
{
    internal static class WindowMainFormatHelpers
    {
        internal static string GetTimingString(float timing)
        {
            string timingString = string.Empty;
            if (timing < 0.5f)
            {
                timingString = "1/8";
            }
            else if (timing < 0.9f)
            {
                timingString = "1/4";
            }
            else if (timing < 1.5f)
            {
                timingString = "1/2";
            }
            else if (timing < 2.5f)
            {
                timingString = "1";
            }
            else if (timing < 3.5f)
            {
                timingString = "2";
            }
            else
            {
                timingString = timing.ToString("F1") + "x";
            }

            return timingString;
        }

        internal static string NormalizeFormatExtension(string? formatCandidate)
        {
            if (string.IsNullOrWhiteSpace(formatCandidate))
            {
                return ".wav";
            }

            string normalized = formatCandidate.Trim();
            if (!normalized.StartsWith(".", StringComparison.Ordinal))
            {
                normalized = "." + normalized;
            }

            return normalized.ToLowerInvariant();
        }

        internal static int ResolveBitSelection(string formatKey, object? selectedBit)
        {
            if (selectedBit is int bitValue)
            {
                return bitValue;
            }

            if (AudioExporter.AvailableExportFormats.TryGetValue(formatKey, out var bits) && bits.Length > 0)
            {
                if (formatKey.Equals(".wav", StringComparison.OrdinalIgnoreCase) && bits.Contains(24))
                {
                    return 24;
                }

                return bits[0];
            }

            var fallback = AudioExporter.AvailableExportFormats.FirstOrDefault();
            if (fallback.Value != null && fallback.Value.Length > 0)
            {
                return fallback.Value[0];
            }

            return 16;
        }
    }
}
