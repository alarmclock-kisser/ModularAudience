using System.Diagnostics;

namespace ModularAudience.Ytdlp;

internal sealed class YtdlpSessionScratch : IDisposable
{
    private readonly string? path;
    private readonly IProgress<string>? progress;

    public YtdlpSessionScratch(ProcessStartInfo start, string jobDirectory, IProgress<string>? progress)
    {
        this.progress = progress;
        if (!start.ArgumentList.Contains("--cookies-from-browser")) return;
        path = Path.Combine(jobDirectory, "browser-session-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        // yt-dlp can copy a browser database before reading it; keep that scratch inside the owned job.
        start.Environment["TEMP"] = path;
        start.Environment["TMP"] = path;
        start.Environment["TMPDIR"] = path;
    }

    public void Dispose()
    {
        if (path is null) return;
        try { Directory.Delete(path, recursive: true); }
        catch (DirectoryNotFoundException) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            progress?.Report($"Warning: Temporary browser database cleanup could not complete at {path}. Job cleanup will also attempt removal. {error.Message}");
        }
    }
}
