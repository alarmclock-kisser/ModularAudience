namespace ModularAudience.Ytdlp;

public sealed record YtdlpDownloadProgress
{
    public string Status { get; init; } = string.Empty;
    public long DownloadedBytes { get; init; }
    public long? TotalBytes { get; init; }
    public bool IsTotalEstimated { get; init; }
    public double? Percent { get; init; }
    public double? SpeedBytesPerSecond { get; init; }
    public TimeSpan? Eta { get; init; }
    public TimeSpan? Elapsed { get; init; }
}
