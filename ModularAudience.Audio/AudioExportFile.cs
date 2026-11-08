namespace ModularAudience.Audio;

/// <summary>
/// Atomically claims an output name before encoding. CreateNew protects existing
/// exports even when parallel exports or other application instances collide.
/// </summary>
internal sealed class AudioExportFile : IDisposable
{
    private readonly bool newlyCreated;
    private bool completed;
    private bool disposed;

    private AudioExportFile(string filePath, FileStream stream, bool newlyCreated)
    {
        this.FilePath = filePath;
        this.Stream = stream;
        this.newlyCreated = newlyCreated;
    }

    internal string FilePath { get; }
    internal FileStream Stream { get; }

    internal static AudioExportFile CreateUnique(string requestedPath)
    {
        string fullPath = Path.GetFullPath(requestedPath);
        string directory = Path.GetDirectoryName(fullPath)!;
        string name = Path.GetFileNameWithoutExtension(fullPath);
        string extension = Path.GetExtension(fullPath);
        for (int suffix = 0; ; suffix = checked(suffix + 1))
        {
            string candidate = suffix == 0 ? fullPath : Path.Combine(directory, $"{name} ({suffix}){extension}");
            try
            {
                var stream = new FileStream(candidate, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
                return new AudioExportFile(candidate, stream, newlyCreated: true);
            }
            catch (IOException) when (File.Exists(candidate) || Directory.Exists(candidate))
            {
                // Another export already owns this name. Try the next suffix.
            }
        }
    }

    internal static AudioExportFile Overwrite(string requestedPath)
    {
        string fullPath = Path.GetFullPath(requestedPath);
        return new AudioExportFile(fullPath,
            new FileStream(fullPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None), newlyCreated: false);
    }

    internal void Complete()
    {
        this.Stream.Dispose();
        this.completed = true;
    }

    public void Dispose()
    {
        if (this.disposed) return;
        this.disposed = true;
        this.Stream.Dispose();
        if (!this.completed && this.newlyCreated)
        {
            try { File.Delete(this.FilePath); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                LogCollection.Log($"An incomplete export could not be removed: {this.FilePath}. {error.Message}");
            }
        }
    }
}
