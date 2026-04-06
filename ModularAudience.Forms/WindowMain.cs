using ModularAudience.Audio;
using ModularAudience.Forms.Modules;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ModularAudience.Audio.Processors_V2;
using ModularAudience.Audio.Processors_V1;
using ModularAudience.Audio.Processors_V3;
using ModularAudience.Forms.Helpers;

namespace ModularAudience.Forms
{
    public partial class WindowMain : Form
    {
        public static WindowMain? Instance { get; private set; }

        public readonly AudioCollection AudioC = new();

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


        // Map AudioObj.Id -> Collection number (01-based) to restore distribution
        private static readonly Dictionary<Guid, int> AudioCollectionTags = [];
        private static readonly HashSet<string> AllowedImportExtensions = new(StringComparer.OrdinalIgnoreCase) { ".wav", ".mp3", ".flac" };
        private static readonly Random ResourceRandom = new();
        private static readonly Size CollectionCascadeOffset = new(26, 28);
        private const int CollectionBaseMargin = 5;
        private static readonly Padding TrackViewScreenMargin = new(20, 20, 20, 20);
        private static readonly Size TrackViewSpacing = new(15, 12);
        private bool suppressExportFormatEvent;
        private string lastImportFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources");
        internal bool StructuredImports => this.checkBox_structure.Checked;

        internal static bool SuppressCollectionViewPositioning = false;

        // Recording timer
        private System.Windows.Forms.Timer? recordingTimer = null;
        private DateTime _infoCtrlToStopAppeared = DateTime.MinValue;

        // Copy + Paste AudioObj
        internal static AudioObj? ClipboardAudioObj = null;


        public WindowMain()
        {
            Instance = this;
            this.InitializeComponent();
            this.KeyPreview = true;
            this.StartPosition = FormStartPosition.Manual;
            this.Location = WindowsScreenHelper.GetCornerPosition(this, false, true);

            // Shift + LeftClick on the form background should bring all open forms of this app to the front
            this.MouseDown += this.WindowMain_MouseDown_BringAllToFront;

            this.Register_ListBox_Log();
            TrackViews.ListChanged += this.TrackViews_ListChanged;
            CollectionViews.ListChanged += this.CollectionViews_ListChanged;

            this.FormClosing += this.WindowMain_FormClosing;
            this.LocationChanged += (_, __) => this.PositionCollectionViews();
            this.SizeChanged += (_, __) => this.PositionCollectionViews();

            this.AllowDrop = true;
            this.DragEnter += this.WindowMain_DragEnter;
            this.DragDrop += this.WindowMain_DragDrop;

            this.textBox_scanBpmResult.DoubleClick += this.textBox_scanBpmResult_DoubleClick;

            this.InitializeExportControls();

            this._keyFilter = new GlobalKeyMessageFilter();
            this._keyFilter.KeyChanged += this.GlobalKeyChanged;
            Application.AddMessageFilter(this._keyFilter);
            this.UpdateTrackDependentUI();
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
                            open.Invoke((Action)(() => BringFormToFrontSafe(open)));
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
            this.StopSyncer();
            this.StopPausingSyncer();
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

            // Close child windows quickly on UI thread where necessary (best-effort, non-blocking)
            try
            {
                foreach (var tv in TrackViews.ToList())
                {
                    try
                    {
                        if (tv == null) continue;
                        if (!tv.IsDisposed)
                        {
                            if (tv.InvokeRequired)
                            {
                                try { tv.Invoke((Action)tv.Close); } catch { /* ignore */ }
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
                        if (tv == null) continue;
                        if (!tv.IsDisposed)
                        {
                            if (tv.InvokeRequired)
                            {
                                try { tv.Invoke((Action)tv.Close); } catch { /* ignore */ }
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


    }
}









