using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModularAudience.Audio.Effects.Curves;

namespace ModularAudience.Forms.Effects;

internal sealed record CurvePreset(string Name, EffectCurve Curve,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? BuiltInId = null)
{
    [JsonIgnore] public bool IsBuiltIn => this.BuiltInId is not null;
    public override string ToString() => this.IsBuiltIn ? "[Default] " + this.Name : this.Name;
}

/// <summary>One shape library for every effect. Built-ins live only in code; only explicit Save writes user JSON.</summary>
internal sealed class CurvePresetStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter(allowIntegerValues: false) } };
    private readonly string mutexName;
    internal string FilePath { get; }
    internal CurvePresetStore(string? path = null)
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify);
        if (path is null && !Path.IsPathFullyQualified(appData)) throw new DirectoryNotFoundException("AppData is unavailable.");
        this.FilePath = Path.GetFullPath(path ?? Path.Combine(appData, "ModularAudience", "Effects", "CurvePresets.json"));
        this.mutexName = @"Local\ModularAudience.CurvePresets." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(this.FilePath.ToUpperInvariant())));
    }
    internal IReadOnlyList<CurvePreset> FactoryPresets() => EffectCurvePresets.All.Select(preset => new CurvePreset(preset.Name, preset.Curve.Copy(), preset.Id)).ToArray();
    internal IReadOnlyList<CurvePreset> List() => this.WithLock(() => (IReadOnlyList<CurvePreset>)this.Read());
    internal IReadOnlyList<CurvePreset> ListAvailable() => this.FactoryPresets().Concat(this.List()).ToArray();
    internal CurvePreset Load(string name) => this.WithLock(() => this.Read().FirstOrDefault(preset => preset.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
        ?? throw new KeyNotFoundException("The selected curve preset no longer exists."));

    internal CurvePreset Save(string? requestedName, EffectCurve curve)
    {
        ArgumentNullException.ThrowIfNull(curve); curve.Compile();
        return this.WithLock(() =>
        {
            List<CurvePreset> presets = this.Read(); string name = requestedName?.Trim() ?? string.Empty;
            if (name.Length == 0)
            {
                var used = presets.Select(preset => preset.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                int number = 1; while (used.Contains(name = $"Curve#{number}")) number = checked(number + 1);
            }
            ValidateName(name);
            int index = presets.FindIndex(preset => preset.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            var saved = new CurvePreset(index >= 0 ? presets[index].Name : name, curve.Copy());
            if (index >= 0) presets[index] = saved;
            else { if (presets.Count >= 4096) throw new InvalidDataException("The curve-preset limit (4096) has been reached."); presets.Add(saved); }
            this.Write(presets); return saved;
        });
    }
    internal CurvePreset Rename(string oldName, string requestedName) => this.WithLock(() =>
    {
        string name = requestedName.Trim(); ValidateName(name); List<CurvePreset> presets = this.Read();
        int index = presets.FindIndex(preset => preset.Name.Equals(oldName, StringComparison.OrdinalIgnoreCase));
        if (index < 0) throw new KeyNotFoundException("The selected user curve no longer exists.");
        if (presets.Where((_, position) => position != index).Any(preset => preset.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("A user curve already has that name. Rename never overwrites another curve.");
        CurvePreset renamed = presets[index] with { Name = name }; presets[index] = renamed; this.Write(presets); return renamed;
    });
    internal bool Delete(string name) => this.WithLock(() =>
    {
        List<CurvePreset> presets = this.Read(); bool removed = presets.RemoveAll(preset => preset.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) > 0;
        if (removed) this.Write(presets); return removed;
    });
    private List<CurvePreset> Read()
    {
        if (!File.Exists(this.FilePath)) return [];
        if (new FileInfo(this.FilePath).Length > 4 * 1024 * 1024) throw new InvalidDataException("Curve presets exceed 4 MiB.");
        var presets = JsonSerializer.Deserialize<List<CurvePreset>>(File.ReadAllText(this.FilePath), JsonOptions)
            ?? throw new InvalidDataException("The curve-preset file must contain a JSON array.");
        if (presets.Count > 4096) throw new InvalidDataException("Too many curve presets.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (CurvePreset? preset in presets)
        {
            if (preset is null || preset.Curve is null || preset.IsBuiltIn) throw new InvalidDataException("The curve file may contain only user-defined curves.");
            ValidateName(preset.Name); preset.Curve.Compile();
            if (!names.Add(preset.Name)) throw new InvalidDataException("Duplicate curve-preset names.");
        }
        return presets;
    }
    private void Write(List<CurvePreset> presets)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(this.FilePath)!);
        string temporary = this.FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, presets, JsonOptions);
                if (stream.Length > 4 * 1024 * 1024) throw new InvalidDataException("Curve presets exceed 4 MiB.");
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(this.FilePath)) File.Replace(temporary, this.FilePath, null); else File.Move(temporary, this.FilePath);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private T WithLock<T>(Func<T> action)
    {
        using var mutex = new Mutex(false, this.mutexName); bool held = false;
        try
        {
            try { held = mutex.WaitOne(TimeSpan.FromSeconds(5)); } catch (AbandonedMutexException) { held = true; }
            if (!held) throw new IOException("Another instance is busy saving curve presets.");
            return action();
        }
        finally { if (held) mutex.ReleaseMutex(); }
    }
    private static void ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120 || name.Any(char.IsControl)) throw new InvalidDataException("Curve names must contain 1–120 printable characters.");
    }
}
