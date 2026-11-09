using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModularAudience.Audio.Effects.AutoTune;

namespace ModularAudience.Forms.Effects;

internal sealed record AutoTunePreset(string Name, AutoTuneSettings Parameters)
{
    public override string ToString() => this.Name;
}

internal sealed class AutoTunePresetStore
{
    private static readonly JsonSerializerOptions JsonOptions = CreateOptions();
    private readonly string mutexName;
    internal string FilePath { get; }
    internal static string DefaultPath
    {
        get
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify);
            if (!Path.IsPathFullyQualified(appData)) throw new DirectoryNotFoundException("AppData is unavailable.");
            return Path.Combine(appData, "ModularAudience", "Effects", "AutoTunePresets.json");
        }
    }

    internal AutoTunePresetStore(string? path = null)
    {
        this.FilePath = Path.GetFullPath(path ?? DefaultPath);
        this.mutexName = @"Local\ModularAudience.AutoTunePresets." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(this.FilePath.ToUpperInvariant())));
    }

    internal IReadOnlyList<AutoTunePreset> List() => this.WithLock(() => (IReadOnlyList<AutoTunePreset>)this.Read().ToArray());

    internal AutoTunePreset Load(string name) => this.WithLock(() => this.Read().FirstOrDefault(preset =>
        preset.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? throw new KeyNotFoundException("The selected preset no longer exists."));

    internal AutoTunePreset Save(string? requestedName, AutoTuneSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        return this.WithLock(() =>
        {
            List<AutoTunePreset> presets = this.Read();
            string name = requestedName?.Trim() ?? string.Empty;
            if (name.Length == 0)
            {
                var used = presets.Select(preset => preset.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                int number = 1;
                while (used.Contains(name = $"Preset#{number}")) number = checked(number + 1);
            }
            ValidateName(name);
            int existing = presets.FindIndex(preset => preset.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            var saved = new AutoTunePreset(existing >= 0 ? presets[existing].Name : name, settings with { });
            if (existing >= 0) presets[existing] = saved;
            else
            {
                if (presets.Count >= 4096) throw new InvalidDataException("The preset limit (4096) has been reached.");
                presets.Add(saved);
            }
            this.Write(presets);
            return saved;
        });
    }

    internal bool Delete(string name) => this.WithLock(() =>
    {
        List<AutoTunePreset> presets = this.Read();
        bool removed = presets.RemoveAll(preset => preset.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) > 0;
        if (removed) this.Write(presets);
        return removed;
    });

    private List<AutoTunePreset> Read()
    {
        if (!File.Exists(this.FilePath)) return [];
        if (new FileInfo(this.FilePath).Length > 4 * 1024 * 1024) throw new InvalidDataException("Auto Tune presets exceed 4 MiB.");
        List<AutoTunePreset> presets = JsonSerializer.Deserialize<List<AutoTunePreset>>(File.ReadAllText(this.FilePath), JsonOptions)
            ?? throw new InvalidDataException("The preset file must contain a JSON array.");
        if (presets.Count > 4096) throw new InvalidDataException("The preset file contains too many entries.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (AutoTunePreset? preset in presets)
        {
            if (preset is null || preset.Parameters is null) throw new InvalidDataException("The preset file contains an empty preset.");
            ValidateName(preset.Name);
            if (!names.Add(preset.Name)) throw new InvalidDataException("The preset file contains duplicate names.");
            preset.Parameters.Validate();
        }
        return presets;
    }

    private void Write(List<AutoTunePreset> presets)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(this.FilePath)!);
        string temporary = this.FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, presets, JsonOptions);
                if (stream.Length > 4 * 1024 * 1024) throw new InvalidDataException("Auto Tune presets exceed 4 MiB.");
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(this.FilePath)) File.Replace(temporary, this.FilePath, null);
            else File.Move(temporary, this.FilePath);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private T WithLock<T>(Func<T> action)
    {
        using var mutex = new Mutex(false, this.mutexName);
        bool held = false;
        try
        {
            try { held = mutex.WaitOne(TimeSpan.FromSeconds(5)); }
            catch (AbandonedMutexException) { held = true; }
            if (!held) throw new IOException("Another instance is busy saving Auto Tune presets.");
            return action();
        }
        finally { if (held) mutex.ReleaseMutex(); }
    }

    private static void ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120 || name.Any(char.IsControl))
            throw new InvalidDataException("Preset names must contain 1–120 printable characters.");
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }
}
