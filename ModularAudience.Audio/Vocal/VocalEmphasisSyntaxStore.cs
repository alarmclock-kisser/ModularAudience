using System.IO;
using System.Text.Json;

namespace ModularAudience.Audio.Vocal;

public sealed class VocalEmphasisSyntaxStore
{
    public string FilePath { get; }

    public VocalEmphasisSyntaxStore(string filePath)
    {
        FilePath = filePath;
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    }

    public static VocalEmphasisSyntaxStore Default() =>
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ModularAudience", "VocalSynth", "EmphasisSyntax.json"));

    public VocalEmphasisSyntax Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return VocalEmphasisSyntax.Default();
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<VocalEmphasisSyntax>(json) ?? VocalEmphasisSyntax.Default();
        }
        catch
        {
            return VocalEmphasisSyntax.Default();
        }
    }

    public void Save(VocalEmphasisSyntax syntax)
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(syntax, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }
}