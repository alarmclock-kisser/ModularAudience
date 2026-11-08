using System.Globalization;
using System.Text.Json;

namespace ModularAudience.Forms.Modules.Dialogs;

public sealed record LlmParameterDefinition(string Key, string Label, double Default, double Minimum, double Maximum, bool Integer = false, bool EnabledByDefault = false, bool Extension = false)
{
    public double Parse(string text)
    {
        if (!double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            || !double.IsFinite(value) || value < Minimum || value > Maximum || Integer && value != Math.Truncate(value))
            throw new ArgumentException($"{Label}: enter {(Integer ? "an integer" : "a number")} between {Minimum} and {Maximum}.");
        return value;
    }
}
public sealed class LlmParameterSetting
{
    public bool Enabled { get; set; }
    public double Value { get; set; }
}
public sealed class BreakbeatLlmSettings
{
    public string Address { get; set; } = "http://127.0.0.1:9931";
    public string Prompt { get; set; } = "";
    public bool JsonResponse { get; set; } = true;
    public bool KeepContext { get; set; }
    public Dictionary<string, LlmParameterSetting> Parameters { get; set; } = [];
    public static IReadOnlyList<LlmParameterDefinition> Definitions { get; } = new LlmParameterDefinition[]
    {
        new("temperature", "Temperature", 0.6, 0, 2, EnabledByDefault: true),
        new("min_p", "Min P", 0.05, 0, 1, Extension: true),
        new("top_p", "Top P", 0.95, 0, 1),
        new("top_k", "Top K", 40, 0, 100000, Integer: true, Extension: true),
        new("repetition_penalty", "Repetition penalty", 1.1, 0.01, 3, Extension: true),
        new("presence_penalty", "Presence penalty", 0, -2, 2),
        new("frequency_penalty", "Frequency penalty", 0, -2, 2),
        new("max_tokens", "Max output tokens", 4096, 1, 131072, Integer: true),
        new("seed", "Seed", 42, 0, int.MaxValue, Integer: true)
    };
    public static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ModularAudience", "breakbeat-llm-settings.json");
    public BreakbeatLlmSettings Copy() => JsonSerializer.Deserialize<BreakbeatLlmSettings>(JsonSerializer.Serialize(this))!;
    public void Normalize()
    {
        Address ??= "http://127.0.0.1:9931"; Prompt ??= ""; Parameters ??= [];
        foreach (var definition in Definitions)
        {
            if (!Parameters.TryGetValue(definition.Key, out var value) || value == null)
                Parameters[definition.Key] = new() { Enabled = definition.EnabledByDefault, Value = definition.Default };
            else
            {
                try { definition.Parse(value.Value.ToString("R", CultureInfo.InvariantCulture)); }
                catch (ArgumentException) { value.Value = definition.Default; }
            }
        }
    }
    public static BreakbeatLlmSettings Load(string? path = null)
    {
        path ??= SettingsPath;
        var result = File.Exists(path) ? JsonSerializer.Deserialize<BreakbeatLlmSettings>(File.ReadAllText(path)) ?? new() : new();
        result.Normalize(); return result;
    }
    public void Save(string? path = null)
    {
        path ??= SettingsPath; Normalize();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public void ApplyTo(Dictionary<string, object?> payload)
    {
        Normalize();
        foreach (var definition in Definitions)
        {
            payload.Remove(definition.Key);
            var value = Parameters[definition.Key];
            if (!value.Enabled) continue;
            definition.Parse(value.Value.ToString("R", CultureInfo.InvariantCulture));
            payload[definition.Key] = definition.Integer ? (object)(long)value.Value : value.Value;
        }
        if (JsonResponse) payload["response_format"] = new Dictionary<string, string> { ["type"] = "json_object" };
        else payload.Remove("response_format");
    }
}
