using System.Diagnostics;
using System.Text.Json;

namespace ModularAudience.Forms.Modules.Dialogs;

internal sealed class BreakbeatRecordBeatSettings
{
    public string InputDeviceId { get; set; } = string.Empty;

    internal static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ModularAudience",
        "BreakbeatRecordBeatSettings.json");

    internal static BreakbeatRecordBeatSettings Load(string? path = null)
    {
        path ??= SettingsPath;
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<BreakbeatRecordBeatSettings>(File.ReadAllText(path)) ?? new()
                : new();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Unable to load Breakbeat Record Beat settings: {exception.Message}");
            return new();
        }
    }

    internal void Save(string? path = null)
    {
        path ??= SettingsPath;
        string temporaryPath = $"{path}.{Environment.ProcessId}.tmp";
        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
