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
        // ── State ──────────────────────────────────────────────────────────────
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

        // ── Private ────────────────────────────────────────────────────────────
        private WaveOutEvent?    _waveOut;
        private AudioFileReader? _reader;
        private CancellationTokenSource? _cts;
        private string? _previousPath;          // 1-track back-history
        private readonly object _lock = new();
        private volatile bool _skipRequested;
        private volatile bool _disposed;

        private static readonly Random Rng = new();

        // ── Events ─────────────────────────────────────────────────────────────
        /// <summary>Fired on the thread-pool when the engine moves to the next track or goes idle.</summary>
        public event Action? TrackChanged;

        /// <summary>
        /// Optional preprocessor: called with the original file path before each track is played.
        /// Return a new (temp) file path to play instead, or null to play the original.
        /// The engine disposes any returned temp file after playback completes.
        /// </summary>
        public Func<string, CancellationToken, Task<string?>>? BeforeTrackPlay { get; set; }

        // ──────────────────────────────────────────────────────────────────────
        //  Public API
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>Start or resume playlist playback.</summary>
        public void Play()
        {
            if (this._disposed) return;
            lock (this._lock)
            {
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

        // ──────────────────────────────────────────────────────────────────────
        //  Internal helpers
        // ──────────────────────────────────────────────────────────────────────

        private async Task RunLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && !this._disposed)
            {
                string? path;
                lock (this._lock)
                {
                    if (this.FilePaths.Count == 0) break;
                    path = this.FilePaths[0];
                }

                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    lock (this._lock) { if (this.FilePaths.Count > 0 && this.FilePaths[0] == path) this.FilePaths.RemoveAt(0); }
                    continue;
                }

                // Run optional preprocessor (e.g. time-stretch) and get play path
                string playPath = path;
                string? tempPath = null;
                this.OriginalCurrentPath = path;
                if (this.BeforeTrackPlay != null)
                {
                    try
                    {
                        string? preprocessed = await this.BeforeTrackPlay(path, ct).ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(preprocessed) && File.Exists(preprocessed))
                        {
                            tempPath = preprocessed;
                            playPath = preprocessed;
                        }
                    }
                    catch (OperationCanceledException) { break; }
                    catch { /* preprocessing failed — play original */ }
                }

                // Play the track and wait until it ends or is skipped
                bool ok = await this.PlayTrackAsync(playPath, ct).ConfigureAwait(false);

                // Delete temp file produced by preprocessor
                if (tempPath != null)
                {
                    try { File.Delete(tempPath); } catch { }
                }

                // Remove from queue + remember for "previous"
                lock (this._lock)
                {
                    if (this.FilePaths.Count > 0 && this.FilePaths[0] == path)
                        this.FilePaths.RemoveAt(0);
                    this._previousPath = path;
                }

                TrackChanged?.Invoke();
                this._skipRequested = false;

                if (ct.IsCancellationRequested) break;
            }

            lock (this._lock)
            {
                this.IsPlaying = false;
                this.IsPaused  = false;
                this.CurrentPath = null;
                this.OriginalCurrentPath = null;
                this.CurrentDuration = TimeSpan.Zero;
            }
            TrackChanged?.Invoke();
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
            wo?.Stop();
        }

        private void StopAndDisposeCurrent()
        {
            this._cts?.Cancel();
            this._skipRequested = true;

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
                try { return this._reader?.CurrentTime ?? TimeSpan.Zero; }
                catch { return TimeSpan.Zero; }
            }
        }

        // ── Tag helpers ────────────────────────────────────────────────────────

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
