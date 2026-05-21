using ModularAudience.Audio;
using ModularAudience.Audio.Processors_V1;
using ModularAudience.Audio.Processors_V2;
using ModularAudience.Audio.Processors_V3;
using ModularAudience.Forms.Helpers;
using ModularAudience.Forms.Modules;
using ModularAudience.Forms.Modules.Dialogs;
using NAudio.Wave;
using System.Text;
using System.Threading;

namespace ModularAudience.Forms
{
    public partial class WindowMain
    {
        // ── Engine + timer ─────────────────────────────────────────────────────
        private PlaylistEngine _playlist = new();
        private System.Windows.Forms.Timer? _playlistTimer;

        // Metadata cache: path → (duration, bpm, channels, sampleRate, bitDepth)
        private readonly Dictionary<string, (TimeSpan Duration, float Bpm, int Channels, int SampleRate, int BitDepth)>
            _playlistMetaCache = [];

        // Auto-timestretch settings (null = disabled)
        private PlaylistStretchSettings? _playlistStretchSettings;

        // Preprocessing status flag
        private volatile bool _isPreprocessingTrack;

        // ── Recording Track-Log ────────────────────────────────────────────────
        private sealed class TrackLogEntry
        {
            public TimeSpan Start { get; init; }
            public TimeSpan? End { get; set; }
            public string TrackId { get; init; } = string.Empty;
        }

        private string? _trackLogFilePath;               // set when a recording begins
        private DateTime? _trackLogRecordStart;          // UTC time when recording started
        private readonly List<TrackLogEntry> _trackLog = [];
        private HashSet<string> _trackLogActivePaths = new(StringComparer.OrdinalIgnoreCase);

        // ── Initializer (called from constructor) ──────────────────────────────
        private void InitPlaylist()
        {
            // Keep engine's list in sync with the static field
            // (engine owns the list reference we swap PlaylistFilePaths with)
            PlaylistFilePaths = this._playlist.FilePaths;

            this._playlist.TrackChanged += () =>
            {
                // Track-log: close previous entry, open new one
                this.OnPlaylistTrackChanged();
                WindowMainStaticHelpers.InvokeIfRequired(Instance, this.UpdatePlaylistUI);
                WindowMainStaticHelpers.InvokeIfRequired(Instance, this.UpdatePlaylistHoverTitle);
            };

            this._playlist.BeforeTrackPlay = this.PreprocessPlaylistTrackAsync;
            this._playlist.CrossfadeDurationProvider = () => WindowMain.CrossfadeDurationSeconds;
            this._playlist.ResolvePlaybackBpm = this.ResolvePlaylistPlaybackBpm;
            this._playlist.CrossfadeStartedAsync = this.HandlePlaylistCrossfadeStartedAsync;

            this._playlistTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            this._playlistTimer.Tick += (_, _) => this.UpdatePlaylistUI();
            this._playlistTimer.Start();

            // Ensure playlist context menu contains Add next / Enqueue last entries
            try
            {
                var addNextMenuItem = new ToolStripMenuItem("Add next", null, this.playlistMenu_AddNext_Click);
                var enqueueLastMenuItem = new ToolStripMenuItem("Enqueue last", null, this.playlistMenu_EnqueueLast_Click);
                // Insert near top so it's easy to find (after Play/Pause)
                if (this.contextMenuStrip_playlist != null)
                {
                    this.contextMenuStrip_playlist.Items.Add(new ToolStripSeparator());
                    this.contextMenuStrip_playlist.Items.Add(addNextMenuItem);
                    this.contextMenuStrip_playlist.Items.Add(enqueueLastMenuItem);
                }
            }
            catch { }
        }

        private float ResolvePlaylistPlaybackBpm(string originalPath, string playPath)
        {
            float bpm = PlaylistEngine.ReadMetadata(originalPath).Bpm;
            if (bpm <= 0 && this._playlistStretchSettings != null)
            {
                bpm = this._playlistStretchSettings.TargetBpm;
            }

            if (bpm <= 0 && !string.Equals(playPath, originalPath, StringComparison.OrdinalIgnoreCase))
            {
                bpm = PlaylistEngine.ReadMetadata(playPath).Bpm;
            }

            return bpm;
        }

        private async Task HandlePlaylistCrossfadeStartedAsync(AudioObj currentTrack, AudioObj nextTrack)
        {
            if (Instance == null || Instance.IsDisposed)
            {
                return;
            }

            try
            {
                await Task.Delay(20).ConfigureAwait(false);

                using CancellationTokenSource syncWindow = new(TimeSpan.FromMilliseconds(500));
                List<AudioObj> playingTracks = new();

                playingTracks.AddRange(this._playlist.ActiveAudioObjs.Where(audio => audio.PlayerPlaying));
                playingTracks.AddRange(TrackViews
                    .Where(tv => tv != null && !tv.IsDisposed && tv.OriginalAudio.PlayerPlaying)
                    .Select(tv => tv.OriginalAudio));

                playingTracks = playingTracks
                    .Where(audio => audio != null && audio.PlayerPlaying)
                    .Distinct()
                    .ToList();

                if (playingTracks.Count < 2)
                {
                    playingTracks = Enumerable
                        .Repeat(currentTrack, 1)
                        .Concat(Enumerable.Repeat(nextTrack, 1))
                        .Where(audio => audio.PlayerPlaying)
                        .Distinct()
                        .ToList();
                }

                var syncer = new PausingPlaybackSyncer(playingTracks, syncWindow.Token, frequency: 0.05, grain: 12);
                LogCollection.Log("Playlist crossfade: 500 ms beat sync window started.");

                try
                {
                    await Task.Delay(500, syncWindow.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }
            catch (Exception ex)
            {
                LogCollection.Log($"Playlist crossfade sync failed: {ex.Message}");
            }
        }

        // ── OFD / button_playlist_Click ────────────────────────────────────────
        private void button_playlist_Click(object sender, EventArgs e)
        {
            bool ctrlHeld = (ModifierKeys & Keys.Control) == Keys.Control;

            using OpenFileDialog ofd = new()
            {
                Multiselect = true,
                Filter = "Audio Files|*.wav;*.mp3;*.flac|All Files|*.*",
                InitialDirectory = this.lastImportFolder,
                Title = ctrlHeld ? "Select Audio Files to Import [in-RAM]" : "Add to Playlist"
            };

            if (ofd.ShowDialog() != DialogResult.OK || ofd.FileNames.Length == 0)
                return;

            this.lastImportFolder =
                Path.GetDirectoryName(ofd.FileNames[0]) ?? this.lastImportFolder;

            if (ctrlHeld)
            {
                // Normal RAM-import into a new bag — reuse existing import path
                _ = Task.Run(async () =>
                {
                    await WindowMainStaticHelpers.InvokeIfRequiredAsync(Instance,
                        () => this.ImportAndPlaceAsync(ofd.FileNames, fromResources: false));
                });
            }
            else
            {
                // Enqueue to playlist (files stay on disk)
                foreach (string path in ofd.FileNames)
                {
                    if (!string.IsNullOrWhiteSpace(path) &&
                        AllowedImportExtensions.Contains(Path.GetExtension(path)))
                    {
                        this._playlist.FilePaths.Add(path);
                    }
                }

                LogCollection.Log($"Playlist: {ofd.FileNames.Length} file(s) enqueued " +
                                  $"({this._playlist.FilePaths.Count} total).");
                this.UpdatePlaylistUI();
            }
        }

        private void button_playlist_TogglePlayPause_Click(object sender, EventArgs e)
        {
            this._playlist.TogglePlayPause();
            this.UpdatePlaylistButtonText();
            this.UpdatePlaylistUI();
        }

        private void playlistMenu_ImportTracks_Click(object sender, EventArgs e)
        {
            this.button_playlist_Click(sender, e);
        }

        internal AudioObj? GetPlaylistPrimaryAudio()
        {
            return this._playlist.PrimaryAudioObj;
        }

        internal IReadOnlyList<AudioObj> GetActivePlaylistAudios()
        {
            return this._playlist.ActiveAudioObjs
                .Where(audio => audio != null)
                .DistinctBy(audio => audio.Id)
                .ToArray();
        }

        // ── Right-click context menu handlers ──────────────────────────────────
        private void playlistMenu_PlayPause_Click(object sender, EventArgs e)
        {
            this._playlist.TogglePlayPause();
            this.UpdatePlaylistButtonText();
        }

        private void playlistMenu_Prev_Click(object sender, EventArgs e)  => this._playlist.RewindOrPrevious();
        private void playlistMenu_Skip_Click(object sender, EventArgs e)  => this._playlist.Skip();

        private void playlistMenu_Shuffle_Click(object sender, EventArgs e)
        {
            this._playlist.Shuffle();
            LogCollection.Log("Playlist shuffled.");
            this.UpdatePlaylistUI();
        }

        private void playlistMenu_Clear_Click(object sender, EventArgs e)
        {
            this._playlist.Clear();
            this._playlistMetaCache.Clear();
            LogCollection.Log("Playlist cleared.");
            // Force immediate UI update on UI thread; a second update fires via TrackChanged event
            WindowMainStaticHelpers.InvokeIfRequired(Instance, this.UpdatePlaylistUI);
        }

        private void playlistMenu_AddNext_Click(object? sender, EventArgs e)
        {
            using OpenFileDialog ofd = new()
            {
                Multiselect = true,
                Filter = "Audio Files|*.wav;*.mp3;*.flac|All Files|*.*",
                InitialDirectory = this.lastImportFolder,
                Title = "Select Audio Files to Add Next"
            };

            if (ofd.ShowDialog() != DialogResult.OK || ofd.FileNames.Length == 0)
                return;

            this.lastImportFolder = Path.GetDirectoryName(ofd.FileNames[0]) ?? this.lastImportFolder;

            List<string> validPaths = ofd.FileNames
                .Where(path => !string.IsNullOrWhiteSpace(path) && AllowedImportExtensions.Contains(Path.GetExtension(path)))
                .ToList();

            if (validPaths.Count == 0)
            {
                return;
            }

            // PlaylistEngine keeps the currently playing track at index 0 until it finishes.
            // "Add next" therefore means insert after index 0 while playing, otherwise at the front.
            int insertIndex = 0;
            try
            {
                if (!this._playlist.IsPlaying || this._playlist.FilePaths.Count == 0)
                {
                    insertIndex = 0;
                }
                else
                {
                    insertIndex = Math.Min(1, this._playlist.FilePaths.Count);
                }
            }
            catch
            {
                insertIndex = Math.Min(this._playlist.IsPlaying ? 1 : 0, this._playlist.FilePaths.Count);
            }

            insertIndex = Math.Clamp(insertIndex, 0, this._playlist.FilePaths.Count);

            foreach (string path in validPaths.Reverse<string>())
            {
                this._playlist.FilePaths.Insert(insertIndex, path);
            }

            LogCollection.Log($"Playlist: {validPaths.Count} file(s) added next.");
            this.UpdatePlaylistUI();
        }

        private void playlistMenu_EnqueueLast_Click(object? sender, EventArgs e)
        {
            using OpenFileDialog ofd = new()
            {
                Multiselect = true,
                Filter = "Audio Files|*.wav;*.mp3;*.flac|All Files|*.*",
                InitialDirectory = this.lastImportFolder,
                Title = "Select Audio Files to Enqueue Last"
            };

            if (ofd.ShowDialog() != DialogResult.OK || ofd.FileNames.Length == 0)
                return;

            this.lastImportFolder = Path.GetDirectoryName(ofd.FileNames[0]) ?? this.lastImportFolder;

            List<string> validPaths = ofd.FileNames
                .Where(path => !string.IsNullOrWhiteSpace(path) && AllowedImportExtensions.Contains(Path.GetExtension(path)))
                .ToList();

            if (validPaths.Count == 0)
            {
                return;
            }

            foreach (string path in validPaths)
            {
                this._playlist.FilePaths.Add(path);
            }

            LogCollection.Log($"Playlist: {validPaths.Count} file(s) enqueued last.");
            this.UpdatePlaylistUI();
        }

        private void playlistMenu_TimestretchEach_Click(object sender, EventArgs e)
        {
            // Toggle: if already enabled, disable
            if (this.toolStripMenuItem_timestretchEach.Checked)
            {
                this.toolStripMenuItem_timestretchEach.Checked = false;
                this._playlistStretchSettings = null;
                this.toolStripMenuItem_timestretchEach.Text = "⏱ Timestretch each...";
                LogCollection.Log("Playlist auto-timestretch disabled.");
                return;
            }

            // Open TimeStretchDialog in configure-only mode with a dummy audio
            var dummy = new AudioObj { Name = "Playlist Track", Bpm = 130f };
            using var dlg = new TimeStretchDialog(audios: [dummy])
            {
                IsConfigureMode = true
            };

            if (dlg.ShowDialog(this) != DialogResult.OK || dlg.ConfirmedSettings == null)
                return;

            this._playlistStretchSettings = dlg.ConfirmedSettings;
            this.toolStripMenuItem_timestretchEach.Checked = true;
            string method = dlg.ConfirmedUsedV2 ? "V2" : "V1";
            this.toolStripMenuItem_timestretchEach.Text = $"⏱ Timestretch each [{this._playlistStretchSettings.TargetBpm:F0} BPM, {method}]";
            LogCollection.Log($"Playlist auto-timestretch enabled: target {this._playlistStretchSettings.TargetBpm:F0} BPM via Stretch {method}.");
        }

        /// <summary>
        /// Preprocessor called by <see cref="PlaylistEngine"/> before each track.
        /// Loads the file, resolves initial BPM (from tag or BeatScanner), stretches to target BPM,
        /// exports to a temp WAV and returns its path.
        /// </summary>
        private async Task<string?> PreprocessPlaylistTrackAsync(string path, CancellationToken ct)
        {
            var settings = this._playlistStretchSettings;
            if (settings == null)
                return null;

            this._isPreprocessingTrack = true;
            WindowMainStaticHelpers.InvokeIfRequired(Instance, this.UpdatePlaylistUI);
            try
            {
                // Load audio from disk
                var audio = new AudioObj(path, load: true);
                if (audio.Data == null || audio.Data.Length == 0)
                    return null;

                // Resolve initial BPM: tag first, then scan
                float initialBpm = audio.Bpm > 0 ? audio.Bpm : audio.ScannedBpm;
                if (initialBpm <= 0)
                {
                    initialBpm = (float) await BeatScanner.ScanBpmAsync(audio).ConfigureAwait(false);
                    audio.ScannedBpm = initialBpm;
                }

                if (initialBpm <= 0)
                    return null; // cannot stretch without initial BPM

                double stretchFactor = settings.Fixed
                    ? (double) settings.StretchFactor
                    : initialBpm / (double) settings.TargetBpm;

                // Guard: half/double tempo if way off
                if (stretchFactor < 0.5) stretchFactor *= 2.0;

                ct.ThrowIfCancellationRequested();

                if (settings.UseV2)
                {
                    int? chunkSize  = settings.AutoChunking ? null : (int?) settings.ChunkSize;
                    float? overlap  = settings.AutoChunking ? null : (float?) settings.Overlap;
                    await TimeStretcher_V2.Timestretch_V2Async(
                        audio, stretchFactor, chunkSize, overlap,
                        progress: null, ct).ConfigureAwait(false);
                }
                else
                {
                    await TimeStretcher.TimeStretchAllThreadsAsync(
                        audio,
                        settings.ChunkSize,
                        settings.Overlap,
                        stretchFactor,
                        keepData: false,
                        normalize: 1.0f,
                        maxWorkers: settings.Threads,
                        progress: null,
                        offload: settings.Offload).ConfigureAwait(false);
                }

                if (settings.Trim)
                    await BeatGridFinder.TrimSilenceAsync(audio).ConfigureAwait(false);

                ct.ThrowIfCancellationRequested();

                // Write stretched audio to a temp WAV file using NAudio (keep original name)
                string origName = Path.GetFileNameWithoutExtension(path);
                string tempFile = Path.Combine(Path.GetTempPath(),
                    $"{origName}__stretched_{Guid.NewGuid():N}.wav");
                await Task.Run(() =>
                {
                    var wf = WaveFormat.CreateIeeeFloatWaveFormat(audio.SampleRate, audio.Channels);
                    using var writer = new NAudio.Wave.WaveFileWriter(tempFile, wf);
                    writer.WriteSamples(audio.Data, 0, audio.Data.Length);
                }, ct).ConfigureAwait(false);
                audio.Dispose();
                return tempFile;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogCollection.Log($"Playlist stretch error for '{Path.GetFileName(path)}': {ex.Message}");
                return null;
            }
            finally
            {
                this._isPreprocessingTrack = false;
                WindowMainStaticHelpers.InvokeIfRequired(Instance, this.UpdatePlaylistUI);
            }
        }

        // ── Tooltip (MouseHover) ───────────────────────────────────────────────
        private void button_playlist_MouseHover(object sender, EventArgs e)
        {
            string? currentTrackTitle = this.GetCurrentPlaylistTrackTitle();
            if (!string.IsNullOrWhiteSpace(currentTrackTitle))
            {
                this.toolTip_playlist.SetToolTip(this.button_playlist, currentTrackTitle);
                return;
            }

            var paths = this._playlist.FilePaths.ToList();
            if (paths.Count == 0)
            {
                this.toolTip_playlist.SetToolTip(this.button_playlist, "Playlist is empty.");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Playlist — {paths.Count} track(s):");
            sb.AppendLine(new string('─', 36));

            for (int i = 0; i < Math.Min(paths.Count, 30); i++)
            {
                string path = paths[i];
                string name = Path.GetFileNameWithoutExtension(path);
                if (name.Length > 32) name = name[..29] + "…";

                var meta = this.GetOrFetchMeta(path);
                string dur = meta.Duration > TimeSpan.Zero
                    ? $"{(int) meta.Duration.TotalMinutes:D2}:{meta.Duration.Seconds:D2}"
                    : "--:--";
                string bpm = meta.Bpm > 0 ? $"{meta.Bpm:F0}" : "?";

                sb.AppendLine($"{i + 1,2}. {name,-32} {dur}  [{bpm} BPM]");
            }

            if (paths.Count > 30)
                sb.AppendLine($"… and {paths.Count - 30} more.");

            this.toolTip_playlist.SetToolTip(this.button_playlist, sb.ToString().TrimEnd());
        }

        // ── Label + button text update ─────────────────────────────────────────
        private void UpdatePlaylistUI()
        {
            if (Instance == null || Instance.IsDisposed) return;
            try
            {
                if (this.label_currentlyEnqueued.InvokeRequired)
                {
                    this.label_currentlyEnqueued.Invoke(this.UpdatePlaylistUI);
                    return;
                }

                this.UpdatePlaylistButtonText();
                string label = this.BuildEnqueuedLabelText();
                this.label_currentlyEnqueued.Text = label;
                this.Text = this.GetWindowTitleText();
            }
            catch { }
        }

        private string GetWindowTitleText()
        {
            string? current = this.GetCurrentPlaylistTrackTitle();
            string baseTitle = this.Tag as string ?? this.Text;

            if (this._playlist.IsPlaying && !this._playlist.IsPaused && !string.IsNullOrWhiteSpace(current))
            {
                return $"▶ {current}";
            }

            if (this._playlist.IsPaused && !string.IsNullOrWhiteSpace(current))
            {
                return $"|| {current}";
            }

            return baseTitle;
        }

        private void UpdatePlaylistButtonText()
        {
            if (this.button_playlist.InvokeRequired)
            {
                this.button_playlist.Invoke(this.UpdatePlaylistButtonText);
                return;
            }

            if (this._playlist.IsPlaying && !this._playlist.IsPaused)
            {
                this.button_playlist.Text = "▶ List";
                return;
            }

            if (this._playlist.IsPaused)
            {
                this.button_playlist.Text = "|| List";
                return;
            }

            this.button_playlist.Text = "▶ List";
        }

        private void UpdatePlaylistHoverTitle()
        {
            if (this.button_playlist.InvokeRequired)
            {
                this.button_playlist.Invoke(this.UpdatePlaylistHoverTitle);
                return;
            }

            string? current = this.GetCurrentPlaylistTrackTitle();
            if (!string.IsNullOrWhiteSpace(current))
            {
                this.toolTip_playlist.SetToolTip(this.button_playlist, current);
            }
        }

        private string BuildEnqueuedLabelText()
        {
            if (this._isPreprocessingTrack)
                return "⏳ Time-Stretching next track...";

            if (!this._playlist.IsPlaying && !this._playlist.IsPaused && this._playlist.CurrentPath == null)
            {
                if (this._playlist.FilePaths.Count > 0)
                    return $"▶ List ready — {this._playlist.FilePaths.Count} track(s) enqueued.";
                return "No track currently enqueued in playlist.";
            }

            string stateIcon = this._playlist.IsPlaying ? "▶" : "||";
            TimeSpan pos = this._playlist.CurrentPosition;
            TimeSpan dur = this._playlist.CurrentDuration;
            string posStr = $"{(int) pos.TotalMinutes:D2}:{pos.Seconds:D2}";
            string durStr = $"{(int) dur.TotalMinutes:D2}:{dur.Seconds:D2}";

            string name = this._playlist.CurrentPath != null
                ? Path.GetFileNameWithoutExtension(this._playlist.CurrentPath).Split("__").FirstOrDefault() ?? "–"
                : "–";
            if (name.Length > 96) name = name[..63] + "…";

            float bpm = this._playlist.CurrentBpm;
            // If BPM unknown but auto-stretch is enabled, show the target BPM
            if (bpm <= 0 && this._playlistStretchSettings != null)
                bpm = this._playlistStretchSettings.TargetBpm;
            string bpmStr = bpm > 0 ? $"{bpm:F0}" : "?";

            int ch   = this._playlist.CurrentChannels;
            int sr   = this._playlist.CurrentSampleRate;
            int bits = this._playlist.CurrentBitDepth;
            string chStr = ch switch { 1 => "mono", 2 => "stereo", _ => $"{ch}-ch" };
            string srStr = (sr / 1000.0).ToString("F1");

            return $"{stateIcon} {posStr} / {durStr} | {name} [{bpmStr}] | {chStr} {srStr} kHz {bits} bits";
        }

        private string? GetCurrentPlaylistTrackTitle()
        {
            if (string.IsNullOrWhiteSpace(this._playlist.OriginalCurrentPath) && string.IsNullOrWhiteSpace(this._playlist.CurrentPath))
                return null;

            string? path = this._playlist.OriginalCurrentPath ?? this._playlist.CurrentPath;
            if (string.IsNullOrWhiteSpace(path))
                return null;

            string name = Path.GetFileNameWithoutExtension(path);
            if (name.Contains("__stretched_", StringComparison.OrdinalIgnoreCase))
                name = name.Split("__stretched_", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];

            float bpm = this._playlist.CurrentBpm;
            if (bpm <= 0 && this._playlistStretchSettings != null)
                bpm = this._playlistStretchSettings.TargetBpm;

            return bpm > 0 ? $"{name} [{bpm:F0} BPM]" : name;
        }

        // ── Metadata cache ─────────────────────────────────────────────────────
        private (TimeSpan Duration, float Bpm, int Channels, int SampleRate, int BitDepth) GetOrFetchMeta(string path)
        {
            if (!this._playlistMetaCache.TryGetValue(path, out var meta))
            {
                try { meta = PlaylistEngine.ReadMetadata(path); } catch { }
                this._playlistMetaCache[path] = meta;
            }
            return meta;
        }

        // ── Cleanup (called from FormClosing) ──────────────────────────────────
        private void DisposePlaylist()
        {
            try { this._playlistTimer?.Stop(); this._playlistTimer?.Dispose(); this._playlistTimer = null; } catch { }
            try { this._playlist.Dispose(); } catch { }
        }

        // ── Recording Track-Log ────────────────────────────────────────────────

        /// <summary>
        /// Called when a recording starts. Binds the log file path to the recording file.
        /// </summary>
        public void StartTrackLog(string recordingFilePath)
        {
            this._trackLogFilePath = Path.ChangeExtension(recordingFilePath, ".txt");
            this._trackLogRecordStart = DateTime.UtcNow;
            this._trackLog.Clear();
            this._trackLogActivePaths.Clear();

            this.SyncPlaylistTrackLog(TimeSpan.Zero);

            this.FlushTrackLog();
        }

        /// <summary>
        /// Finalises the log (closes any open entry) and writes the file.
        /// Called when the recording stops.
        /// </summary>
        public void FinaliseTrackLog()
        {
            if (this._trackLogFilePath == null || this._trackLogRecordStart == null) return;

            TimeSpan now = DateTime.UtcNow - this._trackLogRecordStart.Value;
            foreach (var entry in this._trackLog.Where(e => e.End == null))
                entry.End = now;

            this.FlushTrackLog();

            // Reset so future recordings start fresh
            this._trackLogFilePath   = null;
            this._trackLogRecordStart = null;
            this._trackLogActivePaths.Clear();
        }

        /// <summary>
        /// Called from the TrackChanged event.  Closes the previous entry and opens a new one.
        /// </summary>
        private void OnPlaylistTrackChanged()
        {
            if (this._trackLogFilePath == null || this._trackLogRecordStart == null) return;

            TimeSpan now = DateTime.UtcNow - this._trackLogRecordStart.Value;

            this.SyncPlaylistTrackLog(now);
            this.FlushTrackLog();
        }

        private void SyncPlaylistTrackLog(TimeSpan now)
        {
            HashSet<string> activePaths = this._playlist.ActiveOriginalPaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (string endedPath in this._trackLogActivePaths.Except(activePaths, StringComparer.OrdinalIgnoreCase).ToList())
            {
                string endedTrackId = Path.GetFileNameWithoutExtension(endedPath);
                TrackLogEntry? last = this._trackLog.LastOrDefault(e =>
                    e.TrackId == endedTrackId &&
                    e.End == null);

                if (last != null)
                {
                    last.End = now;
                }
            }

            foreach (string startedPath in activePaths.Except(this._trackLogActivePaths, StringComparer.OrdinalIgnoreCase))
            {
                this._trackLog.Add(new TrackLogEntry
                {
                    Start = now,
                    TrackId = Path.GetFileNameWithoutExtension(startedPath)
                });
            }

            this._trackLogActivePaths = activePaths;
        }

        private void FlushTrackLog()
        {
            if (this._trackLogFilePath == null) return;
            try
            {
                var lines = this._trackLog
                    .Where(e => e.End == null || e.End.Value >= e.Start)
                    .Select(e =>
                {
                    string start = FormatLogTs(e.Start);
                    string end   = e.End.HasValue ? FormatLogTs(e.End.Value) : "ongoing";
                    return $"{start} - {end}\t{e.TrackId}";
                });
                File.WriteAllLines(this._trackLogFilePath, lines);
            }
            catch { }
        }

        private static string FormatLogTs(TimeSpan ts) =>
            $"{(int) ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}";
    }
}
