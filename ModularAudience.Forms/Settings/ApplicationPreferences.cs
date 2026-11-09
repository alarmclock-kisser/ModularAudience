using System.Text.Json;
using System.Text.Json.Serialization;
using ModularAudience.Audio;

namespace ModularAudience.Forms.Settings;

internal sealed record ApplicationPreferences
{
    internal const float DefaultLogFontSize = 8f;
    internal const float MinimumLogFontSize = 6f;
    internal const float MaximumLogFontSize = 20f;

    public string ExportFormat { get; init; } = ".wav";
    public int ExportBits { get; init; } = 16;
    public string? WorkingDirectory { get; init; }
    public float LogFontSize { get; init; } = DefaultLogFontSize;
    public bool KeepStructure { get; init; } = true;
    public bool AllInOneBag { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalSettings { get; init; }

    internal ApplicationPreferences Normalize()
    {
        string format = NormalizeFormat(this.ExportFormat);
        int bits = AudioExporter.AvailableExportFormats.TryGetValue(format, out int[]? options) && options.Contains(this.ExportBits)
            ? this.ExportBits : DefaultBits(format);
        string? directory = string.IsNullOrWhiteSpace(this.WorkingDirectory) ? null : NormalizeDirectory(this.WorkingDirectory);
        float size = float.IsFinite(this.LogFontSize)
            ? Math.Clamp(this.LogFontSize, MinimumLogFontSize, MaximumLogFontSize) : DefaultLogFontSize;
        return this with { ExportFormat = format, ExportBits = bits, WorkingDirectory = directory, LogFontSize = size };
    }

    internal static string NormalizeFormat(string? format)
    {
        string candidate = "." + (format ?? string.Empty).Trim().TrimStart('.').ToLowerInvariant();
        return AudioExporter.AvailableExportFormats.ContainsKey(candidate) ? candidate : ".wav";
    }

    internal static int DefaultBits(string format)
    {
        if (!AudioExporter.AvailableExportFormats.TryGetValue(format, out int[]? bits) || bits.Length == 0) return 16;
        int preferred = AudioExporter.IsMp3Format(format) ? 192 : 16;
        return bits.Contains(preferred) ? preferred : bits[bits.Length / 2];
    }

    internal static string NormalizeDirectory(string directory)
    {
        if (!Path.IsPathFullyQualified(directory)) throw new InvalidDataException("The audio working directory must be an absolute path.");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
    }
}
