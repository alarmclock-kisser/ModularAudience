namespace ModularAudience.Video;

/// <summary>
/// Outcome of converting a single video file to audio.
/// </summary>
public sealed class VideoConversionResult
{
    public string VideoFilePath { get; }
    public string? AudioFilePath { get; }
    public bool Success { get; }
    public string? ErrorMessage { get; }
    public TimeSpan Duration { get; }
    public double AverageBitrateKbps { get; }

    private VideoConversionResult(string videoFilePath, string? audioFilePath, bool success,
        string? errorMessage, TimeSpan duration, double averageBitrateKbps)
    {
        VideoFilePath = videoFilePath;
        AudioFilePath = audioFilePath;
        Success = success;
        ErrorMessage = errorMessage;
        Duration = duration;
        AverageBitrateKbps = averageBitrateKbps;
    }

    public static VideoConversionResult SuccessResult(string videoFilePath, string audioFilePath,
        TimeSpan duration, double averageBitrateKbps) =>
        new(videoFilePath, audioFilePath, true, null, duration, averageBitrateKbps);

    public static VideoConversionResult FailureResult(string videoFilePath, string errorMessage) =>
        new(videoFilePath, null, false, errorMessage, TimeSpan.Zero, 0.0);
}
