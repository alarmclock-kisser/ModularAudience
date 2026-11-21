using System.Text.Json;

namespace ModularAudience.Forms.Modules.Dialogs;

internal sealed class BreakbeatPatternEditorNameSettings
{
    public const int MaximumRecentTemplates = 16;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string Template { get; set; } = string.Empty;
    public List<string> RecentTemplates { get; set; } = [];

    internal static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ModularAudience",
        "breakbeat-pattern-editor-settings.json");

    internal static BreakbeatPatternEditorNameSettings Load(string? path = null)
    {
        path ??= SettingsPath;
        if (!File.Exists(path))
        {
            return new BreakbeatPatternEditorNameSettings();
        }

        BreakbeatPatternEditorNameSettings settings = JsonSerializer.Deserialize<BreakbeatPatternEditorNameSettings>(
            File.ReadAllText(path),
            JsonOptions) ?? new BreakbeatPatternEditorNameSettings();
        settings.Template ??= string.Empty;
        settings.RecentTemplates = (settings.RecentTemplates ?? [])
            .Where(template => !string.IsNullOrWhiteSpace(template))
            .Distinct(StringComparer.Ordinal)
            .Take(MaximumRecentTemplates)
            .ToList();
        return settings;
    }

    internal void RecordSuccessfulTemplate(string template)
    {
        this.RecentTemplates.RemoveAll(existing => existing.Equals(template, StringComparison.Ordinal));
        this.RecentTemplates.Insert(0, template);
        if (this.RecentTemplates.Count > MaximumRecentTemplates)
        {
            this.RecentTemplates.RemoveRange(
                MaximumRecentTemplates,
                this.RecentTemplates.Count - MaximumRecentTemplates);
        }
    }

    internal void Save(string? path = null)
    {
        path ??= SettingsPath;
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string temporaryPath = fullPath + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(temporaryPath, fullPath, true);
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
