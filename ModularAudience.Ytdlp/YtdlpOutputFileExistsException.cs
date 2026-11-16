namespace ModularAudience.Ytdlp;

public sealed class YtdlpOutputFileExistsException : IOException
{
    public string OutputPath { get; }

    public YtdlpOutputFileExistsException(string outputPath, Exception? innerException = null)
        : base($"Cannot save converted audio because an item already exists at '{outputPath}'. Rename or remove it before downloading.", innerException)
    {
        this.OutputPath = outputPath;
    }
}
