namespace ModularAudience.Ytdlp;

public sealed class YtdlpDownloadResult : IDisposable, IAsyncDisposable
{
    private string? ownedDirectory;

    internal YtdlpDownloadResult(string filePath, string? ownedDirectory)
    {
        FilePath = filePath;
        this.ownedDirectory = ownedDirectory;
        IsTemporary = ownedDirectory is not null;
    }

    public string FilePath { get; }
    public bool IsTemporary { get; }

    public void Dispose()
    {
        var directory = Interlocked.Exchange(ref ownedDirectory, null);
        if (directory is null)
            return;

        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (DirectoryNotFoundException) { }
        catch
        {
            Interlocked.CompareExchange(ref ownedDirectory, directory, null);
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
