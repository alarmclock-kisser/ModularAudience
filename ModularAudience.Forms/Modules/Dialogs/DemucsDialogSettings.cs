using ModularAudience.Onnx;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModularAudience.Forms;

internal sealed class DemucsDialogSettings
{
    public List<string> ModelDirectories { get; set; } = ["D:/Models/Demucs"];
    public string SelectedModel { get; set; } = "";
    public int KeepModelLoadedSeconds { get; set; }
    public DemucsLoadOptions Options { get; set; } = new();
    public int[] SelectedDirectMLDeviceIds { get; set; } = [];
    public string[] SelectedStems { get; set; } = ["drums", "bass", "other", "vocals", "guitar", "piano"];
    internal static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ModularAudience", "demucs-settings.json");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true, Converters = { new JsonStringEnumConverter() }
    };

    internal static DemucsDialogSettings Load(string? path = null)
    {
        path ??= SettingsPath;
        if (!File.Exists(path)) return new();
        var result = JsonSerializer.Deserialize<DemucsDialogSettings>(File.ReadAllText(path), JsonOptions) ?? new();
        result.ModelDirectories ??= ["D:/Models/Demucs"];
        result.Options ??= new();
        result.SelectedDirectMLDeviceIds ??= [];
        result.SelectedStems ??= [];
        result.KeepModelLoadedSeconds = Math.Clamp(result.KeepModelLoadedSeconds, 0, 86400);
        return result;
    }

    internal void Save(string? path = null)
    {
        path ??= SettingsPath;
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string temporary = fullPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(temporary, fullPath, true);
    }
}
