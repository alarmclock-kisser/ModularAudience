namespace ModularAudience.Ytdlp;

/// <summary>One additional-attempt budget shared by the phases of an operation.</summary>
public sealed class YtdlpRetryBudget
{
    public const int MaximumRetries = 10;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;
    public int MaxRetries { get; }
    public int RetriesUsed { get; private set; }

    public YtdlpRetryBudget(int maxRetries, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        if (maxRetries is < 0 or > MaximumRetries) throw new ArgumentOutOfRangeException(nameof(maxRetries));
        this.MaxRetries = maxRetries;
        this.delay = delay ?? ((pause, token) => Task.Delay(pause, token));
    }

    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken token,
        Action<int, Exception>? retrying = null, Func<Exception, bool>? canRetry = null)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try { return await action(token); }
            catch (Exception error) when (error is not OperationCanceledException && !token.IsCancellationRequested
                && this.RetriesUsed < this.MaxRetries && (canRetry?.Invoke(error) ?? true))
            {
                this.RetriesUsed++;
                retrying?.Invoke(this.RetriesUsed, error);
                token.ThrowIfCancellationRequested();
                TimeSpan pause = error is YtdlpProcessException { RetryNotBeforeUtc: { } deadline }
                    ? deadline - DateTimeOffset.UtcNow : TimeSpan.FromMilliseconds(500);
                if (pause > TimeSpan.Zero) await this.delay(pause, token);
            }
        }
    }

    public Task RunAsync(Func<CancellationToken, Task> action, CancellationToken token,
        Action<int, Exception>? retrying = null, Func<Exception, bool>? canRetry = null) =>
        RunAsync(async cancellation => { await action(cancellation); return true; }, token, retrying, canRetry);
}
