using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ModularAudience.Generators;

namespace ModularAudience.Forms.Modules.Dialogs
{
    /// <summary>
    /// An entry of the extended clipboard history: copied note structure
    /// with timestamp plus key figures for display and thumbnail.
    /// </summary>
    internal sealed class BreakbeatClipboardEntry
    {
        public BreakbeatClipboardEntry(List<BreakbeatPatternNote> notes, DateTime copiedAt)
        {
            this.Notes = notes;
            this.CopiedAt = copiedAt;
        }

        public List<BreakbeatPatternNote> Notes { get; }

        public DateTime CopiedAt { get; }

        public int NoteCount => this.Notes.Count;

        public int TrackCount => this.Notes.Select(note => note.TrackIndex).Distinct().Count();

        public string Caption => $"{this.NoteCount} notes · {this.TrackCount} tracks · {this.CopiedAt:HH:mm:ss}";
    }

    /// <summary>
    /// Historic clipboard history (Win+V style, modal): arrow keys cycle,
    /// Enter or mouse click takes the entry once for insertion.
    /// Uses a self-drawn scrollable panel so the top entry can never be
    /// clipped by native list-box quirks.
    /// </summary>
    internal sealed class BreakbeatClipboardHistoryDialog : Form
    {
        private const int ItemHeight = 56;

        private readonly HistoryListPanel listPanel = new();
        private readonly List<BreakbeatClipboardEntry> entries = [];

        public BreakbeatClipboardHistoryDialog(IReadOnlyList<BreakbeatClipboardEntry> history)
        {
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.Manual;
            this.ClientSize = new Size(420, 360);
            this.MinimizeBox = false;
            this.MaximizeBox = false;
            this.ShowInTaskbar = false;
            this.KeyPreview = true;
            this.Text = "Clipboard History (short Ctrl+V = top entry)";

            // Add-Reihenfolge wichtig: das Fill-Panel zuerst (es wird beim
            // Dock-Layout zuletzt platziert und füllt nur den Rest zwischen
            // Hinweis- und Buttonzeile, statt darunter zu liegen).
            this.listPanel.Dock = DockStyle.Fill;
            this.Controls.Add(this.listPanel);

            Label hint = new()
            {
                Text = "Up/Down to cycle, Enter or click takes one (pulls to top).",
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
            Button ok = new() { Text = "Paste", DialogResult = DialogResult.OK, Size = new Size(90, 25) };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            this.Controls.Add(buttons);

            this.entries.AddRange(history);
            this.listPanel.EntryAccepted += (_, _) => { this.DialogResult = DialogResult.OK; };

            this.AcceptButton = ok;
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

            this.Shown += (_, _) =>
            {
                this.listPanel.SetEntries(this.entries);
                this.listPanel.Focus();
            };
        }

        public BreakbeatClipboardEntry? SelectedEntry => this.listPanel.SelectedEntry;

        private sealed class HistoryListPanel : Panel
        {
            private List<BreakbeatClipboardEntry> entries = [];
            private int selectedIndex = -1;

            public event EventHandler? EntryAccepted;

            public HistoryListPanel()
            {
                this.AutoScroll = true;
                this.BackColor = SystemColors.Window;
                this.TabStop = true;
                this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            }

            public BreakbeatClipboardEntry? SelectedEntry => this.selectedIndex >= 0 && this.selectedIndex < this.entries.Count
                ? this.entries[this.selectedIndex]
                : null;

            public void SetEntries(List<BreakbeatClipboardEntry> source)
            {
                this.entries = source;
                this.AutoScrollPosition = Point.Empty;
                this.selectedIndex = this.entries.Count > 0 ? 0 : -1;
                this.EnsureVisible();
                this.Invalidate();
            }

            public void MoveSelection(int delta)
            {
                if (this.entries.Count == 0)
                {
                    return;
                }

                int next = Math.Clamp((this.selectedIndex < 0 ? 0 : this.selectedIndex) + delta, 0, this.entries.Count - 1);
                if (next != this.selectedIndex)
                {
                    this.selectedIndex = next;
                    this.EnsureVisible();
                    this.Invalidate();
                }
            }

            private void EnsureVisible()
            {
                if (this.selectedIndex < 0)
                {
                    return;
                }

                int itemTop = this.selectedIndex * ItemHeight;
                int visibleTop = -this.AutoScrollPosition.Y;
                int visibleHeight = this.ClientSize.Height;
                if (itemTop < visibleTop)
                {
                    this.AutoScrollPosition = new Point(0, itemTop);
                }
                else if (itemTop + ItemHeight > visibleTop + visibleHeight)
                {
                    this.AutoScrollPosition = new Point(0, itemTop + ItemHeight - visibleHeight);
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
                    using Brush emptyBrush = new SolidBrush(SystemColors.GrayText);
                    e.Graphics.DrawString("(empty)", this.Font, emptyBrush, new PointF(8f, 8f));
                    return;
                }

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

            private void DrawEntry(Graphics graphics, BreakbeatClipboardEntry entry, Rectangle bounds, bool selected)
            {
                using (Brush back = new SolidBrush(selected ? SystemColors.Highlight : SystemColors.Window))
                {
                    graphics.FillRectangle(back, bounds);
                }

                if (entry.Notes.Count > 0)
                {
                    Rectangle thumb = new(bounds.Left + 4, bounds.Top + 6, 170, bounds.Height - 12);
                    using (Brush thumbBack = new SolidBrush(Color.FromArgb(28, 30, 34)))
                    {
                        graphics.FillRectangle(thumbBack, thumb);
                    }

                    int minTrack = entry.Notes.Min(note => note.TrackIndex);
                    int maxTrack = entry.Notes.Max(note => note.TrackIndex);
                    int minTick = entry.Notes.Min(note => note.StartTick);
                    int maxEnd = entry.Notes.Max(note => note.StartTick + note.DurationTicks);
                    int trackRows = Math.Max(1, maxTrack - minTrack + 1);
                    long tickSpan = Math.Max(1L, (long)maxEnd - minTick);

                    foreach (BreakbeatPatternNote note in entry.Notes)
                    {
                        if (note.DurationTicks <= 0)
                        {
                            continue;
                        }

                        float x = thumb.Left + (float)(note.StartTick - minTick) / tickSpan * thumb.Width;
                        float w = Math.Max(2f, (float)note.DurationTicks / tickSpan * thumb.Width);
                        float rowH = thumb.Height / (float)trackRows;
                        float y = thumb.Top + (note.TrackIndex - minTrack) * rowH;
                        Color color = !note.TimeStretch && !note.Varispeed
                            ? Color.FromArgb(75, 190, 155)
                            : note.Varispeed
                                ? note.IsShortened ? Color.FromArgb(247, 218, 135) : Color.FromArgb(224, 176, 61)
                                : note.IsShortened ? Color.FromArgb(151, 202, 234) : Color.FromArgb(91, 161, 211);
                        using Brush noteBrush = new SolidBrush(color);
                        // 1 px Luft rechts: direkt aneinanderstoßende Noten
                        // verschmelzen sonst optisch zu einem Balken.
                        float barWidth = Math.Max(1f, Math.Min(w, thumb.Right - x) - 1f);
                        graphics.FillRectangle(noteBrush, x, y + 1f, barWidth, Math.Max(1f, rowH - 2f));
                    }

                    graphics.DrawRectangle(Pens.DimGray, thumb);
                    Color textColor = selected ? SystemColors.HighlightText : SystemColors.ControlText;
                    using Brush textBrush = new SolidBrush(textColor);
                    RectangleF captionBounds = new(thumb.Right + 6f, bounds.Top, bounds.Width - thumb.Width - 12f, bounds.Height);
                    using StringFormat format = new() { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
                    graphics.DrawString(entry.Caption, this.Font, textBrush, captionBounds, format);
                }
            }
        }
    }
}
