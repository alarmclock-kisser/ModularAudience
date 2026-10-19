using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace ModularAudience.Forms.Modules.Dialogs
{
    /// <summary>
    /// Derivation parameters for the Remix button in the Breakbeat Pattern
    /// Editor: derive a variation from the current note multiselection.
    /// Persisted globally as JSON in the user AppData (restore + Ctrl+click skip).
    /// </summary>
    internal sealed class BreakbeatRemixOptions
    {
        // Master
        public int VariationPercent { get; set; } = BreakbeatPatternRemixSettings.DefaultVariationPercent;
        public int Seed { get; set; } = BreakbeatPatternRemixSettings.DefaultSeed;

        // Pattern detection / combinatorics
        public int MinPatternLengthSteps { get; set; } = BreakbeatPatternRemixSettings.DefaultMinPatternLengthSteps;
        public int MaxPatternLengthSteps { get; set; } = BreakbeatPatternRemixSettings.DefaultMaxPatternLengthSteps;
        public int MinPatternRepeats { get; set; } = BreakbeatPatternRemixSettings.DefaultMinPatternRepeats;
        public int PatternReusePercent { get; set; } = BreakbeatPatternRemixSettings.DefaultPatternReusePercent;
        public int MirrorPercent { get; set; } = BreakbeatPatternRemixSettings.DefaultMirrorPercent;
        public int SwapHalvesPercent { get; set; } = BreakbeatPatternRemixSettings.DefaultSwapHalvesPercent;
        public int RotateChancePercent { get; set; } = BreakbeatPatternRemixSettings.DefaultRotateChancePercent;
        public int RotateSteps { get; set; } = BreakbeatPatternRemixSettings.DefaultRotateSteps;
        public int LaneShiftChancePercent { get; set; } = BreakbeatPatternRemixSettings.DefaultLaneShiftChancePercent;
        public int LaneShiftSteps { get; set; } = BreakbeatPatternRemixSettings.DefaultLaneShiftSteps;
        public int HumanizeChancePercent { get; set; } = BreakbeatPatternRemixSettings.DefaultHumanizeChancePercent;
        public int HumanizeAmountPercent { get; set; } = BreakbeatPatternRemixSettings.DefaultHumanizeAmountPercent;

        // Note surgery
        public int GhostChancePercent { get; set; } = BreakbeatPatternRemixSettings.DefaultGhostChancePercent;
        public int GhostVolumePercent { get; set; } = BreakbeatPatternRemixSettings.DefaultGhostVolumePercent;
        public int RollChancePercent { get; set; } = BreakbeatPatternRemixSettings.DefaultRollChancePercent;
        public int RollPartsMax { get; set; } = BreakbeatPatternRemixSettings.DefaultRollPartsMax;
        public int MergeChancePercent { get; set; } = BreakbeatPatternRemixSettings.DefaultMergeChancePercent;
        public int MergeGapSteps { get; set; } = BreakbeatPatternRemixSettings.DefaultMergeGapSteps;
        public int ThinChancePercent { get; set; } = BreakbeatPatternRemixSettings.DefaultThinChancePercent;
        public int TrackShufflePercent { get; set; } = BreakbeatPatternRemixSettings.DefaultTrackShufflePercent;
        public int PitchChancePercent { get; set; } = BreakbeatPatternRemixSettings.DefaultPitchChancePercent;
        public float PitchRangeSemitones { get; set; } = BreakbeatPatternRemixSettings.DefaultPitchRangeSemitones;
        public int VolumeChancePercent { get; set; } = BreakbeatPatternRemixSettings.DefaultVolumeChancePercent;
        public float VolumeRangePercent { get; set; } = BreakbeatPatternRemixSettings.DefaultVolumeRangePercent;
        public bool AllowNoteKindChange { get; set; } = BreakbeatPatternRemixSettings.DefaultAllowNoteKindChange;

        // Destination
        public bool CopyToClipboard { get; set; } = BreakbeatPatternRemixSettings.DefaultCopyToClipboard;

        public BreakbeatRemixOptions Clone() => (BreakbeatRemixOptions)this.MemberwiseClone();
    }

    internal static class BreakbeatPatternRemixSettings
    {
        public const int DefaultVariationPercent = 70;
        public const int DefaultSeed = 0;

        public const int DefaultMinPatternLengthSteps = 2;
        public const int DefaultMaxPatternLengthSteps = 8;
        public const int DefaultMinPatternRepeats = 2;
        public const int DefaultPatternReusePercent = 50;
        public const int DefaultMirrorPercent = 25;
        public const int DefaultSwapHalvesPercent = 25;
        public const int DefaultRotateChancePercent = 25;
        public const int DefaultRotateSteps = 2;
        public const int DefaultLaneShiftChancePercent = 20;
        public const int DefaultLaneShiftSteps = 1;
        public const int DefaultHumanizeChancePercent = 15;
        public const int DefaultHumanizeAmountPercent = 15;

        public const int DefaultGhostChancePercent = 25;
        public const int DefaultGhostVolumePercent = 60;
        public const int DefaultRollChancePercent = 20;
        public const int DefaultRollPartsMax = 4;
        public const int DefaultMergeChancePercent = 15;
        public const int DefaultMergeGapSteps = 1;
        public const int DefaultThinChancePercent = 15;
        public const int DefaultTrackShufflePercent = 20;
        public const int DefaultPitchChancePercent = 25;
        public const float DefaultPitchRangeSemitones = 3f;
        public const int DefaultVolumeChancePercent = 30;
        public const float DefaultVolumeRangePercent = 15f;
        public const bool DefaultAllowNoteKindChange = false;

        public const bool DefaultCopyToClipboard = false;

        private static readonly object Sync = new();

        private static string SettingsDirectoryPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ModularAudience");

        private static string SettingsFilePath => Path.Combine(
            SettingsDirectoryPath,
            "BreakbeatPatternRemixSettings.json");

        public static BreakbeatRemixOptions Load()
        {
            lock (Sync)
            {
                try
                {
                    if (!File.Exists(SettingsFilePath))
                    {
                        return new BreakbeatRemixOptions();
                    }

                    string json = File.ReadAllText(SettingsFilePath);
                    return Normalize(JsonSerializer.Deserialize<BreakbeatRemixOptions>(json) ?? new BreakbeatRemixOptions());
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Breakbeat remix settings load failed: {ex.Message}");
                    return new BreakbeatRemixOptions();
                }
            }
        }

        public static void Save(BreakbeatRemixOptions options)
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
                    Debug.WriteLine($"Breakbeat remix settings save failed: {ex.Message}");
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

        public static BreakbeatRemixOptions Normalize(BreakbeatRemixOptions options)
        {
            options.VariationPercent = Math.Clamp(options.VariationPercent, 0, 100);
            options.Seed = Math.Max(0, options.Seed);

            options.MinPatternLengthSteps = Math.Clamp(options.MinPatternLengthSteps, 1, 64);
            options.MaxPatternLengthSteps = Math.Clamp(options.MaxPatternLengthSteps, 1, 64);
            if (options.MinPatternLengthSteps > options.MaxPatternLengthSteps)
            {
                (options.MinPatternLengthSteps, options.MaxPatternLengthSteps) =
                    (options.MaxPatternLengthSteps, options.MinPatternLengthSteps);
            }

            options.MinPatternRepeats = Math.Clamp(options.MinPatternRepeats, 2, 16);
            options.PatternReusePercent = Math.Clamp(options.PatternReusePercent, 0, 100);
            options.MirrorPercent = Math.Clamp(options.MirrorPercent, 0, 100);
            options.SwapHalvesPercent = Math.Clamp(options.SwapHalvesPercent, 0, 100);
            options.RotateChancePercent = Math.Clamp(options.RotateChancePercent, 0, 100);
            options.RotateSteps = Math.Clamp(options.RotateSteps, 1, 16);
            options.LaneShiftChancePercent = Math.Clamp(options.LaneShiftChancePercent, 0, 100);
            options.LaneShiftSteps = Math.Clamp(options.LaneShiftSteps, 1, 16);
            options.HumanizeChancePercent = Math.Clamp(options.HumanizeChancePercent, 0, 100);
            options.HumanizeAmountPercent = Math.Clamp(options.HumanizeAmountPercent, 0, 100);

            options.GhostChancePercent = Math.Clamp(options.GhostChancePercent, 0, 100);
            options.GhostVolumePercent = Math.Clamp(options.GhostVolumePercent, 0, 250);
            options.RollChancePercent = Math.Clamp(options.RollChancePercent, 0, 100);
            options.RollPartsMax = Math.Clamp(options.RollPartsMax, 2, 16);
            options.MergeChancePercent = Math.Clamp(options.MergeChancePercent, 0, 100);
            options.MergeGapSteps = Math.Clamp(options.MergeGapSteps, 0, 16);
            options.ThinChancePercent = Math.Clamp(options.ThinChancePercent, 0, 100);
            options.TrackShufflePercent = Math.Clamp(options.TrackShufflePercent, 0, 100);
            options.PitchChancePercent = Math.Clamp(options.PitchChancePercent, 0, 100);
            options.PitchRangeSemitones = Math.Clamp(options.PitchRangeSemitones, 0f, 24f);
            options.VolumeChancePercent = Math.Clamp(options.VolumeChancePercent, 0, 100);
            options.VolumeRangePercent = Math.Clamp(options.VolumeRangePercent, 0f, 100f);

            return options;
        }
    }
}
