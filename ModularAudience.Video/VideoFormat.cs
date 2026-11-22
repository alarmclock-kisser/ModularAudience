namespace ModularAudience.Video;

/// <summary>
/// Supported video container formats for audio extraction.
/// </summary>
public static class VideoFormat
{
    /// <summary>All extensions supported by the converter, including the leading dot, lower-case.</summary>
    public static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4",
        ".mov",
        ".avi",
        ".flv",
    };

    /// <summary>File filter string suitable for a WinForms <see cref="System.Windows.Forms.OpenFileDialog"/>.</summary>
    public static readonly string Filter =
        "Video Dateien (*.mp4;*.mov;*.avi;*.flv)|*.mp4;*.mov;*.avi;*.flv|Alle Dateien (*.*)|*.*";

    public static bool IsSupported(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var ext = Path.GetExtension(filePath);
        return SupportedExtensions.Contains(ext);
    }
}
