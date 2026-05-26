using ModularAudience.Audio;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ModularAudience.Forms.Modules
{
    /// <summary>
    /// Parameters for automatic time-stretching of each playlist track before playback.
    /// All fields except InitialBpm are track-independent and set once by the user.
    /// </summary>
    public sealed record PlaylistStretchSettings(
        float  TargetBpm,
        float  StretchFactor,
        int    ChunkSize,
        float  Overlap,
        int    Threads,
        bool   UseV2,
        bool   AutoChunking,
        bool   Offload,
        bool   Trim,
        bool   Fixed
    );

    /// <summary>
    /// Lightweight streaming playlist engine. Tracks are played directly from disk via NAudio
    /// and are never fully loaded into RAM. Each finished or skipped track is removed from the
    /// shared <see cref="FilePaths"/> list and disposed cleanly.
    /// </summary>
    internal sealed class PlaylistEngine : IDisposable
    {
        // â”€â”€ State â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public List<string> FilePaths { get; } = [];        // remaining queue (shared ref to WindowMain.PlaylistFilePaths)
        public bool IsPlaying { get; private set; }
        public bool IsPaused  { get; private set; }

        /// <summary>File path of the track currently streaming (null when idle).</summary>
        public string? CurrentPath { get; private set; }
        /// <summary>Original queue path of the current track (before any preprocessing/temp-file substitution).</summary>
        public string? OriginalCurrentPath { get; private set; }
        public TimeSpan CurrentPosition => this.GetCurrentPosition();
        public TimeSpan CurrentDuration { get; private set; }
        public int     CurrentChannels   { get; private set; }
        public int     CurrentSampleRate { get; private set; }
        public int     CurrentBitDepth   { get; private set; }
        public float   CurrentBpm        { get; private set; }

        // â”€â”€ Private â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private WaveOutEvent?    _waveOut;
        private AudioFileReader? _reader;
        private AudioObj? _primaryAudioObj;
        private AudioObj? _secondaryAudioObj;
        private string? _primaryOriginalPath;
        private string? _secondaryOriginalPath;
        private readonly Dictionary<Guid, PreparedPlaylistTrack> _activePreparedTracks = [];
        private Task? _runLoopTask;
        // Paths temporarily banned from random selection until the next Pause click.
        private readonly HashSet<string> _banlist = new(StringComparer.OrdinalIgnoreCase);
        // In-flight prepare tasks keyed by original path to avoid duplicate prepares
        private readonly Dictionary<string, Task<PreparedPlaylistTrack?>> _preparingTasks = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PreparedPlaylistTrack> _preparedByPath = new(StringComparer.OrdinalIgnoreCase);
        private QueuedPreparedStart? _queuedPreparedStart;
        private CancellationTokenSource? _cts;
        private string? _previousPath;          // 1-track back-history
        private readonly object _lock = new();
        private volatile bool _skipRequested;
        private volatile bool _disposed;

        // Lightweight timestamped logger wrapper to produce consistent, ms-precise
        // debug messages into the existing LogCollection sink.
        private static void LogDebug(string message)
        {
            try
            {
                ModularAudience.Audio.LogCollection.Log($"[{DateTime.UtcNow:HH:mm:ss.fff}] {message}");
            }
            catch { }
        }

        public bool HasPreparedOriginalPath(string originalPath)
        {
            string key = NormalizePathForKey(originalPath);
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            lock (this._lock)
            {
                return this._preparedByPath.ContainsKey(key)
                    || this._activePreparedTracks.Values.Any(p =>
                        string.Equals(NormalizePathForKey(p.OriginalPath), key, StringComparison.OrdinalIgnoreCase));
            }
        }

        private static readonly Random Rng = new();

        // â”€â”€ Events â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        /// <summary>Fired on the thread-pool when the engine moves to the next track or goes idle.</summary>
        public event Action? TrackChanged;

        /// <summary>
        /// Optional preprocessor: called with the original file path before each track is played.
        /// Return a new (temp) file path to play instead, or null to play the original.
        /// The engine disposes any returned temp file after playback completes.
        /// </summary>
        public Func<string, CancellationToken, Task<string?>>? BeforeTrackPlay { get; set; }
        public Func<double>? CrossfadeDurationProvider { get; set; }
        public Func<AudioObj, AudioObj, Task>? CrossfadeStartedAsync { get; set; }
        public Func<string, string, float>? ResolvePlaybackBpm { get; set; }
        public IReadOnlyList<AudioObj> ActiveAudioObjs
        {
            get
            {
                lock (this._lock)
                {
                    List<AudioObj> active = this._activePreparedTracks.Values
                        .Select(prepared => prepared.Audio)
                        .Where(audio => audio != null)
                        .DistinctBy(audio => audio.Id)
                        .ToList();

                    if (this._primaryAudioObj != null)
                    {
                        active.Add(this._primaryAudioObj);
                    }

                    if (this._secondaryAudioObj != null)
                    {
                        active.Add(this._secondaryAudioObj);
                    }

                    return active
                        .Where(audio => audio != null)
                        .DistinctBy(audio => audio.Id)
                        .ToArray();
                }
            }
        }
        public IReadOnlyList<string> ActiveOriginalPaths
        {
            get
            {
                lock (this._lock)
                {
                    return this._activePreparedTracks.Values
                        .Select(prepared => prepared.OriginalPath)
                        .Where(path => !string.IsNullOrWhiteSpace(path))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                }
            }
        }

        /// <summary>
        /// Prepared original paths whose associated Audio object is not currently playing.
        /// Used by UI fallback logic to prefer non-playing prepared tracks.
        /// </summary>
        public IReadOnlyList<string> PreparedNonPlayingOriginalPaths
        {
            get
            {
                lock (this._lock)
                {
                    return this._activePreparedTracks.Values
                    .Where(p => p != null && p.Audio != null && !p.Audio.Playing)
                        .Select(p => p.OriginalPath)
                        .Concat(this._preparedByPath.Values
                            .Where(p => p != null && p.Audio != null && !p.Audio.Playing)
                            .Select(p => p.OriginalPath))
                        .Where(path => !string.IsNullOrWhiteSpace(path) && !string.Equals(path, this.OriginalCurrentPath ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                }
            }
        }
        public AudioObj? PrimaryAudioObj
        {
            get
            {
                lock (this._lock)
                {
                    return this._primaryAudioObj;
                }
            }
        }

        private const double CrossfadePreprocessLeadSeconds = 15.0;

        // Hard upper bound for the pre-prepare lead, even when the user configured a huge crossfade.
        // Without this cap a 180 s crossfade would start pre-stretching the next track instantly,
        // chaining multiple expensive time-stretches and causing the engine to feel "stuck".
        private const double MaxPreprocessLeadSeconds = 60.0;

        private static double ComputeEffectiveCrossfade(double configuredCrossfade, double currentDurationSeconds, double currentRemainingSeconds)
        {
            if (configuredCrossfade <= 0.0)
            {
                return 0.0;
            }

            double cap = configuredCrossfade;
            if (currentDurationSeconds > 0.0)
            {
                // Never crossfade across more than half of the current track,
                // so the new track is not started practically at the same moment as the previous one.
                cap = Math.Min(cap, currentDurationSeconds * 0.5);
            }
            if (currentRemainingSeconds > 0.0)
            {
                cap = Math.Min(cap, currentRemainingSeconds);
            }
            return Math.Max(0.0, cap);
        }

        public sealed class PreparedPlaylistTrack
        {
            public required AudioObj Audio { get; init; }
            public required string OriginalPath { get; init; }
            public required string PlayPath { get; init; }
            public string? TempPath { get; init; }
        }

        private sealed class QueuedPreparedStart
        {
            public required string OriginalPath { get; init; }
            public required DateTime RequestedUtc { get; init; }
            public required double MaxDelaySeconds { get; init; }
            public int LastCountdownSecond { get; set; } = -1;
        }

        // Normalize a file path to a canonical key used for dictionaries/banlist.
        private static string NormalizePathForKey(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                string full = Path.GetFullPath(path).Trim();
                return full.ToUpperInvariant();
            }
            catch
            {
                try { return path.Trim().ToUpperInvariant(); } catch { return string.Empty; }
            }
        }

        /// <summary>
        /// Notification from UI that a prepared path was just inserted as the next track.
        /// The engine schedules a short beat-aligned handoff and intentionally avoids
        /// starting heavy time-stretch work on this critical timing path.
        /// </summary>
        public void NotifyInsertedNext(string originalPath)
        {
            this.LogPlayback($"NotifyInsertedNext(originalPath={originalPath}) called");
            if (string.IsNullOrWhiteSpace(originalPath))
            {
                return;
            }

            string key = NormalizePathForKey(originalPath);
            lock (this._lock)
            {
                bool hasPrepared = this._preparedByPath.ContainsKey(key)
                    || this._activePreparedTracks.Values.Any(p =>
                        string.Equals(NormalizePathForKey(p.OriginalPath), key, StringComparison.OrdinalIgnoreCase));
                if (hasPrepared)
                {
                    this._queuedPreparedStart = new QueuedPreparedStart
                    {
                        OriginalPath = originalPath,
                        RequestedUtc = DateTime.UtcNow,
                        MaxDelaySeconds = 10.0
                    };
                    LogDebug($"[PlaylistEngine] auto-enqueue scheduled prepared on-beat start within 10s: {Path.GetFileNameWithoutExtension(originalPath)}");
                }
                else
                {
                    LogDebug($"[PlaylistEngine] auto-enqueue inserted non-prepared track; run-loop will prepare outside timing path: {Path.GetFileNameWithoutExtension(originalPath)}");
                }
            }

            this.EnsureRunLoopIfQueueHasWork("inserted-next");
        }

        public void NotifyQueueChanged(string reason)
        {
            this.EnsureRunLoopIfQueueHasWork(reason);
        }

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        //  Public API
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>Start or resume playlist playback.</summary>
        public void Play()
        {
            if (this._disposed)
            {
                return;
            }

            bool resume = false;
            List<AudioObj>? resumeTargets = null;
            lock (this._lock)
            {
                if (this.IsPaused)
                {
                    resume = true;
                    resumeTargets = this._activePreparedTracks.Values.Select(p => p.Audio).ToList();
                    if (this._primaryAudioObj != null && !resumeTargets.Contains(this._primaryAudioObj))
                    {
                        resumeTargets.Add(this._primaryAudioObj);
                    }

                    if (this._secondaryAudioObj != null && !resumeTargets.Contains(this._secondaryAudioObj))
                    {
                        resumeTargets.Add(this._secondaryAudioObj);
                    }

                    this.IsPaused = false;
                    this.IsPlaying = true;
                }
                else if (this.IsPlaying || this.FilePaths.Count == 0)
                {
                    return;
                }
            }

            if (resume)
            {
                // Resume all active slots outside the lock to avoid deadlock
                var targets = resumeTargets ?? [];
                _ = Task.Run(() =>
                {
                    foreach (var a in targets)
                    {
                        try { a.PauseAsync().GetAwaiter().GetResult(); } catch { }
                    }

                    lock (this._lock) { this._waveOut?.Play(); }
                });
                return;
            }

            this.StartRunLoopIfNeeded();
        }

        private void StartRunLoopIfNeeded()
        {
            lock (this._lock)
            {
                if (this._disposed || this.FilePaths.Count == 0)
                {
                    return;
                }

                if (this._runLoopTask != null && !this._runLoopTask.IsCompleted)
                {
                    return;
                }

                try { this._cts?.Dispose(); } catch { }
                this._skipRequested = false;
                this._cts = new CancellationTokenSource();
                CancellationToken token = this._cts.Token;
                this._runLoopTask = Task.Run(() => this.RunLoop(token));
                this.IsPlaying = true;
                this.IsPaused = false;
            }
        }

        private void EnsureRunLoopIfQueueHasWork(string reason)
        {
            bool needsStart = false;
            lock (this._lock)
            {
                bool hasQueue = this.FilePaths.Count > 0;
                bool hasAudibleAudio = this._activePreparedTracks.Values.Any(p => p.Audio != null && p.Audio.Playing)
                    || this._primaryAudioObj?.Playing == true
                    || this._secondaryAudioObj?.Playing == true;
                bool loopDead = this._runLoopTask == null || this._runLoopTask.IsCompleted;
                needsStart = hasQueue && !this._disposed && (loopDead || (!hasAudibleAudio && !this.IsPaused));
            }

            if (needsStart)
            {
                LogDebug($"[PlaylistEngine] recovery starting run-loop: {reason}");
                this.StartRunLoopIfNeeded();
                TrackChanged?.Invoke();
            }
        }

        /// <summary>Pause current playback without advancing the queue.</summary>
        public void Pause()
        {
            this.LogPlayback($"Pause() called: IsPlaying={this.IsPlaying} IsPaused={this.IsPaused} activePrepared={this._activePreparedTracks.Count}");
            List<AudioObj>? pauseTargets = null;
            lock (this._lock)
            {
                if (!this.IsPlaying || this.IsPaused)
                {
                    return;
                }

                pauseTargets = this._activePreparedTracks.Values.Select(p => p.Audio).ToList();
                if (this._primaryAudioObj != null && !pauseTargets.Contains(this._primaryAudioObj))
                {
                    pauseTargets.Add(this._primaryAudioObj);
                }

                if (this._secondaryAudioObj != null && !pauseTargets.Contains(this._secondaryAudioObj))
                {
                    pauseTargets.Add(this._secondaryAudioObj);
                }

                this.IsPaused  = true;
                this.IsPlaying = false;
            }

            lock (this._lock)
            {
                this._banlist.Clear();
            }

            // Pause all active slots outside the lock to avoid deadlock
            var targets = pauseTargets ?? [];
            _ = Task.Run(() =>
            {
                foreach (var a in targets)
                {
                    try { a.PauseAsync().GetAwaiter().GetResult(); } catch { }
                }

                lock (this._lock) { this._waveOut?.Pause(); }
            });
        }

        /// <summary>Toggle between play and pause.</summary>
        public void TogglePlayPause()
        {
            if (this.IsPaused || (!this.IsPlaying && this.FilePaths.Count > 0))
            {
                this.Play();
            }
            else
            {
                this.Pause();
            }
        }

        /// <summary>
        /// Remove an active prepared or playing audio by its AudioObj Id.
        /// Stops playback of that audio, disposes it and removes internal tracking.
        /// Returns true if the audio was found and removal was initiated.
        /// </summary>
        public bool RemoveActiveById(Guid audioId)
        {
            this.LogPlayback($"RemoveActiveById({audioId}) called");
            PreparedPlaylistTrack? prepared = null;
            AudioObj? primary = null;
            AudioObj? secondary = null;
            WaveOutEvent? wo = null;
            AudioFileReader? rd = null;
            string? removedOriginalPath = null;
            bool removedCurrentPrimary = false;

            lock (this._lock)
            {
                if (this._activePreparedTracks.TryGetValue(audioId, out var p))
                {
                    prepared = p;
                    removedOriginalPath = p.OriginalPath;
                    this._activePreparedTracks.Remove(audioId);
                }

                if (this._primaryAudioObj != null && this._primaryAudioObj.Id == audioId)
                {
                    primary = this._primaryAudioObj;
                    removedOriginalPath ??= this._primaryOriginalPath ?? this.OriginalCurrentPath;
                    removedCurrentPrimary = true;
                    this._primaryAudioObj = null;
                    this._primaryOriginalPath = null;
                    this.CurrentPath = null;
                    this.OriginalCurrentPath = null;
                    wo = this._waveOut;
                    rd = this._reader;
                }

                if (this._secondaryAudioObj != null && this._secondaryAudioObj.Id == audioId)
                {
                    secondary = this._secondaryAudioObj;
                    removedOriginalPath ??= this._secondaryOriginalPath;
                    this._secondaryAudioObj = null;
                    this._secondaryOriginalPath = null;
                }

                if (!string.IsNullOrWhiteSpace(removedOriginalPath))
                {
                    this._preparedByPath.Remove(NormalizePathForKey(removedOriginalPath));
                    int queueIndex = this.FilePaths.FindIndex(path =>
                        string.Equals(path, removedOriginalPath, StringComparison.OrdinalIgnoreCase));
                    if (queueIndex >= 0)
                    {
                        this.FilePaths.RemoveAt(queueIndex);
                    }
                }

                if (removedCurrentPrimary)
                {
                    this._skipRequested = true;
                    this.IsPlaying = false;
                    this.IsPaused = false;
                }
            }

            if (prepared == null && primary == null && secondary == null)
            {
                return false;
            }

            // Stop and dispose outside lock
            _ = Task.Run(() =>
            {
                try { prepared?.Audio.StopAsync().GetAwaiter().GetResult(); } catch { }
                try { primary?.StopAsync().GetAwaiter().GetResult(); } catch { }
                try { secondary?.StopAsync().GetAwaiter().GetResult(); } catch { }

                try { prepared?.Audio.Dispose(); } catch { }
                try { primary?.Dispose(); } catch { }
                try { secondary?.Dispose(); } catch { }

                try { wo?.Stop(); } catch { }
                try { wo?.Dispose(); } catch { }
                try { rd?.Dispose(); } catch { }
            });

            TrackChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Rewind: if position > 1 s, seek to start of current track;
        /// otherwise go back to the previous track (if any).
        /// </summary>
        public void RewindOrPrevious()
        {
            this.LogPlayback($"RewindOrPrevious() called: CurrentPath={this.CurrentPath} PreviousPath={this._previousPath}");
            lock (this._lock)
            {
                if (this._reader != null && this._reader.CurrentTime.TotalSeconds > 1.0)
                {
                    this._reader.CurrentTime = TimeSpan.Zero;
                    return;
                }
            }

            // Go to previous
            if (this._previousPath != null && File.Exists(this._previousPath))
            {
                string prev = this._previousPath;
                this._previousPath = null;
                this.StopCurrentAndInsert(prev);
            }
        }

        /// <summary>Skip the current track and start the next one.</summary>
        public void Skip()
        {
            this.LogPlayback($"Skip() called: CurrentPath={this.CurrentPath} IsPlaying={this.IsPlaying}");
            this._skipRequested = true;
            lock (this._lock) { this._waveOut?.Stop(); }
        }

        /// <summary>Shuffle remaining tracks (not including currently-playing one).</summary>
        public void Shuffle()
        {
            lock (this._lock)
            {
                for (int i = this.FilePaths.Count - 1; i > 0; i--)
                {
                    int j = Rng.Next(i + 1);
                    (this.FilePaths[i], this.FilePaths[j]) = (this.FilePaths[j], this.FilePaths[i]);
                }
            }
        }

        /// <summary>Stop playback, clear the queue, dispose all resources.</summary>
        public void Clear()
        {
            this.LogPlayback($"Clear() called: FilePaths.Count={this.FilePaths?.Count ?? 0} IsPlaying={this.IsPlaying} IsPaused={this.IsPaused}");
            // Cancel the run-loop task first so IsPlaying cannot be set back to true after we clear
            this._cts?.Cancel();
            this._skipRequested = true;
            this.StopAndDisposeCurrent();
            lock (this._lock)
            {
                this.FilePaths?.Clear();
                this._banlist.Clear();
                this.CurrentPath      = null;
                this.OriginalCurrentPath = null;
                this._previousPath    = null;
                this.CurrentDuration  = TimeSpan.Zero;
                this.CurrentChannels  = 0;
                this.CurrentSampleRate = 0;
                this.CurrentBitDepth  = 0;
                this.CurrentBpm       = 0;
                this.IsPlaying = false;
                this.IsPaused  = false;
            }
            TrackChanged?.Invoke();
        }

        public void Dispose()
        {
            this.LogPlayback($"Dispose() called: disposed={this._disposed}");
            this._disposed = true;
            this.StopAndDisposeCurrent();
        }

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        //  Internal helpers
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private async Task RunLoop(CancellationToken ct)
        {
            this.LogPlayback($"RunLoop() called: cancellationRequested={ct.IsCancellationRequested}");
            await this.RunCrossfadeLoop(ct).ConfigureAwait(false);
        }

        private async Task<bool> PlayTrackAsync(string path, CancellationToken ct)
        {
            this.LogPlayback($"PlayTrackAsync(path={path}) called: exists={File.Exists(path ?? string.Empty)}");
            AudioFileReader? reader = null;
            WaveOutEvent?    waveOut = null;

            try
            {
                reader  = new AudioFileReader(path);
                waveOut = new WaveOutEvent { DesiredLatency = 80 };

                // Raise thread priority inside WaveOut callback
                waveOut.Init(reader);

                lock (this._lock)
                {
                    this._reader  = reader;
                    this._waveOut = waveOut;

                    this.CurrentPath       = path;
                    this.CurrentDuration   = reader.TotalTime;
                    this.CurrentChannels   = reader.WaveFormat.Channels;
                    this.CurrentSampleRate = reader.WaveFormat.SampleRate;
                    this.CurrentBitDepth   = reader.WaveFormat.BitsPerSample;
                    this.IsPlaying = true;
                    this.IsPaused  = false;
                }

                // Read BPM tag cheaply
                this.CurrentBpm = ReadBpmTagLight(path ?? "");

                this.LogPlayback($"PlayTrackAsync: started playback path={path} duration={reader.TotalTime} bpm={this.CurrentBpm}");

                TrackChanged?.Invoke();

                // If countdown is enabled, announce 3..2..1 with approx. timing before logging Now playing.
                try
                {
                    string logName = System.IO.Path.GetFileNameWithoutExtension(this.OriginalCurrentPath ?? path) ?? "";
                    string logBpm  = this.CurrentBpm > 0 ? $" [{this.CurrentBpm:F0} BPM]" : string.Empty;
                    bool wantCountdown = false;
                    try { wantCountdown = WindowMain.PlaylistCountdownEnabled; } catch { }

                    if (wantCountdown)
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                double bpm = 0.0;
                                try { bpm = this.CurrentBpm; } catch { }

                                // If no BPM in tag, try a fast scan (non-blocking to UI), with a short timeout
                                if (bpm <= 0.0)
                                {
                                    try
                                    {
                                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                                        // Create a lightweight AudioObj just for scanning metadata if possible
                                        try
                                        {
                                            var ao = new ModularAudience.Audio.AudioObj(path ?? "", load: false);
                                            var scanned = await ModularAudience.Audio.Processors_V1.BeatScanner.ScanBpmAsync(ao).ConfigureAwait(false);
                                            if (scanned > 0.0)
                                            {
                                                bpm = scanned;
                                            }
                                        }
                                        catch { }
                                    }
                                    catch { }
                                }

                                // Fallback interval in ms
                                int intervalMs = 900;
                                if (bpm > 0.0)
                                {
                                    double beatMs = 60000.0 / bpm; // ms per beat
                                    // Use quarter-note count: count 3 beats (3,2,1) so interval = beatMs
                                    intervalMs = (int) Math.Max(150, Math.Round(beatMs));
                                }

                                try { ModularAudience.Audio.LogCollection.Log($"Countdown: 3 (interval={intervalMs}ms)"); } catch { }
                                try { await Task.Delay(intervalMs); } catch { }
                                try { ModularAudience.Audio.LogCollection.Log("Countdown: 2"); } catch { }
                                try { await Task.Delay(intervalMs); } catch { }
                                try { ModularAudience.Audio.LogCollection.Log("Countdown: 1"); } catch { }
                            }
                            catch { }
                        });
                    }

                    // Intentionally logged before Play(): runs on each new track entry via PlayTrackAsync.
                    ModularAudience.Audio.LogCollection.Log($"Now playing: {logName}{logBpm}");
                }
                catch { }

                waveOut.Play();

                // Poll until playback ends or we are asked to stop
                while (!ct.IsCancellationRequested && !this._skipRequested && !this._disposed)
                {
                    if (waveOut.PlaybackState == PlaybackState.Stopped)
                    {
                        break;
                    }

                    await Task.Delay(100, ct).ConfigureAwait(false);
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PlaylistEngine: error playing '{path}': {ex.Message}");
                return false;
            }
            finally
            {
                lock (this._lock)
                {
                    this._waveOut = null;
                    this._reader  = null;
                    this.IsPlaying = false;
                    this.IsPaused  = false;
                }

                try { waveOut?.Stop(); }  catch { }
                try { waveOut?.Dispose(); } catch { }
                try { reader?.Dispose(); }  catch { }
            }
        }

        private void StopCurrentAndInsert(string pathToInsert)
        {
            this.LogPlayback($"StopCurrentAndInsert(pathToInsert={pathToInsert}) called");
            this._skipRequested = true;
            WaveOutEvent? wo;
            AudioObj? primary;
            AudioObj? secondary;
            lock (this._lock)
            {
                wo = this._waveOut;
                primary = this._primaryAudioObj;
                secondary = this._secondaryAudioObj;
                this.FilePaths.Insert(0, pathToInsert);
            }
            _ = Task.Run(() =>
            {
                try { primary?.StopAsync().GetAwaiter().GetResult(); } catch { }
                try { secondary?.StopAsync().GetAwaiter().GetResult(); } catch { }
                try { wo?.Stop(); } catch { }
            });
        }

        private void StopAndDisposeCurrent()
        {
            this._cts?.Cancel();
            this._skipRequested = true;

            AudioObj? primary;
            AudioObj? secondary;
            WaveOutEvent?    wo;
            AudioFileReader? rd;
            lock (this._lock)
            {
                primary   = this._primaryAudioObj;
                secondary = this._secondaryAudioObj;
                wo = this._waveOut;
                rd = this._reader;
                this._primaryAudioObj   = null;
                this._primaryOriginalPath = null;
                this._secondaryAudioObj = null;
                this._secondaryOriginalPath = null;
                this._activePreparedTracks.Clear();
                this._preparedByPath.Clear();
                this._waveOut = null;
                this._reader  = null;
            }

            _ = Task.Run(() =>
            {
                try { primary?.StopAsync().GetAwaiter().GetResult(); }   catch { }
                try { secondary?.StopAsync().GetAwaiter().GetResult(); } catch { }
                try { primary?.Dispose(); }   catch { }
                try { secondary?.Dispose(); } catch { }
                try { wo?.Stop();    } catch { }
                try { wo?.Dispose(); } catch { }
                try { rd?.Dispose(); } catch { }
            });

            try { this._cts?.Dispose(); } catch { }
            this._cts = null;
        }

        private TimeSpan GetCurrentPosition()
        {
            lock (this._lock)
            {
                try
                {
                    if (this._primaryAudioObj != null)
                    {
                        return this._primaryAudioObj.CurrentTime;
                    }

                    return this._reader?.CurrentTime ?? TimeSpan.Zero;
                }
                catch { return TimeSpan.Zero; }
            }
        }

        private double GetCrossfadeDuration()
        {
            try
            {
                return Math.Max(0.0, this.CrossfadeDurationProvider?.Invoke() ?? 0.0);
            }
            catch
            {
                return 0.0;
            }
        }

        private async Task RunCrossfadeLoop(CancellationToken ct)
        {
            this.LogPlayback($"RunCrossfadeLoop() called: cancellationRequested={ct.IsCancellationRequested}");
            var fadeOutTasks = new List<Task>();
            Task<PreparedPlaylistTrack?>? nextPrepareTask = null;
            string? nextPrepareTaskPath = null;

            try
            {
                while (!ct.IsCancellationRequested && !this._disposed)
                {
                    fadeOutTasks.RemoveAll(t => t.IsCompleted);

                    string? currentOriginalPath;
                    lock (this._lock)
                    {
                        while (this.FilePaths.Count > 1 &&
                               string.Equals(this.FilePaths[0], this.FilePaths[1], StringComparison.OrdinalIgnoreCase))
                        {
                            LogDebug($"[PlaylistEngine] duplicate removed: {Path.GetFileNameWithoutExtension(this.FilePaths[1])} FilePathsCount={this.FilePaths.Count}");
                            this.FilePaths.RemoveAt(1);
                        }
                        currentOriginalPath = this.FilePaths.Count > 0 ? this.FilePaths[0] : null;
                    }

                    if (string.IsNullOrWhiteSpace(currentOriginalPath))
                    {
                        break;
                    }

                    PreparedPlaylistTrack? currentPrepared;
                    if (nextPrepareTask != null && string.Equals(nextPrepareTaskPath, currentOriginalPath, StringComparison.OrdinalIgnoreCase))
                    {
                        LogDebug($"[PlaylistEngine] reusing pre-prepared: {Path.GetFileNameWithoutExtension(currentOriginalPath)} nextPrepareTaskPath={nextPrepareTaskPath}");
                        currentPrepared = await nextPrepareTask.ConfigureAwait(false);
                        nextPrepareTask = null;
                        nextPrepareTaskPath = null;
                    }
                    else
                    {
                        if (nextPrepareTask != null)
                        {
                            var staleTask = nextPrepareTask;
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    var ab = await staleTask.ConfigureAwait(false);
                                    if (ab != null)
                                    {
                                        bool shouldDispose = false;
                                        try
                                        {
                                            lock (this._lock)
                                            {
                                                // Only dispose/untrack if the prepared original path is no longer
                                                // present in the queue and the audio is not currently playing.
                                                bool inQueue = this.FilePaths.Any(p => string.Equals(p, ab.OriginalPath, StringComparison.OrdinalIgnoreCase));
                                                if (!inQueue && (ab.Audio == null || !ab.Audio.Playing))
                                                {
                                                    shouldDispose = true;
                                                }
                                            }
                                        }
                                        catch { }

                                        if (shouldDispose)
                                        {
                                            this.UntrackPrepared(ab, "abandoned");
                                            try { ab.Audio?.Dispose(); } catch { }
                                            this.DeleteTempFile(ab.TempPath);
                                        }
                                        // else: keep preprepared track available for later seam/click use
                                    }
                                }
                                catch { }
                            });
                            nextPrepareTask = null;
                            nextPrepareTaskPath = null;
                        }
                        LogDebug($"[PlaylistEngine] PrepareTrackAsync start for {Path.GetFileNameWithoutExtension(currentOriginalPath)} FilePathsCount={this.FilePaths.Count}");
                        currentPrepared = await this.PrepareTrackAsync(currentOriginalPath, ct).ConfigureAwait(false);
                    }

                    if (currentPrepared == null)
                    {
                        lock (this._lock)
                        {
                            if (this.FilePaths.Count > 0 && string.Equals(this.FilePaths[0], currentOriginalPath, StringComparison.OrdinalIgnoreCase))
                            {
                                this.FilePaths.RemoveAt(0);
                            }
                        }
                        continue;
                    }

                    await currentPrepared.Audio.PlayAsync(CancellationToken.None, initialVolume: 1.0f).ConfigureAwait(false);
                    currentPrepared.Audio.Volume = 100f;
                    currentPrepared.Audio.SetPlaybackVolume(1.0f);
                    LogDebug($"[PlaylistEngine] PlayAsync invoked for {Path.GetFileNameWithoutExtension(currentOriginalPath)} AudioId={currentPrepared.Audio.Id} Playing={currentPrepared.Audio.Playing}");
                    this.TrackPreparedAsActive(currentPrepared, "initial play");
                    this.ApplyCurrentTrackState(currentPrepared);
                    TrackChanged?.Invoke();
                    ModularAudience.Audio.LogCollection.Log($"[PlaylistEngine] Now playing: {Path.GetFileNameWithoutExtension(currentOriginalPath)} | active={this.ActiveAudioObjs.Count}");

                    bool crossfadeTriggered = false;
                    int notPlayingStrikes = 0;
                    TimeSpan lastObservedPosition = TimeSpan.Zero;
                    DateTime? silentStallSinceUtc = null;

                    while (!ct.IsCancellationRequested && !this._disposed && !this._skipRequested)
                    {
                        if (!currentPrepared.Audio.Playing)
                        {
                            // User pause or engine pause: do not advance.
                            if (currentPrepared.Audio.Paused || this.IsPaused)
                            {
                                notPlayingStrikes = 0;
                                try { await Task.Delay(150, ct).ConfigureAwait(false); } catch { }
                                continue;
                            }

                            // A single "not playing" sample is unreliable: the WaveOut buffer can
                            // momentarily report stopped during a sync-pulse pause, an internal
                            // reset, or a buffer underrun. Require multiple consecutive strikes
                            // AND a frozen play position AND being effectively at the end of the
                            // track before treating it as a real end.
                            TimeSpan posNow = TimeSpan.Zero;
                            try { posNow = currentPrepared.Audio.CurrentTime; } catch { }
                            double playedSec = posNow.TotalSeconds;
                            double totalSec = currentPrepared.Audio.Duration.TotalSeconds;
                            bool nearEnd = totalSec <= 0.0 || playedSec >= Math.Max(0.5, totalSec * 0.95);
                            bool positionFrozen = Math.Abs((posNow - lastObservedPosition).TotalMilliseconds) < 5.0;
                            lastObservedPosition = posNow;

                            if (!nearEnd || !positionFrozen)
                            {
                                notPlayingStrikes = 0;
                                silentStallSinceUtc ??= DateTime.UtcNow;
                                if ((DateTime.UtcNow - silentStallSinceUtc.Value).TotalSeconds >= 3.0)
                                {
                                    LogDebug($"[PlaylistEngine] silent stall watchdog skipping dead current track: {Path.GetFileNameWithoutExtension(currentOriginalPath)} pos={playedSec:F2}s/{totalSec:F2}s");
                                    this._skipRequested = true;
                                    break;
                                }

                                try { await Task.Delay(150, ct).ConfigureAwait(false); } catch { }
                                continue;
                            }

                            notPlayingStrikes++;
                            if (notPlayingStrikes < 3)
                            {
                                try { await Task.Delay(120, ct).ConfigureAwait(false); } catch { }
                                continue;
                            }

                            ModularAudience.Audio.LogCollection.Log($"[PlaylistEngine] track ended (confirmed): {Path.GetFileNameWithoutExtension(currentOriginalPath)} pos={playedSec:F2}s/{totalSec:F2}s");
                            // Nahtloser Direktübergang: nur dann direkt starten, wenn KEIN Crossfade
                            // konfiguriert ist. Wenn Crossfade aktiv ist, gehört der Übergang in den
                            // normalen Crossfade-Pfad (der bereits früher auslöst), sonst verhindern
                            // wir Überlagerung/Blend-Logik fälschlicherweise.
                            double configuredCrossfade = this.GetCrossfadeDuration();
                            if (configuredCrossfade > 0.0)
                            {
                                ModularAudience.Audio.LogCollection.Log($"[PlaylistEngine] track ended but crossfade configured ({configuredCrossfade}s) - deferring to crossfade logic");
                                break;
                            }

                            // Nahtloser Direktübergang: wenn nächster Track bereits fertig vorbereitet ist,
                            // sofort starten ohne outer-loop-Umweg (verhindert hörbare Pause / harte Naht).
                            string? nextPath;
                            lock (this._lock)
                            {
                                string? cand = this.FilePaths.Count > 1 ? this.FilePaths[1] : null;
                                nextPath = string.Equals(cand, currentOriginalPath, StringComparison.OrdinalIgnoreCase) ? null : cand;
                            }

                            if (!string.IsNullOrWhiteSpace(nextPath)
                                && nextPrepareTask != null
                                && string.Equals(nextPrepareTaskPath, nextPath, StringComparison.OrdinalIgnoreCase)
                                && !nextPrepareTask.IsFaulted
                                && !nextPrepareTask.IsCanceled)
                            {
                                // Wenn Prepare noch läuft (z.B. Stretching), max 8s abwarten bevor wir aufgeben.
                                if (!nextPrepareTask.IsCompleted)
                                {
                                    ModularAudience.Audio.LogCollection.Log($"[PlaylistEngine] seam-wait: prepare still running for {Path.GetFileNameWithoutExtension(nextPath)}");
                                    using var waitCts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(8));
                                    try { await nextPrepareTask.WaitAsync(waitCts.Token).ConfigureAwait(false); } catch { }
                                }

                                PreparedPlaylistTrack? seamNext = null;
                                if (nextPrepareTask.IsCompletedSuccessfully)
                                {
                                    try { seamNext = await nextPrepareTask.ConfigureAwait(false); } catch { }
                                }
                                nextPrepareTask = null;
                                nextPrepareTaskPath = null;

                                if (seamNext != null)
                                {
                                    await seamNext.Audio.PlayAsync(CancellationToken.None, initialVolume: 1.0f).ConfigureAwait(false);
                                    seamNext.Audio.Volume = 100f;
                                    seamNext.Audio.SetPlaybackVolume(1.0f);
                                    this.TrackPreparedAsActive(seamNext, "seam start");

                                    this.UntrackPrepared(currentPrepared, "seam hand-off");
                                    try { currentPrepared.Audio.Dispose(); } catch { }
                                    this.DeleteTempFile(currentPrepared.TempPath);

                                    lock (this._lock)
                                    {
                                        if (this.FilePaths.Count > 0 && string.Equals(this.FilePaths[0], currentOriginalPath, StringComparison.OrdinalIgnoreCase))
                                        {
                                            this.FilePaths.RemoveAt(0);
                                        }

                                        this._previousPath = currentOriginalPath;
                                    }

                                    currentPrepared = seamNext;
                                    currentOriginalPath = nextPath;
                                    crossfadeTriggered = false;

                                    this.ApplyCurrentTrackState(seamNext);
                                    TrackChanged?.Invoke();
                                    ModularAudience.Audio.LogCollection.Log($"[PlaylistEngine] seam-start -> {Path.GetFileNameWithoutExtension(nextPath)} | active={this.ActiveAudioObjs.Count}");
                                    continue;
                                }
                            }

                            break;
                        }

                        notPlayingStrikes = 0;
                        silentStallSinceUtc = null;
                        try { lastObservedPosition = currentPrepared.Audio.CurrentTime; } catch { }

                        TimeSpan remaining = currentPrepared.Audio.Duration - currentPrepared.Audio.CurrentTime;
                        double remainingSeconds = remaining.TotalSeconds;
                        double currentDurationSeconds = currentPrepared.Audio.Duration.TotalSeconds;
                        double crossfadeDuration = this.GetCrossfadeDuration();
                        double effectiveCrossfade = ComputeEffectiveCrossfade(crossfadeDuration, currentDurationSeconds, remainingSeconds);
                        double preprocessLeadSeconds = Math.Min(MaxPreprocessLeadSeconds, effectiveCrossfade + CrossfadePreprocessLeadSeconds);

                        string? nextOriginalPath;
                        lock (this._lock)
                        {
                            string? candidate = this.FilePaths.Count > 1 ? this.FilePaths[1] : null;
                            nextOriginalPath = string.Equals(candidate, currentOriginalPath, StringComparison.OrdinalIgnoreCase) ? null : candidate;
                        }

                        PreparedPlaylistTrack? autoStarted = await this.TryStartQueuedPreparedTrackAsync(currentPrepared, currentOriginalPath, nextOriginalPath, ct).ConfigureAwait(false);
                        if (autoStarted != null && !string.IsNullOrWhiteSpace(nextOriginalPath))
                        {
                            currentPrepared = autoStarted;
                            currentOriginalPath = nextOriginalPath;
                            crossfadeTriggered = false;
                            notPlayingStrikes = 0;
                            silentStallSinceUtc = null;
                            TrackChanged?.Invoke();
                            continue;
                        }

                        // ── Pre-prepare next track as early as possible ──────────────────────
                        // Start as soon as the next path is known – regardless of time window –
                        // so that slow time-stretching operations are hidden behind playback.
                        if (!string.IsNullOrWhiteSpace(nextOriginalPath))
                        {
                            if (nextPrepareTask == null || !string.Equals(nextPrepareTaskPath, nextOriginalPath, StringComparison.OrdinalIgnoreCase))
                            {
                                if (nextPrepareTask != null)
                                {
                                    var staleTask = nextPrepareTask;
                                    _ = Task.Run(async () =>
                                    {
                                        try
                                        {
                                            var stale = await staleTask.ConfigureAwait(false);
                                            if (stale != null)
                                            {
                                                bool shouldDispose = false;
                                                try
                                                {
                                                    lock (this._lock)
                                                    {
                                                        bool inQueue = this.FilePaths.Any(p => string.Equals(p, stale.OriginalPath, StringComparison.OrdinalIgnoreCase));
                                                        if (!inQueue && (stale.Audio == null || !stale.Audio.Playing))
                                                        {
                                                            shouldDispose = true;
                                                        }
                                                    }
                                                }
                                                catch { }

                                                if (shouldDispose)
                                                {
                                                    this.UntrackPrepared(stale, "stale");
                                                    try { stale.Audio?.Dispose(); } catch { }
                                                    this.DeleteTempFile(stale.TempPath);
                                                }
                                                // else: keep prepared stale track available
                                            }
                                        }
                                        catch { }
                                    });
                                }
                                nextPrepareTask = this.PrepareTrackAsync(nextOriginalPath, ct);
                                nextPrepareTaskPath = nextOriginalPath;
                                LogDebug($"[PlaylistEngine] pre-preparing: {Path.GetFileNameWithoutExtension(nextOriginalPath)} (remaining={remainingSeconds:F1}s) nextPrepareTaskPath={nextPrepareTaskPath}");
                            }
                        }

                        // ── Beat-aligned crossfade window check ──────────────────────────────
                        bool inAlignWindow = !crossfadeTriggered
                            && !string.IsNullOrWhiteSpace(nextOriginalPath)
                            && effectiveCrossfade > 0
                            && OnBeatCrossfadeAligner.IsInAlignmentWindow(currentPrepared.Audio.Bpm, remainingSeconds, effectiveCrossfade);

                        if (inAlignWindow)
                        {
                            // Kick off prepare as early as possible so it's ready when the beat arrives
                            if (nextPrepareTask == null || !string.Equals(nextPrepareTaskPath, nextOriginalPath, StringComparison.OrdinalIgnoreCase))
                            {
                                nextPrepareTask = this.PrepareTrackAsync(nextOriginalPath!, ct);
                                nextPrepareTaskPath = nextOriginalPath;
                                LogDebug($"[PlaylistEngine] crossfade: kicked off prepare (remaining={remainingSeconds:F1}s) nextPrepareTaskPath={nextPrepareTaskPath}");
                            }
                        }

                        if (!crossfadeTriggered && !string.IsNullOrWhiteSpace(nextOriginalPath) && effectiveCrossfade > 0)
                        {
                            // Compute beat-aligned wait: may fire earlier or later than the nominal trigger
                            double beatWait = OnBeatCrossfadeAligner.ComputeWaitSeconds(
                                currentPrepared.Audio.Bpm,
                                currentPrepared.Audio.CurrentTime,
                                remainingSeconds,
                                effectiveCrossfade);

                            // Not yet at the beat-aligned trigger point. However, if we're
                            // already within the effective crossfade window (or very near the
                            // end), don't delay waiting for an ideal beat alignment — force
                            // the crossfade to ensure audible overlap instead of falling back
                            // to a hard seam.
                            if (beatWait > 0.025)
                            {
                                bool forceNow = remainingSeconds <= (effectiveCrossfade + 0.1) || remainingSeconds <= 0.5;
                                if (!forceNow)
                                {
                                    await Task.Delay(25, ct).ConfigureAwait(false);
                                    continue;
                                }
                                // else: proceed to trigger crossfade despite imperfect beat alignment
                                ModularAudience.Audio.LogCollection.Log($"[PlaylistEngine] crossfade force-trigger (near-end): remaining={remainingSeconds:F2}s beatWait={beatWait:F2}s");
                            }

                            // Ensure prepare task exists (may have been skipped if BPM unknown)
                            if (nextPrepareTask == null || !string.Equals(nextPrepareTaskPath, nextOriginalPath, StringComparison.OrdinalIgnoreCase))
                            {
                                nextPrepareTask = this.PrepareTrackAsync(nextOriginalPath!, ct);
                                nextPrepareTaskPath = nextOriginalPath;
                            }

                            // Don't block – wait for prepare to finish on next tick unless we
                            // are so close to the end that we must force overlap to avoid a seam.
                            if (!nextPrepareTask.IsCompleted)
                            {
                                if (remainingSeconds > Math.Max(0.5, effectiveCrossfade + 0.1))
                                {
                                    await Task.Delay(25, ct).ConfigureAwait(false);
                                    continue;
                                }
                                ModularAudience.Audio.LogCollection.Log($"[PlaylistEngine] forcing crossfade even though prepare still running (remaining={remainingSeconds:F2}s)");
                            }

                            crossfadeTriggered = true;
                            LogDebug($"[PlaylistEngine] crossfade trigger (on-beat): remaining={remainingSeconds:F1}s cf={crossfadeDuration:F1}s eff={effectiveCrossfade:F1}s nextPrepareTaskPath={nextPrepareTaskPath}");

                            PreparedPlaylistTrack? nextTrack;
                            try { nextTrack = await nextPrepareTask.ConfigureAwait(false); }
                            catch { nextTrack = null; }
                            nextPrepareTask = null;
                            nextPrepareTaskPath = null;

                            if (nextTrack != null)
                            {
                                // Fade duration: honor the user-configured crossfade as closely as possible.
                                // - Cap by the next track's own duration (never longer than half of the incoming track).
                                // - DO NOT cap by currentRemainingNow: the outgoing track will simply end while the
                                //   fade-out task is still running, which is harmless (volume task self-terminates).
                                double nextDurationSeconds = nextTrack.Audio.Duration.TotalSeconds;
                                double fadeDuration = effectiveCrossfade;
                                if (nextDurationSeconds > 0.0)
                                {
                                    fadeDuration = Math.Min(fadeDuration, nextDurationSeconds * 0.5);
                                }
                                fadeDuration = Math.Max(0.1, fadeDuration);
                                var fadingOut = currentPrepared;

                                // Start incoming track silent. Equal-power curve guarantees an audible ramp
                                // without a "sticky baseline" hack, and avoids a -6 dB hole in the middle.
                                nextTrack.Audio.Volume = 100f;
                                nextTrack.Audio.SetPlaybackVolume(0.0f);
                                await nextTrack.Audio.PlayAsync(CancellationToken.None, initialVolume: 0.0f).ConfigureAwait(false);
                                this.TrackPreparedAsActive(nextTrack, "crossfade start");

                                fadeOutTasks.Add(Task.Run(async () =>
                                {
                                    try
                                    {
                                        var started = DateTime.UtcNow;
                                        while (!this._disposed)
                                        {
                                            double elapsed = (DateTime.UtcNow - started).TotalSeconds;
                                            double t = Math.Clamp(elapsed / Math.Max(0.001, fadeDuration), 0.0, 1.0);
                                            // Equal-power fade-out: cos(t * pi/2).
                                            float vol = (float) Math.Cos(t * Math.PI * 0.5);
                                            try { fadingOut.Audio.SetPlaybackVolume(vol); } catch { }

                                            if (t >= 1.0)
                                            {
                                                try { await fadingOut.Audio.StopAsync().ConfigureAwait(false); } catch { }
                                                break;
                                            }
                                            await Task.Delay(25).ConfigureAwait(false);
                                        }
                                    }
                                    catch { }
                                    finally
                                    {
                                        this.UntrackPrepared(fadingOut, "fade-out done");
                                        try { fadingOut.Audio.Dispose(); } catch { }
                                        this.DeleteTempFile(fadingOut.TempPath);
                                        LogDebug($"[PlaylistEngine] fade-out done: {Path.GetFileNameWithoutExtension(fadingOut.OriginalPath)} | active={this.ActiveAudioObjs.Count}");
                                    }
                                }));

                                var fadingIn = nextTrack;
                                double fadeInDuration = fadeDuration;
                                fadeOutTasks.Add(Task.Run(async () =>
                                {
                                    try
                                    {
                                        var started = DateTime.UtcNow;
                                        while (!this._disposed)
                                        {
                                            double elapsed = (DateTime.UtcNow - started).TotalSeconds;
                                            double t = Math.Clamp(elapsed / Math.Max(0.001, fadeInDuration), 0.0, 1.0);
                                            // Equal-power fade-in: sin(t * pi/2).
                                            float vol = (float) Math.Sin(t * Math.PI * 0.5);
                                            try { fadingIn.Audio.SetPlaybackVolume(vol); } catch { }
                                            if (t >= 1.0)
                                            {
                                                break;
                                            }

                                            await Task.Delay(25).ConfigureAwait(false);
                                        }
                                        try { fadingIn.Audio.SetPlaybackVolume(1.0f); } catch { }
                                    }
                                    catch { }
                                }));

                                lock (this._lock)
                                {
                                    if (this.FilePaths.Count > 0 && string.Equals(this.FilePaths[0], currentOriginalPath, StringComparison.OrdinalIgnoreCase))
                                    {
                                        this.FilePaths.RemoveAt(0);
                                    }

                                    this._previousPath = currentOriginalPath;
                                }

                                this.ApplyCurrentTrackState(nextTrack);
                                TrackChanged?.Invoke();

                                if (this.CrossfadeStartedAsync != null)
                                {
                                    _ = Task.Run(() => this.CrossfadeStartedAsync(fadingOut.Audio, nextTrack.Audio), CancellationToken.None);
                                }

                                ModularAudience.Audio.LogCollection.Log($"[PlaylistEngine] crossfade -> {Path.GetFileNameWithoutExtension(nextOriginalPath)} | active={this.ActiveAudioObjs.Count}");

                                // Pivot inner loop to the new primary track without re-entering the outer loop.
                                // This prevents the outer loop from calling PrepareTrackAsync again on the
                                // already-playing nextTrack (which would cause a duplicate stretched copy).
                                currentPrepared = nextTrack;
                                currentOriginalPath = nextOriginalPath;
                                crossfadeTriggered = false;
                                continue;
                            }
                            else
                            {
                                crossfadeTriggered = false;
                                ModularAudience.Audio.LogCollection.Log($"[PlaylistEngine] crossfade prepare FAILED: {nextOriginalPath}");
                            }
                        }

                        await Task.Delay(25, ct).ConfigureAwait(false);
                    }

                    if (this._skipRequested)
                    {
                        try { await currentPrepared.Audio.StopAsync().ConfigureAwait(false); } catch { }
                        this.UntrackPrepared(currentPrepared, "skipped");
                        try { currentPrepared.Audio.Dispose(); } catch { }
                        this.DeleteTempFile(currentPrepared.TempPath);
                        lock (this._lock)
                        {
                            if (this.FilePaths.Count > 0 && string.Equals(this.FilePaths[0], currentOriginalPath, StringComparison.OrdinalIgnoreCase))
                            {
                                this.FilePaths.RemoveAt(0);
                            }
                        }
                        this._skipRequested = false;
                        this.ClearCurrentTrackState();
                        this.SetSecondaryTrack((PreparedPlaylistTrack?) null);
                        TrackChanged?.Invoke();
                    }
                    else if (!crossfadeTriggered)
                    {
                        this.UntrackPrepared(currentPrepared, "natural end");
                        try { currentPrepared.Audio.Dispose(); } catch { }
                        this.DeleteTempFile(currentPrepared.TempPath);
                        lock (this._lock)
                        {
                            if (this.FilePaths.Count > 0 && string.Equals(this.FilePaths[0], currentOriginalPath, StringComparison.OrdinalIgnoreCase))
                            {
                                this.FilePaths.RemoveAt(0);
                            }

                            this._previousPath = currentOriginalPath;
                        }
                        this.ClearCurrentTrackState();
                        this.SetSecondaryTrack((PreparedPlaylistTrack?) null);
                        TrackChanged?.Invoke();
                    }
                    // crossfadeTriggered: old track fading out in background, queue already advanced
                }

                if (fadeOutTasks.Count > 0)
                {
                    await Task.WhenAll(fadeOutTasks).ConfigureAwait(false);
                }
            }
            finally
            {
                if (nextPrepareTask != null)
                {
                    try
                    {
                        var ab = await nextPrepareTask.ConfigureAwait(false);
                        if (ab != null)
                        {
                            bool shouldDispose = false;
                            try
                            {
                                lock (this._lock)
                                {
                                    bool inQueue = this.FilePaths.Any(p => string.Equals(p, ab.OriginalPath, StringComparison.OrdinalIgnoreCase));
                                    if (!inQueue && (ab.Audio == null || !ab.Audio.Playing))
                                    {
                                        shouldDispose = true;
                                    }
                                }
                            }
                            catch { }

                            if (shouldDispose)
                            {
                                this.UntrackPrepared(ab, "abandoned");
                                try { ab.Audio?.Dispose(); } catch { }
                                this.DeleteTempFile(ab.TempPath);
                            }
                            // else: keep preprepared track to allow later use
                        }
                    }
                    catch { }
                }
                if (fadeOutTasks.Count > 0)
                {
                    try { await Task.WhenAll(fadeOutTasks).ConfigureAwait(false); } catch { }
                }

                this.ClearCurrentTrackState();
                this.SetSecondaryTrack((PreparedPlaylistTrack?) null);
                lock (this._lock)
                {
                    this._activePreparedTracks.Clear();
                    this._preparedByPath.Clear();
                    this.IsPlaying = false;
                    this.IsPaused = false;
                    this.CurrentPath = null;
                    this.OriginalCurrentPath = null;
                    this.CurrentDuration = TimeSpan.Zero;
                }
                TrackChanged?.Invoke();
            }
        }
        private async Task<PreparedPlaylistTrack?> PrepareTrackAsync(string originalPath, CancellationToken ct)
        {
            this.LogPlayback($"PrepareTrackAsync(originalPath={originalPath}) called: exists={File.Exists(originalPath ?? string.Empty)}");
            if (string.IsNullOrWhiteSpace(originalPath) || !File.Exists(originalPath))
            {
                return null;
            }

            Task<PreparedPlaylistTrack?> task;
            var key = NormalizePathForKey(originalPath);
            lock (this._lock)
            {
                // Early-out: if this originalPath is banlisted, do not prepare it.
                if (!string.IsNullOrWhiteSpace(key) && this._banlist.Contains(key))
                {
                    try { LogDebug($"PrepareTrackAsync: skipping banlisted path: {originalPath}"); } catch { }
                    return null;
                }

                // Early-out: avoid preparing the currently-playing original or any already-prepared original
                bool isCurrentPath = !string.IsNullOrWhiteSpace(this.OriginalCurrentPath)
                    && string.Equals(NormalizePathForKey(this.OriginalCurrentPath), key, StringComparison.OrdinalIgnoreCase);
                bool currentActuallyPlaying = this._primaryAudioObj?.Playing == true;
                if (isCurrentPath && currentActuallyPlaying)
                {
                    PreparedPlaylistTrack? currentPrepared = this._activePreparedTracks.Values.FirstOrDefault(p =>
                        string.Equals(NormalizePathForKey(p.OriginalPath), key, StringComparison.OrdinalIgnoreCase));
                    if (currentPrepared != null)
                    {
                        try { LogDebug($"PrepareTrackAsync: reusing currently playing path: {originalPath}"); } catch { }
                        return currentPrepared;
                    }

                    try { LogDebug($"PrepareTrackAsync: skipping currently playing path without prepared entry: {originalPath}"); } catch { }
                    return null;
                }

                PreparedPlaylistTrack? existingPrepared = null;
                if (this._preparedByPath.TryGetValue(key, out var cachedPrepared))
                {
                    existingPrepared = cachedPrepared;
                }
                existingPrepared ??= this._activePreparedTracks.Values.FirstOrDefault(p =>
                    string.Equals(NormalizePathForKey(p.OriginalPath), key, StringComparison.OrdinalIgnoreCase));
                if (existingPrepared != null)
                {
                    try { LogDebug($"PrepareTrackAsync: reusing already-prepared path: {originalPath}"); } catch { }
                    return existingPrepared;
                }

                // If a prepare for this path is already in-flight, reuse it.
                if (this._preparingTasks.TryGetValue(key, out var existing))
                {
                    task = existing;
                }
                else
                {
                    task = this.PrepareTrackCoreAsync(originalPath, ct);
                    this._preparingTasks[key] = task;
                }
            }

            return await task.ConfigureAwait(false);
        }

        private async Task<PreparedPlaylistTrack?> TryStartQueuedPreparedTrackAsync(
            PreparedPlaylistTrack currentPrepared,
            string currentOriginalPath,
            string? nextOriginalPath,
            CancellationToken ct)
        {
            QueuedPreparedStart? request;
            PreparedPlaylistTrack? nextTrack = null;
            lock (this._lock)
            {
                request = this._queuedPreparedStart;
                if (request == null || string.IsNullOrWhiteSpace(nextOriginalPath) ||
                    !string.Equals(request.OriginalPath, nextOriginalPath, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                string key = NormalizePathForKey(request.OriginalPath);
                this._preparedByPath.TryGetValue(key, out nextTrack);
                nextTrack ??= this._activePreparedTracks.Values.FirstOrDefault(p =>
                    string.Equals(NormalizePathForKey(p.OriginalPath), key, StringComparison.OrdinalIgnoreCase));
                if (nextTrack == null)
                {
                    return null;
                }
            }

            double elapsedSeconds = Math.Max(0.0, (DateTime.UtcNow - request.RequestedUtc).TotalSeconds);
            double waitSeconds = ComputeNextBeatWaitSeconds(currentPrepared.Audio, request.MaxDelaySeconds - elapsedSeconds);
            if (waitSeconds > 0.025 && elapsedSeconds + waitSeconds <= request.MaxDelaySeconds)
            {
                int countdownSecond = Math.Max(1, (int)Math.Ceiling(waitSeconds));
                if (request.LastCountdownSecond != countdownSecond)
                {
                    request.LastCountdownSecond = countdownSecond;
                    ModularAudience.Audio.LogCollection.Log($"Auto enqueue one: on-beat handoff in {countdownSecond}s -> {Path.GetFileNameWithoutExtension(request.OriginalPath)}");
                }

                await Task.Delay(TimeSpan.FromSeconds(Math.Min(waitSeconds, 0.25)), ct).ConfigureAwait(false);
                return null;
            }

            if (elapsedSeconds > request.MaxDelaySeconds + 0.5)
            {
                lock (this._lock)
                {
                    if (ReferenceEquals(this._queuedPreparedStart, request))
                    {
                        this._queuedPreparedStart = null;
                    }
                }
                return null;
            }

            const double handoffFadeSeconds = 1.5;
            nextTrack.Audio.SetPlaybackVolume(0.0f);
            await nextTrack.Audio.PlayAsync(CancellationToken.None, initialVolume: 0.0f).ConfigureAwait(false);
            nextTrack.Audio.Volume = 100f;
            this.TrackPreparedAsActive(nextTrack, "auto-enqueue on-beat start");

            _ = Task.Run(async () =>
            {
                try
                {
                    var started = DateTime.UtcNow;
                    while (!this._disposed)
                    {
                        double t = Math.Clamp((DateTime.UtcNow - started).TotalSeconds / handoffFadeSeconds, 0.0, 1.0);
                        try { currentPrepared.Audio.SetPlaybackVolume((float)Math.Cos(t * Math.PI * 0.5)); } catch { }
                        try { nextTrack.Audio.SetPlaybackVolume((float)Math.Sin(t * Math.PI * 0.5)); } catch { }
                        if (t >= 1.0)
                        {
                            break;
                        }

                        await Task.Delay(25).ConfigureAwait(false);
                    }
                }
                catch { }
                finally
                {
                    try { nextTrack.Audio.SetPlaybackVolume(1.0f); } catch { }
                    try { await currentPrepared.Audio.StopAsync().ConfigureAwait(false); } catch { }
                    this.UntrackPrepared(currentPrepared, "auto-enqueue hand-off");
                    try { currentPrepared.Audio.Dispose(); } catch { }
                    this.DeleteTempFile(currentPrepared.TempPath);
                }
            });

            lock (this._lock)
            {
                if (this.FilePaths.Count > 0 && string.Equals(this.FilePaths[0], currentOriginalPath, StringComparison.OrdinalIgnoreCase))
                {
                    this.FilePaths.RemoveAt(0);
                }

                this._previousPath = currentOriginalPath;
                if (ReferenceEquals(this._queuedPreparedStart, request))
                {
                    this._queuedPreparedStart = null;
                }
            }

            this.ApplyCurrentTrackState(nextTrack);
            ModularAudience.Audio.LogCollection.Log($"Auto enqueue one: started on-beat -> {Path.GetFileNameWithoutExtension(nextTrack.OriginalPath)}");
            return nextTrack;
        }

        private static double ComputeNextBeatWaitSeconds(AudioObj audio, double maxRemainingSeconds)
        {
            if (maxRemainingSeconds <= 0.0)
            {
                return 0.0;
            }

            float bpm = audio.Bpm > 0 ? audio.Bpm : audio.ScannedBpm;
            if (bpm <= 0f)
            {
                return Math.Min(0.25, maxRemainingSeconds);
            }

            double beatSeconds = 60.0 / bpm;
            if (beatSeconds <= 0.0)
            {
                return 0.0;
            }

            double phase = audio.CurrentTime.TotalSeconds % beatSeconds;
            double wait = phase <= 0.02 ? 0.0 : beatSeconds - phase;
            return wait <= maxRemainingSeconds ? wait : 0.0;
        }

        private async Task<PreparedPlaylistTrack?> PrepareTrackCoreAsync(string originalPath, CancellationToken ct)
        {
            string playPath = originalPath;
            string? tempPath = null;

            try
            {
                this.LogPlayback($"PrepareTrackCoreAsync: original={originalPath}");
                if (this.BeforeTrackPlay != null)
                {
                    try
                    {
                        string? preprocessed = await this.BeforeTrackPlay(originalPath, ct).ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(preprocessed) && File.Exists(preprocessed))
                        {
                            playPath = preprocessed;
                            tempPath = preprocessed;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                    }
                }

                // Create audio object for the play path (may be a temp stretched file).
                var audio = new AudioObj(playPath, load: true)
                {
                    Name = Path.GetFileNameWithoutExtension(originalPath),
                    Volume = 100f
                };

                // Resolve the BPM that represents actual playback rate (e.g. target BPM for stretched files).
                float resolvedPlayBpm = this.ResolvePlaybackBpm?.Invoke(originalPath, playPath) ?? ReadBpmTagLight(originalPath);

                // Read original file's BPM tag (if available) so we can represent stretch as a factor.
                float originalTagBpm = ReadBpmTagLight(originalPath);

                if (originalTagBpm > 0)
                {
                    // Keep original tag BPM as base and store stretch factor so UI computes effective BPM correctly.
                    audio.Bpm = originalTagBpm;
                    try { audio.StretchFactor = (double) resolvedPlayBpm / originalTagBpm; } catch { audio.StretchFactor = 1.0; }
                }
                else
                {
                    // No original BPM available: fall back to resolved playback BPM and set factor to 1.
                    audio.Bpm = resolvedPlayBpm;
                    audio.StretchFactor = 1.0;
                }

                if (audio.Data == null || audio.Data.Length == 0)
                {
                    audio.Dispose();
                    this.DeleteTempFile(tempPath);
                    return null;
                }

                // Normalize to a consistent loudness target so track-to-track level jumps are minimised.
                // This covers both direct playback and already-stretched temp files.
                PlaylistNormalizer.NormalizeToTarget(audio.Data);

                var prepared = new PreparedPlaylistTrack
                {
                    Audio = audio,
                    OriginalPath = originalPath,
                    PlayPath = playPath,
                    TempPath = tempPath
                };

                // Cache warmed-up tracks without marking them active/playing. Active tracking is
                // reserved for tracks actually started by the run-loop, otherwise the UI/loop can
                // mistake prepared-but-silent audio for real playback and dry out.
                try
                {
                    this.LogPlayback($"PrepareTrackCoreAsync: prepared ready original={originalPath} playPath={playPath} temp={tempPath}");
                    lock (this._lock)
                    {
                        this._preparedByPath[NormalizePathForKey(originalPath)] = prepared;
                    }
                }
                catch { }

                return prepared;
            }
            catch
            {
                this.DeleteTempFile(tempPath);
                return null;
            }
            finally
            {
                try { lock (this._lock) { this._preparingTasks.Remove(NormalizePathForKey(originalPath)); } } catch { }
            }
        }

        private void ApplyCurrentTrackState(PreparedPlaylistTrack prepared)
        {
            lock (this._lock)
            {
                this._primaryAudioObj = prepared.Audio;
                this._primaryOriginalPath = prepared.OriginalPath;
                this.CurrentPath = prepared.PlayPath;
                this.OriginalCurrentPath = prepared.OriginalPath;
                this.CurrentDuration = prepared.Audio.Duration;
                this.CurrentChannels = prepared.Audio.Channels;
                this.CurrentSampleRate = prepared.Audio.SampleRate;
                this.CurrentBitDepth = prepared.Audio.BitDepth;
                // Compute effective playback BPM by applying any stretch/sample-rate factors
                float effectiveBpm = 0f;
                if (prepared.Audio.Bpm > 0)
                {
                    double rateFactor = 1.0;
                    try
                    {
                        rateFactor = prepared.Audio.StretchFactor * prepared.Audio.SampleRateFactor * prepared.Audio.ManualSampleRateFactor * prepared.Audio.SyncNudgeSampleRateFactor;
                    }
                    catch { }
                    effectiveBpm = (float)(prepared.Audio.Bpm * rateFactor);
                }
                // If we couldn't compute an effective BPM from factors, fall back to the stored metadata BPM.
                if (effectiveBpm <= 0)
                {
                    effectiveBpm = prepared.Audio.Bpm;
                }

                this.CurrentBpm = effectiveBpm;
                this.IsPlaying = prepared.Audio.Playing || prepared.Audio.PlayerPlaying || prepared.Audio.Paused;
                this.IsPaused = prepared.Audio.Paused;
            }
        }

        private void ClearCurrentTrackState()
        {
            lock (this._lock)
            {
                this._primaryAudioObj = null;
                this._primaryOriginalPath = null;
                this.CurrentPath = null;
                this.OriginalCurrentPath = null;
                this.CurrentDuration = TimeSpan.Zero;
                this.CurrentChannels = 0;
                this.CurrentSampleRate = 0;
                this.CurrentBitDepth = 0;
                this.CurrentBpm = 0;
            }
        }

        private void SetSecondaryTrack(PreparedPlaylistTrack? prepared)
        {
            lock (this._lock)
            {
                this._secondaryAudioObj = prepared?.Audio;
                this._secondaryOriginalPath = prepared?.OriginalPath;
            }
        }

        private void TrackPreparedAsActive(PreparedPlaylistTrack prepared, string reason)
        {
            this.LogPlayback($"TrackPreparedAsActive called: original={prepared.OriginalPath} reason={reason}");
            bool disposePrepared = false;
            lock (this._lock)
            {
                string key = NormalizePathForKey(prepared.OriginalPath);
                // If this prepared track's original path is banned, do not activate it.
                if (!string.IsNullOrWhiteSpace(key) && this._banlist.Contains(key))
                {
                    try { LogDebug($"Playlist: prevented activation of banned track: {Path.GetFileNameWithoutExtension(prepared.OriginalPath)}"); } catch { }
                    disposePrepared = true;
                    return;
                }

                // Prevent duplicate activation for the same canonical original path.
                bool duplicateByPath = this._activePreparedTracks.Values.Any(p =>
                    p.Audio.Id != prepared.Audio.Id &&
                    string.Equals(NormalizePathForKey(p.OriginalPath), key, StringComparison.OrdinalIgnoreCase));
                if (duplicateByPath)
                {
                    try { LogDebug($"TrackPreparedAsActive: duplicate detected for {Path.GetFileNameWithoutExtension(prepared.OriginalPath)}; disposing duplicate prepared track"); } catch { }
                    disposePrepared = true;
                    return;
                }

                this._preparedByPath.Remove(key);
                this._activePreparedTracks[prepared.Audio.Id] = prepared;
            }

            if (disposePrepared)
            {
                try { prepared.Audio.Dispose(); } catch { }
                this.DeleteTempFile(prepared.TempPath);
                return;
            }

            try
            {
                string logName = Path.GetFileNameWithoutExtension(prepared.OriginalPath);
                string logBpm = prepared.Audio.Bpm > 0 ? $" [{prepared.Audio.Bpm:F0} BPM]" : string.Empty;
                int activeCount = this.ActiveAudioObjs.Count;
                LogDebug($"Playlist track initial play ({reason}): {logName}{logBpm} | active={activeCount}");
            }
            catch
            {
            }
        }

        private void UntrackPrepared(PreparedPlaylistTrack prepared, string reason)
        {
            lock (this._lock)
            {
                this._activePreparedTracks.Remove(prepared.Audio.Id);
                string preparedKey = NormalizePathForKey(prepared.OriginalPath);
                if (!string.IsNullOrWhiteSpace(preparedKey) &&
                    this._preparedByPath.TryGetValue(preparedKey, out var cached) &&
                    cached.Audio.Id == prepared.Audio.Id)
                {
                    this._preparedByPath.Remove(preparedKey);
                }
                try
                {
                    if (!string.IsNullOrWhiteSpace(prepared.OriginalPath) &&
                        (string.Equals(reason, "skipped", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(reason, "fade-out done", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(reason, "natural end", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(reason, "seam hand-off", StringComparison.OrdinalIgnoreCase)
                        ))
                    {
                        string key = NormalizePathForKey(prepared.OriginalPath);
                        if (!string.IsNullOrWhiteSpace(key))
                        {
                            this._banlist.Add(key);
                        }
                        try { LogDebug($"Playlist: banlisted track: {Path.GetFileNameWithoutExtension(prepared.OriginalPath)} (reason={reason})"); } catch { }
                    }
                }
                catch { }
            }

            try
            {
                string logName = Path.GetFileNameWithoutExtension(prepared.OriginalPath);
                int activeCount = this.ActiveAudioObjs.Count;
                LogDebug($"Playlist track inactive ({reason}): {logName} | active={activeCount}");
            }
            catch
            {
            }
        }

        private void DeleteTempFile(string? tempPath)
        {
            if (string.IsNullOrWhiteSpace(tempPath))
            {
                return;
            }

            try { File.Delete(tempPath); } catch { }
        }

        // Safe playback logging helper: must never throw
        private void LogPlayback(string message)
        {
            try { ModularAudience.Audio.LogCollection.Log($"[PlaylistEngine::Playback] {message}"); } catch { }
        }

        // â”€â”€ Tag helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>Read Duration + BPM from file tags without loading audio data.</summary>
        public static (TimeSpan Duration, float Bpm, int Channels, int SampleRate, int BitDepth) ReadMetadata(string path)
        {
            TimeSpan duration = TimeSpan.Zero;
            float bpm = 0f;
            int channels = 0, sampleRate = 0, bitDepth = 0;
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    return (TimeSpan.Zero, 0f, 0, 0, 0);
                }

                using var reader = new AudioFileReader(path);
                duration = reader.TotalTime;
                channels = reader.WaveFormat.Channels;
                sampleRate = reader.WaveFormat.SampleRate;
                bitDepth = reader.WaveFormat.BitsPerSample;
            }
            catch { }

            bpm = ReadBpmTagLight(path);
            return (duration, bpm, channels, sampleRate, bitDepth);
        }

        private static float ReadBpmTagLight(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return 0f;
            }

            try
            {
                using var file = TagLib.File.Create(path);
                if (file == null)
                {
                    return 0f;
                }

                float bpm = 0f;

                try { if (file.Tag != null && file.Tag.BeatsPerMinute > 0) { bpm = (float)file.Tag.BeatsPerMinute; } } catch { }

                if (bpm <= 0)
                {
                    try
                    {
                        if (file.TagTypes.HasFlag(TagLib.TagTypes.Id3v2))
                        {
                            var id3 = (TagLib.Id3v2.Tag?)file.GetTag(TagLib.TagTypes.Id3v2);
                            var frame = id3 != null ? TagLib.Id3v2.TextInformationFrame.Get(id3, "TBPM", false) : null;
                            if (frame != null)
                            {
                                string s = (frame.Text.FirstOrDefault() ?? "0").Replace(',', '.');
                                if (float.TryParse(s, System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture, out float v) && v > 0)
                                {
                                    bpm = v;
                                }
                            }
                        }
                    }
                    catch { }
                }

                // Some taggers store BPM * 100 (e.g. 13000 instead of 130)
                if (bpm > 1000f)
                {
                    bpm /= 100f;
                }

                if (bpm >= 30f && bpm <= 1000f)
                {
                    return bpm;
                }
            }
            catch { }
            return 0f;
        }
    }
}
