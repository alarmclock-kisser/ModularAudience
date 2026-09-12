using System;
using System.Collections.Generic;
using System.Linq;

namespace ModularAudience.Audio.Processing
{
    /// <summary>A detached queue and playback-state snapshot captured under the engine lock.</summary>
    public sealed record PlaylistQueueSnapshot(
        IReadOnlyList<string> FilePaths,
        string? OriginalCurrentPath,
        string? CurrentPath,
        string? NextPath,
        bool IsPlaying,
        bool IsPaused,
        TimeSpan CurrentPosition,
        TimeSpan CurrentDuration,
        float CurrentBpm,
        int CurrentChannels,
        int CurrentSampleRate,
        int CurrentBitDepth
    );

    public sealed partial class PlaylistEngine
    {
        /// <summary>
        /// Original queue head reserved by the run-loop, guarded by _lock. The run-loop must
        /// set this before awaiting head preparation, advance it with a handoff, and clear it
        /// when releasing the head or exiting. Queue APIs honor it even before playback starts.
        /// </summary>
        private string? _queueCurrentPath;

        /// <summary>Insert nonblank paths in input order after the protected head, without starting playback.</summary>
        public int EnqueueNext(IEnumerable<string> paths) => this.EnqueuePaths(paths, insertNext: true);

        /// <summary>Append nonblank paths in input order, without starting or resuming playback.</summary>
        public int EnqueueLast(IEnumerable<string> paths) => this.EnqueuePaths(paths, insertNext: false);

        /// <summary>
        /// Promote an eligible cached track and atomically request a beat-aligned handoff.
        /// A null request prefers upcoming queued prepared tracks, then other cached tracks.
        /// Returns false if no safe prepared candidate exists; never prepares, starts or resumes audio here.
        /// </summary>
        public bool AutoEnqueuePreparedNext(string? requestedPath = null)
        {
            lock (this._lock)
            {
                if (this._disposed)
                {
                    return false;
                }

                PreparedPlaylistTrack? prepared = this.SelectPreparedNextLocked(requestedPath);
                if (prepared == null)
                {
                    return false;
                }

                int insertIndex = this.GetProtectedHeadCountLocked();
                this.RemoveQueuedDuplicatesLocked(prepared.OriginalPath, insertIndex);
                this.FilePaths.Insert(insertIndex, prepared.OriginalPath);
                this.SchedulePreparedStartLocked(prepared.OriginalPath);
            }

            TrackChanged?.Invoke();
            return true;
        }

        /// <summary>Capture queue paths and current UI metadata without exposing the mutable queue.</summary>
        public PlaylistQueueSnapshot GetQueueSnapshot()
        {
            lock (this._lock)
            {
                return new PlaylistQueueSnapshot(
                    Array.AsReadOnly(this.FilePaths.ToArray()),
                    this.OriginalCurrentPath,
                    this.CurrentPath,
                    this.GetNextQueuePathLocked(),
                    this.IsPlaying,
                    this.IsPaused,
                    this.GetCurrentPosition(),
                    this.CurrentDuration,
                    this.CurrentBpm,
                    this.CurrentChannels,
                    this.CurrentSampleRate,
                    this.CurrentBitDepth);
            }
        }

        /// <summary>
        /// Legacy notification for an already-inserted next track. Only an eligible prepared
        /// next track may be scheduled; this notification never starts or resumes the run-loop.
        /// </summary>
        public void NotifyInsertedNext(string originalPath)
        {
            lock (this._lock)
            {
                this.InvalidateQueuedPreparedStartLocked();
                string? nextPath = this.GetNextQueuePathLocked();
                if (this._disposed || this.IsPaused || string.IsNullOrWhiteSpace(nextPath) ||
                    !QueuePathsEqual(originalPath, nextPath))
                {
                    return;
                }

                PreparedPlaylistTrack? prepared = this.GetEligiblePreparedTrackLocked(nextPath);
                if (prepared == null)
                {
                    return;
                }

                this.SchedulePreparedStartLocked(nextPath);
            }
        }

        private int EnqueuePaths(IEnumerable<string> paths, bool insertNext)
        {
            ArgumentNullException.ThrowIfNull(paths);
            int insertedCount;
            lock (this._lock)
            {
                if (this._disposed)
                {
                    return 0;
                }

                List<string> validPaths = paths.Where(path => !string.IsNullOrWhiteSpace(path)).ToList();
                insertedCount = validPaths.Count;
                if (insertedCount == 0)
                {
                    return 0;
                }

                int insertIndex = insertNext ? this.GetProtectedHeadCountLocked() : this.FilePaths.Count;
                this.FilePaths.InsertRange(insertIndex, validPaths);
                this.InvalidateQueuedPreparedStartLocked();
            }

            TrackChanged?.Invoke();
            return insertedCount;
        }

        // All Locked helpers require _lock; path comparison does not access the file system.
        private int GetProtectedHeadCountLocked()
        {
            bool headReserved = this.FilePaths.Count > 0 && QueuePathsEqual(this._queueCurrentPath, this.FilePaths[0]);
            return this.FilePaths.Count > 0 && (this.IsPlaying || this.IsPaused || headReserved) ? 1 : 0;
        }

        private string? GetNextQueuePathLocked()
        {
            int nextIndex = this.GetProtectedHeadCountLocked();
            return nextIndex < this.FilePaths.Count ? this.FilePaths[nextIndex] : null;
        }

        private PreparedPlaylistTrack? SelectPreparedNextLocked(string? requestedPath)
        {
            if (requestedPath != null)
            {
                return this.GetEligiblePreparedTrackLocked(requestedPath);
            }

            for (int i = this.GetProtectedHeadCountLocked(); i < this.FilePaths.Count; i++)
            {
                PreparedPlaylistTrack? prepared = this.GetEligiblePreparedTrackLocked(this.FilePaths[i]);
                if (prepared != null)
                {
                    return prepared;
                }
            }

            return this._preparedByPath.Values.FirstOrDefault(this.CanEnqueuePreparedTrackLocked);
        }

        private PreparedPlaylistTrack? GetEligiblePreparedTrackLocked(string path)
        {
            return this._preparedByPath.TryGetValue(NormalizePathForKey(path), out var prepared) &&
                this.CanEnqueuePreparedTrackLocked(prepared) ? prepared : null;
        }

        private bool CanEnqueuePreparedTrackLocked(PreparedPlaylistTrack prepared)
        {
            AudioObj audio = prepared.Audio;
            if (audio == null || audio.Playing || audio.Paused ||
                string.IsNullOrWhiteSpace(prepared.OriginalPath) ||
                this._banlist.Contains(NormalizePathForKey(prepared.OriginalPath)) ||
                this.IsProtectedQueuePathLocked(prepared.OriginalPath) ||
                this.IsProtectedQueuePathLocked(prepared.PlayPath))
            {
                return false;
            }

            return !SameAudio(audio, this._primaryAudioObj) &&
                !SameAudio(audio, this._secondaryAudioObj) &&
                !SameAudio(audio, this._startingPreparedTrack?.Audio) &&
                !this._activePreparedTracks.Values.Any(active => SameAudio(audio, active.Audio));
        }

        private bool IsProtectedQueuePathLocked(string path)
        {
            return (this.GetProtectedHeadCountLocked() > 0 && QueuePathsEqual(path, this.FilePaths[0])) ||
                QueuePathsEqual(path, this._queueCurrentPath) ||
                QueuePathsEqual(path, this.OriginalCurrentPath) ||
                QueuePathsEqual(path, this.CurrentPath) ||
                QueuePathsEqual(path, this._primaryOriginalPath) ||
                QueuePathsEqual(path, this._secondaryOriginalPath) ||
                QueuePathsEqual(path, this._startingPreparedTrack?.OriginalPath) ||
                QueuePathsEqual(path, this._startingPreparedTrack?.PlayPath) ||
                this._activePreparedTracks.Values.Any(active =>
                    QueuePathsEqual(path, active.OriginalPath) || QueuePathsEqual(path, active.PlayPath));
        }

        private static bool SameAudio(AudioObj audio, AudioObj? other)
        {
            return ReferenceEquals(audio, other) || (other != null && audio.Id == other.Id);
        }

        private static bool QueuePathsEqual(string? left, string? right)
        {
            string key = NormalizePathForKey(left);
            return key.Length > 0 && string.Equals(key, NormalizePathForKey(right), StringComparison.OrdinalIgnoreCase);
        }

        private void RemoveQueuedDuplicatesLocked(string originalPath, int protectedHeadCount)
        {
            for (int i = this.FilePaths.Count - 1; i >= protectedHeadCount; i--)
            {
                if (QueuePathsEqual(this.FilePaths[i], originalPath))
                {
                    this.FilePaths.RemoveAt(i);
                }
            }
        }

        private void SchedulePreparedStartLocked(string originalPath)
        {
            if (!this.IsPlaying || this.IsPaused || this._skipRequested ||
                this._cts == null || this._cts.IsCancellationRequested)
            {
                this._queuedPreparedStart = null;
                return;
            }

            this._queuedPreparedStart = new QueuedPreparedStart
            {
                OriginalPath = originalPath,
                RequestedUtc = DateTime.UtcNow,
                MaxDelaySeconds = 10.0
            };
        }

        private void InvalidateQueuedPreparedStartLocked()
        {
            if (this._queuedPreparedStart != null &&
                (this._disposed || this.IsPaused || !this.IsPlaying || this._skipRequested ||
                 !QueuePathsEqual(this._queuedPreparedStart.OriginalPath, this.GetNextQueuePathLocked())))
            {
                this._queuedPreparedStart = null;
            }
        }
    }
}
