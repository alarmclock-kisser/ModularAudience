namespace ModularAudience.Forms.Settings;

/// <summary>Debounces atomic file replacement notifications from other application instances.</summary>
internal sealed class ApplicationPreferencesMonitor : IDisposable
{
    private readonly FileSystemWatcher watcher;
    private readonly System.Threading.Timer debounce;
    private int disposed;

    internal ApplicationPreferencesMonitor(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        this.debounce = new System.Threading.Timer(_ =>
        {
            if (Volatile.Read(ref this.disposed) == 0) this.Changed?.Invoke(this, EventArgs.Empty);
        }, null, Timeout.Infinite, Timeout.Infinite);
        this.watcher = new FileSystemWatcher(directory, Path.GetFileName(fullPath))
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
        };
        this.watcher.Changed += this.Schedule;
        this.watcher.Created += this.Schedule;
        this.watcher.Deleted += this.Schedule;
        this.watcher.Renamed += this.Schedule;
        this.watcher.Error += (_, _) => this.Schedule(this, EventArgs.Empty);
        this.watcher.EnableRaisingEvents = true;
    }

    internal event EventHandler? Changed;

    private void Schedule(object? sender, EventArgs e)
    {
        if (Volatile.Read(ref this.disposed) != 0) return;
        try { this.debounce.Change(120, Timeout.Infinite); }
        catch (ObjectDisposedException) { }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref this.disposed, 1) != 0) return;
        this.watcher.Dispose();
        this.debounce.Dispose();
    }
}
