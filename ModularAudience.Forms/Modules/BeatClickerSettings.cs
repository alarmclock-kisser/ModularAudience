using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace ModularAudience.Forms.Modules
{
    internal static class BeatClickerSettings
    {
        public const int DefaultBackgroundOpacityPercent = 33;
        public const int MinBackgroundOpacityPercent = 10;
        public const int MaxBackgroundOpacityPercent = 100;

        private static readonly object Sync = new();

        private sealed class SettingsData
        {
            public int BackgroundOpacityPercent { get; set; } = DefaultBackgroundOpacityPercent;
        }

        private static string SettingsDirectoryPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ModularAudience");

        private static string SettingsFilePath => Path.Combine(
            SettingsDirectoryPath,
            "BeatClickerSettings.json");

        public static int LoadBackgroundOpacityPercent()
        {
            lock (Sync)
            {
                try
                {
                    if (!File.Exists(SettingsFilePath))
                    {
                        return DefaultBackgroundOpacityPercent;
                    }

                    string json = File.ReadAllText(SettingsFilePath);
                    SettingsData? settings = JsonSerializer.Deserialize<SettingsData>(json);
                    return ClampBackgroundOpacity(settings?.BackgroundOpacityPercent ?? DefaultBackgroundOpacityPercent);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"BeatClicker settings load failed: {ex.Message}");
                    return DefaultBackgroundOpacityPercent;
                }
            }
        }

        public static void SaveBackgroundOpacityPercent(int percent)
        {
            int clampedPercent = ClampBackgroundOpacity(percent);
            string temporaryPath = $"{SettingsFilePath}.{Environment.ProcessId}.tmp";

            lock (Sync)
            {
                try
                {
                    Directory.CreateDirectory(SettingsDirectoryPath);
                    var settings = new SettingsData { BackgroundOpacityPercent = clampedPercent };
                    string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(temporaryPath, json);
                    File.Move(temporaryPath, SettingsFilePath, true);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"BeatClicker settings save failed: {ex.Message}");
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

        private static int ClampBackgroundOpacity(int percent)
        {
            return Math.Clamp(
                percent,
                MinBackgroundOpacityPercent,
                MaxBackgroundOpacityPercent);
        }
    }
}
