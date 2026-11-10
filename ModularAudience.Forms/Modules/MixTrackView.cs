using ModularAudience.Audio;
using ModularAudience.Audio.Arrangement;
using System.Drawing.Drawing2D;
using ModularAudience.Forms.Modules.Dialogs;
using ModularAudience.Forms.Theming;
using ModularAudience.Audio.Processing;

namespace ModularAudience.Forms.Modules;

/// <summary>Modular non-destructive audio arrangement, using one clock for every lane.</summary>
public sealed partial class MixTrackView : Form
{
    public static IReadOnlyList<MixTrackView> OpenMixes => open.Where(m => !m.IsDisposed).ToArray();
    private static readonly List<MixTrackView> open = [];
    public MixArrangement Arrangement { get; private set; } = new();
    private readonly Dictionary<Guid, TrackView> joined = [];
    private readonly Stack<string> undo = new(), redo = new();
    private string committed = "";
    private string committedAudioKey = "", committedProject = "";
    private readonly MixWindowGroup windowGroup;
    private bool restoring, busy, closing, allowClose, dirty;
    private readonly Canvas canvas = new() { Dock = DockStyle.Fill, AllowDrop = true, TabStop = true };
    private readonly ToolTip interactionHints = new();
    private readonly HScrollBar scroll = new() { Dock = DockStyle.Bottom, Minimum = 0, Maximum = 100000, LargeChange = 100 };
    private readonly VScrollBar vertical = new() { Dock = DockStyle.Right, Minimum = 0, Maximum = 100, LargeChange = 1 };
    private readonly NumericUpDown bpm = new() { Minimum = 10, Maximum = 400, Value = 120, DecimalPlaces = 4, Width = 95 };
    private readonly ComboBox division = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 65 };
    private readonly CheckBox snap = new() { Text = "Snap", AutoSize = true };
    private readonly CheckBox loop = new() { Text = "Loop", AutoSize = true };
    private readonly CheckBox autoScroll = new() { Text = "No Scroll", Name = "autoScroll", ThreeState = true, AutoSize = true, AutoCheck = false };
    private readonly Button play = new() { Text = "Play", AutoSize = true };
    private readonly Label status = new() { Dock = DockStyle.Bottom, Height = 24, Text = "Drop audio here. Shift = snap. Drag ruler = loop. Right-click for actions." };
    private readonly System.Windows.Forms.Timer clock = new() { Interval = 30 };
    private readonly System.Windows.Forms.Timer tempoDebounce = new() { Interval = 2000 };
    private readonly System.Windows.Forms.Timer hueTimer = new() { Interval = 33 };
    private readonly System.Diagnostics.Stopwatch hueClock = System.Diagnostics.Stopwatch.StartNew();
    private double animatedHue;
    private AudioObj? playback;
    private CancellationTokenSource? operation;
    private Task renderWork = Task.CompletedTask;
    private Task playbackWork = Task.CompletedTask;
    private AudioObj? cachedMix;
    private string? cachedMixState;
    private long playbackGeneration;
    private bool released;
    private readonly System.Windows.Forms.Timer liveUpdateTimer = new() { Interval = 180 };
    private Task liveUpdateWork = Task.CompletedTask;
    private readonly MixPreparationCache preparationCache = new();
    private string? requestedPreparation;
    private CancellationTokenSource? liveUpdateCancellation;
    private readonly Dictionary<(Guid Source, int Channels), float[][]> wavePeaks = [];
    private double pixelsPerSecond = 90, caret;
    private Guid? selectedClip, selectedLane;
    private MixClip? copiedClip;
    private readonly HashSet<Guid> selectedClips = [];
    private double selectionStart, selectionEnd;
    private readonly List<(double Start, double End, int FirstRow, int LastRow)> lassoRects = [];
    private int selectionFirstRow = -1, selectionLastRow = -1;
    private Point selectionPointer;
    private Guid? draggedLane;
    private int reorderTarget = -1;
    private bool reorderMoved;
    private int dragMode; // 1 move, 2 left trim, 3 right trim, 4 ruler, 5 range, 6 reorder, 7 gain
    private Point down;
    private string? dragBefore;
    private double originalStart, originalOffset, originalDuration, originalPointerTime;
    private readonly CheckBox overlapModeBox;
    private static double lastRepeatSelectionMultiplier = 4.0;
    private readonly Dictionary<Guid, double> dragOriginalStarts = [];
    private bool dragOverActive;
    private int dragOverRow;
    private double dragOverTime;
    private double dragOverDuration;
    private double rulerOriginalLoopStart, rulerOriginalLoopEnd;
    private int resizeOriginalLaneHeight;
    private const int Header = 190, Ruler = 30;
    private int LaneHeight = 100;
    private double preciseLaneHeight = 100;
    private bool laneHeightCustomized;
    private bool automaticSizing;
    private int previousClientHeight;
    private double ViewStart => scroll.Value / 100.0;
    private double XTime(int x) => ViewStart + (x - Header) / pixelsPerSecond;
    private int TimeX(double time) => Header + (int)Math.Round((time - ViewStart) * pixelsPerSecond);
    private int RowAt(int y) => y < Ruler ? -1 : (y - Ruler) / LaneHeight + vertical.Value;
    private MixClip? Clip => Arrangement.Lanes.SelectMany(l => l.Clips).FirstOrDefault(c => c.Id == selectedClip);
    private MixLane? Lane => Arrangement.Lanes.FirstOrDefault(l => l.Id == selectedLane);

    public MixTrackView()
    {
        this.windowGroup = new MixWindowGroup(this);
        this.InitializeLiveMixer();
        Name = "MixTrackView"; Arrangement.Name = "Mix " + (open.Count + 1); Text = Arrangement.Name;
        ClientSize = new Size(1100, 360); MinimumSize = new Size(700, 260); StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = true;
        MaximumSize = Size.Empty;
        KeyPreview = true; AllowDrop = true;
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        toolbar.Controls.Add(play); play.Click += async (_, _) => await TogglePlayAsync();
        Button Tool(string name, Action action)
        {
            var b = new Button { Text = name, AutoSize = true }; b.Click += (_, _) => action(); toolbar.Controls.Add(b);
            this.interactionHints.SetToolTip(b, name switch
            {
                "Stop" => "Stop the mix transport and return its caret to the beginning. Source samples and clip positions are unchanged.",
                "Render ..." => "Render the audible arrangement. Right-click for additional render scopes, including individual tracks. Muting, clip gain and effects are included in audio rendering.",
                "Save project…" => "Save the non-destructive arrangement, sources, clip edits and mixer settings as a project. This is separate from rendering a final audio file.",
                "Open project…" => "Open a saved mix project. Source audio and arrangement settings are restored together; use Render for a finished audio object.",
                "View Settings…" => "Configure timeline appearance and display options. Display changes do not alter source audio.",
                "+ Track" => "Add an empty arrangement lane. Drop audio into it, or double-click its empty timeline area to choose an open source.",
                "Undo" => "Undo the most recent arrangement edit. Display-only navigation does not rewrite audio.",
                "Redo" => "Reapply an undone arrangement edit. A new edit starts a new history branch.",
                _ => "Change timeline magnification without changing clip timing or source audio."
            });
            return b;
        }
        Tool("Stop", () => { operation?.Cancel(); StopPlayback(); caret = 0; canvas.Invalidate(); });
        toolbar.Controls.Add(new Label { Text = "BPM", AutoSize = true, Margin = new Padding(3, 8, 3, 3) }); toolbar.Controls.Add(bpm);
        division.Items.AddRange(new object[] { "1/1", "1/2", "1/4", "1/8", "1/16", "1/32", "1/64" }); division.SelectedIndex = 3;
        toolbar.Controls.Add(division); toolbar.Controls.Add(snap); toolbar.Controls.Add(loop); toolbar.Controls.Add(autoScroll);
        autoScroll.Click += (_, _) =>
        {
            Arrangement.Visual.ScrollMode = (MixScrollMode)(((int)Arrangement.Visual.ScrollMode + 1) % 3);
            UpdateScrollModeControl(); canvas.Invalidate(); Changed();
        };
        this.overlapModeBox = new CheckBox { Text = "Dock (no overlap)", AutoSize = true, ThreeState = true, AutoCheck = false, CheckState = CheckState.Unchecked };
        toolbar.Controls.Add(this.overlapModeBox);
        this.overlapModeBox.Click += (_, _) =>
        {
            this.overlapModeBox.CheckState = this.overlapModeBox.CheckState switch
            {
                CheckState.Unchecked => CheckState.Checked,
                CheckState.Checked => CheckState.Indeterminate,
                _ => CheckState.Unchecked
            };
            Arrangement.OverlapMode = this.overlapModeBox.CheckState switch
            {
                CheckState.Unchecked => MixOverlapMode.DockNoOverlap,
                CheckState.Checked => MixOverlapMode.AllowOverlapOverwrite,
                _ => MixOverlapMode.MixOverlap
            };
            this.overlapModeBox.Text = Arrangement.OverlapMode switch
            {
                MixOverlapMode.DockNoOverlap => "Dock (no overlap)",
                MixOverlapMode.AllowOverlapOverwrite => "Allow overlap (overwrite)",
                _ => "Mix overlap"
            };
            Changed();
        };
        // Initialize from arrangement.
        this.overlapModeBox.CheckState = Arrangement.OverlapMode switch
        {
            MixOverlapMode.DockNoOverlap => CheckState.Unchecked,
            MixOverlapMode.AllowOverlapOverwrite => CheckState.Checked,
            _ => CheckState.Indeterminate
        };
        this.overlapModeBox.Text = Arrangement.OverlapMode switch
        {
            MixOverlapMode.DockNoOverlap => "Dock (no overlap)",
            MixOverlapMode.AllowOverlapOverwrite => "Allow overlap (overwrite)",
            _ => "Mix overlap"
        };
        Tool("− Zoom", () => Zoom(0.8)); Tool("+ Zoom", () => Zoom(1.25));
        Tool("View Settings…", ShowViewSettings);
        Tool("+ Track", AddEmptyTrack);
        Tool("Undo", Undo); Tool("Redo", Redo);
        Tool("Save project…", async () => await SaveProjectAsync());
        Tool("Open project…", async () => await OpenProjectAsync());
        var renderButton = Tool("Render ...", async () => await ExportAsync());
        renderButton.MouseUp += (_, e) => { if (e.Button == MouseButtons.Right) ShowRenderMenu(renderButton, e.Location); };
        toolbar.Controls.Add(autoSave);
        autoSave.CheckedChanged += (_, _) => { if (!restoring) { Arrangement.AutoSave = autoSave.Checked; Changed(); QueueProjectSave(force: true); } };
        Controls.Add(canvas); Controls.Add(vertical); Controls.Add(scroll); Controls.Add(status); Controls.Add(toolbar);
        this.interactionHints.SetToolTip(canvas, "Mix timeline: drop audio to create clips; drag clips to move them and their edges to trim. Drag the ruler to set a loop. Shift enables temporary snapping; Ctrl+wheel zooms. Right-click a clip or empty space for context-specific actions.");
        this.interactionHints.SetToolTip(play, "Play or pause the mix on its shared transport. All lanes follow one clock; non-destructive edits leave source audio intact.");
        this.interactionHints.SetToolTip(bpm, "Mix tempo. Changes are applied after the tempo edit debounce and use the arrangement's tempo handling; original source audio remains available.");
        this.interactionHints.SetToolTip(snap, "Snap clip edits to the selected musical grid. Shift temporarily enables snapping during a gesture.");
        this.interactionHints.SetToolTip(division, "Musical grid division for snap operations. A finer division permits smaller timing changes without altering the source recording.");
        this.interactionHints.SetToolTip(loop, "Repeat the ruler's selected time range during mix playback. Drag the ruler to change the loop boundaries.");
        this.interactionHints.SetToolTip(autoScroll, "Cycle the timeline's playback-follow modes. This changes scrolling behavior, not audio timing or clip positions.");
        bpm.ValueChanged += (_, _) =>
        {
            if (restoring) return;
            tempoDebounce.Stop();
            tempoDebounce.Start();
        };
        tempoDebounce.Tick += async (_, _) =>
        {
            tempoDebounce.Stop();
            await RequestMixTempoChangeAsync((double)bpm.Value);
        };
        division.SelectedIndexChanged += (_, _) => { if (!restoring) { Arrangement.SnapDivision = 1 << division.SelectedIndex; Changed(); } };
        snap.CheckedChanged += (_, _) => { if (!restoring) { Arrangement.Snap = snap.Checked; Changed(); } };
        loop.CheckedChanged += (_, _) => { if (!restoring) { Arrangement.Loop = loop.Checked; Changed(); } };
        scroll.ValueChanged += (_, _) => { canvas.Invalidate(); Arrangement.Visual.HorizontalScrollSeconds = ViewStart; };
        vertical.ValueChanged += (_, _) => { canvas.Invalidate(); Arrangement.Visual.FirstVisibleTrack = vertical.Value; };
        projectSaveTimer.Tick += async (_, _) => { projectSaveTimer.Stop(); await FlushProjectSaveAsync(); };
        pasteHoldTimer.Tick += (_, _) => ShowClipboardHistory(); KeyUp += PasteKeyUp;
        Deactivate += (_, _) => { pasteHoldTimer.Stop(); pasteHoldTracking = false; pasteKeyDown = false; };
        canvas.Paint += PaintTimeline; canvas.MouseDown += MouseDownTimeline; canvas.MouseMove += MouseMoveTimeline; canvas.MouseUp += MouseUpTimeline;
        canvas.KeyDown += (_, e) =>
        {
            if (dragMode != 5 || !(e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.LControlKey || e.KeyCode == Keys.RControlKey)) return;
            bool ctrlNow = (ModifierKeys & Keys.Control) != 0;
            if (ctrlNow == selectionAdditive) return;
            if (ctrlNow) { additiveSelection.Clear(); additiveSelection.UnionWith(selectedClips); if (lassoRects.Count == 0) lassoRects.Add((selectionStart, selectionEnd, selectionFirstRow, selectionLastRow)); }
            else { lassoRects.Clear(); additiveSelection.Clear(); }
            selectionAdditive = ctrlNow; selectingWhole = ctrlNow;
            UpdateRangeSelection(lastPointer); e.Handled = true;
        };
        canvas.KeyUp += (_, e) =>
        {
            if (dragMode != 5 || !(e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.LControlKey || e.KeyCode == Keys.RControlKey)) return;
            bool ctrlNow = (ModifierKeys & Keys.Control) != 0;
            if (ctrlNow == selectionAdditive) return;
            if (ctrlNow) { additiveSelection.Clear(); additiveSelection.UnionWith(selectedClips); if (lassoRects.Count == 0) lassoRects.Add((selectionStart, selectionEnd, selectionFirstRow, selectionLastRow)); }
            else { lassoRects.Clear(); additiveSelection.Clear(); }
            selectionAdditive = ctrlNow; selectingWhole = ctrlNow;
            UpdateRangeSelection(lastPointer); e.Handled = true;
        };
        canvas.MouseCaptureChanged += (_, _) =>
        {
            if (!canvas.Capture && dragMode == 7 && !closing && !released)
            { dragMode = 0; dragBefore = null; Changed(); }
        };
        canvas.MouseDoubleClick += (_, e) =>
        {
            if (busy || e.Button != MouseButtons.Left || e.X < Header) return;
            int row = RowAt(e.Y);
            if (row < 0 || row >= Arrangement.Lanes.Count) return;
            var lane = Arrangement.Lanes[row];
            if (lane.Clips.Any(c => ClipRect(row, c).Contains(e.Location))) return;
            double time = Arrangement.SnapTime(XTime(e.X), ModifierKeys.HasFlag(Keys.Shift));
            ShowSourcePicker(lane, time, e.Location);
        };
        canvas.MouseWheel += (_, e) => { if ((ModifierKeys & Keys.Control) != 0) Zoom(e.Delta > 0 ? 1.15 : 1 / 1.15); else vertical.Value = Math.Clamp(vertical.Value - Math.Sign(e.Delta), 0, Math.Max(0, Arrangement.Lanes.Count - 1)); };
        foreach (Control target in new Control[] { this, canvas })
        {
            target.DragEnter += (_, e) =>
            {
                if (Dragged(e.Data).Length == 0) { e.Effect = DragDropEffects.None; return; }
                e.Effect = DragDropEffects.Copy;
                var p = canvas.PointToClient(new Point(e.X, e.Y));
                var audios = Dragged(e.Data);
                dragOverActive = true; dragOverRow = Arrangement.Lanes.Count > 0 ? Math.Clamp(RowAt(p.Y), 0, Arrangement.Lanes.Count - 1) : 0;
                dragOverTime = Arrangement.SnapTime(XTime(p.X), (e.KeyState & 4) != 0);
                dragOverDuration = audios.Length > 0 ? Math.Max(0.05, audios[0].Duration.TotalSeconds) : 0.5;
                canvas.Invalidate();
            };
            target.DragOver += (_, e) =>
            {
                if (Dragged(e.Data).Length == 0) { e.Effect = DragDropEffects.None; dragOverActive = false; canvas.Invalidate(); return; }
                e.Effect = DragDropEffects.Copy;
                var p = canvas.PointToClient(new Point(e.X, e.Y));
                var audios2 = Dragged(e.Data);
                dragOverRow = Arrangement.Lanes.Count > 0 ? Math.Clamp(RowAt(p.Y), 0, Arrangement.Lanes.Count - 1) : 0;
                dragOverTime = Arrangement.SnapTime(XTime(p.X), (e.KeyState & 4) != 0);
                dragOverDuration = audios2.Length > 0 ? Math.Max(0.05, audios2[0].Duration.TotalSeconds) : dragOverDuration;
                dragOverActive = true;
                canvas.Invalidate();
            };
            target.DragLeave += (_, _) => { dragOverActive = false; canvas.Invalidate(); };
            target.DragDrop += DropAudio;
        }
        clock.Tick += (_, _) => { if (playback != null) { caret = playback.CurrentTime.TotalSeconds; FollowPlaybackCaret(); canvas.Invalidate(); if (!playback.Playing && !busy) StopPlayback(); } };
        hueTimer.Tick += (_, _) =>
        {
            double elapsed = hueClock.Elapsed.TotalSeconds;
            hueClock.Restart();
            if (!Arrangement.Visual.HueCycle || !Visible) return;
            animatedHue = ((animatedHue + elapsed * Arrangement.Visual.HueSpeed) % 360 + 360) % 360;
            canvas.Invalidate();
        };
        hueTimer.Start();
        FormClosing += CloseMix;
        FormClosed += (_, _) => ReleaseResources();
        committed = MixAudioHistory.Capture(Arrangement); committedAudioKey = MixAudioHistory.Key(Arrangement);
        committedProject = Arrangement.CaptureProject(); open.Add(this);
        previousClientHeight = ClientSize.Height;
        ResizeBegin += (_, _) => { resizeOriginalLaneHeight = LaneHeight; };
        ResizeEnd += (_, _) =>
        {
            if (resizeOriginalLaneHeight != LaneHeight)
            {
                laneHeightCustomized = true;
                canvas.Invalidate();
            }
        };
        SizeChanged += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized) return;
            if (!automaticSizing && previousClientHeight > 0 && Arrangement.Lanes.Count > 0)
            {
                if (Arrangement.Visual.AutoTrackHeight)
                {
                    ApplyAutoTrackHeight();
                }
                else
                {
                    laneHeightCustomized = true;
                    preciseLaneHeight = Math.Clamp(preciseLaneHeight + (ClientSize.Height - previousClientHeight) / (double)Arrangement.Lanes.Count, 32, 600);
                    LaneHeight = (int)Math.Round(preciseLaneHeight);
                }
            }
            previousClientHeight = ClientSize.Height;
            canvas.Invalidate();
        };
    }
    private static AudioObj[] Dragged(IDataObject? data) => data?.GetData(typeof(AudioObj[])) as AudioObj[]
        ?? (data?.GetData(typeof(IEnumerable<AudioObj>)) as IEnumerable<AudioObj>)?.ToArray() ?? [];
    public void Join(TrackView view)
    {
        _ = JoinAsync(view);
    }
    private async Task JoinAsync(TrackView view)
    {
        if (busy || closing || released || this.windowGroup.IsBlocked) { view.SetJoinedMix(view.JoinedMix); return; }
        var existing = joined.FirstOrDefault(p => ReferenceEquals(p.Value, view) && Arrangement.Lanes.Any(l => l.Id == p.Key));
        if (existing.Value != null) { Show(); Activate(); return; }
        float sourceBpm = view.OriginalAudio.Bpm > 0 ? view.OriginalAudio.Bpm : view.OriginalAudio.ScannedBpm;
        bool firstTrack = !Arrangement.Lanes.Any(l => l.Clips.Count > 0);
        if (firstTrack) InitializeTempoFromFirstTrack(sourceBpm);
        ClipTempoMode? tempo = ClipTempoMode.Original;
        if (!firstTrack && float.IsFinite(sourceBpm) && sourceBpm > 0 && Math.Abs(sourceBpm - Arrangement.Bpm) > 0.1)
            tempo = await ChooseTempoAsync(sourceBpm);
        if (tempo == null) { view.SetJoinedMix(view.JoinedMix); return; }
        PlaylistStretchSettings? stretchSettings = null;
        if (tempo == ClipTempoMode.TimeStretch)
        {
            stretchSettings = await ConfigureImportStretchAsync(view.OriginalAudio);
            if (stretchSettings == null) { view.SetJoinedMix(view.JoinedMix); return; }
        }
        MixSource source;
        if (tempo.Value == ClipTempoMode.Original)
            source = new MixSource { Name = view.OriginalAudio.Name, Data = (float[])view.OriginalAudio.Data.Clone(),
                SampleRate = view.OriginalAudio.SampleRate, Channels = view.OriginalAudio.Channels, Bpm = sourceBpm };
        else
        {
            using var snapshot = view.OriginalAudio.Clone();
            var input = new MixSource { Name = snapshot.Name, Data = snapshot.Data, SampleRate = snapshot.SampleRate,
                Channels = snapshot.Channels, Bpm = sourceBpm };
            Arrangement.Sources.Add(input.Id, input);
            MixSource? prepared = null;
            Show(); status.Text = $"Importing: {tempo.Value} to {Arrangement.Bpm:0.##} BPM…";
            await RunAsync(async token => prepared = await MixTempoOrigins.PrepareAsync(Arrangement, input, stretchSettings?.TargetBpm ?? Arrangement.Bpm,
                Arrangement.Bpm, tempo.Value, stretchSettings, token));
            if (prepared == null || closing || released || view.IsDisposed) { Arrangement.Sources.Remove(input.Id); if (!view.IsDisposed) view.SetJoinedMix(view.JoinedMix); return; }
            source = prepared;
        }
        this.AttachJoinedTrack(view, source);
        this.FinishJoiningTracks();
    }
    private void AttachJoinedTrack(TrackView view, MixSource source)
    {
        view.LeaveCurrentMix();
        foreach (var old in joined.Where(p => ReferenceEquals(p.Value, view)).Select(p => p.Key).ToArray()) joined.Remove(old);
        Arrangement.Sources.TryAdd(source.Id, source);
        var lane = new MixLane { Name = source.Name, Mute = view.Muted, Solo = view.Soloed };
        Arrangement.Lanes.Add(lane); Arrangement.AddClip(lane, source, 0); joined[lane.Id] = view;
        this.RememberJoinedOrigin(lane.Id, view);
        view.SetJoinedMix(this); view.Hide(); view.Settings.Hide();
        _ = view.OriginalAudio.StopAsync();
    }
    private void FinishJoiningTracks()
    {
        Changed();
        automaticSizing = true;
        if (!laneHeightCustomized)
            Height = Math.Min(Screen.FromControl(this).WorkingArea.Height - 40, 180 + Arrangement.Lanes.Count * LaneHeight);
        automaticSizing = false;
        Show(); Activate();
    }
    private void InitializeTempoFromFirstTrack(float sourceBpm)
    {
        if (Arrangement.Lanes.Any(l => l.Clips.Count > 0) || !float.IsFinite(sourceBpm) || sourceBpm is < 10 or > 400) return;
        Arrangement.Bpm = sourceBpm;
        restoring = true;
        try { bpm.Value = (decimal)sourceBpm; }
        finally { restoring = false; }
    }
    private async Task<PlaylistStretchSettings?> ConfigureImportStretchAsync(AudioObj audio, bool matchMixTempo = false)
    {
        using var snapshot = audio.Clone();
        float sourceBpm = audio.Bpm > 0 ? audio.Bpm : audio.ScannedBpm;
        using var dialog = new TimeStretchDialog(null, [snapshot]) { IsConfigureMode = true };
        if (matchMixTempo) { dialog.LimitConfigurationTarget((decimal)Arrangement.Bpm, (decimal)Arrangement.Bpm); }
        dialog.SetConfigurationTempo(sourceBpm, (float)Arrangement.Bpm);
        return await this.ShowMixDialogAsync(dialog) == DialogResult.OK ? dialog.ConfirmedSettings : null;
    }
    private async Task<ClipTempoMode?> ChooseTempoAsync(float sourceBpm)
    {
        using var dialog = new Form { Text = "Join track — tempo matching", ClientSize = new Size(540, 145),
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterParent };
        var text = new Label { Dock = DockStyle.Top, Height = 65, Padding = new Padding(12),
            Text = $"Track: {sourceBpm:0.##} BPM    Mix grid: {Arrangement.Bpm:0.##} BPM\nHow should this track be added? Original audio is preserved." };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 65, Padding = new Padding(8) };
        ClipTempoMode? result = null;
        foreach (var choice in new[] { ("Time-stretch", ClipTempoMode.TimeStretch), ("Varispeed", ClipTempoMode.Varispeed), ("Keep original tempo", ClipTempoMode.Original) })
        {
            var button = new Button { Text = choice.Item1, AutoSize = true, Height = 30 };
            button.Click += (_, _) => { result = choice.Item2; dialog.DialogResult = DialogResult.OK; dialog.Close(); }; buttons.Controls.Add(button);
        }
        dialog.Controls.Add(text); dialog.Controls.Add(buttons); await this.ShowMixDialogAsync(dialog); return result;
    }
    public void Detach(TrackView view)
    {
        var pair = joined.FirstOrDefault(p => ReferenceEquals(p.Value, view));
        if (pair.Value == null) return;
        Arrangement.Lanes.RemoveAll(l => l.Id == pair.Key); Changed();
    }
    private void ReconcileJoined()
    {
        foreach (var pair in joined.ToArray())
        {
            if (pair.Value.IsDisposed || pair.Value.Disposing) continue;
            if (Arrangement.Lanes.Any(l => l.Id == pair.Key))
            {
                if (pair.Value.JoinedMix != null && !ReferenceEquals(pair.Value.JoinedMix, this))
                { Arrangement.Lanes.RemoveAll(l => l.Id == pair.Key); continue; }
                pair.Value.SetJoinedMix(this); pair.Value.Hide();
            }
            else if (ReferenceEquals(pair.Value.JoinedMix, this)) { pair.Value.SetJoinedMix(null); pair.Value.Show(); }
        }
    }
    private void ApplyAutoTrackHeight()
    {
        if (!Arrangement.Visual.AutoTrackHeight) return;
        int count = Math.Max(1, Arrangement.Lanes.Count);
        int available = canvas.ClientSize.Height - Ruler - 4;
        if (available < count * 32) available = count * 32;
        LaneHeight = Math.Clamp(available / count, 32, 600);
        preciseLaneHeight = LaneHeight;
        vertical.Value = 0;
        vertical.Maximum = Math.Max(0, Arrangement.Lanes.Count - 1);
    }
    private async void RepeatSelection()
    {
        if (busy || closing) return;
        double value = lastRepeatSelectionMultiplier;
        using var form = new Form { Text = "Repeat selection", ClientSize = new Size(300, 100), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
        var label = new Label { Text = "Repeat count (N):", Location = new Point(12, 14), AutoSize = true };
        var num = new NumericUpDown { Location = new Point(140, 12), Width = 120, Minimum = 0.25m, Maximum = 64m, DecimalPlaces = 2, Increment = 0.25m, Value = (decimal)value };
        var ok = new Button { Text = "OK", Location = new Point(140, 56), Size = new Size(60, 26), DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Location = new Point(210, 56), Size = new Size(60, 26), DialogResult = DialogResult.Cancel };
        form.Controls.AddRange(new Control[] { label, num, ok, cancel });
        form.AcceptButton = null; form.CancelButton = cancel;
        if (form.ShowDialog(this) != DialogResult.OK) return;
        double n = (double)num.Value;
        lastRepeatSelectionMultiplier = n;

        // Collect target clips (original clips, possibly trimmed to the active selection).
        var targets = new List<(int Row, MixClip Clip)>();
        if (selectedClips.Count > 0)
        {
            for (int row = 0; row < Arrangement.Lanes.Count; row++)
                foreach (var clip in Arrangement.Lanes[row].Clips)
                {
                    if (!selectedClips.Contains(clip.Id)) continue;
                    var copy = System.Text.Json.JsonSerializer.Deserialize<MixClip>(System.Text.Json.JsonSerializer.Serialize(clip))!;
                    if (!fullSelectedClips.Contains(clip.Id) && selectionFirstRow >= 0 && selectionEnd > selectionStart)
                    {
                        double start = Math.Max(clip.Start, selectionStart), end = Math.Min(clip.Start + Arrangement.ClipDuration(clip), selectionEnd);
                        if (end <= start) continue;
                        copy.SourceOffset += (start - clip.Start) * Arrangement.Rate(clip);
                        copy.SourceDuration = (end - start) * Arrangement.Rate(clip); copy.Start = start;
                        if (start > clip.Start) copy.FadeIn = 0;
                        if (end < clip.Start + Arrangement.ClipDuration(clip)) copy.FadeOut = 0;
                    }
                    targets.Add((row, copy));
                }
        }
        else if (Clip is { } c)
        {
            for (int row = 0; row < Arrangement.Lanes.Count; row++)
                if (Arrangement.Lanes[row].Clips.Contains(c))
                { targets.Add((row, System.Text.Json.JsonSerializer.Deserialize<MixClip>(System.Text.Json.JsonSerializer.Serialize(c))!)); break; }
        }
        if (targets.Count == 0) return;

        foreach (var (row, clip) in targets)
        {
            if (row < 0 || row >= Arrangement.Lanes.Count) continue;
            var lane = Arrangement.Lanes[row];
            double clipDur = Arrangement.ClipDuration(clip);
            if (clipDur <= 0) continue;
            int fullCopies = (int)Math.Floor(n);
            double remainder = n - fullCopies;
            for (int i = 1; i < fullCopies; i++)
            {
                var copy = System.Text.Json.JsonSerializer.Deserialize<MixClip>(System.Text.Json.JsonSerializer.Serialize(clip))!;
                copy.Id = Guid.NewGuid();
                copy.Start = clip.Start + i * clipDur;
                lane.Clips.Add(copy);
            }
            if (remainder > 0.001)
            {
                var copy = System.Text.Json.JsonSerializer.Deserialize<MixClip>(System.Text.Json.JsonSerializer.Serialize(clip))!;
                copy.Id = Guid.NewGuid();
                copy.Start = clip.Start + fullCopies * clipDur;
                copy.SourceDuration = clip.SourceDuration * remainder;
                lane.Clips.Add(copy);
            }
        }
        Changed();
    }
    private void Changed()
    {
        if (restoring) return;
        string state = MixAudioHistory.Capture(Arrangement), key = MixAudioHistory.Key(Arrangement);
        if (key != committedAudioKey) { undo.Push(MixAudioHistory.Create(committed, state)); redo.Clear(); }
        committed = state; committedAudioKey = key;
        string project = Arrangement.CaptureProject();
        if (project != committedProject) { committedProject = project; dirty = true; Text = Arrangement.Name; QueueProjectSave(); }
        foreach (var clip in Arrangement.Lanes.SelectMany(l => l.Clips))
            if (clip.TempoMode != ClipTempoMode.Original && Arrangement.Sources.TryGetValue(clip.SourceId, out var source) && source.Bpm <= 0)
                status.Text = "A tempo-matched clip has no source BPM. Right-click it → Set source BPM.";
        ReconcileJoined();
        vertical.Maximum = Math.Max(0, Arrangement.Lanes.Count - 1); vertical.Value = Math.Min(vertical.Value, vertical.Maximum);
        this.UpdateTimelineScrollBounds();
        canvas.Invalidate();
        this.UpdateTransportLoop();
        SchedulePreparation();
        EnsureTrackLayout();
    }
    private void Restore(string state)
    {
        tempoDebounce.Stop();
        ClearRangeSelection();
        Arrangement.Restore(state); committed = MixAudioHistory.Capture(Arrangement); committedAudioKey = MixAudioHistory.Key(Arrangement);
        committedProject = Arrangement.CaptureProject(); restoring = true;
        bpm.Value = (decimal)Arrangement.Bpm; division.SelectedIndex = (int)Math.Log2(Arrangement.SnapDivision);
        snap.Checked = Arrangement.Snap; loop.Checked = Arrangement.Loop; restoring = false; dirty = true;
        LaneHeight = Arrangement.DisplayLaneHeight; preciseLaneHeight = LaneHeight;
        if (Arrangement.Visual.AutoTrackHeight) ApplyAutoTrackHeight();
        pixelsPerSecond = Arrangement.Visual.PixelsPerSecond;
        this.overlapModeBox.CheckState = Arrangement.OverlapMode switch
        {
            MixOverlapMode.DockNoOverlap => CheckState.Unchecked,
            MixOverlapMode.AllowOverlapOverwrite => CheckState.Checked,
            _ => CheckState.Indeterminate
        };
        this.overlapModeBox.Text = Arrangement.OverlapMode switch
        {
            MixOverlapMode.DockNoOverlap => "Dock (no overlap)",
            MixOverlapMode.AllowOverlapOverwrite => "Allow overlap (overwrite)",
            _ => "Mix overlap"
        };
        UpdateScrollModeControl();
        restoring = true;
        try { autoSave.Checked = Arrangement.AutoSave; RestoreViewport(); }
        finally { restoring = false; }
        ReconcileJoined(); Text = Arrangement.Name; canvas.Invalidate(); SchedulePreparation(); EnsureTrackLayout(); QueueProjectSave(force: true);
    }
    private void Undo() { if (undo.Count > 0) { string edit = undo.Pop(); MixAudioHistory.Apply(Arrangement, edit, undo: true); redo.Push(edit); this.RefreshAfterHistory(); } }
    private void Redo() { if (redo.Count > 0) { string edit = redo.Pop(); MixAudioHistory.Apply(Arrangement, edit, undo: false); undo.Push(edit); this.RefreshAfterHistory(); } }
    private void Zoom(double factor)
    {
        pixelsPerSecond = Math.Clamp(pixelsPerSecond * factor, 8, 2000);
        canvas.Invalidate();
    }
    private Color VisualColor(string value) => ThemeFiles.ParseColor(value);
    private Color ClipColor(string value, int row)
    {
        Color color = VisualColor(value);
        double hue = (color.GetHue() + Arrangement.Visual.Hue + (Arrangement.Visual.HueCycle ? animatedHue : 0)
            + (Arrangement.Visual.ColorTracks ? row * 37 : 0)) % 360;
        double saturation = color.GetSaturation(), lightness = color.GetBrightness();
        double chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        double x = chroma * (1 - Math.Abs(hue / 60 % 2 - 1)), m = lightness - chroma / 2;
        (double r, double g, double b) = hue switch
        {
            < 60 => (chroma, x, 0d), < 120 => (x, chroma, 0d), < 180 => (0d, chroma, x),
            < 240 => (0d, x, chroma), < 300 => (x, 0d, chroma), _ => (chroma, 0d, x)
        };
        return Color.FromArgb((int)Math.Round((r + m) * 255), (int)Math.Round((g + m) * 255), (int)Math.Round((b + m) * 255));
    }
    private async void ShowViewSettings()
    {
        if (busy || this.windowGroup.IsBlocked) return;
        tempoDebounce.Stop();
        string before = Arrangement.Capture();
        using var dialog = new MixOptionsDialog(Arrangement, PreviewViewOptions, this.PickMixColorAsync);
        DialogResult answer = await this.ShowMixDialogAsync(dialog);
        if (closing || released) return;
        if (answer == DialogResult.OK)
        {
            double oldBpm = System.Text.Json.JsonSerializer.Deserialize<MixArrangement>(before)!.Bpm;
            double targetBpm = dialog.Options.Bpm;
            PreviewViewOptions(dialog.Options);
            Arrangement.Bpm = oldBpm;
            await RequestMixTempoChangeAsync(targetBpm);
            Arrangement.Validate();
            laneHeightCustomized = true;
            Changed();
        }
        else
        {
            Arrangement.Restore(before);
            PreviewViewOptions(Arrangement);
            canvas.Invalidate();
        }
    }
    private void PreviewViewOptions(MixArrangement options)
    {
        Arrangement.Visual = System.Text.Json.JsonSerializer.Deserialize<MixVisualOptions>(System.Text.Json.JsonSerializer.Serialize(options.Visual))!;
        Arrangement.DisplayLaneHeight = options.DisplayLaneHeight;
        Arrangement.Bpm = options.Bpm; Arrangement.SnapDivision = options.SnapDivision; Arrangement.Snap = options.Snap;
        LaneHeight = options.DisplayLaneHeight; preciseLaneHeight = LaneHeight; if (options.Visual.AutoTrackHeight) ApplyAutoTrackHeight();
        pixelsPerSecond = options.Visual.PixelsPerSecond;
        restoring = true;
        try { bpm.Value = (decimal)Arrangement.Bpm; division.SelectedIndex = (int)Math.Log2(Arrangement.SnapDivision); snap.Checked = Arrangement.Snap; }
        finally { restoring = false; }
        canvas.Invalidate();
    }
    private void UpdateScrollModeControl()
    {
        autoScroll.CheckState = Arrangement.Visual.ScrollMode switch
        {
            MixScrollMode.Follow => CheckState.Checked, MixScrollMode.Page => CheckState.Indeterminate, _ => CheckState.Unchecked
        };
        autoScroll.Text = Arrangement.Visual.ScrollMode switch
        { MixScrollMode.Follow => "Auto-Scroll", MixScrollMode.Page => "Auto-Skip", _ => "No Scroll" };
    }
    private void FollowPlaybackCaret()
    {
        if (Arrangement.Visual.ScrollMode == MixScrollMode.None || dragMode != 0) return;
        double widthSeconds = Math.Max(1, canvas.ClientSize.Width - Header - 8) / pixelsPerSecond;
        double target;
        if (Arrangement.Visual.ScrollMode == MixScrollMode.Follow)
            target = Math.Max(0, caret - widthSeconds * 0.25);
        else
        {
            // Stay on the current page until the caret reaches an edge. On loop wrap,
            // select the page containing the new (earlier) position as well.
            if (caret >= ViewStart && caret < ViewStart + widthSeconds) return;
            target = Math.Floor(Math.Max(0, caret) / widthSeconds) * widthSeconds;
        }
        int value = (int)Math.Min(int.MaxValue - 100, Math.Round(target * 100));
        scroll.Maximum = Math.Max(scroll.Maximum, value + scroll.LargeChange);
        scroll.Value = Math.Clamp(value, scroll.Minimum, scroll.Maximum - scroll.LargeChange + 1);
    }
    private void PaintTimeline(object? sender, PaintEventArgs e)
    {
        var visual = Arrangement.Visual;
        var g = e.Graphics; g.Clear(VisualColor(visual.Background)); g.SmoothingMode = SmoothingMode.None;
        double step = 240.0 / Arrangement.Bpm / Arrangement.SnapDivision;
        if (step * pixelsPerSecond < 5) step *= Math.Ceiling(5 / (step * pixelsPerSecond));
        using var gridPen = new Pen(VisualColor(visual.Grid));
        for (double t = Math.Floor(ViewStart / step) * step; TimeX(t) < canvas.Width; t += step)
        {
            int x = TimeX(t); if (x < Header) continue;
            if (visual.ShowGrid) g.DrawLine(gridPen, x, 0, x, canvas.Height);
            double bar = t / (240.0 / Arrangement.Bpm);
            if (Math.Abs(bar - Math.Round(bar)) < 0.0001) TextRenderer.DrawText(g, (1 + (int)Math.Round(bar)).ToString(), Font, new Point(x + 2, 4), VisualColor(visual.Text));
        }
        if (Arrangement.LoopEnd > Arrangement.LoopStart)
        { using var brush = new SolidBrush(Color.FromArgb(80, VisualColor(visual.LoopRange))); g.FillRectangle(brush, TimeX(Arrangement.LoopStart), 0, TimeX(Arrangement.LoopEnd) - TimeX(Arrangement.LoopStart), Ruler); }
        for (int row = vertical.Value; row < Arrangement.Lanes.Count; row++)
        {
            int y = Ruler + (row - vertical.Value) * LaneHeight; if (y > canvas.Height) break;
            var lane = Arrangement.Lanes[row];
            using var headerBrush = new SolidBrush(VisualColor(lane.Id == selectedLane ? visual.SelectedHeader : visual.TrackHeader));
            g.FillRectangle(headerBrush, 0, y, Header, LaneHeight);
            TextRenderer.DrawText(g, $"{row + 1}. {lane.Name}\n{lane.Gain:P0}  {(lane.Solo ? "SOLO " : "")}{(lane.Mute ? "MUTE" : "")}", Font,
                new Rectangle(5, y + 3, Header - 8, Math.Max(1, LaneHeight - 29)), lane.Mute ? Color.Gray : VisualColor(visual.Text), TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
            DrawLaneButton(g, row, 0, "S", lane.Solo ? Color.LightGreen : Color.FromArgb(65, 69, 76));
            DrawLaneButton(g, row, 1, "M", lane.Mute ? Color.FromArgb(32, 32, 32) : Color.FromArgb(65, 69, 76));
            Rectangle volume = LaneVolumeRect(row);
            using (var trackPen = new Pen(Color.Gray, 3)) g.DrawLine(trackPen, volume.Left, volume.Top + volume.Height / 2, volume.Right, volume.Top + volume.Height / 2);
            int vx = volume.Left + (int)(Math.Clamp(lane.Gain, 0, 2) / 2 * volume.Width);
            g.FillRectangle(Brushes.LightGray, vx - 3, volume.Top + 2, 6, Math.Max(2, volume.Height - 4));
            foreach (var clip in lane.Clips)
            {
                Rectangle rect = ClipRect(row, clip); if (rect.Right < Header || rect.Left > canvas.Width) continue;
                var saved = g.Save(); g.SetClip(new Rectangle(Header, y, canvas.Width - Header, LaneHeight));
                bool selected = clip.Id == selectedClip || selectedClips.Contains(clip.Id);
                using var brush = new SolidBrush(ClipColor(selected ? visual.SelectedClip : visual.Clip, row)); g.FillRectangle(brush, rect);
                using var edge = new Pen(selected ? Color.White : Color.FromArgb(110, 150, 165)); g.DrawRectangle(edge, rect);
                using (var handleBrush = new SolidBrush(Color.FromArgb(65, Color.Black))) g.FillRectangle(handleBrush, rect.X, rect.Y, rect.Width, Math.Min(21, rect.Height));
                int labelX = Math.Max(Header + 4, rect.X + 4);
                int labelWidth = Math.Max(1, rect.Right - labelX);
                TextRenderer.DrawText(g, clip.Name, Font, new Rectangle(labelX, rect.Y + 2, labelWidth, 18), VisualColor(visual.Text), TextFormatFlags.EndEllipsis);
                if (visual.ShowWaveforms && Arrangement.Sources.TryGetValue(clip.SourceId, out var source)) DrawWave(g, rect, clip, source);
                g.Restore(saved);
            }
            g.DrawLine(Pens.DimGray, 0, y + LaneHeight - 1, canvas.Width, y + LaneHeight - 1);
        }
        // Ghost outline on the hovered target lane while dragging clips onto another lane.
        if (dragMode == 1 && Clip is { } dragged && Arrangement.Lanes.Count > 0)
        {
            int targetRow = Math.Clamp(RowAt(lastPointer.Y), 0, Arrangement.Lanes.Count - 1);
            var group = dragOriginalStarts.Count > 0
                ? Arrangement.Lanes.SelectMany(l => l.Clips).Where(c => dragOriginalStarts.ContainsKey(c.Id)).ToList()
                : new List<MixClip> { dragged };
            int draggedRow = Arrangement.Lanes.FindIndex(l => l.Clips.Contains(dragged));
            if (targetRow != draggedRow)
            {
                using var ghostBrush = new SolidBrush(Color.FromArgb(45, VisualColor(visual.Clip)));
                using var ghostPen = new Pen(Color.FromArgb(180, VisualColor(visual.SelectedClip))) { DashStyle = DashStyle.Dash };
                foreach (var gc in group)
                {
                    int x = TimeX(gc.Start);
                    int w = Math.Max(2, (int)Math.Round(Arrangement.ClipDuration(gc) * pixelsPerSecond));
                    int y = Ruler + (targetRow - vertical.Value) * LaneHeight + 3;
                    var rect = new Rectangle(x, y, w, LaneHeight - 7);
                    g.FillRectangle(ghostBrush, Rectangle.Intersect(rect, new Rectangle(Header, Ruler, Math.Max(0, canvas.Width - Header), Math.Max(0, canvas.Height - Ruler))));
                    g.DrawRectangle(ghostPen, rect);
                }
            }
        }
        // Ghost preview while dragging audio from a collection into the mix.
        if (dragOverActive)
        {
            int row = Arrangement.Lanes.Count > 0 ? Math.Clamp(dragOverRow, 0, Arrangement.Lanes.Count - 1) : 0;
            int y = Arrangement.Lanes.Count > 0 ? Ruler + (row - vertical.Value) * LaneHeight : Ruler + 3;
            using var ghostBrush = new SolidBrush(Color.FromArgb(45, VisualColor(visual.Clip)));
            using var ghostPen = new Pen(Color.FromArgb(180, VisualColor(visual.SelectedClip))) { DashStyle = DashStyle.Dash };
            int x = TimeX(dragOverTime);
            int w = Math.Max(8, (int)Math.Round((dragOverDuration > 0 ? dragOverDuration : 0.5) * pixelsPerSecond));
            var rect = new Rectangle(x, y + 3, w, LaneHeight - 7);
            var clipRect = Rectangle.Intersect(rect, new Rectangle(Header, Ruler, Math.Max(0, canvas.Width - Header), Math.Max(0, canvas.Height - Ruler)));
            g.FillRectangle(ghostBrush, clipRect);
            g.DrawRectangle(ghostPen, rect);
        }
        if (selectionFirstRow >= 0 && selectionEnd > selectionStart)
        {
            var rects = selectionAdditive && lassoRects.Count > 0 ? lassoRects : new List<(double, double, int, int)> { (selectionStart, selectionEnd, selectionFirstRow, selectionLastRow) };
            using var brush = new SolidBrush(Color.FromArgb(65, VisualColor(visual.LoopRange)));
            using var pen = new Pen(VisualColor(visual.Text)) { DashStyle = DashStyle.Dash };
            foreach (var (rs, re, rf, rl) in rects)
            {
                int y = Ruler + (Math.Max(0, rf) - vertical.Value) * LaneHeight;
                var selection = Rectangle.Intersect(new Rectangle(Header, Ruler, Math.Max(0, canvas.Width - Header), Math.Max(0, canvas.Height - Ruler)),
                    new Rectangle(TimeX(rs), y, Math.Max(1, TimeX(re) - TimeX(rs)), (Math.Min(Arrangement.Lanes.Count - 1, rl) - Math.Max(0, rf) + 1) * LaneHeight));
                g.FillRectangle(brush, selection);
                g.DrawRectangle(pen, selection);
            }
        }
        if (dragMode == 6 && reorderTarget >= 0)
        {
            int y = Ruler + (reorderTarget - vertical.Value) * LaneHeight;
            using var pen = new Pen(Color.LightGreen, 3); g.DrawLine(pen, 0, y, canvas.Width, y);
        }
        int cx = TimeX(caret); if (cx >= Header) { using var caretPen = new Pen(VisualColor(visual.Playhead)); g.DrawLine(caretPen, cx, 0, cx, canvas.Height); }
        PaintPastePreview(g);
    }
    private Rectangle LaneButtonRect(int row, int button) => new(5 + button * 25,
        Ruler + (row - vertical.Value + 1) * LaneHeight - 25, 21, 21);
    private Rectangle LaneVolumeRect(int row) => new(62, Ruler + (row - vertical.Value + 1) * LaneHeight - 25, Header - 74, 21);
    private void SetLaneGain(int row, int x)
    {
        if (row < 0 || row >= Arrangement.Lanes.Count) return;
        Rectangle bounds = LaneVolumeRect(row);
        Arrangement.Lanes[row].Gain = Math.Clamp((x - bounds.Left) / (double)bounds.Width * 2, 0, 2);
        status.Text = $"{Arrangement.Lanes[row].Name}: {Arrangement.Lanes[row].Gain:P0} (slider: 0–200%)";
        canvas.Invalidate(new Rectangle(0, bounds.Top - LaneHeight + 25, Header, LaneHeight));
        if (canvas.IsHandleCreated) canvas.Update();
        this.RequestLiveGainMix();
    }
    private void ClearRangeSelection()
    { selectedClips.Clear(); fullSelectedClips.Clear(); selectionFirstRow = selectionLastRow = -1; selectionStart = selectionEnd = 0; lassoRects.Clear(); }
    private void UpdateRangeSelection(Point point)
    {
        selectionPointer = point;
        selectionStart = Arrangement.SnapTime(Math.Min(XTime(down.X), XTime(point.X)), ModifierKeys.HasFlag(Keys.Shift));
        selectionEnd = Arrangement.SnapTime(Math.Max(XTime(down.X), XTime(point.X)), ModifierKeys.HasFlag(Keys.Shift));
        selectionFirstRow = Math.Clamp(Math.Min(RowAt(down.Y), RowAt(point.Y)), 0, Math.Max(0, Arrangement.Lanes.Count - 1));
        selectionLastRow = Math.Clamp(Math.Max(RowAt(down.Y), RowAt(point.Y)), 0, Math.Max(0, Arrangement.Lanes.Count - 1));
        selectedClips.Clear();
        fullSelectedClips.Clear();
        selectedClips.UnionWith(additiveSelection);
        if (selectingWhole) fullSelectedClips.UnionWith(additiveSelection);
        // Lasso (Ctrl): when accumulating, seed the first rectangle so an initial
        // Ctrl-drag also grows instead of staying a single moving rectangle.
        if (selectionAdditive && lassoRects.Count == 0
            && (Math.Abs(selectionEnd - selectionStart) > 1e-9 || selectionFirstRow != selectionLastRow))
        {
            lassoRects.Add((selectionStart, selectionEnd, selectionFirstRow, selectionLastRow));
        }
        // Lasso (Ctrl): accumulate completed rectangles; current drag extends the selection.
        var rects = new List<(double, double, int, int)>(lassoRects) { (selectionStart, selectionEnd, selectionFirstRow, selectionLastRow) };
        foreach (var (rs, re, rf, rl) in rects)
            for (int row = Math.Max(0, rf); row <= Math.Min(Arrangement.Lanes.Count - 1, rl); row++)
                foreach (var clip in Arrangement.Lanes[row].Clips)
                    if (clip.Start < re && clip.Start + Arrangement.ClipDuration(clip) > rs)
                    { selectedClips.Add(clip.Id); if (selectingWhole) fullSelectedClips.Add(clip.Id); }
        status.Text = $"Selection: {selectionStart:0.000}–{selectionEnd:0.000}s; tracks {selectionFirstRow + 1}–{selectionLastRow + 1}; {selectedClips.Count} segments";
        canvas.Invalidate();
    }
    private void DrawLaneButton(Graphics g, int row, int button, string text, Color color)
    {
        Rectangle bounds = LaneButtonRect(row, button);
        using var brush = new SolidBrush(color); g.FillRectangle(brush, bounds);
        TextRenderer.DrawText(g, text, Font, bounds, color == Color.LightGreen ? Color.Black : Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
    private Rectangle ClipRect(int row, MixClip c) => new(TimeX(c.Start), Ruler + (row - vertical.Value) * LaneHeight + 3,
        Math.Max(2, (int)Math.Round(Arrangement.ClipDuration(c) * pixelsPerSecond)), LaneHeight - 7);
    private void DrawWave(Graphics g, Rectangle rect, MixClip clip, MixSource source)
    {
        int channels = source.Channels; int top = rect.Top + 23, height = rect.Height - 25;
        if (!wavePeaks.TryGetValue((source.Id, channels), out var peaks))
        {
            int blocks = (source.Data.Length / channels + 255) / 256;
            peaks = Enumerable.Range(0, Math.Min(channels, 8)).Select(_ => new float[blocks]).ToArray();
            for (int frame = 0; frame < source.Data.Length / channels; frame++)
                for (int ch = 0; ch < peaks.Length; ch++) peaks[ch][frame / 256] = Math.Max(peaks[ch][frame / 256], Math.Abs(source.Data[frame * channels + ch]));
            wavePeaks[(source.Id, channels)] = peaks;
        }
        using var pen = new Pen(VisualColor(Arrangement.Visual.Waveform));
        int first = Math.Max(rect.Left, Header), last = Math.Min(rect.Right, canvas.Width);
        for (int x = first; x < last; x++)
        {
            double fraction = (x - rect.Left) / (double)Math.Max(1, rect.Width);
            int frame = (int)((clip.SourceOffset + fraction * clip.SourceDuration) * source.SampleRate);
            int span = Math.Max(1, (int)(clip.SourceDuration * source.SampleRate / Math.Max(1, rect.Width)));
            for (int ch = 0; ch < Math.Min(channels, 8); ch++)
            {
                float peak = 0;
                if (span >= 256)
                    for (int block = frame / 256; block <= Math.Min(peaks[ch].Length - 1, (frame + span) / 256); block++) peak = Math.Max(peak, peaks[ch][block]);
                else for (int f = frame; f < Math.Min(source.Data.Length / channels, frame + span); f++) peak = Math.Max(peak, Math.Abs(source.Data[f * channels + ch]));
                float band = height / (float)Math.Min(channels, 8), mid = top + band * (ch + 0.5f);
                g.DrawLine(pen, x, mid - Math.Min(1, peak) * band * 0.45f, x, mid + Math.Min(1, peak) * band * 0.45f);
            }
        }
    }
    private void MouseDownTimeline(object? sender, MouseEventArgs e)
    {
        canvas.Focus(); if (busy) return;
        Guid? previousSelectedClip = selectedClip;
        lastPointer = e.Location;
        if (pastePreview) { if (e.Button == MouseButtons.Left && e.X >= Header && e.Y >= Ruler) PlacePastePreview(); else if (e.Button == MouseButtons.Right) CancelPastePreview(); return; }
        // A right click must not collapse an existing range/multiselection or
        // silently retarget it to the segment/empty space under the pointer.
        if (e.Button == MouseButtons.Right) { ShowMenu(e.Location); return; }
        int row = RowAt(e.Y); selectedLane = row >= 0 && row < Arrangement.Lanes.Count ? Arrangement.Lanes[row].Id : null;
        selectedClip = row >= 0 && row < Arrangement.Lanes.Count
            ? Arrangement.Lanes[row].Clips.LastOrDefault(c => e.X >= Header && ClipRect(row, c).Contains(e.Location))?.Id : null;
        if (e.Button != MouseButtons.Left) return;
        down = e.Location; originalPointerTime = XTime(e.X); dragBefore = Arrangement.Capture();
        selectionAdditive = ModifierKeys.HasFlag(Keys.Control); selectingWhole = selectionAdditive;
        if (!selectionAdditive) lassoRects.Clear();
        additiveSelection.Clear();
        if (selectionAdditive) additiveSelection.UnionWith(selectedClips);
        // Ctrl+click on a segment that is already part of the multi-selection removes it again
        // (toggle). The segment is not dragged afterwards.
        if (selectionAdditive && selectedClip is { } toggled && selectedClips.Contains(toggled) && e.X >= Header && e.Y >= Ruler)
        {
            selectedClips.Remove(toggled); fullSelectedClips.Remove(toggled);
            if (selectedClip == toggled) selectedClip = null;
            canvas.Invalidate(); return;
        }
        if (selectionAdditive && previousSelectedClip is { } previousId) { selectedClips.Add(previousId); additiveSelection.Add(previousId); }
        if (selectionAdditive && selectedClip is { } clicked && e.X >= Header && e.Y >= Ruler)
        {
            SelectWholeClip(clicked, true); additiveSelection.UnionWith(selectedClips);
            dragMode = 5; canvas.Capture = true; return;
        }
        if (row >= 0 && row < Arrangement.Lanes.Count)
        {
            var lane = Arrangement.Lanes[row];
            if (LaneButtonRect(row, 0).Contains(e.Location)) { lane.Solo = !lane.Solo; Changed(); return; }
            if (LaneButtonRect(row, 1).Contains(e.Location)) { lane.Mute = !lane.Mute; Changed(); return; }
            if (e.X < Header)
            {
                if (LaneVolumeRect(row).Contains(e.Location)) { dragMode = 7; SetLaneGain(row, e.X); }
                else { dragMode = 6; draggedLane = lane.Id; reorderTarget = row; reorderMoved = false; canvas.Cursor = Cursors.SizeNS; }
                canvas.Capture = true; return;
            }
        }
        if (e.Y < Ruler && e.X >= Header)
        {
            rulerOriginalLoopStart = Arrangement.LoopStart; rulerOriginalLoopEnd = Arrangement.LoopEnd;
            caret = manualCaret = Arrangement.SnapTime(XTime(e.X), ModifierKeys.HasFlag(Keys.Shift)); dragMode = 4;
        }
        else if (Clip is { } clip)
        {
            var rect = ClipRect(row, clip);
            // Only the title handle moves clips. Waveforms are for selecting audio regions.
            dragMode = e.Y < rect.Top + 21 ? (e.X - rect.Left < 7 ? 2 : rect.Right - e.X < 7 ? 3 : 1) : 5;
            originalStart = clip.Start; originalOffset = clip.SourceOffset; originalDuration = clip.SourceDuration;
            dragOriginalStarts.Clear();
            if (dragMode == 1 && selectedClips.Count > 1 && selectedClips.Contains(clip.Id))
            {
                foreach (var grouped in Arrangement.Lanes.SelectMany(l => l.Clips).Where(c => selectedClips.Contains(c.Id)))
                    dragOriginalStarts[grouped.Id] = grouped.Start;
            }
            if (dragMode != 5) ClearRangeSelection();
        }
        else if (e.X >= Header && row >= 0 && row < Arrangement.Lanes.Count) dragMode = 5;
        if (dragMode == 5) { if (!selectionAdditive) ClearRangeSelection(); selectedClip = null; UpdateRangeSelection(e.Location); }
        canvas.Capture = dragMode != 0; canvas.Invalidate();
    }
    private void MouseMoveTimeline(object? sender, MouseEventArgs e)
    {
        lastPointer = e.Location;
        if (pastePreview) { canvas.Invalidate(); return; }
        if (dragMode == 0)
        {
            int row = RowAt(e.Y);
            var hit = row >= 0 && row < Arrangement.Lanes.Count
                ? Arrangement.Lanes[row].Clips.LastOrDefault(c => ClipRect(row, c).Contains(e.Location)) : null;
            if (hit != null && e.X >= Header)
            {
                var rect = ClipRect(row, hit);
                canvas.Cursor = e.Y >= rect.Top + 21 ? Cursors.Cross : e.X - rect.Left < 7 || rect.Right - e.X < 7 ? Cursors.SizeWE : Cursors.Hand;
            }
            else canvas.Cursor = e.X >= Header ? Cursors.Cross : row >= 0 && row < Arrangement.Lanes.Count
                ? LaneVolumeRect(row).Contains(e.Location) ? Cursors.Hand : Cursors.SizeNS : Cursors.Default;
            return;
        }
        if (dragMode == 5)
        {
            bool ctrlNow = (ModifierKeys & Keys.Control) != 0;
            if (ctrlNow != selectionAdditive)
            {
                // Toggle lasso/accumulate mode on the fly while dragging.
                if (ctrlNow)
                {
                    additiveSelection.Clear();
                    additiveSelection.UnionWith(selectedClips);
                    if (lassoRects.Count == 0) lassoRects.Add((selectionStart, selectionEnd, selectionFirstRow, selectionLastRow));
                }
                else
                {
                    lassoRects.Clear();
                    additiveSelection.Clear();
                }
                selectionAdditive = ctrlNow;
                selectingWhole = ctrlNow;
                canvas.Invalidate();
            }
            if (!selectionAdditive || Math.Abs(e.X - down.X) + Math.Abs(e.Y - down.Y) > 3) UpdateRangeSelection(e.Location);
            return;
        }
        if (dragMode == 6)
        {
            if (Math.Abs(e.Y - down.Y) < SystemInformation.DragSize.Height / 2) return;
            reorderMoved = true;
            if (e.Y < Ruler + 12 && vertical.Value > 0) vertical.Value--;
            else if (e.Y > canvas.Height - 12 && vertical.Value < Math.Max(0, Arrangement.Lanes.Count - 1)) vertical.Value++;
            reorderTarget = Math.Clamp((int)Math.Floor((e.Y - Ruler) / (double)LaneHeight + 0.5) + vertical.Value, 0, Arrangement.Lanes.Count);
            canvas.Invalidate(); return;
        }
        if (dragMode == 7) { SetLaneGain(Arrangement.Lanes.FindIndex(l => l.Id == selectedLane), e.X); return; }
        if (dragMode == 4)
        {
            Arrangement.LoopStart = Math.Max(0, Arrangement.SnapTime(Math.Min(XTime(down.X), XTime(e.X)), ModifierKeys.HasFlag(Keys.Shift)));
            Arrangement.LoopEnd = Math.Max(0, Arrangement.SnapTime(Math.Max(XTime(down.X), XTime(e.X)), ModifierKeys.HasFlag(Keys.Shift)));
        }
        else if (Clip is { } c && Arrangement.Sources.TryGetValue(c.SourceId, out var s))
        {
            this.AutoPanClipDrag(e.X);
            double delta = XTime(e.X) - originalPointerTime, factor = Arrangement.Rate(c);
            if (dragMode == 1)
            {
                c.Start = Arrangement.SnapTime(originalStart + delta, ModifierKeys.HasFlag(Keys.Shift));
                if (dragOriginalStarts.Count > 0)
                {
                    foreach (var clip in Arrangement.Lanes.SelectMany(l => l.Clips).Where(cl => dragOriginalStarts.ContainsKey(cl.Id)).ToArray())
                    {
                        if (clip.Id == c.Id) continue;
                        double groupStart = dragOriginalStarts.TryGetValue(clip.Id, out double gs) ? gs : clip.Start;
                        clip.Start = Arrangement.SnapTime(groupStart + delta, ModifierKeys.HasFlag(Keys.Shift));
                    }
                }
            }
            if (dragMode == 2)
            {
                double start = Arrangement.SnapTime(originalStart + delta, ModifierKeys.HasFlag(Keys.Shift));
                double offset = Math.Clamp(originalOffset + (start - originalStart) * factor, 0, originalOffset + originalDuration - 0.001);
                c.Start = originalStart + (offset - originalOffset) / factor; c.SourceOffset = offset; c.SourceDuration = originalDuration - offset + originalOffset;
            }
            if (dragMode == 3)
            {
                double end = Arrangement.SnapTime(originalStart + originalDuration / factor + delta, ModifierKeys.HasFlag(Keys.Shift));
                c.SourceDuration = Math.Clamp((end - c.Start) * factor, 0.001, s.Duration - c.SourceOffset);
            }
        }
        canvas.Invalidate();
    }
    private void MouseUpTimeline(object? sender, MouseEventArgs e)
    {
        if (dragMode == 0) return;
        if (dragMode == 6 && draggedLane is { } laneId && reorderMoved)
        {
            reorderTarget = Math.Clamp((int)Math.Floor((e.Y - Ruler) / (double)LaneHeight + 0.5) + vertical.Value, 0, Arrangement.Lanes.Count);
            MoveLane(laneId, reorderTarget);
            draggedLane = null; reorderTarget = -1;
        }
        if (dragMode == 1 && Clip is { } clip)
        {
            int target = RowAt(e.Y);
            var group = dragOriginalStarts.Count > 0
                ? Arrangement.Lanes.SelectMany(l => l.Clips).Where(c => dragOriginalStarts.ContainsKey(c.Id)).ToList()
                : new List<MixClip> { clip };
            if (target >= 0 && target < Arrangement.Lanes.Count)
            {
                foreach (var grouped in group)
                {
                    var oldLane = Arrangement.Lanes.FirstOrDefault(l => l.Clips.Contains(grouped));
                    if (oldLane != null && oldLane != Arrangement.Lanes[target])
                    { oldLane.Clips.Remove(grouped); Arrangement.Lanes[target].Clips.Add(grouped); }
                }
                selectedLane = Arrangement.Lanes[target].Id;
            }
        }
        dragOriginalStarts.Clear();
        bool rulerDrag = dragMode == 4;
        bool rangeDrag = dragMode == 5;
        if (rangeDrag && selectionAdditive && selectionEnd > selectionStart)
        {
            var current = (selectionStart, selectionEnd, selectionFirstRow, selectionLastRow);
            if (!lassoRects.Any(r => Math.Abs(r.Start - current.selectionStart) < 1e-9 && Math.Abs(r.End - current.selectionEnd) < 1e-9 && r.FirstRow == current.selectionFirstRow && r.LastRow == current.selectionLastRow))
                lassoRects.Add(current);
        }
        dragMode = 0; canvas.Capture = false; dragBefore = null;
        if (rulerDrag && Math.Abs(e.X - down.X) < 3)
        {
            Arrangement.LoopStart = rulerOriginalLoopStart; Arrangement.LoopEnd = rulerOriginalLoopEnd;
            if (playback != null) playback.SetPosition((long)(caret * playback.SampleRate));
            canvas.Invalidate(); return;
        }
        if (rulerDrag) this.UpdateTransportLoop(); else if (!rangeDrag) Changed();
        draggedLane = null; reorderTarget = -1; reorderMoved = false;
        canvas.Cursor = Cursors.Default;
    }
    private void MoveLane(Guid id, int insertion)
    {
        int old = Arrangement.Lanes.FindIndex(l => l.Id == id);
        if (old < 0) return;
        var lane = Arrangement.Lanes[old]; Arrangement.Lanes.RemoveAt(old);
        int target = Math.Clamp(insertion > old ? insertion - 1 : insertion, 0, Arrangement.Lanes.Count);
        Arrangement.Lanes.Insert(target, lane); selectedLane = lane.Id;
        ClearRangeSelection();
    }
    private async void DropAudio(object? sender, DragEventArgs e)
    {
        if (busy || this.windowGroup.IsBlocked) return; Point p = canvas.PointToClient(new Point(e.X, e.Y)); int row = RowAt(p.Y);
        var audios = Dragged(e.Data); double start = Arrangement.SnapTime(XTime(p.X), (e.KeyState & 4) != 0);
        foreach (var audio in audios)
        {
            InitializeTempoFromFirstTrack(audio.Bpm > 0 ? audio.Bpm : audio.ScannedBpm);
            bool firstTrack = !Arrangement.Lanes.Any(l => l.Clips.Count > 0);
            float sourceBpm = audio.Bpm > 0 ? audio.Bpm : audio.ScannedBpm;
            ClipTempoMode? mode = ClipTempoMode.Original;
            if (!firstTrack && float.IsFinite(sourceBpm) && sourceBpm > 0 && Math.Abs(sourceBpm - Arrangement.Bpm) > 0.1)
                mode = await ChooseTempoAsync(sourceBpm);
            if (mode == null) continue;
            PlaylistStretchSettings? stretchSettings = mode == ClipTempoMode.TimeStretch ? await ConfigureImportStretchAsync(audio) : null;
            if (mode == ClipTempoMode.TimeStretch && stretchSettings == null) continue;
            var source = new MixSource { Name = audio.Name, Data = (float[])audio.Data.Clone(), SampleRate = audio.SampleRate,
                Channels = audio.Channels, Bpm = sourceBpm };
            if (mode != ClipTempoMode.Original)
            {
                MixSource original = source; Arrangement.Sources.Add(original.Id, original);
                MixSource? prepared = null;
                status.Text = $"Importing: {mode} to {Arrangement.Bpm:0.##} BPM…";
                await RunAsync(async token => prepared = await MixTempoOrigins.PrepareAsync(Arrangement, original, stretchSettings?.TargetBpm ?? Arrangement.Bpm,
                    Arrangement.Bpm, mode.Value, stretchSettings, token));
                if (prepared == null || closing || released) { Arrangement.Sources.Remove(original.Id); break; }
                source = prepared;
            }
            Arrangement.Sources.Add(source.Id, source);
            MixLane lane;
            if (row >= 0 && row < Arrangement.Lanes.Count) lane = Arrangement.Lanes[row];
            else { lane = new() { Name = source.Name }; Arrangement.Lanes.Add(lane); }
            // Honor the overlap mode when placing the dropped audio.
            double clipDur = Math.Max(0.001, source.Duration);
            if (Arrangement.OverlapMode == MixOverlapMode.DockNoOverlap && row >= 0 && row < Arrangement.Lanes.Count)
            {
                double candidate = start; bool changed = true; int guard = 0;
                while (changed && guard < 200)
                {
                    changed = false; guard++;
                    foreach (var existing in lane.Clips)
                    {
                        double eEnd = existing.Start + Arrangement.ClipDuration(existing);
                        if (candidate < eEnd && candidate + clipDur > existing.Start)
                        { candidate = eEnd; changed = true; break; }
                    }
                }
                start = Arrangement.SnapTime(candidate, (e.KeyState & 4) != 0);
            }
            else if (Arrangement.OverlapMode == MixOverlapMode.AllowOverlapOverwrite && row >= 0 && row < Arrangement.Lanes.Count)
            {
                foreach (var existing in lane.Clips.Where(c => c.Start < start + clipDur && c.Start + Arrangement.ClipDuration(c) > start).ToList())
                    lane.Clips.Remove(existing);
            }
            Arrangement.AddClip(lane, source, start); row++;
        }
        Changed();
    }
    private void ShowMenu(Point point)
    {
        this.timelineContextMenu?.Close();
        var menu = this.CreateTimelineContextMenu(point); this.timelineContextMenu = menu;
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(this.timelineContextMenu, menu)) this.timelineContextMenu = null;
            if (!IsDisposed && !Disposing && IsHandleCreated) BeginInvoke((Action)menu.Dispose);
            else menu.Dispose();
        };
        menu.Show(canvas, point);
    }
    internal ContextMenuStrip CreateTimelineContextMenu(Point point)
    {
        if (!this.HasAudioEffectSelection)
        {
            int row = RowAt(point.Y);
            selectedLane = row >= 0 && row < Arrangement.Lanes.Count ? Arrangement.Lanes[row].Id : null;
            selectedClip = row >= 0 && row < Arrangement.Lanes.Count ? Arrangement.Lanes[row].Clips.LastOrDefault(clip =>
                point.X >= Header && ClipRect(row, clip).Contains(point))?.Id : null;
        }
        return this.HasAudioEffectSelection ? this.CreateAudioContextMenu() : this.CreateGeneralContextMenu(point);
    }
    private ContextMenuStrip CreateAudioContextMenu()
    {
        var menu = new ContextMenuStrip { Name = "mixAudioContextMenu", ShowItemToolTips = true };
        void Item(string text, Action action, bool enabled = true) { var item = new ToolStripMenuItem(text) { Enabled = enabled }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Item("Undo (Ctrl+Z)", Undo, undo.Count > 0); Item("Redo (Ctrl+Y)", Redo, redo.Count > 0);
        ToolStripMenuItem effects = ModularAudience.Forms.Effects.EffectMenuFactory.CreateApplyMenu(this.OpenMixEffect);
        effects.Enabled = !busy && !closing && !this.mixEffectOpening; menu.Items.Add(effects);
        menu.Items.Add(new ToolStripSeparator());
        if (selectionFirstRow >= 0 && selectionEnd > selectionStart)
        {
            Item("Set loop to selection", () => { Arrangement.LoopStart = Math.Max(0, selectionStart); Arrangement.LoopEnd = Math.Max(0, selectionEnd); this.UpdateTransportLoop(); });
            Item("Delete selected audio range", DeleteSelectedRange);
            Item("Render selected range → Collection", async () => await ExportSelectedRangeAsync(), SelectedParts().Count > 0);
        }
        else Item("Render selection → Collection", async () => await ExportSelectionAsync(), SelectedParts().Count > 0);
        Item("Copy selection (Ctrl+C)", CopySelection, SelectedParts().Count > 0);
        Item("Repeat selection…", RepeatSelection, SelectedParts().Count > 0 || Clip != null);
        Item("Delete selection (Delete)", DeleteSelection, SelectedParts().Count > 0);
        Item("Clear selection", this.ClearAudioEffectSelection);
        if (Clip is { } clip && SelectedParts().Count <= 1)
        {
            Item("Clip properties…", async () => await EditPropertiesAsync(clip));
            Item("Split at playhead (S)", Split);
            Item("Duplicate (Ctrl+D)", Duplicate);
            foreach (var mode in Enum.GetValues<ClipTempoMode>()) Item("Tempo: " + mode, () => { clip.TempoMode = mode; Changed(); });
            Item("Set source BPM…", async () =>
            {
                var source = Arrangement.Sources[clip.SourceId];
                string? value = await PromptAsync("Source BPM (0 = unknown)", source.Bpm.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsed) && float.IsFinite(parsed) && parsed is >= 0 and <= 400)
                {
                    // Copy source metadata so undo does not mutate an older snapshot's asset.
                    var copy = new MixSource { Name = source.Name, Data = source.Data, SampleRate = source.SampleRate, Channels = source.Channels, Bpm = parsed,
                        TempoOriginId = source.TempoOriginId, TempoOriginScale = source.TempoOriginScale,
                        TempoOriginBpm = source.TempoOriginBpm, TempoRecipe = source.TempoRecipe };
                    Arrangement.Sources.Add(copy.Id, copy); clip.SourceId = copy.Id; Changed();
                }
            });
            Item("Align start to nearest beat", () => { double beat = 60 / Arrangement.Bpm; clip.Start = Math.Round(clip.Start / beat) * beat; Changed(); });
        }
        return menu;
    }
    private ContextMenuStrip CreateGeneralContextMenu(Point point)
    {
        var menu = new ContextMenuStrip { Name = "mixGeneralContextMenu" };
        void Item(string text, Action action, bool enabled = true) { var item = new ToolStripMenuItem(text) { Enabled = enabled }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Item("Undo (Ctrl+Z)", Undo, undo.Count > 0); Item("Redo (Ctrl+Y)", Redo, redo.Count > 0);
        Item("View Settings…", ShowViewSettings);
        if (Lane is { } lane)
        {
            Item("Track properties (gain / solo / mute / crossfade)…", async () => await EditPropertiesAsync(lane));
            Item(lane.Mute ? "Unmute track" : "Mute track", () => { lane.Mute = !lane.Mute; Changed(); });
            Item(lane.Solo ? "Unsolo track" : "Solo track", () => { lane.Solo = !lane.Solo; Changed(); });
            Item("Move track up", () => { int index = Arrangement.Lanes.IndexOf(lane); if (index > 0) { Arrangement.Lanes.RemoveAt(index); Arrangement.Lanes.Insert(index - 1, lane); Changed(); } });
            Item("Move track down", () => { int index = Arrangement.Lanes.IndexOf(lane); if (index + 1 < Arrangement.Lanes.Count) { Arrangement.Lanes.RemoveAt(index); Arrangement.Lanes.Insert(index + 1, lane); Changed(); } });
        }
        Item("Remove Track", () =>
        {
            // Direct click: remove the track under the context menu point and, when a
            // multi-selection exists, every affected selected track too. Unlike the
            // chooser below, this does not reopen the removed tracks as TrackViews.
            var lanes = new HashSet<Guid>();
            int row = RowAt(point.Y);
            if (row >= 0 && row < Arrangement.Lanes.Count) lanes.Add(Arrangement.Lanes[row].Id);
            foreach (var clipId in selectedClips)
            {
                var lane = Arrangement.Lanes.FirstOrDefault(l => l.Clips.Any(c => c.Id == clipId));
                if (lane != null) lanes.Add(lane.Id);
            }
            if (lanes.Count == 0 && Lane is { } fallback) lanes.Add(fallback.Id);
            RemoveTracks(lanes, openTrackView: false);
            menu.Close();
        });
        var remove = new ToolStripMenuItem("Remove Track…");
        var combo = new ToolStripComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250 };
        foreach (var option in Arrangement.Lanes) combo.Items.Add(new LaneItem(option.Id, option.Name));
        combo.SelectedIndexChanged += (_, _) => { if (combo.SelectedItem is LaneItem item) { RemoveLane(item.Id); menu.Close(); } };
        remove.DropDownItems.Add(combo); menu.Items.Add(remove);
        Item("Add empty track", AddEmptyTrack);
        Item("Paste preview (Ctrl+V)", StartPastePreview, clipboardHistory.Count > 0);
        if (Lane is { } targetLane)
        {
            var insert = new ToolStripMenuItem("Place source audio here");
            var sources = new ToolStripComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
            foreach (var source in Arrangement.Sources.Values) sources.Items.Add(new LaneItem(source.Id, source.Name));
            double time = Arrangement.SnapTime(XTime(point.X), ModifierKeys.HasFlag(Keys.Shift));
            sources.SelectedIndexChanged += (_, _) =>
            {
                if (sources.SelectedItem is LaneItem item) { Arrangement.AddClip(targetLane, Arrangement.Sources[item.Id], time); Changed(); menu.Close(); }
            };
            insert.DropDownItems.Add(sources); menu.Items.Add(insert);
        }
        Item("Select loop range…", () => EditLoop());
        Item("Fit arrangement horizontally", () => { pixelsPerSecond = Math.Clamp((canvas.Width - Header - 24) / Math.Max(1, Arrangement.Duration), 8, 2000); scroll.Value = 0; canvas.Invalidate(); });
        Item("Rename mix…", RenameMix);
        return menu;
    }
    private sealed record LaneItem(Guid Id, string Name) { public override string ToString() => Name; }
    private void DeleteSelectedRange()
    {
        if (fullSelectedClips.Count > 0) { DeleteSelection(); return; }
        if (selectionFirstRow < 0 || selectionEnd <= selectionStart) return;
        for (int row = selectionFirstRow; row <= selectionLastRow && row < Arrangement.Lanes.Count; row++)
        {
            var lane = Arrangement.Lanes[row];
            foreach (var clip in lane.Clips.ToArray())
            {
                double end = clip.Start + Arrangement.ClipDuration(clip);
                if (clip.Start >= selectionEnd || end <= selectionStart) continue;
                double factor = Arrangement.Rate(clip), originalStart = clip.Start, originalOffset = clip.SourceOffset;
                lane.Clips.Remove(clip);
                if (originalStart < selectionStart)
                {
                    var left = System.Text.Json.JsonSerializer.Deserialize<MixClip>(System.Text.Json.JsonSerializer.Serialize(clip))!;
                    left.Id = Guid.NewGuid(); left.SourceDuration = (selectionStart - originalStart) * factor; left.FadeOut = 0;
                    lane.Clips.Add(left);
                }
                if (end > selectionEnd)
                {
                    var right = System.Text.Json.JsonSerializer.Deserialize<MixClip>(System.Text.Json.JsonSerializer.Serialize(clip))!;
                    right.Id = Guid.NewGuid(); right.Start = selectionEnd;
                    right.SourceOffset = originalOffset + (selectionEnd - originalStart) * factor;
                    right.SourceDuration = (end - selectionEnd) * factor; right.FadeIn = 0;
                    lane.Clips.Add(right);
                }
            }
        }
        ClearRangeSelection(); selectedClip = null; Changed();
    }
    private Task ExportSelectedRangeAsync() => ExportSelectionAsync();
    private void ShowSourcePicker(MixLane lane, double time, Point location)
    {
        var menu = new ContextMenuStrip();
        if (Arrangement.Sources.Count == 0)
            menu.Items.Add(new ToolStripMenuItem("Drop audio from an Audio Collection to add a source") { Enabled = false });
        foreach (var source in Arrangement.Sources.Values)
        {
            var item = new ToolStripMenuItem(source.Name);
            item.Click += (_, _) => { Arrangement.AddClip(lane, source, time); Changed(); };
            menu.Items.Add(item);
        }
        menu.Closed += (_, _) => { if (!IsDisposed) BeginInvoke((Action)menu.Dispose); };
        menu.Show(canvas, location);
    }
    private void CopyClip()
    {
        CopySelection();
    }
    private void PasteClip()
    {
        StartPastePreview();
    }
    private void RemoveLane(Guid id)
    {
        RemoveTracks(new[] { id }, openTrackView: true);
    }
    private void RemoveTracks(IEnumerable<Guid> ids, bool openTrackView)
    {
        foreach (var id in ids.ToArray())
        {
        var lane = Arrangement.Lanes.FirstOrDefault(l => l.Id == id);
        if (lane == null) continue;
        if (openTrackView && !joined.ContainsKey(id) && lane.Clips.FirstOrDefault() is { } clip && Arrangement.Sources.TryGetValue(clip.SourceId, out var source))
        {
            using var audio = new AudioObj { Name = source.Name, Data = source.Data, SampleRate = source.SampleRate, Channels = source.Channels,
                Bpm = source.Bpm, Length = source.Data.Length, Duration = TimeSpan.FromSeconds(source.Duration) };
            joined[id] = new TrackView(audio);
            joined[id].SetJoinedMix(this);
        }
        Arrangement.Lanes.Remove(lane); Changed();
        }
    }
    private void Duplicate()
    {
        if (Clip is not { } clip || Lane == null) return;
        var copy = System.Text.Json.JsonSerializer.Deserialize<MixClip>(System.Text.Json.JsonSerializer.Serialize(clip))!;
        copy.Id = Guid.NewGuid(); copy.Start += Arrangement.ClipDuration(clip); Lane.Clips.Add(copy); selectedClip = copy.Id; Changed();
    }
    private void Split()
    {
        if (Clip is not { } clip || Lane == null) return;
        double split = Arrangement.SnapTime(caret, ModifierKeys.HasFlag(Keys.Shift));
        double left = (split - clip.Start) * Arrangement.Rate(clip);
        if (left <= 0.001 || left >= clip.SourceDuration - 0.001) return;
        var copy = System.Text.Json.JsonSerializer.Deserialize<MixClip>(System.Text.Json.JsonSerializer.Serialize(clip))!;
        copy.Id = Guid.NewGuid(); copy.Start = split; copy.SourceOffset += left; copy.SourceDuration -= left; copy.FadeIn = 0;
        clip.SourceDuration = left; clip.FadeOut = 0; Lane.Clips.Add(copy); Changed();
    }
    private async Task<bool> EditPropertiesAsync(object value)
    {
        string before = Arrangement.Capture();
        using var form = new Form { Text = "Arrangement properties", Size = new Size(430, 500), StartPosition = FormStartPosition.CenterParent };
        var grid = new PropertyGrid { Dock = DockStyle.Fill, SelectedObject = value, ToolbarVisible = false };
        var ok = new Button { Text = "Apply", Dock = DockStyle.Bottom, DialogResult = DialogResult.OK };
        form.Controls.Add(grid); form.Controls.Add(ok); form.AcceptButton = ok;
        DialogResult answer = await this.ShowMixDialogAsync(form);
        if (closing || released) return false;
        if (answer != DialogResult.OK) { Arrangement.Restore(before); canvas.Invalidate(); return false; }
        try { Arrangement.Validate(); Changed(); return true; } catch (Exception ex) { Arrangement.Restore(before); await this.ErrorAsync(ex); return false; }
    }
    private async void EditLoop()
    {
        var range = new LoopRange { Start = Arrangement.LoopStart, End = Arrangement.LoopEnd };
        if (await EditPropertiesAsync(range) && double.IsFinite(range.End) && double.IsFinite(range.Start) && range.End > range.Start && range.Start >= 0)
        { Arrangement.LoopStart = range.Start; Arrangement.LoopEnd = range.End; this.UpdateTransportLoop(); }
    }
    private sealed class LoopRange { public double Start { get; set; } public double End { get; set; } }
    private async void RenameMix()
    {
        if (closing) return;
        // Wait for any in-flight operation instead of silently dropping the rename.
        while (busy) { await Task.Delay(50); if (closing) return; }
        string? text = await PromptAsync("Mix / project name", Arrangement.Name);
        if (string.IsNullOrWhiteSpace(text)) return;
        Arrangement.Name = text.Trim();
        Text = Arrangement.Name;
        committedProject = Arrangement.CaptureProject();
        Changed();
        // Force the title bar to reflect the new name immediately.
        try { this.Update(); this.Refresh(); } catch { }
    }
    protected override void WndProc(ref Message message)
    {
        // WM_STYLECHANGING: Windows/WinForms can rewrite styles after CreateParams.
        if (message.Msg == 0x007C && message.WParam.ToInt64() == -16 && message.LParam != IntPtr.Zero)
        {
            int style = System.Runtime.InteropServices.Marshal.ReadInt32(message.LParam, 4);
            System.Runtime.InteropServices.Marshal.WriteInt32(message.LParam, 4, style | 0x00010000 | 0x00040000);
        }
        // WM_NCLBUTTONDBLCLK / HTCAPTION: rename rather than the standard maximize toggle.
        if (message.Msg == 0x00A3 && message.WParam.ToInt64() == 2)
        {
            RenameMix();
            message.Result = IntPtr.Zero;
            return;
        }
        base.WndProc(ref message);
    }
    protected override CreateParams CreateParams
    {
        get { var parameters = base.CreateParams; parameters.Style |= 0x00010000 | 0x00040000; return parameters; }
    }
    private async Task<string?> PromptAsync(string title, string initial)
    {
        using var form = new Form { Text = title, ClientSize = new Size(350, 80), StartPosition = FormStartPosition.CenterParent };
        var text = new TextBox { Text = initial, Dock = DockStyle.Top }; var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom };
        form.Controls.Add(text); form.Controls.Add(ok); form.AcceptButton = ok; return await this.ShowMixDialogAsync(form) == DialogResult.OK ? text.Text : null;
    }
    private async Task TogglePlayAsync()
    {
        if (busy) { operation?.Cancel(); return; }
        if (playback != null) { StopPlayback(); return; }
        await RunAsync(async token =>
        {
            long generation = ++playbackGeneration;
            SchedulePreparation();
            await liveUpdateWork;
            await this.FlushLiveGainMixAsync();
            if (cachedMix == null || cachedMixState != MixPreparationCache.AudioKey(Arrangement))
            {
                status.Text = "Audio preparation is still pending or failed. Check the status and retry.";
                return;
            }
            var preparedAudio = new AudioObj { Name = Arrangement.Name, Data = cachedMix.Data, SampleRate = cachedMix.SampleRate,
                Channels = cachedMix.Channels, Length = cachedMix.Length, Duration = cachedMix.Duration, Bpm = cachedMix.Bpm };
            if (token.IsCancellationRequested || closing) { preparedAudio.Dispose(); return; }
            playback = preparedAudio;
            ApplyPlaybackLoop(playback);
            double startSeconds = Arrangement.Loop ? Math.Clamp(caret, Arrangement.LoopStart, Math.Max(Arrangement.LoopStart, Arrangement.LoopEnd - 0.0001)) : caret;
            startSeconds = Math.Clamp(startSeconds, 0, Math.Max(0, playback.Duration.TotalSeconds - 1.0 / playback.SampleRate));
            playback.StartingOffset = (long)(startSeconds * playback.SampleRate) * playback.Channels;
            playbackWork = playback.PlayAsync(CancellationToken.None, initialVolume: 1);
            await playbackWork;
            if (generation != playbackGeneration || !ReferenceEquals(playback, preparedAudio) || closing) return;
            clock.Start(); play.Text = "Pause / stop"; status.Text = "Playing — single shared sample clock.";
        });
    }
    private void StopPlayback()
    {
        playbackGeneration++;
        liveUpdateTimer.Stop();
        clock.Stop(); var audio = playback; playback = null;
        if (audio != null) { caret = audio.CurrentTime.TotalSeconds; this.playbackStopWork = FinishPlaybackAsync(audio, playbackWork); }
        play.Text = "Play";
    }
    private void ApplyPlaybackLoop(AudioObj audio)
    {
        if (!Arrangement.Loop || Arrangement.LoopEnd <= Arrangement.LoopStart) return;
        long begin = (long)(Arrangement.LoopStart * audio.SampleRate) * audio.Channels;
        long end = Math.Min(audio.Data.LongLength, (long)(Arrangement.LoopEnd * audio.SampleRate) * audio.Channels);
        if (end > begin) audio.UpdateLoopFraction(begin, end, end - begin, true, false);
    }
    private static async Task FinishPlaybackAsync(AudioObj audio, Task work)
    {
        try { await audio.StopAsync(); await work; }
        catch (OperationCanceledException) { }
        catch (Exception error) { LogManager.Log(error); }
        finally { audio.Dispose(); }
    }
    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (busy || closing || released) return; busy = true; operation = new();
        var source = operation;
        try { renderWork = action(source.Token); await renderWork; }
        catch (OperationCanceledException) { if (!IsDisposed) status.Text = "Cancelled."; }
        catch (Exception ex) { if (!IsDisposed && !closing) await this.ErrorAsync(ex); else LogManager.Log(ex); }
        finally { busy = false; source.Dispose(); if (ReferenceEquals(operation, source)) operation = null; }
    }
    private async Task ExportAsync() => await RunAsync(async token =>
    {
        var audio = await MixRenderer.RenderAsync(Arrangement, token);
        if (token.IsCancellationRequested || released) { audio.Dispose(); return; }
        var collection = new AudioCollectionView([audio], Arrangement.Name + " - Mix"); collection.Show();
        status.Text = "Rendered mix added to an Audio Collection.";
    });
    private async Task SaveProjectAsync()
    {
        if (busy) return;
        try
        {
            string directory = DefaultProjectsDirectory;
            Directory.CreateDirectory(directory);
            string filename = string.Concat(Arrangement.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            using var dialog = new SaveFileDialog { Filter = "ModularAudience mix (*.mamix)|*.mamix", FileName = filename + ".mamix",
                InitialDirectory = directory, DefaultExt = "mamix", AddExtension = true };
            if (await this.ShowMixFileDialogAsync(dialog, "Save mix project") != DialogResult.OK) return;
            await RunAsync(async token => await SaveToPathAsync(dialog.FileName, token));
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { await this.ErrorAsync(error); }
    }
    internal async Task OpenProjectAsync()
    {
        if (busy) return;
        try
        {
            string directory = DefaultProjectsDirectory;
            Directory.CreateDirectory(directory);
            using var dialog = MixProjectMenu.CreateOpenDialog();
            if (await this.ShowMixFileDialogAsync(dialog, "Open mix project") != DialogResult.OK)
            {
                // Cancelling the open dialog must not discard the (possibly empty) mix view.
                // Provide a fresh empty mix view so the user can keep working.
                try { var fresh = new MixTrackView(); fresh.Show(); } catch { }
                return;
            }
            await RunAsync(async token =>
            {
                await LoadFromPathAsync(dialog.FileName, token);
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { await this.ErrorAsync(error); }
    }
    public static string DefaultProjectsDirectory => Path.Combine(AudioStoragePaths.WorkingDirectory, "_Projects");
    private async void CloseMix(object? sender, FormClosingEventArgs e)
    {
        if (allowClose) return;
        e.Cancel = true; if (closing) return;
        projectSaveTimer.Stop();
        await FlushProjectSaveAsync();
        if (dirty && projectPath != null)
        {
            DialogResult answer = await this.windowGroup.AskAsync("Save changes to the project before closing?", "Close mix", MessageBoxButtons.YesNoCancel);
            if (answer == DialogResult.Cancel) return;
            if (answer == DialogResult.Yes) await RunAsync(async token => await SaveToPathAsync(projectPath, token));
        }
        else if (dirty)
        {
            if (await this.windowGroup.AskAsync("Close this mix without saving? Unsaved changes will be lost.", "Close mix", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        }
        closing = true; operation?.Cancel();
        this.CloseMixEffectWindows();
        this.windowGroup.Dispose();
        tempoDebounce.Stop();
        liveUpdateTimer.Stop(); liveUpdateCancellation?.Cancel();
        try { await renderWork; await liveUpdateWork; } catch { }
        StopPlayback();
        this.RestoreJoinedWindowsOnClose();
        allowClose = true; Close();
    }
    private async Task ErrorAsync(Exception error)
    {
        LogManager.Log("Mix editor failed.", error);
        if (released || closing || IsDisposed) return;
        status.Text = error.Message;
        if (!this.windowGroup.IsBlocked) await this.windowGroup.AskAsync(error.Message, "Mix editor", MessageBoxButtons.OK);
    }
    private void ReleaseResources()
    {
        if (released) return; released = true; this.CloseMixEffectWindows(); this.windowGroup.Dispose(); this.CancelLiveGainMix(); this.timelineContextMenu?.Dispose(); open.Remove(this); operation?.Cancel(); StopPlayback(); clock.Dispose(); liveUpdateTimer.Dispose();
        this.interactionHints.Dispose();
        liveUpdateCancellation?.Cancel();
        hueTimer.Dispose(); hueClock.Stop();
        tempoDebounce.Dispose();
        projectSaveTimer.Dispose(); pasteHoldTimer.Dispose();
        this.RestoreJoinedWindowsOnClose();
        preparationCache.Dispose(); cachedMix?.Dispose(); cachedMix = null; wavePeaks.Clear(); Arrangement.Dispose();
    }
    protected override void Dispose(bool disposing) { if (disposing) ReleaseResources(); base.Dispose(disposing); }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (this.windowGroup.IsBlocked) return true;
        if (busy && keyData != Keys.Space && keyData != Keys.Escape) return base.ProcessCmdKey(ref msg, keyData);
        if (keyData == (Keys.Control | Keys.Z)) { Undo(); return true; }
        if (keyData == (Keys.Control | Keys.Y)) { Redo(); return true; }
        if (ModularAudience.Forms.TextInputFocus.IsActive()) return base.ProcessCmdKey(ref msg, keyData);
        if (keyData == (Keys.Control | Keys.D)) { Duplicate(); return true; }
        if (keyData == (Keys.Control | Keys.C)) { CopySelection(); return true; }
        if (keyData == (Keys.Control | Keys.V)) { BeginPasteHold(); return true; }
        if (keyData == (Keys.Control | Keys.S)) { _ = SaveProjectAsync(); return true; }
        if (keyData == (Keys.Control | Keys.O)) { _ = OpenProjectAsync(); return true; }
        if (keyData == Keys.S) { Split(); return true; }
        if (keyData == Keys.Delete && (selectedClips.Count > 0 || Clip != null)) { DeleteSelection(); return true; }
        if (keyData is Keys.Back or (Keys.Control | Keys.Back)) { JumpToManualCaret(keyData.HasFlag(Keys.Control)); return true; }
        if (keyData == Keys.Space) { _ = TogglePlayAsync(); return true; }
        if (keyData == Keys.Escape && pastePreview) { CancelPastePreview(); return true; }
        if (keyData == Keys.Escape && dragBefore != null)
        {
            Arrangement.Restore(dragBefore); dragBefore = null; dragMode = 0; draggedLane = null; reorderTarget = -1;
            canvas.Capture = false; RefreshPreparedMixer(); canvas.Invalidate(); return true;
        }
        if (keyData == Keys.Escape && this.HasAudioEffectSelection) { this.ClearAudioEffectSelection(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    private sealed class Canvas : Control
    {
        public Canvas() { DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw | ControlStyles.Selectable, true); }
    }
}
