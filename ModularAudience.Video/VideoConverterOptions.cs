namespace ModularAudience.Video;

/// <summary>
/// Configuration options for video-to-audio conversion.
/// </summary>
public sealed class VideoConverterOptions
{
    /// <summary>
    /// Maximum number of parallel conversion workers. Defaults to <c>Environment.ProcessorCount - 2</c>, clamped to at least 1.
    /// </summary>
    public int MaxWorkers { get; set; } = Math.Max(1, Environment.ProcessorCount - 2);

    /// <summary>
    /// Target sample rate for the extracted audio (Hz). Default 44100.
    /// </summary>
    public int SampleRate { get; set; } = 44100;

    /// <summary>
    /// Target channel count for the extracted audio. Default 2 (stereo).
    /// </summary>
    public int Channels { get; set; } = 2;

    /// <summary>
    /// Output audio format. Currently only <see cref="AudioOutputFormat.Wav"/> is implemented.
    /// </summary>
    public AudioOutputFormat OutputFormat { get; set; } = AudioOutputFormat.Wav;

    /// <summary>
    /// Directory where converted audio files are written. Defaults to the system temp path.
    /// </summary>
    public string OutputDirectory { get; set; } = Path.GetTempPath();

    /// <summary>
    /// Full path to ffmpeg.exe. If null, the converter attempts to locate ffmpeg on PATH or in common install locations.
    /// </summary>
    public string? FfmpegPath { get; set; }

    public void EnsureOutputDirectory()
    {
        if (!string.IsNullOrWhiteSpace(OutputDirectory) && !Directory.Exists(OutputDirectory))
        {
            Directory.CreateDirectory(OutputDirectory);
        }
    }
}

/// <summary>
/// Audio output formats supported by the converter.
/// </summary>
public enum AudioOutputFormat
{
    Wav,
    Mp3,
}
