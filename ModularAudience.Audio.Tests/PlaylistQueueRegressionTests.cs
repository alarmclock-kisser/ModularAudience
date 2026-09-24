using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Processing;
using NAudio.Wave;
using System.Diagnostics;
using System.Reflection;
using static ModularAudience.Audio.Processing.PlaylistEngine;

namespace ModularAudience.Audio.Tests
{
    [TestClass]
    [DoNotParallelize]
    public sealed class PlaylistQueueRegressionTests
    {
        private const string Head = "current-original.wav";
        private static readonly TimeSpan WorkerTimeout = TimeSpan.FromSeconds(5);

        [TestMethod]
        public void EnqueueNextPublishesOneOrderedBatchAfterCurrentHead()
        {
            using PlaylistEngine engine = CreateRunningEngine();
            engine.EnqueueLast(["existing-next.wav"]);
            PlaylistQueueSnapshot before = engine.GetQueueSnapshot();
            List<string[]> publishedQueues = [];
            engine.TrackChanged += () => publishedQueues.Add(engine.GetQueueSnapshot().FilePaths.ToArray());

            int inserted = engine.EnqueueNext(["first.wav", " ", "second.wav", "third.wav"]);

            Assert.AreEqual(3, inserted);
            Assert.AreEqual(1, publishedQueues.Count, "Observers must see one complete batch, not intermediate queues.");
            CollectionAssert.AreEqual(new[] { Head, "first.wav", "second.wav", "third.wav", "existing-next.wav" }, publishedQueues[0]);
            CollectionAssert.AreEqual(new[] { Head, "existing-next.wav" }, before.FilePaths.ToArray(), "Snapshots must stay detached.");
            Assert.AreEqual("first.wav", engine.GetQueueSnapshot().NextPath);
            Assert.AreEqual(Head, engine.OriginalCurrentPath);
        }

        [TestMethod]
        public void EnqueueNextRetainsPausedHeadAndDoesNotResume()
        {
            using PlaylistEngine engine = CreateRunningEngine();
            engine.EnqueueLast(["existing-next.wav"]);
            engine.Pause();

            engine.EnqueueNext(["first.wav", "second.wav"]);

            AssertQueue(engine, Head, "first.wav", "second.wav", "existing-next.wav");
            Assert.IsTrue(engine.IsPaused);
            Assert.IsFalse(engine.IsPlaying);
            Assert.AreEqual(Head, engine.OriginalCurrentPath);
            Assert.IsNull(GetField(engine, "_runLoopTask"));
        }

        [TestMethod]
        public void EnqueueLastAppendsAndIdleQueueOperationsNeverStartPlayback()
        {
            using PlaylistEngine engine = new();
            Assert.AreEqual(1, engine.EnqueueLast(["existing.wav"]));
            Assert.AreEqual(2, engine.EnqueueNext(["first.wav", "second.wav"]));
            Assert.AreEqual(2, engine.EnqueueLast(["last-a.wav", "last-b.wav"]));

            AssertQueue(engine, "first.wav", "second.wav", "existing.wav", "last-a.wav", "last-b.wav");
            Assert.IsFalse(engine.IsPlaying);
            Assert.IsFalse(engine.IsPaused);
            Assert.IsNull(engine.CurrentPath);
            Assert.IsNull(GetField(engine, "_cts"));
            Assert.IsNull(GetField(engine, "_runLoopTask"));
        }

        [TestMethod]
        public void AutoEnqueueCannotRemoveCurrentOrReuseActiveOverlapAudio()
        {
            using AudioTestScope scope = new();
            using PlaylistEngine engine = CreateRunningEngine();
            PreparedPlaylistTrack current = Prepared(scope.Create([]), Head);
            PreparedPlaylistTrack overlap = Prepared(scope.Create([]), "overlap.wav");
            Dictionary<string, PreparedPlaylistTrack> cache = Cache(engine);
            Dictionary<Guid, PreparedPlaylistTrack> active = Active(engine);
            cache[Path.GetFullPath(Head)] = current;
            cache[Path.GetFullPath("alias.wav")] = Prepared(overlap.Audio, "alias.wav");
            active[overlap.Audio.Id] = overlap;
            engine.EnqueueLast(["user-next.wav"]);
            int notifications = 0;
            engine.TrackChanged += () => notifications++;
            try
            {
                Assert.IsFalse(engine.AutoEnqueuePreparedNext(Head.ToUpperInvariant()));
                Assert.IsFalse(engine.AutoEnqueuePreparedNext("alias.wav"), "An active audio must not be reused under another path.");
                Assert.IsFalse(engine.AutoEnqueuePreparedNext(), "Automatic selection must reject the same unsafe candidates.");
                AssertQueue(engine, Head, "user-next.wav");
                Assert.AreSame(overlap.Audio, engine.ActiveAudioObjs.Single());
                Assert.AreEqual(2, cache.Count, "Rejected candidates must not be removed from the cache.");
                Assert.AreEqual(0, notifications);
            }
            finally
            {
                cache.Clear();
                active.Clear();
            }
        }

        [TestMethod]
        public void AutoEnqueuePromotesPreparedNextOnceWithoutRemovingHeadOrStartingAudio()
        {
            using AudioTestScope scope = new();
            using PlaylistEngine engine = CreateRunningEngine();
            PreparedPlaylistTrack next = Prepared(scope.Create([]), "prepared.wav");
            Dictionary<string, PreparedPlaylistTrack> cache = Cache(engine);
            cache[Path.GetFullPath(next.OriginalPath)] = next;
            SetField(engine, "_cts", new CancellationTokenSource());
            engine.EnqueueLast(["other.wav", "prepared.wav", "PREPARED.wav"]);
            try
            {
                Assert.IsTrue(engine.AutoEnqueuePreparedNext());

                AssertQueue(engine, Head, "prepared.wav", "other.wav");
                Assert.IsNotNull(GetField(engine, "_queuedPreparedStart"), "A safe next track should schedule a handoff.");
                Assert.IsFalse(next.Audio.Playing, "Promotion itself must not start audio.");
                Assert.IsNull(GetField(engine, "_runLoopTask"));
            }
            finally
            {
                cache.Clear();
            }
        }

        [TestMethod]
        public Task DelayedAutoProviderCannotOverrideUserNext() => WithDelayedProviderAsync(
            engine => engine.EnqueueNext(["user-next.wav"]),
            (engine, _) => AssertQueue(engine, Head, "user-next.wav"));

        [TestMethod]
        public Task DelayedAutoProviderCannotEnqueueAfterPause() => WithDelayedProviderAsync(
            engine => engine.Pause(),
            (engine, _) =>
            {
                AssertQueue(engine, Head);
                Assert.IsTrue(engine.IsPaused);
                Assert.IsFalse(engine.IsPlaying);
            });

        [TestMethod]
        public Task DelayedAutoProviderCannotRepopulateClearedQueue() => WithDelayedProviderAsync(
            engine => engine.Clear(),
            (engine, _) =>
            {
                AssertQueue(engine);
                Assert.IsFalse(engine.IsPlaying);
                Assert.IsFalse(engine.IsPaused);
                Assert.IsNull(engine.OriginalCurrentPath);
            });

        [TestMethod]
        public Task DelayedAutoProviderAddsSuccessorWhenStateIsUnchanged() => WithDelayedProviderAsync(
            _ => { },
            (engine, candidate) =>
            {
                AssertQueue(engine, Head, candidate);
                Assert.AreEqual(candidate, engine.GetQueueSnapshot().NextPath);
                Assert.IsNull(GetField(engine, "_runLoopTask"));
            });

        [TestMethod]
        public async Task ExplicitPlayAfterClearWaitsForPreviousLoopAndStartsOnlyOnce()
        {
            using PlaylistEngine engine = CreateRunningEngine();
            TaskCompletionSource previousLoop = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            string path = Path.GetTempFileName();
            int preparations = 0;
            SetField(engine, "_runLoopTask", previousLoop.Task);
            engine.BeforeTrackPlay = async (_, ct) =>
            {
                Interlocked.Increment(ref preparations);
                entered.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
                return null;
            };
            try
            {
                engine.Clear();
                engine.EnqueueLast([path]);
                Assert.IsNull(GetField(engine, "_deferredPlayTask"), "Enqueue must not request playback.");
                engine.Play();
                Task deferred = (Task)GetField(engine, "_deferredPlayTask")!;
                Assert.IsNotNull(deferred);
                engine.Play();
                Assert.AreSame(deferred, GetField(engine, "_deferredPlayTask"));
                Assert.AreSame(previousLoop.Task, GetField(engine, "_runLoopTask"));
                Assert.IsNull(GetField(engine, "_cts"), "No new loop may start while the previous one is unwinding.");

                previousLoop.SetResult();
                await deferred.WaitAsync(WorkerTimeout);
                await entered.Task.WaitAsync(WorkerTimeout);
                Assert.AreEqual(1, Volatile.Read(ref preparations));
                Assert.AreNotSame(previousLoop.Task, GetField(engine, "_runLoopTask"));
                Assert.IsTrue(engine.IsPlaying);
            }
            finally
            {
                engine.Clear();
                previousLoop.TrySetResult();
                if (GetField(engine, "_deferredPlayTask") is Task deferred) { await deferred.WaitAsync(WorkerTimeout); }
                try { await ((Task)GetField(engine, "_runLoopTask")!).WaitAsync(WorkerTimeout); }
                catch (OperationCanceledException) { }
                File.Delete(path);
            }
        }

        [TestMethod]
        public async Task DeferredExplicitPlayIsCancelledByPauseClearOrDispose()
        {
            foreach (string interruption in new[] { "Pause", "Clear", "Dispose" })
            {
                using PlaylistEngine engine = CreateRunningEngine();
                TaskCompletionSource previousLoop = new(TaskCreationOptions.RunContinuationsAsynchronously);
                SetField(engine, "_runLoopTask", previousLoop.Task);
                engine.Clear();
                engine.EnqueueLast(["pending.wav"]);
                engine.Play();
                Task deferred = (Task)GetField(engine, "_deferredPlayTask")!;
                Assert.IsNotNull(deferred);
                try
                {
                    if (interruption == "Pause") { engine.Pause(); }
                    else if (interruption == "Clear")
                    {
                        engine.Clear();
                        engine.EnqueueLast(["replacement.wav"]);
                    }
                    else { engine.Dispose(); }

                    previousLoop.SetResult();
                    await deferred.WaitAsync(WorkerTimeout);
                    Assert.AreSame(previousLoop.Task, GetField(engine, "_runLoopTask"), interruption);
                    Assert.IsNull(GetField(engine, "_cts"), interruption);
                    if (interruption == "Pause") { Assert.IsTrue(engine.IsPaused); }
                    if (interruption == "Clear") { AssertQueue(engine, "replacement.wav"); }
                }
                finally
                {
                    engine.Clear();
                    previousLoop.TrySetResult();
                    await deferred.WaitAsync(WorkerTimeout);
                }
            }
        }

        [TestMethod]
        public async Task ExplicitQueuedRepeatPreparesDistinctAudioAndSurvivesPreviousOccurrenceBan()
        {
            using AudioTestScope scope = new();
            using PlaylistEngine engine = CreateRunningEngine();
            string original = Path.Combine(Path.GetTempPath(), $"playlist-repeat-{Guid.NewGuid():N}.wav");
            string firstTemp = Path.ChangeExtension(original, ".first.wav");
            string repeatTemp = Path.ChangeExtension(original, ".repeat.wav");
            PreparedPlaylistTrack first = new()
            {
                Audio = scope.Create([]),
                OriginalPath = original,
                PlayPath = firstTemp,
                TempPath = firstTemp
            };
            PreparedPlaylistTrack current = Prepared(scope.Create([]), Head);
            try
            {
                foreach (string path in new[] { original, firstTemp, repeatTemp })
                {
                    using WaveFileWriter writer = new(path, new WaveFormat(8000, 16, 1));
                    writer.Write(new byte[320], 0, 320);
                }
                engine.EnqueueLast([original]);
                Active(engine)[first.Audio.Id] = first;
                Active(engine)[current.Audio.Id] = current;
                SetField(engine, "_primaryAudioObj", current.Audio);
                SetField(engine, "_secondaryAudioObj", first.Audio);
                Cache(engine)[original] = first;
                engine.BeforeTrackPlay = (_, _) => Task.FromResult<string?>(repeatTemp);

                Assert.IsFalse(await ((Task<bool>)Invoke(engine, "TryStartPreparedAsync", first, current,
                    1.0f, CancellationToken.None, null)!).WaitAsync(WorkerTimeout), "Active audio identity must still be rejected.");
                PreparedPlaylistTrack? repeat = await ((Task<PreparedPlaylistTrack?>)Invoke(engine,
                    "PrepareTrackAsync", original, CancellationToken.None)!).WaitAsync(WorkerTimeout);
                Assert.IsNotNull(repeat);
                scope.Own([repeat.Audio]);
                Assert.AreNotSame(first.Audio, repeat.Audio);
                Assert.IsFalse(repeat.Audio.Playing);
                Assert.IsFalse(repeat.Audio.Paused);
                Assert.AreSame(first, Active(engine)[first.Audio.Id], "The fading occurrence must keep its ownership.");
                Assert.AreSame(repeat, Cache(engine)[original]);

                Invoke(engine, "UntrackPrepared", first, "fade-out done", CancellationToken.None);
                SetField(engine, "_secondaryAudioObj", null);
                Assert.AreSame(repeat, Cache(engine)[original], "Untracking the first occurrence must preserve the repeat cache.");
                Assert.IsTrue(File.Exists(firstTemp));
                Assert.IsTrue(File.Exists(repeatTemp));
                Assert.AreSame(repeat, await ((Task<PreparedPlaylistTrack?>)Invoke(engine,
                    "PrepareTrackAsync", original, CancellationToken.None)!).WaitAsync(WorkerTimeout));
                Assert.IsFalse(engine.AutoEnqueuePreparedNext(original), "The ban must still apply to automatic selection.");
                Assert.IsTrue((bool)Invoke(engine, "CanCommitPreparedStartLocked", repeat, current,
                    CancellationToken.None, null)!, "An explicit queued repeat must not be banned.");
                engine.Pause();
                Assert.IsFalse((bool)Invoke(engine, "CanCommitPreparedStartLocked", repeat, current,
                    CancellationToken.None, null)!, "Explicit repeats must still respect pause.");
            }
            finally
            {
                Cache(engine).Clear();
                Active(engine).Clear();
                SetField(engine, "_primaryAudioObj", null);
                SetField(engine, "_secondaryAudioObj", null);
                File.Delete(original);
                File.Delete(firstTemp);
                File.Delete(repeatTemp);
            }
        }

        private static PlaylistEngine CreateRunningEngine()
        {
            PlaylistEngine engine = new();
            engine.EnqueueLast([Head]);
            // Reserve only run-loop bookkeeping; never call Start or open an audio device.
            typeof(PlaylistEngine).GetProperty(nameof(PlaylistEngine.IsPlaying))!.SetValue(engine, true);
            typeof(PlaylistEngine).GetProperty(nameof(PlaylistEngine.OriginalCurrentPath))!.SetValue(engine, Head);
            typeof(PlaylistEngine).GetProperty(nameof(PlaylistEngine.CurrentPath))!.SetValue(engine, "prepared-current.wav");
            lock (GetField(engine, "_lock")!)
            {
                Invoke(engine, "ReserveQueueHeadLocked", Head);
            }
            return engine;
        }

        private static async Task WithDelayedProviderAsync(Action<PlaylistEngine> interrupt, Action<PlaylistEngine, string> verify)
        {
            using PlaylistEngine engine = CreateRunningEngine();
            using ManualResetEventSlim release = new(false);
            TaskCompletionSource<string?> entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            string candidate = Path.Combine(Path.GetTempPath(), $"playlist-regression-{Guid.NewGuid():N}.wav");
            int calls = 0;
            try
            {
                using (WaveFileWriter writer = new(candidate, new WaveFormat(8000, 16, 1)))
                {
                    writer.Write(new byte[32], 0, 32);
                }
                engine.AutoEnqueuePathProvider = original =>
                {
                    Interlocked.Increment(ref calls);
                    entered.TrySetResult(original);
                    if (!release.Wait(WorkerTimeout)) { throw new TimeoutException("Provider was not released."); }
                    return candidate;
                };
                Invoke(engine, "RequestAutoEnqueueSuccessor", CancellationToken.None);
                Assert.AreEqual(Head, await entered.Task.WaitAsync(WorkerTimeout), "Provider must receive the original, not prepared path.");
                Invoke(engine, "RequestAutoEnqueueSuccessor", CancellationToken.None);
                interrupt(engine);
                release.Set();
                await WaitForProviderAsync(engine);
                Assert.AreEqual(1, Volatile.Read(ref calls), "Only one provider request may be in flight.");
                verify(engine, candidate);
            }
            finally
            {
                release.Set();
                try { await WaitForProviderAsync(engine); }
                finally { File.Delete(candidate); }
            }
        }

        private static async Task WaitForProviderAsync(PlaylistEngine engine)
        {
            Stopwatch elapsed = Stopwatch.StartNew();
            while (true)
            {
                lock (GetField(engine, "_lock")!)
                {
                    if (!(bool)GetField(engine, "_autoEnqueuePending")!) { return; }
                }
                Assert.IsTrue(elapsed.Elapsed < WorkerTimeout, "Auto-enqueue worker did not finish.");
                await Task.Delay(10);
            }
        }

        private static PreparedPlaylistTrack Prepared(AudioObj audio, string path) => new()
        {
            Audio = audio,
            OriginalPath = path,
            PlayPath = path
        };

        private static Dictionary<string, PreparedPlaylistTrack> Cache(PlaylistEngine engine) =>
            (Dictionary<string, PreparedPlaylistTrack>)GetField(engine, "_preparedByPath")!;

        private static Dictionary<Guid, PreparedPlaylistTrack> Active(PlaylistEngine engine) =>
            (Dictionary<Guid, PreparedPlaylistTrack>)GetField(engine, "_activePreparedTracks")!;

        private static void AssertQueue(PlaylistEngine engine, params string[] expected) =>
            CollectionAssert.AreEqual(expected, engine.GetQueueSnapshot().FilePaths.ToArray());

        private static object? GetField(PlaylistEngine engine, string name) =>
            typeof(PlaylistEngine).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine);

        private static void SetField(PlaylistEngine engine, string name, object? value) =>
            typeof(PlaylistEngine).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(engine, value);

        private static object? Invoke(PlaylistEngine engine, string name, params object?[] args) =>
            typeof(PlaylistEngine).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(engine, args);
    }
}
