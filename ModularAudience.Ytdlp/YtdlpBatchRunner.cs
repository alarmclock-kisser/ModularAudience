using System.Runtime.ExceptionServices;

namespace ModularAudience.Ytdlp;

public static class YtdlpBatchRunner
{
    public const int MaximumParallelism = 8;

    /// <summary>
    /// Runs at most maxParallelism items concurrently on the caller's synchronization context.
    /// A worker slot covers the entire callback, including download, cooldown, import and cleanup,
    /// bounding temporary files when results are disposed before the callback returns.
    /// UI imports must use a caller-owned serial gate; asynchronous callbacks can interleave.
    /// </summary>
    public static Task RunAsync<T>(IReadOnlyList<T> items, int maxParallelism,
        Func<T, CancellationToken, Task> processItem, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(processItem);
        if (maxParallelism is < 1 or > MaximumParallelism)
            throw new ArgumentOutOfRangeException(nameof(maxParallelism), "Parallelism must be between 1 and 8.");
        return RunWorkersAsync(items, maxParallelism, processItem, cancellationToken);
    }

    private static async Task RunWorkersAsync<T>(IReadOnlyList<T> items, int maxParallelism,
        Func<T, CancellationToken, Task> processItem, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var state = new WorkerState();
        var count = items.Count;
        var workers = new Task[Math.Min(maxParallelism, count)];
        for (var index = 0; index < workers.Length; index++)
            workers[index] = RunWorkerAsync(items, count, processItem, cancellation, state);
        try
        {
            await Task.WhenAll(workers);
        }
        catch
        {
            state.Failure?.Throw();
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static async Task RunWorkerAsync<T>(IReadOnlyList<T> items, int count,
        Func<T, CancellationToken, Task> processItem, CancellationTokenSource cancellation, WorkerState state)
    {
        await Task.Yield();
        var token = cancellation.Token;
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var index = Interlocked.Increment(ref state.NextIndex);
                if (index >= count)
                    return;
                token.ThrowIfCancellationRequested();
                await processItem(items[(int)index], token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Interlocked.CompareExchange(ref state.Failure, ExceptionDispatchInfo.Capture(exception), null);
            await cancellation.CancelAsync();
            throw;
        }
    }

    private sealed class WorkerState
    {
        internal long NextIndex = -1;
        internal ExceptionDispatchInfo? Failure;
    }
}
