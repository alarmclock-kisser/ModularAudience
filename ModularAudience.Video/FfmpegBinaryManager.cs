using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace ModularAudience.Video;

/// <summary>
/// Lädt nur ffmpeg.exe von GitHub (BtbN/FFmpeg-Builds) herunter und installiert es
/// im selben Verzeichnis wie die Ytdlp-Binaries, sodass es von beiden Seiten wiederverwendet werden kann.
/// </summary>
public sealed class FfmpegBinaryManager
{
    private const string GithubRepository = "BtbN/FFmpeg-Builds";
    private const string FfmpegAssetName = "ffmpeg-master-latest-win64-gpl.zip";

    private static readonly string BinaryRootDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ModularAudience", "binaries_ytdlp");

    public string BinaryDirectory { get; } = BinaryRootDirectory;

    /// <summary>
    /// Gibt den Pfad zu einer bereits installierten ffmpeg.exe zurück, oder null.
    /// Sucht im Ytdlp-Verzeichnis (ffmpeg-*) und in PATH.
    /// </summary>
    public string? FindInstalledFfmpeg()
    {
        try
        {
            if (Directory.Exists(BinaryRootDirectory))
            {
                foreach (var dir in Directory.EnumerateDirectories(BinaryRootDirectory, "ffmpeg-*", SearchOption.TopDirectoryOnly))
                {
                    var candidate = Path.Combine(dir, "ffmpeg.exe");
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
        }
        catch { /* Verzeichnis existiert ggf. noch nicht */ }

        // Neben der Entry Assembly
        var local = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        if (File.Exists(local)) return local;

        // PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var entry in pathEnv.Split(Path.PathSeparator))
        {
            var dir = entry.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(dir) || !Path.IsPathFullyQualified(dir)) continue;
            try
            {
                var candidate = Path.Combine(dir, "ffmpeg.exe");
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            }
            catch { /* ignore */ }
        }

        return null;
    }

    /// <summary>
    /// Installiert ffmpeg.exe (nur ffmpeg, kein yt-dlp/deno) mit Fortschrittsmeldungen.
    /// Gibt den Pfad zur installierten ffmpeg.exe zurück.
    /// </summary>
    public async Task<string> InstallFfmpegAsync(IProgress<string>? progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(BinaryRootDirectory);

        progress?.Report("Prüfe ffmpeg-Version auf GitHub...");
        var releaseUrl = $"https://api.github.com/repos/{GithubRepository}/releases/latest";

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("ModularAudience", "1.0"));

        string json;
        try
        {
            json = await httpClient.GetStringAsync(releaseUrl).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Konnte GitHub-Release für ffmpeg nicht abfragen: {ex.Message}", ex);
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string? downloadUrl = null;
        string? assetName = null;

        if (root.TryGetProperty("assets", out var assets))
        {
            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.TryGetProperty("name", out var nameProp) &&
                    string.Equals(nameProp.GetString(), FfmpegAssetName, StringComparison.OrdinalIgnoreCase) &&
                    asset.TryGetProperty("browser_download_url", out var urlProp))
                {
                    downloadUrl = urlProp.GetString();
                    assetName = nameProp.GetString();
                    break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            throw new InvalidDataException($"Das GitHub-Release enthält keine {FfmpegAssetName}.");
        }

        progress?.Report($"Lade {assetName} herunter ...");

        var staging = Path.Combine(BinaryRootDirectory, ".stage-ffmpeg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var zipPath = Path.Combine(staging, "ffmpeg.zip");

            await DownloadFileAsync(httpClient, downloadUrl!, zipPath, progress, cancellationToken).ConfigureAwait(false);

            progress?.Report("Entpacke ffmpeg ...");
            var payload = Path.Combine(staging, "payload");
            Directory.CreateDirectory(payload);

            using var archive = ZipFile.OpenRead(zipPath);
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.Equals(entry.Name, "ffmpeg.exe", StringComparison.OrdinalIgnoreCase) &&
                    entry.Length > 0 && entry.Length < 100_000_000)
                {
                    var targetPath = Path.Combine(payload, entry.Name);
                    await using var source = entry.Open();
                    await using var target = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
                    await source.CopyToAsync(target, 81920, cancellationToken).ConfigureAwait(false);
                    break;
                }
            }

            var ffmpegExe = Path.Combine(payload, "ffmpeg.exe");
            if (!File.Exists(ffmpegExe))
            {
                throw new InvalidDataException("ffmpeg.exe wurde im Archiv nicht gefunden.");
            }

            var destination = Path.Combine(BinaryRootDirectory, $"ffmpeg-installed-{Guid.NewGuid():N}");
            Directory.Move(payload, destination);

            var finalPath = Path.Combine(destination, "ffmpeg.exe");
            progress?.Report("ffmpeg wurde installiert.");
            return finalPath;
        }
        finally
        {
            try { Directory.Delete(staging, recursive: true); } catch { }
        }
    }

    private static async Task DownloadFileAsync(HttpClient httpClient, string url, string destination,
        IProgress<string>? progress, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        using var sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var targetStream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);

        var buffer = new byte[81920];
        long totalRead = 0;
        int read;
        while ((read = await sourceStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await targetStream.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
            totalRead += read;

            if (totalBytes.HasValue && totalBytes.Value > 0)
            {
                double pct = (double)totalRead / totalBytes.Value * 100.0;
                progress?.Report($"Heruntergeladen: {totalRead / 1024.0 / 1024.0:F1} MB / {totalBytes.Value / 1024.0 / 1024.0:F1} MB ({pct:F1}%)");
            }
            else
            {
                progress?.Report($"Heruntergeladen: {totalRead / 1024.0 / 1024.0:F1} MB");
            }
        }
    }
}
