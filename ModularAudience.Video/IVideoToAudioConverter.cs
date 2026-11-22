using System.Threading;

namespace ModularAudience.Video;

/// <summary>
/// Converts video files to audio files. Thread-safe: a single instance may be used
/// concurrently from multiple threads for different video files.
/// </summary>
public interface IVideoToAudioConverter
{
    /// <summary>
    /// Converts a single video file to an audio file and reports progress.
    /// </summary>
    /// <param name="videoFilePath">Full path to the source video file.</param>
    /// <param name="outputAudioFilePath">Full path for the resulting audio file.</param>
    /// <param name="progress">Progress reporter (0.0 to 1.0). May be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="VideoConversionResult"/> describing the outcome.</returns>
    Task<VideoConversionResult> ConvertAsync(string videoFilePath, string outputAudioFilePath,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Converts multiple video files to audio, with configurable concurrency and aggregated progress.
    /// </summary>
    /// <param name="videoFilePaths">Full paths to the source video files.</param>
    /// <param name="progress">Aggregated progress reporter (0.0 to 1.0 across all files). May be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Results for each input file, in the same order.</returns>
    Task<IReadOnlyList<VideoConversionResult>> ConvertManyAsync(IEnumerable<string> videoFilePaths,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}
