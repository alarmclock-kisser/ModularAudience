using ModularAudience.Audio;
using ModularAudience.Generators;

namespace ModularAudience.Forms.Modules.Dialogs;

internal sealed class TrackAutoRerollOptions
{
    public bool Enabled { get; set; }
    public int Loops { get; set; } = 4;
    public bool SaveEnsemble { get; set; }
    public bool SaveSolo { get; set; }
    public BreakbeatGenerateOptions Generate { get; set; } = new();
    public TrackAutoRerollOptions Clone() => new()
    {
        Enabled = Enabled, Loops = Loops, SaveEnsemble = SaveEnsemble, SaveSolo = SaveSolo, Generate = Generate.Clone()
    };
}

public partial class BreakbeatPatternEditorDialog
{
    private sealed class TrackAutoRerollState
    {
        public TrackAutoRerollOptions Options = new();
        public long CompletedLoops;
    }
    // Sample identity follows reorder/undo; a numeric row index does not.
    private readonly Dictionary<AudioObj, TrackAutoRerollState> autoRerolls = new(ReferenceEqualityComparer.Instance);
    private bool automaticGeneration;
    private long observedAutoLoopPass;
    private int[] autoLoopSelection = [];
    private Task autoSaveWork = Task.CompletedTask;
    private readonly Dictionary<AudioObj, AudioCollectionView> autoSoloCollections = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<AudioObj, float> autoSoloBpm = new(ReferenceEqualityComparer.Instance);
    private AudioCollectionView? autoEnsembleCollection;
    private double autoEnsembleBpm;
    private long autoVariation;
    private bool autoRerollClosing;
    private BreakbeatPatternGenerateDialog? trackGenerateDialog;
    private readonly HashSet<string> savedEnsembleHashes = [];

    private Rectangle AutoToggleBounds(int row)
    {
        Rectangle grid = this.GetGridBounds();
        float height = grid.Height / (float)Math.Max(1, this.samples.Count);
        return new Rectangle(4, grid.Top + (int)(row * height) + 1, 28, Math.Max(1, Math.Min(20, (int)height - 2)));
    }
    private Rectangle AutoCounterBounds(int row)
    {
        Rectangle toggle = this.AutoToggleBounds(row);
        return new(toggle.Right + 3, toggle.Top, 46, toggle.Height);
    }
    private Rectangle AutoRollBounds(int row)
    {
        Rectangle counter = this.AutoCounterBounds(row);
        return new(counter.Right + 3, counter.Top, 32, counter.Height);
    }
    private void PaintAutoReroll(Graphics graphics, int row)
    {
        var bounds = this.AutoToggleBounds(row);
        this.autoRerolls.TryGetValue(this.samples[row], out var state);
        bool on = state?.Options.Enabled == true;
        using var brush = new SolidBrush(on ? Color.FromArgb(42, 133, 91) : Color.FromArgb(65, 69, 76));
        graphics.FillRectangle(brush, bounds);
        TextRenderer.DrawText(graphics, on ? "On" : "Off", SystemFonts.MessageBoxFont, bounds, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        var label = this.AutoCounterBounds(row);
        string text = state == null ? "Auto" : $"{state.CompletedLoops} / {state.Options.Loops}";
        TextRenderer.DrawText(graphics, text, SystemFonts.MessageBoxFont, label, Color.LightGray,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        Rectangle roll = this.AutoRollBounds(row);
        using var rollBrush = new SolidBrush(Color.FromArgb(65, 69, 76));
        graphics.FillRectangle(rollBrush, roll);
        TextRenderer.DrawText(graphics, "Roll", SystemFonts.MessageBoxFont, roll, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        this.PaintTrackButtons(graphics, row);
    }
    private bool TryClickAutoReroll(MouseEventArgs e)
    {
        for (int row = 0; row < this.samples.Count; row++)
        {
            var bounds = this.AutoToggleBounds(row);
            if (!Rectangle.Union(bounds, this.AutoRollBounds(row)).Contains(e.Location)) continue;
            AudioObj sample = this.samples[row];
            if (!this.autoRerolls.TryGetValue(sample, out var state))
                this.autoRerolls[sample] = state = new();
            if (e.Button == MouseButtons.Left && this.AutoCounterBounds(row).Contains(e.Location))
            {
                int currentLoops = state.Options.Loops;
                using var dialog = new Form { Text = "Auto-reroll loops", ClientSize = new Size(260, 100), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
                var label = new Label { Text = "Loops (N):", Location = new Point(12, 14), AutoSize = true };
                var num = new NumericUpDown { Location = new Point(100, 12), Width = 100, Minimum = 1, Maximum = 10000, Value = currentLoops, Increment = 1 };
                var ok = new Button { Text = "OK", Location = new Point(100, 56), Size = new Size(60, 26), DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Cancel", Location = new Point(170, 56), Size = new Size(60, 26), DialogResult = DialogResult.Cancel };
                dialog.Controls.AddRange(new Control[] { label, num, ok, cancel });
                dialog.AcceptButton = null; dialog.CancelButton = cancel;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    state.Options.Loops = (int)num.Value;
                    state.CompletedLoops = 0;
                    this.pictureBox_pattern.Invalidate();
                }
                return true;
            }
            if (e.Button == MouseButtons.Left && this.AutoRollBounds(row).Contains(e.Location))
            {
                bool advanceCounter = (ModifierKeys & Keys.Control) == 0;
                this.RollAutoTrack(row, advanceCounter);
                return true;
            }
            if (e.Button == MouseButtons.Left && bounds.Contains(e.Location) && state.Options.Enabled)
                state.Options.Enabled = false;
            else
            {
                if (this.trackGenerateDialog is { IsDisposed: false } existing)
                {
                    existing.Activate();
                    return true;
                }
                var dialog = new BreakbeatPatternGenerateDialog(state.Options.Generate.Clone(), state.Options, this.currentResolution);
                dialog.SetOtherTrackOptions(Enumerable.Range(0, this.samples.Count)
                    .Where(other => other != row && this.autoRerolls.ContainsKey(this.samples[other]))
                    .Select(other => (other + 1,
                        other < this.rowLabels.Count ? this.rowLabels[other] : this.samples[other].Name,
                        this.autoRerolls[this.samples[other]].Options)));
                this.trackGenerateDialog = dialog;
                // Owned modeless window: only this editor is disabled, not every UI-thread window as with ShowDialog.
                dialog.FormClosed += (_, _) =>
                {
                    this.trackGenerateDialog = null;
                    if (this.IsDisposed || this.Disposing || this.autoRerollClosing) return;
                    this.Enabled = true;
                    if (dialog.DialogResult == DialogResult.OK && this.samples.Contains(sample))
                    {
                        this.ConfirmTrackGenerateOptions(this.samples.IndexOf(sample), dialog.AutoOptions, dialog.ApplyForAll);
                        this.pictureBox_pattern.Invalidate();
                        this.CommitHistoryAction();
                    }
                    this.Activate();
                };
                dialog.StartPosition = FormStartPosition.Manual;
                dialog.Location = new Point(
                    Math.Max(Screen.FromControl(this).WorkingArea.Left, this.Left + (this.Width - dialog.Width) / 2),
                    Math.Max(Screen.FromControl(this).WorkingArea.Top, this.Top + (this.Height - dialog.Height) / 2));
                dialog.Show(this);
                this.Enabled = false;
                return true;
            }
            this.pictureBox_pattern.Invalidate();
            this.CommitHistoryAction();
            return true;
        }
        return false;
    }
    private int[] AutoRerollSections()
    {
        int[] sections = this.GetLoopSectionsSnapshot();
        return sections.Length > 0 ? sections : Enumerable.Range(0, this.bars * 4).ToArray();
    }
    private void ConfirmTrackGenerateOptions(int row, TrackAutoRerollOptions options, bool applyForAll)
    {
        int[] rows = applyForAll ? Enumerable.Range(0, this.samples.Count).ToArray() : [row];
        foreach (int target in rows)
        {
            if (!this.autoRerolls.TryGetValue(this.samples[target], out var state))
                this.autoRerolls[this.samples[target]] = state = new();
            state.Options = options.Clone();
            state.Options.Enabled = true;
            state.CompletedLoops = 0;
        }
        // Confirming options is an explicit roll, including already populated tracks.
        // Generate together so ensemble autosaves see one final shared state.
        this.ApplyTrackAutoRerolls(rows, this.AutoRerollSections());
    }
    private void ActivateAutoReroll(int row, TrackAutoRerollOptions options)
    {
        AudioObj sample = this.samples[row];
        if (!this.autoRerolls.TryGetValue(sample, out var state)) this.autoRerolls[sample] = state = new();
        state.Options = options.Clone();
        state.CompletedLoops = 0;
        int[] sections = this.AutoRerollSections();
        int quarterTicks = BreakbeatGenerator_V2.PatternTicksPerBar / 4;
        if (state.Options.Enabled && !this.notes.Any(n => n.TrackIndex == row && sections.Any(section =>
            n.StartTick < (section + 1L) * quarterTicks && (long)n.StartTick + n.DurationTicks > section * quarterTicks)))
            this.ApplyTrackAutoRerolls([row], sections);
    }
    private void ActivateAutoRerollForAll(TrackAutoRerollOptions options)
    {
        int[] sections = this.AutoRerollSections();
        int quarterTicks = BreakbeatGenerator_V2.PatternTicksPerBar / 4;
        var emptyRows = new List<int>();
        for (int row = 0; row < this.samples.Count; row++)
        {
            if (!this.autoRerolls.TryGetValue(this.samples[row], out var state))
                this.autoRerolls[this.samples[row]] = state = new();
            state.Options = options.Clone();
            state.Options.Enabled = true;
            state.CompletedLoops = 0;
            if (!this.notes.Any(n => n.TrackIndex == row && sections.Any(section =>
                n.StartTick < (section + 1L) * quarterTicks && (long)n.StartTick + n.DurationTicks > section * quarterTicks)))
                emptyRows.Add(row);
        }
        // Fill empty tracks together so their initial ensemble save is one shared final state.
        if (emptyRows.Count > 0) this.ApplyTrackAutoRerolls(emptyRows, sections);
        this.CommitHistoryAction();
        this.pictureBox_pattern.Invalidate();
    }
    private void RollAutoTrack(int row, bool advanceCounter = true)
    {
        if (this.saveInProgress || this.autoRerollClosing) return;
        if (!this.autoRerolls.TryGetValue(this.samples[row], out var state))
            this.autoRerolls[this.samples[row]] = state = new();
        if (advanceCounter && state.Options.Enabled)
            state.CompletedLoops = (state.CompletedLoops + 1) % Math.Max(1, state.Options.Loops);
        this.ApplyTrackAutoRerolls([row], this.AutoRerollSections());
        this.pictureBox_pattern.Invalidate();
    }
    private void ResetAutoRerollCounters()
    {
        foreach (var state in this.autoRerolls.Values) state.CompletedLoops = 0;
        this.pictureBox_pattern.Invalidate();
    }
    private void PrepareAutoRerollPlayback()
    {
        this.observedAutoLoopPass = 0;
        int[] sections = this.GetLoopSectionsSnapshot();
        if (!sections.SequenceEqual(this.autoLoopSelection)) this.ResetAutoRerollCounters();
        this.autoLoopSelection = sections;
    }
    private void ResetAutoRerollClock()
    {
        this.PrepareAutoRerollPlayback();
        this.ResetAutoRerollCounters();
    }
    private void StopAutoRerollPlayback(bool resetCounters)
    {
        if (resetCounters) this.ResetAutoRerollCounters();
    }
    private void ObserveAutoRerollLoops(long loopPass)
    {
        if (this.autoRerollClosing) return;
        int[] sections = this.GetLoopSectionsSnapshot();
        if (!sections.SequenceEqual(this.autoLoopSelection))
        {
            this.autoLoopSelection = sections;
            this.observedAutoLoopPass = loopPass;
            foreach (var state in this.autoRerolls.Values) state.CompletedLoops = 0;
            return;
        }
        long elapsed = loopPass - this.observedAutoLoopPass;
        if (elapsed <= 0) return;
        this.observedAutoLoopPass = loopPass;
        if (sections.Length == 0) return;
        this.AdvanceAutoRerollCounters(elapsed, sections);
    }
    private void CompleteNonLoopHear(bool completed)
    {
        if (!completed || this.autoRerollClosing || this.IsDisposed || this.selectedLoopSections.Count > 0) return;
        this.AdvanceAutoRerollCounters(1, Enumerable.Range(0, this.bars * 4).ToArray());
    }
    private void DisableAutoRerollForManualNotes(IEnumerable<BreakbeatPatternNote> placed)
    {
        if (this.automaticGeneration) return;
        foreach (int row in placed.Select(n => n.TrackIndex).Distinct())
            if (row >= 0 && row < this.samples.Count && this.autoRerolls.TryGetValue(this.samples[row], out var state))
                state.Options.Enabled = false;
        this.pictureBox_pattern.Invalidate();
    }
    private void button_roll_Click(object? sender, EventArgs e)
    {
        if (this.saveInProgress || this.autoRerollClosing) return;
        int[] rows = Enumerable.Range(0, this.samples.Count)
            .Where(row => this.autoRerolls.TryGetValue(this.samples[row], out var state) && state.Options.Enabled)
            .ToArray();
        if (rows.Length == 0) return;
        foreach (int row in rows) this.autoRerolls[this.samples[row]].CompletedLoops = 0;
        this.ApplyTrackAutoRerolls(rows, this.AutoRerollSections());
        this.pictureBox_pattern.Invalidate();
    }
    private void AdvanceAutoRerollCounters(long elapsed, int[] sections)
    {
        var due = new List<int>();
        for (int row = 0; row < this.samples.Count; row++)
        {
            if (!this.autoRerolls.TryGetValue(this.samples[row], out var state) || !state.Options.Enabled) continue;
            state.CompletedLoops += elapsed;
            if (state.CompletedLoops >= Math.Max(1, state.Options.Loops))
            {
                state.CompletedLoops %= Math.Max(1, state.Options.Loops);
                due.Add(row);
            }
        }
        if (due.Count > 0) this.ApplyTrackAutoRerolls(due, sections);
        this.pictureBox_pattern.Invalidate();
    }
    private void ApplyTrackAutoRerolls(IReadOnlyList<int> rows, int[] sections)
    {
        int quarterTicks = BreakbeatGenerator_V2.PatternTicksPerBar / 4;
        var selected = sections.ToHashSet();
        var before = this.notes.ToArray();
        this.automaticGeneration = true;
        try
        {
            foreach (int row in rows)
            {
                var options = this.autoRerolls[this.samples[row]].Options.Generate.Clone();
                options.Interleaved = false;
                options.Seed = options.Seed == 0 ? 0 : unchecked(options.Seed + (int)++this.autoVariation);
                // Keep notes in unselected quarters, even if they overlap a selected boundary.
                this.notes.RemoveAll(n => n.TrackIndex == row && selected.Contains(n.StartTick / quarterTicks));
                foreach (var group in ContiguousSections(sections))
                {
                    int start = (int)Math.Ceiling(group.Start * quarterTicks / (double)this.GetTicksPerStep());
                    int end = (group.End + 1) * quarterTicks / this.GetTicksPerStep() - 1;
                    if (end >= start)
                    {
                        var previous = this.notes.ToHashSet(ReferenceEqualityComparer.Instance);
                        this.GeneratePatternInArea(row, row, start, end, options);
                        int endTick = (group.End + 1) * quarterTicks;
                        for (int i = 0; i < this.notes.Count; i++)
                            if (!previous.Contains(this.notes[i]) && (long)this.notes[i].StartTick + this.notes[i].DurationTicks > endTick)
                                this.notes[i] = this.notes[i] with { DurationTicks = endTick - this.notes[i].StartTick, HardResized = true };
                    }
                }
            }
        }
        finally { this.automaticGeneration = false; }
        this.selectedNotes.RemoveWhere(n => !this.notes.Contains(n));
        this.deadGroupNotes.Clear();
        this.RegisterPatternNotesChanged(before.Concat(this.notes).Select(n => (BreakbeatPatternNote?)n).ToArray());
        this.CommitHistoryAction();
        this.QueueAutoRerollSaves(rows, sections);
    }
    private static IEnumerable<(int Start, int End)> ContiguousSections(int[] sections)
    {
        int[] ordered = sections.Distinct().Order().ToArray();
        if (ordered.Length == 0) yield break;
        int start = ordered[0], end = start;
        foreach (int section in ordered.Skip(1))
            if (section == end + 1) end = section;
            else { yield return (start, end); start = end = section; }
        yield return (start, end);
    }
    private void QueueAutoRerollSaves(IReadOnlyList<int> rows, int[] sections)
    {
        int[] solos = rows.Where(r => this.autoRerolls[this.samples[r]].Options.SaveSolo).ToArray();
        bool ensemble = rows.Any(r => this.autoRerolls[this.samples[r]].Options.SaveEnsemble);
        if (!ensemble && solos.Length == 0) return;
        // Capture the final shared arrangement once AFTER all due tracks rerolled.
        // Only one ensemble is queued, regardless of how many tracks requested it.
        var notes = this.ApplyTrackAudibility(this.GetNotesWithoutCoveredRetriggers());
        var samples = this.samples.Select(CloneSampleWithMetadata).ToArray();
        var trackIdentities = this.samples.ToArray();
        var settings = this.trackSettings.Select(NormalizeTrackSettings).ToArray();
        string[] labels = this.rowLabels.ToArray();
        int bars = this.bars, resolution = this.currentResolution;
        float bpm = (float)this.Bpm, swing = this.swing;
        long variation = ++this.autoVariation;
        Task previous = this.autoSaveWork;
        this.autoSaveWork = SaveAsync();
        async Task SaveAsync()
        {
            try
            {
                await previous;
                foreach (int row in solos) await RenderAsync(notes.Where(n => n.TrackIndex == row).ToArray(), $"{labels[row]} — Solo {variation}", row);
                if (ensemble) await RenderAsync(notes, $"Ensemble {variation}", null);
            }
            catch (Exception error)
            {
                LogManager.Log("Auto-reroll save failed."); LogManager.Log(error);
                if (!this.IsDisposed && !this.pictureBox_pattern.IsDisposed)
                    this.toolTip_pattern.Show("Auto-reroll save failed: " + error.Message, this.pictureBox_pattern, 8, 8, 8000);
            }
            finally { foreach (var sample in samples) sample.Dispose(); }
        }
        async Task RenderAsync(BreakbeatPatternNote[] renderNotes, string name, int? soloRow)
        {
            bool isEnsemble = soloRow == null;
            AudioObj? rendered = await BreakbeatGenerator_V2.RenderPatternNotesAsync(renderNotes, samples, bars, bpm,
                resolution, swing, name, trackSettings: settings);
            if (rendered == null) return;
            try
            {
                AudioObj excerpt = BreakbeatGenerator_V2.ExtractPatternQuarters(rendered, sections, bpm);
                rendered.Dispose(); rendered = excerpt; rendered.Rename(name);
                if (this.IsDisposed || this.Disposing) return;
                string? hash = null;
                if (isEnsemble)
                {
                    if (this.autoEnsembleCollection == null || this.autoEnsembleCollection.IsDisposed || this.autoEnsembleBpm != bpm)
                    {
                        this.savedEnsembleHashes.Clear();
                        this.autoEnsembleCollection = null;
                    }
                    var bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(rendered.Data.AsSpan());
                    hash = $"{rendered.SampleRate}:{rendered.Channels}:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
                    if (this.savedEnsembleHashes.Contains(hash)) return;
                }
                AudioObj? soloTrack = isEnsemble ? null : trackIdentities[soloRow!.Value];
                bool soloBpmChanged = !isEnsemble && this.autoSoloBpm.TryGetValue(soloTrack!, out float existingSoloBpm) && existingSoloBpm != bpm;
                AudioCollectionView? collection = isEnsemble ? this.autoEnsembleCollection
                    : this.autoSoloCollections.GetValueOrDefault(soloTrack!);
                if (collection == null || collection.IsDisposed || soloBpmChanged)
                {
                    collection = new AudioCollectionView([]);
                    collection.Rename(isEnsemble
                        ? $"Auto-Reroll Ensembles [{bpm:F1}]"
                        : $"Auto-Reroll Solos — {labels[soloRow!.Value]} [{bpm:F1}]");
                    if (isEnsemble) { this.autoEnsembleCollection = collection; this.autoEnsembleBpm = bpm; }
                    else
                    {
                        this.autoSoloCollections[soloTrack!] = collection;
                        this.autoSoloBpm[soloTrack!] = bpm;
                    }
                }
                collection.AudioC.Audios.Add(rendered); rendered = null;
                if (hash != null) this.savedEnsembleHashes.Add(hash);
                collection.Show();
            }
            finally { rendered?.Dispose(); }
        }
    }

}
