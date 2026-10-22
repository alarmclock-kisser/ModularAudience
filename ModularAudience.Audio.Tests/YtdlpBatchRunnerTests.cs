using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Ytdlp;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class YtdlpBatchRunnerTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    [TestMethod]
    public async Task RunAsync_ParallelLimitsOneAndTwo_ReachLimitWithoutExceedingItAcrossFourItems()
    {
        foreach (int limit in new[] { 1, 2 })
            await AssertConcurrencyAsync(limit);
    }

    [TestMethod]
    public async Task RunAsync_Cancellation_StopsQueuedItemsAndAwaitsActiveCleanup()
    {
        using var cancellation = new CancellationTokenSource();
        var started = Gate();
        var cleaning = Gate();
        var releaseCleanup = Gate();
        var work = Gate();
        int starts = 0, cleanups = 0, cleaned = 0;
        var run = YtdlpBatchRunner.RunAsync(new[] { 0, 1, 2, 3 }, 2, async (_, token) =>
        {
            if (Interlocked.Increment(ref starts) == 2) started.TrySetResult();
            try { await work.Task.WaitAsync(Deadline, token); }
            finally
            {
                if (Interlocked.Increment(ref cleanups) == 2) cleaning.TrySetResult();
                await releaseCleanup.Task.WaitAsync(Deadline);
                Interlocked.Increment(ref cleaned);
            }
        }, cancellation.Token);
        try
        {
            await started.Task.WaitAsync(Deadline);
            await cancellation.CancelAsync();
            await cleaning.Task.WaitAsync(Deadline);
            Assert.AreEqual(2, Volatile.Read(ref starts), "Queued items must not start after cancellation.");
            Assert.AreEqual(0, Volatile.Read(ref cleaned));
            Assert.IsFalse(run.IsCompleted, "The batch must await both active workers' finally blocks.");
            releaseCleanup.TrySetResult();
            var error = await Assert.ThrowsExactlyAsync<OperationCanceledException>(
                async () => await run.WaitAsync(Deadline));
            Assert.AreEqual(cancellation.Token, error.CancellationToken);
            Assert.AreEqual(2, Volatile.Read(ref cleaned));
            Assert.AreEqual(2, Volatile.Read(ref starts));
        }
        finally
        {
            releaseCleanup.TrySetResult();
            work.TrySetResult();
            await cancellation.CancelAsync();
            await DrainAsync(run);
        }
    }

    [TestMethod]
    public async Task RunAsync_WorkerFailure_CancelsPeerAndAwaitsCleanupBeforeRethrowingOriginal()
    {
        using var cancellation = new CancellationTokenSource();
        var peerStarted = Gate();
        var fail = Gate();
        var peerCleaning = Gate();
        var releaseCleanup = Gate();
        var work = Gate();
        var original = new InvalidOperationException("Original worker failure.");
        int starts = 0, cleaned = 0;
        bool peerCancelled = false;
        var run = YtdlpBatchRunner.RunAsync(new[] { 0, 1, 2, 3 }, 2, async (item, token) =>
        {
            Interlocked.Increment(ref starts);
            if (item == 0)
            {
                await fail.Task.WaitAsync(Deadline, token);
                throw original;
            }
            peerStarted.TrySetResult();
            try { await work.Task.WaitAsync(Deadline, token); }
            finally
            {
                peerCancelled = token.IsCancellationRequested;
                peerCleaning.TrySetResult();
                await releaseCleanup.Task.WaitAsync(Deadline);
                Interlocked.Increment(ref cleaned);
            }
        }, cancellation.Token);
        try
        {
            await peerStarted.Task.WaitAsync(Deadline);
            fail.TrySetResult();
            await peerCleaning.Task.WaitAsync(Deadline);
            Assert.IsTrue(peerCancelled, "Failure must cancel the active peer's token.");
            Assert.IsFalse(run.IsCompleted, "Failure must not escape while peer cleanup is held.");
            Assert.AreEqual(0, Volatile.Read(ref cleaned));
            Assert.AreEqual(2, Volatile.Read(ref starts));
            releaseCleanup.TrySetResult();
            var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                async () => await run.WaitAsync(Deadline));
            Assert.AreSame(original, error);
            Assert.AreEqual(1, Volatile.Read(ref cleaned));
            Assert.AreEqual(2, Volatile.Read(ref starts), "Queued items must remain unstarted after failure.");
        }
        finally
        {
            fail.TrySetResult();
            releaseCleanup.TrySetResult();
            work.TrySetResult();
            await cancellation.CancelAsync();
            await DrainAsync(run);
        }
    }

    [TestMethod]
    public void RunAsync_LimitsZeroAndNine_AreRejectedBeforeStartingWorkers()
    {
        foreach (int limit in new[] { 0, 9 })
        {
            int calls = 0;
            var error = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
                YtdlpBatchRunner.RunAsync(new[] { 0 }, limit, (_, _) =>
                {
                    calls++;
                    return Task.CompletedTask;
                }, CancellationToken.None));
            Assert.AreEqual("maxParallelism", error.ParamName);
            Assert.AreEqual(0, calls);
        }
    }

    private static async Task AssertConcurrencyAsync(int limit)
    {
        using var cancellation = new CancellationTokenSource();
        var started = Enumerable.Range(0, 4).Select(_ => Gate()).ToArray();
        var release = Enumerable.Range(0, 4).Select(_ => Gate()).ToArray();
        var visits = new int[4];
        var sync = new object();
        int active = 0, peak = 0, calls = 0;
        var run = YtdlpBatchRunner.RunAsync(new[] { 0, 1, 2, 3 }, limit, async (item, token) =>
        {
            lock (sync)
            {
                active++;
                peak = Math.Max(peak, active);
                calls++;
                visits[item]++;
            }
            started[item].TrySetResult();
            try { await release[item].Task.WaitAsync(Deadline, token); }
            finally { lock (sync) active--; }
        }, cancellation.Token);
        try
        {
            for (int first = 0; first < 4; first += limit)
            {
                await Task.WhenAll(started.Skip(first).Take(limit).Select(gate => gate.Task)).WaitAsync(Deadline);
                lock (sync)
                {
                    Assert.AreEqual(limit, active, "Workers must occupy every configured slot before release.");
                    Assert.AreEqual(first + limit, calls, "No queued item may start while all slots are held.");
                    Assert.AreEqual(limit, peak);
                }
                for (int index = first; index < first + limit; index++) release[index].TrySetResult();
            }
            await run.WaitAsync(Deadline);
            Assert.AreEqual(limit, peak);
            Assert.AreEqual(0, active);
            CollectionAssert.AreEqual(new[] { 1, 1, 1, 1 }, visits);
        }
        finally
        {
            await cancellation.CancelAsync();
            foreach (var gate in release) gate.TrySetResult();
            await DrainAsync(run);
        }
    }

    private static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task DrainAsync(Task run)
    {
        try { await run.WaitAsync(Deadline); }
        catch (OperationCanceledException) { }
        catch (InvalidOperationException) { }
    }
}
