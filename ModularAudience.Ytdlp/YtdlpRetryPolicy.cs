namespace ModularAudience.Ytdlp;

public static class YtdlpRetryPolicy
{
    public const string ProgressPrefix = "__MA_RETRY__:";

    public static Task<T> ExecuteAsync<T>(Func<int, CancellationToken, Task<T>> action,
        IProgress<string>? progress, CancellationToken token) =>
        ExecuteCoreAsync(action, progress, (delay, cancellation) => Task.Delay(delay, cancellation), token);

    internal static async Task<T> ExecuteCoreAsync<T>(Func<int, CancellationToken, Task<T>> action,
        IProgress<string>? progress, Func<TimeSpan, CancellationToken, Task> delay, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try { return await action(1, token); }
        catch (YtdlpProcessException error) when (CanRetry(error))
        {
            token.ThrowIfCancellationRequested();
            var pause = error.RetryAfter is { } requested && requested > TimeSpan.FromSeconds(2)
                ? requested : TimeSpan.FromSeconds(2);
            progress?.Report(ProgressPrefix + $"Transient network failure; retrying in {pause.TotalSeconds:0} seconds (attempt 2/2). Metadata and media URLs will be refreshed. Each attempt includes yt-dlp's bounded internal retries.");
            await delay(pause, token);
            token.ThrowIfCancellationRequested();
            return await action(2, token);
        }
    }

    public static bool CanRetry(YtdlpProcessException error) => error.ExitCode != 0 &&
        error.FailureKind == YtdlpFailureKind.TransientNetwork &&
        (error.RetryAfter is null || error.RetryAfter <= TimeSpan.FromSeconds(30));
}
