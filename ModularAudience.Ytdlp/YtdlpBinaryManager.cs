using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace ModularAudience.Ytdlp;

public sealed class YtdlpBinaryManager
{
    private static readonly SemaphoreSlim UpdateGate = new(1, 1);
    private static readonly string[] YtdlpFiles = ["yt-dlp.exe"];
    private static readonly string[] FfmpegFiles = ["ffmpeg.exe", "ffprobe.exe"];
    private static readonly string[] DenoFiles = ["deno.exe"];
    private string? verifiedDenoDirectory;
    private string? fallbackDenoPath;

    public string BinaryDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ModularAudience", "binaries_ytdlp");

    public Task EnsureBinariesAsync(IProgress<string>? progress, CancellationToken cancellationToken) =>
        EnsureBinariesAsync(progress, cancellationToken, onlyIfMissing: false);

    internal Task EnsureAvailableAsync(IProgress<string>? progress, CancellationToken cancellationToken) =>
        EnsureBinariesAsync(progress, cancellationToken, onlyIfMissing: true);

    private async Task EnsureBinariesAsync(IProgress<string>? progress, CancellationToken cancellationToken,
        bool onlyIfMissing)
    {
        RequireWindows();
        await UpdateGate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (onlyIfMissing && HasBinaries)
                return;
            Directory.CreateDirectory(BinaryDirectory);
            await UpdateSafelyAsync("yt-dlp", "yt-dlp/yt-dlp", "yt-dlp.exe", YtdlpFiles, progress, cancellationToken);
            await UpdateSafelyAsync("ffmpeg", "BtbN/FFmpeg-Builds", "ffmpeg-master-latest-win64-gpl.zip",
                FfmpegFiles, progress, cancellationToken);
            await UpdateDenoAsync(progress, cancellationToken);
        }
        finally { UpdateGate.Release(); }
    }

    internal static void RequireWindows()
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.OSArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("The bundled binaries require Windows x64.");
    }

    internal bool HasBinaries => GetInstalledDirectory("yt-dlp", YtdlpFiles) is not null &&
        GetInstalledDirectory("ffmpeg", FfmpegFiles) is not null && FindDeno() is not null;

    internal string YtdlpPath => Path.Combine(GetInstalledDirectory("yt-dlp", YtdlpFiles)
        ?? throw new FileNotFoundException("yt-dlp is not installed. Call EnsureBinariesAsync first."), "yt-dlp.exe");

    internal string FfmpegDirectory => GetInstalledDirectory("ffmpeg", FfmpegFiles)
        ?? throw new FileNotFoundException("FFmpeg is not installed. Call EnsureBinariesAsync first.");

    private async Task UpdateDenoAsync(IProgress<string>? progress, CancellationToken token)
    {
        try
        {
            await UpdateSafelyAsync("deno", "denoland/deno", "deno-x86_64-pc-windows-msvc.zip",
                DenoFiles, progress, token);
            verifiedDenoDirectory = GetInstalledDirectory("deno", DenoFiles);
            fallbackDenoPath = null;
        }
        catch (InvalidOperationException error)
        {
            token.ThrowIfCancellationRequested();
            verifiedDenoDirectory = null;
            fallbackDenoPath = FindDenoOnPath();
            if (fallbackDenoPath is null)
                throw;
            progress?.Report($"Warning: Managed Deno installation failed; using the existing PATH runtime at {fallbackDenoPath}. {error.Message}");
        }
    }

    private async Task UpdateSafelyAsync(string tool, string repository, string assetName, string[] files,
        IProgress<string>? progress, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(20));
        try
        {
            progress?.Report($"Checking {tool} for updates.");
            var assets = await GitHubReleaseSource.GetAssetsAsync(repository, timeout.Token);
            var asset = assets.SingleOrDefault(a => a.Name == assetName)
                ?? throw new InvalidDataException($"The GitHub release does not contain {assetName}.");
            // yt-dlp publishes a dedicated checksum manifest; use it even when API digests are available.
            var checksumAsset = tool == "yt-dlp" ? asset with { Digest = null } : asset;
            var hash = await GitHubReleaseSource.GetChecksumAsync(checksumAsset, assets, timeout.Token);
            if (await IsCurrentAsync(tool, hash, files, timeout.Token))
            {
                progress?.Report($"{tool} is up to date.");
                return;
            }
            await InstallAsync(tool, asset, hash, files, progress, timeout.Token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) when (IsUpdateFailure(error))
        {
            if (!await IsUsableAsync(tool, files, token))
                throw new InvalidOperationException($"Unable to install the required {tool} binaries: {error.Message}", error);
            progress?.Report($"Warning: {tool} update failed; the verified installed version remains available. {error.Message}");
        }
    }

    private static bool IsUpdateFailure(Exception error) => error is HttpRequestException or IOException or
        InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or
        OperationCanceledException;

    private async Task InstallAsync(string tool, ReleaseAsset asset, string hash, string[] files,
        IProgress<string>? progress, CancellationToken token)
    {
        var staging = Path.Combine(BinaryDirectory, ".stage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            progress?.Report($"Downloading and verifying {tool}.");
            var package = Path.Combine(staging, "package.download");
            await GitHubReleaseSource.DownloadAsync(asset, package, hash, token);
            var payload = Path.Combine(staging, "payload");
            Directory.CreateDirectory(payload);
            if (asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                await ExtractExecutablesAsync(package, payload, files, token);
            else if (files.Length == 1)
                File.Move(package, Path.Combine(payload, files[0]));
            else
                throw new InvalidDataException("Multiple executables require a ZIP archive.");
            var hashes = await VerifyExecutablesAsync(payload, files, token);
            var state = new InstalledState(hash, Guid.NewGuid().ToString("N"), hashes);
            var destination = StateDirectory(tool, state);
            token.ThrowIfCancellationRequested();
            Directory.Move(payload, destination);
            var marker = Path.Combine(staging, "state.json");
            await File.WriteAllTextAsync(marker, JsonSerializer.Serialize(state), token);
            token.ThrowIfCancellationRequested();
            File.Move(marker, MarkerPath(tool), overwrite: true);
            progress?.Report($"{tool} was installed successfully.");
        }
        finally { DeleteStaging(staging); }
    }

    private static async Task ExtractExecutablesAsync(string package, string destination, string[] files,
        CancellationToken token)
    {
        using var archive = ZipFile.OpenRead(package);
        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            var entry = archive.Entries.SingleOrDefault(e => IsExecutableEntry(e, file))
                ?? throw new InvalidDataException($"The archive does not contain {file}.");
            if (entry.Length is < 2 or > 536870912)
                throw new InvalidDataException("The extracted executable exceeds the allowed size.");
            await using var source = entry.Open();
            await using var target = new FileStream(Path.Combine(destination, file), FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
            var buffer = new byte[81920];
            long total = 0;
            int count;
            while ((count = await source.ReadAsync(buffer.AsMemory(), token)) > 0)
            {
                total += count;
                if (total > entry.Length)
                    throw new InvalidDataException("The archive entry exceeded its declared size.");
                await target.WriteAsync(buffer.AsMemory(0, count), token);
            }
            if (total != entry.Length)
                throw new InvalidDataException("The archive entry is truncated.");
        }
    }

    private static bool IsExecutableEntry(ZipArchiveEntry entry, string file)
    {
        var segments = entry.FullName.Replace('\\', '/').Split('/');
        return string.Equals(segments[^1], file, StringComparison.OrdinalIgnoreCase) &&
            segments.All(segment => segment.Length > 0 && segment is not ("." or "..") && !segment.Contains(':'));
    }

    private static async Task<Dictionary<string, string>> VerifyExecutablesAsync(string directory, string[] files,
        CancellationToken token)
    {
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var path = Path.Combine(directory, file);
            using (var stream = File.OpenRead(path))
                if (stream.ReadByte() != 'M' || stream.ReadByte() != 'Z')
                    throw new InvalidDataException($"{file} is not a Windows executable.");
            hashes[file] = await GitHubReleaseSource.HashAsync(path, token);
        }
        return hashes;
    }

    private async Task<bool> IsCurrentAsync(string tool, string hash, string[] files, CancellationToken token)
    {
        var state = ReadState(tool);
        return state?.PackageHash == hash && await IsUsableAsync(tool, files, token);
    }

    private async Task<bool> IsUsableAsync(string tool, string[] files, CancellationToken token)
    {
        var state = ReadState(tool);
        var directory = GetInstalledDirectory(tool, files);
        if (state is null || directory is null)
            return false;
        try
        {
            foreach (var file in files)
                if (!state.FileHashes.TryGetValue(file, out var hash) ||
                    !string.Equals(hash, await GitHubReleaseSource.HashAsync(Path.Combine(directory, file), token),
                        StringComparison.OrdinalIgnoreCase))
                    return false;
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private string? GetInstalledDirectory(string tool, string[] files)
    {
        var state = ReadState(tool);
        if (state is null)
            return null;
        var directory = StateDirectory(tool, state);
        return files.All(file => File.Exists(Path.Combine(directory, file))) ? directory : null;
    }

    private InstalledState? ReadState(string tool)
    {
        try
        {
            var path = MarkerPath(tool);
            if (!File.Exists(path) || new FileInfo(path).Length > 8192)
                return null;
            var state = JsonSerializer.Deserialize<InstalledState>(File.ReadAllText(path));
            return state is not null && IsHex(state.PackageHash, 64) && IsHex(state.InstallId, 32) &&
                state.FileHashes is not null ? state : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        { return null; }
    }

    private static bool IsHex(string? value, int length) =>
        value?.Length == length && value.All(char.IsAsciiHexDigit);

    private string MarkerPath(string tool) => Path.Combine(BinaryDirectory, tool + ".installed.json");
    private string StateDirectory(string tool, InstalledState state) =>
        Path.Combine(BinaryDirectory, $"{tool}-{state.PackageHash}-{state.InstallId}");

    internal string? FindDeno()
    {
        if (verifiedDenoDirectory is not null)
        {
            var path = Path.Combine(verifiedDenoDirectory, "deno.exe");
            if (File.Exists(path))
                return path;
        }
        return fallbackDenoPath is not null && File.Exists(fallbackDenoPath) ? fallbackDenoPath : null;
    }

    private static string? FindDenoOnPath()
    {
        foreach (var entry in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
        {
            var directory = entry.Trim().Trim('"');
            if (!Path.IsPathFullyQualified(directory))
                continue;
            try
            {
                var path = Path.Combine(directory, "deno.exe");
                if (File.Exists(path))
                    return Path.GetFullPath(path);
            }
            catch (ArgumentException) { }
        }
        return null;
    }

    internal static void DeleteStaging(string directory)
    {
        try { Directory.Delete(directory, recursive: true); }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed record InstalledState(string PackageHash, string InstallId, Dictionary<string, string> FileHashes);
}
