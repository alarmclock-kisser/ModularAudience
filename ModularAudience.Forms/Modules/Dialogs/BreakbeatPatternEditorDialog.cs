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
        private CancellationTokenSource? hearCancellationTokenSource;
        private AudioObj? previewAudio;
        private CancellationTokenSource? notePreviewCancellationTokenSource;
        private AudioObj? notePreviewAudio;
        private bool drawing;
        private int drawingRow;
        private int drawingStartColumn;
        private MouseButtons drawingButton;
        private BreakbeatPatternNote? drawingNote;
        private int currentResolution = 4;
        private bool initializing = true;

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
            this.notes = existingNotes?.Select(note => note with { }).ToList()
                ?? BreakbeatGenerator_V2.CreatePatternNotesFromGrid(this.pattern, this.samples, Math.Max(1, resolution), (float)this.Bpm, this.currentResolution);
            this.initializing = false;
            this.Text = "Breakbeat Pattern Editor";
            this.pictureBox_pattern.Cursor = Cursors.Cross;
            this.FormClosing += this.BreakbeatPatternEditorDialog_FormClosing;
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
            float cellWidth = grid.Width / (float)columns;
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
            using Brush barBrush = new SolidBrush(Color.FromArgb(158, 164, 173));
            using Pen rowPen = new(Color.FromArgb(75, 79, 87));
            using Pen stepPen = new(Color.FromArgb(62, 66, 73));
            using Pen barPen = new(Color.FromArgb(112, 119, 128), 2f);
            using Pen notePen = new(Color.FromArgb(28, 30, 34));

            for (int row = 0; row < rows; row++)
            {
                float y = grid.Top + row * cellHeight;
                RectangleF labelBounds = new(8, y, Math.Max(1, grid.Left - 16), Math.Max(1f, cellHeight));
                string label = row < this.rowLabels.Length ? this.rowLabels[row] : $"Track {row + 1}";
                e.Graphics.DrawString(label, labelFont, labelBrush, labelBounds, labelFormat);
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

                    float x = grid.Left + column * cellWidth;
                    e.Graphics.DrawLine(stepPen, x, grid.Top, x, grid.Bottom);
                }
            }

            for (int bar = 0; bar <= this.bars; bar++)
            {
                int column = Math.Min(columns, bar * this.currentResolution);
                float x = grid.Left + column * cellWidth;
                e.Graphics.DrawLine(barPen, x, grid.Top, x, grid.Bottom);
                if (bar < this.bars && column < columns)
                {
                    RectangleF barBounds = new(grid.Left + column * cellWidth + 3f, 2f, Math.Max(1f, this.currentResolution * cellWidth - 6f), 20f);
                    e.Graphics.DrawString((bar + 1).ToString(), barFont, barBrush, barBounds);
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

                float x = grid.Left + (float)(clippedStart / patternTicks * grid.Width);
                float noteWidth = (float)((clippedEnd - clippedStart) / patternTicks * grid.Width);
                float y = grid.Top + note.TrackIndex * cellHeight;
                float inset = Math.Min(2f, Math.Min(cellWidth, cellHeight) * 0.12f);
                RectangleF hit = new(x + inset, y + inset, Math.Max(1f, noteWidth - (2f * inset)), Math.Max(1f, cellHeight - (2f * inset)));
                e.Graphics.FillRectangle(note.TimeStretch ? stretchedHitBrush : hitBrush, hit);
                e.Graphics.DrawRectangle(notePen, hit.X, hit.Y, hit.Width, hit.Height);
            }

            if (this.previewAudio is not null && this.previewAudio.PlayerPlaying)
            {
                double patternDuration = this.bars * 240.0 / Math.Max(1.0, (double)this.Bpm);
                float caretX = grid.Left + (float)Math.Clamp(this.previewAudio.CurrentTime.TotalSeconds / patternDuration, 0, 1) * grid.Width;
                using Pen caretPen = new(Color.OrangeRed, 2f);
                e.Graphics.DrawLine(caretPen, caretX, grid.Top, caretX, grid.Bottom);
            }
        }

        private void pictureBox_pattern_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button is not (MouseButtons.Left or MouseButtons.Right) || !this.TryGetCell(e.Location, out int row, out int column))
            {
                return;
            }

            this.drawingButton = e.Button;
            this.drawingRow = row;
            this.drawingStartColumn = column;
            if (e.Button == MouseButtons.Left)
            {
                int stepTicks = this.GetTicksPerStep();
                int startTick = column * stepTicks;
                int minimumDuration = BreakbeatGenerator_V2.GetMinimumNoteDurationTicks(this.samples, row, (float)this.Bpm, this.currentResolution);
                BreakbeatPatternNote newNote = new(row, startTick, minimumDuration);
                if (this.HasOverlappingNote(newNote))
                {
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
                return;
            }

            if (this.drawingButton == MouseButtons.Right)
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
                BreakbeatPatternNote updated = this.drawingNote with
                {
                    StartTick = firstColumn * stepTicks,
                    DurationTicks = Math.Max(minimumDuration, draggedDuration),
                    TimeStretch = draggedDuration > minimumDuration
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

            this.drawing = false;
            BreakbeatPatternNote? placedNote = this.drawingButton == MouseButtons.Left ? this.drawingNote : null;
            bool shouldPrehear = placedNote is not null && this.checkBox_preHear.Checked;
            this.drawingNote = null;
            this.pictureBox_pattern.Capture = false;

            if (placedNote is { TimeStretch: true })
            {
                List<BreakbeatPatternNote> notesWithoutRetriggers = BreakbeatGenerator_V2.RemoveRetriggersCoveredByStretchedNote(this.notes, placedNote);
                this.notes.Clear();
                this.notes.AddRange(notesWithoutRetriggers);
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

        private int PointToColumn(Point point)
        {
            Rectangle grid = this.GetGridBounds();
            int count = Math.Max(1, this.bars * this.currentResolution);
            return Math.Clamp((int)Math.Floor((point.X - grid.Left) / (grid.Width / (double)count)), 0, count - 1);
        }

        private int GetTicksPerStep() => Math.Max(1, (int)Math.Round(BreakbeatGenerator_V2.PatternTicksPerBar / (double)this.currentResolution));

        private List<BreakbeatPatternNote> GetNotesWithoutCoveredRetriggers()
        {
            List<BreakbeatPatternNote> result = this.notes.ToList();
            BreakbeatPatternNote[] stretchedNotes = result
                .Where(note => note.TimeStretch)
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
            double fraction = Math.Clamp((point.X - grid.Left) / (double)grid.Width, 0, 1);
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
                this.previewAudio = await BreakbeatGenerator_V2.RenderPatternNotesAsync(
                    this.Notes,
                    this.samples,
                    this.bars,
                    (float)this.numericUpDown_bpm.Value,
                    this.currentResolution,
                    this.swing,
                    "BreakbeatPreview",
                    cancellationTokenSource.Token);

                this.timer_previewCaret.Start();
                if (this.previewAudio is null)
                {
                    return;
                }

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
                this.hearCancellationTokenSource?.Cancel();
                this.notePreviewCancellationTokenSource?.Cancel();
                this.pictureBox_pattern.Invalidate();
            }

            private static int SnapResolution(int requested, int previous)
            {
                int[] resolutions = [1, 2, 4, 8, 16, 32, 64];
                if (resolutions.Contains(requested))
                {
                    return requested;
                }

                return requested > previous
                    ? resolutions.First(value => value > requested || value == 64)
                    : resolutions.Last(value => value < requested || value == 1);
            }

            private void timer_previewCaret_Tick(object? sender, EventArgs e)
            {
                this.pictureBox_pattern.Invalidate();
            }

            private void pictureBox_pattern_Resize(object? sender, EventArgs e)
            {
                this.pictureBox_pattern.Invalidate();
            }

            private void button_addBar_Click(object? sender, EventArgs e)
            {
                if (this.bars >= 64)
                {
                    return;
                }

                this.bars++;
                this.pictureBox_pattern.Invalidate();
            }

            private void button_removeBar_Click(object? sender, EventArgs e)
            {
                if (this.bars <= 1)
                {
                    return;
                }

                this.bars--;
                int patternEndTick = this.bars * BreakbeatGenerator_V2.PatternTicksPerBar;
                this.notes.RemoveAll(note => note.StartTick >= patternEndTick);
                for (int index = 0; index < this.notes.Count; index++)
                {
                    BreakbeatPatternNote note = this.notes[index];
                    int duration = Math.Min(note.DurationTicks, patternEndTick - note.StartTick);
                    this.notes[index] = note with { DurationTicks = duration };
                }

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
                    this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                }
            }

        private void button_save_Click(object? sender, EventArgs e)
        {
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}