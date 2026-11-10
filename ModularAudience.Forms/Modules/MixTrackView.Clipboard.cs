using System.Text.Json;
using ModularAudience.Audio.Arrangement;

namespace ModularAudience.Forms.Modules;

public sealed partial class MixTrackView
{
    private sealed record ClipboardPart(int Row, MixClip Clip);
    private sealed record ClipboardEntry(List<ClipboardPart> Parts, DateTime Time, string Key);
    private readonly List<ClipboardEntry> clipboardHistory = [];
    private ClipboardEntry? pasteEntry;
    private bool pastePreview, pasteHoldTracking, pasteKeyDown;
    private readonly System.Windows.Forms.Timer pasteHoldTimer = new() { Interval = 250 };
    private Point lastPointer = new(Header, Ruler);
    private readonly HashSet<Guid> fullSelectedClips = [], additiveSelection = [];
    private bool selectingWhole, selectionAdditive;
    private static MixClip CloneClip(MixClip value) => JsonSerializer.Deserialize<MixClip>(JsonSerializer.Serialize(value))!;

    private List<ClipboardPart> SelectedParts()
    {
        var result = new List<ClipboardPart>();
        for (int row = 0; row < Arrangement.Lanes.Count; row++)
            foreach (var clip in Arrangement.Lanes[row].Clips)
            {
                if (!selectedClips.Contains(clip.Id) && (selectedClips.Count > 0 || clip.Id != selectedClip)) continue;
                var copy = CloneClip(clip);
                if (!fullSelectedClips.Contains(clip.Id) && selectionFirstRow >= 0 && selectionEnd > selectionStart)
                {
                    double start = Math.Max(clip.Start, selectionStart), end = Math.Min(clip.Start + Arrangement.ClipDuration(clip), selectionEnd);
                    if (end <= start) continue;
                    copy.SourceOffset += (start - clip.Start) * Arrangement.Rate(clip);
                    copy.SourceDuration = (end - start) * Arrangement.Rate(clip); copy.Start = start;
                    if (start > clip.Start) copy.FadeIn = 0;
                    if (end < clip.Start + Arrangement.ClipDuration(clip)) copy.FadeOut = 0;
                }
                result.Add(new(row, copy));
            }
        return result;
    }
    private void CutSelection()
    {
        CopySelection();
        DeleteSelection();
    }

    private void CopySelection()
    {
        var parts = SelectedParts(); if (parts.Count == 0) return;
        int first = parts.Min(p => p.Row); double start = parts.Min(p => p.Clip.Start);
        parts = parts.Select(p => { var c = p.Clip; c.Start -= start; c.Id = Guid.Empty; return new ClipboardPart(p.Row - first, c); }).ToList();
        string key = JsonSerializer.Serialize(parts);
        clipboardHistory.RemoveAll(e => e.Key == key);
        clipboardHistory.Insert(0, new(parts, DateTime.Now, key));
        if (clipboardHistory.Count > 50) clipboardHistory.RemoveAt(50);
        copiedClip = parts[0].Clip; status.Text = $"Copied {parts.Count} clips · {clipboardHistory.Count} clipboard entries.";
    }
    private void StartPastePreview()
    {
        if (clipboardHistory.Count == 0) return;
        pasteEntry = clipboardHistory[0]; pastePreview = true; canvas.Cursor = Cursors.Cross; canvas.Focus(); canvas.Invalidate();
        status.Text = "Paste preview: move pointer, left-click to place, Esc / right-click to cancel. Shift = snap.";
    }
    private void CancelPastePreview() { pastePreview = false; pasteEntry = null; canvas.Invalidate(); }
    private (int Row, double Time) PasteAnchor() => (Math.Max(0, RowAt(lastPointer.Y)), Arrangement.SnapTime(XTime(lastPointer.X), ModifierKeys.HasFlag(Keys.Shift)));
    private void PlacePastePreview()
    {
        if (!pastePreview || pasteEntry == null || busy) return;
        var (row, time) = PasteAnchor();
        if (pasteEntry.Parts.Any(p => !Arrangement.Sources.ContainsKey(p.Clip.SourceId))) return;
        while (Arrangement.Lanes.Count <= row + pasteEntry.Parts.Max(p => p.Row)) Arrangement.Lanes.Add(new() { Name = NextLaneName() });
        ClearRangeSelection(); selectedClip = null;
        foreach (var part in pasteEntry.Parts)
        {
            var copy = CloneClip(part.Clip); copy.Id = Guid.NewGuid(); copy.Start += time;
            Arrangement.Lanes[row + part.Row].Clips.Add(copy); selectedClips.Add(copy.Id); fullSelectedClips.Add(copy.Id);
        }
        selectedLane = Arrangement.Lanes[row].Id; CancelPastePreview(); Changed(); EnsureTrackLayout();
    }
    private void PaintPastePreview(Graphics g)
    {
        if (!pastePreview || pasteEntry == null) return;
        var (row, time) = PasteAnchor();
        var state = g.Save(); g.SetClip(new Rectangle(Header, Ruler, Math.Max(0, canvas.Width - Header), Math.Max(0, canvas.Height - Ruler)));
        foreach (var part in pasteEntry.Parts)
        {
            var clip = CloneClip(part.Clip); clip.Start += time;
            Rectangle bounds = ClipRect(row + part.Row, clip);
            using var brush = new SolidBrush(Color.FromArgb(95, Color.LightSkyBlue)); g.FillRectangle(brush, bounds);
            using var pen = new Pen(Color.White) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash }; g.DrawRectangle(pen, bounds);
            if (Arrangement.Sources.TryGetValue(clip.SourceId, out var source)) DrawWave(g, bounds, clip, source);
            TextRenderer.DrawText(g, clip.Name, Font, bounds, Color.White, TextFormatFlags.EndEllipsis);
        }
        g.Restore(state);
    }
    private void BeginPasteHold()
    {
        if (pasteKeyDown) return;
        pasteKeyDown = true; pasteHoldTracking = true; pasteHoldTimer.Start();
    }
    private void PasteKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.V) return;
        pasteKeyDown = false;
        if (!pasteHoldTracking) return;
        pasteHoldTracking = false; pasteHoldTimer.Stop(); StartPastePreview(); e.Handled = true;
    }
    private async void ShowClipboardHistory()
    {
        pasteHoldTimer.Stop(); if (!pasteHoldTracking) return; pasteHoldTracking = false;
        if (clipboardHistory.Count == 0) return;
        using var dialog = new Form { Text = "Clipboard History (short Ctrl+V = top entry)", ClientSize = new Size(480, 390),
            StartPosition = FormStartPosition.Manual, ShowInTaskbar = false, KeyPreview = true };
        var list = new ListBox { Dock = DockStyle.Fill, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 70, IntegralHeight = false };
        list.Items.AddRange(clipboardHistory.Cast<object>().ToArray()); list.SelectedIndex = 0;
        list.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return; e.DrawBackground(); var entry = (ClipboardEntry)list.Items[e.Index];
            TextRenderer.DrawText(e.Graphics, $"{entry.Parts.Count} clips · {entry.Parts.Select(p => p.Row).Distinct().Count()} tracks · {entry.Time:HH:mm:ss}",
                Font, new Point(e.Bounds.Left + 5, e.Bounds.Top + 3), e.ForeColor);
            double length = entry.Parts.Max(p => p.Clip.Start + Arrangement.ClipDuration(p.Clip)); int rows = entry.Parts.Max(p => p.Row) + 1;
            foreach (var part in entry.Parts)
            {
                var rect = new Rectangle(e.Bounds.Left + 6 + (int)(part.Clip.Start / Math.Max(0.001, length) * (e.Bounds.Width - 15)),
                    e.Bounds.Top + 25 + part.Row * 38 / rows, Math.Max(2, (int)(Arrangement.ClipDuration(part.Clip) / Math.Max(0.001, length) * (e.Bounds.Width - 15))), Math.Max(2, 34 / rows));
                e.Graphics.FillRectangle(Brushes.SteelBlue, rect);
                if (Arrangement.Sources.TryGetValue(part.Clip.SourceId, out var source))
                {
                    for (int x = 0; x < rect.Width; x += 2)
                    {
                        int frame = Math.Clamp((int)((part.Clip.SourceOffset + part.Clip.SourceDuration * x / Math.Max(1, rect.Width)) * source.SampleRate), 0, Math.Max(0, source.Data.Length / source.Channels - 1));
                        if (source.Data.Length == 0) continue;
                        float amp = Math.Min(1, Math.Abs(source.Data[frame * source.Channels]));
                        e.Graphics.DrawLine(Pens.LightCyan, rect.X + x, rect.Top + rect.Height * (0.5f - amp * 0.45f), rect.X + x, rect.Top + rect.Height * (0.5f + amp * 0.45f));
                    }
                }
            }
            e.DrawFocusRectangle();
        };
        var hint = new Label { Text = "Up/Down to cycle · Enter or click selects · Esc cancels", Dock = DockStyle.Top, Height = 25 };
        dialog.Controls.Add(list); dialog.Controls.Add(hint);
        void Accept() { if (list.SelectedIndex >= 0) { dialog.DialogResult = DialogResult.OK; dialog.Close(); } }
        list.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left && list.IndexFromPoint(e.Location) >= 0) Accept(); };
        dialog.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { Accept(); e.Handled = true; } else if (e.KeyCode == Keys.Escape) dialog.Close(); };
        Rectangle work = Screen.GetWorkingArea(this); Point pointer = Cursor.Position;
        dialog.Location = new(Math.Clamp(pointer.X - 60, work.Left, Math.Max(work.Left, work.Right - dialog.Width)), Math.Clamp(pointer.Y - 60, work.Top, Math.Max(work.Top, work.Bottom - dialog.Height)));
        if (await this.ShowMixDialogAsync(dialog) != DialogResult.OK || list.SelectedItem is not ClipboardEntry chosen) return;
        clipboardHistory.Remove(chosen); clipboardHistory.Insert(0, chosen); StartPastePreview();
    }
    private void SelectWholeClip(Guid id, bool additive)
    {
        if (!additive) ClearRangeSelection();
        // Existing partial range members become complete clips before extending the selection.
        foreach (var selected in selectedClips) fullSelectedClips.Add(selected);
        selectionFirstRow = selectionLastRow = -1; selectionStart = selectionEnd = 0;
        selectedClips.Add(id); fullSelectedClips.Add(id); selectedClip = id; canvas.Invalidate();
    }
    private void DeleteSelection()
    {
        if (selectionFirstRow >= 0 && selectionEnd > selectionStart && fullSelectedClips.Count == 0) { DeleteSelectedRange(); return; }
        foreach (var lane in Arrangement.Lanes) lane.Clips.RemoveAll(c => selectedClips.Contains(c.Id) || selectedClips.Count == 0 && c.Id == selectedClip);
        selectedClip = null; ClearRangeSelection(); Changed();
    }
}
