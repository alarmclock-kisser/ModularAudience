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
    private bool restoring, busy, closing, allowClose, dirty;
    private readonly Canvas canvas = new() { Dock = DockStyle.Fill, AllowDrop = true, TabStop = true };
    private readonly HScrollBar scroll = new() { Dock = DockStyle.Bottom, Minimum = 0, Maximum = 100000, LargeChange = 100 };
    private readonly VScrollBar vertical = new() { Dock = DockStyle.Right, Minimum = 0, Maximum = 100, LargeChange = 1 };
    private readonly NumericUpDown bpm = new() { Minimum = 10, Maximum = 400, Value = 120, DecimalPlaces = 1, Width = 70 };
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
    private int selectionFirstRow = -1, selectionLastRow = -1;
    private Point selectionPointer;
    private Guid? draggedLane;
    private int reorderTarget = -1;
    private bool reorderMoved;
    private int dragMode; // 1 move, 2 left trim, 3 right trim, 4 ruler, 5 range, 6 reorder, 7 gain
    private Point down;
    private string? dragBefore;
    private double originalStart, originalOffset, originalDuration;
    private double rulerOriginalLoopStart, rulerOriginalLoopEnd;
    private bool resizeHeightGesture;
    private int resizeOriginalLaneHeight;
    private const int Header = 190, Ruler = 30;
    private int LaneHeight = 100;
    private double preciseLaneHeight = 100;
    private bool laneHeightCustomized;
    private bool automaticSizing;
    private int previousClientHeight;
    private double ViewStart => scroll.Value / 100.0;
    private double XTime(int x) => Math.Max(0, ViewStart + (x - Header) / pixelsPerSecond);
    private int TimeX(double time) => Header + (int)Math.Round((time - ViewStart) * pixelsPerSecond);
    private int RowAt(int y) => y < Ruler ? -1 : (y - Ruler) / LaneHeight + vertical.Value;
    private MixClip? Clip => Arrangement.Lanes.SelectMany(l => l.Clips).FirstOrDefault(c => c.Id == selectedClip);
    private MixLane? Lane => Arrangement.Lanes.FirstOrDefault(l => l.Id == selectedLane);

    public MixTrackView()
    {
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
        { var b = new Button { Text = name, AutoSize = true }; b.Click += (_, _) => action(); toolbar.Controls.Add(b); return b; }
        Tool("Stop", () => { operation?.Cancel(); StopPlayback(); caret = 0; canvas.Invalidate(); });
        toolbar.Controls.Add(new Label { Text = "BPM", AutoSize = true, Margin = new Padding(3, 8, 3, 3) }); toolbar.Controls.Add(bpm);
        division.Items.AddRange(new object[] { "1/1", "1/2", "1/4", "1/8", "1/16", "1/32", "1/64" }); division.SelectedIndex = 3;
        toolbar.Controls.Add(division); toolbar.Controls.Add(snap); toolbar.Controls.Add(loop); toolbar.Controls.Add(autoScroll);
        autoScroll.Click += (_, _) =>
        {
            Arrangement.Visual.ScrollMode = (MixScrollMode)(((int)Arrangement.Visual.ScrollMode + 1) % 3);
            UpdateScrollModeControl(); Changed();
        };
        Tool("− Zoom", () => Zoom(0.8)); Tool("+ Zoom", () => Zoom(1.25));
        Tool("View Settings…", ShowViewSettings);
        Tool("+ Track", () => { Arrangement.Lanes.Add(new() { Name = "Track " + (Arrangement.Lanes.Count + 1) }); Changed(); });
        Tool("Undo", Undo); Tool("Redo", Redo);
        Tool("Save project…", async () => await SaveProjectAsync());
        Tool("Open project…", async () => await OpenProjectAsync());
        Tool("Render → Collection", async () => await ExportAsync());
        Controls.Add(canvas); Controls.Add(vertical); Controls.Add(scroll); Controls.Add(status); Controls.Add(toolbar);
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
        scroll.ValueChanged += (_, _) => canvas.Invalidate(); vertical.ValueChanged += (_, _) => canvas.Invalidate();
        canvas.Paint += PaintTimeline; canvas.MouseDown += MouseDownTimeline; canvas.MouseMove += MouseMoveTimeline; canvas.MouseUp += MouseUpTimeline;
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
            target.DragEnter += (_, e) => e.Effect = Dragged(e.Data).Length > 0 ? DragDropEffects.Copy : DragDropEffects.None;
            target.DragOver += (_, e) => e.Effect = Dragged(e.Data).Length > 0 ? DragDropEffects.Copy : DragDropEffects.None;
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
        committed = Arrangement.Capture(); open.Add(this);
        previousClientHeight = ClientSize.Height;
        ResizeBegin += (_, _) => { resizeHeightGesture = true; resizeOriginalLaneHeight = LaneHeight; };
        ResizeEnd += (_, _) =>
        {
            resizeHeightGesture = false;
            if (resizeOriginalLaneHeight != LaneHeight)
            {
                laneHeightCustomized = true;
                Arrangement.DisplayLaneHeight = LaneHeight;
                Changed();
            }
        };
        SizeChanged += (_, _) =>
        {
            if (!automaticSizing && previousClientHeight > 0 && Arrangement.Lanes.Count > 0)
            {
                laneHeightCustomized = true;
                preciseLaneHeight = Math.Clamp(preciseLaneHeight + (ClientSize.Height - previousClientHeight) / (double)Arrangement.Lanes.Count, 32, 600);
                LaneHeight = (int)Math.Round(preciseLaneHeight);
            }
            if (!automaticSizing && !resizeHeightGesture) Arrangement.DisplayLaneHeight = LaneHeight;
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
        if (busy || closing || released) { view.SetJoinedMix(view.JoinedMix); return; }
        var existing = joined.FirstOrDefault(p => ReferenceEquals(p.Value, view) && Arrangement.Lanes.Any(l => l.Id == p.Key));
        if (existing.Value != null) { Show(); Activate(); return; }
        float sourceBpm = view.OriginalAudio.Bpm > 0 ? view.OriginalAudio.Bpm : view.OriginalAudio.ScannedBpm;
        bool firstTrack = !Arrangement.Lanes.Any(l => l.Clips.Count > 0);
        if (firstTrack) InitializeTempoFromFirstTrack(sourceBpm);
        ClipTempoMode? tempo = ClipTempoMode.Original;
        if (!firstTrack && float.IsFinite(sourceBpm) && sourceBpm > 0 && Math.Abs(sourceBpm - Arrangement.Bpm) > 0.1)
            tempo = ChooseTempo(sourceBpm);
        if (tempo == null) { view.SetJoinedMix(view.JoinedMix); return; }
        PlaylistStretchSettings? stretchSettings = null;
        if (tempo == ClipTempoMode.TimeStretch)
        {
            stretchSettings = ConfigureImportStretch(view.OriginalAudio);
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
            MixSource? prepared = null;
            Show(); status.Text = $"Importing: {tempo.Value} to {Arrangement.Bpm:0.##} BPM…";
            await RunAsync(async token => prepared = await MixRenderer.PrepareSourceAsync(input, stretchSettings?.TargetBpm ?? Arrangement.Bpm, tempo.Value, token, stretchSettings));
            if (prepared == null || closing || released || view.IsDisposed) { if (!view.IsDisposed) view.SetJoinedMix(view.JoinedMix); return; }
            source = prepared;
        }
        view.LeaveCurrentMix();
        foreach (var old in joined.Where(p => ReferenceEquals(p.Value, view)).Select(p => p.Key).ToArray()) joined.Remove(old);
        Arrangement.Sources.Add(source.Id, source);
        var lane = new MixLane { Name = source.Name, Mute = view.Muted, Solo = view.Soloed };
        Arrangement.Lanes.Add(lane); Arrangement.AddClip(lane, source, 0); joined[lane.Id] = view;
        view.SetJoinedMix(this); view.Hide(); view.Settings.Hide();
        _ = view.OriginalAudio.StopAsync();
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
    private PlaylistStretchSettings? ConfigureImportStretch(AudioObj audio)
    {
        using var snapshot = audio.Clone();
        float sourceBpm = audio.Bpm > 0 ? audio.Bpm : audio.ScannedBpm;
        using var dialog = new TimeStretchDialog(null, [snapshot]) { IsConfigureMode = true };
        dialog.SetConfigurationTempo(sourceBpm, (float)Arrangement.Bpm);
        return dialog.ShowDialog(this.Visible ? this : null) == DialogResult.OK ? dialog.ConfirmedSettings : null;
    }
    private ClipTempoMode? ChooseTempo(float sourceBpm)
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
        dialog.Controls.Add(text); dialog.Controls.Add(buttons); dialog.ShowDialog(this.Visible ? this : null); return result;
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
    private void Changed()
    {
        if (restoring) return;
        string state = Arrangement.Capture();
        if (state != committed) { undo.Push(committed); redo.Clear(); committed = state; dirty = true; Text = Arrangement.Name; }
        foreach (var clip in Arrangement.Lanes.SelectMany(l => l.Clips))
            if (clip.TempoMode != ClipTempoMode.Original && Arrangement.Sources.TryGetValue(clip.SourceId, out var source) && source.Bpm <= 0)
                status.Text = "A tempo-matched clip has no source BPM. Right-click it → Set source BPM.";
        ReconcileJoined();
        vertical.Maximum = Math.Max(0, Arrangement.Lanes.Count - 1); vertical.Value = Math.Min(vertical.Value, vertical.Maximum);
        scroll.Maximum = Math.Max(1000, (int)Math.Min(int.MaxValue - 100, Math.Ceiling(Arrangement.Duration * 100)) + 100);
        canvas.Invalidate();
        if (playback != null)
        {
            if (Arrangement.Loop) ApplyPlaybackLoop(playback);
            else if (playback.LoopEnabled) playback.UpdateLoopFraction(0, playback.Data.LongLength, playback.Data.LongLength, false, false);
        }
        SchedulePreparation();
    }
    private void Restore(string state)
    {
        tempoDebounce.Stop();
        ClearRangeSelection();
        Arrangement.Restore(state); committed = state; restoring = true;
        bpm.Value = (decimal)Arrangement.Bpm; division.SelectedIndex = (int)Math.Log2(Arrangement.SnapDivision);
        snap.Checked = Arrangement.Snap; loop.Checked = Arrangement.Loop; restoring = false; dirty = true;
        LaneHeight = Arrangement.DisplayLaneHeight; preciseLaneHeight = LaneHeight;
        pixelsPerSecond = Arrangement.Visual.PixelsPerSecond;
        UpdateScrollModeControl();
        ReconcileJoined(); Text = Arrangement.Name; canvas.Invalidate(); SchedulePreparation();
    }
    private void Undo() { if (undo.Count > 0) { redo.Push(committed); Restore(undo.Pop()); } }
    private void Redo() { if (redo.Count > 0) { undo.Push(committed); Restore(redo.Pop()); } }
    private void Zoom(double factor)
    {
        pixelsPerSecond = Math.Clamp(pixelsPerSecond * factor, 8, 2000);
        Arrangement.Visual.PixelsPerSecond = pixelsPerSecond; Changed();
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
        if (busy) return;
        tempoDebounce.Stop();
        string before = Arrangement.Capture();
        using var dialog = new MixOptionsDialog(Arrangement, PreviewViewOptions);
        if (dialog.ShowDialog(this) == DialogResult.OK)
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
        LaneHeight = options.DisplayLaneHeight; preciseLaneHeight = LaneHeight; pixelsPerSecond = options.Visual.PixelsPerSecond;
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
                TextRenderer.DrawText(g, clip.Name, Font, new Rectangle(rect.X + 4, rect.Y + 2, Math.Max(1, rect.Width - 8), 18), VisualColor(visual.Text), TextFormatFlags.EndEllipsis);
                if (visual.ShowWaveforms && Arrangement.Sources.TryGetValue(clip.SourceId, out var source)) DrawWave(g, rect, clip, source);
                g.Restore(saved);
            }
            g.DrawLine(Pens.DimGray, 0, y + LaneHeight - 1, canvas.Width, y + LaneHeight - 1);
        }
        if (selectionFirstRow >= 0 && selectionEnd > selectionStart)
        {
            int y = Ruler + (selectionFirstRow - vertical.Value) * LaneHeight;
            var selection = Rectangle.Intersect(new Rectangle(Header, Ruler, Math.Max(0, canvas.Width - Header), Math.Max(0, canvas.Height - Ruler)),
                new Rectangle(TimeX(selectionStart), y, Math.Max(1, TimeX(selectionEnd) - TimeX(selectionStart)), (selectionLastRow - selectionFirstRow + 1) * LaneHeight));
            using var brush = new SolidBrush(Color.FromArgb(65, VisualColor(visual.LoopRange))); g.FillRectangle(brush, selection);
            using var pen = new Pen(VisualColor(visual.Text)) { DashStyle = DashStyle.Dash }; g.DrawRectangle(pen, selection);
        }
        if (dragMode == 6 && reorderTarget >= 0)
        {
            int y = Ruler + (reorderTarget - vertical.Value) * LaneHeight;
            using var pen = new Pen(Color.LightGreen, 3); g.DrawLine(pen, 0, y, canvas.Width, y);
        }
        int cx = TimeX(caret); if (cx >= Header) { using var caretPen = new Pen(VisualColor(visual.Playhead)); g.DrawLine(caretPen, cx, 0, cx, canvas.Height); }
    }
    private Rectangle LaneButtonRect(int row, int button) => new(5 + button * 25,
        Ruler + (row - vertical.Value + 1) * LaneHeight - 25, 21, 21);
    private Rectangle LaneVolumeRect(int row) => new(62, Ruler + (row - vertical.Value + 1) * LaneHeight - 25, Header - 74, 21);
    private void SetLaneGain(int row, int x)
    {
        if (row < 0 || row >= Arrangement.Lanes.Count) return;
        Rectangle bounds = LaneVolumeRect(row);
        Arrangement.Lanes[row].Gain = Math.Clamp((x - bounds.Left) / (double)bounds.Width * 2, 0, 2);
        RefreshPreparedMixer();
        status.Text = $"{Arrangement.Lanes[row].Name}: {Arrangement.Lanes[row].Gain:P0} (slider: 0–200%)";
        canvas.Invalidate();
    }
    private void ClearRangeSelection()
    { selectedClips.Clear(); selectionFirstRow = selectionLastRow = -1; selectionStart = selectionEnd = 0; }
    private void UpdateRangeSelection(Point point)
    {
        selectionPointer = point;
        selectionStart = Arrangement.SnapTime(Math.Min(XTime(down.X), XTime(point.X)), ModifierKeys.HasFlag(Keys.Shift));
        selectionEnd = Arrangement.SnapTime(Math.Max(XTime(down.X), XTime(point.X)), ModifierKeys.HasFlag(Keys.Shift));
        selectionFirstRow = Math.Clamp(Math.Min(RowAt(down.Y), RowAt(point.Y)), 0, Math.Max(0, Arrangement.Lanes.Count - 1));
        selectionLastRow = Math.Clamp(Math.Max(RowAt(down.Y), RowAt(point.Y)), 0, Math.Max(0, Arrangement.Lanes.Count - 1));
        selectedClips.Clear();
        for (int row = selectionFirstRow; row <= selectionLastRow && row < Arrangement.Lanes.Count; row++)
            foreach (var clip in Arrangement.Lanes[row].Clips)
                if (clip.Start < selectionEnd && clip.Start + Arrangement.ClipDuration(clip) > selectionStart) selectedClips.Add(clip.Id);
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
        int row = RowAt(e.Y); selectedLane = row >= 0 && row < Arrangement.Lanes.Count ? Arrangement.Lanes[row].Id : null;
        selectedClip = row >= 0 && row < Arrangement.Lanes.Count
            ? Arrangement.Lanes[row].Clips.LastOrDefault(c => e.X >= Header && ClipRect(row, c).Contains(e.Location))?.Id : null;
        if (e.Button == MouseButtons.Right) { ShowMenu(e.Location); return; }
        if (e.Button != MouseButtons.Left) return;
        down = e.Location; dragBefore = Arrangement.Capture();
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
            caret = Arrangement.SnapTime(XTime(e.X), ModifierKeys.HasFlag(Keys.Shift)); dragMode = 4;
        }
        else if (Clip is { } clip)
        {
            var rect = ClipRect(row, clip);
            // Only the title handle moves clips. Waveforms are for selecting audio regions.
            dragMode = e.Y < rect.Top + 21 ? (e.X - rect.Left < 7 ? 2 : rect.Right - e.X < 7 ? 3 : 1) : 5;
            originalStart = clip.Start; originalOffset = clip.SourceOffset; originalDuration = clip.SourceDuration;
            if (dragMode != 5) ClearRangeSelection();
        }
        else if (e.X >= Header && row >= 0 && row < Arrangement.Lanes.Count) dragMode = 5;
        if (dragMode == 5) { ClearRangeSelection(); selectedClip = null; UpdateRangeSelection(e.Location); }
        canvas.Capture = dragMode != 0; canvas.Invalidate();
    }
    private void MouseMoveTimeline(object? sender, MouseEventArgs e)
    {
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
        if (dragMode == 5) { UpdateRangeSelection(e.Location); return; }
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
            Arrangement.LoopStart = Arrangement.SnapTime(Math.Min(XTime(down.X), XTime(e.X)), ModifierKeys.HasFlag(Keys.Shift));
            Arrangement.LoopEnd = Arrangement.SnapTime(Math.Max(XTime(down.X), XTime(e.X)), ModifierKeys.HasFlag(Keys.Shift));
        }
        else if (Clip is { } c && Arrangement.Sources.TryGetValue(c.SourceId, out var s))
        {
            double delta = (e.X - down.X) / pixelsPerSecond, factor = Arrangement.Rate(c);
            if (dragMode == 1) c.Start = Arrangement.SnapTime(originalStart + delta, ModifierKeys.HasFlag(Keys.Shift));
            if (dragMode == 2)
            {
                double start = Arrangement.SnapTime(originalStart + delta, ModifierKeys.HasFlag(Keys.Shift));
                double offset = Math.Clamp(originalOffset + (start - originalStart) * factor, 0, originalOffset + originalDuration - 0.001);
                c.Start = Math.Max(0, originalStart + (offset - originalOffset) / factor); c.SourceOffset = offset; c.SourceDuration = originalDuration - offset + originalOffset;
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
            var old = Arrangement.Lanes.FirstOrDefault(l => l.Clips.Contains(clip));
            if (old != null && target >= 0 && target < Arrangement.Lanes.Count && old != Arrangement.Lanes[target])
            { old.Clips.Remove(clip); Arrangement.Lanes[target].Clips.Add(clip); selectedLane = Arrangement.Lanes[target].Id; }
        }
        bool rulerDrag = dragMode == 4;
        bool rangeDrag = dragMode == 5;
        dragMode = 0; canvas.Capture = false; dragBefore = null;
        if (rulerDrag && Math.Abs(e.X - down.X) < 3)
        {
            Arrangement.LoopStart = rulerOriginalLoopStart; Arrangement.LoopEnd = rulerOriginalLoopEnd;
            if (playback != null) playback.SetPosition((long)(caret * playback.SampleRate));
            canvas.Invalidate(); return;
        }
        if (!rangeDrag) Changed();
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
        if (busy) return; Point p = canvas.PointToClient(new Point(e.X, e.Y)); int row = RowAt(p.Y);
        var audios = Dragged(e.Data); double start = Arrangement.SnapTime(XTime(p.X), (e.KeyState & 4) != 0);
        foreach (var audio in audios)
        {
            InitializeTempoFromFirstTrack(audio.Bpm > 0 ? audio.Bpm : audio.ScannedBpm);
            bool firstTrack = !Arrangement.Lanes.Any(l => l.Clips.Count > 0);
            float sourceBpm = audio.Bpm > 0 ? audio.Bpm : audio.ScannedBpm;
            ClipTempoMode? mode = ClipTempoMode.Original;
            if (!firstTrack && float.IsFinite(sourceBpm) && sourceBpm > 0 && Math.Abs(sourceBpm - Arrangement.Bpm) > 0.1)
                mode = ChooseTempo(sourceBpm);
            if (mode == null) continue;
            PlaylistStretchSettings? stretchSettings = mode == ClipTempoMode.TimeStretch ? ConfigureImportStretch(audio) : null;
            if (mode == ClipTempoMode.TimeStretch && stretchSettings == null) continue;
            var source = new MixSource { Name = audio.Name, Data = (float[])audio.Data.Clone(), SampleRate = audio.SampleRate,
                Channels = audio.Channels, Bpm = sourceBpm };
            if (mode != ClipTempoMode.Original)
            {
                MixSource? prepared = null;
                status.Text = $"Importing: {mode} to {Arrangement.Bpm:0.##} BPM…";
                await RunAsync(async token => prepared = await MixRenderer.PrepareSourceAsync(source, stretchSettings?.TargetBpm ?? Arrangement.Bpm, mode.Value, token, stretchSettings));
                if (prepared == null || closing || released) break;
                source = prepared;
            }
            Arrangement.Sources.Add(source.Id, source);
            MixLane lane;
            if (row >= 0 && row < Arrangement.Lanes.Count) lane = Arrangement.Lanes[row];
            else { lane = new() { Name = source.Name }; Arrangement.Lanes.Add(lane); }
            Arrangement.AddClip(lane, source, start); row++;
        }
        Changed();
    }
    private void ShowMenu(Point point)
    {
        var menu = new ContextMenuStrip();
        void Item(string text, Action action, bool enabled = true) { var item = new ToolStripMenuItem(text) { Enabled = enabled }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Item("Undo (Ctrl+Z)", Undo, undo.Count > 0); Item("Redo (Ctrl+Y)", Redo, redo.Count > 0);
        Item("View Settings…", ShowViewSettings);
        if (selectionFirstRow >= 0 && selectionEnd > selectionStart)
        {
            Item("Set loop to selection", () => { Arrangement.LoopStart = selectionStart; Arrangement.LoopEnd = selectionEnd; Changed(); });
            Item("Delete selected audio range", DeleteSelectedRange);
            Item("Render selected range → Collection", async () => await ExportSelectedRangeAsync());
            Item("Clear selection", () => { ClearRangeSelection(); canvas.Invalidate(); });
        }
        if (Clip is { } clip)
        {
            Item("Clip properties…", () => EditProperties(clip));
            Item("Split at playhead (S)", Split);
            Item("Duplicate (Ctrl+D)", Duplicate);
            Item("Copy segment (Ctrl+C)", CopyClip);
            Item("Delete segment (Delete)", () => { Lane?.Clips.Remove(clip); Changed(); });
            foreach (var mode in Enum.GetValues<ClipTempoMode>()) Item("Tempo: " + mode, () => { clip.TempoMode = mode; Changed(); });
            Item("Set source BPM…", () =>
            {
                var source = Arrangement.Sources[clip.SourceId];
                string? value = Prompt("Source BPM (0 = unknown)", source.Bpm.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsed) && float.IsFinite(parsed) && parsed is >= 0 and <= 400)
                {
                    // Copy source metadata so undo does not mutate an older snapshot's asset.
                    var copy = new MixSource { Name = source.Name, Data = source.Data, SampleRate = source.SampleRate, Channels = source.Channels, Bpm = parsed };
                    Arrangement.Sources.Add(copy.Id, copy); clip.SourceId = copy.Id; Changed();
                }
            });
            Item("Align start to nearest beat", () => { double beat = 60 / Arrangement.Bpm; clip.Start = Math.Round(clip.Start / beat) * beat; Changed(); });
        }
        if (Lane is { } lane)
        {
            Item("Track properties (gain / solo / mute / crossfade)…", () => EditProperties(lane));
            Item(lane.Mute ? "Unmute track" : "Mute track", () => { lane.Mute = !lane.Mute; Changed(); });
            Item(lane.Solo ? "Unsolo track" : "Solo track", () => { lane.Solo = !lane.Solo; Changed(); });
            Item("Move track up", () => { int index = Arrangement.Lanes.IndexOf(lane); if (index > 0) { Arrangement.Lanes.RemoveAt(index); Arrangement.Lanes.Insert(index - 1, lane); Changed(); } });
            Item("Move track down", () => { int index = Arrangement.Lanes.IndexOf(lane); if (index + 1 < Arrangement.Lanes.Count) { Arrangement.Lanes.RemoveAt(index); Arrangement.Lanes.Insert(index + 1, lane); Changed(); } });
        }
        var remove = new ToolStripMenuItem("Remove Track");
        var combo = new ToolStripComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250 };
        foreach (var option in Arrangement.Lanes) combo.Items.Add(new LaneItem(option.Id, option.Name));
        combo.SelectedIndexChanged += (_, _) => { if (combo.SelectedItem is LaneItem item) { RemoveLane(item.Id); menu.Close(); } };
        remove.DropDownItems.Add(combo); menu.Items.Add(remove);
        Item("Add empty track", () => { Arrangement.Lanes.Add(new()); Changed(); });
        Item("Paste segment at playhead (Ctrl+V)", PasteClip, copiedClip != null);
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
        Item("Fit arrangement horizontally", () => { pixelsPerSecond = Math.Clamp((canvas.Width - Header - 24) / Math.Max(1, Arrangement.Duration), 8, 2000); Arrangement.Visual.PixelsPerSecond = pixelsPerSecond; scroll.Value = 0; Changed(); });
        Item("Rename mix…", RenameMix);
        menu.Closed += (_, _) => { if (!IsDisposed) BeginInvoke((Action)menu.Dispose); };
        menu.Show(canvas, point);
    }
    private sealed record LaneItem(Guid Id, string Name) { public override string ToString() => Name; }
    private void DeleteSelectedRange()
    {
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
    private async Task ExportSelectedRangeAsync()
    {
        if (selectionFirstRow < 0 || selectionEnd <= selectionStart || busy) return;
        var selected = new MixArrangement(); selected.Restore(Arrangement.Capture());
        foreach (var pair in Arrangement.Sources) selected.Sources.Add(pair.Key, pair.Value);
        selected.Lanes = selected.Lanes.Skip(selectionFirstRow).Take(selectionLastRow - selectionFirstRow + 1).ToList();
        double start = selectionStart, end = selectionEnd;
        await RunAsync(async token =>
        {
            using var whole = await MixRenderer.RenderAsync(selected, token);
            int begin = (int)Math.Min(whole.Data.Length, Math.Round(start * whole.SampleRate) * whole.Channels);
            int finish = (int)Math.Min(whole.Data.Length, Math.Round(end * whole.SampleRate) * whole.Channels);
            if (finish <= begin) return;
            var excerpt = new AudioObj { Name = Arrangement.Name + " - Selection", Data = whole.Data.AsSpan(begin, finish - begin).ToArray(),
                SampleRate = whole.SampleRate, Channels = whole.Channels, Bpm = whole.Bpm, Length = finish - begin,
                Duration = TimeSpan.FromSeconds((finish - begin) / (double)(whole.SampleRate * whole.Channels)), BitDepth = 32 };
            new AudioCollectionView([excerpt], excerpt.Name).Show();
        });
        selected.Dispose();
    }
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
        if (Clip != null) copiedClip = System.Text.Json.JsonSerializer.Deserialize<MixClip>(System.Text.Json.JsonSerializer.Serialize(Clip));
    }
    private void PasteClip()
    {
        if (copiedClip == null || !Arrangement.Sources.ContainsKey(copiedClip.SourceId)) return;
        var lane = Lane;
        if (lane == null) { lane = new MixLane { Name = copiedClip.Name }; Arrangement.Lanes.Add(lane); }
        var copy = System.Text.Json.JsonSerializer.Deserialize<MixClip>(System.Text.Json.JsonSerializer.Serialize(copiedClip))!;
        copy.Id = Guid.NewGuid(); copy.Start = Arrangement.SnapTime(caret, ModifierKeys.HasFlag(Keys.Shift)); lane.Clips.Add(copy);
        selectedClip = copy.Id; selectedLane = lane.Id; Changed();
    }
    private void RemoveLane(Guid id)
    {
        var lane = Arrangement.Lanes.FirstOrDefault(l => l.Id == id);
        if (lane == null) return;
        if (!joined.ContainsKey(id) && lane.Clips.FirstOrDefault() is { } clip && Arrangement.Sources.TryGetValue(clip.SourceId, out var source))
        {
            using var audio = new AudioObj { Name = source.Name, Data = source.Data, SampleRate = source.SampleRate, Channels = source.Channels,
                Bpm = source.Bpm, Length = source.Data.Length, Duration = TimeSpan.FromSeconds(source.Duration) };
            joined[id] = new TrackView(audio);
            joined[id].SetJoinedMix(this);
        }
        Arrangement.Lanes.Remove(lane); Changed();
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
    private bool EditProperties(object value)
    {
        string before = Arrangement.Capture();
        using var form = new Form { Text = "Arrangement properties", Size = new Size(430, 500), StartPosition = FormStartPosition.CenterParent };
        var grid = new PropertyGrid { Dock = DockStyle.Fill, SelectedObject = value, ToolbarVisible = false };
        var ok = new Button { Text = "Apply", Dock = DockStyle.Bottom, DialogResult = DialogResult.OK };
        form.Controls.Add(grid); form.Controls.Add(ok); form.AcceptButton = ok;
        if (form.ShowDialog(this) != DialogResult.OK) { Arrangement.Restore(before); canvas.Invalidate(); return false; }
        try { Arrangement.Validate(); Changed(); return true; } catch (Exception ex) { Arrangement.Restore(before); Error(ex); return false; }
    }
    private void EditLoop()
    {
        var range = new LoopRange { Start = Arrangement.LoopStart, End = Arrangement.LoopEnd };
        if (EditProperties(range) && double.IsFinite(range.End) && double.IsFinite(range.Start) && range.End > range.Start && range.Start >= 0)
        { Arrangement.LoopStart = range.Start; Arrangement.LoopEnd = range.End; Changed(); }
    }
    private sealed class LoopRange { public double Start { get; set; } public double End { get; set; } }
    private void RenameMix()
    {
        if (busy || closing) return;
        string? text = Prompt("Mix / project name", Arrangement.Name);
        if (string.IsNullOrWhiteSpace(text)) return;
        Arrangement.Name = text.Trim();
        Text = Arrangement.Name;
        Changed();
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
    private static string? Prompt(string title, string initial)
    {
        using var form = new Form { Text = title, ClientSize = new Size(350, 80), StartPosition = FormStartPosition.CenterParent };
        var text = new TextBox { Text = initial, Dock = DockStyle.Top }; var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom };
        form.Controls.Add(text); form.Controls.Add(ok); form.AcceptButton = ok; return form.ShowDialog() == DialogResult.OK ? text.Text : null;
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
        if (audio != null) { caret = audio.CurrentTime.TotalSeconds; _ = FinishPlaybackAsync(audio, playbackWork); }
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
        catch (Exception ex) { if (!IsDisposed && !closing) Error(ex); else LogManager.Log(ex); }
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
        string directory = DefaultProjectsDirectory;
        Directory.CreateDirectory(directory);
        string filename = string.Concat(Arrangement.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        using var dialog = new SaveFileDialog { Filter = "ModularAudience mix (*.mamix)|*.mamix", FileName = filename + ".mamix",
            InitialDirectory = directory, DefaultExt = "mamix", AddExtension = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        await RunAsync(async token => { await Arrangement.SaveAsync(dialog.FileName, token); dirty = false; status.Text = "Project saved with embedded Float32 audio."; });
    }
    internal async Task OpenProjectAsync()
    {
        if (busy) return;
        string directory = DefaultProjectsDirectory;
        Directory.CreateDirectory(directory);
        using var dialog = new OpenFileDialog { Filter = "ModularAudience mix (*.mamix)|*.mamix", InitialDirectory = directory };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        await RunAsync(async token =>
        {
            var loaded = await MixArrangement.LoadAsync(dialog.FileName, token);
            StopPlayback();
            foreach (var source in loaded.Sources) if (!Arrangement.Sources.ContainsKey(source.Key)) Arrangement.Sources.Add(source.Key, source.Value);
            Arrangement.Restore(loaded.Capture()); Changed(); Restore(Arrangement.Capture()); Text = Arrangement.Name; dirty = false;
        });
    }
    public static string DefaultProjectsDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        "ModularAudience", "_Projects");
    private async void CloseMix(object? sender, FormClosingEventArgs e)
    {
        if (allowClose) return;
        e.Cancel = true; if (closing) return;
        if (dirty && MessageBox.Show(this, "Close this mix? Unsaved arrangement changes will be lost.", "Close mix", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        closing = true; operation?.Cancel();
        tempoDebounce.Stop();
        liveUpdateTimer.Stop(); liveUpdateCancellation?.Cancel();
        try { await renderWork; await liveUpdateWork; } catch { }
        StopPlayback();
        foreach (var view in joined.Values.Distinct()) if (!view.IsDisposed && ReferenceEquals(view.JoinedMix, this)) { view.SetJoinedMix(null); view.Show(); }
        allowClose = true; Close();
    }
    private void Error(Exception ex) { status.Text = ex.Message; MessageBox.Show(this, ex.Message, "Mix editor", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    private void ReleaseResources()
    {
        if (released) return; released = true; open.Remove(this); operation?.Cancel(); StopPlayback(); clock.Dispose(); liveUpdateTimer.Dispose();
        liveUpdateCancellation?.Cancel();
        hueTimer.Dispose(); hueClock.Stop();
        tempoDebounce.Dispose();
        foreach (var view in joined.Values.Distinct())
            if (!view.IsDisposed && ReferenceEquals(view.JoinedMix, this)) { view.SetJoinedMix(null); if (!view.Disposing) view.Show(); }
        preparationCache.Dispose(); cachedMix?.Dispose(); cachedMix = null; wavePeaks.Clear(); Arrangement.Dispose();
    }
    protected override void Dispose(bool disposing) { if (disposing) ReleaseResources(); base.Dispose(disposing); }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (busy && keyData != Keys.Space && keyData != Keys.Escape) return base.ProcessCmdKey(ref msg, keyData);
        if (keyData == (Keys.Control | Keys.Z)) { Undo(); return true; }
        if (keyData == (Keys.Control | Keys.Y)) { Redo(); return true; }
        if (ModularAudience.Forms.TextInputFocus.IsActive()) return base.ProcessCmdKey(ref msg, keyData);
        if (keyData == (Keys.Control | Keys.D)) { Duplicate(); return true; }
        if (keyData == (Keys.Control | Keys.C)) { CopyClip(); return true; }
        if (keyData == (Keys.Control | Keys.V)) { PasteClip(); return true; }
        if (keyData == (Keys.Control | Keys.S)) { _ = SaveProjectAsync(); return true; }
        if (keyData == (Keys.Control | Keys.O)) { _ = OpenProjectAsync(); return true; }
        if (keyData == Keys.S) { Split(); return true; }
        if (keyData == Keys.Delete && selectionFirstRow >= 0 && selectionEnd > selectionStart) { DeleteSelectedRange(); return true; }
        if (keyData == Keys.Delete && Clip is { } clip) { Lane?.Clips.Remove(clip); Changed(); return true; }
        if (keyData == Keys.Space) { _ = TogglePlayAsync(); return true; }
        if (keyData == Keys.Escape && dragBefore != null)
        {
            Arrangement.Restore(dragBefore); dragBefore = null; dragMode = 0; draggedLane = null; reorderTarget = -1;
            canvas.Capture = false; RefreshPreparedMixer(); canvas.Invalidate(); return true;
        }
        if (keyData == Keys.Escape && selectionFirstRow >= 0) { ClearRangeSelection(); canvas.Invalidate(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    private sealed class Canvas : Control
    {
        public Canvas() { DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw | ControlStyles.Selectable, true); }
    }
}
