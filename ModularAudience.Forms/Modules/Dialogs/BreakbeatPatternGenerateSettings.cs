using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace ModularAudience.Forms.Modules.Dialogs
{
    /// <summary>
    /// Randomization parameters for the Generate rectangle selection in the
    /// Breakbeat Pattern Editor. Persisted globally as JSON in the
    /// user AppData (restore + Ctrl+click skip).
    /// </summary>
    internal sealed class BreakbeatGenerateOptions
    {
        public int DensityPercent { get; set; } = BreakbeatPatternGenerateSettings.DefaultDensityPercent;
        public int AmenFactorPercent { get; set; } = BreakbeatPatternGenerateSettings.DefaultAmenFactorPercent;
        public int MotifLengthSteps { get; set; } = BreakbeatPatternGenerateSettings.DefaultMotifLengthSteps;
        public int MotifRepeatPercent { get; set; } = BreakbeatPatternGenerateSettings.DefaultMotifRepeatPercent;
        public int MaxNoteLengthSteps { get; set; } = BreakbeatPatternGenerateSettings.DefaultMaxNoteLengthSteps;
        public int MinNoteLengthSteps { get; set; } = BreakbeatPatternGenerateSettings.DefaultMinNoteLengthSteps;
        public int PitchChancePercent { get; set; } = BreakbeatPatternGenerateSettings.DefaultPitchChancePercent;
        public float PitchRangeSemitones { get; set; } = BreakbeatPatternGenerateSettings.DefaultPitchRangeSemitones;
        public bool AllowPitchedNotes { get; set; } = BreakbeatPatternGenerateSettings.DefaultAllowPitchedNotes;
        public float VolumeMinPercent { get; set; } = BreakbeatPatternGenerateSettings.DefaultVolumeMinPercent;
        public float VolumeMaxPercent { get; set; } = BreakbeatPatternGenerateSettings.DefaultVolumeMaxPercent;
        public bool AllowTimeStretch { get; set; } = BreakbeatPatternGenerateSettings.DefaultAllowTimeStretch;
        public bool AllowVarispeed { get; set; } = BreakbeatPatternGenerateSettings.DefaultAllowVarispeed;
        public bool AllowTruncated { get; set; } = BreakbeatPatternGenerateSettings.DefaultAllowTruncated;
        public int Seed { get; set; } = BreakbeatPatternGenerateSettings.DefaultSeed;

        public BreakbeatGenerateOptions Clone() => (BreakbeatGenerateOptions)this.MemberwiseClone();
    }

    internal static class BreakbeatPatternGenerateSettings
    {
        public const int DefaultDensityPercent = 45;
        public const int DefaultAmenFactorPercent = 30;
        public const int DefaultMotifLengthSteps = 4;
        public const int DefaultMotifRepeatPercent = 50;
        public const int DefaultMaxNoteLengthSteps = 2;
        public const int DefaultMinNoteLengthSteps = 1;
        public const int DefaultPitchChancePercent = 25;
        public const float DefaultPitchRangeSemitones = 3f;
        public const bool DefaultAllowPitchedNotes = false;
        public const float DefaultVolumeMinPercent = 90f;
        public const float DefaultVolumeMaxPercent = 110f;
        public const bool DefaultAllowTimeStretch = false;
        public const bool DefaultAllowVarispeed = false;
        public const bool DefaultAllowTruncated = false;
        public const int DefaultSeed = 0;

        private static readonly object Sync = new();

        private static string SettingsDirectoryPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ModularAudience");

        private static string SettingsFilePath => Path.Combine(
            SettingsDirectoryPath,
            "BreakbeatPatternGenerateSettings.json");

        public static BreakbeatGenerateOptions Load()
        {
            lock (Sync)
            {
                try
                {
                    if (!File.Exists(SettingsFilePath))
                    {
                        return new BreakbeatGenerateOptions();
                    }

                    string json = File.ReadAllText(SettingsFilePath);
                    return Normalize(JsonSerializer.Deserialize<BreakbeatGenerateOptions>(json) ?? new BreakbeatGenerateOptions());
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Breakbeat generate settings load failed: {ex.Message}");
                    return new BreakbeatGenerateOptions();
                }
            }
        }

        public static void Save(BreakbeatGenerateOptions options)
        {
            lock (Sync)
            {
                string temporaryPath = $"{SettingsFilePath}.{Environment.ProcessId}.tmp";
                try
                {
                    Directory.CreateDirectory(SettingsDirectoryPath);
                    string json = JsonSerializer.Serialize(Normalize(options), new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(temporaryPath, json);
                    File.Move(temporaryPath, SettingsFilePath, true);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Breakbeat generate settings save failed: {ex.Message}");
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

        public static BreakbeatGenerateOptions Normalize(BreakbeatGenerateOptions options)
        {
            options.DensityPercent = Math.Clamp(options.DensityPercent, 0, 100);
            options.AmenFactorPercent = Math.Clamp(options.AmenFactorPercent, 0, 100);
            options.MotifLengthSteps = Math.Clamp(options.MotifLengthSteps, 1, 64);
            options.MotifRepeatPercent = Math.Clamp(options.MotifRepeatPercent, 0, 100);
            options.MaxNoteLengthSteps = Math.Clamp(options.MaxNoteLengthSteps, 1, 16);
            options.MinNoteLengthSteps = Math.Clamp(options.MinNoteLengthSteps, 1, 16);
            if (options.MinNoteLengthSteps > options.MaxNoteLengthSteps)
            {
                (options.MinNoteLengthSteps, options.MaxNoteLengthSteps) = (options.MaxNoteLengthSteps, options.MinNoteLengthSteps);
            }
            options.PitchChancePercent = Math.Clamp(options.PitchChancePercent, 0, 100);
            options.PitchRangeSemitones = Math.Clamp(options.PitchRangeSemitones, 0f, 12f);
            options.VolumeMinPercent = Math.Clamp(options.VolumeMinPercent, 0f, 250f);
            options.VolumeMaxPercent = Math.Clamp(options.VolumeMaxPercent, 0f, 250f);
            if (options.VolumeMinPercent > options.VolumeMaxPercent)
            {
                (options.VolumeMinPercent, options.VolumeMaxPercent) = (options.VolumeMaxPercent, options.VolumeMinPercent);
            }

            options.Seed = Math.Max(0, options.Seed);
            return options;
        }
    }
}
