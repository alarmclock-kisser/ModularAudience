using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Ytdlp;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class YtdlpPipelineRetryTests
{
    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(4)]
    public async Task PipelineReleasesConversionSlotsWhileAllJobsCanWaitForTags(int limit)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var allWaiting = Gate();
        var finish = Gate();
        int network = 0, peak = 0, waiting = 0;
        var sync = new object();
        int total = limit + 4;
        Task run = YtdlpBatchRunner.RunPipelineQueueAsync(new Queue<int>(Enumerable.Range(0, total)), limit,
            async (_, release, token) =>
            {
                lock (sync) { network++; peak = Math.Max(peak, network); }
                await Task.Delay(20, token);
                lock (sync) network--;
                release();
                release(); // Releasing twice must be harmless.
                if (Interlocked.Increment(ref waiting) == total) allWaiting.SetResult();
                await finish.Task.WaitAsync(token);
            }, cancellation.Token);
        try
        {
            await allWaiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(total, waiting);
            Assert.IsFalse(run.IsCompleted, "The batch must still await post-download work.");
            Assert.IsTrue(peak <= limit);
            finish.SetResult();
            await run;
        }
        finally { cancellation.Cancel(); finish.TrySetResult(); await Drain(run); }
    }

    [TestMethod]
    public async Task PipelineAcceptsLateDownloadsWhileOnlyTagWaitersRemainAndAwaitsCancelledCleanup()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var changed = new SemaphoreSlim(0);
        var queue = new Queue<int>([0]);
        var firstWaiting = Gate(); var secondStarted = Gate(); var cleaning = Gate(); var releaseCleanup = Gate();
        int cleanups = 0;
        Task run = YtdlpBatchRunner.RunPipelineQueueAsync(queue, 1, async (item, release, token) =>
        {
            release();
            if (item == 0) firstWaiting.SetResult(); else secondStarted.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally
            {
                if (Interlocked.Increment(ref cleanups) == 2) cleaning.SetResult();
                await releaseCleanup.Task;
            }
        }, cancellation.Token, changed);
        try
        {
            await firstWaiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
            queue.Enqueue(1); changed.Release();
            await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            await cleaning.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsFalse(run.IsCompleted, "Cancellation cannot abandon temporary-file cleanup.");
            releaseCleanup.SetResult();
            await Assert.ThrowsAsync<OperationCanceledException>(() => run);
        }
        finally { cancellation.Cancel(); releaseCleanup.TrySetResult(); await Drain(run); }
    }

    [TestMethod]
    public async Task RetryBudgetIsSharedAcrossDownloadAndImportAndStopsAtTheConfiguredCount()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var budget = new YtdlpRetryBudget(2, (_, _) => Task.CompletedTask);
        int downloads = 0, imports = 0;
        var options = new object();
        object prepared = await budget.RunAsync<object>(_ => ++downloads == 1
            ? Task.FromException<object>(new IOException("download")) : Task.FromResult(options), cancellation.Token);
        Assert.AreSame(options, prepared);
        await Assert.ThrowsExactlyAsync<IOException>(() => budget.RunAsync(_ =>
        {
            imports++;
            return Task.FromException(new IOException("import"));
        }, cancellation.Token));
        Assert.AreEqual(2, downloads);
        Assert.AreEqual(2, imports);
        Assert.AreEqual(2, budget.RetriesUsed);
    }

    [TestMethod]
    public async Task RetryBudgetDoesNotRetryCancellationOrUnsafePostHandoffFailures()
    {
        var budget = new YtdlpRetryBudget(3, (_, _) => Task.CompletedTask);
        int attempts = 0;
        await Assert.ThrowsAsync<OperationCanceledException>(() => budget.RunAsync(_ =>
        { attempts++; return Task.FromException(new OperationCanceledException()); }, CancellationToken.None));
        Assert.AreEqual(1, attempts);
        await Assert.ThrowsExactlyAsync<IOException>(() => budget.RunAsync(_ =>
        { attempts++; return Task.FromException(new IOException("already handed off")); }, CancellationToken.None, canRetry: _ => false));
        Assert.AreEqual(2, attempts);
        Assert.AreEqual(0, budget.RetriesUsed);
    }

    private static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static async Task Drain(Task task)
    {
        try { await task.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (OperationCanceledException) { }
    }
}
