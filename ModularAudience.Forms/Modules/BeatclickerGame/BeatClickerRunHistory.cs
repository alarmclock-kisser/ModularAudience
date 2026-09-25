using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace ModularAudience.Forms.Modules.BeatclickerGame
{
    internal sealed class BeatClickerRunSummary
    {
        public DateTimeOffset StartedUtc { get; set; }
        public string TrackName { get; set; } = string.Empty;
        public string Mode { get; set; } = string.Empty;
        public string Difficulty { get; set; } = string.Empty;
        public double Score { get; set; }
        public int Hits { get; set; }
        public int Misses { get; set; }
        public int BestStreak { get; set; }
        public int FinalStreak { get; set; }
        public double MeanTimingMs { get; set; }
        public int TimingSampleCount { get; set; }
        public double TrackPlayedPercent { get; set; }
        public double ObjectsResolvedPercent { get; set; }
        public double DurationSeconds { get; set; }
        public bool Completed { get; set; }
    }

    internal static class BeatClickerRunHistory
    {
        private sealed class HistoryData
        {
            public List<BeatClickerRunSummary> Runs { get; set; } = [];
        }

        private static readonly object Sync = new();

        private static string DirectoryPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ModularAudience");

        private static string FilePath => Path.Combine(DirectoryPath, "BeatClickerRunHistory.json");

        public static void Save(BeatClickerRunSummary summary)
        {
            lock (Sync)
            {
                string temporaryPath = $"{FilePath}.{Environment.ProcessId}.tmp";
                try
                {
                    HistoryData history = LoadUnsafe();
                    history.Runs ??= [];
                    history.Runs.Add(summary);
                    Directory.CreateDirectory(DirectoryPath);

                    string json = JsonSerializer.Serialize(history, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });
                    File.WriteAllText(temporaryPath, json);
                    File.Move(temporaryPath, FilePath, true);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"BeatClicker run history save failed: {ex.Message}");
                }
                finally
                {
                    try
                    {
                        if (File.Exists(temporaryPath))
                        {
                            File.Delete(temporaryPath);
                        }
                    }
                    catch { }
                }
            }
        }

        public static IReadOnlyList<BeatClickerRunSummary> Load()
        {
            lock (Sync)
            {
                return LoadUnsafe().Runs;
            }
        }

        private static HistoryData LoadUnsafe()
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    return new HistoryData();
                }

                string json = File.ReadAllText(FilePath);
                HistoryData history = JsonSerializer.Deserialize<HistoryData>(json) ?? new HistoryData();
                history.Runs ??= [];
                return history;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"BeatClicker run history load failed: {ex.Message}");
                return new HistoryData();
            }
        }
    }
}