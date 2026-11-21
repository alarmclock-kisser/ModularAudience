using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModularAudience.Forms.Modules.Dialogs;

internal sealed class DecompileDialogSettings
{
    public int Version { get; set; } = 1;
    public int SeparationMethod { get; set; }
    public int AtomizerMethod { get; set; }
    public int AtomizerSensitivity { get; set; } = 1;
    public int MinSliceMs { get; set; } = 60;
    public int TailPaddingMs { get; set; } = 30;
    public bool AllowSingleAtomFallback { get; set; } = true;
    public bool DeduplicateAtomizer { get; set; } = true;
    public int MaxVariantsPerCluster { get; set; } = 2;
    public float MinimumRmsLevel { get; set; } = 0.001f;
    public int MinimumAtomicDurationMs { get; set; } = 20;
    public float SimilarityThreshold { get; set; } = 0.94f;
    public float LegacySimilarityThreshold { get; set; } = 0.88f;
    public bool LegacyAutomaticSimilarity { get; set; } = true;

    public bool ProjectOutput { get; set; } = true;
    public bool CollectionOutput { get; set; }
    public int ProjectDetail { get; set; }
    public int ProjectOverlap { get; set; }
    public int ProjectAttackMs { get; set; } = 1;
    public bool ProjectSnapToGrid { get; set; }
    public int ProjectSnapDivision { get; set; } = 16;
    public bool DeduplicateCollections { get; set; } = true;
    public int MaxWorkers { get; set; } = Math.Max(1, Environment.ProcessorCount / 2);

    public int AnalogWindowSize { get; set; } = 4096;
    public int AnalogOverlapPercent { get; set; } = 50;
    public int AnalogSharpness { get; set; } = 4;
    public bool AnalogSubtractFromOriginal { get; set; }
    public bool AnalogTimbreAware { get; set; } = true;
    public bool AnalogHarmonicAware { get; set; } = true;
    public List<DecompileAnalogBandState> AnalogBands { get; set; } = [];

    public string DeterministicPreset { get; set; } = "Balanced";
    public DecompileDeterministicState Deterministic { get; set; } = new();
    public bool DeterministicAutomaticProfiles { get; set; } = true;
    public string[] DeterministicProfiles { get; set; } = [];

    public DemucsDialogSettings? Demucs { get; set; }

    internal static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ModularAudience",
        "decompile-settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    internal static DecompileDialogSettings Load(string? path = null)
    {
        path ??= SettingsPath;
        if (!File.Exists(path))
        {
            return new DecompileDialogSettings();
        }

        DecompileDialogSettings settings = JsonSerializer.Deserialize<DecompileDialogSettings>(
            File.ReadAllText(path),
            JsonOptions) ?? new DecompileDialogSettings();
        settings.AnalogBands ??= [];
        settings.Deterministic ??= new DecompileDeterministicState();
        settings.DeterministicProfiles ??= [];
        settings.MaxWorkers = Math.Clamp(settings.MaxWorkers, 1, Environment.ProcessorCount);
        settings.ProjectAttackMs = Math.Clamp(settings.ProjectAttackMs, 1, 100);
        settings.ProjectSnapDivision = settings.ProjectSnapDivision switch
        {
            4 or 8 or 16 or 32 or 64 or 128 or 256 => settings.ProjectSnapDivision,
            _ => 16
        };
        if (settings.MaxVariantsPerCluster < 1)
        {
            settings.MaxVariantsPerCluster = 2;
        }

        return settings;
    }

    internal void Save(string? path = null)
    {
        path ??= SettingsPath;
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string temporary = fullPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(temporary, fullPath, true);
    }
}

internal sealed record DecompileAnalogBandState(string Name, string LowHz, string HighHz);

internal sealed class DecompileDeterministicState
{
    public int WindowSize { get; set; } = 4096;
    public int MaxComponents { get; set; } = 16;
    public int Iterations { get; set; } = 80;
    public int AnalysisFrames { get; set; } = 1024;
    public int BlockFrames { get; set; } = 128;
    public int MedianFrames { get; set; } = 17;
    public int MedianBins { get; set; } = 17;
    public double SeparationMargin { get; set; } = 2.0;
    public double MaskFloor { get; set; } = 0.001;
    public double TransientPreservation { get; set; } = 0.75;
    public bool UseCqtAnalysis { get; set; }
    public bool UseCqtSynthesis { get; set; }
    public bool UsePyin { get; set; }
    public bool UseIlrma { get; set; }
    public int CqtBinsPerOctave { get; set; } = 12;
    public double CqtMinimumHz { get; set; } = 27.5;
    public double PyinMinimumHz { get; set; } = 27.5;
    public double PyinMaximumHz { get; set; } = 1500;
    public int IlrmaIterations { get; set; } = 80;
    public int IlrmaComponents { get; set; } = 2;
}
