using System.IO;
using System.Text.Json;

namespace ModularAudience.Audio.Vocal;

public sealed class VocalVoiceProfileStore
{
    public string DirectoryPath { get; }

    public VocalVoiceProfileStore(string directoryPath)
    {
        DirectoryPath = directoryPath;
        Directory.CreateDirectory(DirectoryPath);
    }

    public static VocalVoiceProfileStore Default() =>
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ModularAudience", "VocalSynth", "Profiles"));

    public IReadOnlyList<VocalVoiceProfile> LoadAll()
    {
        var result = new List<VocalVoiceProfile>();
        if (!Directory.Exists(DirectoryPath)) return result;
        foreach (var file in Directory.EnumerateFiles(DirectoryPath, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var profile = JsonSerializer.Deserialize<VocalVoiceProfile>(json);
                if (profile is not null) result.Add(profile);
            }
            catch { /* skip corrupt */ }
        }
        return result;
    }

    public string FilePathFor(string name) =>
        Path.Combine(DirectoryPath, Sanitize(name) + ".json");

    public void Save(VocalVoiceProfile profile)
    {
        Directory.CreateDirectory(DirectoryPath);
        var path = FilePathFor(profile.Name);
        var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    public bool Delete(string name)
    {
        var path = FilePathFor(name);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    public bool Rename(string oldName, string newName)
    {
        var oldPath = FilePathFor(oldName);
        if (!File.Exists(oldPath)) return false;
        var profile = JsonSerializer.Deserialize<VocalVoiceProfile>(File.ReadAllText(oldPath));
        if (profile is null) return false;
        profile.Name = newName;
        Save(profile);
        if (!string.Equals(oldPath, FilePathFor(newName), StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(oldPath); } catch { }
        }
        return true;
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(cleaned)) cleaned = "profile";
        return cleaned;
    }
}