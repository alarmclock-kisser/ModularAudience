using System.Collections.Concurrent;
using System.Globalization;

namespace ModularAudience.Ytdlp;

public sealed partial class YtdlpClient
{
    private readonly ConcurrentDictionary<string, DateOnly> uploadDates = new(StringComparer.Ordinal);

    public async Task<DateOnly?> GetUploadDateAsync(YtdlpVideo video, CancellationToken token)
    {
        string url = ValidateVideo(video);
        if (video.UploadDate is { } known) return known;
        if (uploadDates.TryGetValue(video.Id, out var cached)) return cached;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(12));
        var arguments = CommonArguments();
        MetadataSession?.AddArguments(arguments);
        arguments.AddRange(["--no-playlist", "--skip-download", "--ignore-no-formats-error",
            "--extractor-args", "youtube:skip=hls,dash,translated_subs;player_skip=js,configs",
            "--socket-timeout", "5", "--retries", "0", "--extractor-retries", "0",
            "--print", "%(upload_date)s", "--", url]);
        var job = CreateJobDirectory();
        try
        {
            string output = await YtdlpProcess.RunAsync(manager.YtdlpPath, arguments, job,
                new Progress<string>(), false, deadline.Token).ConfigureAwait(false);
            var date = ParseUploadDateOutput(output);
            if (date is { } value) uploadDates[video.Id] = value;
            return date;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException("The upload-date lookup exceeded its 12-second time limit.");
        }
        finally { YtdlpBinaryManager.DeleteStaging(job); }
    }

    public static DateOnly? ParseUploadDateOutput(string output)
    {
        foreach (string line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            if (DateOnly.TryParseExact(line.Trim(), "yyyyMMdd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date)) return date;
        return null;
    }
}
