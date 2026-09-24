using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ModularAudience.Audio.Processing
{
    public sealed partial class PlaylistEngine
    {
        /// <summary>
        /// Optional worker-thread callback, invoked outside the engine lock when a reserved head
        /// has no successor. Receives the last original track path (not its prepared/temp path).
        /// Return one existing audio path, or null when disabled/unavailable. Do not mutate the queue
        /// or access controls directly. Results are rechecked against playback state and explicit next.
        /// At most one request runs at a time; null/invalid results are not retried for the same head
        /// until resume or reassignment of this property. Set null to disable pending results too.
        /// </summary>
        public Func<string?, string?>? AutoEnqueuePathProvider
        {
            get { lock (this._lock) { return this._autoEnqueuePathProvider; } }
            set
            {
                lock (this._lock)
                {
                    this._autoEnqueuePathProvider = value;
                    this._autoEnqueueAttemptVersion = -1;
                }
            }
        }

        private Func<string?, string?>? _autoEnqueuePathProvider;
        private PreparedPlaylistTrack? _startingPreparedTrack;
        private long _playbackStateVersion;
        private long _queueHeadVersion;
        private long _autoEnqueueAttemptVersion = -1;
        private bool _autoEnqueuePending;

        private void ReserveQueueHeadLocked(string? path)
        {
            if (!QueuePathsEqual(this._queueCurrentPath, path))
            {
                this._queueHeadVersion++;
            }
            this._queueCurrentPath = path;
            this.InvalidateQueuedPreparedStartLocked();
        }

        private bool IsReservedHeadLocked(string path)
        {
            return this.FilePaths.Count > 0 && QueuePathsEqual(this.FilePaths[0], path) &&
                QueuePathsEqual(this._queueCurrentPath, path);
        }

        private bool CanCommitPreparedStartLocked(PreparedPlaylistTrack prepared,
            PreparedPlaylistTrack? current, CancellationToken ct, QueuedPreparedStart? request)
        {
            if (ct.IsCancellationRequested || this._disposed || this.IsPaused ||
                !this.IsPlaying || this._skipRequested)
            {
                return false;
            }

            if (current == null)
            {
                return this.IsReservedHeadLocked(prepared.OriginalPath);
            }

            return !SameAudio(prepared.Audio, current.Audio) &&
                SameAudio(current.Audio, this._primaryAudioObj) &&
                this.IsReservedHeadLocked(current.OriginalPath) &&
                QueuePathsEqual(prepared.OriginalPath, this.GetNextQueuePathLocked()) &&
                (request == null || ReferenceEquals(request, this._queuedPreparedStart));
        }

        private async Task<bool> TryStartPreparedAsync(PreparedPlaylistTrack prepared,
            PreparedPlaylistTrack? current, float volume, CancellationToken ct,
            QueuedPreparedStart? request = null)
        {
            Task startTask;
            long stateVersion;
            lock (this._lock)
            {
                if (!this.CanCommitPreparedStartLocked(prepared, current, ct, request) ||
                    this._startingPreparedTrack != null || prepared.Audio.Playing || prepared.Audio.Paused ||
                    SameAudio(prepared.Audio, this._primaryAudioObj) ||
                    SameAudio(prepared.Audio, this._secondaryAudioObj) ||
                    this._activePreparedTracks.Values.Any(active => SameAudio(prepared.Audio, active.Audio)) ||
                    !this._preparedByPath.TryGetValue(NormalizePathForKey(prepared.OriginalPath), out var cached) ||
                    !ReferenceEquals(cached, prepared))
                {
                    return false;
                }

                this._startingPreparedTrack = prepared;
                stateVersion = this._playbackStateVersion;
                // Initialize silently: pause, cancellation or queue edits during the await must not
                // make an obsolete candidate audible or publish it as the current track.
                startTask = prepared.Audio.PlayAsync(ct, initialVolume: 0.0f);
            }

            bool committed = false;
            try
            {
                await startTask.ConfigureAwait(false);
                lock (this._lock)
                {
                    if (stateVersion == this._playbackStateVersion &&
                        this.CanCommitPreparedStartLocked(prepared, current, ct, request) &&
                        prepared.Audio.Playing)
                    {
                        if (current != null)
                        {
                            this.FilePaths.RemoveAt(0);
                            this._previousPath = current.OriginalPath;
                        }
                        this._preparedByPath.Remove(NormalizePathForKey(prepared.OriginalPath));
                        this._activePreparedTracks[prepared.Audio.Id] = prepared;
                        this.ReserveQueueHeadLocked(prepared.OriginalPath);
                        this.ApplyCurrentTrackState(prepared);
                        this._startingPreparedTrack = null;
                        prepared.Audio.Volume = 100f;
                        prepared.Audio.SetPlaybackVolume(volume);
                        committed = true;
                    }
                }
                return committed;
            }
            finally
            {
                if (!committed)
                {
                    try { await prepared.Audio.StopAsync().ConfigureAwait(false); } catch { }
                    lock (this._lock)
                    {
                        if (ReferenceEquals(this._startingPreparedTrack, prepared))
                        {
                            this._startingPreparedTrack = null;
                        }
                    }
                    this.DiscardPreparedIfUnused(prepared);
                }
            }
        }

        private void ReleaseQueueHeadLocked(string originalPath)
        {
            if (!this.IsReservedHeadLocked(originalPath))
            {
                return;
            }

            this.FilePaths.RemoveAt(0);
            this._previousPath = originalPath;
            this.ClearCurrentTrackState();
            this.ReserveQueueHeadLocked(this.FilePaths.FirstOrDefault());
        }

        private async Task DiscardPreparationAsync(Task<PreparedPlaylistTrack?> task)
        {
            try
            {
                PreparedPlaylistTrack? prepared = await task.ConfigureAwait(false);
                if (prepared != null)
                {
                    this.DiscardPreparedIfUnused(prepared);
                }
            }
            catch { }
        }

        private async Task FadePreparedAsync(PreparedPlaylistTrack prepared, double seconds,
            bool fadeIn, CancellationToken ct)
        {
            double elapsed = 0.0;
            DateTime previous = DateTime.UtcNow;
            try
            {
                while (!ct.IsCancellationRequested && !this._disposed)
                {
                    DateTime now = DateTime.UtcNow;
                    lock (this._lock)
                    {
                        if (!this._activePreparedTracks.ContainsKey(prepared.Audio.Id) ||
                            (fadeIn && !SameAudio(prepared.Audio, this._primaryAudioObj)))
                        {
                            break;
                        }
                        if (!this.IsPaused && !prepared.Audio.Paused)
                        {
                            elapsed += (now - previous).TotalSeconds;
                            double t = Math.Clamp(elapsed / Math.Max(0.001, seconds), 0.0, 1.0);
                            prepared.Audio.SetPlaybackVolume((float)(fadeIn
                                ? Math.Sin(t * Math.PI * 0.5) : Math.Cos(t * Math.PI * 0.5)));
                            if (t >= 1.0) { break; }
                        }
                    }
                    previous = now;
                    await Task.Delay(25, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { LogCollection.Log($"Playlist fade failed: {ex.Message}"); }
            finally
            {
                if (!fadeIn)
                {
                    bool release;
                    lock (this._lock)
                    {
                        release = !SameAudio(prepared.Audio, this._primaryAudioObj) &&
                            !SameAudio(prepared.Audio, this._startingPreparedTrack?.Audio);
                        if (release)
                        {
                            this.UntrackPrepared(prepared, "fade-out done", ct);
                        }
                    }
                    if (release)
                    {
                        try { await prepared.Audio.StopAsync().ConfigureAwait(false); } catch { }
                        this.DiscardPreparedIfUnused(prepared);
                    }
                }
            }
        }

        private void DiscardPreparedIfUnused(PreparedPlaylistTrack prepared)
        {
            lock (this._lock)
            {
                if (SameAudio(prepared.Audio, this._startingPreparedTrack?.Audio) ||
                    prepared.Audio.Playing || prepared.Audio.Paused ||
                    this.IsProtectedQueuePathLocked(prepared.OriginalPath) ||
                    this.IsProtectedQueuePathLocked(prepared.PlayPath) ||
                    this._activePreparedTracks.Values.Any(active => SameAudio(prepared.Audio, active.Audio)) ||
                    this.FilePaths.Any(path => QueuePathsEqual(path, prepared.OriginalPath) ||
                        QueuePathsEqual(path, prepared.PlayPath)))
                {
                    return;
                }

                string key = NormalizePathForKey(prepared.OriginalPath);
                if (this._preparedByPath.TryGetValue(key, out var cached) && ReferenceEquals(cached, prepared))
                {
                    this._preparedByPath.Remove(key);
                }
            }
            // Start validation requires cache ownership, which was detached under the lock.
            try { prepared.Audio.Dispose(); } catch { }
            this.DeleteTempFile(prepared.TempPath);
        }

        private void RequestAutoEnqueueSuccessor(CancellationToken ct)
        {
            Func<string?, string?>? provider;
            string? originalPath;
            long headVersion;
            long stateVersion;
            lock (this._lock)
            {
                provider = this.AutoEnqueuePathProvider;
                if (provider == null || this._autoEnqueuePending || this.IsPaused || !this.IsPlaying ||
                    this._disposed || ct.IsCancellationRequested || this._skipRequested ||
                    this._queueCurrentPath == null || this.GetNextQueuePathLocked() != null ||
                    this._autoEnqueueAttemptVersion == this._queueHeadVersion)
                {
                    return;
                }
                headVersion = this._queueHeadVersion;
                stateVersion = this._playbackStateVersion;
                this._autoEnqueueAttemptVersion = headVersion;
                this._autoEnqueuePending = true;
                originalPath = this._queueCurrentPath;
            }

            _ = Task.Run(() =>
            {
                bool added = false;
                try
                {
                    string? path = provider(originalPath);
                    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    {
                        return;
                    }
                    lock (this._lock)
                    {
                        if (ct.IsCancellationRequested || this._disposed || this.IsPaused || !this.IsPlaying ||
                            this._skipRequested || stateVersion != this._playbackStateVersion ||
                            !ReferenceEquals(provider, this._autoEnqueuePathProvider) ||
                            headVersion != this._queueHeadVersion || this.GetNextQueuePathLocked() != null ||
                            this.IsProtectedQueuePathLocked(path) || this._banlist.Contains(NormalizePathForKey(path)))
                        {
                            return;
                        }
                        this.FilePaths.Add(path);
                        this.InvalidateQueuedPreparedStartLocked();
                        added = true;
                    }
                }
                catch (Exception ex)
                {
                    LogCollection.Log($"Playlist auto-enqueue failed: {ex.Message}");
                }
                finally
                {
                    lock (this._lock) { this._autoEnqueuePending = false; }
                }
                if (added)
                {
                    TrackChanged?.Invoke();
                }
            });
        }
    }
}
