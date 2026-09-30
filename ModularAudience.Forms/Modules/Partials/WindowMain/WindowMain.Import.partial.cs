using ModularAudience.Audio;
using ModularAudience.Audio.Processors_V1;
using ModularAudience.Forms.Helpers;
using ModularAudience.Forms.Modules;
using ModularAudience.Forms.Modules.Dialogs;
using ModularAudience.Audio.Midi;
using System.ComponentModel;
using ModularAudience.Audio.Omr;
using ModularAudience.Audio.Processing;

namespace ModularAudience.Forms
{
    public partial class WindowMain
    {
        private PlaylistStretchSettings? _importStretchSettings;

        private ToolTip? _logToolTip;
        private ContextMenuStrip? _logContextMenu;
        private int _logSelectionAnchor = -1;
        private int _logDragEndIndex = -1;
        private bool _logShiftDragging;
        private Font? _logListFont;

        private void Register_ListBox_Log()
        {
            this.listBox_log.Items.Clear();
            this.listBox_log.DataSource = LogManager.Logs;
            this.listBox_log.HorizontalScrollbar = true;

            // Enable double buffering to reduce white/blank flicker when scrolling or updating
            try
            {
                var prop = this.listBox_log.GetType().GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                prop?.SetValue(this.listBox_log, true, null);
            }
            catch { }

            // Compact font & fixed item height.
            // IntegralHeight must be false, otherwise WinForms shrinks/grows the whole control
            // to fit a whole number of rows whenever ItemHeight changes (e.g. Ctrl+Wheel).
            this.listBox_log.IntegralHeight = false;
            _logListFont = new Font("Consolas", 8f, FontStyle.Regular);
            this.listBox_log.Font = _logListFont;
            this.listBox_log.ItemHeight = 16;
            this.listBox_log.DrawMode = DrawMode.OwnerDrawFixed;
            // MultiExtended is the only mode that allows SetSelected() and gives us the
            // native click / ctrl+click / shift+click behaviour we build upon.
            this.listBox_log.SelectionMode = SelectionMode.MultiExtended;

            // We own the rendering, so no system blue highlight is drawn.
            this.listBox_log.DrawItem += ListBox_Log_DrawItem;

            // Re-render when the bound collection changed.
            LogManager.Logs.ListChanged += (_, __) =>
            {
                try { this.listBox_log.Invalidate(); } catch { }
            };

            // Keep view pinned to the newest entry unless the user scrolled away.
            LogManager.Logs.ListChanged += (_, __) =>
            {
                try
                {
                    if (LogManager.AutoScroll && LogManager.Logs.Count > 0)
                    {
                        this.listBox_log.TopIndex = LogManager.Logs.Count - 1;
                    }
                }
                catch { }
            };

            Register_ListBox_Log_ToolTip();
            Register_ListBox_Log_Selection();
            Register_ListBox_Log_ContextMenu();
        }

        private void ListBox_Log_DrawItem(object? sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= LogManager.Logs.Count)
            {
                return;
            }

            Color background = this.listBox_log.BackColor;
            Color foreground = this.listBox_log.ForeColor;
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

            if (selected)
            {
                background = Color.FromArgb(38, 62, 58);
                foreground = Color.FromArgb(220, 245, 235);
            }
            else if ((e.State & DrawItemState.ComboBoxEdit) != 0)
            {
                return;
            }

            using (Brush backgroundBrush = new SolidBrush(background))
            {
                e.Graphics.FillRectangle(backgroundBrush, e.Bounds);
            }

            string text = LogManager.Logs[e.Index];
            const int textPadding = 2;

            using (Brush textBrush = new SolidBrush(foreground))
            {
                TextRenderer.DrawText(
                    e.Graphics,
                    text,
                    this.listBox_log.Font,
                    new Rectangle(e.Bounds.Left + textPadding, e.Bounds.Top, e.Bounds.Width - textPadding, e.Bounds.Height),
                    foreground,
                    TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }

        private void Register_ListBox_Log_ToolTip()
        {
            _logToolTip = new ToolTip
            {
                AutoPopDelay = 30000,
                InitialDelay = 300,
                ReshowDelay = 100,
                ShowAlways = true,
                UseAnimation = false,
                UseFading = false
            };

            this.listBox_log.MouseMove += (_, e) =>
            {
                int index = this.listBox_log.IndexFromPoint(e.Location);
                if (index < 0 || index >= LogManager.Logs.Count)
                {
                    return;
                }

                string fullText = LogManager.Logs[index];
                if (!string.IsNullOrEmpty(fullText) && _logToolTip!.GetToolTip(this.listBox_log) != fullText)
                {
                    _logToolTip.SetToolTip(this.listBox_log, fullText);
                }
            };
        }

        /// <summary>Resolves a y coordinate to a row index, clamping into the valid range when over empty space.</summary>
        private int GetLogIndexFromPoint(int y)
        {
            int count = LogManager.Logs.Count;
            if (count == 0)
            {
                return -1;
            }

            int index = this.listBox_log.IndexFromPoint(new Point(0, y));
            if (index >= 0)
            {
                return Math.Min(index, count - 1);
            }

            // Clicked in the empty area below the last item: select the last row.
            return count - 1;
        }

        private void SetLogSelectedRange(int start, int end)
        {
            int count = LogManager.Logs.Count;
            if (count == 0 || start < 0 || end < 0)
            {
                return;
            }

            int min = Math.Clamp(Math.Min(start, end), 0, count - 1);
            int max = Math.Clamp(Math.Max(start, end), 0, count - 1);

            this.listBox_log.BeginUpdate();
            try
            {
                for (int i = 0; i < count; i++)
                {
                    this.listBox_log.SetSelected(i, i >= min && i <= max);
                }
            }
            finally
            {
                this.listBox_log.EndUpdate();
            }
        }

        private void SelectLogSingle(int index, bool toggle)
        {
            if (index < 0 || index >= LogManager.Logs.Count)
            {
                return;
            }

            if (toggle)
            {
                this.listBox_log.SetSelected(index, !this.listBox_log.GetSelected(index));
            }
            else
            {
                this.listBox_log.SelectedIndices.Clear();
                this.listBox_log.SetSelected(index, true);
            }

            _logSelectionAnchor = index;
        }

        private void Register_ListBox_Log_Selection()
        {
            // SelectionMode.MultiExtended already implements plain click, ctrl+click toggle and
            // shift+click range selection natively - and it is the only mode where SetSelected()
            // is allowed. So we must NOT redo those gestures here, otherwise every gesture is
            // applied twice (which made ctrl+click cancel itself out).
            //
            // We only add what the native control cannot do:
            //   * live shift+drag range selection
            //   * shift+drag / click that starts in the empty area below the last row

            this.listBox_log.SelectedIndexChanged += (_, __) =>
            {
                if (!_logShiftDragging)
                {
                    _logSelectionAnchor = this.listBox_log.SelectedIndex;
                }
            };

            this.listBox_log.MouseDown += (_, e) =>
            {
                this.listBox_log.Focus();

                if (e.Button != MouseButtons.Left)
                {
                    return;
                }

                if ((ModifierKeys & Keys.Shift) != Keys.Shift)
                {
                    return;
                }

                // Shift gesture: take over so we can extend the range while dragging.
                if (_logSelectionAnchor < 0)
                {
                    _logSelectionAnchor = this.listBox_log.SelectedIndex;
                }
                if (_logSelectionAnchor < 0)
                {
                    _logSelectionAnchor = GetLogIndexFromPoint(e.Y);
                }

                _logShiftDragging = true;
                _logDragEndIndex = GetLogIndexFromPoint(e.Y);
                this.listBox_log.Capture = true;

                if (_logDragEndIndex >= 0)
                {
                    SetLogSelectedRange(_logSelectionAnchor, _logDragEndIndex);
                }
            };

            this.listBox_log.MouseMove += (_, e) =>
            {
                if (!_logShiftDragging || (e.Button & MouseButtons.Left) != MouseButtons.Left)
                {
                    return;
                }

                if (_logSelectionAnchor < 0)
                {
                    return;
                }

                int index = GetLogIndexFromPoint(e.Y);
                if (index >= 0)
                {
                    _logDragEndIndex = index;
                    SetLogSelectedRange(_logSelectionAnchor, index);
                }
            };

            this.listBox_log.MouseUp += (_, e) =>
            {
                if (e.Button != MouseButtons.Left)
                {
                    return;
                }

                if (_logShiftDragging && _logSelectionAnchor >= 0 && _logDragEndIndex >= 0)
                {
                    // Re-apply once more: when the drag began in the empty area the native
                    // handler cleared the selection after our MouseDown ran.
                    SetLogSelectedRange(_logSelectionAnchor, _logDragEndIndex);
                }

                EndLogRangeDrag();
            };

            // If the mouse is released outside the list (or the control loses the mouse /
            // focus) MouseUp never arrives - make sure the drag state can never get stuck,
            // otherwise a later gesture would be treated as a shift+drag.
            this.listBox_log.MouseCaptureChanged += (_, _) =>
            {
                if (!this.listBox_log.Capture)
                {
                    EndLogRangeDrag();
                }
            };
            this.listBox_log.LostFocus += (_, __) => EndLogRangeDrag();

            // Ctrl + mouse wheel over the list rescales the font (and item height),
            // without changing the size of the control itself.
            this.listBox_log.MouseWheel += (_, e) =>
            {
                if ((ModifierKeys & Keys.Control) != Keys.Control)
                {
                    return;
                }

                float currentSize = _logListFont?.Size ?? 8f;
                float newSize = Math.Clamp(currentSize + Math.Sign(e.Delta), 6f, 20f);
                if (Math.Abs(newSize - currentSize) < 0.01f)
                {
                    return;
                }

                ApplyLogListFontSize(newSize);
            };
        }

        private void EndLogRangeDrag()
        {
            _logShiftDragging = false;
            _logDragEndIndex = -1;
            if (this.listBox_log.Capture)
            {
                this.listBox_log.Capture = false;
            }
        }

        private void ApplyLogListFontSize(float size)
        {
            System.Drawing.Font previousFont = _logListFont ?? this.listBox_log.Font;
            System.Drawing.Font newFont = new(previousFont.FontFamily, size, previousFont.Style);

            // IntegralHeight must stay false, otherwise the control itself resizes to fit
            // a whole number of rows. Remember and restore the bounds regardless.
            bool keepIntegralHeight = this.listBox_log.IntegralHeight;
            Size keepSize = this.listBox_log.Size;

            _logListFont = newFont;
            this.listBox_log.IntegralHeight = false;
            this.listBox_log.Font = newFont;
            this.listBox_log.ItemHeight = Math.Max(10, (int)Math.Round(size * 1.7f));
            this.listBox_log.IntegralHeight = keepIntegralHeight;
            this.listBox_log.Size = keepSize;
            this.listBox_log.Invalidate();

            previousFont.Dispose();
        }

        private void Register_ListBox_Log_ContextMenu()
        {
            _logContextMenu = new ContextMenuStrip();

            ToolStripMenuItem copyItem = new("Copy Log Line(s)");
            copyItem.Click += (_, __) =>
            {
                var lines = this.listBox_log.SelectedIndices
                    .Cast<int>()
                    .Where(i => i >= 0 && i < LogManager.Logs.Count)
                    .Select(i => LogManager.Logs[i])
                    .ToList();

                if (lines.Count == 0)
                {
                    return;
                }

                try { Clipboard.SetText(string.Join(Environment.NewLine, lines)); } catch { }
            };
            _logContextMenu.Items.Add(copyItem);

            _logContextMenu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem clearItem = new("Clear Logs")
            {
                ForeColor = Color.Firebrick
            };
            clearItem.Click += (_, __) =>
            {
                _logSelectionAnchor = -1;
                LogManager.ClearLogs();
            };
            _logContextMenu.Items.Add(clearItem);

            _logContextMenu.Opening += (_, e) =>
            {
                // Select the row under the cursor so the menu acts on it.
                Point client = this.listBox_log.PointToClient(Cursor.Position);
                int index = GetLogIndexFromPoint(client.Y);

                if (index >= 0 && !this.listBox_log.GetSelected(index))
                {
                    _logSelectionAnchor = index;
                    SelectLogSingle(index, toggle: false);
                }

                copyItem.Enabled = this.listBox_log.SelectedIndices.Count > 0;
                clearItem.Enabled = LogManager.Logs.Count > 0;

                if (!copyItem.Enabled)
                {
                    e.Cancel = true;
                }
            };

            this.listBox_log.ContextMenuStrip = _logContextMenu;
        }

        private async void button_import_Click(object sender, EventArgs e)
        {
            IEnumerable<string> filesToImport = [];
            bool fromResources = false;

            if (ModifierKeys.HasFlag(Keys.Shift))
            {
                using FolderBrowserDialog folderBrowserDialog = new()
                {
                    Description = "Select Resource Folder to Import Audio Files From",
                    SelectedPath = this.lastImportFolder,
                    ShowNewFolderButton = false
                };

                if (folderBrowserDialog.ShowDialog() != DialogResult.OK)
                {
                    return;
                }

                this.lastImportFolder = folderBrowserDialog.SelectedPath;
                try
                {
                    filesToImport = Directory
                        .EnumerateFiles(folderBrowserDialog.SelectedPath, "*.*", SearchOption.AllDirectories)
                        .Where(f => AllowedImportExtensions.Contains(Path.GetExtension(f)));
                }
                catch (Exception ex)
                {
                    LogManager.Log($"Failed to scan resources at '{folderBrowserDialog.SelectedPath}': {ex.Message}");
                    return;
                }
            }
            else if (ModifierKeys.HasFlag(Keys.Alt))
            {
                string? resourceFile = this.TryGetRandomResourceFile();
                if (resourceFile == null)
                {
                    LogManager.Log("No resource audio files found for import.");
                    return;
                }

                filesToImport = [resourceFile];
                fromResources = true;
            }
            else
            {
                string initialDir = this.AudioC.ImportDirectory;
                if (ModifierKeys.HasFlag(Keys.Control))
                {
                    initialDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources");
                    LogManager.Log("Import: Using Resources folder as initial directory.");
                }

                using OpenFileDialog openFileDialog = new()
                {
                    InitialDirectory = this.lastImportFolder,
                    Filter = "Audio Files|*.wav;*.mp3;*.flac;*.mid|MIDI Files|*.mid;*.midi|PDF Files|*.pdf|All Supported Files|*.wav;*.mp3;*.flac;*.mid;*.midi;*.pdf",
                    Multiselect = true,
                    Title = "Import Audio Files / Loops",
                    RestoreDirectory = true
                };

                if (openFileDialog.ShowDialog() != DialogResult.OK)
                {
                    return;
                }

                filesToImport = openFileDialog.FileNames;
            }

            List<string> selectedFiles = filesToImport.ToList();
            List<string> midiFiles = selectedFiles
                .Where(file => MidiFileData.IsMidiPath(file))
                .ToList();
            List<string> audioFiles = selectedFiles
                .Where(file => !MidiFileData.IsMidiPath(file) && !Path.GetExtension(file).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                .ToList();
            List<string> pdfFiles = selectedFiles
                .Where(file => Path.GetExtension(file).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (string midiFile in midiFiles)
            {
                try
                {
                    MidiWindow midiWindow = new(midiFile);
                    midiWindow.Show(this);
                }
                catch (Exception ex)
                {
                    LogManager.Log($"MIDI import failed for '{midiFile}': {ex}");
                    ShowErrorWithCopyButton(this, "MIDI import failed", ex);
                }
            }

            foreach (string pdfFile in pdfFiles)
            {
                try
                {
                    ImageObj? imageObj = await ImageObj.LoadAsync(pdfFile);
                    if (imageObj == null)
                    {
                        LogManager.Log($"PDF import failed for '{pdfFile}': Unable to load PDF as image.");
                        continue;
                    }

                    ImageViewDialog viewDlg = new(imageObj);
                    viewDlg.Show();

                    OmrObj? omr = await ImageToOmrObjParser.ParseAsync(imageObj);
                    if (omr == null)
                    {
                        LogManager.Log($"PDF import failed for '{pdfFile}': Unable to parse OMR from image.");
                        continue;
                    }

                    var midiData = OmrToMidiObjConverter.Convert(omr);
                    if (midiData == null)
                    {
                        LogManager.Log($"PDF import failed for '{pdfFile}': Unable to generate MIDI from OMR.");
                        continue;
                    }

                    MidiWindow midiWindow = new(null, midiData);
                    midiWindow.Show(this);
                }
                catch (Exception ex)
                {
                    LogManager.Log($"PDF import failed for '{pdfFile}': {ex}");
                    ShowErrorWithCopyButton(this, "PDF import failed", ex);
                }
            }

            if (audioFiles.Count > 0)
            {
                await this.ImportAndPlaceAsync(audioFiles, fromResources);
            }

            // Remember the folder where files were imported from (for next time)
            try
            {
                if (filesToImport.Count() > 0)
                {
                    this.lastImportFolder = Path.GetDirectoryName(filesToImport.First()) ?? this.lastImportFolder;
                }
            }
            catch { }
        }

        private async void button_random_Click(object sender, EventArgs e)
        {
            // Get focussed track or last collection last track 's filepath base dir
            var lastTrack = LastSelectedTrackView?.OriginalAudio ?? CollectionViews.LastOrDefault(cv => cv != null && !cv.IsDisposed)?.AudioC.Audios.LastOrDefault();
            string baseDir = lastTrack != null && !string.IsNullOrWhiteSpace(lastTrack.FilePath) ? Path.GetDirectoryName(lastTrack.FilePath) ?? string.Empty : string.Empty;

            // If baseDir is empty or doesn't exist, fallback to random MyMusic audio file
            if (string.IsNullOrWhiteSpace(baseDir) || !Directory.Exists(baseDir))
            {
                baseDir = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            }

            Random rand = new();

            string[] allAudioFiles = Directory.Exists(baseDir)
                ? Directory.EnumerateFiles(baseDir, "*.*", SearchOption.AllDirectories)
                    .Where(f => AllowedImportExtensions.Contains(Path.GetExtension(f)))
                    .ToArray()
                : Array.Empty<string>();

            // Random selection: pick a random file, load it, and keep it only if its
            // duration is shorter than 12 minutes. Try another file otherwise.
            const double maxDurationSeconds = 12.0 * 60.0;
            AudioObj? randomAudio = null;
            int attempts = 0;
            int maxAttempts = allAudioFiles.Length > 0 ? allAudioFiles.Length : 100;
            while (attempts < maxAttempts && randomAudio == null)
            {
                attempts++;
                string candidate = allAudioFiles[rand.Next(allAudioFiles.Length)];
                var loaded = (await this.AudioC.LoadManyAsync([candidate])).ToList();
                var audio = loaded.FirstOrDefault(a => a != null);
                if (audio != null && audio.Duration.TotalSeconds < maxDurationSeconds)
                {
                    randomAudio = audio;
                }
                else
                {
                    this.AudioC.Audios.Remove(audio);
                    audio?.Dispose();
                }
            }

            if (randomAudio == null)
            {
                LogManager.Log("Random import: No audio files shorter than 12 minutes found.");
                return;
            }

            // Import into an existing AudioCollectionView (not a new one)
            var importDir = Path.GetDirectoryName(randomAudio.FilePath) ?? string.Empty;
            var targetView = CollectionViews
                .FirstOrDefault(cv => cv != null && !cv.IsDisposed &&
                    cv.AudioC.Audios.Any(a => Path.GetDirectoryName(a.FilePath) == importDir));

            if (targetView == null)
            {
                targetView = CollectionViews.LastOrDefault(cv => cv != null && !cv.IsDisposed);
            }

            if (targetView == null)
            {
                targetView = new AudioCollectionView([]);
            }

            int num = WindowMain.GetCollectionNumber(targetView);
            targetView.AudioC.Audios.Add(randomAudio);
            WindowMain.AudioCollectionTags[randomAudio.Id] = num;
            LogManager.Log($"{randomAudio.Name} imported into existing collection.");
            targetView.Show();

            if (this._importStretchSettings != null)
            {
                var track = LastSelectedTrackView?.OriginalAudio ?? CollectionViews.LastOrDefault(cv => cv != null && !cv.IsDisposed)?.AudioC.Audios.LastOrDefault();
                if (track != null)
                {
                    track.ReplaceWith(await TimeStretcher.TimeStretchAllThreadsAsync(track, this._importStretchSettings.ChunkSize, this._importStretchSettings.Overlap, this._importStretchSettings.StretchFactor, false, 0.8f, this._importStretchSettings.Threads, null, this._importStretchSettings.Offload, true));
                }
            }
        }

        private void timeStretchImportedToToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // Toggle: if already enabled, disable
            if (this.timeStretchImportedToToolStripMenuItem.Checked)
            {
                this.timeStretchImportedToToolStripMenuItem.Checked = false;
                this._playlistStretchSettings = null;
                this.timeStretchImportedToToolStripMenuItem.Text = "⏱ Timestretch each...";
                LogManager.Log("Playlist auto-timestretch disabled.");
                return;
            }

            // Open TimeStretchDialog in configure-only mode with a dummy audio
            var dummy = new AudioObj { Name = "Playlist Track", Bpm = 130f };
            using var dlg = new TimeStretchDialog(audios: [dummy])
            {
                IsConfigureMode = true
            };

            if (dlg.ShowDialog(this) != DialogResult.OK || dlg.ConfirmedSettings == null)
            {
                return;
            }

            this._importStretchSettings = dlg.ConfirmedSettings;
            this.timeStretchImportedToToolStripMenuItem.Checked = true;
            string method = dlg.ConfirmedUsedV2 ? "V2" : "V1";
            this.timeStretchImportedToToolStripMenuItem.Text = $"⏱ Timestretch each [{this._importStretchSettings.TargetBpm:F0} BPM, {method}]";
            LogManager.Log($"Playlist auto-timestretch enabled: target {this._importStretchSettings.TargetBpm:F0} BPM via Stretch {method}.");
        }

        private string? TryGetRandomResourceFile()
        {
            return WindowMainStaticHelpers.TryGetRandomResourceFile(AllowedImportExtensions, ResourceRandom);
        }

        private void WindowMain_DragEnter(object? sender, DragEventArgs e)
        {
            try
            {
                if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    var items = e.Data.GetData(DataFormats.FileDrop) as string[] ?? [];
                    if (items.Any(p => !string.IsNullOrWhiteSpace(p) &&
                        (Directory.Exists(p) || AllowedImportExtensions.Contains(Path.GetExtension(p)))))
                    {
                        e.Effect = DragDropEffects.Copy;
                        return;
                    }
                }
            }
            catch { }

            e.Effect = DragDropEffects.None;
        }

        private async void WindowMain_DragDrop(object? sender, DragEventArgs e)
        {
            if (e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                LogManager.Log("DragDrop: no FileDrop data.");
                return;
            }

            string[] dropped;
            try
            {
                dropped = e.Data.GetData(DataFormats.FileDrop) as string[] ?? [];
            }
            catch (Exception ex)
            {
                LogManager.Log($"DragDrop: failed to read dropped data: {ex.Message}");
                return;
            }

            var collectedPaths = new List<string>();
            foreach (var path in dropped.Where(p => !string.IsNullOrWhiteSpace(p)))
            {
                try
                {
                    if (Directory.Exists(path))
                    {
                        var found = Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories)
                            .Where(f => AllowedImportExtensions.Contains(Path.GetExtension(f)));
                        collectedPaths.AddRange(found);
                    }
                    else if (File.Exists(path) && AllowedImportExtensions.Contains(Path.GetExtension(path)))
                    {
                        collectedPaths.Add(path);
                    }
                }
                catch (Exception ex)
                {
                    LogManager.Log($"DragDrop: error scanning '{path}': {ex.Message}");
                }
            }

            var validPaths = collectedPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (validPaths.Count == 0)
            {
                LogManager.Log("DragDrop: No allowed audio files found in drop.");
                return;
            }

            try { this.lastImportFolder = Path.GetDirectoryName(validPaths[0]) ?? this.lastImportFolder; } catch { }
            await this.ImportAndPlaceAsync(validPaths, fromResources: false);
        }

        private async Task ImportAndPlaceAsync(IEnumerable<string> filePaths, bool fromResources)
        {
            var validPaths = filePaths.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
            if (validPaths.Count == 0)
            {
                return;
            }

            var loaded = (await this.AudioC.LoadManyAsync(validPaths)).ToList();
            var pairs = validPaths
                .Select((p, i) => (Path: Path.GetFullPath(p), Audio: i < loaded.Count ? loaded[i] : null))
                .Where(x => x.Audio != null)
                .Select(x => (x.Path, Audio: x.Audio!))
                .ToList();

            var importedAudios = pairs.Select(x => x.Audio!).ToList();
            if (importedAudios.Count == 0)
            {
                return;
            }

            foreach (var audio in importedAudios)
            {
                LogManager.Log(fromResources ? $"{audio.Name} imported from resources." : $"{audio.Name} imported.");
            }

            this.PlaceImportedAudios(pairs, importedAudios);

            foreach (var audio in importedAudios)
            {
                this.AudioC.Audios.Remove(audio);
            }
        }

        internal void PlaceImportedAudios(List<(string Path, AudioObj Audio)> importedPairs, List<AudioObj> importedAudios)
        {
            WindowMainStaticHelpers.InvokeIfRequired(Instance, () =>
            {
                bool prevSuppress = SuppressCollectionViewPositioning;
                SuppressCollectionViewPositioning = true;
                try
                {
                    var pairs = importedPairs;
                    if (this.AllInOneBag)
                    {
                        var targetView = CollectionViews.LastOrDefault(cv => cv != null && !cv.IsDisposed);
                        if (targetView == null)
                        {
                            targetView = new AudioCollectionView([]);
                        }

                        int num = GetCollectionNumber(targetView);
                        foreach (var audio in importedAudios)
                        {
                            targetView.AudioC.Audios.Add(audio);
                            AudioCollectionTags[audio.Id] = num;
                        }
                        targetView.Show();
                    }

                    else if (this.StructuredImports)
                    {
                        var groups = pairs.GroupBy(x => Path.GetDirectoryName(x.Path) ?? string.Empty);
                        foreach (var g in groups)
                        {
                            var audios = g.Select(x => x.Audio).ToList();
                            if (audios.Count == 0)
                            {
                                continue;
                            }

                            var newView = new AudioCollectionView(audios);
                            string folderName = Path.GetFileName(g.Key);
                            if (string.IsNullOrWhiteSpace(folderName))
                            {
                                folderName = g.Key;
                            }

                            try { newView.Rename(folderName); } catch { }
                            int num = GetCollectionNumber(newView);
                            foreach (var audio in audios)
                            {
                                AudioCollectionTags[audio.Id] = num;
                            }

                            newView.Show();
                        }
                    }
                    else
                    {
                        var audioList = pairs.Select(x => x.Audio).ToList();
                        // Find an existing collection from the same directory, or the last one
                        var importDir = pairs[0].Path != null ? Path.GetDirectoryName(pairs[0].Path) ?? string.Empty : string.Empty;
                        var targetView = CollectionViews
                            .FirstOrDefault(cv => cv != null && !cv.IsDisposed &&
                                (cv.AudioC.Audios.Any(a => Path.GetDirectoryName(a.FilePath) == importDir) ||
                                 cv.AudioC.Audios.Count == 0));

                        if (targetView == null)
                        {
                            var newView = new AudioCollectionView(audioList);
                            int num = GetCollectionNumber(newView);
                            foreach (var audio in audioList)
                            {
                                AudioCollectionTags[audio.Id] = num;
                            }
                            newView.Show();
                        }
                        else if (targetView.AudioCount == 0)
                        {
                            int num = GetCollectionNumber(targetView);
                            foreach (var audio in audioList)
                            {
                                targetView.AudioC.Audios.Add(audio);
                                AudioCollectionTags[audio.Id] = num;
                            }
                            targetView.Show();
                        }
                        else
                        {
                            var newView = new AudioCollectionView(audioList);
                            int num = GetCollectionNumber(newView);
                            foreach (var audio in audioList)
                            {
                                AudioCollectionTags[audio.Id] = num;
                            }
                            newView.Show();
                        }
                    }
                }
                finally
                {
                    SuppressCollectionViewPositioning = prevSuppress;
                }
            });

            foreach (var audio in importedAudios)
            {
                this.AudioC.Audios.Remove(audio);
            }

            // Opening a track directly counts as playlist activity, so the bottom
            // label switches from the build timestamp to the live playback info.
            this._playlistActivityStarted = true;
            WindowMainStaticHelpers.InvokeIfRequired(Instance, this.UpdatePlaylistUI);
        }

        internal void PlaceRenderedAudio(AudioObj audio)
        {
            ArgumentNullException.ThrowIfNull(audio);
            this.PlaceImportedAudios([($"MIDI:{audio.Name}", audio)], [audio]);
        }

    }
}
