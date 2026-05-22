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
using System.ComponentModel;

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

        // Designer hookup for context menu opening
        private void contextMenuStrip_playlist_Opening(object? sender, CancelEventArgs e)
        {
            try
            {
                if (this.contextMenuStrip_playlist == null) return;
                foreach (ToolStripItem it in this.contextMenuStrip_playlist.Items)
                {
                    if (it is ToolStripMenuItem mi && string.Equals(mi.Text, "Auto enqueue one", StringComparison.OrdinalIgnoreCase))
                    {
                        bool enable = false;
                        try
                        {
                            if (LoopControlWindow != null && !LoopControlWindow.IsDisposed)
                                enable = LoopControlWindow.HasSelectedPlaylistItem();

                            // Also enable when the engine already holds a prepared track that is NOT the
                            // currently playing original path. This avoids enabling the menu solely because
                            // the currently playing track reports as "prepared".
                            try
                            {
                                enable = enable || (this._playlist != null &&
                                    this._playlist.ActiveOriginalPaths.Any(p =>
                                        !string.IsNullOrWhiteSpace(p) &&
                                        !string.Equals(p, this._playlist.OriginalCurrentPath ?? string.Empty, StringComparison.OrdinalIgnoreCase)));
                            }
                            catch { }
                        }
                        catch { enable = false; }
                        mi.Enabled = enable;
                        try
                        {
                            // Ensure right-click on the menu item maps to the fallback behaviour.
                            mi.MouseDown -= this.playlistMenu_AutoEnqueueOne_MouseDown;
                            mi.MouseDown += this.playlistMenu_AutoEnqueueOne_MouseDown;
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        // If user clicks the menu item with the right mouse button, treat it like Ctrl+Click (allow fallback)
        private volatile bool _autoEnqueueOne_ForceAllowFallback = false;
        private void playlistMenu_AutoEnqueueOne_MouseDown(object? sender, MouseEventArgs e)
        {
            try
            {
                if (e.Button == MouseButtons.Right)
                {
                    try
                    {
                        _autoEnqueueOne_ForceAllowFallback = true;
                        // Invoke the click handler directly so behaviour is shared
                        this.playlistMenu_AutoEnqueueOne_Click(sender, EventArgs.Empty);
                    }
                    finally { _autoEnqueueOne_ForceAllowFallback = false; }
                }
            }
            catch { }
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
            this._playlist.CrossfadeDurationProvider = () => WindowMain.CrossfadeDurationSeconds;
            this._playlist.ResolvePlaybackBpm = this.ResolvePlaylistPlaybackBpm;
            this._playlist.CrossfadeStartedAsync = this.HandlePlaylistCrossfadeStartedAsync;

            this._playlistTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            this._playlistTimer.Tick += (_, _) => this.UpdatePlaylistUI();
            this._playlistTimer.Start();

            // Ensure playlist context menu contains Add next / Enqueue last entries and Auto enqueue one
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

                    try
                    {
                        var autoOne = new ToolStripMenuItem("Auto enqueue one", null, this.playlistMenu_AutoEnqueueOne_Click);
                        this.contextMenuStrip_playlist.Items.Add(new ToolStripSeparator());
                        this.contextMenuStrip_playlist.Items.Add(autoOne);
                    }
                    catch { }
                }
            }
            catch { }

            // Default countdown enabled state
            try { PlaylistCountdownEnabled = true; } catch { }
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

            int syncDurationMs = Math.Max(0, WindowMain.CrossSyncDurationMs);
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
                List<AudioObj> syncSet = new() { nextTrack };
                syncSet.AddRange(externalPlayingTracks);

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

        private void playlistMenu_AutoEnqueueOne_Click(object? sender, EventArgs e)
        {
            try
            {
                // Prefer a selected playlist item from LoopControl if available.
                string? selectedPath = null;
                try
                {
                    if (LoopControlWindow != null && !LoopControlWindow.IsDisposed)
                        selectedPath = LoopControlWindow.GetSelectedPlaylistPath();
                }
                catch { selectedPath = null; }

                // If Ctrl is held, allow fallback to collection selection or file dialog
                bool ctrl = (ModifierKeys & Keys.Control) == Keys.Control;
                if (string.IsNullOrWhiteSpace(selectedPath) && ctrl)
                {
                    try { selectedPath = CollectionViews.Where(cv => !cv.IsDisposed).SelectMany(cv => cv.SelectedAudios).FirstOrDefault()?.FilePath; } catch { }
                    if (string.IsNullOrWhiteSpace(selectedPath))
                    {
                        using OpenFileDialog ofd = new()
                        {
                            Multiselect = false,
                            Filter = "Audio Files|*.wav;*.mp3;*.flac|All Files|*.*",
                            InitialDirectory = this.lastImportFolder
                        };
                        if (ofd.ShowDialog() != DialogResult.OK || ofd.FileNames.Length == 0) return;
                        selectedPath = ofd.FileNames[0];
                        if (!AllowedImportExtensions.Contains(Path.GetExtension(selectedPath))) return;
                    }
                }

                if (string.IsNullOrWhiteSpace(selectedPath))
                {
                    // If there is an already pre-prepared track in the engine, use that instead of failing.
                    try
                    {
                        // Prefer an already pre-prepared track that is NOT the currently playing track.
                        var preparedPathsAll = this._playlist?.ActiveOriginalPaths ?? Array.Empty<string>();
                        var preparedNonPlaying = this._playlist?.PreparedNonPlayingOriginalPaths ?? Array.Empty<string>();

                        // If the playlist has no next item (count <= 1), prefer any prepared track
                        // (excluding the playing original if possible). This ensures Auto enqueue one
                        // can pick a prepared track even when there's no explicit "next" in the queue.
                        string? candidate = null;
                        try
                        {
                            if (this._playlist != null && this._playlist.FilePaths.Count > 0)
                            {
                                // Attempt to pick a random candidate from the playlist starting at index currentIndex + 4
                                try
                                {
                                    int currentIndex = 0;
                                    string currentOriginal = this._playlist.OriginalCurrentPath ?? string.Empty;
                                    try { currentIndex = Math.Max(0, this._playlist.FilePaths.FindIndex(p => string.Equals(p, currentOriginal, StringComparison.OrdinalIgnoreCase))); } catch { currentIndex = 0; }
                                    int startIndex = currentIndex + 4;
                                    int count = this._playlist.FilePaths.Count;

                                    if (startIndex < count)
                                    {
                                        var rng = new Random();
                                        // Build a pool excluding the currently playing original to avoid reselecting it
                                        var pool = this._playlist.FilePaths.Skip(startIndex)
                                            .Where(p => !string.Equals(p, currentOriginal, StringComparison.OrdinalIgnoreCase))
                                            .ToList();
                                        if (pool.Count > 0)
                                        {
                                            candidate = pool[rng.Next(pool.Count)];
                                            if (!string.IsNullOrWhiteSpace(candidate))
                                            {
                                                LogCollection.Log($"Auto enqueue one: selected from playlist [{startIndex}..{count - 1}] -> {Path.GetFileNameWithoutExtension(candidate)}");
                                                // Remove any existing occurrences after the head to avoid duplicate entries when we insert below
                                                try
                                                {
                                                    for (int i = this._playlist.FilePaths.Count - 1; i >= 1; i--)
                                                    {
                                                        if (string.Equals(this._playlist.FilePaths[i], candidate, StringComparison.OrdinalIgnoreCase))
                                                            this._playlist.FilePaths.RemoveAt(i);
                                                    }
                                                }
                                                catch { }
                                            }
                                        }
                                    }
                                    else
                                    {
                                        // Fallback: prefer a prepared non-playing original, then any prepared, then directory sample
                                        candidate = preparedNonPlaying.FirstOrDefault(p => !string.Equals(p, this._playlist.OriginalCurrentPath ?? string.Empty, StringComparison.OrdinalIgnoreCase));
                                        if (string.IsNullOrWhiteSpace(candidate))
                                            candidate = preparedPathsAll.FirstOrDefault(p => !string.Equals(p, this._playlist.OriginalCurrentPath ?? string.Empty, StringComparison.OrdinalIgnoreCase));

                                        if (string.IsNullOrWhiteSpace(candidate))
                                        {
                                            try
                                            {
                                                var dir = Path.GetDirectoryName(this._playlist.OriginalCurrentPath ?? string.Empty) ?? string.Empty;
                                                var filePaths = Directory.Exists(dir)
                                                    ? Directory.GetFiles(dir).Where(p => !string.IsNullOrWhiteSpace(p) && AllowedImportExtensions.Contains(Path.GetExtension(p))).ToArray()
                                                    : Array.Empty<string>();
                                                if (filePaths.Length > 0)
                                                    candidate = filePaths[new Random().Next(filePaths.Length)];
                                            }
                                            catch { }
                                        }
                                    }
                                }
                                catch { }
                            }
                        }
                        catch { }

                        if (!string.IsNullOrWhiteSpace(candidate))
                        {
                            selectedPath = candidate;
                            LogCollection.Log($"Auto enqueue one: using already pre-prepared track -> {Path.GetFileNameWithoutExtension(selectedPath)}");
                        }
                    }
                    catch { }
                }

                if (string.IsNullOrWhiteSpace(selectedPath))
                {
                    LogCollection.Log("Auto enqueue one: no playlist selection available.");
                    return;
                }

                // Insert as next track (after index 0 if playing)
                int insertIndex = 0;
                try
                {
                    // Mutate the playlist under lock to avoid races and to remove any existing
                    // occurrences of the selected path so we don't enqueue duplicates.
                    lock (this._playlist)
                    {
                        // Remove existing occurrences of the same path (case-insensitive)
                        this._playlist.FilePaths.RemoveAll(p => string.Equals(p, selectedPath, StringComparison.OrdinalIgnoreCase));

                        insertIndex = this._playlist.IsPlaying ? Math.Min(1, this._playlist.FilePaths.Count) : 0;

                        // If selectedPath equals the current head, adjust to insert after head to avoid immediate duplicate at index 0.
                        if (string.Equals(selectedPath, this._playlist.FilePaths.FirstOrDefault() ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                        {
                            insertIndex = Math.Min(insertIndex + 1, this._playlist.FilePaths.Count);
                        }

                        this._playlist.FilePaths.Insert(insertIndex, selectedPath);
                    }
                }
                catch { insertIndex = 0; }
                LogCollection.Log($"Playlist: auto-enqueued one -> {Path.GetFileNameWithoutExtension(selectedPath)}");
                this.UpdatePlaylistUI();

                // Kick off pre-prepare for the newly enqueued track
                try
                {
                    string? pathToPrepare = null;
                    lock (this._playlist)
                    {
                        pathToPrepare = this._playlist.FilePaths.Count > insertIndex ? this._playlist.FilePaths[insertIndex] : null;
                    }

                    if (!string.IsNullOrWhiteSpace(pathToPrepare) && this._playlist.BeforeTrackPlay != null)
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                string? preprocessed = null;
                                try { preprocessed = await this._playlist.BeforeTrackPlay(pathToPrepare, CancellationToken.None).ConfigureAwait(false); } catch { }
                                if (!string.IsNullOrWhiteSpace(preprocessed) && File.Exists(preprocessed))
                                {
                                    LogCollection.Log($"Playlist: pre-prepared {Path.GetFileNameWithoutExtension(pathToPrepare)} (temp)");
                                }
                                else
                                {
                                    LogCollection.Log($"Playlist: pre-prepare requested but no preprocessed file produced for {Path.GetFileNameWithoutExtension(pathToPrepare)}");
                                }

                                // Also attempt to pre-prepare the following track in queue to keep pipeline filled
                                string? nextToPrepare = null;
                                lock (this._playlist)
                                {
                                    nextToPrepare = this._playlist.FilePaths.Count > insertIndex + 1 ? this._playlist.FilePaths[insertIndex + 1] : null;
                                }
                                if (!string.IsNullOrWhiteSpace(nextToPrepare))
                                {
                                    try
                                    {
                                        string? pre2 = null;
                                        try { pre2 = await this._playlist.BeforeTrackPlay(nextToPrepare, CancellationToken.None).ConfigureAwait(false); } catch { }
                                        if (!string.IsNullOrWhiteSpace(pre2) && File.Exists(pre2))
                                            LogCollection.Log($"Playlist: additionally pre-prepared {Path.GetFileNameWithoutExtension(nextToPrepare)} (temp)");
                                    }
                                    catch { }
                                }
                            }
                            catch (Exception ex) { LogCollection.Log($"Auto-enqueue preprepare failed: {ex.Message}"); }
                        });
                    }
                }
                catch { }
            }
            catch (Exception ex)
            {
                LogCollection.Log(ex);
            }
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
                        offload: settings.Offload).ConfigureAwait(false);
                    PlaylistNormalizer.ApplyRmsGain(audio.Data, preRms);
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
                ? Path.GetFileNameWithoutExtension(this._playlist.CurrentPath) ?? "–"
                : "–";
            if (!string.IsNullOrWhiteSpace(name) && name.Contains("__"))
                name = name.Split(new[] { "__" }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? name;
            if (name.Length > 96) name = name[..63] + "…";

            // Prefer the engine-reported current BPM (already adjusted for any applied stretch).
            float bpm = this._playlist.CurrentBpm;
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

            string name = Path.GetFileNameWithoutExtension(path) ?? "–";
            // Remove generated suffixes after a double-underscore and common _stretched_ markers
            if (name.Contains("__"))
                name = name.Split(new[] { "__" }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? name;
            if (name.IndexOf("_stretched_", StringComparison.OrdinalIgnoreCase) >= 0)
                name = name.Split(new[] { "_stretched_" }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? name;

            // Prefer engine-reported current BPM (reflects applied stretch). If missing, fall back to stretch settings.
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
            if (this._playlist.IsPaused) return;

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
