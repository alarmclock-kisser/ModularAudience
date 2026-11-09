namespace ModularAudience.Ytdlp;

public sealed record YtdlpVideo
{
    public bool IsChannel { get; init; }
    public bool IsPlaylist { get; init; }
    public bool IsVideo => !this.IsChannel && !this.IsPlaylist;
    public long? PlaylistCount { get; init; }
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string Uploader { get; init; } = string.Empty;
    public string? ChannelUrl { get; init; }
    public double? DurationSeconds { get; init; }
    public string? ThumbnailUrl { get; init; }
    public long? ViewCount { get; init; }
    public DateOnly? UploadDate { get; init; }
    public string? Description { get; init; }
}
