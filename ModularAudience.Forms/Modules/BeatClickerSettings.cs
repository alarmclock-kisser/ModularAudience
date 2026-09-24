using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;

namespace ModularAudience.Forms.Modules
{
    internal static class BeatClickerSettings
    {
        public const int DefaultBackgroundOpacityPercent = 33;
        public const int MinBackgroundOpacityPercent = 10;
        public const int MaxBackgroundOpacityPercent = 100;
        public const int DefaultInputDelayMs = 0;
        public const int MinInputDelayMs = -250;
        public const int MaxInputDelayMs = 250;
        public const int DefaultClickKeyData = (int)Keys.Space;

        private static readonly object Sync = new();

        private sealed class SettingsData
        {
            public int BackgroundOpacityPercent { get; set; } = DefaultBackgroundOpacityPercent;
            public int InputDelayMs { get; set; } = DefaultInputDelayMs;
            public int ClickKeyData { get; set; } = DefaultClickKeyData;
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
                    return ClampBackgroundOpacity(LoadSettingsUnsafe().BackgroundOpacityPercent);
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
            lock (Sync)
            {
                try
                {
                    SettingsData settings = LoadSettingsUnsafe();
                    settings.BackgroundOpacityPercent = ClampBackgroundOpacity(percent);
                    SaveSettingsUnsafe(settings);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"BeatClicker settings save failed: {ex.Message}");
                }
            }
        }

        public static int LoadInputDelayMs()
        {
            lock (Sync)
            {
                try
                {
                    return ClampInputDelayMs(LoadSettingsUnsafe().InputDelayMs);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"BeatClicker input delay load failed: {ex.Message}");
                    return DefaultInputDelayMs;
                }
            }
        }

        public static void SaveInputDelayMs(int delayMs)
        {
            lock (Sync)
            {
                try
                {
                    SettingsData settings = LoadSettingsUnsafe();
                    settings.InputDelayMs = ClampInputDelayMs(delayMs);
                    SaveSettingsUnsafe(settings);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"BeatClicker input delay save failed: {ex.Message}");
                }
            }
        }

        public static int LoadClickKeyData()
        {
            lock (Sync)
            {
                try
                {
                    return NormalizeClickKeyData(LoadSettingsUnsafe().ClickKeyData);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"BeatClicker click key load failed: {ex.Message}");
                    return DefaultClickKeyData;
                }
            }
        }

        public static void SaveClickKeyData(int keyData)
        {
            lock (Sync)
            {
                try
                {
                    SettingsData settings = LoadSettingsUnsafe();
                    settings.ClickKeyData = NormalizeClickKeyData(keyData);
                    SaveSettingsUnsafe(settings);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"BeatClicker click key save failed: {ex.Message}");
                }
            }
        }

        private static SettingsData LoadSettingsUnsafe()
        {
            if (!File.Exists(SettingsFilePath))
            {
                return new SettingsData();
            }

            try
            {
                string json = File.ReadAllText(SettingsFilePath);
                return JsonSerializer.Deserialize<SettingsData>(json) ?? new SettingsData();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"BeatClicker settings file read failed: {ex.Message}");
                return new SettingsData();
            }
        }

        private static void SaveSettingsUnsafe(SettingsData settings)
        {
            string temporaryPath = $"{SettingsFilePath}.{Environment.ProcessId}.tmp";
            try
            {
                Directory.CreateDirectory(SettingsDirectoryPath);
                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(temporaryPath, json);
                File.Move(temporaryPath, SettingsFilePath, true);
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

        private static int ClampBackgroundOpacity(int percent)
        {
            return Math.Clamp(
                percent,
                MinBackgroundOpacityPercent,
                MaxBackgroundOpacityPercent);
        }

        private static int ClampInputDelayMs(int delayMs)
        {
            return Math.Clamp(
                delayMs,
                MinInputDelayMs,
                MaxInputDelayMs);
        }

        private static int NormalizeClickKeyData(int keyData)
        {
            Keys keyCode = (Keys)keyData & Keys.KeyCode;
            return keyCode == Keys.None || keyCode == Keys.Escape
                ? DefaultClickKeyData
                : (int)keyCode;
        }
    }
}
