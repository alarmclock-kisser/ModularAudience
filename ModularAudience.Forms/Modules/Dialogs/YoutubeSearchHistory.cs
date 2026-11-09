using System.Text.Json;
using System.Text.RegularExpressions;

namespace ModularAudience.Forms.Modules.Dialogs;

public sealed class YoutubeSearchHistory
{
    public const int MaximumEntries = 32;
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ModularAudience", "youtube-search-history.json");
    private readonly string path;
    private readonly List<string> entries = [];
    public IReadOnlyList<string> Entries => this.entries.AsReadOnly();
    public YoutubeSearchHistory(string? path = null)
    {
        this.path = path ?? DefaultPath;
        if (!File.Exists(this.path)) return;
        foreach (string? entry in JsonSerializer.Deserialize<string[]>(File.ReadAllText(this.path)) ?? [])
        {
            string value = Clean(entry);
            if (value.Length > 0 && !this.entries.Contains(value, StringComparer.OrdinalIgnoreCase)) this.entries.Add(value);
            if (this.entries.Count == MaximumEntries) break;
        }
    }
    private static string Clean(string? query)
    {
        string value = new((query ?? "").Trim().Where(c => !char.IsControl(c)).Take(4096).ToArray());
        // Older UI tests accidentally wrote their Size.ToString() + handler names to
        // the real history. These are test artifacts, never placeholder suggestions.
        return Regex.IsMatch(value, @"^\{Width=\d+,\s*Height=\d+\}(?:Search_Click|Query_KeyDown)$",
            RegexOptions.CultureInvariant) ? string.Empty : value;
    }
    public void Add(string query)
    {
        string value = Clean(query);
        if (value.Length == 0) return;
        this.entries.RemoveAll(entry => entry.Equals(value, StringComparison.OrdinalIgnoreCase));
        this.entries.Insert(0, value);
        if (this.entries.Count > MaximumEntries) this.entries.RemoveRange(MaximumEntries, this.entries.Count - MaximumEntries);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(this.path))!);
        string temporary = this.path + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(this.entries, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, this.path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
