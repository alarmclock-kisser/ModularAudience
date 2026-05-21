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
        private readonly List<PlaylistSlot> _slots = [];
        private CancellationTokenSource? _cts;
        private string? _previousPath;
        private readonly object _lock = new();
        private volatile bool _skipRequested;
        private volatile bool _disposed;

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
                    return this._slots.Select(s => s.Audio).DistinctBy(a => a.Id).ToArray();
            }
        }
        public IReadOnlyList<string> ActiveOriginalPaths
        {
            get
            {
                lock (this._lock)
                    return this._slots.Select(s => s.OriginalPath)
                        .Where(p => !string.IsNullOrWhiteSpace(p))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
            }
        }
        public AudioObj? PrimaryAudioObj
        {
            get { lock (this._lock) return this._slots.FirstOrDefault(s => s.IsPrimary)?.Audio; }
        }

        private const double CrossfadePreprocessLeadSeconds = 15.0;

        private sealed class PreparedPlaylistTrack
        {
            public required AudioObj Audio { get; init; }
            public required string OriginalPath { get; init; }
            public required string PlayPath { get; init; }
            public string? TempPath { get; init; }
        }

        /// <summary>One active audio slot — primary (current) or fading-out (crossfade).</summary>
        private sealed class PlaylistSlot
        {
            public required PreparedPlaylistTrack Track { get; init; }
            /// <summary>True while this slot drives the main playback position.</summary>
            public bool IsPrimary { get; set; }
            /// <summary>True while a fade-out background task is running; slot is removed when done.</summary>
            public bool FadingOut { get; set; }

            public AudioObj Audio => this.Track.Audio;
            public string OriginalPath => this.Track.OriginalPath;
        }

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        //  Public API
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>Start or resume playlist playback.</summary>
        public void Play()
        {
            if (this._disposed) return;
            lock (this._lock)
            {
                if (this.IsPaused)
                {
                    foreach (var slot in this._slots)
                        try { slot.Audio.PauseAsync().GetAwaiter().GetResult(); } catch { }
                    this.IsPaused = false;
                    this.IsPlaying = true;
                    return;
                }

                if (this.IsPlaying) return;
                if (this.FilePaths.Count == 0) return;
            }

            this._cts = new CancellationTokenSource();
            _ = Task.Run(() => this.RunLoop(this._cts.Token));
        }

        /// <summary>Pause current playback without advancing the queue.</summary>
        public void Pause()
        {
            lock (this._lock)
            {
                if (!this.IsPlaying || this.IsPaused) return;
                foreach (var slot in this._slots)
                    try { slot.Audio.PauseAsync().GetAwaiter().GetResult(); } catch { }
                this.IsPaused  = true;
                this.IsPlaying = false;
            }
        }

        /// <summary>Toggle between play and pause.</summary>
        public void TogglePlayPause()
        {
            if (this.IsPaused || (!this.IsPlaying && this.FilePaths.Count > 0))
                this.Play();
            else
                this.Pause();
        }

        /// <summary>
        /// Rewind: if position > 1 s, seek to start of current track;
        /// otherwise go back to the previous track (if any).
        /// </summary>
        public void RewindOrPrevious()
        {
            AudioObj? primary;
            lock (this._lock) primary = this._slots.FirstOrDefault(s => s.IsPrimary)?.Audio;

            if (primary != null && primary.CurrentTime.TotalSeconds > 1.0)
            {
                primary.StartingOffset = 0;
                return;
            }

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
            this._skipRequested = true;
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
            // Cancel the run-loop task first so IsPlaying cannot be set back to true after we clear
            this._cts?.Cancel();
            this._skipRequested = true;
            this.StopAndDisposeCurrent();
            lock (this._lock)
            {
                this.FilePaths.Clear();
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
            this._disposed = true;
            this.StopAndDisposeCurrent();
        }

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        //  Internal helpers
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private async Task RunLoop(CancellationToken ct)
        {
            await this.RunSlotLoop(ct).ConfigureAwait(false);
        }

        // ── Slot helpers ──────────────────────────────────────────────────────

        private PlaylistSlot AddSlot(PreparedPlaylistTrack track, bool isPrimary)
        {
            var slot = new PlaylistSlot { Track = track, IsPrimary = isPrimary };
            lock (this._lock) this._slots.Add(slot);
            string logBpm = track.Audio.Bpm > 0 ? $" [{track.Audio.Bpm:F0} BPM]" : string.Empty;
            ModularAudience.Audio.LogCollection.Log(
                $"[Slot+] {(isPrimary ? "PRIMARY" : "FADING-IN")} {Path.GetFileNameWithoutExtension(track.OriginalPath)}{logBpm} | slots={this._slots.Count}");
            return slot;
        }

        private void RemoveSlotAndDispose(PlaylistSlot slot, string reason)
        {
            lock (this._lock) this._slots.Remove(slot);
            ModularAudience.Audio.LogCollection.Log(
                $"[Slot-] {reason}: {Path.GetFileNameWithoutExtension(slot.OriginalPath)} | slots={this._slots.Count}");
            try { slot.Audio.StopAsync().GetAwaiter().GetResult(); } catch { }
            try { slot.Audio.Dispose(); } catch { }
            this.DeleteTempFile(slot.Track.TempPath);
        }

        private Task StartFadeOut(PlaylistSlot slot, double durationSeconds)
        {
            slot.FadingOut = true;
            slot.IsPrimary = false;
            double dur = Math.Max(0.3, durationSeconds);
            return Task.Run(async () =>
            {
                try
                {
                    var started = DateTime.UtcNow;
                    while (!this._disposed && slot.Audio.Playing)
                    {
                        double elapsed = (DateTime.UtcNow - started).TotalSeconds;
                        float vol = (float) Math.Clamp(1.0 - elapsed / dur, 0.0, 1.0);
                        try { slot.Audio.SetPlaybackVolume(vol); } catch { }
                        if (vol <= 0f) break;
                        await Task.Delay(20).ConfigureAwait(false);
                    }
                }
                catch { }
                finally { this.RemoveSlotAndDispose(slot, "fade-out"); }
            });
        }

        private Task StartFadeIn(PlaylistSlot slot, double durationSeconds)
        {
            double dur = Math.Max(0.3, durationSeconds);
            return Task.Run(async () =>
            {
                try
                {
                    var started = DateTime.UtcNow;
                    while (!this._disposed && slot.Audio.Playing)
                    {
                        double elapsed = (DateTime.UtcNow - started).TotalSeconds;
                        float vol = (float) Math.Clamp(elapsed / dur, 0.0, 1.0);
                        try { slot.Audio.SetPlaybackVolume(vol); } catch { }
                        if (vol >= 1f) break;
                        await Task.Delay(20).ConfigureAwait(false);
                    }
                    try { slot.Audio.SetPlaybackVolume(1.0f); } catch { }
                }
                catch { }
            });
        }

        private void UpdateCurrentTrackState(PlaylistSlot primary)
        {
            lock (this._lock)
            {
                this.CurrentPath = primary.Track.PlayPath;
                this.OriginalCurrentPath = primary.Track.OriginalPath;
                this.CurrentDuration = primary.Audio.Duration;
                this.CurrentChannels = primary.Audio.Channels;
                this.CurrentSampleRate = primary.Audio.SampleRate;
                this.CurrentBitDepth = primary.Audio.BitDepth;
                this.CurrentBpm = primary.Audio.Bpm;
                this.IsPlaying = primary.Audio.Playing || primary.Audio.PlayerPlaying || primary.Audio.Paused;
                this.IsPaused = primary.Audio.Paused;
            }
        }

        private void ClearEngineState()
        {
            lock (this._lock)
            {
                this.CurrentPath = null;
                this.OriginalCurrentPath = null;
                this.CurrentDuration = TimeSpan.Zero;
                this.CurrentChannels = 0;
                this.CurrentSampleRate = 0;
                this.CurrentBitDepth = 0;
                this.CurrentBpm = 0;
                this.IsPlaying = false;
                this.IsPaused = false;
            }
        }

        private void StopAndDisposeCurrent()
        {
            this._cts?.Cancel();
            this._skipRequested = true;

            List<PlaylistSlot> toDispose;
            lock (this._lock)
            {
                toDispose = new List<PlaylistSlot>(this._slots);
                this._slots.Clear();
            }
            foreach (var s in toDispose)
            {
                try { s.Audio.StopAsync().GetAwaiter().GetResult(); } catch { }
                try { s.Audio.Dispose(); } catch { }
                this.DeleteTempFile(s.Track.TempPath);
            }

            try { this._cts?.Dispose(); } catch { }
            this._cts = null;
        }

        private void StopCurrentAndInsert(string pathToInsert)
        {
            lock (this._lock) this.FilePaths.Insert(0, pathToInsert);
            this._skipRequested = true;
        }

        private TimeSpan GetCurrentPosition()
        {
            lock (this._lock)
            {
                try { return this._slots.FirstOrDefault(s => s.IsPrimary)?.Audio.CurrentTime ?? TimeSpan.Zero; }
                catch { return TimeSpan.Zero; }
            }
        }

        private double GetCrossfadeDuration()
        {
            try { return Math.Max(0.0, this.CrossfadeDurationProvider?.Invoke() ?? 0.0); }
            catch { return 0.0; }
        }

        // ── Main slot loop ─────────────────────────────────────────────────────

        private const int MaxSlots = 4;

        private async Task RunSlotLoop(CancellationToken ct)
        {
            Task<PreparedPlaylistTrack?>? prepareTask = null;
            string? prepareTaskPath = null;
            var fadeTasks = new List<Task>();

            try
            {
                while (!ct.IsCancellationRequested && !this._disposed)
                {
                    fadeTasks.RemoveAll(t => t.IsCompleted);

                    string? currentPath;
                    lock (this._lock)
                    {
                        while (this.FilePaths.Count > 1 &&
                               string.Equals(this.FilePaths[0], this.FilePaths[1], StringComparison.OrdinalIgnoreCase))
                        {
                            ModularAudience.Audio.LogCollection.Log(
                                $"[PlaylistEngine] duplicate removed: {Path.GetFileNameWithoutExtension(this.FilePaths[1])}");
                            this.FilePaths.RemoveAt(1);
                        }
                        currentPath = this.FilePaths.Count > 0 ? this.FilePaths[0] : null;
                    }

                    if (string.IsNullOrWhiteSpace(currentPath))
                        break;

                    PreparedPlaylistTrack? current;
                    if (prepareTask != null && string.Equals(prepareTaskPath, currentPath, StringComparison.OrdinalIgnoreCase))
                    {
                        current = await prepareTask.ConfigureAwait(false);
                        prepareTask = null;
                        prepareTaskPath = null;
                        ModularAudience.Audio.LogCollection.Log(
                            $"[PlaylistEngine] reusing pre-prepared: {Path.GetFileNameWithoutExtension(currentPath)}");
                    }
                    else
                    {
                        if (prepareTask != null)
                        {
                            try
                            {
                                var stale = await prepareTask.ConfigureAwait(false);
                                if (stale != null) { try { stale.Audio.Dispose(); } catch { } this.DeleteTempFile(stale.TempPath); }
                            }
                            catch { }
                            prepareTask = null;
                            prepareTaskPath = null;
                        }
                        current = await this.PrepareTrackAsync(currentPath, ct).ConfigureAwait(false);
                    }

                    if (current == null)
                    {
                        lock (this._lock)
                        {
                            if (this.FilePaths.Count > 0 &&
                                string.Equals(this.FilePaths[0], currentPath, StringComparison.OrdinalIgnoreCase))
                                this.FilePaths.RemoveAt(0);
                        }
                        continue;
                    }

                    var primarySlot = this.AddSlot(current, isPrimary: true);
                    await current.Audio.PlayAsync(CancellationToken.None, initialVolume: 1.0f).ConfigureAwait(false);
                    current.Audio.Volume = 100f;
                    current.Audio.SetPlaybackVolume(1.0f);
                    this.UpdateCurrentTrackState(primarySlot);
                    TrackChanged?.Invoke();
                    ModularAudience.Audio.LogCollection.Log(
                        $"[PlaylistEngine] NOW PLAYING: {Path.GetFileNameWithoutExtension(currentPath)} | slots={this._slots.Count}");

                    bool crossfadeDone = false;

                    while (!ct.IsCancellationRequested && !this._disposed && !this._skipRequested)
                    {
                        if (!current.Audio.Playing)
                        {
                            ModularAudience.Audio.LogCollection.Log(
                                $"[PlaylistEngine] track ended: {Path.GetFileNameWithoutExtension(currentPath)}");
                            break;
                        }

                        TimeSpan remaining = current.Audio.Duration - current.Audio.CurrentTime;
                        double remainingSeconds = Math.Max(0.0, remaining.TotalSeconds);
                        double cfDuration = this.GetCrossfadeDuration();
                        double prepLead = cfDuration + CrossfadePreprocessLeadSeconds;

                        string? nextPath;
                        lock (this._lock)
                        {
                            string? candidate = this.FilePaths.Count > 1 ? this.FilePaths[1] : null;
                            nextPath = string.Equals(candidate, currentPath, StringComparison.OrdinalIgnoreCase)
                                ? null : candidate;
                        }

                        // ── Pre-prepare next track (non-blocking fire-and-forget task) ────────
                        if (!string.IsNullOrWhiteSpace(nextPath) && remainingSeconds <= prepLead)
                        {
                            if (prepareTask == null ||
                                !string.Equals(prepareTaskPath, nextPath, StringComparison.OrdinalIgnoreCase))
                            {
                                // Discard stale prepare without blocking the loop
                                if (prepareTask != null)
                                {
                                    var staleTask = prepareTask;
                                    _ = Task.Run(async () =>
                                    {
                                        try
                                        {
                                            var stale = await staleTask.ConfigureAwait(false);
                                            if (stale != null) { try { stale.Audio.Dispose(); } catch { } this.DeleteTempFile(stale.TempPath); }
                                        }
                                        catch { }
                                    });
                                }
                                prepareTask = this.PrepareTrackAsync(nextPath, ct);
                                prepareTaskPath = nextPath;
                                ModularAudience.Audio.LogCollection.Log(
                                    $"[PlaylistEngine] pre-preparing: {Path.GetFileNameWithoutExtension(nextPath)} (rem={remainingSeconds:F1}s)");
                            }
                        }

                        // ── Crossfade trigger (only when prepare is complete) ──────────────
                        if (!crossfadeDone && !string.IsNullOrWhiteSpace(nextPath) &&
                            cfDuration > 0 && remainingSeconds <= cfDuration &&
                            this._slots.Count < MaxSlots)
                        {
                            // Ensure prepare task is running for the next track
                            if (prepareTask == null ||
                                !string.Equals(prepareTaskPath, nextPath, StringComparison.OrdinalIgnoreCase))
                            {
                                prepareTask = this.PrepareTrackAsync(nextPath, ct);
                                prepareTaskPath = nextPath;
                                ModularAudience.Audio.LogCollection.Log(
                                    $"[PlaylistEngine] crossfade: kicked off prepare (rem={remainingSeconds:F1}s)");
                            }

                            // Don't block the loop — wait until prepare finishes naturally
                            if (!prepareTask.IsCompleted)
                            {
                                await Task.Delay(25, ct).ConfigureAwait(false);
                                continue;
                            }

                            crossfadeDone = true;
                            ModularAudience.Audio.LogCollection.Log(
                                $"[PlaylistEngine] crossfade trigger: rem={remainingSeconds:F1}s cf={cfDuration:F1}s");

                            PreparedPlaylistTrack? nextTrack;
                            try { nextTrack = await prepareTask.ConfigureAwait(false); }
                            catch { nextTrack = null; }
                            prepareTask = null;
                            prepareTaskPath = null;

                            if (nextTrack != null)
                            {
                                lock (this._lock)
                                {
                                    if (this.FilePaths.Count > 0 &&
                                        string.Equals(this.FilePaths[0], currentPath, StringComparison.OrdinalIgnoreCase))
                                        this.FilePaths.RemoveAt(0);
                                    this._previousPath = currentPath;
                                }

                                var nextSlot = this.AddSlot(nextTrack, isPrimary: true);
                                primarySlot.IsPrimary = false;
                                await nextTrack.Audio.PlayAsync(CancellationToken.None, initialVolume: 0.0f).ConfigureAwait(false);
                                nextTrack.Audio.Volume = 100f;

                                this.UpdateCurrentTrackState(nextSlot);
                                TrackChanged?.Invoke();

                                if (this.CrossfadeStartedAsync != null)
                                    _ = Task.Run(() => this.CrossfadeStartedAsync!(primarySlot.Audio, nextTrack.Audio), CancellationToken.None);

                                fadeTasks.Add(this.StartFadeOut(primarySlot, cfDuration));
                                fadeTasks.Add(this.StartFadeIn(nextSlot, cfDuration));

                                ModularAudience.Audio.LogCollection.Log(
                                    $"[PlaylistEngine] crossfade -> {Path.GetFileNameWithoutExtension(nextPath)} | slots={this._slots.Count}");

                                primarySlot = nextSlot;
                                current = nextTrack;
                                currentPath = nextPath;
                                crossfadeDone = false;
                                continue;
                            }
                            else
                            {
                                crossfadeDone = false;
                                ModularAudience.Audio.LogCollection.Log(
                                    $"[PlaylistEngine] crossfade prepare FAILED: {nextPath}");
                            }
                        }

                        await Task.Delay(25, ct).ConfigureAwait(false);
                    }

                    if (this._skipRequested)
                    {
                        lock (this._lock) this._slots.Remove(primarySlot);
                        try { await primarySlot.Audio.StopAsync().ConfigureAwait(false); } catch { }
                        try { primarySlot.Audio.Dispose(); } catch { }
                        this.DeleteTempFile(current.TempPath);

                        lock (this._lock)
                        {
                            if (this.FilePaths.Count > 0 &&
                                string.Equals(this.FilePaths[0], currentPath, StringComparison.OrdinalIgnoreCase))
                                this.FilePaths.RemoveAt(0);
                        }
                        this._skipRequested = false;
                        this.ClearEngineState();
                        TrackChanged?.Invoke();
                    }
                    else if (!crossfadeDone)
                    {
                        lock (this._lock) this._slots.Remove(primarySlot);
                        try { primarySlot.Audio.Dispose(); } catch { }
                        this.DeleteTempFile(current.TempPath);

                        lock (this._lock)
                        {
                            if (this.FilePaths.Count > 0 &&
                                string.Equals(this.FilePaths[0], currentPath, StringComparison.OrdinalIgnoreCase))
                                this.FilePaths.RemoveAt(0);
                            this._previousPath = currentPath;
                        }
                        this.ClearEngineState();
                        TrackChanged?.Invoke();
                    }
                }

                if (fadeTasks.Count > 0)
                    try { await Task.WhenAll(fadeTasks).ConfigureAwait(false); } catch { }
            }
            finally
            {
                if (prepareTask != null)
                    try
                    {
                        var ab = await prepareTask.ConfigureAwait(false);
                        if (ab != null) { try { ab.Audio.Dispose(); } catch { } this.DeleteTempFile(ab.TempPath); }
                    }
                    catch { }

                if (fadeTasks.Count > 0)
                    try { await Task.WhenAll(fadeTasks).ConfigureAwait(false); } catch { }

                List<PlaylistSlot> leftover;
                lock (this._lock) { leftover = new List<PlaylistSlot>(this._slots); this._slots.Clear(); }
                foreach (var s in leftover)
                {
                    try { s.Audio.StopAsync().GetAwaiter().GetResult(); } catch { }
                    try { s.Audio.Dispose(); } catch { }
                    this.DeleteTempFile(s.Track.TempPath);
                }

                this.ClearEngineState();
                TrackChanged?.Invoke();
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

        // â”€â”€ Tag helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private async Task<PreparedPlaylistTrack?> PrepareTrackAsync(string originalPath, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(originalPath) || !File.Exists(originalPath))
                return null;

            string playPath = originalPath;
            string? tempPath = null;

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
                catch (OperationCanceledException) { throw; }
                catch { }
            }

            try
            {
                var audio = new AudioObj(playPath, load: true)
                {
                    Name = Path.GetFileNameWithoutExtension(originalPath),
                    Volume = 100f,
                    Bpm = this.ResolvePlaybackBpm?.Invoke(originalPath, playPath) ?? ReadBpmTagLight(originalPath)
                };

                if (audio.Data == null || audio.Data.Length == 0)
                {
                    audio.Dispose();
                    this.DeleteTempFile(tempPath);
                    return null;
                }

                return new PreparedPlaylistTrack
                {
                    Audio = audio,
                    OriginalPath = originalPath,
                    PlayPath = playPath,
                    TempPath = tempPath
                };
            }
            catch
            {
                this.DeleteTempFile(tempPath);
                return null;
            }
        }

        /// <summary>Read Duration + BPM from file tags without loading audio data.</summary>
        public static (TimeSpan Duration, float Bpm, int Channels, int SampleRate, int BitDepth) ReadMetadata(string path)
        {
            TimeSpan duration = TimeSpan.Zero;
            float bpm = 0f;
            int channels = 0, sampleRate = 0, bitDepth = 0;
            try
            {
                using var reader = new AudioFileReader(path);
                duration   = reader.TotalTime;
                channels   = reader.WaveFormat.Channels;
                sampleRate = reader.WaveFormat.SampleRate;
                bitDepth   = reader.WaveFormat.BitsPerSample;
            }
            catch { }

            bpm = ReadBpmTagLight(path);
            return (duration, bpm, channels, sampleRate, bitDepth);
        }

        private static float ReadBpmTagLight(string path)
        {
            try
            {
                using var file = TagLib.File.Create(path);
                float bpm = 0f;

                if (file.Tag.BeatsPerMinute > 0)
                    bpm = (float) file.Tag.BeatsPerMinute;

                if (bpm <= 0 && file.TagTypes.HasFlag(TagLib.TagTypes.Id3v2))
                {
                    var id3 = (TagLib.Id3v2.Tag) file.GetTag(TagLib.TagTypes.Id3v2);
                    var frame = TagLib.Id3v2.TextInformationFrame.Get(id3, "TBPM", false);
                    if (frame != null)
                    {
                        string s = (frame.Text.FirstOrDefault() ?? "0").Replace(',', '.');
                        if (float.TryParse(s, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out float v) && v > 0)
                            bpm = v;
                    }
                }

                // Some taggers store BPM * 100 (e.g. 13000 instead of 130)
                if (bpm > 1000f)
                    bpm /= 100f;

                if (bpm >= 30f && bpm <= 1000f)
                    return bpm;
            }
            catch { }
            return 0f;
        }
    }
}
