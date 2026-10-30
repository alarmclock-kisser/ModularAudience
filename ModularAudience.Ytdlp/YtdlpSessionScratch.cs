using System.Diagnostics;

namespace ModularAudience.Ytdlp;

internal sealed class YtdlpSessionScratch : IDisposable
{
    private readonly string? path;
    private readonly IProgress<string>? progress;

    public YtdlpSessionScratch(ProcessStartInfo start, string jobDirectory, IProgress<string>? progress)
        : this(start, jobDirectory, progress, null) { }

    public YtdlpSessionScratch(ProcessStartInfo start, string jobDirectory, IProgress<string>? progress, string? cookieContents)
    {
        this.progress = progress;
        if (cookieContents is null && !start.ArgumentList.Contains("--cookies-from-browser") && !start.ArgumentList.Contains("--cookies")) return;
        path = Path.Combine(jobDirectory, "browser-session-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        // yt-dlp can copy a browser database before reading it; keep that scratch inside the owned job.
        start.Environment["TEMP"] = path;
        start.Environment["TMP"] = path;
        start.Environment["TMPDIR"] = path;
        if (cookieContents is not null)
        {
            try
            {
                string copy = Path.Combine(path, "cookies.txt");
                File.WriteAllText(copy, cookieContents, new System.Text.UTF8Encoding(false));
                start.ArgumentList.Add("--cookies");
                start.ArgumentList.Add(copy);
                // Options must precede the URL delimiter.
                int delimiter = start.ArgumentList.IndexOf("--");
                if (delimiter >= 0)
                {
                    start.ArgumentList.RemoveAt(start.ArgumentList.Count - 1);
                    start.ArgumentList.RemoveAt(start.ArgumentList.Count - 1);
                    start.ArgumentList.Insert(delimiter, "--cookies");
                    start.ArgumentList.Insert(delimiter + 1, copy);
                }
                return;
            }
            catch { Directory.Delete(path, true); throw; }
        }
        int cookieIndex = start.ArgumentList.IndexOf("--cookies");
        if (cookieIndex >= 0 && cookieIndex + 1 < start.ArgumentList.Count)
        {
            try
            {
                string copy = Path.Combine(path, "cookies.txt");
                File.Copy(start.ArgumentList[cookieIndex + 1], copy);
                // yt-dlp writes its cookie jar on exit. Each attempt gets its own copy,
                // preserving the user's export and avoiding parallel writers.
                start.ArgumentList[cookieIndex + 1] = copy;
            }
            catch { Directory.Delete(path, recursive: true); throw; }
        }
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
