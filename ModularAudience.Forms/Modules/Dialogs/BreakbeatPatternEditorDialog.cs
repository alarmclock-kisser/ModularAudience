using ModularAudience.Audio;
using ModularAudience.Forms.Helpers;
using ModularAudience.Generators;
using System.Drawing.Drawing2D;

namespace ModularAudience.Forms.Modules.Dialogs
{
    public partial class BreakbeatPatternEditorDialog : Form
    {
        private readonly List<bool[]> pattern;
        private readonly List<AudioObj> samples;
        private readonly string[] rowLabels;
        private int bars;
        private readonly float swing;
        private readonly List<BreakbeatPatternNote> notes;
        private readonly HashSet<int> selectedBars = [];
        private CancellationTokenSource? hearCancellationTokenSource;
        private AudioObj? previewAudio;
        private int[] previewBarMap = [];
        private CancellationTokenSource? notePreviewCancellationTokenSource;
        private AudioObj? notePreviewAudio;
        private bool drawing;
        private int drawingRow;
        private int drawingStartColumn;
        private MouseButtons drawingButton;
        private BreakbeatPatternNote? drawingNote;
        private bool drawingShortTimeStretchNote;
        private BreakbeatPatternNote? resizingNote;
        private ResizeEdge resizingEdge;
        private int resizeFixedTick;
        private bool resizingWithControl;
        private bool resizeChanged;
        private bool resizeUseVarispeed;
        private int resizeStartX;
        private BreakbeatPatternNote? resizeOriginalNote;
        private const double MaximumHorizontalZoom = 512.0;
        private double horizontalZoom = 1.0;
        private int currentResolution = 4;
        private bool initializing = true;

        private enum ResizeEdge
        {
            None,
            Left,
            Right
        }

        public IReadOnlyList<bool[]> Pattern => this.BuildPatternMatrix();

        public IReadOnlyList<BreakbeatPatternNote> Notes => this.GetNotesWithoutCoveredRetriggers();

        public decimal Bpm => this.numericUpDown_bpm.Value;

        public int Resolution => this.currentResolution;

        public int Bars => this.bars;

        public BreakbeatPatternEditorDialog(
            IReadOnlyList<bool[]> pattern,
            IEnumerable<AudioObj> samples,
            IReadOnlyList<string> rowLabels,
            int bars,
            int resolution,
            float swing,
            decimal bpm,
            IReadOnlyList<BreakbeatPatternNote>? existingNotes = null,
            int? initialGridResolution = null)
        {
            this.InitializeComponent();
            this.pattern = pattern.Select(row => row.ToArray()).ToList();
            this.samples = samples.ToList();
            this.rowLabels = rowLabels.ToArray();
            this.bars = Math.Max(1, bars);
            this.swing = swing;
            this.numericUpDown_bpm.Value = Math.Clamp(decimal.Round(bpm, 1), this.numericUpDown_bpm.Minimum, this.numericUpDown_bpm.Maximum);
            if (initialGridResolution is int savedResolution)
            {
                this.numericUpDown_resolution.Value = Math.Clamp(savedResolution, (int)this.numericUpDown_resolution.Minimum, (int)this.numericUpDown_resolution.Maximum);
            }

            this.currentResolution = (int)this.numericUpDown_resolution.Value;
            this.notes = existingNotes?.Select(note => note with
                {
                    OriginalDurationTicks = note.OriginalDurationTicks > 0
                        ? note.OriginalDurationTicks
                        : note.TimeStretch || note.Varispeed
                            ? BreakbeatGenerator_V2.GetMinimumNoteDurationTicks(this.samples, note.TrackIndex, (float)this.Bpm, this.currentResolution)
                            : note.DurationTicks,
                    ManuallyResized = note.ManuallyResized || note.TimeStretch || note.Varispeed
                }).ToList()
                ?? BreakbeatGenerator_V2.CreatePatternNotesFromGrid(this.pattern, this.samples, Math.Max(1, resolution), (float)this.Bpm, this.currentResolution);
            this.initializing = false;
            this.ConfigurePatternScrollBar();
            this.Text = "Breakbeat Pattern Editor";
            this.pictureBox_pattern.Cursor = Cursors.Cross;
            this.FormClosing += this.BreakbeatPatternEditorDialog_FormClosing;
            this.MouseWheel += this.pictureBox_pattern_MouseWheel;
        }

        private void pictureBox_pattern_Paint(object? sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.None;
            e.Graphics.Clear(Color.FromArgb(28, 30, 34));

            if (this.samples.Count == 0 || this.bars <= 0)
            {
                return;
            }

            Rectangle grid = this.GetGridBounds();
            int rows = this.samples.Count;
            int columns = this.bars * this.currentResolution;
            double virtualGridWidth = grid.Width * this.horizontalZoom;
            int scrollOffset = this.hScrollBar_pattern.Value;
            float cellWidth = (float)(virtualGridWidth / columns);
            float cellHeight = grid.Height / (float)rows;
            int ticksPerBar = BreakbeatGenerator_V2.PatternTicksPerBar;

            using Font labelFont = new("Segoe UI", Math.Clamp(Math.Min(10f, cellHeight * 0.48f), 6f, 10f));
            using Font barFont = new("Segoe UI", 8f, FontStyle.Bold);
            using StringFormat labelFormat = new()
            {
                Alignment = StringAlignment.Far,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            };
            using Brush labelBrush = new SolidBrush(Color.FromArgb(220, 222, 226));
            using Brush emptyBrush = new SolidBrush(Color.FromArgb(47, 50, 56));
            using Brush hitBrush = new SolidBrush(Color.FromArgb(75, 190, 155));
            using Brush stretchedHitBrush = new SolidBrush(Color.FromArgb(91, 161, 211));
            using Brush shortenedHitBrush = new SolidBrush(Color.FromArgb(151, 202, 234));
            using Brush varispeedHitBrush = new SolidBrush(Color.FromArgb(224, 176, 61));
            using Brush shortenedVarispeedHitBrush = new SolidBrush(Color.FromArgb(247, 218, 135));
            using Brush barBrush = new SolidBrush(Color.FromArgb(158, 164, 173));
            using Brush selectedBarHeaderBrush = new SolidBrush(Color.FromArgb(69, 94, 81));
            using Brush selectedBarTextBrush = new SolidBrush(Color.FromArgb(241, 255, 246));
            using Pen rowPen = new(Color.FromArgb(75, 79, 87));
            using Pen stepPen = new(Color.FromArgb(62, 66, 73));
            using Pen barPen = new(Color.FromArgb(112, 119, 128), 2f);
            using Pen selectedBarHeaderPen = new(Color.FromArgb(130, 205, 158), 1f);
            using Pen notePen = new(Color.FromArgb(28, 30, 34));

            for (int row = 0; row < rows; row++)
            {
                float y = grid.Top + row * cellHeight;
                RectangleF labelBounds = new(8, y, Math.Max(1, grid.Left - 16), Math.Max(1f, cellHeight));
                string label = row < this.rowLabels.Length ? this.rowLabels[row] : $"Track {row + 1}";
                e.Graphics.DrawString(label, labelFont, labelBrush, labelBounds, labelFormat);
            }

            GraphicsState gridState = e.Graphics.Save();
            e.Graphics.SetClip(new Rectangle(grid.Left, 0, grid.Width, grid.Bottom));
            for (int row = 0; row < rows; row++)
            {
                float y = grid.Top + row * cellHeight;
                e.Graphics.FillRectangle(emptyBrush, grid.Left, y, grid.Width, Math.Max(1f, cellHeight - 1f));
                e.Graphics.DrawLine(rowPen, grid.Left, y + cellHeight, grid.Right, y + cellHeight);
            }

            if (cellWidth >= 6f)
            {
                for (int column = 1; column < columns; column++)
                {
                    if (column % this.currentResolution == 0)
                    {
                        continue;
                    }

                    float x = grid.Left + column * cellWidth - scrollOffset;
                    e.Graphics.DrawLine(stepPen, x, grid.Top, x, grid.Bottom);
                }
            }

            for (int bar = 0; bar <= this.bars; bar++)
            {
                int column = Math.Min(columns, bar * this.currentResolution);
                float x = grid.Left + column * cellWidth - scrollOffset;
                e.Graphics.DrawLine(barPen, x, grid.Top, x, grid.Bottom);
                if (bar < this.bars && column < columns)
                {
                    float barWidth = this.currentResolution * cellWidth;
                    if (this.selectedBars.Contains(bar))
                    {
                        RectangleF headerHighlight = new(x + 1f, 1f, Math.Max(1f, barWidth - 2f), 23f);
                        e.Graphics.FillRectangle(selectedBarHeaderBrush, headerHighlight);
                        e.Graphics.DrawRectangle(selectedBarHeaderPen, headerHighlight.X, headerHighlight.Y, headerHighlight.Width, headerHighlight.Height);
                    }

                    RectangleF barBounds = new(x + 3f, 2f, Math.Max(1f, barWidth - 6f), 20f);
                    Brush textBrush = this.selectedBars.Contains(bar) ? selectedBarTextBrush : barBrush;
                    e.Graphics.DrawString((bar + 1).ToString(), barFont, textBrush, barBounds);
                }
            }

            foreach (BreakbeatPatternNote note in this.notes)
            {
                if (note.TrackIndex < 0 || note.TrackIndex >= rows || note.DurationTicks <= 0)
                {
                    continue;
                }

                double patternTicks = this.bars * (double)ticksPerBar;
                double clippedStart = Math.Clamp(note.StartTick, 0, patternTicks);
                double clippedEnd = Math.Clamp((double)note.StartTick + note.DurationTicks, 0, patternTicks);
                if (clippedEnd <= clippedStart)
                {
                    continue;
                }

                float x = grid.Left + (float)(clippedStart / patternTicks * virtualGridWidth) - scrollOffset;
                float noteWidth = (float)((clippedEnd - clippedStart) / patternTicks * virtualGridWidth);
                float y = grid.Top + note.TrackIndex * cellHeight;
                float inset = Math.Min(2f, Math.Min(cellWidth, cellHeight) * 0.12f);
                RectangleF hit = new(x + inset, y + inset, Math.Max(1f, noteWidth - (2f * inset)), Math.Max(1f, cellHeight - (2f * inset)));
                Brush noteBrush = !note.IsManuallyAdjusted
                    ? hitBrush
                    : note.Varispeed
                        ? note.IsShortened ? shortenedVarispeedHitBrush : varispeedHitBrush
                        : note.IsShortened ? shortenedHitBrush : stretchedHitBrush;
                e.Graphics.FillRectangle(noteBrush, hit);
                e.Graphics.DrawRectangle(notePen, hit.X, hit.Y, hit.Width, hit.Height);
            }

            if (this.previewAudio is not null && this.previewAudio.PlayerPlaying)
            {
                double secondsPerBar = 240.0 / Math.Max(1.0, (double)this.Bpm);
                double patternDuration = this.bars * secondsPerBar;
                double caretTime = this.previewAudio.CurrentTime.TotalSeconds;
                if (this.previewBarMap.Length > 0)
                {
                    double selectedDuration = this.previewBarMap.Length * secondsPerBar;
                    double sequenceTime = caretTime % selectedDuration;
                    int selectedIndex = Math.Min((int)(sequenceTime / secondsPerBar), this.previewBarMap.Length - 1);
                    double withinBar = sequenceTime - selectedIndex * secondsPerBar;
                    caretTime = (this.previewBarMap[selectedIndex] * secondsPerBar) + withinBar;
                }

                float caretX = grid.Left + (float)Math.Clamp(caretTime / patternDuration, 0, 1) * (float)virtualGridWidth - scrollOffset;
                using Pen caretPen = new(Color.OrangeRed, 2f);
                e.Graphics.DrawLine(caretPen, caretX, grid.Top, caretX, grid.Bottom);
            }

            e.Graphics.Restore(gridState);
        }

        private void pictureBox_pattern_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button is not (MouseButtons.Left or MouseButtons.Right))
            {
                return;
            }

            if (e.Button == MouseButtons.Left && this.TryGetHeaderBar(e.Location, out int headerBar))
            {
                if (!this.selectedBars.Add(headerBar))
                {
                    this.selectedBars.Remove(headerBar);
                }

                this.pictureBox_pattern.Invalidate();
                return;
            }

            if (!this.TryGetCell(e.Location, out int row, out int column))
            {
                return;
            }

            this.drawingButton = e.Button;
            this.drawingRow = row;
            this.drawingStartColumn = column;
            if (e.Button == MouseButtons.Left)
            {
                int existingIndex = this.FindNoteIndexAt(row, this.PointToTick(e.Location));
                if (existingIndex >= 0)
                {
                    BreakbeatPatternNote existingNote = this.notes[existingIndex];
                    this.resizingEdge = this.GetResizeEdge(existingNote, e.Location);
                    if (this.resizingEdge == ResizeEdge.None)
                    {
                        return;
                    }

                    this.resizingNote = existingNote;
                    this.resizeOriginalNote = existingNote;
                    this.resizeStartX = e.X;
                    this.resizingWithControl = (ModifierKeys & Keys.Control) == Keys.Control;
                    this.resizeChanged = false;
                    this.resizeUseVarispeed = this.resizingWithControl
                        ? !existingNote.Varispeed
                        : existingNote.Varispeed;
                    this.resizeFixedTick = this.resizingEdge == ResizeEdge.Left
                        ? existingNote.StartTick + existingNote.DurationTicks
                        : existingNote.StartTick;
                    this.drawing = true;
                    this.pictureBox_pattern.Capture = true;
                    this.pictureBox_pattern.Cursor = Cursors.SizeWE;
                    return;
                }

                int stepTicks = this.GetTicksPerStep();
                int startTick = column * stepTicks;
                int minimumDuration = BreakbeatGenerator_V2.GetMinimumNoteDurationTicks(this.samples, row, (float)this.Bpm, this.currentResolution);
                this.drawingShortTimeStretchNote = (ModifierKeys & Keys.Control) == Keys.Control;
                int originalDurationTicks = this.drawingShortTimeStretchNote
                    ? Math.Max(minimumDuration, stepTicks * 2)
                    : minimumDuration;
                int initialDurationTicks = this.drawingShortTimeStretchNote ? stepTicks : minimumDuration;
                BreakbeatPatternNote newNote = new(
                    row,
                    startTick,
                    initialDurationTicks,
                    TimeStretch: this.drawingShortTimeStretchNote,
                    ManuallyResized: this.drawingShortTimeStretchNote,
                    OriginalDurationTicks: originalDurationTicks);
                if (this.HasOverlappingNote(newNote))
                {
                    this.drawingShortTimeStretchNote = false;
                    return;
                }

                this.drawing = true;
                this.drawingNote = newNote;
                this.notes.Add(this.drawingNote);
            }
            else
            {
                this.drawing = true;
                this.drawingNote = null;
                this.DeleteNotesAt(row, this.PointToTick(e.Location));
            }

            this.pictureBox_pattern.Capture = true;
            this.pictureBox_pattern.Invalidate();
        }

        private void pictureBox_pattern_MouseMove(object? sender, MouseEventArgs e)
        {
            if (!this.drawing)
            {
                this.UpdatePatternCursor(e.Location);
                return;
            }

            if (this.resizingNote is not null)
            {
                this.UpdateResizingNote(e.Location);
            }
            else if (this.drawingButton == MouseButtons.Right)
            {
                if (this.TryGetCell(e.Location, out int row, out int column))
                {
                    this.DeleteNotesAt(row, this.PointToTick(e.Location));
                }
            }
            else if (this.drawingNote is not null)
            {
                int column = this.PointToColumn(e.Location);
                int firstColumn = Math.Min(this.drawingStartColumn, column);
                int lastColumn = Math.Max(this.drawingStartColumn, column);
                int stepTicks = this.GetTicksPerStep();
                int minimumDuration = BreakbeatGenerator_V2.GetMinimumNoteDurationTicks(this.samples, this.drawingRow, (float)this.Bpm, this.currentResolution);
                int draggedDuration = (lastColumn - firstColumn + 1) * stepTicks;
                int originalDurationTicks = this.drawingShortTimeStretchNote
                    ? this.drawingNote.OriginalDurationTicks
                    : minimumDuration;
                int durationTicks = this.drawingShortTimeStretchNote
                    ? draggedDuration
                    : Math.Max(minimumDuration, draggedDuration);
                bool manuallyResized = durationTicks != originalDurationTicks;
                BreakbeatPatternNote updated = this.drawingNote with
                {
                    StartTick = firstColumn * stepTicks,
                    DurationTicks = durationTicks,
                    TimeStretch = manuallyResized,
                    Varispeed = false,
                    ManuallyResized = manuallyResized,
                    OriginalDurationTicks = originalDurationTicks
                };
                int index = this.notes.FindIndex(note => ReferenceEquals(note, this.drawingNote));
                if (index >= 0)
                {
                    if (!this.HasOverlappingNote(updated, this.drawingNote))
                    {
                        this.notes[index] = updated;
                        this.drawingNote = updated;
                    }
                }
            }

            this.pictureBox_pattern.Invalidate();
        }

        private void pictureBox_pattern_MouseUp(object? sender, MouseEventArgs e)
        {
            if (!this.drawing || e.Button != this.drawingButton)
            {
                return;
            }

            this.UpdateResizingNote(e.Location);
            this.drawing = false;
            bool wasResizeGesture = this.resizingNote is not null;
            bool didResize = wasResizeGesture && this.resizeChanged;
            BreakbeatPatternNote? clickedNote = wasResizeGesture ? this.resizeOriginalNote : null;
            BreakbeatPatternNote? placedNote = this.drawingButton == MouseButtons.Left
                ? wasResizeGesture
                    ? didResize ? this.resizingNote : clickedNote
                    : this.drawingNote
                : null;
            bool toggledPlaybackMode = false;
            if (this.resizingWithControl && wasResizeGesture && !didResize && clickedNote?.IsManuallyAdjusted == true)
            {
                BreakbeatPatternNote toggledNote = clickedNote.Varispeed
                    ? clickedNote with { TimeStretch = true, Varispeed = false }
                    : clickedNote with { TimeStretch = false, Varispeed = true };
                int noteIndex = this.notes.FindIndex(note => ReferenceEquals(note, clickedNote));
                if (noteIndex >= 0)
                {
                    this.notes[noteIndex] = toggledNote;
                    placedNote = toggledNote;
                    toggledPlaybackMode = true;
                }
            }

            bool shouldPrehear = placedNote is not null
                && ((wasResizeGesture && !didResize && !this.resizingWithControl)
                    || this.checkBox_preHear.Checked);
            this.drawingNote = null;
            this.drawingShortTimeStretchNote = false;
            this.resizingNote = null;
            this.resizeOriginalNote = null;
            this.resizingEdge = ResizeEdge.None;
            this.resizingWithControl = false;
            this.resizeChanged = false;
            this.resizeUseVarispeed = false;
            this.pictureBox_pattern.Capture = false;
            this.UpdatePatternCursor(e.Location);

            if (placedNote is not null && (!wasResizeGesture || didResize || toggledPlaybackMode))
            {
                this.RemoveCoveredRetriggersIfNeeded(placedNote);
            }

            this.pictureBox_pattern.Invalidate();

            if (shouldPrehear && placedNote is not null)
            {
                _ = this.PrehearNoteAsync(placedNote);
            }
        }

        private void DeleteNotesAt(int row, int tick)
        {
            this.notes.RemoveAll(note => note.TrackIndex == row && tick >= note.StartTick && tick < note.StartTick + note.DurationTicks);
        }

        private int FindNoteIndexAt(int row, int tick)
        {
            return this.notes.FindLastIndex(note =>
                note.TrackIndex == row
                && tick >= note.StartTick
                && tick < (long)note.StartTick + note.DurationTicks);
        }

        private ResizeEdge GetResizeEdge(BreakbeatPatternNote note, Point point)
        {
            Rectangle grid = this.GetGridBounds();
            float patternTicks = this.bars * (float)BreakbeatGenerator_V2.PatternTicksPerBar;
            double virtualGridWidth = grid.Width * this.horizontalZoom;
            int scrollOffset = this.hScrollBar_pattern.Value;
            float left = grid.Left + (float)(note.StartTick / patternTicks * virtualGridWidth) - scrollOffset;
            float right = grid.Left + (float)((note.StartTick + note.DurationTicks) / patternTicks * virtualGridWidth) - scrollOffset;

            if (point.X < left || point.X > right)
            {
                return ResizeEdge.None;
            }

            return point.X < left + ((right - left) / 2f) ? ResizeEdge.Left : ResizeEdge.Right;
        }

        private void UpdatePatternCursor(Point point)
        {
            if (this.TryGetHeaderBar(point, out _))
            {
                this.pictureBox_pattern.Cursor = Cursors.Hand;
                return;
            }

            bool overNote = this.TryGetCell(point, out int row, out _)
                && this.FindNoteIndexAt(row, this.PointToTick(point)) >= 0;
            this.pictureBox_pattern.Cursor = overNote ? Cursors.SizeWE : Cursors.Cross;
        }

        private bool HasResizeDistance(Point point)
        {
            Rectangle grid = this.GetGridBounds();
            float cellWidth = (float)(grid.Width * this.horizontalZoom / Math.Max(1, this.bars * this.currentResolution));
            int minimumDistance = Math.Max(1, (int)Math.Ceiling(cellWidth / 2f));
            return Math.Abs(point.X - this.resizeStartX) >= minimumDistance;
        }

        private void pictureBox_pattern_MouseWheel(object? sender, MouseEventArgs e)
        {
            Point pointer = e.Location;
            if (sender is Control source)
            {
                pointer = this.pictureBox_pattern.PointToClient(source.PointToScreen(e.Location));
            }

            bool handled = false;
            if ((ModifierKeys & Keys.Control) == Keys.Control && e.Delta != 0)
            {
                double zoomFactor = Math.Pow(1.5, e.Delta / 120.0);
                this.SetHorizontalZoom(this.horizontalZoom * zoomFactor, pointer.X);
                handled = true;
            }
            else if (this.hScrollBar_pattern.Visible && e.Delta != 0)
            {
                HScrollBar scrollBar = this.hScrollBar_pattern;
                int maximum = Math.Max(scrollBar.Minimum, scrollBar.Maximum - scrollBar.LargeChange + 1);
                int movement = (int)Math.Round(-(e.Delta / 120.0) * scrollBar.SmallChange * 3.0);
                scrollBar.Value = Math.Clamp(scrollBar.Value + movement, scrollBar.Minimum, maximum);
                handled = true;
            }

            if (e is HandledMouseEventArgs handledEventArgs)
            {
                handledEventArgs.Handled = handled;
            }
        }

        private void pictureBox_pattern_MouseEnter(object? sender, EventArgs e)
        {
            this.pictureBox_pattern.Focus();
        }

        private void SetHorizontalZoom(double requestedZoom, int anchorX)
        {
            double newZoom = Math.Clamp(requestedZoom, 1.0, MaximumHorizontalZoom);
            if (Math.Abs(newZoom - this.horizontalZoom) < 0.0001)
            {
                return;
            }

            Rectangle grid = this.GetGridBounds();
            double anchorOffset = Math.Clamp(anchorX - grid.Left, 0, grid.Width);
            double newScrollOffset = ((this.hScrollBar_pattern.Value + anchorOffset) * newZoom / this.horizontalZoom) - anchorOffset;
            this.horizontalZoom = newZoom;
            this.ConfigurePatternScrollBar((int)Math.Round(newScrollOffset));
            this.pictureBox_pattern.Invalidate();
        }

        private void ConfigurePatternScrollBar(int? requestedOffset = null)
        {
            Rectangle grid = this.GetGridBounds();
            HScrollBar scrollBar = this.hScrollBar_pattern;
            int contentWidth = Math.Max(grid.Width, (int)Math.Ceiling(grid.Width * this.horizontalZoom));
            int maximumOffset = Math.Max(0, contentWidth - grid.Width);
            if (maximumOffset == 0)
            {
                scrollBar.Minimum = 0;
                scrollBar.Maximum = 0;
                scrollBar.LargeChange = 1;
                scrollBar.SmallChange = 1;
                scrollBar.Value = 0;
                scrollBar.Visible = false;
                return;
            }

            int columns = Math.Max(1, this.bars * this.currentResolution);
            scrollBar.Minimum = 0;
            scrollBar.LargeChange = Math.Max(1, grid.Width);
            scrollBar.SmallChange = Math.Max(1, (int)Math.Ceiling(contentWidth / (double)columns));
            scrollBar.Maximum = maximumOffset + scrollBar.LargeChange - 1;
            int value = Math.Clamp(requestedOffset ?? scrollBar.Value, 0, maximumOffset);
            scrollBar.Value = value;
            scrollBar.Visible = true;
        }

        private void hScrollBar_pattern_ValueChanged(object? sender, EventArgs e)
        {
            this.pictureBox_pattern.Invalidate();
        }

        private void UpdateResizingNote(Point point)
        {
            if (this.resizingNote is null)
            {
                return;
            }

            if (!this.HasResizeDistance(point))
            {
                if (this.resizeChanged && this.resizeOriginalNote is not null)
                {
                    int originalIndex = this.notes.FindIndex(note => ReferenceEquals(note, this.resizingNote));
                    if (originalIndex >= 0)
                    {
                        this.notes[originalIndex] = this.resizeOriginalNote;
                    }

                    this.resizingNote = this.resizeOriginalNote;
                }

                this.resizeChanged = false;
                return;
            }

            this.resizeChanged = true;
            BreakbeatPatternNote updated = this.ResizeNoteToPoint(this.resizingNote, point);
            int index = this.notes.FindIndex(note => ReferenceEquals(note, this.resizingNote));
            if (index >= 0 && !this.HasOverlappingNote(updated, this.resizingNote))
            {
                this.notes[index] = updated;
                this.resizingNote = updated;
            }
        }

        private BreakbeatPatternNote ResizeNoteToPoint(BreakbeatPatternNote note, Point point)
        {
            int stepTicks = this.GetTicksPerStep();
            int patternEndTick = this.bars * BreakbeatGenerator_V2.PatternTicksPerBar;
            int column = this.PointToColumn(point);
            int originalDurationTicks = note.OriginalDurationTicks > 0
                ? note.OriginalDurationTicks
                : note.TimeStretch || note.Varispeed
                    ? BreakbeatGenerator_V2.GetMinimumNoteDurationTicks(this.samples, note.TrackIndex, (float)this.Bpm, this.currentResolution)
                    : note.DurationTicks;
            int startTick = note.StartTick;
            int durationTicks;

            if (this.resizingEdge == ResizeEdge.Left)
            {
                startTick = Math.Clamp(column * stepTicks, 0, Math.Max(0, this.resizeFixedTick - stepTicks));
                durationTicks = this.resizeFixedTick - startTick;
            }
            else
            {
                int endTick = Math.Clamp((column + 1) * stepTicks, this.resizeFixedTick + stepTicks, patternEndTick);
                startTick = this.resizeFixedTick;
                durationTicks = endTick - startTick;
            }

            bool manuallyResized = durationTicks != originalDurationTicks;
            bool useVarispeed = manuallyResized && this.resizeUseVarispeed;
            return note with
            {
                StartTick = startTick,
                DurationTicks = durationTicks,
                TimeStretch = manuallyResized && !useVarispeed,
                Varispeed = useVarispeed,
                ManuallyResized = manuallyResized,
                OriginalDurationTicks = originalDurationTicks
            };
        }

        private void RemoveCoveredRetriggersIfNeeded(BreakbeatPatternNote note)
        {
            if (!note.IsTimeExtended)
            {
                return;
            }

            List<BreakbeatPatternNote> notesWithoutRetriggers = BreakbeatGenerator_V2.RemoveRetriggersCoveredByStretchedNote(this.notes, note);
            this.notes.Clear();
            this.notes.AddRange(notesWithoutRetriggers);
        }

        private bool HasOverlappingNote(BreakbeatPatternNote candidate, BreakbeatPatternNote? ignoredNote = null)
        {
            long candidateEndTick = (long)candidate.StartTick + candidate.DurationTicks;
            return this.notes.Any(note =>
                !ReferenceEquals(note, ignoredNote)
                && note.TrackIndex == candidate.TrackIndex
                && candidate.StartTick < (long)note.StartTick + note.DurationTicks
                && note.StartTick < candidateEndTick);
        }

        private bool TryGetCell(Point point, out int row, out int column)
        {
            row = -1;
            column = -1;
            if (this.samples.Count == 0)
            {
                return false;
            }

            Rectangle grid = this.GetGridBounds();
            if (!grid.Contains(point))
            {
                return false;
            }

            row = Math.Clamp((int)((point.Y - grid.Top) / (grid.Height / (double)this.samples.Count)), 0, this.samples.Count - 1);
            column = this.PointToColumn(point);
            return true;
        }

        private bool TryGetHeaderBar(Point point, out int bar)
        {
            bar = -1;
            Rectangle grid = this.GetGridBounds();
            if (this.bars <= 0 || point.X < grid.Left || point.X >= grid.Right || point.Y < 0 || point.Y >= grid.Top)
            {
                return false;
            }

            double virtualGridWidth = grid.Width * this.horizontalZoom;
            double virtualX = point.X - grid.Left + this.hScrollBar_pattern.Value;
            bar = Math.Clamp((int)(virtualX / (virtualGridWidth / this.bars)), 0, this.bars - 1);
            return true;
        }

        private int PointToColumn(Point point)
        {
            Rectangle grid = this.GetGridBounds();
            int count = Math.Max(1, this.bars * this.currentResolution);
            double virtualGridWidth = grid.Width * this.horizontalZoom;
            double virtualX = point.X - grid.Left + this.hScrollBar_pattern.Value;
            return Math.Clamp((int)Math.Floor(virtualX / (virtualGridWidth / count)), 0, count - 1);
        }

        private int GetTicksPerStep() => Math.Max(1, (int)Math.Round(BreakbeatGenerator_V2.PatternTicksPerBar / (double)this.currentResolution));

        private List<BreakbeatPatternNote> GetNotesWithoutCoveredRetriggers()
        {
            List<BreakbeatPatternNote> result = this.notes.ToList();
            BreakbeatPatternNote[] stretchedNotes = result
                .Where(note => note.IsTimeExtended)
                .OrderBy(note => note.StartTick)
                .ToArray();

            foreach (BreakbeatPatternNote stretchedNote in stretchedNotes)
            {
                if (result.Any(note => ReferenceEquals(note, stretchedNote)))
                {
                    result = BreakbeatGenerator_V2.RemoveRetriggersCoveredByStretchedNote(result, stretchedNote);
                }
            }

            return result;
        }

        private List<bool[]> BuildPatternMatrix()
        {
            int columns = Math.Max(1, this.bars * this.currentResolution);
            var result = Enumerable.Range(0, this.samples.Count).Select(_ => new bool[columns]).ToList();
            int stepTicks = this.GetTicksPerStep();
            foreach (BreakbeatPatternNote note in this.GetNotesWithoutCoveredRetriggers())
            {
                if (note.TrackIndex < 0 || note.TrackIndex >= result.Count || note.DurationTicks <= 0)
                {
                    continue;
                }

                int startColumn = Math.Clamp((int)Math.Round(note.StartTick / (double)stepTicks), 0, columns - 1);
                result[note.TrackIndex][startColumn] = true;
            }

            return result;
        }

        private Rectangle GetGridBounds()
        {
            int width = Math.Max(1, this.pictureBox_pattern.ClientSize.Width);
            int height = Math.Max(1, this.pictureBox_pattern.ClientSize.Height);
            int labelWidth = Math.Clamp(width / 4, 130, 260);
            int left = Math.Min(labelWidth, Math.Max(1, width - 16));
            int top = Math.Min(26, Math.Max(1, height - 1));
            return new Rectangle(left, top, Math.Max(1, width - left - 8), Math.Max(1, height - top - 6));
        }

        private int PointToTick(Point point)
        {
            Rectangle grid = this.GetGridBounds();
            double virtualGridWidth = grid.Width * this.horizontalZoom;
            double virtualX = point.X - grid.Left + this.hScrollBar_pattern.Value;
            double fraction = Math.Clamp(virtualX / virtualGridWidth, 0, 1);
            return (int)Math.Round(fraction * this.bars * BreakbeatGenerator_V2.PatternTicksPerBar);
        }

        private async void button_hear_Click(object? sender, EventArgs e)
        {
            if (this.hearCancellationTokenSource is not null)
            {
                this.hearCancellationTokenSource.Cancel();
                if (this.previewAudio?.Playing == true)
                {
                    await this.previewAudio.StopAsync();
                }

                return;
            }

            await this.StopNotePreviewAsync();
            CancellationTokenSource cancellationTokenSource = new();
            this.hearCancellationTokenSource = cancellationTokenSource;
            this.button_hear.Text = "Stop";
            try
            {
                int[] selectedBarsForPreview = this.selectedBars.OrderBy(bar => bar).ToArray();
                AudioObj? renderedPattern = await BreakbeatGenerator_V2.RenderPatternNotesAsync(
                    this.Notes,
                    this.samples,
                    this.bars,
                    (float)this.numericUpDown_bpm.Value,
                    this.currentResolution,
                    this.swing,
                    "BreakbeatPreview",
                    cancellationTokenSource.Token);

                if (renderedPattern is null)
                {
                    return;
                }

                if (selectedBarsForPreview.Length > 0)
                {
                    try
                    {
                        this.previewAudio = BreakbeatGenerator_V2.ExtractPatternBars(
                            renderedPattern,
                            selectedBarsForPreview,
                            (float)this.numericUpDown_bpm.Value);
                    }
                    finally
                    {
                        renderedPattern.Dispose();
                    }

                    this.previewBarMap = selectedBarsForPreview;
                    this.previewAudio.UpdateLoopFraction(
                        0,
                        this.previewAudio.Data.LongLength,
                        this.previewAudio.Data.LongLength,
                        loopEnabled: true,
                        adjustPosition: false);
                }
                else
                {
                    this.previewAudio = renderedPattern;
                    this.previewBarMap = [];
                }

                this.previewAudio.SetPosition(0);

                this.timer_previewCaret.Start();
                await this.previewAudio.PlayAsync(cancellationTokenSource.Token, initialVolume: 1f);
                while (this.previewAudio.Playing && !cancellationTokenSource.IsCancellationRequested)
                {
                    await Task.Delay(25);
                }
            }

            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                LogCollection.Log("Breakbeat pattern preview failed.");
                LogCollection.Log(ex);
                if (!this.IsDisposed)
                {
                    WindowMainStaticHelpers.ShowErrorWithCopyButton(this, "Breakbeat Pattern Preview", ex);
                }
            }
            finally
            {
                this.timer_previewCaret.Stop();
                if (this.previewAudio?.Playing == true)
                {
                    await this.previewAudio.StopAsync();
                }

                this.previewAudio?.Dispose();
                this.previewAudio = null;
                this.previewBarMap = [];
                if (!this.IsDisposed && !this.pictureBox_pattern.IsDisposed)
                {
                    this.pictureBox_pattern.Invalidate();
                }

                if (ReferenceEquals(this.hearCancellationTokenSource, cancellationTokenSource))
                {
                    this.hearCancellationTokenSource = null;
                    if (!this.IsDisposed)
                    {
                        this.button_hear.Text = "Hear";
                    }
                }

                cancellationTokenSource.Dispose();
            }
        }

        private async Task PrehearNoteAsync(BreakbeatPatternNote note)
        {
            await this.StopWholePatternPreviewAsync();
            await this.StopNotePreviewAsync();

            CancellationTokenSource cancellationTokenSource = new();
            this.notePreviewCancellationTokenSource = cancellationTokenSource;
            try
            {
                int previewBars = Math.Max(1, (int)Math.Ceiling(note.DurationTicks / (double)BreakbeatGenerator_V2.PatternTicksPerBar));
                BreakbeatPatternNote previewNote = note with { StartTick = 0 };
                this.notePreviewAudio = await BreakbeatGenerator_V2.RenderPatternNotesAsync(
                    [previewNote],
                    this.samples,
                    previewBars,
                    (float)this.numericUpDown_bpm.Value,
                    this.currentResolution,
                    0f,
                    "BreakbeatNotePreview",
                    cancellationTokenSource.Token);

                if (this.notePreviewAudio is null)
                {
                    return;
                }

                await this.notePreviewAudio.PlayAsync(cancellationTokenSource.Token, initialVolume: 1f);
                while (this.notePreviewAudio.Playing && !cancellationTokenSource.IsCancellationRequested)
                {
                    await Task.Delay(20);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                LogCollection.Log("Breakbeat note preview failed.");
                LogCollection.Log(ex);
                if (!this.IsDisposed)
                {
                    WindowMainStaticHelpers.ShowErrorWithCopyButton(this, "Breakbeat Note Preview", ex);
                }
            }
            finally
            {
                if (this.notePreviewAudio?.Playing == true)
                {
                    await this.notePreviewAudio.StopAsync();
                }

                this.notePreviewAudio?.Dispose();
                this.notePreviewAudio = null;
                if (ReferenceEquals(this.notePreviewCancellationTokenSource, cancellationTokenSource))
                {
                    this.notePreviewCancellationTokenSource = null;
                }

                cancellationTokenSource.Dispose();
            }
        }

        private async Task StopNotePreviewAsync()
        {
            CancellationTokenSource? cancellationTokenSource = this.notePreviewCancellationTokenSource;
            if (cancellationTokenSource is null)
            {
                return;
            }

            cancellationTokenSource.Cancel();
            if (this.notePreviewAudio?.Playing == true)
            {
                await this.notePreviewAudio.StopAsync();
            }

            while (ReferenceEquals(this.notePreviewCancellationTokenSource, cancellationTokenSource))
            {
                await Task.Delay(10);
            }
        }

        private async Task StopWholePatternPreviewAsync()
        {
            CancellationTokenSource? cancellationTokenSource = this.hearCancellationTokenSource;
            if (cancellationTokenSource is null)
            {
                return;
            }

            cancellationTokenSource.Cancel();
            if (this.previewAudio?.Playing == true)
            {
                await this.previewAudio.StopAsync();
            }

            while (ReferenceEquals(this.hearCancellationTokenSource, cancellationTokenSource))
            {
                await Task.Delay(10);
            }
        }

            private void numericUpDown_bpm_ValueChanged(object? sender, EventArgs e)
            {
                if (this.initializing)
                {
                    return;
                }

                this.hearCancellationTokenSource?.Cancel();
                this.notePreviewCancellationTokenSource?.Cancel();
                this.pictureBox_pattern.Invalidate();
            }

            private void numericUpDown_resolution_ValueChanged(object? sender, EventArgs e)
            {
                if (this.initializing)
                {
                    return;
                }

                int requested = (int)this.numericUpDown_resolution.Value;
                int snapped = SnapResolution(requested, this.currentResolution);
                if (snapped != requested)
                {
                    this.numericUpDown_resolution.Value = snapped;
                    return;
                }

                this.currentResolution = snapped;
                this.ConfigurePatternScrollBar();
                this.hearCancellationTokenSource?.Cancel();
                this.notePreviewCancellationTokenSource?.Cancel();
                this.pictureBox_pattern.Invalidate();
            }

            private static int SnapResolution(int requested, int previous)
            {
                int[] resolutions = [1, 2, 4, 8, 16, 32, 64, 128, 256];
                if (resolutions.Contains(requested))
                {
                    return requested;
                }

                return requested > previous
                    ? resolutions.First(value => value > requested || value == 256)
                    : resolutions.Last(value => value < requested || value == 1);
            }

            private void timer_previewCaret_Tick(object? sender, EventArgs e)
            {
                this.pictureBox_pattern.Invalidate();
            }

            private void pictureBox_pattern_Resize(object? sender, EventArgs e)
            {
                this.ConfigurePatternScrollBar();
                this.pictureBox_pattern.Invalidate();
            }

            private void button_addBar_Click(object? sender, EventArgs e)
            {
                if (this.bars >= 64)
                {
                    return;
                }

                this.bars++;
                this.ConfigurePatternScrollBar();
                this.pictureBox_pattern.Invalidate();
            }

            private void button_removeBar_Click(object? sender, EventArgs e)
            {
                if (this.bars <= 1)
                {
                    return;
                }

                this.bars--;
                this.selectedBars.RemoveWhere(bar => bar >= this.bars);
                int patternEndTick = this.bars * BreakbeatGenerator_V2.PatternTicksPerBar;
                this.notes.RemoveAll(note => note.StartTick >= patternEndTick);
                for (int index = 0; index < this.notes.Count; index++)
                {
                    BreakbeatPatternNote note = this.notes[index];
                    int duration = Math.Min(note.DurationTicks, patternEndTick - note.StartTick);
                    this.notes[index] = note with { DurationTicks = duration };
                }

                this.ConfigurePatternScrollBar();
                this.pictureBox_pattern.Invalidate();
            }

            private async void BreakbeatPatternEditorDialog_FormClosing(object? sender, FormClosingEventArgs e)
            {
                CancellationTokenSource? hearCancellationTokenSource = this.hearCancellationTokenSource;
                CancellationTokenSource? notePreviewCancellationTokenSource = this.notePreviewCancellationTokenSource;
                if (hearCancellationTokenSource is null && notePreviewCancellationTokenSource is null)
                {
                    return;
                }

                e.Cancel = true;
                hearCancellationTokenSource?.Cancel();
                notePreviewCancellationTokenSource?.Cancel();
                if (this.previewAudio?.Playing == true)
                {
                    await this.previewAudio.StopAsync();
                }

                if (this.notePreviewAudio?.Playing == true)
                {
                    await this.notePreviewAudio.StopAsync();
                }

                while ((hearCancellationTokenSource is not null && ReferenceEquals(this.hearCancellationTokenSource, hearCancellationTokenSource))
                    || (notePreviewCancellationTokenSource is not null && ReferenceEquals(this.notePreviewCancellationTokenSource, notePreviewCancellationTokenSource)))
                {
                    await Task.Delay(10);
                }

                if (!this.IsDisposed)
                {
                    this.Close();
                }
            }

            private sealed class BufferedPictureBox : PictureBox
            {
                public BufferedPictureBox()
                {
                    this.SetStyle(
                        ControlStyles.AllPaintingInWmPaint
                        | ControlStyles.OptimizedDoubleBuffer
                        | ControlStyles.ResizeRedraw
                        | ControlStyles.Selectable,
                        true);
                }
            }

        private void button_save_Click(object? sender, EventArgs e)
        {
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void button_help_Click(object? sender, EventArgs e)
        {
            string helpText = string.Join(Environment.NewLine,
            [
                "NOTES",
                "Left-click an empty cell to add a hit. Drag across empty cells to place a longer note.",
                "Left-click an existing note to play it. Right-click or right-drag to delete notes.",
                "Pre-hear controls automatic preview after placing or resizing notes; clicking an existing note always plays it.",
                "",
                "RESIZING AND PLAYBACK MODES",
                "Hover over a note for the horizontal resize cursor. Drag its left or right half to resize that edge.",
                "A resize requires at least half a visible grid cell of horizontal movement from mouse-down to mouse-up. Smaller movement is treated as a click.",
                "Hold Ctrl while resizing to switch between TimeStretch and Varispeed. Returning to the original note length restores the normal green note.",
                "Ctrl-click a manually resized note to toggle its mode. Ctrl-click an empty cell to add a one-step shortened TimeStretch note; Ctrl-click it again to switch to Varispeed.",
                "TimeStretch preserves pitch. Varispeed changes pitch with the note length.",
                "",
                "NOTE COLORS",
                "Green: original length. Blue / light blue: longer / shorter TimeStretch. Yellow / light yellow: longer / shorter Varispeed.",
                "",
                "ZOOM AND GRID",
                "Steps per bar can be set to powers of two up to 256. Changing the resolution does not change the zoom.",
                "Ctrl+mouse wheel zooms around the pointer. Mouse wheel without Ctrl scrolls horizontally while zoomed; use the bottom scrollbar for precise navigation.",
                "",
                "BARS AND PREVIEW",
                "Click numbered bar headers to select or deselect bars. With no bars selected, Hear plays the whole pattern once.",
                "With bars selected, Hear loops only those bars in ascending order; the caret follows the selected bars and jumps at loop boundaries.",
                "Hear starts or stops pattern playback. Save keeps changes; Cancel closes without saving."
            ]);

            MessageBox.Show(
                this,
                helpText,
                "Breakbeat Pattern Editor Help",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }
}