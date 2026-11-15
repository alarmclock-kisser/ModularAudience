using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ModularAudience.Audio.Effects;
using ModularAudience.Audio.Effects.AutoTune;
using ModularAudience.Audio.Effects.Curves;
using ModularAudience.Audio.Processing;
using ModularAudience.Audio.Synthesis;
using ModularAudience.Audio.Vocal;
using ModularAudience.CoOp;
using ModularAudience.CoOp.Protocol;
using ModularAudience.Forms.Effects;
using ModularAudience.Forms.Quantum;
using ModularAudience.Forms.Theming;
using ModularAudience.Quantum.Models;
using ModularAudience.Quantum.Serialization;

namespace ModularAudience.Forms;

internal sealed record CoOpPresetSyncResult(int ReceivedCount, int CreatedCount, int DuplicatesSkipped);

internal sealed class CoOpPresetSynchronizer
{
    private const string AutoTuneStoreId = "effects:AutoTune";
    private const string CurveStoreId = "effects:Curves";
    private const string InstrumentStorePrefix = "sample-synthesizer:";
    private const string SampleModellerStoreId = "sample-modeller";
    private const string WaveformStoreId = "waveforms";
    private const string VocalProfileStoreId = "vocal-profiles";
    private const string ThemeStoreId = "themes";
    private const string QuantumExperimentStoreId = "quantum-experiments";

    private static readonly JsonSerializerOptions PresetJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions EnumPresetJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    private static readonly JsonSerializerOptions InstrumentPresetJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly JsonSerializerOptions QuantumPresetJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    private readonly string appDataRoot;

    internal CoOpPresetSynchronizer(string? appDataRoot = null)
    {
        string root = appDataRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ModularAudience");
        if (!Path.IsPathFullyQualified(root))
        {
            throw new DirectoryNotFoundException("The ModularAudience AppData directory is unavailable.");
        }

        this.appDataRoot = Path.GetFullPath(root);
    }

    internal IReadOnlyList<CoOpPresetSyncItem> Capture()
    {
        var presets = new List<CoOpPresetSyncItem>();
        var fingerprints = new HashSet<string>(StringComparer.Ordinal);

        this.CaptureEffectPresets(presets, fingerprints);
        this.CaptureInstrumentPresets(presets, fingerprints);
        this.CaptureSampleModellerPresets(presets, fingerprints);
        this.CaptureQuantumExperiments(presets, fingerprints);

        foreach (NamedPreset<WaveformSettings> preset in this.ReadWaveformPresets())
        {
            AddPreset(presets, fingerprints, WaveformStoreId, preset.Preset, PresetJsonOptions);
        }

        foreach (NamedPreset<VocalVoiceProfile> preset in this.ReadVocalProfiles())
        {
            AddPreset(presets, fingerprints, VocalProfileStoreId, preset.Preset, PresetJsonOptions);
        }

        foreach (NamedPreset<ColorTheme> preset in this.ReadThemes())
        {
            AddPreset(presets, fingerprints, ThemeStoreId, preset.Preset, ThemeFiles.JsonOptions);
        }

        ValidateTransfer(presets);
        return presets;
    }

    internal CoOpPresetSyncResult Import(string sourceDisplayName, IReadOnlyList<CoOpPresetSyncItem> presets)
    {
        ArgumentNullException.ThrowIfNull(presets);
        ValidateTransfer(presets);
        string hostName = NormalizeHostName(sourceDisplayName);
        int created = 0;
        int duplicates = 0;

        foreach (CoOpPresetSyncItem preset in presets)
        {
            if (this.ImportPreset(preset, hostName))
            {
                created++;
            }
            else
            {
                duplicates++;
            }
        }

        return new CoOpPresetSyncResult(presets.Count, created, duplicates);
    }

    private void CaptureEffectPresets(List<CoOpPresetSyncItem> presets, HashSet<string> fingerprints)
    {
        string directory = Path.Combine(this.appDataRoot, "Effects");
        var autoTuneStore = new AutoTunePresetStore(Path.Combine(directory, "AutoTunePresets.json"));
        foreach (AutoTunePreset preset in autoTuneStore.List())
        {
            AddPreset(presets, fingerprints, AutoTuneStoreId, preset, EnumPresetJsonOptions);
        }

        var curveStore = new CurvePresetStore(Path.Combine(directory, "CurvePresets.json"));
        foreach (CurvePreset preset in curveStore.List())
        {
            AddPreset(presets, fingerprints, CurveStoreId, preset, EnumPresetJsonOptions);
        }

        foreach (AudioEffectDefinition definition in AudioEffectCatalog.All.Where(
            effect => effect.OpensOptions && effect.Kind != AudioEffectKind.AutoTune))
        {
            var store = new EffectPresetStore(
                definition.Kind,
                Path.Combine(directory, definition.Kind + "Presets.json"));
            foreach (EffectPreset preset in store.List())
            {
                AddPreset(
                    presets,
                    fingerprints,
                    "effects:" + definition.Kind,
                    preset,
                    EnumPresetJsonOptions);
            }
        }
    }

    private void CaptureInstrumentPresets(List<CoOpPresetSyncItem> presets, HashSet<string> fingerprints)
    {
        var store = new InstrumentSynthStore(Path.Combine(this.appDataRoot, "SampleSynthesizer"));
        foreach (SynthFamily family in Enum.GetValues<SynthFamily>())
        {
            foreach (string name in store.Names(family))
            {
                AddPreset(
                    presets,
                    fingerprints,
                    InstrumentStorePrefix + family,
                    store.Load(family, name),
                    InstrumentPresetJsonOptions);
            }
        }
    }

    private void CaptureSampleModellerPresets(List<CoOpPresetSyncItem> presets, HashSet<string> fingerprints)
    {
        string directory = Path.Combine(this.appDataRoot, "SampleModellerPresets");
        foreach (string file in EnumeratePresetFiles(directory))
        {
            SampleModellerSettings preset = Deserialize<SampleModellerSettings>(
                ReadPresetFile(file),
                PresetJsonOptions);
            preset.Validate();
            preset.Name = Path.GetFileNameWithoutExtension(file);
            AddPreset(presets, fingerprints, SampleModellerStoreId, preset, PresetJsonOptions);
        }
    }

    private void CaptureQuantumExperiments(List<CoOpPresetSyncItem> presets, HashSet<string> fingerprints)
    {
        var store = new QuantumStudioStore(Path.Combine(this.appDataRoot, "QuantumBeatEngine"));
        foreach (string name in store.ExperimentNames())
        {
            QuantumStudioWorkspace workspace = store.LoadExperiment(name) with { LoadedExperimentName = null };
            AddPreset(
                presets,
                fingerprints,
                QuantumExperimentStoreId,
                new QuantumPresetPayload(name, workspace),
                QuantumPresetJsonOptions);
        }
    }

    private bool ImportPreset(CoOpPresetSyncItem preset, string hostName)
    {
        ArgumentNullException.ThrowIfNull(preset);
        if (string.IsNullOrWhiteSpace(preset.StoreId) || string.IsNullOrWhiteSpace(preset.Json))
        {
            throw new InvalidDataException("A synchronized preset is missing its store or JSON content.");
        }

        if (string.Equals(preset.StoreId, AutoTuneStoreId, StringComparison.OrdinalIgnoreCase))
        {
            return this.ImportAutoTune(preset.Json, hostName);
        }

        if (string.Equals(preset.StoreId, CurveStoreId, StringComparison.OrdinalIgnoreCase))
        {
            return this.ImportCurve(preset.Json, hostName);
        }

        if (string.Equals(preset.StoreId, SampleModellerStoreId, StringComparison.OrdinalIgnoreCase))
        {
            return this.ImportSampleModeller(preset.Json, hostName);
        }

        if (string.Equals(preset.StoreId, WaveformStoreId, StringComparison.OrdinalIgnoreCase))
        {
            return this.ImportWaveform(preset.Json, hostName);
        }

        if (string.Equals(preset.StoreId, VocalProfileStoreId, StringComparison.OrdinalIgnoreCase))
        {
            return this.ImportVocalProfile(preset.Json, hostName);
        }

        if (string.Equals(preset.StoreId, ThemeStoreId, StringComparison.OrdinalIgnoreCase))
        {
            return this.ImportTheme(preset.Json, hostName);
        }

        if (string.Equals(preset.StoreId, QuantumExperimentStoreId, StringComparison.OrdinalIgnoreCase))
        {
            return this.ImportQuantumExperiment(preset.Json, hostName);
        }

        if (preset.StoreId.StartsWith("effects:", StringComparison.OrdinalIgnoreCase))
        {
            return this.ImportEffect(preset.StoreId, preset.Json, hostName);
        }

        if (preset.StoreId.StartsWith(InstrumentStorePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return this.ImportInstrument(preset.StoreId, preset.Json, hostName);
        }

        throw new InvalidDataException($"Unsupported preset store '{preset.StoreId}'.");
    }

    private bool ImportAutoTune(string json, string hostName)
    {
        AutoTunePreset incoming = Deserialize<AutoTunePreset>(json, EnumPresetJsonOptions);
        if (incoming.IsBuiltIn || incoming.Parameters is null)
        {
            throw new InvalidDataException("Only user-created Auto Tune presets can be synchronized.");
        }

        incoming.Parameters.Validate();
        var store = new AutoTunePresetStore(Path.Combine(this.appDataRoot, "Effects", "AutoTunePresets.json"));
        IReadOnlyList<AutoTunePreset> existing = store.List();
        string fingerprint = Fingerprint(incoming, EnumPresetJsonOptions);
        if (existing.Any(preset => Fingerprint(preset, EnumPresetJsonOptions) == fingerprint))
        {
            return false;
        }

        string name = CreateAvailableName(incoming.Name, hostName, existing.Select(preset => preset.Name), 120, false);
        store.Save(name, incoming.Parameters);
        return true;
    }

    private bool ImportCurve(string json, string hostName)
    {
        CurvePreset incoming = Deserialize<CurvePreset>(json, EnumPresetJsonOptions);
        if (incoming.IsBuiltIn || incoming.Curve is null)
        {
            throw new InvalidDataException("Only user-created curve presets can be synchronized.");
        }

        incoming.Curve.Compile();
        var store = new CurvePresetStore(Path.Combine(this.appDataRoot, "Effects", "CurvePresets.json"));
        IReadOnlyList<CurvePreset> existing = store.List();
        string fingerprint = Fingerprint(incoming, EnumPresetJsonOptions);
        if (existing.Any(preset => Fingerprint(preset, EnumPresetJsonOptions) == fingerprint))
        {
            return false;
        }

        string name = CreateAvailableName(incoming.Name, hostName, existing.Select(preset => preset.Name), 120, false);
        store.Save(name, incoming.Curve);
        return true;
    }

    private bool ImportEffect(string storeId, string json, string hostName)
    {
        string namePart = storeId["effects:".Length..];
        if (!Enum.TryParse(namePart, ignoreCase: true, out AudioEffectKind effect) ||
            !Enum.IsDefined(effect) ||
            effect is AudioEffectKind.AutoTune or AudioEffectKind.ConvertToMono or AudioEffectKind.Reverse)
        {
            throw new InvalidDataException($"Unsupported effect preset store '{storeId}'.");
        }

        var store = new EffectPresetStore(
            effect,
            Path.Combine(this.appDataRoot, "Effects", effect + "Presets.json"));
        EffectPreset incoming = Deserialize<EffectPreset>(json, EnumPresetJsonOptions);
        if (incoming.IsBuiltIn || incoming.Parameters is null)
        {
            throw new InvalidDataException("Only user-created effect presets can be synchronized.");
        }

        incoming.Parameters.Validate();
        if (incoming.Parameters.Effect != effect)
        {
            throw new InvalidDataException("The preset does not match its effect store.");
        }

        IReadOnlyList<EffectPreset> existing = store.List();
        string fingerprint = Fingerprint(incoming, EnumPresetJsonOptions);
        if (existing.Any(preset => Fingerprint(preset, EnumPresetJsonOptions) == fingerprint))
        {
            return false;
        }

        string name = CreateAvailableName(incoming.Name, hostName, existing.Select(preset => preset.Name), 120, false);
        store.Save(name, incoming.Parameters);
        return true;
    }

    private bool ImportInstrument(string storeId, string json, string hostName)
    {
        string familyPart = storeId[InstrumentStorePrefix.Length..];
        if (!Enum.TryParse(familyPart, ignoreCase: true, out SynthFamily family) ||
            !Enum.IsDefined(family))
        {
            throw new InvalidDataException($"Unsupported instrument preset store '{storeId}'.");
        }

        InstrumentSynthPreset incoming = Deserialize<InstrumentSynthPreset>(json, InstrumentPresetJsonOptions);
        if (incoming.Settings is null || incoming.Settings.Family != family)
        {
            throw new InvalidDataException("The preset does not match its instrument family.");
        }

        RestoreInstrumentTypeIndices(incoming, family);
        incoming.Settings.Validate();

        var store = new InstrumentSynthStore(Path.Combine(this.appDataRoot, "SampleSynthesizer"));
        string[] names = store.Names(family);
        InstrumentSynthPreset[] existing = names.Select(name => store.Load(family, name)).ToArray();
        string fingerprint = Fingerprint(incoming, InstrumentPresetJsonOptions);
        if (existing.Any(preset => Fingerprint(preset, InstrumentPresetJsonOptions) == fingerprint))
        {
            return false;
        }

        string name = CreateAvailableName(incoming.Name, hostName, names, 100, false);
        store.Save(family, name, incoming.Settings, incoming.Loop);
        return true;
    }

    private bool ImportSampleModeller(string json, string hostName)
    {
        SampleModellerSettings incoming = Deserialize<SampleModellerSettings>(json, PresetJsonOptions);
        incoming.Validate();

        string directory = Path.Combine(this.appDataRoot, "SampleModellerPresets");
        NamedPreset<SampleModellerSettings>[] existing = this.ReadSampleModellerPresets(directory).ToArray();
        string fingerprint = Fingerprint(incoming, PresetJsonOptions);
        if (existing.Any(preset => Fingerprint(preset.Preset, PresetJsonOptions) == fingerprint))
        {
            return false;
        }

        string name = CreateAvailableName(
            incoming.Name,
            hostName,
            existing.Select(preset => preset.FileName),
            100,
            true);
        incoming.Name = name;
        string content = JsonSerializer.Serialize(incoming, PresetJsonOptions);
        WriteNewPreset(Path.Combine(directory, name + ".json"), content);
        return true;
    }

    private bool ImportWaveform(string json, string hostName)
    {
        WaveformSettings incoming = Deserialize<WaveformSettings>(json, PresetJsonOptions);
        incoming.Validate();
        WaveformModel.BuildCycle(incoming);

        string directory = Path.Combine(this.appDataRoot, "Waveforms");
        NamedPreset<WaveformSettings>[] existing = this.ReadWaveformPresets(directory).ToArray();
        string fingerprint = Fingerprint(incoming, PresetJsonOptions);
        if (existing.Any(preset => Fingerprint(preset.Preset, PresetJsonOptions) == fingerprint))
        {
            return false;
        }

        string name = CreateAvailableName(
            incoming.Name,
            hostName,
            existing.SelectMany(preset => new[] { preset.FileName, preset.Preset.Name }),
            100,
            true);
        incoming.Name = name;
        string content = JsonSerializer.Serialize(incoming, PresetJsonOptions);
        WriteNewPreset(Path.Combine(directory, name + ".json"), content);
        return true;
    }

    private bool ImportVocalProfile(string json, string hostName)
    {
        VocalVoiceProfile incoming = Deserialize<VocalVoiceProfile>(json, PresetJsonOptions);
        string directory = Path.Combine(this.appDataRoot, "VocalSynth", "Profiles");
        NamedPreset<VocalVoiceProfile>[] existing = this.ReadVocalProfiles(directory).ToArray();
        string fingerprint = Fingerprint(incoming, PresetJsonOptions);
        if (existing.Any(preset => Fingerprint(preset.Preset, PresetJsonOptions) == fingerprint))
        {
            return false;
        }

        string name = CreateAvailableName(
            incoming.Name,
            hostName,
            existing.SelectMany(preset => new[] { preset.FileName, preset.Preset.Name }),
            100,
            true);
        incoming.Name = name;
        string content = JsonSerializer.Serialize(incoming, PresetJsonOptions);
        WriteNewPreset(Path.Combine(directory, name + ".json"), content);
        return true;
    }

    private bool ImportTheme(string json, string hostName)
    {
        ColorTheme incoming = ThemeFiles.Deserialize(json);
        NamedPreset<ColorTheme>[] existing = this.ReadThemes().ToArray();
        string fingerprint = Fingerprint(incoming, ThemeFiles.JsonOptions);
        if (existing.Any(preset => Fingerprint(preset.Preset, ThemeFiles.JsonOptions) == fingerprint))
        {
            return false;
        }

        string name = CreateAvailableName(
            incoming.Name,
            hostName,
            existing.SelectMany(preset => new[] { preset.FileName, preset.Preset.Name })
                .Append("active-theme"),
            100,
            true);
        incoming.Name = name;
        string content = JsonSerializer.Serialize(incoming, ThemeFiles.JsonOptions);
        _ = ThemeFiles.Deserialize(content);
        WriteNewPreset(Path.Combine(this.appDataRoot, "Themes", name + ".json"), content);
        return true;
    }

    private bool ImportQuantumExperiment(string json, string hostName)
    {
        if (Encoding.UTF8.GetByteCount(json) > 1_048_576)
        {
            throw new InvalidDataException("A quantum experiment preset exceeds the synchronization size limit.");
        }

        QuantumPresetPayload incoming = Deserialize<QuantumPresetPayload>(json, QuantumPresetJsonOptions);
        if (string.IsNullOrWhiteSpace(incoming.Name) || incoming.Workspace is null)
        {
            throw new InvalidDataException("A quantum experiment preset is missing its name or workspace.");
        }

        QuantumStudioWorkspace workspace = incoming.Workspace with { LoadedExperimentName = null };
        _ = QuantumExperimentSerializer.Serialize(workspace.Experiment);

        var store = new QuantumStudioStore(Path.Combine(this.appDataRoot, "QuantumBeatEngine"));
        string[] names = store.ExperimentNames();
        string fingerprint = Fingerprint(workspace, QuantumPresetJsonOptions);
        foreach (string name in names)
        {
            QuantumStudioWorkspace existing = store.LoadExperiment(name) with { LoadedExperimentName = null };
            if (Fingerprint(existing, QuantumPresetJsonOptions) == fingerprint)
            {
                return false;
            }
        }

        string availableName = CreateAvailableName(incoming.Name, hostName, names, 80, true);
        store.SaveExperiment(availableName, workspace);
        return true;
    }

    private IReadOnlyList<NamedPreset<SampleModellerSettings>> ReadSampleModellerPresets(string directory)
    {
        var presets = new List<NamedPreset<SampleModellerSettings>>();
        foreach (string file in EnumeratePresetFiles(directory))
        {
            SampleModellerSettings preset = Deserialize<SampleModellerSettings>(
                ReadPresetFile(file),
                PresetJsonOptions);
            preset.Validate();
            string fileName = Path.GetFileNameWithoutExtension(file);
            preset.Name = fileName;
            presets.Add(new NamedPreset<SampleModellerSettings>(fileName, preset));
        }

        return presets;
    }

    private IReadOnlyList<NamedPreset<WaveformSettings>> ReadWaveformPresets(string? directory = null)
    {
        directory ??= Path.Combine(this.appDataRoot, "Waveforms");
        var presets = new List<NamedPreset<WaveformSettings>>();
        foreach (string file in EnumeratePresetFiles(directory))
        {
            WaveformSettings preset = Deserialize<WaveformSettings>(ReadPresetFile(file), PresetJsonOptions);
            preset.Validate();
            WaveformModel.BuildCycle(preset);
            presets.Add(new NamedPreset<WaveformSettings>(Path.GetFileNameWithoutExtension(file), preset));
        }

        return presets;
    }

    private IReadOnlyList<NamedPreset<VocalVoiceProfile>> ReadVocalProfiles(string? directory = null)
    {
        directory ??= Path.Combine(this.appDataRoot, "VocalSynth", "Profiles");
        var presets = new List<NamedPreset<VocalVoiceProfile>>();
        foreach (string file in EnumeratePresetFiles(directory))
        {
            VocalVoiceProfile preset = Deserialize<VocalVoiceProfile>(ReadPresetFile(file), PresetJsonOptions);
            presets.Add(new NamedPreset<VocalVoiceProfile>(Path.GetFileNameWithoutExtension(file), preset));
        }

        return presets;
    }

    private IReadOnlyList<NamedPreset<ColorTheme>> ReadThemes()
    {
        string directory = Path.Combine(this.appDataRoot, "Themes");
        var presets = new List<NamedPreset<ColorTheme>>();
        foreach (string file in EnumeratePresetFiles(directory))
        {
            if (string.Equals(Path.GetFileName(file), "active-theme.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            ColorTheme preset = ThemeFiles.Deserialize(ReadPresetFile(file));
            presets.Add(new NamedPreset<ColorTheme>(Path.GetFileNameWithoutExtension(file), preset));
        }

        return presets;
    }

    private static void RestoreInstrumentTypeIndices(InstrumentSynthPreset preset, SynthFamily family)
    {
        if (!string.IsNullOrWhiteSpace(preset.VoiceType))
        {
            int voiceIndex = InstrumentSynthCatalog.Voices(family)
                .ToList()
                .FindIndex(voice => string.Equals(voice.Name, preset.VoiceType, StringComparison.Ordinal));
            if (voiceIndex < 0)
            {
                throw new InvalidDataException("The preset references an unknown instrument type.");
            }

            preset.Settings.VoiceIndex = voiceIndex;
        }

        if (!string.IsNullOrWhiteSpace(preset.DrumKitType))
        {
            int kitIndex = InstrumentSynthCatalog.DrumKits
                .ToList()
                .FindIndex(kit => string.Equals(kit.Name, preset.DrumKitType, StringComparison.Ordinal));
            if (kitIndex < 0)
            {
                throw new InvalidDataException("The preset references an unknown drum-set type.");
            }

            preset.Settings.KitIndex = kitIndex;
        }
    }

    private static void AddPreset<T>(
        List<CoOpPresetSyncItem> presets,
        HashSet<string> fingerprints,
        string storeId,
        T preset,
        JsonSerializerOptions options)
    {
        string json = JsonSerializer.Serialize(preset, options);
        string fingerprint = storeId + ":" + Fingerprint(json);
        if (fingerprints.Add(fingerprint))
        {
            presets.Add(new CoOpPresetSyncItem(storeId, json));
        }
    }

    private static T Deserialize<T>(string json, JsonSerializerOptions options)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, options)
                ?? throw new InvalidDataException("The preset JSON is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("A synchronized preset contains invalid JSON.", exception);
        }
    }

    private static string Fingerprint<T>(T preset, JsonSerializerOptions options)
    {
        return Fingerprint(JsonSerializer.Serialize(preset, options));
    }

    private static string Fingerprint(string json)
    {
        JsonNode node = JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidDataException("A preset must be a JSON object.");
        JsonObject preset = (JsonObject)node;
        foreach (string property in preset.Select(pair => pair.Key)
            .Where(name => string.Equals(name, "Name", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "BuiltInId", StringComparison.OrdinalIgnoreCase))
            .ToArray())
        {
            preset.Remove(property);
        }

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteCanonicalJson(writer, preset);
        }

        return Convert.ToHexString(SHA256.HashData(buffer.ToArray()));
    }

    private static void WriteCanonicalJson(Utf8JsonWriter writer, JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            writer.WriteStartObject();
            foreach (KeyValuePair<string, JsonNode?> property in obj.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Key);
                WriteCanonicalJson(writer, property.Value);
            }

            writer.WriteEndObject();
            return;
        }

        if (node is JsonArray array)
        {
            writer.WriteStartArray();
            foreach (JsonNode? item in array)
            {
                WriteCanonicalJson(writer, item);
            }

            writer.WriteEndArray();
            return;
        }

        if (node is null)
        {
            writer.WriteNullValue();
            return;
        }

        node.WriteTo(writer, PresetJsonOptions);
    }

    private static string CreateAvailableName(
        string? requestedName,
        string sourceDisplayName,
        IEnumerable<string> existingNames,
        int maxLength,
        bool fileNameSafe)
    {
        string name = NormalizePresetName(requestedName, maxLength, fileNameSafe);
        var used = existingNames
            .Select(existing => NormalizePresetName(existing, maxLength, fileNameSafe))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(name))
        {
            return name;
        }

        string suffix = " (" + sourceDisplayName + ")";
        for (int duplicateNumber = 1; ; duplicateNumber++)
        {
            string numberedSuffix = duplicateNumber == 1 ? suffix : $"{suffix} ({duplicateNumber})";
            int baseLength = maxLength - numberedSuffix.Length;
            if (baseLength < 1)
            {
                throw new InvalidDataException("The co-op host name is too long to create a unique preset name.");
            }

            string baseName = name.Length > baseLength ? name[..baseLength].TrimEnd() : name;
            string candidate = baseName + numberedSuffix;
            if (!used.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private static string NormalizePresetName(string? name, int maxLength, bool fileNameSafe)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Any(char.IsControl))
        {
            throw new InvalidDataException("Preset names must contain printable characters.");
        }

        string result = name.Trim();
        if (fileNameSafe)
        {
            var invalidCharacters = Path.GetInvalidFileNameChars().ToHashSet();
            invalidCharacters.Add('/');
            invalidCharacters.Add('\\');
            result = new string(result.Select(character =>
                invalidCharacters.Contains(character) || char.IsControl(character) ? '_' : character).ToArray())
                .TrimEnd(' ', '.');
        }

        if (result.Length == 0)
        {
            throw new InvalidDataException("A preset name is empty after file-name validation.");
        }

        if (result.Length > maxLength)
        {
            result = result[..maxLength].TrimEnd(' ', '.');
        }

        if (result.Length == 0)
        {
            throw new InvalidDataException("A preset name is too long to synchronize safely.");
        }

        return result;
    }

    private static string NormalizeHostName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Any(char.IsControl))
        {
            throw new InvalidDataException("The co-op peer has an invalid display name.");
        }

        var invalidCharacters = Path.GetInvalidFileNameChars().ToHashSet();
        invalidCharacters.Add('/');
        invalidCharacters.Add('\\');
        string result = new(name.Trim().Select(character =>
            invalidCharacters.Contains(character) || char.IsControl(character) ? '_' : character).ToArray());
        result = result.Trim().TrimEnd(' ', '.');
        if (result.Length == 0)
        {
            throw new InvalidDataException("The co-op peer display name cannot be used in a preset name.");
        }

        return result.Length > 48 ? result[..48] : result;
    }

    private static IEnumerable<string> EnumeratePresetFiles(string directory)
    {
        return Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            : [];
    }

    private static string ReadPresetFile(string file)
    {
        if (new FileInfo(file).Length > CoOpDefaults.MaxPresetSyncBytes)
        {
            throw new InvalidDataException($"Preset file '{Path.GetFileName(file)}' exceeds the synchronization size limit.");
        }

        return File.ReadAllText(file);
    }

    private static void ValidateTransfer(IReadOnlyCollection<CoOpPresetSyncItem> presets)
    {
        if (presets.Count > CoOpDefaults.MaxPresetSyncEntries)
        {
            throw new InvalidDataException("The preset collection exceeds the synchronization entry limit.");
        }

        long totalBytes = 0;
        foreach (CoOpPresetSyncItem? preset in presets)
        {
            if (preset is null || string.IsNullOrWhiteSpace(preset.StoreId) || string.IsNullOrWhiteSpace(preset.Json))
            {
                throw new InvalidDataException("The preset collection contains an invalid entry.");
            }

            totalBytes += Encoding.UTF8.GetByteCount(preset.StoreId);
            totalBytes += Encoding.UTF8.GetByteCount(preset.Json);
            if (totalBytes > CoOpDefaults.MaxPresetSyncBytes)
            {
                throw new InvalidDataException("The preset collection exceeds the synchronization size limit.");
            }
        }
    }

    private static void WriteNewPreset(string path, string content)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))
            ?? throw new InvalidDataException("The preset destination directory is invalid.");
        Directory.CreateDirectory(directory);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private sealed record NamedPreset<T>(string FileName, T Preset);
    private sealed record QuantumPresetPayload(string Name, QuantumStudioWorkspace? Workspace);
}
