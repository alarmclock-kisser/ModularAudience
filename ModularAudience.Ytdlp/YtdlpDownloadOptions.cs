namespace ModularAudience.Ytdlp;

public sealed class YtdlpDownloadOptions
{
    public string Format { get; init; } = "wav";
    public bool SaveToDisk { get; init; }
    public string OutputDirectory { get; init; } = string.Empty;
    public int Mp3Bitrate { get; init; } = 192;
    public int WavBitDepth { get; init; } = 16;
    public YtdlpNetworkOptions Network { get; init; } = new();
    public TimeSpan? MaxDuration { get; init; } = TimeSpan.FromHours(4);
    public long? MaxFileSizeBytes { get; init; } = 2L * 1024 * 1024 * 1024;
}
