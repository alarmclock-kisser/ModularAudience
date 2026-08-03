using ModularAudience.Audio;
using ModularAudience.Audio.Midi;
using ModularAudience.Generators;
using System.Drawing.Drawing2D;

namespace ModularAudience.Forms.Modules.Dialogs
{
    public partial class MidiEditor : Form
    {
        public MidiEditSelection MidiEditSelection { get; private set; }

        private readonly MidiWindow? sourceMidiWindow;
        private Point? editStart;
        private Point? rectangleSelectionStart;
        private Point? rectangleSelectionEnd;
        private MouseButtons editButton;
        private int editNote;
        private long editPreviousLength;
        private bool panning;
        private Point panStart;
        private long panViewStartTick;
        private long panViewEndTick;
        private long viewStartTick;
        private long viewEndTick;
        private long editorLengthTicks;
        private double pixelsPerTick;
        private CancellationTokenSource? playbackCts;
        private AudioObj? previewAudio;
        private bool previewCaretVisible;
        private bool updatingScrollBar;

        private const double EmptyTailRatio = 0.20;

        public MidiEditor(MidiEditSelection midiEditSelection, MidiWindow? sourceMidiWindow = null)
        {
            this.MidiEditSelection = midiEditSelection;
            this.sourceMidiWindow = sourceMidiWindow;
            this.InitializeComponent();
            this.numericUpDown_pitchFrequency.Value = (decimal) Math.Clamp(midiEditSelection.MidiFile.PitchFrequency, 1.0, 1000.0);
            long defaultViewLength = this.DefaultViewLengthTicks;
            long initialViewLength = Math.Max(this.GetRequiredEditorLength(), defaultViewLength);
            this.editorLengthTicks = initialViewLength;
            this.pixelsPerTick = Math.Max(0.001, (this.pictureBox_editor.ClientSize.Width - 44) / (double) initialViewLength);
            this.viewStartTick = 0;
            this.viewEndTick = initialViewLength;
            this.pictureBox_editor.Cursor = Cursors.Cross;
            this.UpdateScrollBar();
        }

        private MidiTrackData SelectedTrack => this.MidiEditSelection.MidiFile.Tracks[0];

        private int LowestNote => ParseMidiNote(this.MidiEditSelection.LowestNoteName);

        private int HighestNote => ParseMidiNote(this.MidiEditSelection.HighestNoteName);

        private int NoteGranularity => Math.Max(1, this.domainUpDown_noteGranularity.SelectedIndex + 1);

        private long GridTicks => Math.Max(1, this.MidiEditSelection.MidiFile.TicksPerQuarterNote / this.NoteGranularity);

        private double PitchFrequency => (double) this.numericUpDown_pitchFrequency.Value;

        private long DefaultViewLengthTicks => this.GridTicks * 8;

        private void pictureBox_editor_Paint(object? sender, PaintEventArgs e)
        {
            int lowest = this.LowestNote;
            int highest = Math.Max(lowest, this.HighestNote);
            int noteCount = highest - lowest + 1;
            int width = Math.Max(1, this.pictureBox_editor.ClientSize.Width - 44);
            int height = Math.Max(1, this.pictureBox_editor.ClientSize.Height - 20);
            long lengthTicks = Math.Max(this.GridTicks, this.editorLengthTicks);
            long visibleStartTick = Math.Clamp(this.viewStartTick, 0, Math.Max(0, lengthTicks - this.GridTicks));
            long visibleLengthTicks = Math.Clamp(this.viewEndTick - this.viewStartTick, this.GridTicks, lengthTicks);
            long visibleEndTick = Math.Min(lengthTicks, visibleStartTick + visibleLengthTicks);
            e.Graphics.SmoothingMode = SmoothingMode.HighQuality;
            e.Graphics.Clear(Color.FromArgb(24, 24, 28));

            using Pen gridPen = new(Color.FromArgb(48, 48, 56));
            using Pen octavePen = new(Color.FromArgb(75, 75, 85));
            using Font font = new("Segoe UI", 8f);
            for (int note = lowest; note <= highest; note++)
            {
                float y = NoteToY(note, lowest, noteCount, height);
                e.Graphics.DrawLine(note % 12 == 0 ? octavePen : gridPen, 44, y, 44 + width, y);
                e.Graphics.DrawString(MidiNoteName(note), font, Brushes.Gainsboro, 2, y - 7);
            }

            long firstGridTick = visibleStartTick - visibleStartTick % this.GridTicks;
            for (long tick = firstGridTick; tick <= visibleEndTick; tick += this.GridTicks)
            {
                float x = 44 + (float) ((tick - visibleStartTick) * this.pixelsPerTick);
                e.Graphics.DrawLine(gridPen, x, 0, x, height);
            }

            foreach (MidiNoteData note in this.SelectedTrack.Notes)
            {
                if (note.NoteNumber < lowest || note.NoteNumber > highest)
                {
                    continue;
                }

                long noteEndTick = note.StartTick + note.DurationTicks;
                if (noteEndTick <= visibleStartTick || note.StartTick >= visibleEndTick)
                {
                    continue;
                }

                long clippedStartTick = Math.Max(note.StartTick, visibleStartTick);
                long clippedEndTick = Math.Min(noteEndTick, visibleEndTick);
                float x = 44 + (float) ((clippedStartTick - visibleStartTick) * this.pixelsPerTick);
                float noteWidth = Math.Max(2, (float) ((clippedEndTick - clippedStartTick) * this.pixelsPerTick));
                float y = NoteToY(note.NoteNumber, lowest, noteCount, height);
                float noteHeight = Math.Max(3, height / (float) noteCount - 1);
                using Brush brush = new SolidBrush(Color.DeepSkyBlue);
                e.Graphics.FillRectangle(brush, x, y, Math.Min(noteWidth, 44 + width - x), noteHeight);
            }

            if (this.rectangleSelectionStart is Point rectangleStart && this.rectangleSelectionEnd is Point rectangleEnd)
            {
                Rectangle selectionRectangle = Rectangle.FromLTRB(
                    Math.Min(rectangleStart.X, rectangleEnd.X),
                    Math.Min(rectangleStart.Y, rectangleEnd.Y),
                    Math.Max(rectangleStart.X, rectangleEnd.X),
                    Math.Max(rectangleStart.Y, rectangleEnd.Y));
                using Pen selectionPen = new(Color.White, 1f) { DashStyle = DashStyle.Dash };
                e.Graphics.DrawRectangle(selectionPen, selectionRectangle);
            }

            if (this.previewCaretVisible && this.previewAudio?.PlayerPlaying == true && this.previewAudio.Duration > TimeSpan.Zero)
            {
                double secondsPerTick = 60.0 / Math.Max(1.0, this.sourceMidiWindow?.PreviewBpm ?? 120.0) / Math.Max(1, this.MidiEditSelection.MidiFile.TicksPerQuarterNote);
                long previewTick = (long) Math.Round(this.previewAudio.CurrentTime.TotalSeconds / secondsPerTick);
                previewTick = Math.Clamp(previewTick, 0, Math.Max(this.GridTicks, this.SelectedTrack.LengthTicks));
                float caretX = 44 + (float) ((previewTick - visibleStartTick) * this.pixelsPerTick);
                using Pen caretPen = new(Color.Red, 2f);
                e.Graphics.DrawLine(caretPen, caretX, 0, caretX, height);
            }

        }

        private void pictureBox_editor_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button is not (MouseButtons.Left or MouseButtons.Right))
            {
                return;
            }

            if (e.Button == MouseButtons.Right && (Control.ModifierKeys & Keys.Shift) != Keys.None)
            {
                this.rectangleSelectionStart = ClampToEditor(e.Location);
                this.rectangleSelectionEnd = this.rectangleSelectionStart;
                this.editButton = MouseButtons.Right;
                this.editPreviousLength = this.editorLengthTicks;
                this.pictureBox_editor.Capture = true;
                this.pictureBox_editor.Invalidate();
                return;
            }

            if ((Control.ModifierKeys & Keys.Control) != Keys.None)
            {
                this.panning = true;
                this.panStart = e.Location;
                this.panViewStartTick = this.viewStartTick;
                this.panViewEndTick = this.viewEndTick;
                this.pictureBox_editor.Capture = true;
                this.pictureBox_editor.Cursor = Cursors.SizeWE;
                return;
            }

            this.editStart = ClampToEditor(e.Location);
            this.editButton = e.Button;
            this.editNote = PointToNote(this.editStart.Value.Y);
            this.editPreviousLength = this.editorLengthTicks;
            this.pictureBox_editor.Capture = true;
            this.ApplyNoteRange(this.editStart.Value, this.editStart.Value, e.Button == MouseButtons.Left, false);
            this.pictureBox_editor.Invalidate();
        }

        private void domainUpDown_noteGranularity_SelectedItemChanged(object? sender, EventArgs e)
        {
            long visibleLength = Math.Max(this.GridTicks, this.viewEndTick - this.viewStartTick);
            this.editorLengthTicks = Math.Max(this.editorLengthTicks, this.GetRequiredEditorLength());
            this.SetView(this.viewStartTick, this.viewStartTick + visibleLength);
            this.UpdateScrollBar();
            this.pictureBox_editor.Invalidate();
        }

        private void pictureBox_editor_MouseMove(object? sender, MouseEventArgs e)
        {
            if (this.panning)
            {
                this.PanView(e.Location.X - this.panStart.X);
                return;
            }

            if (this.editStart == null)
            {
                if (this.rectangleSelectionStart is Point && this.editButton == MouseButtons.Right)
                {
                    this.rectangleSelectionEnd = ClampToEditor(e.Location);
                    this.pictureBox_editor.Invalidate();
                }
                return;
            }

            Point current = ClampToEditor(e.Location);
            if (this.editButton == MouseButtons.Right)
            {
                this.ApplyNoteRange(current, current, false, false);
            }
            else if (PointToNote(current.Y) == this.editNote)
            {
                this.ApplyNoteRange(this.editStart.Value, current, this.editButton == MouseButtons.Left, false);
            }

            this.pictureBox_editor.Invalidate();
        }

        private async void pictureBox_editor_MouseUp(object? sender, MouseEventArgs e)
        {
            if (this.panning)
            {
                this.panning = false;
                this.pictureBox_editor.Capture = false;
                this.pictureBox_editor.Cursor = Cursors.Cross;
                return;
            }

            if (this.rectangleSelectionStart is Point rectangleStart && e.Button == MouseButtons.Right)
            {
                Point rectangleEnd = ClampToEditor(e.Location);
                this.DeleteNotesInRectangle(rectangleStart, rectangleEnd);
                this.rectangleSelectionStart = null;
                this.rectangleSelectionEnd = null;
                this.pictureBox_editor.Capture = false;
                this.UpdateEditorLengthAfterNoteChange(this.editPreviousLength);
                this.pictureBox_editor.Invalidate();
                return;
            }

            if (this.editStart is not Point start || e.Button != this.editButton)
            {
                return;
            }

            Point end = ClampToEditor(e.Location);
            this.editStart = null;
            this.pictureBox_editor.Capture = false;
            this.UpdateEditorLengthAfterNoteChange(this.editPreviousLength);
            this.pictureBox_editor.Invalidate();

            if (this.checkBox_preview.Checked && this.editButton == MouseButtons.Left)
            {
                Point previewPoint = new((start.X + end.X) / 2, start.Y);
                MidiNoteData? previewNote = this.FindNoteAt(previewPoint);
                if (previewNote != null)
                {
                    await this.PreviewNoteAsync(previewNote);
                }
            }
        }

        private void DeleteNotesInRectangle(Point first, Point second)
        {
            int left = Math.Min(first.X, second.X);
            int right = Math.Max(first.X, second.X);
            int top = Math.Min(first.Y, second.Y);
            int bottom = Math.Max(first.Y, second.Y);
            long startTick = PointToTick(left);
            long endTick = PointToTick(right) + this.GridTicks;
            int height = Math.Max(1, this.pictureBox_editor.ClientSize.Height - 20);
            int count = Math.Max(1, HighestNote - LowestNote + 1);

            this.SelectedTrack.Notes.RemoveAll(note =>
            {
                float noteTop = NoteToY(note.NoteNumber, LowestNote, count, height);
                float noteBottom = noteTop + Math.Max(3, height / (float) count - 1);
                long noteEndTick = note.StartTick + note.DurationTicks;
                bool overlapsTime = note.StartTick <= endTick && noteEndTick >= startTick;
                bool overlapsNote = noteTop <= bottom && noteBottom >= top;
                return overlapsTime && overlapsNote;
            });
        }

        private void pictureBox_editor_MouseWheel(object? sender, MouseEventArgs e)
        {
            if ((Control.ModifierKeys & Keys.Control) == Keys.None)
            {
                long scrollTicks = this.GridTicks * Math.Max(1, Math.Abs(e.Delta) / 120) * 4;
                this.ScrollView(e.Delta < 0 ? scrollTicks : -scrollTicks);
                return;
            }

            long totalLength = Math.Max(this.GridTicks, this.editorLengthTicks);
            bool zoomingOut = e.Delta < 0;
            if (!zoomingOut && totalLength <= this.GridTicks)
            {
                return;
            }

            long currentLength = Math.Max(this.GridTicks, this.viewEndTick - this.viewStartTick);
            double zoomFactor = zoomingOut ? 1.2 : 1.0 / 1.2;
            long newLength = Math.Max(this.GridTicks, (long) Math.Round(currentLength * zoomFactor));
            if (!zoomingOut)
            {
                newLength = Math.Min(newLength, totalLength);
            }

            int width = Math.Max(1, this.pictureBox_editor.ClientSize.Width - 44);
            double mouseRatio = Math.Clamp((e.X - 44) / (double) width, 0, 1);
            long tickAtMouse = this.viewStartTick + (long) Math.Round(currentLength * mouseRatio);
            long newStart = Math.Max(0, tickAtMouse - (long) Math.Round(newLength * mouseRatio));
            if (zoomingOut)
            {
                this.editorLengthTicks = Math.Max(this.editorLengthTicks, newStart + newLength);
            }

            this.SetView(newStart, newStart + newLength);
            this.pixelsPerTick = width / (double) Math.Max(this.GridTicks, newLength);
            this.UpdateScrollBar();
            this.pictureBox_editor.Invalidate();
        }

        private void ScrollView(long tickDelta)
        {
            long visibleLength = Math.Max(this.GridTicks, this.viewEndTick - this.viewStartTick);
            this.SetView(this.viewStartTick + tickDelta, this.viewStartTick + tickDelta + visibleLength);
            this.UpdateScrollBar();
            this.pictureBox_editor.Invalidate();
        }

        private void MidiEditor_Resize(object? sender, EventArgs e)
        {
            int width = Math.Max(1, this.pictureBox_editor.ClientSize.Width - 44);
            long visibleLength = Math.Max(this.GridTicks, (long) Math.Ceiling(width / this.pixelsPerTick));
            this.viewEndTick = this.viewStartTick + visibleLength;
            this.editorLengthTicks = Math.Max(this.editorLengthTicks, this.viewEndTick);

            this.UpdateScrollBar();
            this.pictureBox_editor.Invalidate();
        }

        private void PanView(int pixelDelta)
        {
            long totalLength = Math.Max(this.GridTicks, this.editorLengthTicks);
            long visibleLength = Math.Max(this.GridTicks, this.panViewEndTick - this.panViewStartTick);
            int width = Math.Max(1, this.pictureBox_editor.ClientSize.Width - 44);
            long tickDelta = (long) Math.Round(-pixelDelta / (double) width * visibleLength);
            this.SetView(this.panViewStartTick + tickDelta, this.panViewEndTick + tickDelta);
            this.UpdateScrollBar();
            this.pictureBox_editor.Invalidate();
        }

        private void SetView(long startTick, long endTick)
        {
            long totalLength = Math.Max(this.GridTicks, this.editorLengthTicks);
            long visibleLength = Math.Clamp(endTick - startTick, this.GridTicks, totalLength);
            long clampedStart = Math.Clamp(startTick, 0, Math.Max(0, totalLength - visibleLength));
            this.viewStartTick = clampedStart;
            this.viewEndTick = clampedStart + visibleLength;
        }

        private void hScrollBar_editor_Scroll(object? sender, ScrollEventArgs e)
        {
            if (this.updatingScrollBar)
            {
                return;
            }

            long totalLength = Math.Max(this.GridTicks, this.editorLengthTicks);
            long visibleLength = Math.Max(this.GridTicks, this.viewEndTick - this.viewStartTick);
            long scrollableLength = Math.Max(0, totalLength - visibleLength);
            if (scrollableLength == 0)
            {
                return;
            }

            int scrollRange = Math.Max(1, this.hScrollBar_editor.Maximum - this.hScrollBar_editor.LargeChange + 1);
            long newStart = (long) Math.Round(e.NewValue / (double) scrollRange * scrollableLength);
            this.SetView(newStart, newStart + visibleLength);
            this.pictureBox_editor.Invalidate();
        }

        private void UpdateScrollBar()
        {
            if (this.hScrollBar_editor == null || this.hScrollBar_editor.IsDisposed)
            {
                return;
            }

            long totalLength = Math.Max(this.GridTicks, this.editorLengthTicks);
            long visibleLength = Math.Max(this.GridTicks, this.viewEndTick - this.viewStartTick);
            bool scrollable = totalLength > visibleLength;
            this.updatingScrollBar = true;
            try
            {
                this.hScrollBar_editor.Enabled = scrollable;
                this.hScrollBar_editor.Visible = scrollable;
                if (!scrollable)
                {
                    this.hScrollBar_editor.Value = this.hScrollBar_editor.Minimum;
                    return;
                }

                int scrollRange = Math.Max(1, this.hScrollBar_editor.Maximum - this.hScrollBar_editor.LargeChange + 1);
                long scrollableLength = totalLength - visibleLength;
                int value = (int) Math.Clamp(Math.Round(this.viewStartTick / (double) scrollableLength * scrollRange), 0, scrollRange);
                this.hScrollBar_editor.Value = value;
            }
            finally
            {
                this.updatingScrollBar = false;
            }
        }

        private long GetRequiredEditorLength()
        {
            long lastNoteEnd = this.SelectedTrack.Notes.Count == 0
                ? this.GridTicks
                : this.SelectedTrack.Notes.Max(note => note.StartTick + note.DurationTicks);
            long paddedLength = (long) Math.Ceiling(lastNoteEnd / (1.0 - EmptyTailRatio));
            return Math.Max(this.GridTicks, paddedLength);
        }

        private void UpdateEditorLengthAfterNoteChange(long previousLength)
        {
            this.editorLengthTicks = Math.Max(this.editorLengthTicks, this.GetRequiredEditorLength());

            this.UpdateScrollBar();
            this.pictureBox_editor.Invalidate();
        }

        private void ApplyNoteRange(Point first, Point second, bool add, bool updateView = true)
        {
            int firstNote = PointToNote(first.Y);
            int note = Math.Clamp(firstNote, LowestNote, HighestNote);
            long startTick = PointToTick(Math.Min(first.X, second.X));
            long endTick = Math.Max(startTick + this.GridTicks, PointToTick(Math.Max(first.X, second.X)) + this.GridTicks);
            long previousLength = this.SelectedTrack.LengthTicks;

            this.SelectedTrack.Notes.RemoveAll(candidate =>
            {
                if (candidate.NoteNumber != note)
                {
                    return false;
                }

                bool overlaps = candidate.StartTick < endTick && candidate.StartTick + candidate.DurationTicks > startTick;
                return overlaps;
            });

            if (add)
            {
                this.SelectedTrack.Notes.Add(new MidiNoteData
                {
                    NoteNumber = note,
                    Channel = 0,
                    Velocity = 100,
                    StartTick = startTick,
                    DurationTicks = endTick - startTick
                });

                this.SelectedTrack.ExtendLengthTo(endTick);
            }

            if (updateView)
            {
                this.UpdateEditorLengthAfterNoteChange(previousLength);
            }
        }

        private MidiNoteData? FindNoteAt(Point point)
        {
            int note = PointToNote(point.Y);
            long tick = PointToTick(point.X);
            return this.SelectedTrack.Notes.FirstOrDefault(candidate => candidate.NoteNumber == note && candidate.StartTick <= tick && candidate.StartTick + candidate.DurationTicks > tick);
        }

        private Point ClampToEditor(Point point)
        {
            return new Point(Math.Clamp(point.X, 44, Math.Max(44, this.pictureBox_editor.ClientSize.Width - 1)), Math.Clamp(point.Y, 0, Math.Max(0, this.pictureBox_editor.ClientSize.Height - 20)));
        }

        private long PointToTick(int x)
        {
            long relativeTick = (long) Math.Floor((x - 44) / this.pixelsPerTick / this.GridTicks) * this.GridTicks;
            long tick = this.viewStartTick + relativeTick;
            return Math.Clamp(tick, 0, Math.Max(0, this.editorLengthTicks));
        }

        private int PointToNote(int y)
        {
            int height = Math.Max(1, this.pictureBox_editor.ClientSize.Height - 20);
            int count = Math.Max(1, HighestNote - LowestNote + 1);
            return Math.Clamp(HighestNote - (int) Math.Floor(y / (double) height * count), LowestNote, HighestNote);
        }

        private async Task PreviewNoteAsync(MidiNoteData note)
        {
            MidiFileData previewFile = MidiFileData.CreateSingleNotePreview(
                this.MidiEditSelection.MidiFile,
                this.SelectedTrack.Index,
                note);
            await this.PreviewMidiAsync(previewFile, previewFile.Tracks[0].Index, false);
        }

        private async void button_play_Click(object? sender, EventArgs e)
        {
            if (this.playbackCts != null)
            {
                await this.StopPlaybackAsync();
                return;
            }

            if (this.sourceMidiWindow == null || this.SelectedTrack.Notes.Count == 0)
            {
                return;
            }

            try
            {
                await this.PreviewMidiAsync(this.MidiEditSelection.MidiFile, this.SelectedTrack.Index, true);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                LogCollection.Log($"MIDI editor playback failed: {ex}");
                MessageBox.Show(this, ex.Message, "MIDI editor playback failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (this.playbackCts == null)
                {
                    this.button_play.Text = "Play";
                }
            }
        }

        private async Task StopPlaybackAsync()
        {
            await this.StopPreviewAsync();

            this.button_play.Text = "Play";
        }

        private async Task PreviewMidiAsync(MidiFileData midi, int trackIndex, bool showCaret)
        {
            await this.StopPreviewAsync();
            if (this.sourceMidiWindow == null)
            {
                return;
            }

            CancellationTokenSource runCts = new();
            this.playbackCts = runCts;
            this.button_play.Text = "Stop";
            ProgressDialog? progressDialog = null;
            try
            {
                this.previewCaretVisible = showCaret;
                IProgress<double>? progress = null;
                if (showCaret)
                {
                    Progress<double> previewProgress = new(value => progressDialog?.Report(value));
                    progress = previewProgress;
                    progressDialog = new ProgressDialog("Rendering MIDI editor preview...", progress, windowCloseDelay: 0.0d, ct: runCts.Token, cancellationSource: runCts);
                    progressDialog.Show(this);
                    progressDialog.BringToFront();
                }

                this.previewAudio = await this.sourceMidiWindow.RenderMidiAsync(midi, trackIndex, runCts.Token, this.PitchFrequency, progress);
                progressDialog?.Complete();
                if (showCaret)
                {
                    this.timer_previewCaret.Start();
                }
                TaskCompletionSource playbackStopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
                await this.previewAudio.PlayAsync(runCts.Token, () => playbackStopped.TrySetResult());
                await playbackStopped.Task.WaitAsync(runCts.Token);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                if (progressDialog != null && !progressDialog.IsDisposed)
                {
                    progressDialog.Close();
                }

                if (ReferenceEquals(this.playbackCts, runCts))
                {
                    this.ResetPreviewState();
                }
                else
                {
                    runCts.Dispose();
                }
            }
        }

        private async Task StopPreviewAsync()
        {
            this.playbackCts?.Cancel();
            if (this.previewAudio != null)
            {
                await this.previewAudio.StopAsync();
            }

            this.ResetPreviewState();
        }

        private void ResetPreviewState()
        {
            this.timer_previewCaret.Stop();
            this.button_play.Text = "Play";
            this.playbackCts?.Dispose();
            this.playbackCts = null;
            this.previewAudio?.Dispose();
            this.previewAudio = null;
            this.previewCaretVisible = false;
            this.pictureBox_editor.Invalidate();
        }

        private void timer_previewCaret_Tick(object? sender, EventArgs e) => this.pictureBox_editor.Invalidate();

        private async void MidiEditor_FormClosing(object? sender, FormClosingEventArgs e)
        {
            await this.StopPlaybackAsync();
        }

        private async void button_save_Click(object? sender, EventArgs e)
        {
            try
            {
                MidiFileData? source = this.MidiEditSelection.SourceMidiFile;
                MidiFileData editedFile = this.MidiEditSelection.MidiFile.WithPitchFrequency(this.PitchFrequency);
                this.MidiEditSelection.MidiFile = editedFile;
                if (source != null && this.sourceMidiWindow != null)
                {
                    this.sourceMidiWindow.ApplyEdit(source.ReplaceSelection(this.MidiEditSelection, editedFile));
                }
                else if (source == null)
                {
                    MidiWindow midiWindow = new(editedFile.FilePath, editedFile);
                    midiWindow.StartPosition = FormStartPosition.Manual;
                    midiWindow.Location = new Point(this.Location.X + 24, this.Location.Y + 24);
                    midiWindow.Show(this);
                }

                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            catch (Exception ex)
            {
                LogCollection.Log($"MIDI edit save failed: {ex}");
                MessageBox.Show(this, ex.Message, "MIDI edit failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                await Task.CompletedTask;
            }
        }

        private static float NoteToY(int note, int lowest, int count, int height) => (count - 1 - (note - lowest)) / (float) Math.Max(1, count) * height;

        private static Rectangle NormalizeRectangle(Point first, Point second) => Rectangle.FromLTRB(Math.Min(first.X, second.X), Math.Min(first.Y, second.Y), Math.Max(first.X, second.X), Math.Max(first.Y, second.Y));

        private static int ParseMidiNote(string name)
        {
            string[] names = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
            string? matchedName = names
                .OrderByDescending(value => value.Length)
                .FirstOrDefault(value => name.StartsWith(value, StringComparison.OrdinalIgnoreCase));
            if (matchedName == null || !int.TryParse(name[matchedName.Length..], out int octave))
            {
                return 0;
            }

            int noteIndex = Array.IndexOf(names, matchedName);
            return Math.Clamp((octave + 1) * 12 + noteIndex, 0, 127);
        }

        private static string MidiNoteName(int note)
        {
            string[] names = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
            return names[note % 12] + (note / 12 - 1);
        }

        private async void button_import_Click(object sender, EventArgs e)
        {
            // OFD at MyMusic, Filter for MIDI files (*.mid, *.midi), single file selection
            using OpenFileDialog ofd = new()
            {
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                Filter = "MIDI files (*.mid;*.midi)|*.mid;*.midi",
                Multiselect = false,
                RestoreDirectory = true
            };

            if (ofd.ShowDialog(this) == DialogResult.OK)
            {
                try
                {
                    MidiFileData importedFile = MidiFileData.Load(ofd.FileName);
                    this.MidiEditSelection.MidiFile = importedFile;
                    this.editorLengthTicks = Math.Max(this.editorLengthTicks, this.GetRequiredEditorLength());
                    this.SetView(0, Math.Max(this.GridTicks, this.viewEndTick - this.viewStartTick));
                    this.UpdateScrollBar();
                    this.pictureBox_editor.Invalidate();
                }
                catch (Exception ex)
                {
                    LogCollection.Log($"MIDI import failed: {ex}");
                    MessageBox.Show(this, ex.Message, "MIDI import failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void button_generate_Click(object sender, EventArgs e)
        {
            using MidiGeneratorDialog dialog = new(this.MidiEditSelection.MidiFile);
            if (dialog.ShowDialog(this) != DialogResult.OK || dialog.GeneratedMidiFileData == null)
            {
                return;
            }

            this.MidiEditSelection.MidiFile = dialog.GeneratedMidiFileData;
            this.editorLengthTicks = Math.Max(this.editorLengthTicks, this.GetRequiredEditorLength());
            long visibleLength = Math.Max(this.GridTicks, this.viewEndTick - this.viewStartTick);
            this.SetView(this.viewStartTick, this.viewStartTick + visibleLength);
            this.UpdateScrollBar();
            this.pictureBox_editor.Invalidate();
        }

        private async void button_remix_Click(object? sender, EventArgs e)
        {
            using MidiRemixDialog dialog = new(this.MidiEditSelection.MidiFile);
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            try
            {
                MidiFileData remixedFile = await MidiRemixer.RemixAsync(this.MidiEditSelection.MidiFile, dialog.Settings, dialog.TrackIndex);
                MidiEditSelection selection = new(remixedFile);
                MidiEditor editor = new(selection, this.sourceMidiWindow)
                {
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(this.Location.X + 24, this.Location.Y + 24)
                };
                editor.Show(this);
            }
            catch (Exception ex)
            {
                LogCollection.Log($"MIDI remix failed: {ex}");
                MessageBox.Show(this, ex.Message, "MIDI remix failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}