using ModularAudience.Audio.Processing;
using ModularAudience.Ytdlp;
using System.Text.Json;

namespace ModularAudience.Forms.Modules.Dialogs;

internal sealed class YoutubeBrowserSettings
{
    public int Width { get; set; } = 1180;
    public int Height { get; set; } = 820;
    public bool Maximized { get; set; }
    public int SplitterDistance { get; set; } = 760;
    public bool ShowThumbnails { get; set; } = true;
    public bool[] VisibleColumns { get; set; } = [true, true, true, true, false, false, true];
    public int ResultLimit { get; set; } = 30;
    public bool SaveToDisk { get; set; }
    public string Format { get; set; } = "wav";
    public int Mp3Bitrate { get; set; } = 192;
    public int WavBitDepth { get; set; } = 16;
    public int SortColumn { get; set; } = -1;
    public bool SortDescending { get; set; }
    public YtdlpIpVersion IpVersion { get; set; }
    public int ProxyMode { get; set; }
    public string ProxyUrl { get; set; } = string.Empty;
    public string OutputDirectory { get; set; } = DefaultOutputDirectory;
    public int CooldownSeconds { get; set; } = 5;
    public int MaxDurationMinutes { get; set; } = 30;
    public int MaxSizeMiB { get; set; } = 512;
    public int MaxParallelDownloads { get; set; } = 2;
    public bool StretchEnabled { get; set; }
    public PlaylistStretchSettings? Stretch { get; set; }

    internal static string DefaultOutputDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "ModularAudience");

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ModularAudience", "youtube-settings.json");

    internal static YoutubeBrowserSettings Load(out string? warning)
    {
        warning = null;
        try
        {
            if (!File.Exists(SettingsPath)) return new();
            if (new FileInfo(SettingsPath).Length > 65536)
                throw new InvalidDataException("The settings file exceeds 64 KiB.");
            var settings = JsonSerializer.Deserialize<YoutubeBrowserSettings>(File.ReadAllText(SettingsPath))
                ?? throw new InvalidDataException("The settings file is empty.");
            if (settings.Normalize()) warning = "Warning: Invalid YouTube settings were reset to safe values.";
            return settings;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            warning = $"Warning: YouTube settings could not be read; defaults are in use. {error.Message}";
            return new();
        }
    }

    private bool Normalize()
    {
        string before = JsonSerializer.Serialize(this);
        Width = Math.Clamp(Width, 1000, 7680);
        Height = Math.Clamp(Height, 680, 4320);
        SplitterDistance = Math.Clamp(SplitterDistance, 400, 7000);
        ResultLimit = Math.Clamp(ResultLimit, 1, 200);
        int[] bitrates = [64, 96, 128, 192, 256, 320];
        Mp3Bitrate = bitrates.MinBy(value => Math.Abs((long)value - Mp3Bitrate));
        if (WavBitDepth is not (8 or 16 or 24 or 32)) WavBitDepth = 16;
        SortColumn = Math.Clamp(SortColumn, -1, 6);
        if (!Enum.IsDefined(IpVersion)) IpVersion = YtdlpIpVersion.Automatic;
        ProxyMode = Math.Clamp(ProxyMode, 0, 2);
        ProxyUrl ??= string.Empty;
        try { new YtdlpNetworkOptions { ProxyUrl = ProxyUrl }.Validate(); }
        catch (ArgumentException) { ProxyUrl = string.Empty; ProxyMode = 0; }
        CooldownSeconds = Math.Clamp(CooldownSeconds, 0, 300);
        MaxDurationMinutes = Math.Clamp(MaxDurationMinutes, 1, 1440);
        MaxSizeMiB = Math.Clamp(MaxSizeMiB, 1, 16384);
        MaxParallelDownloads = Math.Clamp(MaxParallelDownloads, 1, YtdlpBatchRunner.MaximumParallelism);
        Format = Format == "mp3" ? "mp3" : "wav";
        if (VisibleColumns is { Length: 6 }) VisibleColumns = [.. VisibleColumns, true];
        if (VisibleColumns is not { Length: 7 }) VisibleColumns = [true, true, true, true, false, false, true];
        VisibleColumns[0] = true;
        OutputDirectory = NormalizeDirectory(OutputDirectory);
        if (!ValidStretch(Stretch)) Stretch = null;
        if (Stretch is not null) Stretch = Stretch with { Threads = Math.Clamp(Stretch.Threads, 1, Math.Max(1, Environment.ProcessorCount)) };
        StretchEnabled &= Stretch is not null;
        return before != JsonSerializer.Serialize(this);
    }

    private static string NormalizeDirectory(string? directory)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(directory) && directory.Length <= 2048 &&
                !directory.Any(char.IsControl) && Path.IsPathFullyQualified(directory))
                return Path.GetFullPath(directory);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException) { }
        return DefaultOutputDirectory;
    }

    private static bool ValidStretch(PlaylistStretchSettings? value) => value is not null &&
        float.IsFinite(value.TargetBpm) && value.TargetBpm is >= 5 and <= 420 &&
        float.IsFinite(value.StretchFactor) && value.StretchFactor is >= 0.01f and <= 128 &&
        value.ChunkSize is >= 128 and <= 65536 && (value.ChunkSize & (value.ChunkSize - 1)) == 0 &&
        float.IsFinite(value.Overlap) && value.Overlap is >= 0 and <= 0.9f && value.Threads > 0;

    internal void Save()
    {
        Normalize();
        string path = SettingsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, this, new JsonSerializerOptions { WriteIndented = true });
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
