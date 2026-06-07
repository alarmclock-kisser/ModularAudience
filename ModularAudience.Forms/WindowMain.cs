using ModularAudience.Audio;
using ModularAudience.Forms.Modules;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ModularAudience.Audio.Processors_V2;
using ModularAudience.Audio.Processors_V1;
using ModularAudience.Audio.Processors_V3;
using ModularAudience.Forms.Helpers;
using ModularAudience.Forms.Modules.Dialogs;

namespace ModularAudience.Forms
{
    public partial class WindowMain : Form
    {
        public static WindowMain? Instance { get; private set; }

        public readonly AudioCollection AudioC = new();

        public float MasterLimiter => 1f - (float) this.vScrollBar_masterLimiter.Value / Math.Max(1, this.vScrollBar_masterLimiter.Maximum);

        internal static LoopControl? LoopControlWindow = null;
        internal static DeveloperFunctions? DeveloperFunctionsWindow = null;
        internal static CudaFunctions? CudaFunctionsWindow = null;

        internal static readonly BindingList<AudioCollectionView> CollectionViews = [];
        internal static int TotalTracks => CollectionViews.Sum(cv => cv.AudioCount);
        internal static IEnumerable<AudioObj> SelectedTracks => CollectionViews.Where(cv => !cv.IsDisposed).SelectMany(cv => cv.SelectedAudios);
        internal static IEnumerable<TrackView> PlayingTrackViews => TrackViews.Where(tv => tv.OriginalAudio.PlayerPlaying);
        internal static bool IsAnyTrackPlaying => PlayingTrackViews.Any();
        internal static IEnumerable<TrackView> SyncedTrackViews => TrackViews.Where(tv => tv.Synced);

        internal static readonly BindingList<TrackView> TrackViews = [];
        internal static List<int> TrackViewIds { get; set; } = [];
        private static TrackView? _lastSelectedTrackView = null;
        private CancellationTokenSource? _syncerCts;
        private NudgingPlaybackSyncer? _syncer;
        private CancellationTokenSource? _pausingCts;
        private PausingPlaybackSyncer? _pausingSyncer;
        private bool _nudgingActive;
        private bool _pausingActive;
        private bool _shiftPressed;
        private GlobalKeyMessageFilter? _keyFilter;
        internal static TrackView? LastSelectedTrackView
        {
            get => _lastSelectedTrackView;
            set
            {
                if (!ReferenceEquals(_lastSelectedTrackView, value))
                {
                    _lastSelectedTrackView = value;
                    Instance?.UpdateTrackDependentUI();
                    Instance?.HighlightSelectedTrackView();
                    LoopControlWindow?.UpdateLoopButtonsState();
                    DeveloperFunctionsWindow?.UpdateControlStates();
                }
            }
        }

        internal static int? CurrentScreenId = 0;


        // Map AudioObj.Id -> Collection number (01-based) to restore distribution
        internal static readonly Dictionary<Guid, int> AudioCollectionTags = [];
        internal static readonly HashSet<string> AllowedImportExtensions = new(StringComparer.OrdinalIgnoreCase) { ".wav", ".mp3", ".flac" };
        private static readonly Random ResourceRandom = new();
        private static readonly Size CollectionCascadeOffset = new(26, 28);
        private const int CollectionBaseMargin = 2;
        private static readonly Padding TrackViewScreenMargin = new(20, 20, 20, 20);
        private static readonly Size TrackViewSpacing = new(15, 12);
        private bool suppressExportFormatEvent;
        private string lastImportFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources");
        internal bool StructuredImports => this.checkBox_structure.Checked;

        internal static bool SuppressCollectionViewPositioning = false;

        // Recording timer
        private System.Windows.Forms.Timer? recordingTimer = null;
        private DateTime _infoCtrlToStopAppeared = DateTime.MinValue;
        // Track mouse-driven move so we save on mouse up instead of polling
        private bool _isMouseDownForPosition = false;
        // Keep reference to log event handler to avoid duplicate subscriptions
        private Action<DateTime, string>? _logPostedWithTimestampHandler = null;

        // Copy + Paste AudioObj
        internal static AudioObj? ClipboardAudioObj = null;

        // Playlist FilePaths
        internal static List<string> PlaylistFilePaths = [];

        // User comment history (newest first) and draft
        public static List<string> CommentHistory { get; } = [];
        public static string CommentDraft { get; set; } = string.Empty;
        // When true, typing in comment dialog should not trigger pausing/syncer key handlers
        internal bool IsCommentDialogOpen = false;

        // Crossfade duration in seconds
        public static double CrossfadeDurationSeconds = 0.0;

        // Duration in ms for which PausingPlaybackSyncer runs at the start of each crossfade transition
        public static int CrossSyncDurationMs = 500;
        private System.Windows.Forms.Timer? _positionSaveTimer;

        public WindowMain()
        {
            Instance = this;
            this.InitializeComponent();
            this.Tag = this.Text;
            this.KeyPreview = true;
            this.StartPosition = FormStartPosition.Manual;
            // Try restore last saved position (multi-screen aware). Fallback to corner position.
            if (!WindowsScreenHelper.TryRestoreFormPosition(this))
            {
                this.Location = WindowsScreenHelper.GetCornerPosition(this, false, true, CurrentScreenId);
            }
            CurrentScreenId = WindowsScreenHelper.GetScreenId(Instance);

            // Shift + LeftClick on the form background should bring all open forms of this app to the front
            this.MouseDown += this.WindowMain_MouseDown_BringAllToFront;

            this.Register_ListBox_Log();
            TrackViews.ListChanged += this.TrackViews_ListChanged;
            CollectionViews.ListChanged += this.CollectionViews_ListChanged;

            this.FormClosing += this.WindowMain_FormClosing;
            this.LocationChanged += this.WindowMain_LocationChanged_ForPositionSave;
            this.LocationChanged += (_, __) => this.PositionCollectionViews();
            this.SizeChanged += (_, __) => this.PositionCollectionViews();

            this.AllowDrop = true;
            this.DragEnter += this.WindowMain_DragEnter;
            this.DragDrop += this.WindowMain_DragDrop;

            this.textBox_scanBpmResult.DoubleClick += this.textBox_scanBpmResult_DoubleClick;

            this.InitializeExportControls();
            this.InitPlaylist();
            AudioPlaybackService.SetMasterLimiter(this.MasterLimiter);

            this._keyFilter = new GlobalKeyMessageFilter();
            this._keyFilter.KeyChanged += this.GlobalKeyChanged;
            Application.AddMessageFilter(this._keyFilter);
            // Save form position when user finishes moving (mouse up) or when keyboard moves it
            this.MouseDown += this.WindowMain_MouseDown_ForPositionSave;
            this.MouseUp += this.WindowMain_MouseUp_ForPositionSave;
            this.KeyDown += this.WindowMain_KeyDown_ForPositionSave;
            this.UpdateTrackDependentUI();
        }

        private void button_copyLog_Click(object? sender, EventArgs e)
        {
            try
            {
                // Compose full log as newline-joined string from LogCollection.Logs
                string all = string.Empty;
                try { all = string.Join(Environment.NewLine, LogCollection.Logs.ToArray()); } catch { all = string.Empty; }
                if (!string.IsNullOrEmpty(all))
                {
                    try
                    {
                        Clipboard.SetText(all);
                    }
                    catch { }
                }
            }
            catch { }
        }

        private void WindowMain_LocationChanged_ForPositionSave(object? sender, EventArgs e)
        {
            try
            {
                // If user is currently dragging with mouse, defer saving until MouseUp.
                if (this._isMouseDownForPosition)
                {
                    return;
                }

                // Otherwise, save immediately (keyboard move or programmatic)
                WindowsScreenHelper.SaveFormPosition(this);
            }
            catch { }
        }

        private void WindowMain_MouseDown_ForPositionSave(object? sender, MouseEventArgs e)
        {
            try
            {
                if (e.Button == MouseButtons.Left)
                {
                    this._isMouseDownForPosition = true;
                }
            }
            catch { }
        }

        private void WindowMain_MouseUp_ForPositionSave(object? sender, MouseEventArgs e)
        {
            try
            {
                if (e.Button == MouseButtons.Left)
                {
                    this._isMouseDownForPosition = false;
                    WindowsScreenHelper.SaveFormPosition(this);
                }
            }
            catch { }
        }

        private void WindowMain_KeyDown_ForPositionSave(object? sender, KeyEventArgs e)
        {
            try
            {
                // Arrow keys may move window when user uses accessibility or window-management shortcuts
                if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right || e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
                {
                    // small delay to allow Location to update - but save immediately as best-effort
                    WindowsScreenHelper.SaveFormPosition(this);
                }
            }
            catch { }
        }

        private void WindowMain_MouseDown_BringAllToFront(object? sender, MouseEventArgs e)
        {
            try
            {
                if (e.Button != MouseButtons.Left)
                {
                    return;
                }

                if ((ModifierKeys & Keys.Shift) != Keys.Shift)
                {
                    return;
                }

                // Iterate over Application.OpenForms and try to bring each to the foreground
                foreach (Form open in Application.OpenForms.Cast<Form>().ToArray())
                {
                    try
                    {
                        if (open.IsDisposed)
                        {
                            continue;
                        }

                        if (open.InvokeRequired)
                        {
                            open.Invoke((Action) (() => BringFormToFrontSafe(open)));
                        }
                        else
                        {
                            BringFormToFrontSafe(open);
                        }
                    }
                    catch
                    {
                        // best-effort: ignore individual failures
                    }
                }
            }
            catch
            {
                // swallow - this action is best-effort
            }
        }

        private static void BringFormToFrontSafe(Form form)
        {
            try
            {
                if (form.WindowState == FormWindowState.Minimized)
                {
                    form.WindowState = FormWindowState.Normal;
                }

                // Attempt to bring to front and activate
                form.BringToFront();
                try { form.Activate(); } catch { }
            }
            catch
            {
                // ignore
            }
        }

        private void WindowMain_FormClosing(object? sender, FormClosingEventArgs e)
        {
            try { WindowsScreenHelper.SaveFormPosition(this); } catch { }
            this.StopSyncer();
            this.StopPausingSyncer();
            this.DisposePlaylist();
            if (this._keyFilter != null)
            {
                try { Application.RemoveMessageFilter(this._keyFilter); } catch { }
                this._keyFilter.KeyChanged -= this.GlobalKeyChanged;
                this._keyFilter = null;
            }
            // Detach handler to avoid re-entry
            try { this.FormClosing -= this.WindowMain_FormClosing; } catch { }

            // Stop timers and lightweight UI work synchronously (fast)
            try
            {
                if (this.recordingTimer != null)
                {
                    try { this.recordingTimer.Stop(); } catch { }
                    try { this.recordingTimer.Dispose(); } catch { }
                    this.recordingTimer = null;
                }
            }
            catch { }

            try
            {
                if (this._positionSaveTimer != null)
                {
                    try { this._positionSaveTimer.Stop(); } catch { }
                    try { this._positionSaveTimer.Dispose(); } catch { }
                    this._positionSaveTimer = null;
                }
            }
            catch { }

            // Close child windows quickly on UI thread where necessary (best-effort, non-blocking)
            try
            {
                foreach (var tv in TrackViews.ToList())
                {
                    try
                    {
                        if (tv == null)
                        {
                            continue;
                        }

                        if (!tv.IsDisposed)
                        {
                            if (tv.InvokeRequired)
                            {
                                try { tv.Invoke((Action) tv.Close); } catch { /* ignore */ }
                            }
                            else
                            {
                                try { tv.Close(); } catch { /* ignore */ }
                            }
                        }
                    }
                    catch { /* ignore individual child errors */ }
                }
            }
            catch { }

            // Fire-and-forget background cleanup so UI closes immediately.
            // We intentionally DO NOT await this; it's best-effort.
            try
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // Try clearing audio collection without blocking UI close.
                        // Wrap in try/catch and swallow any exceptions (process may exit immediately).
                        await this.AudioC.ClearAsync().ConfigureAwait(false);
                    }
                    catch { }
                });
            }
            catch { }

            // Exit process immediately and silently to make the application terminate as fast as possible.
            // Application.ExitThread and Environment.Exit are best-effort; Environment.Exit will terminate process.
            try { Application.ExitThread(); } catch { }
            try { Environment.Exit(0); } catch { }
        }

        private async Task CleanupAsync()
        {
            // Kept for compatibility if called elsewhere; perform best-effort synchronous-ish cleanup then return quickly.
            try
            {
                // Close child windows quickly on UI thread where necessary (best-effort)
                foreach (var tv in TrackViews.ToList())
                {
                    try
                    {
                        if (tv == null)
                        {
                            continue;
                        }

                        if (!tv.IsDisposed)
                        {
                            if (tv.InvokeRequired)
                            {
                                try { tv.Invoke((Action) tv.Close); } catch { /* ignore */ }
                            }
                            else
                            {
                                try { tv.Close(); } catch { /* ignore */ }
                            }
                        }
                    }
                    catch { /* ignore individual child errors */ }
                }

                // Stop and dispose timer if present
                try
                {
                    if (this.recordingTimer != null)
                    {
                        try { this.recordingTimer.Stop(); } catch { }
                        try { this.recordingTimer.Dispose(); } catch { }
                        this.recordingTimer = null;
                    }
                }
                catch { }

                // Do not block the UI shutdown long — attempt to clear audio collection but with a short timeout.
                try
                {
                    var clearTask = this.AudioC.ClearAsync();
                    // Wait briefly to allow cleanup to start but do not block shutdown indefinitely
                    var completed = await Task.WhenAny(clearTask, Task.Delay(2000)).ConfigureAwait(false);
                    if (completed == clearTask)
                    {
                        // allow potential exceptions to surface to the catch below
                        await clearTask.ConfigureAwait(false);
                    }
                }
                catch { /* swallow - best-effort */ }

                // Finally try to exit threads cleanly (best-effort)
                try { Application.ExitThread(); } catch { }
            }
            catch (Exception ex)
            {
                try { LogCollection.Log($"CleanupAsync: Exception during cleanup: {ex.Message}"); } catch { }
            }
            finally
            {
                // Last resort: ensure process terminates (non-returning)
                try { Environment.Exit(0); } catch { }
            }
        }

        private void comboBox_exportBits_SelectedIndexChanged(object sender, EventArgs e)
        {
            GlobalExportBits = WindowMainFormatHelpers.ResolveBitSelection(
                WindowMainFormatHelpers.NormalizeFormatExtension(this.comboBox_exportFormat.SelectedItem as string),
                this.comboBox_exportBits.SelectedItem);
        }

        private void checkBox_oneBag_CheckedChanged(object sender, EventArgs e)
        {
            this.button_newBag.Enabled = !this.checkBox_oneBag.Checked;

            if (this.checkBox_oneBag.Checked)
            {
                this.MergeCollectionsToSingle();
            }
        }

        private void button_bringAllToFront_Click(object sender, EventArgs e)
        {
            foreach (Form open in Application.OpenForms.Cast<Form>().ToArray())
            {
                try
                {
                    if (open.IsDisposed)
                    {
                        continue;
                    }
                    if (open.InvokeRequired)
                    {
                        open.Invoke((Action) (() => BringFormToFrontSafe(open)));
                    }
                    else
                    {
                        BringFormToFrontSafe(open);
                    }
                }
                catch
                {
                    // best-effort: ignore individual failures
                }
            }
        }

        private void toolStripMenuItem_crossfade_Click(object sender, EventArgs e)
        {
            // Prompt user for crossfade duration VBasic-style input box
            string input = Microsoft.VisualBasic.Interaction.InputBox(
                "Enter crossfade duration in seconds (e.g. 2.5):",
                "Set Crossfade Duration",
                CrossfadeDurationSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (double.TryParse(input, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double result))
            {
                CrossfadeDurationSeconds = Math.Max(0.0, result);
            }
        }

        private void toolStripMenuItem_crossSyncDuration_Click(object sender, EventArgs e)
        {
            string input = Microsoft.VisualBasic.Interaction.InputBox(
                "Enter cross-sync duration in milliseconds (default 500):",
                "Set Cross Sync Duration",
                CrossSyncDurationMs.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (int.TryParse(input, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int result))
            {
                CrossSyncDurationMs = Math.Clamp(result, 0, 60_000);
                Audio.LogCollection.Log($"Cross sync duration set to {CrossSyncDurationMs} ms.");
            }
        }

        private void toolStripMenuItem_timestretchEach_DoubleClick(object sender, EventArgs e)
        {
            // Do explicitly NOT toggle the menu item on double-click, instead open TimeStretchDialog to configure later enqueued tracks.
            using (var dialog = new TimeStretchDialog())
            {
                dialog.ShowDialog(this);
            }
        }

        private void listBox_log_Click(object sender, MouseEventArgs e)
        {
            // Right-click: show context menu to copy all log entries to clipboard
            if (e.Button != MouseButtons.Right)
            {
                return;
            }

            if (this.listBox_log.Items.Count == 0)
            {
                return;
            }

            var contextMenu = new ContextMenuStrip();
            var copyAllItem = new ToolStripMenuItem("Copy All");
            copyAllItem.Click += (s, args) =>
            {
                try
                {
                    string allLogs = string.Join(Environment.NewLine, this.listBox_log.Items.Cast<object>().Select(o => o?.ToString() ?? string.Empty));
                    if (!string.IsNullOrEmpty(allLogs))
                    {
                        Clipboard.SetText(allLogs);
                    }
                }
                catch { /* ignore clipboard errors */ }
            };
            contextMenu.Items.Add(copyAllItem);

            // Show at mouse location relative to the list box
            try
            {
                contextMenu.Show(this.listBox_log, e.Location);
            }
            catch
            {
                // Fallback: show at cursor position
                try { contextMenu.Show(Cursor.Position); } catch { }
            }
        }


        private ToolTip limiterToolTip = new();
        private void vScrollBar_masterLimiter_Scroll(object sender, ScrollEventArgs e)
        {
            // Dein bestehender Code fürs Audio-Backend
            AudioPlaybackService.SetMasterLimiter(this.MasterLimiter);

            // Prüfen, ob der User die Maustaste/den Thumb losgelassen hat
            if (e.Type == ScrollEventType.EndScroll)
            {
                // Losgelassen -> Tooltip sofort ausblenden
                this.limiterToolTip.Hide(this.vScrollBar_masterLimiter);
            }
            else
            {
                // Während des Scrollens/Haltens -> Tooltip updaten und an der Maus positionieren

                // Tipp: Häng direkt deine Einheit dran (z.B. dB oder %), das liest sich im UI besser
                string hintText = $"Limiter: {this.MasterLimiter} dB";

                // Position berechnen, damit der Tooltip genau neben dem Thumb an der Maus schwebt
                // (x + 20 damit er nicht direkt unter dem Mauszeiger klebt und flackert)
                Point mousePos = this.vScrollBar_masterLimiter.PointToClient(MousePosition);
                Point tooltipLocation = new Point(mousePos.X + 20, mousePos.Y - 10);

                // Tooltip einblenden bzw. Text/Position in Echtzeit updaten
                this.limiterToolTip.Show(hintText, this.vScrollBar_masterLimiter, tooltipLocation);
            }
        }
    }
}









