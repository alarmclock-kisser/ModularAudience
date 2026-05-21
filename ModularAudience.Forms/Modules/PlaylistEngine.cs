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
        private CancellationTokenSource? _cts;
        private string? _previousPath;          // 1-track back-history
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

        private sealed class PreparedPlaylistTrack
        {
            public required AudioObj Audio { get; init; }
            public required string OriginalPath { get; init; }
            public required string PlayPath { get; init; }
            public string? TempPath { get; init; }
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
                if (this.IsPaused && this._primaryAudioObj != null)
                {
                    try { this._primaryAudioObj.PauseAsync().GetAwaiter().GetResult(); } catch { }
                    try { this._secondaryAudioObj?.PauseAsync().GetAwaiter().GetResult(); } catch { }
                    this.IsPaused = false;
                    this.IsPlaying = true;
                    return;
                }

                if (this.IsPaused && this._waveOut != null)
                {
                    this._waveOut.Play();
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
                if (this._primaryAudioObj != null && this.IsPlaying && !this.IsPaused)
                {
                    try { this._primaryAudioObj.PauseAsync().GetAwaiter().GetResult(); } catch { }
                    try { this._secondaryAudioObj?.PauseAsync().GetAwaiter().GetResult(); } catch { }
                    this.IsPaused  = true;
                    this.IsPlaying = false;
                    return;
                }

                if (this._waveOut != null && this.IsPlaying && !this.IsPaused)
                {
                    this._waveOut.Pause();
                    this.IsPaused  = true;
                    this.IsPlaying = false;
                }
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
            await this.RunCrossfadeLoop(ct).ConfigureAwait(false);
        }

        private async Task<bool> PlayTrackAsync(string path, CancellationToken ct)
        {
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
                this.CurrentBpm = ReadBpmTagLight(path);

                TrackChanged?.Invoke();

                // Intentionally logged before Play(): runs on each new track entry via PlayTrackAsync.
                string logName = System.IO.Path.GetFileNameWithoutExtension(this.OriginalCurrentPath ?? path);
                string logBpm  = this.CurrentBpm > 0 ? $" [{this.CurrentBpm:F0} BPM]" : string.Empty;
                ModularAudience.Audio.LogCollection.Log($"Now playing: {logName}{logBpm}");

                waveOut.Play();

                // Poll until playback ends or we are asked to stop
                while (!ct.IsCancellationRequested && !this._skipRequested && !this._disposed)
                {
                    if (waveOut.PlaybackState == PlaybackState.Stopped)
                        break;

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
            this._skipRequested = true;
            WaveOutEvent? wo;
            lock (this._lock)
            {
                wo = this._waveOut;
                this.FilePaths.Insert(0, pathToInsert);
            }
            try { this._primaryAudioObj?.StopAsync().GetAwaiter().GetResult(); } catch { }
            try { this._secondaryAudioObj?.StopAsync().GetAwaiter().GetResult(); } catch { }
            wo?.Stop();
        }

        private void StopAndDisposeCurrent()
        {
            this._cts?.Cancel();
            this._skipRequested = true;

            try { this._primaryAudioObj?.StopAsync().GetAwaiter().GetResult(); } catch { }
            try { this._secondaryAudioObj?.StopAsync().GetAwaiter().GetResult(); } catch { }
            try { this._primaryAudioObj?.Dispose(); } catch { }
            try { this._secondaryAudioObj?.Dispose(); } catch { }
            this._primaryAudioObj = null;
            this._primaryOriginalPath = null;
            this._secondaryAudioObj = null;
            this._secondaryOriginalPath = null;
            this._activePreparedTracks.Clear();

            WaveOutEvent?    wo;
            AudioFileReader? rd;
            lock (this._lock) { wo = this._waveOut; rd = this._reader; this._waveOut = null; this._reader = null; }

            try { wo?.Stop();    } catch { }
            try { wo?.Dispose(); } catch { }
            try { rd?.Dispose(); } catch { }

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
                            ModularAudience.Audio.LogCollection.Log($"[PlaylistEngine] duplicate removed: {Path.GetFileNameWithoutExtension(this.FilePaths[1])}");
                            this.FilePaths.RemoveAt(1);
                        }
                        currentOriginalPath = this.FilePaths.Count > 0 ? this.FilePaths[0] : null;
                    }

                    if (string.IsNullOrWhiteSpace(currentOriginalPath))
                        break;

                    PreparedPlaylistTrack? currentPrepared;
                    if (nextPrepareTask != null && string.Equals(nextPrepareTaskPath, currentOriginalPath, StringComparison.OrdinalIgnoreCase))
                    {
                        ModularAudience.Audio.LogCollection.Log($"[PlaylistEngine] reusing pre-prepared: {Path.GetFileNameWithoutExtension(currentOriginalPath)}");
                        currentPrepared = await nextPrepareTask.ConfigureAwait(false);
                        nextPrepareTask = null;
                        nextPrepareTaskPath = null;
                    }
                    else
                    {
                        if (nextPrepareTask != null)
                        {
                            try { var ab = await nextPrepareTask.ConfigureAwait(false); if (ab != null) { this.UntrackPrepared(ab, "abandoned"); try { ab.Audio.Dispose(); } catch { } this.DeleteTempFile(ab.TempPath); } } catch { }
                            nextPrepareTask = null;
                            nextPrepareTaskPath = null;
                        }
                        currentPrepared = await this.PrepareTrackAsync(currentOriginalPath, ct).ConfigureAwait(false);
                    }

                    if (currentPrepared == null)
                    {
                        lock (this._lock)
                        {
                            if (this.FilePaths.Count > 0 && string.Equals(this.FilePaths[0], currentOriginalPath, StringComparison.OrdinalIgnoreCase))
                                this.FilePaths.RemoveAt(0);
                        }
                        continue;
                    }

                    await currentPrepared.Audio.PlayAsync(CancellationToken.None, initialVolume: 1.0f).ConfigureAwait(false);
                    currentPrepared.Audio.Volume = 100f;
                    currentPrepared.Audio.SetPlaybackVolume(1.0f);
                    this.TrackPreparedAsActive(currentPrepared, "initial play");
                    this.ApplyCurrentTrackState(currentPrepared);
                    TrackChanged?.Invoke();
                    ModularAudience.Audio.LogCollection.Log($"[PlaylistEngine] Now playing: {Path.GetFileNameWithoutExtension(currentOriginalPath)} | active={this.ActiveAudioObjs.Count}");

                    bool crossfadeTriggered = false;

                    while (!ct.IsCancellationRequested && !this._disposed && !this._skipRequested)
                    {
                        if (!currentPrepared.Audio.Playing)
                        {
                            ModularAudience.Audio.LogCollection.Log($"[PlaylistEngine] track ended: {Path.GetFileNameWithoutExtension(currentOriginalPath)}");
                            break;
                        }

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

                        if (!string.IsNullOrWhiteSpace(nextOriginalPath) && remainingSeconds <= preprocessLeadSeconds)
                        {
                            if (nextPrepareTask == null || !string.Equals(nextPrepareTaskPath, nextOriginalPath, StringComparison.OrdinalIgnoreCase))
                            {
                                if (nextPrepareTask != null)
                                {
                                    try { var stale = await nextPrepareTask.ConfigureAwait(false); if (stale != null) { this.UntrackPrepared(stale, "stale"); try { stale.Audio.Dispose(); } catch { } this.DeleteTempFile(stale.TempPath); } } catch { }
                                }
                                nextPrepareTask = this.PrepareTrackAsync(nextOriginalPath, ct);
                                nextPrepareTaskPath = nextOriginalPath;
                                ModularAudience.Audio.LogCollection.Log($"[PlaylistEngine] pre-preparing: {Path.GetFileNameWithoutExtension(nextOriginalPath)} (remaining={remainingSeconds:F1}s)");
                            }
                        }

                        if (!crossfadeTriggered && !string.IsNullOrWhiteSpace(nextOriginalPath) && effectiveCrossfade > 0 && remainingSeconds <= effectiveCrossfade)
                        {
                            crossfadeTriggered = true;
                            ModularAudience.Audio.LogCollection.Log($"[PlaylistEngine] crossfade trigger: remaining={remainingSeconds:F1}s cf={crossfadeDuration:F1}s eff={effectiveCrossfade:F1}s");

                            PreparedPlaylistTrack? nextTrack;
                            if (nextPrepareTask != null && string.Equals(nextPrepareTaskPath, nextOriginalPath, StringComparison.OrdinalIgnoreCase))
                                nextTrack = await nextPrepareTask.ConfigureAwait(false);
                            else
                                nextTrack = await this.PrepareTrackAsync(nextOriginalPath, ct).ConfigureAwait(false);
                            nextPrepareTask = null;
                            nextPrepareTaskPath = null;

                            if (nextTrack != null)
                            {
                                // Final cap once we know the next track's actual duration.
                                double nextDurationSeconds = nextTrack.Audio.Duration.TotalSeconds;
                                double currentRemainingNow = Math.Max(0.0, (currentPrepared.Audio.Duration - currentPrepared.Audio.CurrentTime).TotalSeconds);
                                double fadeDuration = effectiveCrossfade;
                                if (nextDurationSeconds > 0.0)
                                {
                                    fadeDuration = Math.Min(fadeDuration, nextDurationSeconds * 0.5);
                                }
                                if (currentRemainingNow > 0.0)
                                {
                                    fadeDuration = Math.Min(fadeDuration, currentRemainingNow);
                                }
                                fadeDuration = Math.Max(0.1, fadeDuration);
                                var fadingOut = currentPrepared;

                                nextTrack.Audio.Volume = 100f;
                                nextTrack.Audio.SetPlaybackVolume(0.0f);
                                await nextTrack.Audio.PlayAsync(CancellationToken.None, initialVolume: 0.0f).ConfigureAwait(false);
                                this.TrackPreparedAsActive(nextTrack, "crossfade start");

                                fadeOutTasks.Add(Task.Run(async () =>
                                {
                                    try
                                    {
                                        var started = DateTime.UtcNow;
                                        while (!this._disposed && fadingOut.Audio.Playing)
                                        {
                                            double elapsed = (DateTime.UtcNow - started).TotalSeconds;
                                            float vol = (float) Math.Clamp(1.0 - elapsed / Math.Max(0.001, fadeDuration), 0.0, 1.0);
                                            fadingOut.Audio.SetPlaybackVolume(vol);
                                            if (vol <= 0f) { try { await fadingOut.Audio.StopAsync().ConfigureAwait(false); } catch { } break; }
                                            await Task.Delay(25).ConfigureAwait(false);
                                        }
                                    }
                                    catch { }
                                    finally
                                    {
                                        this.UntrackPrepared(fadingOut, "fade-out done");
                                        try { fadingOut.Audio.Dispose(); } catch { }
                                        this.DeleteTempFile(fadingOut.TempPath);
                                        ModularAudience.Audio.LogCollection.Log($"[PlaylistEngine] fade-out done: {Path.GetFileNameWithoutExtension(fadingOut.OriginalPath)} | active={this.ActiveAudioObjs.Count}");
                                    }
                                }));

                                var fadingIn = nextTrack;
                                double fadeInDuration = fadeDuration;
                                fadeOutTasks.Add(Task.Run(async () =>
                                {
                                    try
                                    {
                                        var started = DateTime.UtcNow;
                                        while (!this._disposed && fadingIn.Audio.Playing)
                                        {
                                            double elapsed = (DateTime.UtcNow - started).TotalSeconds;
                                            float vol = (float) Math.Clamp(elapsed / Math.Max(0.001, fadeInDuration), 0.0, 1.0);
                                            fadingIn.Audio.SetPlaybackVolume(vol);
                                            if (vol >= 1f) break;
                                            await Task.Delay(25).ConfigureAwait(false);
                                        }
                                        fadingIn.Audio.SetPlaybackVolume(1.0f);
                                    }
                                    catch { }
                                }));

                                lock (this._lock)
                                {
                                    if (this.FilePaths.Count > 0 && string.Equals(this.FilePaths[0], currentOriginalPath, StringComparison.OrdinalIgnoreCase))
                                        this.FilePaths.RemoveAt(0);
                                    this._previousPath = currentOriginalPath;
                                }

                                this.ApplyCurrentTrackState(nextTrack);
                                TrackChanged?.Invoke();

                                if (this.CrossfadeStartedAsync != null)
                                    _ = Task.Run(() => this.CrossfadeStartedAsync(fadingOut.Audio, nextTrack.Audio), CancellationToken.None);

                                ModularAudience.Audio.LogCollection.Log($"[PlaylistEngine] crossfade -> {Path.GetFileNameWithoutExtension(nextOriginalPath)} | active={this.ActiveAudioObjs.Count}");
                                break;
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
                                this.FilePaths.RemoveAt(0);
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
                                this.FilePaths.RemoveAt(0);
                            this._previousPath = currentOriginalPath;
                        }
                        this.ClearCurrentTrackState();
                        this.SetSecondaryTrack((PreparedPlaylistTrack?) null);
                        TrackChanged?.Invoke();
                    }
                    // crossfadeTriggered: old track fading out in background, queue already advanced
                }

                if (fadeOutTasks.Count > 0)
                    await Task.WhenAll(fadeOutTasks).ConfigureAwait(false);
            }
            finally
            {
                if (nextPrepareTask != null)
                {
                    try { var ab = await nextPrepareTask.ConfigureAwait(false); if (ab != null) { this.UntrackPrepared(ab, "abandoned"); try { ab.Audio.Dispose(); } catch { } this.DeleteTempFile(ab.TempPath); } } catch { }
                }
                if (fadeOutTasks.Count > 0)
                    try { await Task.WhenAll(fadeOutTasks).ConfigureAwait(false); } catch { }

                this.ClearCurrentTrackState();
                this.SetSecondaryTrack((PreparedPlaylistTrack?) null);
                lock (this._lock)
                {
                    this._activePreparedTracks.Clear();
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
            if (string.IsNullOrWhiteSpace(originalPath) || !File.Exists(originalPath))
            {
                return null;
            }

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
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                }
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
                this.CurrentBpm = prepared.Audio.Bpm;
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
            lock (this._lock)
            {
                this._activePreparedTracks[prepared.Audio.Id] = prepared;
            }

            try
            {
                string logName = Path.GetFileNameWithoutExtension(prepared.OriginalPath);
                string logBpm = prepared.Audio.Bpm > 0 ? $" [{prepared.Audio.Bpm:F0} BPM]" : string.Empty;
                int activeCount = this.ActiveAudioObjs.Count;
                ModularAudience.Audio.LogCollection.Log($"Playlist track initial play ({reason}): {logName}{logBpm} | active={activeCount}");
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
            }

            try
            {
                string logName = Path.GetFileNameWithoutExtension(prepared.OriginalPath);
                int activeCount = this.ActiveAudioObjs.Count;
                ModularAudience.Audio.LogCollection.Log($"Playlist track inactive ({reason}): {logName} | active={activeCount}");
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

        // â”€â”€ Tag helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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
