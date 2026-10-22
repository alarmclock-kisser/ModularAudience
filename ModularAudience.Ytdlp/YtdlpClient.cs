using System.Globalization;

namespace ModularAudience.Ytdlp;

public sealed class YtdlpClient
{
    private const string OutputPathPrefix = "__MA_OUTPUT__:";
    private readonly YtdlpBinaryManager manager;

    public YtdlpClient(YtdlpBinaryManager? manager = null)
    {
        this.manager = manager ?? new YtdlpBinaryManager();
    }

    public event EventHandler<string>? ProgressChanged;
    public YtdlpNetworkOptions NetworkOptions { get; set; } = new();
    public YtdlpBrowserSession? MetadataSession { get; set; }

    public Task<IReadOnlyList<YtdlpVideo>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        YtdlpParser.ValidateMaxResults(maxResults);
        if (query.Length > 500 || query.Any(char.IsControl))
            throw new ArgumentException("Search queries must contain at most 500 characters and no control characters.", nameof(query));
        return ReadMetadataAsync($"ytsearch{maxResults}:{query.Trim()}", maxResults, cancellationToken);
    }

    public Task<IReadOnlyList<YtdlpVideo>> BrowseAsync(string youtubeUrl, int maxResults,
        CancellationToken cancellationToken)
    {
        YtdlpParser.ValidateMaxResults(maxResults);
        return ReadMetadataAsync(YtdlpParser.ValidateYoutubeUrl(youtubeUrl).AbsoluteUri, maxResults, cancellationToken);
    }

    public async Task<YtdlpVideo> GetVideoMetadataAsync(YtdlpVideo video, CancellationToken token)
    {
        var url = ValidateVideo(video);
        var progress = CreateProgress(null);
        await EnsureAvailableAsync(progress, token);
        var arguments = CommonArguments();
        MetadataSession?.AddArguments(arguments);
        arguments.AddRange(["--dump-single-json", "--no-playlist", "--skip-download", "--", url]);
        var job = CreateJobDirectory();
        try
        {
            var json = await YtdlpProcess.RunAsync(manager.YtdlpPath, arguments, job, progress, false, token);
            return YtdlpParser.ParseVideos(json, 1).SingleOrDefault()
                ?? throw new InvalidDataException("No video metadata was returned.");
        }
        finally { YtdlpBinaryManager.DeleteStaging(job); }
    }

    private async Task<IReadOnlyList<YtdlpVideo>> ReadMetadataAsync(string target, int maxResults, CancellationToken token)
    {
        var progress = CreateProgress(null);
        await EnsureAvailableAsync(progress, token);
        var arguments = CommonArguments();
        MetadataSession?.AddArguments(arguments);
        arguments.AddRange(["--dump-single-json", "--flat-playlist", "--skip-download", "--yes-playlist",
            "--playlist-end", maxResults.ToString(CultureInfo.InvariantCulture), "--", target]);
        var job = CreateJobDirectory();
        try
        {
            var json = await YtdlpProcess.RunAsync(manager.YtdlpPath, arguments, job, progress, false, token);
            var videos = YtdlpParser.ParseVideos(json, maxResults);
            progress.Report($"Loaded {videos.Count} YouTube video entries.");
            return videos;
        }
        finally { YtdlpBinaryManager.DeleteStaging(job); }
    }

    public async Task<YtdlpDownloadResult> DownloadAsync(YtdlpVideo video, YtdlpDownloadOptions options,
        IProgress<string>? progress, CancellationToken cancellationToken,
        IProgress<YtdlpDownloadProgress>? downloadProgress = null)
    {
        ArgumentNullException.ThrowIfNull(video);
        ArgumentNullException.ThrowIfNull(options);
        var format = ValidateOptions(options);
        var url = ValidateVideo(video);
        var outputDirectory = options.SaveToDisk ? Path.GetFullPath(options.OutputDirectory) : null;
        var output = CreateDownloadProgress(progress, downloadProgress);
        await EnsureAvailableAsync(output, cancellationToken);
        var job = CreateJobDirectory();
        var ownershipTransferred = false;
        try
        {
            var arguments = DownloadArguments(url, format, job, options);
            var text = await YtdlpProcess.RunAsync(manager.YtdlpPath, arguments, job, output, true, cancellationToken);
            var path = ValidateDownloadedPath(text, job, format, options.MaxFileSizeBytes);
            cancellationToken.ThrowIfCancellationRequested();
            if (outputDirectory is not null)
                path = await SaveAsync(path, outputDirectory, video, format, cancellationToken);
            var result = new YtdlpDownloadResult(path, outputDirectory is null ? job : null);
            ownershipTransferred = outputDirectory is null;
            return result;
        }
        finally
        {
            if (!ownershipTransferred)
                YtdlpBinaryManager.DeleteStaging(job);
        }
    }

    private async Task EnsureAvailableAsync(IProgress<string> progress, CancellationToken token)
    {
        YtdlpBinaryManager.RequireWindows();
        token.ThrowIfCancellationRequested();
        if (!manager.HasBinaries)
            await manager.EnsureAvailableAsync(progress, token);
    }

    private List<string> CommonArguments(YtdlpNetworkOptions? network = null)
    {
        var deno = manager.FindDeno()
            ?? throw new FileNotFoundException("Deno is required. Call EnsureBinariesAsync first.");
        var arguments = new List<string>
        {
            "--ignore-config", "--no-plugin-dirs", "--no-remote-components", "--no-js-runtimes",
            "--js-runtimes", "deno:" + deno,
            "--no-cache-dir", "--encoding", "utf-8", "--no-colors", "--socket-timeout", "30",
            "--retries", "3", "--fragment-retries", "3", "--extractor-retries", "3",
            "--use-extractors", "youtube.*", "--ffmpeg-location", manager.FfmpegDirectory
        };
        (network ?? NetworkOptions).AddArguments(arguments);
        return arguments;
    }

    private List<string> DownloadArguments(string url, string format, string job, YtdlpDownloadOptions options)
    {
        var arguments = CommonArguments(options.Network);
        options.BrowserSession?.AddArguments(arguments);
        arguments.AddRange(["--no-playlist", "--extract-audio", "--audio-format", format,
            "--format", "bestaudio/best", "--paths", job, "--output", "audio.%(ext)s",
            "--no-overwrites", "--windows-filenames", "--newline", "--progress", "--progress-delta", "1",
            "--progress-template", "download:" + YtdlpProgressParser.Prefix +
                "{\"status\":%(progress.status)j,\"downloaded_bytes\":%(progress.downloaded_bytes)j," +
                "\"total_bytes\":%(progress.total_bytes)j,\"total_bytes_estimate\":%(progress.total_bytes_estimate)j," +
                "\"speed\":%(progress.speed)j,\"eta\":%(progress.eta)j,\"elapsed\":%(progress.elapsed)j}",
            "--print", "after_move:" + OutputPathPrefix + "%(filepath)s", "--no-quiet"]);
        AddAudioQualityArguments(arguments, format, options);
        var filter = "!is_live";
        if (options.MaxDuration is { } duration)
            filter += " & duration <= " + duration.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        arguments.AddRange(["--match-filters", filter]);
        if (options.MaxFileSizeBytes is { } maximum)
            arguments.AddRange(["--max-filesize", maximum.ToString(CultureInfo.InvariantCulture)]);
        arguments.AddRange(["--", url]);
        return arguments;
    }

    private static void AddAudioQualityArguments(List<string> arguments, string format, YtdlpDownloadOptions options)
    {
        if (format.Contains("3"))
            arguments.AddRange(["--audio-quality", options.Mp3Bitrate.ToString(CultureInfo.InvariantCulture) + "K"]);
        else
            arguments.AddRange(["--postprocessor-args", "ExtractAudio+ffmpeg_o:-c:a " + WavCodec(options.WavBitDepth)]);
    }

    private static string WavCodec(int bitDepth) => bitDepth switch
    {
        8 => "pcm_u8", 16 => "pcm_s16le", 24 => "pcm_s24le", 32 => "pcm_s32le",
        _ => throw new ArgumentOutOfRangeException(nameof(bitDepth), "WAV bit depth must be 8, 16, 24 or 32.")
    };

    private static string ValidateOptions(YtdlpDownloadOptions options)
    {
        var format = YtdlpParser.ValidateOutputFormat(options.Format);
        options.Network.Validate();
        options.BrowserSession?.Validate();
        if (options.Mp3Bitrate is not (64 or 96 or 128 or 192 or 256 or 320))
            throw new ArgumentOutOfRangeException(nameof(options), "Unsupported MP3 bitrate.");
        _ = WavCodec(options.WavBitDepth);
        if (options.SaveToDisk && string.IsNullOrWhiteSpace(options.OutputDirectory))
            throw new ArgumentException("An output directory is required when SaveToDisk is enabled.", nameof(options));
        if (options.MaxDuration is { } duration && (duration <= TimeSpan.Zero || duration > TimeSpan.FromHours(24)))
            throw new ArgumentOutOfRangeException(nameof(options), "Maximum duration must be positive and at most 24 hours.");
        if (options.MaxFileSizeBytes is { } size && (size <= 0 || size > 16L * 1024 * 1024 * 1024))
            throw new ArgumentOutOfRangeException(nameof(options), "Maximum file size must be positive and at most 16 GiB.");
        return format;
    }

    private static string ValidateVideo(YtdlpVideo video)
    {
        var uri = YtdlpParser.ValidateYoutubeUrl(video.Url);
        var expected = "https://www.youtube.com/watch?v=" + video.Id;
        if (string.IsNullOrEmpty(video.Id) || uri.AbsoluteUri.Split('&')[0] != expected)
            throw new ArgumentException("The video ID must match a YouTube video URL; playlist-only URLs cannot be downloaded.", nameof(video));
        return expected;
    }

    private static string ValidateDownloadedPath(string output, string job, string format, long? maximumSize)
    {
        var marked = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault(line => line.StartsWith(OutputPathPrefix, StringComparison.Ordinal));
        var printed = marked?[OutputPathPrefix.Length..].Trim();
        if (string.IsNullOrWhiteSpace(printed))
            throw new InvalidDataException("yt-dlp did not report an audio output path.");
        var path = Path.GetFullPath(printed, job);
        if (!string.Equals(Path.GetDirectoryName(path), job, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetExtension(path), "." + format, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("yt-dlp reported an unexpected output path or audio format.");
        var file = new FileInfo(path);
        if (!file.Exists || file.Length == 0 || (file.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("The reported audio output is missing, empty, or a symbolic link.");
        if (maximumSize is { } limit && file.Length > limit)
            throw new InvalidDataException("The converted audio exceeds the configured file size limit.");
        return path;
    }

    private static async Task<string> SaveAsync(string source, string outputDirectory, YtdlpVideo video,
        string format, CancellationToken token)
    {
        Directory.CreateDirectory(outputDirectory);
        var name = $"YouTube_{SafeTitle(video.Title)}_{video.Id}_{Guid.NewGuid():N}.{format}";
        var destination = Path.Combine(outputDirectory, name);
        var staging = Path.Combine(outputDirectory, ".ytdlp-" + Guid.NewGuid().ToString("N") + ".partial");
        try
        {
            await CopyFileAsync(source, staging, token);
            token.ThrowIfCancellationRequested();
            File.Move(staging, destination, overwrite: false);
            return destination;
        }
        finally
        {
            try { File.Delete(staging); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task CopyFileAsync(string source, string destination, CancellationToken token)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            81920, FileOptions.Asynchronous);
        await input.CopyToAsync(output, token);
        await output.FlushAsync(token);
    }

    private static string SafeTitle(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string((title ?? string.Empty).Take(80)
            .Select(c => char.IsControl(c) || invalid.Contains(c) ? '_' : c).ToArray()).Trim(' ', '.');
        return string.IsNullOrWhiteSpace(safe) ? "audio" : safe;
    }

    private static string CreateJobDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ModularAudience", "ytdlp", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private IProgress<string> CreateProgress(IProgress<string>? progress) => new CallbackProgress(message =>
    {
        progress?.Report(message);
        ProgressChanged?.Invoke(this, message);
    });

    private IProgress<string> CreateDownloadProgress(IProgress<string>? progress,
        IProgress<YtdlpDownloadProgress>? downloadProgress)
    {
        var diagnostics = CreateProgress(progress);
        return new CallbackProgress(message =>
        {
            if (message.StartsWith(OutputPathPrefix, StringComparison.Ordinal))
                return;
            if (message.StartsWith(YtdlpProgressParser.Prefix, StringComparison.Ordinal))
            {
                if (YtdlpProgressParser.TryParse(message, out var parsed))
                    downloadProgress?.Report(parsed!);
                return;
            }
            diagnostics.Report(message);
        });
    }

    private sealed class CallbackProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }
}
