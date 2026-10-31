using ModularAudience.Audio;
using ModularAudience.Generators;

namespace ModularAudience.Forms.Modules.Dialogs;

public partial class BreakbeatPatternEditorDialog
{
    private readonly HashSet<AudioObj> soloTracks = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<AudioObj> mutedTracks = new(ReferenceEqualityComparer.Instance);
    private Rectangle TrackButtonBounds(int row, int button)
    {
        var roll = this.AutoRollBounds(row);
        int side = Math.Min(20, roll.Height);
        return new Rectangle(roll.Right + 3 + button * (side + 2), roll.Top, side, side);
    }
    private void PaintTrackButtons(Graphics graphics, int row)
    {
        for (int button = 0; button < 3; button++)
        {
            Rectangle bounds = this.TrackButtonBounds(row, button);
            bool on = button == 0 ? this.soloTracks.Contains(this.samples[row]) : button == 1 && this.mutedTracks.Contains(this.samples[row]);
            Color back = on && button == 0 ? Color.LightGreen : on ? Color.FromArgb(48, 48, 48) : Color.FromArgb(65, 69, 76);
            using var brush = new SolidBrush(back); graphics.FillRectangle(brush, bounds);
            if (button < 2)
                TextRenderer.DrawText(graphics, button == 0 ? "S" : "M", SystemFonts.MessageBoxFont, bounds,
                    on && button == 0 ? Color.Black : Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            else
            {
                // Small eraser glyph, independent of installed symbol/emoji fonts.
                var glyph = Rectangle.Inflate(bounds, -4, -5);
                using var pen = new Pen(Color.White);
                graphics.DrawPolygon(pen, [new Point(glyph.Left, glyph.Bottom - 3), new Point(glyph.Left + 5, glyph.Top),
                    new Point(glyph.Right, glyph.Top + 3), new Point(glyph.Right - 5, glyph.Bottom)]);
            }
        }
    }
    private bool TryClickTrackButtons(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return false;
        for (int row = 0; row < this.samples.Count; row++)
            for (int button = 0; button < 3; button++)
            {
                if (!this.TrackButtonBounds(row, button).Contains(e.Location)) continue;
                AudioObj sample = this.samples[row];
                if (button < 2)
                {
                    var set = button == 0 ? this.soloTracks : this.mutedTracks;
                    if (!set.Remove(sample)) set.Add(sample);
                    this.RegisterPatternNotesChanged(this.notes.Select(n => (BreakbeatPatternNote?)n).ToArray());
                }
                else
                {
                    var removed = this.notes.Where(n => n.TrackIndex == row).ToArray();
                    this.notes.RemoveAll(n => n.TrackIndex == row);
                    this.selectedNotes.RemoveWhere(n => n.TrackIndex == row);
                    this.deadGroupNotes.Clear();
                    this.RegisterPatternNotesChanged(removed.Select(n => (BreakbeatPatternNote?)n).ToArray());
                }
                this.CommitHistoryAction();
                this.pictureBox_pattern.Invalidate();
                return true;
            }
        return false;
    }
    private bool IsTrackAudible(int row)
    {
        if (row < 0 || row >= this.samples.Count || this.mutedTracks.Contains(this.samples[row])) return false;
        bool anySolo = this.samples.Any(this.soloTracks.Contains);
        return !anySolo || this.soloTracks.Contains(this.samples[row]);
    }
    private void ClearAllPatternNotes()
    {
        if (this.notes.Count == 0) return;
        var removed = this.notes.ToArray();
        this.notes.Clear();
        this.selectedNotes.Clear();
        this.deadGroupNotes.Clear();
        this.lastGenerate = null;
        this.RegisterPatternNotesChanged(removed.Select(n => (BreakbeatPatternNote?)n).ToArray());
        this.CommitHistoryAction();
        this.pictureBox_pattern.Invalidate();
    }
    private BreakbeatPatternNote[] ApplyTrackAudibility(IEnumerable<BreakbeatPatternNote> notes) => notes
        .Select(n => this.IsTrackAudible(n.TrackIndex) ? n : n with { VolumePercent = 0 }).ToArray();
}
