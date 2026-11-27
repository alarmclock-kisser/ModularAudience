using System.Text.Json;

namespace ModularAudience.Forms.Modules.Dialogs;

public sealed class PitchAnalysisConfig
{
    public int Version { get; set; } = 1;
    public int MedianWindow { get; set; } = 5;
    public int HysteresisFrames { get; set; } = 3;
}

/// <summary>
/// Persists the pitch-analysis settings (median window and hysteresis) to a JSON
/// file in %AppData%\ModularAudience\PitchAnalysis so the user's tuning survives
/// between sessions.
/// </summary>
public sealed class PitchAnalysisConfigStore
{
    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ModularAudience", "PitchAnalysis");

    public static PitchAnalysisConfigStore Shared { get; } = new();

    public string DirectoryPath { get; }
    public string ConfigPath => Path.Combine(DirectoryPath, "config.json");

    private readonly object gate = new();
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public PitchAnalysisConfigStore(string? directory = null) => DirectoryPath = directory ?? DefaultDirectory;

    public PitchAnalysisConfig Load()
    {
        lock (gate)
        {
            if (!File.Exists(ConfigPath))
            {
                return new PitchAnalysisConfig();
            }

            try
            {
                string text = File.ReadAllText(ConfigPath);
                PitchAnalysisConfig? value = JsonSerializer.Deserialize<PitchAnalysisConfig>(text, Json);
                if (value is null || value.Version != 1)
                {
                    return new PitchAnalysisConfig();
                }

                value.MedianWindow = Math.Clamp(value.MedianWindow, 1, 51);
                value.HysteresisFrames = Math.Clamp(value.HysteresisFrames, 1, 30);
                return value;
            }
            catch
            {
                return new PitchAnalysisConfig();
            }
        }
    }

    public void Save(PitchAnalysisConfig config)
    {
        config.MedianWindow = Math.Clamp(config.MedianWindow, 1, 51);
        config.HysteresisFrames = Math.Clamp(config.HysteresisFrames, 1, 30);
        config.Version = 1;
        lock (gate)
        {
            WriteAtomic(ConfigPath, config);
        }
    }

    private static void WriteAtomic(string path, PitchAnalysisConfig value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, Json));
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
