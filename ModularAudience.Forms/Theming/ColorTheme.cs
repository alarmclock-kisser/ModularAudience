using System.Drawing;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModularAudience.Forms.Theming;

public sealed class ColorTheme
{
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "Custom theme";
    public ThemeColors Palette { get; set; } = new();
    public List<ThemeRule> Rules { get; set; } = [];
    public static ColorTheme Dark() => new()
    {
        Name = "Dark",
        Palette = new() { Background = "#202124", Foreground = "#E8EAED", InputBackground = "#292A2D",
            InputForeground = "#F1F3F4", SelectionBackground = "#375A88", SelectionForeground = "#FFFFFF",
            Border = "#606368", DisabledForeground = "#92969C", Accent = "#8AB4F8" }
    };
    public static ColorTheme Light() => new()
    {
        Name = "Light",
        Palette = new() { Background = "#F4F4F4", Foreground = "#202124", InputBackground = "#FFFFFF",
            InputForeground = "#202124", SelectionBackground = "#2864B4", SelectionForeground = "#FFFFFF",
            Border = "#B0B0B0", DisabledForeground = "#808080", Accent = "#2864B4" }
    };
    public ColorTheme Clone() => ThemeFiles.Deserialize(JsonSerializer.Serialize(this, ThemeFiles.JsonOptions));
}

public sealed class ThemeColors
{
    public string? Background { get; set; }
    public string? Foreground { get; set; }
    public string? InputBackground { get; set; }
    public string? InputForeground { get; set; }
    public string? SelectionBackground { get; set; }
    public string? SelectionForeground { get; set; }
    public string? Border { get; set; }
    public string? DisabledForeground { get; set; }
    public string? Accent { get; set; }
}

public sealed class ThemeRule
{
    public bool Enabled { get; set; } = true;
    public string Description { get; set; } = "Color rule";
    public string FormType { get; set; } = "*";
    public string ControlType { get; set; } = "*";
    public string ElementPath { get; set; } = "*";
    // Any writable Color property, including properties supplied by custom controls.
    public Dictionary<string, string> Colors { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public override string ToString() => $"{(Enabled ? "" : "[disabled] ")}{Description}";
}

public static class ThemeFiles
{
    public static string SettingsDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ModularAudience", "Themes");
    public static string ActiveThemePath => Path.Combine(SettingsDirectory, "active-theme.json");
    public static string[] SavedThemes() => Directory.Exists(SettingsDirectory)
        ? Directory.EnumerateFiles(SettingsDirectory, "*.json").Where(p => !string.Equals(p, ActiveThemePath, StringComparison.OrdinalIgnoreCase))
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToArray() : [];
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true, PropertyNameCaseInsensitive = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    public static Color ParseColor(string text)
    {
        if (text.Equals("Transparent", StringComparison.OrdinalIgnoreCase)) return Color.Transparent;
        if (text.StartsWith('#') && (text.Length == 7 || text.Length == 9))
        {
            uint value = Convert.ToUInt32(text[1..], 16);
            return Color.FromArgb(unchecked((int)(text.Length == 7 ? value | 0xFF000000 : value)));
        }
        if (Enum.TryParse<KnownColor>(text, true, out var known)) return Color.FromKnownColor(known);
        throw new FormatException($"Invalid color '{text}'. Use #RRGGBB, #AARRGGBB or a named color.");
    }
    public static string FormatColor(Color color) => color.A == 255 ? $"#{color.R:X2}{color.G:X2}{color.B:X2}" : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
    public static ColorTheme Deserialize(string json)
    {
        var theme = JsonSerializer.Deserialize<ColorTheme>(json, JsonOptions) ?? throw new JsonException("The theme is empty.");
        if (theme.Version != 1) throw new JsonException("Unsupported theme version.");
        if (theme.Palette == null || theme.Rules == null) throw new JsonException("Palette and rules are required.");
        foreach (var property in typeof(ThemeColors).GetProperties())
            if (property.GetValue(theme.Palette) is string value && !string.IsNullOrWhiteSpace(value)) ParseColor(value);
        foreach (var rule in theme.Rules)
        {
            if (rule == null || rule.Colors == null || rule.FormType == null || rule.ControlType == null || rule.ElementPath == null)
                throw new JsonException("Invalid theme rule.");
            foreach (string value in rule.Colors.Values) ParseColor(value);
        }
        return theme;
    }
    public static ColorTheme Load(string path) => Deserialize(File.ReadAllText(path));
    public static void Save(string path, ColorTheme theme)
    {
        string json = JsonSerializer.Serialize(theme, JsonOptions);
        Deserialize(json);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, json); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
