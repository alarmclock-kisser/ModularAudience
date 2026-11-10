using ModularAudience.Audio.Arrangement;

namespace ModularAudience.Forms.Modules;

public sealed partial class MixTrackView
{
    private readonly CheckBox autoSave = new() { Text = "Auto-save", AutoSize = true, Enabled = false };
    private readonly System.Windows.Forms.Timer projectSaveTimer = new() { Interval = 300 };
    private string? projectPath;
    private Task projectSaveWork = Task.CompletedTask;
    private (string Path, string State, MixSource[] Sources)? pendingProjectSave;
    private int layoutLaneCount;
    private double manualCaret;

    private void AddEmptyTrack()
    {
        Arrangement.Lanes.Add(new() { Name = NextLaneName() }); Changed();
    }
    private void EnsureTrackLayout()
    {
        bool laneCountChanged = Arrangement.Lanes.Count != layoutLaneCount;
        if (Arrangement.Visual.AutoTrackHeight && !laneCountChanged) ApplyAutoTrackHeight();
        UpdateVerticalScrollbar();
        if (!laneCountChanged) return;
        layoutLaneCount = Arrangement.Lanes.Count;
        if (layoutLaneCount == 0) return;
        // All lanes use one shared pixel height. Grow the client area on insertion;
        // if the screen is full, use the existing vertical scrollbar, never squeeze the new lane.
        int extra = ClientSize.Height - canvas.ClientSize.Height;
        int requested = extra + Ruler + layoutLaneCount * LaneHeight + 4;
        int maximum = Screen.FromControl(this).WorkingArea.Height - (Height - ClientSize.Height) - 25;
        int maximumClientHeight = Math.Max(MinimumSize.Height, maximum);
        if (Arrangement.Visual.AutoTrackHeight && requested > maximumClientHeight)
        {
            int available = Math.Max(0, maximumClientHeight - extra - Ruler - 4);
            LaneHeight = Math.Clamp(available / layoutLaneCount, 32, 600);
            preciseLaneHeight = LaneHeight;
            requested = extra + Ruler + layoutLaneCount * LaneHeight + 4;
        }
        automaticSizing = true;
        try { if (requested > ClientSize.Height) ClientSize = new(ClientSize.Width, Math.Min(requested, maximumClientHeight)); }
        finally { previousClientHeight = ClientSize.Height; automaticSizing = false; }
        UpdateVerticalScrollbar();
        canvas.Invalidate();
    }
    private void RestoreViewport()
    {
        UpdateVerticalScrollbar();
        int maximumValue = Math.Max(0, vertical.Maximum - vertical.LargeChange + 1);
        vertical.Value = Arrangement.Visual.AutoTrackHeight ? 0 : Math.Clamp(Arrangement.Visual.FirstVisibleTrack, 0, maximumValue);
        this.UpdateTimelineScrollBounds();
        int value = (int)Math.Clamp(Arrangement.Visual.HorizontalScrollSeconds * 100, scroll.Minimum, int.MaxValue - 200);
        scroll.Maximum = Math.Max(scroll.Maximum, value + scroll.LargeChange);
        scroll.Value = value;
    }
    private void JumpToManualCaret(bool reset)
    {
        if (reset) manualCaret = 0;
        caret = manualCaret;
        playback?.SetPosition((long)(caret * playback.SampleRate));
        double width = Math.Max(1, canvas.Width - Header) / pixelsPerSecond;
        if (caret < ViewStart || caret >= ViewStart + width)
            scroll.Value = Math.Clamp((int)(caret * 100), scroll.Minimum, scroll.Maximum - scroll.LargeChange + 1);
        canvas.Invalidate();
    }
    private void QueueProjectSave(bool force = false)
    {
        if (restoring || released || projectPath == null || (!Arrangement.AutoSave && !force)) return;
        string state = Arrangement.CaptureProject();
        if (pendingProjectSave is { } pending && pending.Path == projectPath && pending.State == state) return;
        pendingProjectSave = (projectPath, state, Arrangement.ProjectSources().ToArray());
        projectSaveTimer.Stop(); projectSaveTimer.Start();
    }
    private async Task FlushProjectSaveAsync()
    {
        if (!projectSaveWork.IsCompleted) { try { await projectSaveWork; } catch { } if (pendingProjectSave != null) await FlushProjectSaveAsync(); return; }
        if (pendingProjectSave is not { } save) return;
        pendingProjectSave = null;
        projectSaveWork = MixProjectFile.SaveSnapshotAsync(save.Path, save.State, save.Sources);
        try
        {
            await projectSaveWork;
            if (!released && projectPath == save.Path && Arrangement.CaptureProject() == save.State)
            { dirty = false; status.Text = "Auto-saved project metadata; existing audio assets reused."; }
        }
        catch (Exception error) { if (!released) status.Text = "Auto-save failed: " + error.Message; }
        if (pendingProjectSave != null && !released) { projectSaveTimer.Stop(); projectSaveTimer.Start(); }
    }
    private async Task SaveToPathAsync(string path, CancellationToken token = default)
    {
        projectSaveTimer.Stop(); await FlushProjectSaveAsync();
        // Saving "with a name" makes that name the mix's current project name, so
        // it shows up in the window title (and in the saved project data).
        string baseName = Path.GetFileNameWithoutExtension(path);
        if (!string.IsNullOrWhiteSpace(baseName)) Arrangement.Name = baseName.Trim();
        string state = Arrangement.CaptureProject();
        var sources = Arrangement.ProjectSources();
        await MixProjectFile.SaveSnapshotAsync(path, state, sources, token);
        projectPath = path; autoSave.Enabled = true;
        Text = Arrangement.Name;
        committedProject = state;
        dirty = false;
        status.Text = "Project saved — journal metadata and reusable Float32 assets.";
        if (dirty) QueueProjectSave();
    }
    private async Task LoadFromPathAsync(string path, CancellationToken token = default)
    {
        projectSaveTimer.Stop(); await FlushProjectSaveAsync();
        var loaded = await MixArrangement.LoadAsync(path, token);
        StopPlayback(); CancelPastePreview(); clipboardHistory.Clear();
        Arrangement.Sources.Clear(); wavePeaks.Clear();
        foreach (var source in loaded.Sources) Arrangement.Sources[source.Key] = source.Value;
        // Project loading begins a fresh undo session; assets from the previous project
        // cannot be referenced by later undo operations.
        undo.Clear(); redo.Clear(); projectPath = null;
        Restore(loaded.Capture()); committed = MixAudioHistory.Capture(Arrangement); committedAudioKey = MixAudioHistory.Key(Arrangement);
        undo.Clear(); redo.Clear(); projectPath = path; autoSave.Enabled = true; dirty = false;
        loaded.Dispose(); status.Text = "Project loaded.";
    }
}
