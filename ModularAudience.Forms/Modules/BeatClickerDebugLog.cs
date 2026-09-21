using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ModularAudience.Forms.Modules
{
    /// <summary>
    /// Verbose, millisecond-precise debug logger for the Beat Clicker Game. When enabled
    /// (via the "Create Debug Log" context-menu entry), it records every element-generation
    /// decision (with the audio criteria and small audio float[] snippets that drove it) and
    /// every player input event (mouse position, click timing, delays) to a .TXT file named
    /// "{TrackName}-{Difficulty}.TXT" in a dedicated, git-ignored directory.
    ///
    /// All timestamps are relative to the game clock (00:00:000 at game start). The log is
    /// thread-safe (a single lock) because beatmap generation runs on a background thread
    /// while input events arrive on the UI thread.
    /// </summary>
    public sealed class BeatClickerDebugLog : IDisposable
    {
        private readonly object _lock = new();
        private readonly StreamWriter _writer;
        private readonly string _filePath;
        private readonly float _sampleRate;
        private readonly float[]? _audioData;
        private readonly int _channels;
        private readonly bool _ownsWriter;

        public string FilePath => _filePath;

        private BeatClickerDebugLog(StreamWriter writer, string filePath, float sampleRate, float[]? audioData, int channels, bool ownsWriter)
        {
            _writer = writer;
            _filePath = filePath;
            _sampleRate = sampleRate;
            _audioData = audioData;
            _channels = channels;
            _ownsWriter = ownsWriter;
        }

        /// <summary>
        /// Creates a debug log for the given track. The file name encodes the game mode
        /// ("classic" or "taiko") so classic and taiko logs are easy to tell apart. Returns null
        /// if the directory/file could not be created (logging is best-effort and must never
        /// break the game).
        /// </summary>
        public static BeatClickerDebugLog? Create(string trackName, string difficulty, bool isTaikoMode, float sampleRate, float[]? audioData, int channels)
        {
            try
            {
                string dir = GetLogDirectory();
                string safeName = SanitizeFileName(trackName);
                string safeDiff = SanitizeFileName(difficulty);
                string mode = isTaikoMode ? "taiko" : "classic";
                // Prefix the file name with a ddMMyyyy-hhmmss timestamp so multiple runs of the
                // same track/difficulty are easy to tell apart and ordered by time.
                string stamp = DateTime.Now.ToString("ddMMyyyy-HHmmss", CultureInfo.InvariantCulture);
                string fileName = $"{stamp}-{safeName}-{mode}-{safeDiff}.TXT";
                string path = Path.Combine(dir, fileName);

                var writer = new StreamWriter(path, append: false, Encoding.UTF8);
                writer.AutoFlush = true;
                var log = new BeatClickerDebugLog(writer, path, sampleRate, audioData, channels, ownsWriter: true);

                log.Header(
                    trackName,
                    difficulty,
                    isTaikoMode,
                    sampleRate,
                    audioData?.Length ?? 0,
                    channels);
                return log;
            }
            catch
            {
                return null;
            }
        }

        private static string GetLogDirectory()
        {
            // Save the .TXT files in the repo root (the folder that contains the .slnx),
            // in a dedicated, git-ignored subfolder "BeatClickerDebugLogs". Walk up from the
            // binary output directory until the solution file is found; fall back to the
            // binary directory if it can't be located (e.g. single-file publish).
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BeatClickerDebugLogs");
            try
            {
                string? current = AppDomain.CurrentDomain.BaseDirectory;
                for (int i = 0; i < 12 && current != null; i++)
                {
                    if (Directory.EnumerateFiles(current, "*.slnx").Any()
                        || Directory.EnumerateFiles(current, "*.sln").Any())
                    {
                        dir = Path.Combine(current, "BeatClickerDebugLogs");
                        break;
                    }
                    current = Directory.GetParent(current)?.FullName;
                }
            }
            catch { }
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "unnamed";
            }
            char[] invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                // Replace whitespace and any invalid/special character with '_' so the
                // track name stays readable but is safe for the file system.
                bool isSpace = char.IsWhiteSpace(c);
                bool isInvalid = Array.IndexOf(invalid, c) >= 0;
                sb.Append(isSpace || isInvalid ? '_' : c);
            }
            string result = sb.ToString().Trim('_');
            return result.Length == 0 ? "unnamed" : result;
        }

        private void Header(string trackName, string difficulty, bool isTaikoMode, float sampleRate, long sampleCount, int channels)
        {
            lock (_lock)
            {
                _writer.WriteLine("=== Beat Clicker Game — Verbose Debug Log ===");
                _writer.WriteLine($"Track: {trackName}");
                _writer.WriteLine($"Mode: {(isTaikoMode ? "Taiko" : "Classic")}");
                _writer.WriteLine($"Difficulty: {difficulty}");
                _writer.WriteLine($"SampleRate: {sampleRate:F0} Hz");
                _writer.WriteLine($"TotalSamples: {sampleCount}  Channels: {channels}");
                _writer.WriteLine($"Created: {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)}");
                _writer.WriteLine("Timestamps are relative to game start (00:00:000).");
                _writer.WriteLine();
                _writer.WriteLine("--- BEATMAP GENERATION ---");
                _writer.WriteLine();
            }
        }

        /// <summary>Formats a game-clock time (seconds) as 00:00:000 (h:mm:ss.fff).</summary>
        private static string Ts(float seconds)
        {
            if (seconds < 0)
            {
                seconds = 0;
            }
            var ts = TimeSpan.FromSeconds(seconds);
            return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}.{ts.Milliseconds:000}";
        }

        public void Log(float gameTime, string message)
        {
            lock (_lock)
            {
                _writer.WriteLine($"[{Ts(gameTime)}] {message}");
            }
        }

        public void LogSection(float gameTime, string title)
        {
            lock (_lock)
            {
                _writer.WriteLine();
                _writer.WriteLine($"[{Ts(gameTime)}] === {title} ===");
            }
        }

        /// <summary>
        /// Logs a small audio float[] snippet (the first <paramref name="count"/> samples
        /// starting at <paramref name="timeSeconds"/>) so the audio that drove a decision
        /// can be inspected. Samples are printed with 4 decimal places, comma-separated.
        /// </summary>
        public void LogAudioSnippet(float gameTime, string label, float timeSeconds, int count = 32)
        {
            lock (_lock)
            {
                _writer.WriteLine($"[{Ts(gameTime)}]   {label} @ {timeSeconds:F4}s:");
                if (_audioData == null || _audioData.Length == 0 || _sampleRate <= 0)
                {
                    _writer.WriteLine("    (no audio data)");
                    return;
                }

                int ch = Math.Max(1, _channels);
                int startFrame = (int)(timeSeconds * _sampleRate);
                int endFrame = Math.Min(startFrame + count, _audioData.Length / ch);
                if (startFrame >= endFrame)
                {
                    _writer.WriteLine("    (out of range)");
                    return;
                }

                var sb = new StringBuilder();
                for (int i = startFrame; i < endFrame; i++)
                {
                    if (sb.Length > 0)
                    {
                        sb.Append(',');
                    }
                    sb.Append(_audioData[i * ch].ToString("F4", CultureInfo.InvariantCulture));
                }
                _writer.WriteLine($"    [{sb}]");
            }
        }

        public void LogPlayerInput(float gameTime, string action, int x, int y, float extraValue, string extraLabel)
        {
            lock (_lock)
            {
                _writer.WriteLine($"[{Ts(gameTime)}]   INPUT {action} @ ({x},{y})  {extraLabel}={extraValue:F4}");
            }
        }

        public void LogPlayerInput(float gameTime, string action, int x, int y)
        {
            lock (_lock)
            {
                _writer.WriteLine($"[{Ts(gameTime)}]   INPUT {action} @ ({x},{y})");
            }
        }

        public void LogDecision(float gameTime, string decision, string criteria)
        {
            lock (_lock)
            {
                _writer.WriteLine($"[{Ts(gameTime)}]   DECISION {decision}  | {criteria}");
            }
        }

        public void LogHit(float gameTime, string kind, float objTime, float timeUntilHit, int x, int y)
        {
            lock (_lock)
            {
                _writer.WriteLine($"[{Ts(gameTime)}]   HIT {kind}  objTime={objTime:F4}s  timeUntilHit={timeUntilHit * 1000:F1}ms  @ ({x},{y})");
            }
        }

        public void LogMiss(float gameTime, string kind, float objTime, float timeUntilHit, string reason)
        {
            lock (_lock)
            {
                _writer.WriteLine($"[{Ts(gameTime)}]   MISS {kind}  objTime={objTime:F4}s  timeUntilHit={timeUntilHit * 1000:F1}ms  reason={reason}");
            }
        }

        public void LogEvent(float gameTime, string message)
        {
            lock (_lock)
            {
                _writer.WriteLine($"[{Ts(gameTime)}]   EVENT {message}");
            }
        }

        public void LogSummary(float gameTime, int hits, int misses, int bestStreak, float passRate)
        {
            lock (_lock)
            {
                _writer.WriteLine();
                _writer.WriteLine($"[{Ts(gameTime)}] === GAME SUMMARY ===");
                _writer.WriteLine($"Hits: {hits}  Misses: {misses}  BestStreak: {bestStreak}  PassRate: {passRate:F2}%");
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                try
                {
                    _writer.Flush();
                    if (_ownsWriter)
                    {
                        _writer.Dispose();
                    }
                }
                catch { }
            }
        }
    }
}
