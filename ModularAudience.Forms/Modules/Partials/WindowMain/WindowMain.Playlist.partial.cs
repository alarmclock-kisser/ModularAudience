using ModularAudience.Audio;
using ModularAudience.Audio.Processing;
using ModularAudience.Audio.Processors_V1;
using ModularAudience.Audio.Processors_V2;
using ModularAudience.Audio.Processors_V3;
using ModularAudience.Forms.Helpers;
using ModularAudience.Forms.Modules;
using ModularAudience.Forms.Modules.Dialogs;
using NAudio.Wave;
using System.Text;
using System.Threading;
using System.ComponentModel;

namespace ModularAudience.Forms
{
    public partial class WindowMain
    {
        // ── Engine + timer ─────────────────────────────────────────────────────
        private PlaylistEngine _playlist = new();
        private System.Windows.Forms.Timer? _playlistTimer;
        private bool _playlistAutoEnqueueRightClickHandled;

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

        // Designer hookup for context menu opening
        private void contextMenuStrip_playlist_Opening(object? sender, CancelEventArgs e)
        {
            this._playlistAutoEnqueueRightClickHandled = false;
            this.toolStripMenuItem_autoEnqueueOne.Enabled = true;
        }

        // If user clicks the menu item with the right mouse button, treat it like Ctrl+Click (allow fallback)
        private void playlistMenu_AutoEnqueueOne_MouseDown(object? sender, MouseEventArgs e)
        {
            this._playlistAutoEnqueueRightClickHandled = e.Button == MouseButtons.Right;
            if (this._playlistAutoEnqueueRightClickHandled)
            {
                this.contextMenuStrip_playlist.Close();
                this.AutoEnqueueOne(allowFallback: true);
            }
        }

        private string? _trackLogFilePath;               // set when a recording begins
        private DateTime? _trackLogRecordStart;          // UTC time when recording started
        private readonly List<TrackLogEntry> _trackLog = [];
        private HashSet<string> _trackLogActivePaths = new(StringComparer.OrdinalIgnoreCase);

        // ── Initializer (called from constructor) ──────────────────────────────
        private void InitPlaylist()
        {
            this._playlist.CountdownEnabledProvider = () => PlaylistCountdownEnabled;
            this._playlist.AutoEnqueuePathProvider = SelectRandomPlaylistPath;

            this._playlist.TrackChanged += () =>
            {
                // Pause is a true pause: do not treat it like track completion/advance.
                if (this._playlist.IsPaused)
                {
                    WindowMainStaticHelpers.InvokeIfRequired(Instance, this.UpdatePlaylistUI);
                    WindowMainStaticHelpers.InvokeIfRequired(Instance, this.UpdatePlaylistHoverTitle);
                    return;
                }

                // Track-log: close previous entry, open new one
                this.OnPlaylistTrackChanged();
                WindowMainStaticHelpers.InvokeIfRequired(Instance, this.UpdatePlaylistUI);
                WindowMainStaticHelpers.InvokeIfRequired(Instance, this.UpdatePlaylistHoverTitle);
            };

            this._playlist.BeforeTrackPlay = this.PreprocessPlaylistTrackAsync;
            this._playlist.CrossfadeDurationProvider = () => CrossfadeDurationSeconds;
            this._playlist.ResolvePlaybackBpm = this.ResolvePlaylistPlaybackBpm;
            this._playlist.CrossfadeStartedAsync = this.HandlePlaylistCrossfadeStartedAsync;

            this._playlistTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            this._playlistTimer.Tick += (_, _) => this.UpdatePlaylistUI();
            this._playlistTimer.Start();

            // Default countdown enabled state
            try { PlaylistCountdownEnabled = true; } catch { }
        }

        private static string? SelectRandomPlaylistPath(string? originalPath)
        {
            if (string.IsNullOrWhiteSpace(originalPath))
            {
                return null;
            }

            try
            {
                string fullOriginalPath = Path.GetFullPath(originalPath);
                string? directory = Path.GetDirectoryName(fullOriginalPath);
                if (!Directory.Exists(directory))
                {
                    return null;
                }

                string[] candidates = Directory.EnumerateFiles(directory)
                    .Where(path => Path.GetExtension(path).ToLowerInvariant() is ".wav" or ".mp3" or ".flac")
                    .Where(path => !string.Equals(path, fullOriginalPath, StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                    .ToArray();
                return candidates.Length > 0 ? candidates[Random.Shared.Next(candidates.Length)] : null;
            }
            catch (Exception ex)
            {
                LogCollection.Log($"Playlist random selection failed: {ex.Message}");
                return null;
            }
        }

        // Toggle controlled by context menu
        internal static bool PlaylistCountdownEnabled = true;

        private float ResolvePlaylistPlaybackBpm(string originalPath, string playPath)
        {
            // If the playback path differs from the original (i.e. a preprocessed/stretched temp file),
            // prefer the user-configured target BPM so the UI and engine reflect the actual playback rate.
            if (!string.Equals(playPath, originalPath, StringComparison.OrdinalIgnoreCase) && this._playlistStretchSettings != null)
            {
                return this._playlistStretchSettings.TargetBpm;
            }



            float bpm = PlaylistEngine.ReadMetadata(originalPath).Bpm;
            if (bpm <= 0 && !string.Equals(playPath, originalPath, StringComparison.OrdinalIgnoreCase))
            {
                bpm = PlaylistEngine.ReadMetadata(playPath).Bpm;
            }

            if (bpm <= 0 && this._playlistStretchSettings != null)
            {
                bpm = this._playlistStretchSettings.TargetBpm;
            }

            return bpm;
        }

        private async Task HandlePlaylistCrossfadeStartedAsync(AudioObj currentTrack, AudioObj nextTrack)
        {
            if (Instance == null || Instance.IsDisposed)
            {
                return;
            }

            int syncDurationMs = Math.Max(0, CrossSyncDurationMs);
            if (syncDurationMs <= 0)
            {
                return;
            }

            try
            {
                await Task.Delay(20).ConfigureAwait(false);

                // CRITICAL: The two tracks involved in the crossfade MUST NOT be handed to the
                // PausingPlaybackSyncer. The syncer works by briefly pausing tracks to nudge
                // their beat phase – pausing the just-started incoming track would cause the
                // fade-in volume ramp (which runs on wall-clock time) to advance while audio
                // is silent, producing the long, quiet ramp the user reported.
                // We only sync OTHER tracks (e.g. open TrackView audios) against the incoming
                // track if such third-party tracks exist.
                Guid currentId = currentTrack?.Id ?? Guid.Empty;
                Guid nextId = nextTrack?.Id ?? Guid.Empty;

                List<AudioObj> externalPlayingTracks = TrackViews
                    .Where(tv => tv != null && !tv.IsDisposed && tv.OriginalAudio != null && tv.OriginalAudio.PlayerPlaying)
                    .Select(tv => tv.OriginalAudio)
                    .Where(audio => audio.Id != currentId && audio.Id != nextId)
                    .Distinct()
                    .ToList();

                if (externalPlayingTracks.Count == 0 || nextTrack == null)
                {
                    // Nothing to sync against – skip silently. The crossfade itself proceeds untouched.
                    return;
                }

                // Sync external tracks to align with the new incoming track.
                List<AudioObj> syncSet = [nextTrack, .. externalPlayingTracks];

                using CancellationTokenSource syncWindow = new(TimeSpan.FromMilliseconds(syncDurationMs));

                // IMPORTANT: pass the incoming track only so it can be used as master reference.
                // PausingPlaybackSyncer will only pulse-pause slaves (the external tracks),
                // never the master with highest volume – but to be safe we keep nextTrack
                // boosted: it is the freshly-started fade-in target, dropping its volume
                // momentarily would be inaudible at the start of the fade and acceptable.
                var syncer = new PausingPlaybackSyncer(syncSet, syncWindow.Token, frequency: 0.05, grain: 12);
                LogCollection.Log($"Playlist crossfade: {syncDurationMs} ms beat sync window started (slaves={externalPlayingTracks.Count}).");

                try
                {
                    await Task.Delay(syncDurationMs, syncWindow.Token).ConfigureAwait(false);
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
            {
                return;
            }

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
                int added = this._playlist.EnqueueLast(ofd.FileNames.Where(path =>
                    !string.IsNullOrWhiteSpace(path) && AllowedImportExtensions.Contains(Path.GetExtension(path))));

                LogCollection.Log($"Playlist: {added} file(s) enqueued " +
                                  $"({this._playlist.GetQueueSnapshot().FilePaths.Count} total).");
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

        internal bool TryRemoveActivePlaylistAudioById(Guid audioId)
        {
            try
            {
                return this._playlist.RemoveActiveById(audioId);
            }
            catch { }
            return false;
        }

        // ── Right-click context menu handlers ──────────────────────────────────
        private void playlistMenu_PlayPause_Click(object sender, EventArgs e)
        {
            this._playlist.TogglePlayPause();
            this.UpdatePlaylistButtonText();
        }

        private void playlistMenu_Prev_Click(object sender, EventArgs e) => this._playlist.RewindOrPrevious();
        private void playlistMenu_Skip_Click(object sender, EventArgs e) => this._playlist.Skip();

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

        private void playlistMenu_Countdown_Click(object? sender, EventArgs e)
        {
            try
            {
                if (sender is ToolStripMenuItem it)
                {
                    PlaylistCountdownEnabled = it.Checked;
                    LogCollection.Log($"Playlist countdown {(it.Checked ? "enabled" : "disabled")}. ");
                }
            }
            catch { }
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
            {
                return;
            }

            this.lastImportFolder = Path.GetDirectoryName(ofd.FileNames[0]) ?? this.lastImportFolder;

            List<string> validPaths = ofd.FileNames
                .Where(path => !string.IsNullOrWhiteSpace(path) && AllowedImportExtensions.Contains(Path.GetExtension(path)))
                .ToList();

            if (validPaths.Count == 0)
            {
                return;
            }

            int added = this._playlist.EnqueueNext(validPaths);
            LogCollection.Log($"Playlist: {added} file(s) added next.");
            this.UpdatePlaylistUI();
        }

        private void playlistMenu_AutoEnqueueOne_Click(object? sender, EventArgs e)
        {
            if (!this._playlistAutoEnqueueRightClickHandled)
            {
                this.AutoEnqueueOne(allowFallback: (ModifierKeys & Keys.Control) == Keys.Control);
            }
        }

        private void AutoEnqueueOne(bool allowFallback)
        {
            try
            {
                string? selectedPath = LoopControlWindow != null && !LoopControlWindow.IsDisposed
                    ? LoopControlWindow.GetSelectedPlaylistPath() : null;
                bool promoted = !string.IsNullOrWhiteSpace(selectedPath) &&
                    this._playlist.AutoEnqueuePreparedNext(selectedPath);

                if (!promoted && allowFallback)
                {
                    string[] selectedPaths = this.SelectPlaylistFallbackPaths();
                    if (selectedPaths.Length == 0)
                    {
                        return;
                    }
                    int added = this._playlist.EnqueueNext(selectedPaths);
                    this._playlist.NotifyInsertedNext(selectedPaths[0]);
                    LogCollection.Log($"Playlist: {added} selected file(s) added next.");
                    this.UpdatePlaylistUI();
                    return;
                }
                else if (!promoted)
                {
                    promoted = this.AutoEnqueueRandomPreparedNext();
                }

                LogCollection.Log(promoted
                    ? "Playlist: auto-enqueued one prepared track."
                    : "Auto enqueue one: no eligible prepared non-active track; queue unchanged.");
                this.UpdatePlaylistUI();
            }
            catch (Exception ex)
            {
                LogCollection.Log($"Auto enqueue one failed: {ex.Message}");
            }
        }

        private string[] SelectPlaylistFallbackPaths()
        {
            string[] selectedPaths = CollectionViews.Where(cv => !cv.IsDisposed)
                .SelectMany(cv => cv.SelectedAudios).Select(audio => audio.FilePath)
                .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path) &&
                    AllowedImportExtensions.Contains(Path.GetExtension(path)))
                .ToArray();
            if (selectedPaths.Length > 0)
            {
                return selectedPaths;
            }

            using OpenFileDialog ofd = new()
            {
                Multiselect = true,
                Filter = "Audio Files|*.wav;*.mp3;*.flac|All Files|*.*",
                InitialDirectory = this.lastImportFolder,
                Title = "Select Audio Files to Add Next"
            };
            if (ofd.ShowDialog(this) != DialogResult.OK || ofd.FileNames.Length == 0)
            {
                return [];
            }

            this.lastImportFolder = Path.GetDirectoryName(ofd.FileNames[0]) ?? this.lastImportFolder;
            return ofd.FileNames.Where(path => !string.IsNullOrWhiteSpace(path) &&
                AllowedImportExtensions.Contains(Path.GetExtension(path))).ToArray();
        }

        private bool AutoEnqueueRandomPreparedNext()
        {
            string[] candidates = this._playlist.GetQueueSnapshot().FilePaths.Skip(4).ToArray();
            Random.Shared.Shuffle(candidates);
            foreach (string candidate in candidates)
            {
                if (this._playlist.AutoEnqueuePreparedNext(candidate))
                {
                    return true;
                }
            }
            return this._playlist.AutoEnqueuePreparedNext();
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
            {
                return;
            }

            this.lastImportFolder = Path.GetDirectoryName(ofd.FileNames[0]) ?? this.lastImportFolder;

            List<string> validPaths = ofd.FileNames
                .Where(path => !string.IsNullOrWhiteSpace(path) && AllowedImportExtensions.Contains(Path.GetExtension(path)))
                .ToList();

            if (validPaths.Count == 0)
            {
                return;
            }

            int added = this._playlist.EnqueueLast(validPaths);
            LogCollection.Log($"Playlist: {added} file(s) enqueued last.");
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
            using var dlg = new TimeStretchDialog(filePaths: this._playlist.GetQueueSnapshot().FilePaths)
            {
                IsConfigureMode = true
            };

            if (dlg.ShowDialog(this) != DialogResult.OK || dlg.ConfirmedSettings == null)
            {
                return;
            }

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
            {
                return null;
            }

            this._isPreprocessingTrack = true;
            WindowMainStaticHelpers.InvokeIfRequired(Instance, this.UpdatePlaylistUI);
            try
            {
                // Load audio from disk
                var audio = new AudioObj(path, load: true);
                if (audio.Data == null || audio.Data.Length == 0)
                {
                    return null;
                }

                // Resolve initial BPM: tag first, then scan
                float initialBpm = audio.Bpm > 0 ? audio.Bpm : audio.ScannedBpm;
                if (initialBpm <= 0)
                {
                    initialBpm = (float) await BeatScanner.ScanBpmAsync(audio).ConfigureAwait(false);
                    audio.ScannedBpm = initialBpm;
                }

                if (initialBpm <= 0)
                {
                    return null; // cannot stretch without initial BPM
                }

                double stretchFactor = settings.Fixed
                    ? (double) settings.StretchFactor
                    : initialBpm / (double) settings.TargetBpm;

                // Guard: half/double tempo if way off
                if (stretchFactor < 0.5)
                {
                    stretchFactor *= 2.0;
                }

                ct.ThrowIfCancellationRequested();

                if (settings.UseV2)
                {
                    int? chunkSize = settings.AutoChunking ? null : (int?) settings.ChunkSize;
                    float? overlap = settings.AutoChunking ? null : (float?) settings.Overlap;

                    float preRms = PlaylistNormalizer.MeasureRms(audio.Data);
                    await TimeStretcher_V2.Timestretch_V2Async(
                        audio, stretchFactor, chunkSize, overlap,
                        progress: null, ct).ConfigureAwait(false);
                    PlaylistNormalizer.ApplyRmsGain(audio.Data, preRms);
                }
                else
                {
                    float preRms = PlaylistNormalizer.MeasureRms(audio.Data);
                    await TimeStretcher.TimeStretchAllThreadsAsync(
                        audio,
                        settings.ChunkSize,
                        settings.Overlap,
                        stretchFactor,
                        keepData: false,
                        normalize: 1.0f,
                        maxWorkers: settings.Threads,
                        progress: null,
                        offload: settings.Offload, channeled: settings.Channeled).ConfigureAwait(false);
                    PlaylistNormalizer.ApplyRmsGain(audio.Data, preRms);
                }

                if (settings.Trim)
                {
                    await BeatGridFinder.TrimSilenceAsync(audio).ConfigureAwait(false);
                }

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
            PlaylistQueueSnapshot snapshot = this._playlist.GetQueueSnapshot();
            string? currentTrackTitle = this.GetCurrentPlaylistTrackTitle(snapshot);
            if (!string.IsNullOrWhiteSpace(currentTrackTitle))
            {
                this.toolTip_playlist.SetToolTip(this.button_playlist, this.BuildPlaylistHoverSummary(snapshot));
                return;
            }

            var paths = snapshot.FilePaths;
            if (paths.Count == 0)
            {
                this.toolTip_playlist.SetToolTip(this.button_playlist, "Playlist is empty.");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine(this.BuildPlaylistHoverSummary(snapshot));
            sb.AppendLine(new string('─', 36));

            for (int i = 0; i < Math.Min(paths.Count, 30); i++)
            {
                string path = paths[i];
                string name = Path.GetFileNameWithoutExtension(path);
                if (name.Length > 32)
                {
                    name = name[..29] + "…";
                }

                var meta = this.GetOrFetchMeta(path);
                string dur = meta.Duration > TimeSpan.Zero
                    ? $"{(int) meta.Duration.TotalMinutes:D2}:{meta.Duration.Seconds:D2}"
                    : "--:--";
                string bpm = meta.Bpm > 0 ? $"{meta.Bpm:F0}" : "?";

                sb.AppendLine($"{i + 1,2}. {name,-32} {dur}  [{bpm} BPM]");
            }

            if (paths.Count > 30)
            {
                sb.AppendLine($"… and {paths.Count - 30} more.");
            }

            this.toolTip_playlist.SetToolTip(this.button_playlist, sb.ToString().TrimEnd());
        }

        // ── Label + button text update ─────────────────────────────────────────
        private void UpdatePlaylistUI()
        {
            if (Instance == null || Instance.IsDisposed)
            {
                return;
            }

            try
            {
                if (this.label_currentlyEnqueued.InvokeRequired)
                {
                    this.label_currentlyEnqueued.Invoke(this.UpdatePlaylistUI);
                    return;
                }

                PlaylistQueueSnapshot snapshot = this._playlist.GetQueueSnapshot();
                this.button_playlist.Text = snapshot.IsPaused ? "|| List" : "▶ List";
                string label = this.BuildEnqueuedLabelText(snapshot);
                this.label_currentlyEnqueued.Text = label;
                this.Text = this.GetWindowTitleText(snapshot);
            }
            catch (Exception ex)
            {
                LogCollection.Log($"Playlist UI update failed: {ex.Message}");
            }
        }

        private string GetWindowTitleText(PlaylistQueueSnapshot snapshot)
        {
            string? current = this.GetCurrentPlaylistTrackTitle(snapshot);
            string baseTitle = this.Tag as string ?? this.Text;

            if (snapshot.IsPlaying && !snapshot.IsPaused && !string.IsNullOrWhiteSpace(current))
            {
                return $"▶ {current}";
            }

            if (snapshot.IsPaused && !string.IsNullOrWhiteSpace(current))
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

            PlaylistQueueSnapshot snapshot = this._playlist.GetQueueSnapshot();
            this.button_playlist.Text = snapshot.IsPaused ? "|| List" : "▶ List";
        }

        private void UpdatePlaylistHoverTitle()
        {
            if (this.button_playlist.InvokeRequired)
            {
                this.button_playlist.Invoke(this.UpdatePlaylistHoverTitle);
                return;
            }

            PlaylistQueueSnapshot snapshot = this._playlist.GetQueueSnapshot();
            this.toolTip_playlist.SetToolTip(this.button_playlist, this.BuildPlaylistHoverSummary(snapshot));
        }

        private string BuildPlaylistHoverSummary(PlaylistQueueSnapshot snapshot)
        {
            string current = this.GetCurrentPlaylistTrackTitle(snapshot) ?? "None";
            string next = string.IsNullOrWhiteSpace(snapshot.NextPath)
                ? "None" : Path.GetFileNameWithoutExtension(snapshot.NextPath);
            return $"Current: {current}\nNext up: {next}\nQueue total: {snapshot.FilePaths.Count} track(s)";
        }

        private string BuildEnqueuedLabelText(PlaylistQueueSnapshot snapshot)
        {
            if (this._isPreprocessingTrack)
            {
                return "⏳ Time-Stretching next track...";
            }

            if (!snapshot.IsPlaying && !snapshot.IsPaused && snapshot.CurrentPath == null)
            {
                if (snapshot.FilePaths.Count > 0)
                {
                    return $"▶ List ready — {snapshot.FilePaths.Count} track(s) enqueued.";
                }

                return "No track currently enqueued in playlist.";
            }

            string stateIcon = snapshot.IsPaused ? "||" : "▶";
            TimeSpan pos = snapshot.CurrentPosition;
            TimeSpan dur = snapshot.CurrentDuration;
            string posStr = $"{(int) pos.TotalMinutes:D2}:{pos.Seconds:D2}";
            string durStr = $"{(int) dur.TotalMinutes:D2}:{dur.Seconds:D2}";

            string name = GetCurrentPlaylistTrackName(snapshot) ?? "–";
            if (name.Length > 96)
            {
                name = name[..63] + "…";
            }

            // Prefer the engine-reported current BPM (already adjusted for any applied stretch).
            float bpm = snapshot.CurrentBpm;
            if (bpm <= 0 && this._playlistStretchSettings != null)
            {
                bpm = this._playlistStretchSettings.TargetBpm;
            }

            string bpmStr = bpm > 0 ? $"{bpm:F0}" : "?";

            int ch = snapshot.CurrentChannels;
            int sr = snapshot.CurrentSampleRate;
            int bits = snapshot.CurrentBitDepth;
            string chStr = ch switch { 1 => "mono", 2 => "stereo", _ => $"{ch}-ch" };
            string srStr = (sr / 1000.0).ToString("F1");

            return $"{stateIcon} {posStr} / {durStr} | {name} [{bpmStr}] | {chStr} {srStr} kHz {bits} bits";
        }

        private static string? GetCurrentPlaylistTrackName(PlaylistQueueSnapshot snapshot)
        {
            if (!string.IsNullOrWhiteSpace(snapshot.OriginalCurrentPath))
            {
                return Path.GetFileNameWithoutExtension(snapshot.OriginalCurrentPath);
            }

            string? path = snapshot.CurrentPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            string name = Path.GetFileNameWithoutExtension(path) ?? "–";
            // Remove generated suffixes after a double-underscore and common _stretched_ markers
            if (name.Contains("__"))
            {
                name = name.Split(["__"], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? name;
            }

            if (name.IndexOf("_stretched_", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                name = name.Split(["_stretched_"], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? name;
            }

            return name;
        }

        private string? GetCurrentPlaylistTrackTitle(PlaylistQueueSnapshot snapshot)
        {
            string? name = GetCurrentPlaylistTrackName(snapshot);
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            // Prefer engine-reported current BPM (reflects applied stretch). If missing, fall back to stretch settings.
            float bpm = snapshot.CurrentBpm;
            if (bpm <= 0 && this._playlistStretchSettings != null)
            {
                bpm = this._playlistStretchSettings.TargetBpm;
            }

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
            if (this._trackLogFilePath == null || this._trackLogRecordStart == null)
            {
                return;
            }

            TimeSpan now = DateTime.UtcNow - this._trackLogRecordStart.Value;
            foreach (var entry in this._trackLog.Where(e => e.End == null))
            {
                entry.End = now;
            }

            this.FlushTrackLog();

            // Reset so future recordings start fresh
            this._trackLogFilePath = null;
            this._trackLogRecordStart = null;
            this._trackLogActivePaths.Clear();
        }

        /// <summary>
        /// Called from the TrackChanged event.  Closes the previous entry and opens a new one.
        /// </summary>
        private void OnPlaylistTrackChanged()
        {
            if (this._trackLogFilePath == null || this._trackLogRecordStart == null)
            {
                return;
            }

            if (this._playlist.IsPaused)
            {
                return;
            }

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
            if (this._trackLogFilePath == null)
            {
                return;
            }

            try
            {
                var lines = this._trackLog
                    .Where(e => e.End == null || e.End.Value >= e.Start)
                    .Select(e =>
                {
                    string start = FormatLogTs(e.Start);
                    string end = e.End.HasValue ? FormatLogTs(e.End.Value) : "ongoing";
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
