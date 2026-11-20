using System.Drawing.Drawing2D;
using ModularAudience.Audio.Midi;

namespace ModularAudience.Forms.Modules.Dialogs;

/// <summary>Win+V-style history picker for MIDI note clips.</summary>
internal sealed class MidiClipboardHistoryDialog : Form
{
    private const int ItemHeight = 58;
    private readonly HistoryListPanel listPanel = new();
    private readonly List<MidiClipboardEntry> entries = [];

    public MidiClipboardHistoryDialog(IReadOnlyList<MidiClipboardEntry> history)
    {
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.StartPosition = FormStartPosition.Manual;
        this.ClientSize = new Size(440, 370);
        this.MinimizeBox = false;
        this.MaximizeBox = false;
        this.ShowInTaskbar = false;
        this.KeyPreview = true;
        this.Text = "MIDI Clipboard History (short Ctrl+V = top entry)";

        this.listPanel.Dock = DockStyle.Fill;
        this.Controls.Add(this.listPanel);
        Label hint = new()
        {
            Text = "Up/Down to cycle, Enter or click selects a ghost preview.",
            Dock = DockStyle.Top,
            Height = 24,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0)
        };
        this.Controls.Add(hint);

        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8),
            AutoSize = true
        };
        Button cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, Size = new Size(75, 25) };
        Button paste = new() { Text = "Paste", DialogResult = DialogResult.OK, Size = new Size(90, 25) };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(paste);
        this.Controls.Add(buttons);

        this.entries.AddRange(history);
        this.listPanel.SetEntries(this.entries);
        this.listPanel.EntryAccepted += (_, _) => this.DialogResult = DialogResult.OK;
        this.AcceptButton = paste;
        this.CancelButton = cancel;
        this.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Down)
            {
                this.listPanel.MoveSelection(1);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Up)
            {
                this.listPanel.MoveSelection(-1);
                e.Handled = true;
            }
        };
        this.Shown += (_, _) => this.listPanel.Focus();
    }

    public MidiClipboardEntry? SelectedEntry => this.listPanel.SelectedEntry;

    private sealed class HistoryListPanel : Panel
    {
        private List<MidiClipboardEntry> entries = [];
        private int selectedIndex = -1;

        public event EventHandler? EntryAccepted;

        public HistoryListPanel()
        {
            this.AutoScroll = true;
            this.BackColor = SystemColors.Window;
            this.TabStop = true;
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }

        public MidiClipboardEntry? SelectedEntry => this.selectedIndex >= 0 && this.selectedIndex < this.entries.Count
            ? this.entries[this.selectedIndex]
            : null;

        public void SetEntries(List<MidiClipboardEntry> source)
        {
            this.entries = source;
            this.AutoScrollPosition = Point.Empty;
            this.selectedIndex = this.entries.Count > 0 ? 0 : -1;
            this.AutoScrollMinSize = new Size(0, this.entries.Count * ItemHeight);
            this.Invalidate();
        }

        public void MoveSelection(int delta)
        {
            if (this.entries.Count == 0)
            {
                return;
            }

            this.selectedIndex = Math.Clamp(this.selectedIndex + delta, 0, this.entries.Count - 1);
            this.EnsureVisible();
            this.Invalidate();
        }

        private void EnsureVisible()
        {
            if (this.selectedIndex < 0)
            {
                return;
            }

            int top = this.selectedIndex * ItemHeight;
            int visibleTop = -this.AutoScrollPosition.Y;
            if (top < visibleTop)
            {
                this.AutoScrollPosition = new Point(0, top);
            }
            else if (top + ItemHeight > visibleTop + this.ClientSize.Height)
            {
                this.AutoScrollPosition = new Point(0, top + ItemHeight - this.ClientSize.Height);
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            int index = (e.Y - this.AutoScrollPosition.Y) / ItemHeight;
            if (index >= 0 && index < this.entries.Count)
            {
                this.selectedIndex = index;
                this.Invalidate();
                this.EntryAccepted?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (this.entries.Count == 0)
            {
                using Brush empty = new SolidBrush(SystemColors.GrayText);
                e.Graphics.DrawString("(empty)", this.Font, empty, new PointF(8, 8));
                return;
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int offsetY = this.AutoScrollPosition.Y;
            for (int index = 0; index < this.entries.Count; index++)
            {
                Rectangle bounds = new(0, index * ItemHeight + offsetY, this.ClientSize.Width, ItemHeight);
                if (!bounds.IntersectsWith(e.ClipRectangle))
                {
                    continue;
                }

                this.DrawEntry(e.Graphics, this.entries[index], bounds, index == this.selectedIndex);
            }
        }

        private void DrawEntry(Graphics graphics, MidiClipboardEntry entry, Rectangle bounds, bool selected)
        {
            using Brush background = new SolidBrush(selected ? SystemColors.Highlight : SystemColors.Window);
            graphics.FillRectangle(background, bounds);
            Rectangle thumbnail = new(bounds.Left + 5, bounds.Top + 6, 190, bounds.Height - 12);
            using Brush thumbBack = new SolidBrush(Color.FromArgb(28, 30, 34));
            graphics.FillRectangle(thumbBack, thumbnail);

            int minPitch = entry.Notes.Min(note => note.NoteNumber);
            int maxPitch = entry.Notes.Max(note => note.NoteNumber);
            int pitchCount = Math.Max(1, maxPitch - minPitch + 1);
            long maxEnd = entry.Notes.Max(note => note.StartTick + note.DurationTicks);
            long span = Math.Max(1, maxEnd);
            using Brush noteBrush = new SolidBrush(selected ? Color.LightGreen : Color.DeepSkyBlue);
            foreach (MidiNoteData note in entry.Notes)
            {
                float x = thumbnail.Left + note.StartTick / (float)span * thumbnail.Width;
                float width = Math.Max(2, note.DurationTicks / (float)span * thumbnail.Width);
                float rowHeight = thumbnail.Height / (float)pitchCount;
                float y = thumbnail.Bottom - (note.NoteNumber - minPitch + 1) * rowHeight;
                graphics.FillRectangle(noteBrush, x, y + 1, Math.Max(1, Math.Min(width, thumbnail.Right - x) - 1), Math.Max(1, rowHeight - 2));
            }

            graphics.DrawRectangle(Pens.DimGray, thumbnail);
            using Brush text = new SolidBrush(selected ? SystemColors.HighlightText : SystemColors.ControlText);
            RectangleF caption = new(thumbnail.Right + 8, bounds.Top, bounds.Width - thumbnail.Width - 16, bounds.Height);
            using StringFormat format = new() { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
            graphics.DrawString(entry.Caption, this.Font, text, caption, format);
        }
    }
}
