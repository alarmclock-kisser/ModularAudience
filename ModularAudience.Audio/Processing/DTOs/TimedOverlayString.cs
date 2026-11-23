using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ModularAudience.Audio.Processing.DTOs
{
    /// <summary>
    /// A single timed text overlay (e.g. a subtitle line) rendered onto visualizer frames.
    /// Lines are parsed from .txt subtitle files with timestamps, e.g.
    /// "00:00:03,500 - 00:00:08,000 Hello world"
    /// </summary>
    public class TimedOverlayString
    {
        public string Text { get; set; } = string.Empty;

        public double ShowTimeStartSeconds { get; set; } = 0.0;
        public double? ShowTimeEndSeconds { get; set; } = null; // Infinite if null

        public string FontFamily { get; set; } = "Arial";
        public float FontSize { get; set; } = 12f; // In points
        public string FontStyle { get; set; } = "Regular"; // Regular, Bold, Italic, etc.
        public bool Shadow { get; set; } = true;

        public string Color { get; set; } = "#FFFFFF"; // Hex color code, default is white

        public int? XOffset { get; set; } = null; // Centered
        public int? YOffset { get; set; } = null; // Centered
        public float Rotation { get; set; } = 0f; // In degrees, 0 is normal, clockwise rotation


        [JsonConstructor]
        public TimedOverlayString()
        {
        }

        public TimedOverlayString(string subtitlesLine, string fontFamily = "Arial", float fontSize = 12f, string fontStyle = "Regular", bool shadow = true, string color = "#FFFFFF", int? xOffset = null, int? yOffset = null, float rotation = 0f)
        {
            this.FontFamily = fontFamily;
            this.FontSize = fontSize;
            this.FontStyle = fontStyle;
            this.Shadow = shadow;
            this.Color = color.Replace("#", "") + "#";
            this.XOffset = xOffset;
            this.YOffset = yOffset;
            this.Rotation = 360.0f % rotation;

            this.ParseSubtitlesLine(subtitlesLine);
        }


        private void ParseSubtitlesLine(string subtitlesLine)
        {
            if (string.IsNullOrWhiteSpace(subtitlesLine))
            {
                throw new FormatException("Subtitle line is empty.");
            }

            // Pattern: start time, separator ( - -- - | -> >), end time, then the text.
            // Times: H:M:S.mmm or M:S.mmm, decimal separator dot or comma.
            var pattern = @"(\d{1,2}:\d{2}:\d{2}[.,]\d{3})\s*[-–—|→>]+\s*(\d{1,2}:\d{2}:\d{2}[.,]\d{3})\s*(.*)";
            var match = Regex.Match(subtitlesLine, pattern);

            if (!match.Success)
            {
                // Fallback: only one timestamp present, or none at all.
                var simpleTimePattern = @"(\d{1,2}:\d{2}:\d{2}[.,]\d{3})";
                var simpleMatch = Regex.Match(subtitlesLine, simpleTimePattern);

                if (simpleMatch.Success)
                {
                    this.ShowTimeStartSeconds = this.ParseTimeToSeconds(simpleMatch.Groups[1].Value);
                    this.ShowTimeEndSeconds = this.ShowTimeStartSeconds + 10.0; // Default 10 seconds
                    this.Text = subtitlesLine.Replace(simpleMatch.Value, "").Trim();
                }
                else
                {
                    throw new FormatException($"Could not parse any valid timestamp in line: {subtitlesLine}");
                }
                return;
            }

            string startTimeStr = match.Groups[1].Value;
            string endTimeStr = match.Groups[2].Value;
            string rawText = match.Groups[3].Value;

            this.ShowTimeStartSeconds = this.ParseTimeToSeconds(startTimeStr);
            this.ShowTimeEndSeconds = this.ParseTimeToSeconds(endTimeStr);

            // Sanitization: strip leading tabs and duplicate whitespace
            this.Text = Regex.Replace(rawText, @"^\s+|\s+$", "").Trim();
        }

        /// <summary>
        /// Converts various time formats (HH:mm:ss.fff, mm:ss.fff, etc.) and decimal
        /// separators (dot/comma) into seconds, best effort.
        /// </summary>
        private double ParseTimeToSeconds(string timeStr)
        {
            string normalized = timeStr.Replace(',', '.');

            string[] formats = { "hh:mm:ss.fff", "mm:ss.fff", "h:mm:ss.fff", "m:ss.fff" };

            if (TimeSpan.TryParseExact(normalized, formats, System.Globalization.CultureInfo.InvariantCulture,
                (System.Globalization.TimeSpanStyles)System.Globalization.DateTimeStyles.None, out TimeSpan ts))
            {
                return ts.TotalSeconds;
            }

            if (TimeSpan.TryParse(normalized, System.Globalization.CultureInfo.InvariantCulture, out ts))
            {
                return ts.TotalSeconds;
            }

            throw new FormatException($"Could not parse time string: {timeStr}");
        }


        public override string ToString()
        {
            return $"{TimeSpan.FromSeconds(this.ShowTimeStartSeconds).ToString(@"hh\:mm\:ss")} - {TimeSpan.FromSeconds(this.ShowTimeEndSeconds ?? this.ShowTimeStartSeconds + 10).ToString(@"hh\:mm\:ss")}: '{this.Text}'";
        }
    }
}
