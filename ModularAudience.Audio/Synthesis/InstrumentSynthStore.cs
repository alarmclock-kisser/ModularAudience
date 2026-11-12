using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModularAudience.Audio.Synthesis;

public sealed class InstrumentSynthPreferences
{
    public int Version { get; set; } = 1;
    public int PrehearDelayMs { get; set; } = 200;
}

public sealed class InstrumentSynthPreset
{
    public string Name { get; set; } = "";
    public InstrumentSynthSettings Settings { get; set; } = new();
    public bool Loop { get; set; }
    // Store stable names in addition to indices so catalog reordering cannot change a saved sound.
    public string VoiceType { get; set; } = "";
    public string DrumKitType { get; set; } = "";
}

/// <summary>One shared preference file and one versioned, family-checked preset document per instrument family.</summary>
public sealed class InstrumentSynthStore
{
    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ModularAudience", "SampleSynthesizer");
    public static InstrumentSynthStore Shared { get; } = new();
    public string DirectoryPath { get; }
    public string PreferencesPath => Path.Combine(DirectoryPath, "settings.json");
    public string PresetsPath(SynthFamily family)
    {
        if (!Enum.IsDefined(family)) throw new ArgumentException("Unknown instrument family.");
        return Path.Combine(DirectoryPath, "Presets", family + ".json");
    }
    private readonly object gate = new();
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    public event Action<int>? PrehearDelayChanged;
    public event Action<SynthFamily>? PresetsChanged;
    public InstrumentSynthStore(string? directory = null) => DirectoryPath = directory ?? DefaultDirectory;

    public int LoadPrehearDelay()
    {
        lock (gate)
        {
            if (!File.Exists(PreferencesPath)) return 200;
            var value = JsonSerializer.Deserialize<InstrumentSynthPreferences>(File.ReadAllText(PreferencesPath), Json)
                ?? throw new InvalidDataException("Empty synthesizer settings.");
            if (value.Version != 1 || value.PrehearDelayMs is < 0 or > 10000)
                throw new InvalidDataException("Invalid synthesizer settings version or prehear delay.");
            return value.PrehearDelayMs;
        }
    }
    public void SavePrehearDelay(int milliseconds)
    {
        if (milliseconds is < 0 or > 10000) throw new ArgumentOutOfRangeException(nameof(milliseconds));
        lock (gate) WriteAtomic(PreferencesPath, new InstrumentSynthPreferences { PrehearDelayMs = milliseconds });
        PrehearDelayChanged?.Invoke(milliseconds);
    }

    private sealed class PresetDocument
    {
        public int Version { get; set; } = 1;
        public string Schema { get; set; } = "";
        public SynthFamily Family { get; set; }
        public List<InstrumentSynthPreset> Presets { get; set; } = [];
    }
    private static string Schema(SynthFamily family) => "ModularAudience.SampleSynthesizer." + family + ".v1";
    private PresetDocument ReadDocument(SynthFamily family)
    {
        string path = PresetsPath(family);
        if (!File.Exists(path)) return new() { Family = family, Schema = Schema(family) };
        var document = JsonSerializer.Deserialize<PresetDocument>(File.ReadAllText(path), Json)
            ?? throw new InvalidDataException("Empty instrument preset file.");
        if (document.Version != 1 || document.Family != family || document.Schema != Schema(family) || document.Presets == null)
            throw new InvalidDataException("This preset file belongs to a different instrument family or unsupported version.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var preset in document.Presets)
        {
            if (preset == null || preset.Settings == null || preset.Settings.Family != family)
                throw new InvalidDataException("A preset belongs to a different instrument family.");
            ValidateName(preset.Name);
            if (!names.Add(preset.Name)) throw new InvalidDataException("Duplicate preset name.");
            RestoreTypeIndices(preset);
            preset.Settings.Validate();
        }
        return document;
    }
    private static void RestoreTypeIndices(InstrumentSynthPreset preset)
    {
        var s = preset.Settings;
        var voices = InstrumentSynthCatalog.Voices(s.Family);
        int voice = string.IsNullOrWhiteSpace(preset.VoiceType)
            ? s.VoiceIndex
            : voices.ToList().FindIndex(v => v.Name == preset.VoiceType);
        int kit = string.IsNullOrWhiteSpace(preset.DrumKitType)
            ? s.KitIndex
            : InstrumentSynthCatalog.DrumKits.ToList().FindIndex(k => k.Name == preset.DrumKitType);
        if (voice < 0 || voice >= voices.Count || kit < 0 || kit >= InstrumentSynthCatalog.DrumKits.Count)
            throw new InvalidDataException("A preset references an unknown instrument or drum-set type.");
        s.VoiceIndex = voice; s.KitIndex = kit;
    }
    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100 || name.Any(char.IsControl))
            throw new ArgumentException("Enter a preset name of 1–100 characters without control characters.");
    }
    public string[] Names(SynthFamily family)
    { lock (gate) return ReadDocument(family).Presets.Select(p => p.Name).Order(StringComparer.OrdinalIgnoreCase).ToArray(); }
    public InstrumentSynthPreset Load(SynthFamily family, string name)
    {
        lock (gate) return ReadDocument(family).Presets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("Select an existing preset.");
    }
    public void Save(SynthFamily family, string name, InstrumentSynthSettings settings, bool loop)
    {
        ValidateName(name); name = name.Trim(); settings.Validate();
        if (settings.Family != family) throw new ArgumentException("Presets cannot be shared between different instrument families.");
        lock (gate)
        {
            var document = ReadDocument(family);
            document.Presets.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            document.Presets.Add(new()
            {
                Name = name, Settings = settings.Clone(), Loop = loop,
                VoiceType = InstrumentSynthCatalog.Voices(family)[settings.VoiceIndex].Name,
                DrumKitType = InstrumentSynthCatalog.DrumKits[settings.KitIndex].Name
            });
            WriteAtomic(PresetsPath(family), document);
        }
        PresetsChanged?.Invoke(family);
    }
    public void Rename(SynthFamily family, string oldName, string newName)
    {
        ValidateName(newName); newName = newName.Trim();
        lock (gate)
        {
            var document = ReadDocument(family);
            var entry = document.Presets.FirstOrDefault(p => string.Equals(p.Name, oldName, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException("Select an existing preset.");
            if (document.Presets.Any(p => !ReferenceEquals(p, entry) && string.Equals(p.Name, newName, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Another preset already has this name.");
            entry.Name = newName; WriteAtomic(PresetsPath(family), document);
        }
        PresetsChanged?.Invoke(family);
    }
    public void Delete(SynthFamily family, string name)
    {
        lock (gate)
        {
            var document = ReadDocument(family);
            if (document.Presets.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) == 0)
                throw new ArgumentException("Select an existing preset.");
            WriteAtomic(PresetsPath(family), document);
        }
        PresetsChanged?.Invoke(family);
    }
    private static void WriteAtomic<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(value, Json)); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
