using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModularAudience.Audio.Effects;

namespace ModularAudience.Forms.Effects;

internal sealed record EffectPreset(string Name, AudioEffectSettings Parameters,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? BuiltInId = null)
{
    [JsonIgnore] public bool IsBuiltIn => this.BuiltInId is not null;
    public override string ToString() => this.IsBuiltIn ? "[Default] " + this.Name : this.Name;
}

/// <summary>Isolated per-effect stores, atomic writes, cross-process locking and non-destructive factory seeding.</summary>
internal sealed class EffectPresetStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter(allowIntegerValues: false) } };
    private readonly AudioEffectDefinition definition;
    private readonly string mutexName;
    internal string FilePath { get; }
    internal AudioEffectKind Effect => this.definition.Kind;

    internal EffectPresetStore(AudioEffectKind effect, string? path = null)
    {
        this.definition = AudioEffectCatalog.Get(effect);
        if (!this.definition.OpensOptions || effect == AudioEffectKind.AutoTune) throw new ArgumentException("This effect has no generic preset store.");
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify);
        if (path is null && !Path.IsPathFullyQualified(appData)) throw new DirectoryNotFoundException("AppData is unavailable.");
        this.FilePath = Path.GetFullPath(path ?? Path.Combine(appData, "ModularAudience", "Effects", effect + "Presets.json"));
        this.mutexName = @"Local\ModularAudience.EffectPresets." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(this.FilePath.ToUpperInvariant())));
    }

    internal IReadOnlyList<EffectPreset> FactoryPresets() => this.definition.BuiltInPresets
        .Select(preset => new EffectPreset(preset.Name, preset.Parameters.Copy(), preset.Id)).ToArray();

    internal IReadOnlyList<EffectPreset> List() => this.WithLock(() => (IReadOnlyList<EffectPreset>)this.Read().Where(preset => !preset.IsBuiltIn).ToArray());

    internal IReadOnlyList<EffectPreset> ListAvailable() => this.WithLock(() =>
    {
        List<EffectPreset> presets = this.Read(); bool changed = false;
        foreach (EffectPreset preset in this.FactoryPresets())
            if (!presets.Any(existing => string.Equals(existing.BuiltInId, preset.BuiltInId, StringComparison.OrdinalIgnoreCase)))
            { presets.Add(preset); changed = true; }
        if (changed) this.Write(presets);
        var known = this.definition.BuiltInPresets.Select(preset => preset.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return (IReadOnlyList<EffectPreset>)this.definition.BuiltInPresets.Select(definition => presets.Single(preset =>
                string.Equals(preset.BuiltInId, definition.Id, StringComparison.OrdinalIgnoreCase)))
            .Concat(presets.Where(preset => preset.IsBuiltIn && !known.Contains(preset.BuiltInId!)))
            .Concat(presets.Where(preset => !preset.IsBuiltIn)).ToArray();
    });

    internal EffectPreset Load(string name) => this.WithLock(() => this.Read().FirstOrDefault(preset =>
        !preset.IsBuiltIn && preset.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? throw new KeyNotFoundException("The selected preset no longer exists."));

    internal EffectPreset Save(string? requestedName, AudioEffectSettings settings)
    {
        this.ValidateSettings(settings);
        return this.WithLock(() =>
        {
            List<EffectPreset> presets = this.Read(); string name = requestedName?.Trim() ?? string.Empty;
            if (name.Length == 0)
            {
                var used = presets.Select(preset => preset.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                int number = 1; while (used.Contains(name = $"Preset#{number}")) number = checked(number + 1);
            }
            ValidateName(name);
            int existing = presets.FindIndex(preset => !preset.IsBuiltIn && preset.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            var saved = new EffectPreset(existing >= 0 ? presets[existing].Name : name, settings.Copy());
            if (existing >= 0) presets[existing] = saved;
            else
            {
                if (presets.Count(preset => !preset.IsBuiltIn) >= 4096) throw new InvalidDataException("The user-preset limit (4096) has been reached.");
                presets.Add(saved);
            }
            this.Write(presets); return saved;
        });
    }

    internal EffectPreset Rename(string oldName, string requestedName) => this.WithLock(() =>
    {
        string name = requestedName.Trim(); ValidateName(name);
        List<EffectPreset> presets = this.Read();
        int index = presets.FindIndex(preset => !preset.IsBuiltIn && preset.Name.Equals(oldName, StringComparison.OrdinalIgnoreCase));
        if (index < 0) throw new KeyNotFoundException("The selected user preset no longer exists.");
        if (presets.Where((_, position) => position != index).Any(preset => !preset.IsBuiltIn && preset.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("A user preset already has that name. Rename never overwrites another preset.");
        EffectPreset renamed = presets[index] with { Name = name }; presets[index] = renamed;
        this.Write(presets); return renamed;
    });

    internal bool Delete(string name) => this.WithLock(() =>
    {
        List<EffectPreset> presets = this.Read();
        bool removed = presets.RemoveAll(preset => !preset.IsBuiltIn && preset.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) > 0;
        if (removed) this.Write(presets); return removed;
    });

    private void ValidateSettings(AudioEffectSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Effect != this.Effect) throw new InvalidDataException("This preset belongs to another effect.");
        settings.Validate();
    }

    private List<EffectPreset> Read()
    {
        if (!File.Exists(this.FilePath)) return [];
        if (new FileInfo(this.FilePath).Length > 4 * 1024 * 1024) throw new InvalidDataException("Effect presets exceed 4 MiB.");
        List<EffectPreset> presets = JsonSerializer.Deserialize<List<EffectPreset>>(File.ReadAllText(this.FilePath), JsonOptions)
            ?? throw new InvalidDataException("The preset file must contain a JSON array.");
        if (presets.Count > 8192 || presets.Count(preset => preset is not null && !preset.IsBuiltIn) > 4096)
            throw new InvalidDataException("The preset file contains too many entries.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (EffectPreset? preset in presets)
        {
            if (preset is null || preset.Parameters is null) throw new InvalidDataException("The preset file contains an empty preset.");
            ValidateName(preset.Name); this.ValidateSettings(preset.Parameters);
            if (preset.IsBuiltIn)
            {
                if (string.IsNullOrWhiteSpace(preset.BuiltInId) || preset.BuiltInId.Length > 64 || preset.BuiltInId.Any(char.IsControl) || !ids.Add(preset.BuiltInId))
                    throw new InvalidDataException("Invalid or duplicate default-preset identifiers.");
            }
            else if (!names.Add(preset.Name)) throw new InvalidDataException("Duplicate user-preset names.");
        }
        return presets;
    }

    private void Write(List<EffectPreset> presets)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(this.FilePath)!);
        string temporary = this.FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, presets, JsonOptions);
                if (stream.Length > 4 * 1024 * 1024) throw new InvalidDataException("Effect presets exceed 4 MiB.");
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(this.FilePath)) File.Replace(temporary, this.FilePath, null);
            else File.Move(temporary, this.FilePath);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private T WithLock<T>(Func<T> action)
    {
        using var mutex = new Mutex(false, this.mutexName); bool held = false;
        try
        {
            try { held = mutex.WaitOne(TimeSpan.FromSeconds(5)); } catch (AbandonedMutexException) { held = true; }
            if (!held) throw new IOException("Another instance is busy saving effect presets.");
            return action();
        }
        finally { if (held) mutex.ReleaseMutex(); }
    }

    private static void ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120 || name.Any(char.IsControl))
            throw new InvalidDataException("Preset names must contain 1–120 printable characters.");
    }
}
