using ModularAudience.Audio;
using ModularAudience.Forms.Helpers;
using ModularAudience.Generators;
using System.Globalization;
using System.Drawing.Drawing2D;

namespace ModularAudience.Forms.Modules.Dialogs
{
    public partial class BreakbeatPatternEditorDialog : Form
    {
        private readonly List<bool[]> pattern;
        private readonly List<AudioObj> samples;
        private readonly List<AudioObj> originalSampleOrder;
        private readonly List<AudioObj> sourceSampleOrder;
        private readonly IDictionary<AudioObj, BreakbeatTrackSettings>? trackSettingsBySample;
        private readonly List<BreakbeatTrackSettings> trackSettings;
        private readonly List<string> rowLabels;
        private int bars;
        private readonly float swing;
        private readonly List<BreakbeatPatternNote> notes;
        private readonly HashSet<int> selectedBars = [];
        private readonly HashSet<BreakbeatPatternNote> selectedNotes = [];
        private readonly List<BreakbeatPatternNote> copiedNotes = [];
        private readonly List<BreakbeatPatternNote> pastePreviewNotes = [];
        private readonly List<(int StartTick, int EndTick)> pendingLivePreviewRegions = [];
        private int patternNotesRevision;
        private CancellationTokenSource? hearCancellationTokenSource;
        private AudioObj? previewAudio;
        private int[] previewBarMap = [];
        private CancellationTokenSource? notePreviewCancellationTokenSource;
        private AudioObj? notePreviewAudio;
        private long notePreviewRequestVersion;
        private bool drawing;
        private int drawingRow;
        private int drawingStartColumn;
        private MouseButtons drawingButton;
        private BreakbeatPatternNote? drawingNote;
        private bool drawingShortTimeStretchNote;
        private bool selectingNotes;
        private Point selectionStartPoint;
        private Point selectionCurrentPoint;
        private Point lastPatternPointer;
        private bool hasPatternPointer;
        private BreakbeatPatternNote? hoverPitchNote;
        private bool hoverPitchTooltipVisible;
        private bool pastePreviewActive;
        private bool pastePreviewValid;
        private BreakbeatPatternNote? pitchGestureNote;
        private bool pitchGestureChanged;
        private BreakbeatPatternNote? volumeGestureNote;
        private bool volumeGestureChanged;
        private bool rowReorderActive;
        private int rowReorderSource = -1;
        private int rowReorderTarget = -1;
        private Point rowReorderPointer;
        private int externalTrackDropIndex = -1;
        private bool externalTrackDragActive;
        private BreakbeatPatternNote? resizingNote;
        private ResizeEdge resizingEdge;
        private int resizeFixedTick;
        private bool resizingWithControl;
        private bool resizeChanged;
        private bool resizeUseVarispeed;
        private int resizeStartX;
        private Point resizeStartPoint;
        private BreakbeatPatternNote? resizeOriginalNote;
        private const double MaximumHorizontalZoom = 512.0;
        private double horizontalZoom = 1.0;
        private int currentResolution = 4;
        private bool initializing = true;
        private bool saveInProgress;

        private enum ResizeEdge
        {
            None,
            Left,
            Right,
            Move
        }

        public IReadOnlyList<bool[]> Pattern => this.BuildPatternMatrix();

        public IReadOnlyList<BreakbeatPatternNote> Notes => this.GetNotesWithoutCoveredRetriggers()
            .Select(note => note with { TrackIndex = this.GetOriginalTrackIndex(note.TrackIndex) })
            .ToArray();

        public decimal Bpm => this.numericUpDown_bpm.Value;

        public int Resolution => this.currentResolution;

        public int Bars => this.bars;

        public event Func<BreakbeatPatternEditorDialog, Task>? SaveRequested;
        internal event Action<AudioObj, int>? TrackAdded;

        public BreakbeatPatternEditorDialog(
            IReadOnlyList<bool[]> pattern,
            IEnumerable<AudioObj> samples,
            IReadOnlyList<string> rowLabels,
            int bars,
            int resolution,
            float swing,
            decimal bpm,
            IReadOnlyList<BreakbeatPatternNote>? existingNotes = null,
            int? initialGridResolution = null,
            IDictionary<AudioObj, BreakbeatTrackSettings>? trackSettingsBySample = null)
        {
            this.InitializeComponent();
            this.KeyPreview = true;
            this.KeyUp += this.BreakbeatPatternEditorDialog_KeyUp;
            this.pattern = pattern.Select(row => row.ToArray()).ToList();
            AudioObj[] sourceSamples = samples.ToArray();
            this.sourceSampleOrder = sourceSamples.ToList();
            this.samples = sourceSamples.Select(sample => sample.Clone()).ToList();
            this.originalSampleOrder = this.samples.ToList();
            this.trackSettingsBySample = trackSettingsBySample;
            this.trackSettings = sourceSamples.Select(sample =>
                trackSettingsBySample is not null && trackSettingsBySample.TryGetValue(sample, out BreakbeatTrackSettings? settings)
                    ? NormalizeTrackSettings(settings)
                    : new BreakbeatTrackSettings()).ToList();
            this.rowLabels = rowLabels.ToList();
            this.bars = Math.Max(1, bars);
            this.swing = swing;
            this.numericUpDown_bpm.Value = Math.Clamp(decimal.Round(bpm, 1), this.numericUpDown_bpm.Minimum, this.numericUpDown_bpm.Maximum);
            if (initialGridResolution is int savedResolution)
            {
                this.numericUpDown_resolution.Value = Math.Clamp(savedResolution, (int)this.numericUpDown_resolution.Minimum, (int)this.numericUpDown_resolution.Maximum);
            }

            this.currentResolution = (int)this.numericUpDown_resolution.Value;
            List<BreakbeatPatternNote> initialNotes = existingNotes?.Select(note => note with
                {
                    OriginalDurationTicks = note.OriginalDurationTicks > 0
                        ? note.OriginalDurationTicks
                        : note.TimeStretch || note.Varispeed
                            ? BreakbeatGenerator_V2.GetMinimumNoteDurationTicks(this.samples, note.TrackIndex, (float)this.Bpm, this.currentResolution)
                            : note.DurationTicks,
                    ManuallyResized = note.ManuallyResized || note.TimeStretch || note.Varispeed
                }).ToList()
                ?? BreakbeatGenerator_V2.CreatePatternNotesFromGrid(this.pattern, this.samples, Math.Max(1, resolution), (float)this.Bpm, this.currentResolution);
            if (existingNotes is null)
            {
                for (int index = 0; index < initialNotes.Count; index++)
                {
                    BreakbeatPatternNote note = initialNotes[index];
                    BreakbeatTrackSettings settings = this.trackSettings[note.TrackIndex];
                    initialNotes[index] = note with
                    {
                        PitchSemitones = settings.DefaultPitchSemitones,
                        VolumePercent = settings.DefaultVolumePercent
                    };
                }
            }

            this.notes = initialNotes;
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
            using Pen selectedNotePen = new(Color.FromArgb(245, 248, 255), 2f);
            using Font pitchFont = new("Segoe UI", 8f, FontStyle.Bold);
            using Font volumeFont = new("Segoe UI", 6f, FontStyle.Bold);
            using StringFormat pitchFormat = new()
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoWrap
            };
            using StringFormat volumeFormat = new()
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoWrap
            };
            using Brush pitchLabelBackground = new SolidBrush(Color.FromArgb(220, 245, 247, 248));
            using Brush pitchLabelBrush = new SolidBrush(Color.FromArgb(15, 20, 24));
            using Brush volumeLabelBackground = new SolidBrush(Color.FromArgb(225, 18, 22, 26));
            using Brush volumeLabelBrush = new SolidBrush(Color.FromArgb(245, 245, 247, 248));
            using Brush settingsSummaryBrush = new SolidBrush(Color.FromArgb(155, 165, 176));
            for (int row = 0; row < rows; row++)
            {
                float y = grid.Top + row * cellHeight;
                RectangleF labelBounds = new(8, y, Math.Max(1, grid.Left - 16), Math.Max(1f, cellHeight));
                string label = row < this.rowLabels.Count ? this.rowLabels[row] : $"Track {row + 1}";
                string settingsSummary = row < this.trackSettings.Count
                    ? FormatTrackSettingsSummary(this.trackSettings[row])
                    : string.Empty;
                float nameHeight = labelFont.GetHeight(e.Graphics);
                float summaryHeight = nameHeight;
                float combinedHeight = nameHeight + (string.IsNullOrEmpty(settingsSummary) ? 0f : summaryHeight + 1f);
                if (string.IsNullOrEmpty(settingsSummary) || combinedHeight > cellHeight - 2f)
                {
                    e.Graphics.DrawString(label, labelFont, labelBrush, labelBounds, labelFormat);
                    continue;
                }

                float textTop = y + (cellHeight - combinedHeight) / 2f;
                RectangleF nameBounds = new(labelBounds.X, textTop, labelBounds.Width, nameHeight);
                RectangleF summaryBounds = new(labelBounds.X, textTop + nameHeight + 1f, labelBounds.Width, summaryHeight);
                e.Graphics.DrawString(label, labelFont, labelBrush, nameBounds, labelFormat);
                e.Graphics.DrawString(settingsSummary, labelFont, settingsSummaryBrush, summaryBounds, labelFormat);
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
                if (this.selectedNotes.Contains(note))
                {
                    e.Graphics.DrawRectangle(selectedNotePen, hit.X, hit.Y, hit.Width, hit.Height);
                }

                DrawNoteIndicators(
                    e.Graphics,
                    hit,
                    note.PitchSemitones,
                    note.VolumePercent,
                    pitchFont,
                    volumeFont,
                    pitchFormat,
                    volumeFormat,
                    pitchLabelBackground,
                    pitchLabelBrush,
                    volumeLabelBackground,
                    volumeLabelBrush);
            }

            if (this.pastePreviewActive)
            {
                Color previewColor = this.pastePreviewValid
                    ? Color.FromArgb(120, 105, 196, 255)
                    : Color.FromArgb(130, 255, 105, 105);
                using Brush previewBrush = new SolidBrush(previewColor);
                using Pen previewPen = new(Color.FromArgb(230, previewColor), 2f) { DashStyle = DashStyle.Dash };
                foreach (BreakbeatPatternNote note in this.pastePreviewNotes)
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
                    RectangleF ghost = new(x + 2f, y + 2f, Math.Max(1f, noteWidth - 4f), Math.Max(1f, cellHeight - 4f));
                    e.Graphics.FillRectangle(previewBrush, ghost);
                    e.Graphics.DrawRectangle(previewPen, ghost.X, ghost.Y, ghost.Width, ghost.Height);
                    DrawNoteIndicators(
                        e.Graphics,
                        ghost,
                        note.PitchSemitones,
                        note.VolumePercent,
                        pitchFont,
                        volumeFont,
                        pitchFormat,
                        volumeFormat,
                        pitchLabelBackground,
                        pitchLabelBrush,
                        volumeLabelBackground,
                        volumeLabelBrush);
                }
            }

            if (this.selectingNotes)
            {
                Rectangle selection = this.GetSelectionBounds();
                using Brush selectionBrush = new SolidBrush(Color.FromArgb(35, 92, 190, 255));
                using Pen selectionPen = new(Color.FromArgb(210, 120, 205, 255)) { DashStyle = DashStyle.Dash };
                e.Graphics.FillRectangle(selectionBrush, selection);
                e.Graphics.DrawRectangle(selectionPen, selection);
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
            this.DrawRowReorderOverlay(e.Graphics, grid, cellHeight);
            this.DrawExternalTrackDropIndicator(e.Graphics, grid, cellHeight);
        }

        private void DrawRowReorderOverlay(Graphics graphics, Rectangle grid, float cellHeight)
        {
            if (!this.rowReorderActive || this.rowReorderSource < 0 || this.rowReorderSource >= this.samples.Count)
            {
                return;
            }

            GraphicsState state = graphics.Save();
            using Brush dimBrush = new SolidBrush(Color.FromArgb(90, 10, 14, 18));
            using Brush ghostBrush = new SolidBrush(Color.FromArgb(150, 100, 160, 205));
            using Brush ghostNoteBrush = new SolidBrush(Color.FromArgb(210, 222, 242, 255));
            using Brush ghostLabelBrush = new SolidBrush(Color.FromArgb(245, 245, 249, 252));
            using Pen ghostBorderPen = new(Color.FromArgb(230, 177, 220, 255), 1.5f);
            using Pen insertionPen = new(Color.FromArgb(245, 255, 206, 86), 3f);

            float sourceTop = grid.Top + this.rowReorderSource * cellHeight;
            graphics.FillRectangle(dimBrush, 0, sourceTop, this.pictureBox_pattern.ClientSize.Width, cellHeight);

            float ghostTop = Math.Clamp(
                this.rowReorderPointer.Y - cellHeight / 2f,
                grid.Top,
                Math.Max(grid.Top, grid.Bottom - cellHeight));
            RectangleF ghostRow = new(2f, ghostTop, this.pictureBox_pattern.ClientSize.Width - 4f, cellHeight);
            graphics.FillRectangle(ghostBrush, ghostRow);
            graphics.DrawRectangle(ghostBorderPen, ghostRow.X, ghostRow.Y, ghostRow.Width, ghostRow.Height);

            RectangleF labelBounds = new(8f, ghostTop, Math.Max(1, grid.Left - 16), cellHeight);
            string label = this.rowReorderSource < this.rowLabels.Count
                ? this.rowLabels[this.rowReorderSource]
                : $"Track {this.rowReorderSource + 1}";
            graphics.DrawString(label, SystemFonts.DefaultFont, ghostLabelBrush, labelBounds);

            double patternTicks = this.bars * (double)BreakbeatGenerator_V2.PatternTicksPerBar;
            double virtualGridWidth = grid.Width * this.horizontalZoom;
            int scrollOffset = this.hScrollBar_pattern.Value;
            foreach (BreakbeatPatternNote note in this.notes.Where(note => note.TrackIndex == this.rowReorderSource))
            {
                float x = grid.Left + (float)(note.StartTick / patternTicks * virtualGridWidth) - scrollOffset;
                float width = (float)(note.DurationTicks / patternTicks * virtualGridWidth);
                RectangleF ghostNote = new(x + 2f, ghostTop + 2f, Math.Max(1f, width - 4f), Math.Max(1f, cellHeight - 4f));
                graphics.FillRectangle(ghostNoteBrush, ghostNote);
            }

            float insertionY = grid.Top + Math.Clamp(this.rowReorderTarget, 0, this.samples.Count) * cellHeight;
            graphics.DrawLine(insertionPen, grid.Left, insertionY, this.pictureBox_pattern.ClientSize.Width - 2, insertionY);
            graphics.Restore(state);
        }

        private void DrawExternalTrackDropIndicator(Graphics graphics, Rectangle grid, float cellHeight)
        {
            if (!this.externalTrackDragActive || this.externalTrackDropIndex < 0)
            {
                return;
            }

            float insertionY = grid.Top + Math.Clamp(this.externalTrackDropIndex, 0, this.samples.Count) * cellHeight;
            using Pen insertionPen = new(Color.FromArgb(245, 255, 206, 86), 3f);
            graphics.DrawLine(insertionPen, 2, insertionY, this.pictureBox_pattern.ClientSize.Width - 2, insertionY);
        }

        private static string FormatPitchSemitones(float semitones)
        {
            int quarterSteps = (int)Math.Round(Math.Abs(semitones) * 4f, MidpointRounding.AwayFromZero);
            if (!float.IsFinite(semitones) || quarterSteps == 0)
            {
                return string.Empty;
            }

            int wholeSemitones = quarterSteps / 4;
            string fraction = (quarterSteps % 4) switch
            {
                1 => "¼",
                2 => "½",
                3 => "¾",
                _ => string.Empty
            };
            string value = wholeSemitones > 0
                ? wholeSemitones.ToString(CultureInfo.InvariantCulture) + fraction
                : fraction;
            return (semitones < 0 ? "-" : "+") + value;
        }

        private static string FormatTrackSettingsSummary(BreakbeatTrackSettings settings)
        {
            List<string> values = [];
            if (Math.Abs(settings.DefaultVolumePercent - 100f) >= 0.5f)
            {
                int volume = (int)Math.Round(settings.DefaultVolumePercent, MidpointRounding.AwayFromZero);
                values.Add(volume.ToString(CultureInfo.InvariantCulture) + "% Vol.");
            }

            if (Math.Abs(settings.DefaultPitchSemitones) >= 0.0001f)
            {
                values.Add(FormatPitchSemitones(settings.DefaultPitchSemitones));
            }

            if (settings.DefaultPlaybackMode == BreakbeatPlaybackMode.Varispeed)
            {
                values.Add("Varispeed");
            }

            return string.Join(" • ", values);
        }

        private static BreakbeatTrackSettings NormalizeTrackSettings(BreakbeatTrackSettings settings)
        {
            float volume = float.IsFinite(settings.DefaultVolumePercent)
                ? Math.Clamp(settings.DefaultVolumePercent, 0f, 250f)
                : 100f;
            float pitch = float.IsFinite(settings.DefaultPitchSemitones)
                ? Math.Clamp(settings.DefaultPitchSemitones, -24f, 24f)
                : 0f;
            BreakbeatPlaybackMode mode = Enum.IsDefined(settings.DefaultPlaybackMode)
                ? settings.DefaultPlaybackMode
                : BreakbeatPlaybackMode.TimeStretch;
            return new BreakbeatTrackSettings(volume, pitch, mode);
        }

        private static void DrawPitchMark(
            Graphics graphics,
            RectangleF noteBounds,
            float semitones,
            Font font,
            StringFormat format,
            Brush labelBackground,
            Brush labelBrush)
        {
            if (Math.Abs(semitones) < 0.0001f)
            {
                return;
            }

            string label = FormatPitchSemitones(semitones);
            Font labelFont = font;
            Font? reducedFont = null;
            SizeF labelSize = graphics.MeasureString(label, labelFont);
            bool fits = noteBounds.Width >= labelSize.Width + 4f && noteBounds.Height >= labelSize.Height + 1f;
            bool hasFraction = label.Contains('¼') || label.Contains('½') || label.Contains('¾');
            if (!fits && hasFraction)
            {
                float widthScale = (noteBounds.Width - 4f) / labelSize.Width;
                float heightScale = (noteBounds.Height - 1f) / labelSize.Height;
                float scale = Math.Min(widthScale, heightScale);
                float reducedSize = font.Size * scale;
                if (scale > 0f && reducedSize >= 4f && reducedSize < font.Size)
                {
                    reducedFont = new Font(font.FontFamily, reducedSize, font.Style, font.Unit);
                    labelFont = reducedFont;
                    labelSize = graphics.MeasureString(label, labelFont);
                    fits = noteBounds.Width >= labelSize.Width + 4f && noteBounds.Height >= labelSize.Height + 1f;
                }
            }

            try
            {
                if (fits)
                {
                    RectangleF labelBounds = new(
                        noteBounds.X + (noteBounds.Width - labelSize.Width - 4f) / 2f,
                        noteBounds.Y + (noteBounds.Height - labelSize.Height) / 2f,
                        labelSize.Width + 4f,
                        labelSize.Height);
                    graphics.FillRectangle(labelBackground, labelBounds);
                    graphics.DrawString(label, labelFont, labelBrush, labelBounds, format);
                    return;
                }
            }
            finally
            {
                reducedFont?.Dispose();
            }

            RectangleF fadeBounds = new(
                noteBounds.X,
                noteBounds.Y,
                noteBounds.Width,
                Math.Max(1f, noteBounds.Height * 0.15f));
            using LinearGradientBrush pitchFade = new(
                fadeBounds,
                Color.Black,
                Color.FromArgb(0, Color.Black),
                LinearGradientMode.Vertical);
            graphics.FillRectangle(pitchFade, fadeBounds);
        }

        private static void DrawNoteIndicators(
            Graphics graphics,
            RectangleF noteBounds,
            float pitchSemitones,
            float volumePercent,
            Font pitchFont,
            Font volumeFont,
            StringFormat pitchFormat,
            StringFormat volumeFormat,
            Brush pitchLabelBackground,
            Brush pitchLabelBrush,
            Brush volumeLabelBackground,
            Brush volumeLabelBrush)
        {
            bool hasPitch = Math.Abs(pitchSemitones) >= 0.0001f;
            bool hasAdjustedVolume = float.IsFinite(volumePercent) && Math.Abs(volumePercent - 100f) >= 0.5f;
            if (!hasAdjustedVolume)
            {
                DrawPitchMark(graphics, noteBounds, pitchSemitones, pitchFont, pitchFormat, pitchLabelBackground, pitchLabelBrush);
                return;
            }

            int roundedVolume = (int)Math.Clamp(Math.Round(volumePercent, MidpointRounding.AwayFromZero), 0, 250);
            string volumeLabel = roundedVolume.ToString(CultureInfo.InvariantCulture) + "%";
            SizeF volumeSize = graphics.MeasureString(volumeLabel, volumeFont);
            if (noteBounds.Width < volumeSize.Width + 4f || noteBounds.Height < volumeSize.Height + 1f)
            {
                DrawPitchMark(graphics, noteBounds, pitchSemitones, pitchFont, pitchFormat, pitchLabelBackground, pitchLabelBrush);
                return;
            }

            float volumeTop;
            if (hasPitch)
            {
                float pitchAreaHeight = noteBounds.Height - volumeSize.Height - 2f;
                if (pitchAreaHeight <= 1f)
                {
                    DrawPitchMark(graphics, noteBounds, pitchSemitones, pitchFont, pitchFormat, pitchLabelBackground, pitchLabelBrush);
                    return;
                }

                RectangleF pitchArea = new(noteBounds.X, noteBounds.Y, noteBounds.Width, pitchAreaHeight);
                DrawPitchMark(graphics, pitchArea, pitchSemitones, pitchFont, pitchFormat, pitchLabelBackground, pitchLabelBrush);
                volumeTop = pitchArea.Bottom + 1f;
            }
            else
            {
                volumeTop = noteBounds.Y + (noteBounds.Height - volumeSize.Height) / 2f;
            }

            RectangleF volumeBounds = new(
                noteBounds.X + (noteBounds.Width - volumeSize.Width - 4f) / 2f,
                volumeTop,
                volumeSize.Width + 4f,
                volumeSize.Height);
            graphics.FillRectangle(volumeLabelBackground, volumeBounds);
            graphics.DrawString(volumeLabel, volumeFont, volumeLabelBrush, volumeBounds, volumeFormat);
        }

        private void pictureBox_pattern_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button is not (MouseButtons.Left or MouseButtons.Right))
            {
                return;
            }

            this.pictureBox_pattern.Focus();
            this.lastPatternPointer = e.Location;
            this.hasPatternPointer = true;

            if (this.pastePreviewActive)
            {
                if (e.Button == MouseButtons.Right)
                {
                    this.CancelPastePreview();
                }
                else if (this.TryGetCell(e.Location, out _, out _))
                {
                    this.UpdatePastePreview(e.Location);
                    if (this.pastePreviewValid)
                    {
                        this.PlacePastePreview();
                    }
                }

                return;
            }

            if (e.Button == MouseButtons.Right && this.TryGetRowReorderSource(e.Location, out int settingsRow))
            {
                this.ShowTrackSettings(settingsRow);
                return;
            }

            if (e.Button == MouseButtons.Left && this.TryGetRowReorderSource(e.Location, out int rowToReorder))
            {
                this.rowReorderActive = true;
                this.rowReorderSource = rowToReorder;
                this.rowReorderTarget = rowToReorder;
                this.rowReorderPointer = e.Location;
                this.pictureBox_pattern.Capture = true;
                this.pictureBox_pattern.Cursor = Cursors.SizeAll;
                this.pictureBox_pattern.Invalidate();
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

            if (e.Button == MouseButtons.Left && (ModifierKeys & Keys.Shift) == Keys.Shift)
            {
                this.selectedNotes.Clear();
                this.selectingNotes = true;
                this.selectionStartPoint = e.Location;
                this.selectionCurrentPoint = e.Location;
                this.pictureBox_pattern.Capture = true;
                this.pictureBox_pattern.Invalidate();
                return;
            }

            this.selectedNotes.Clear();

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
                    this.resizeStartPoint = e.Location;
                    this.resizingWithControl = (ModifierKeys & Keys.Control) == Keys.Control;
                    this.resizeChanged = false;
                    bool currentVarispeed = existingNote.IsManuallyAdjusted
                        ? existingNote.Varispeed
                        : this.trackSettings[row].DefaultPlaybackMode == BreakbeatPlaybackMode.Varispeed;
                    this.resizeUseVarispeed = this.resizingWithControl ? !currentVarispeed : currentVarispeed;
                    this.resizeFixedTick = this.resizingEdge == ResizeEdge.Left
                        ? existingNote.StartTick + existingNote.DurationTicks
                        : existingNote.StartTick;
                    this.drawing = true;
                    this.pictureBox_pattern.Capture = true;
                    this.pictureBox_pattern.Cursor = this.resizingEdge == ResizeEdge.Move ? Cursors.Hand : Cursors.SizeWE;
                    return;
                }

                int stepTicks = this.GetTicksPerStep();
                int startTick = column * stepTicks;
                int minimumDuration = BreakbeatGenerator_V2.GetMinimumNoteDurationTicks(this.samples, row, (float)this.Bpm, this.currentResolution);
                bool controlShortNote = (ModifierKeys & Keys.Control) == Keys.Control;
                int originalDurationTicks = controlShortNote
                    ? Math.Max(minimumDuration, stepTicks * 2)
                    : minimumDuration;
                int initialDurationTicks = controlShortNote ? stepTicks : minimumDuration;
                if (!controlShortNote
                    && !this.TryGetClickNotePlacement(row, startTick, minimumDuration, stepTicks, out startTick, out initialDurationTicks))
                {
                    return;
                }

                this.drawingStartColumn = Math.Max(0, startTick / stepTicks);
                this.drawingShortTimeStretchNote = controlShortNote || initialDurationTicks < minimumDuration;
                BreakbeatTrackSettings trackDefault = this.trackSettings[row];
                bool useVarispeed = trackDefault.DefaultPlaybackMode == BreakbeatPlaybackMode.Varispeed;
                BreakbeatPatternNote newNote = new(
                    row,
                    startTick,
                    initialDurationTicks,
                    TimeStretch: this.drawingShortTimeStretchNote && !useVarispeed,
                    Varispeed: this.drawingShortTimeStretchNote && useVarispeed,
                    ManuallyResized: this.drawingShortTimeStretchNote,
                    OriginalDurationTicks: originalDurationTicks,
                    PitchSemitones: trackDefault.DefaultPitchSemitones,
                    VolumePercent: trackDefault.DefaultVolumePercent);
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

        private bool TryGetClickNotePlacement(
            int row,
            int requestedStartTick,
            int minimumDurationTicks,
            int stepTicks,
            out int startTick,
            out int durationTicks)
        {
            startTick = requestedStartTick;
            durationTicks = minimumDurationTicks;
            int gapStartTick = 0;
            int nextNoteStartTick = int.MaxValue;

            foreach (BreakbeatPatternNote note in this.notes)
            {
                if (note.TrackIndex != row)
                {
                    continue;
                }

                int noteEndTick = note.StartTick + note.DurationTicks;
                if (noteEndTick <= requestedStartTick)
                {
                    gapStartTick = Math.Max(gapStartTick, noteEndTick);
                }
                else if (note.StartTick >= requestedStartTick)
                {
                    nextNoteStartTick = Math.Min(nextNoteStartTick, note.StartTick);
                }
                else
                {
                    return false;
                }
            }

            if (nextNoteStartTick == int.MaxValue || requestedStartTick + minimumDurationTicks <= nextNoteStartTick)
            {
                return true;
            }

            int availableGapTicks = nextNoteStartTick - gapStartTick;
            if (availableGapTicks >= minimumDurationTicks)
            {
                startTick = nextNoteStartTick - minimumDurationTicks;
                return true;
            }

            int shortenedDurationTicks = availableGapTicks / stepTicks * stepTicks;
            if (shortenedDurationTicks < stepTicks)
            {
                return false;
            }

            startTick = gapStartTick;
            durationTicks = shortenedDurationTicks;
            return true;
        }

        private void pictureBox_pattern_MouseMove(object? sender, MouseEventArgs e)
        {
            this.lastPatternPointer = e.Location;
            this.hasPatternPointer = true;
            this.UpdatePitchTooltipHover(e.Location);

            if (this.rowReorderActive)
            {
                this.rowReorderPointer = e.Location;
                this.rowReorderTarget = this.GetRowInsertionIndex(e.Location.Y);
                this.pictureBox_pattern.Invalidate();
                return;
            }

            if (this.pastePreviewActive && !this.drawing)
            {
                this.UpdatePastePreview(e.Location);
                this.pictureBox_pattern.Invalidate();
                return;
            }

            if (this.selectingNotes)
            {
                this.selectionCurrentPoint = this.ClampToGrid(e.Location);
                this.pictureBox_pattern.Invalidate();
                return;
            }

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
            if (this.rowReorderActive && e.Button == MouseButtons.Left)
            {
                this.rowReorderPointer = e.Location;
                this.rowReorderTarget = this.GetRowInsertionIndex(e.Location.Y);
                this.CompleteRowReorder();
                this.rowReorderActive = false;
                this.rowReorderSource = -1;
                this.rowReorderTarget = -1;
                this.pictureBox_pattern.Capture = false;
                this.UpdatePatternCursor(e.Location);
                this.pictureBox_pattern.Invalidate();
                return;
            }

            if (this.selectingNotes && e.Button == MouseButtons.Left)
            {
                this.selectionCurrentPoint = this.ClampToGrid(e.Location);
                this.selectingNotes = false;
                this.pictureBox_pattern.Capture = false;
                this.SelectNotesInRectangle();
                this.pictureBox_pattern.Invalidate();
                return;
            }

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
                this.RegisterPatternNotesChanged(clickedNote, placedNote);
            }

            this.pictureBox_pattern.Invalidate();

            if (shouldPrehear && placedNote is not null && this.hearCancellationTokenSource is null)
            {
                _ = this.PrehearNoteAsync(placedNote);
            }
        }

        private bool TryGetRowReorderSource(Point point, out int row)
        {
            row = -1;
            Rectangle grid = this.GetGridBounds();
            if (point.X < 0
                || point.X >= grid.Left
                || point.Y < grid.Top
                || point.Y >= grid.Bottom
                || this.samples.Count == 0)
            {
                return false;
            }

            float cellHeight = grid.Height / (float)this.samples.Count;
            row = Math.Clamp((int)((point.Y - grid.Top) / cellHeight), 0, this.samples.Count - 1);
            return true;
        }

        private void ShowTrackSettings(int row)
        {
            if (row < 0 || row >= this.samples.Count)
            {
                return;
            }

            string sampleName = row < this.rowLabels.Count ? this.rowLabels[row] : this.samples[row].Name;
            using BreakbeatTrackSettingsDialog dialog = new(sampleName, this.trackSettings[row]);
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            BreakbeatTrackSettings settings = NormalizeTrackSettings(dialog.Settings);
            this.trackSettings[row] = settings;
            int originalTrackIndex = this.GetOriginalTrackIndex(row);
            if (this.trackSettingsBySample is not null
                && originalTrackIndex >= 0
                && originalTrackIndex < this.sourceSampleOrder.Count)
            {
                this.trackSettingsBySample[this.sourceSampleOrder[originalTrackIndex]] = settings;
            }

            this.pictureBox_pattern.Invalidate();
        }

        private void pictureBox_pattern_DragEnter(object? sender, DragEventArgs e)
        {
            this.UpdateExternalTrackDrop(e);
        }

        private void pictureBox_pattern_DragOver(object? sender, DragEventArgs e)
        {
            this.UpdateExternalTrackDrop(e);
        }

        private void pictureBox_pattern_DragLeave(object? sender, EventArgs e)
        {
            this.ClearExternalTrackDrop();
        }

        private void pictureBox_pattern_DragDrop(object? sender, DragEventArgs e)
        {
            try
            {
                if (!TryGetDraggedSamples(e.Data, out AudioObj[] draggedSamples) || this.TrackAdded is null)
                {
                    return;
                }

                Point point = this.pictureBox_pattern.PointToClient(new Point(e.X, e.Y));
                int displayIndex = this.externalTrackDropIndex >= 0
                    ? this.externalTrackDropIndex
                    : this.GetTrackDropInsertionIndex(point.Y);
                displayIndex = Math.Clamp(displayIndex, 0, this.samples.Count);
                int sourceIndex = displayIndex < this.samples.Count
                    ? this.GetOriginalTrackIndex(displayIndex)
                    : this.sourceSampleOrder.Count;
                if (sourceIndex < 0)
                {
                    sourceIndex = this.sourceSampleOrder.Count;
                }

                foreach (AudioObj draggedSample in draggedSamples)
                {
                    this.InsertSampleTrack(draggedSample, displayIndex++, sourceIndex++);
                }
            }
            finally
            {
                this.ClearExternalTrackDrop();
            }
        }

        private void UpdateExternalTrackDrop(DragEventArgs e)
        {
            if (!TryGetDraggedSamples(e.Data, out _) || this.TrackAdded is null)
            {
                e.Effect = DragDropEffects.None;
                this.ClearExternalTrackDrop();
                return;
            }

            e.Effect = DragDropEffects.Copy;
            Point point = this.pictureBox_pattern.PointToClient(new Point(e.X, e.Y));
            int insertionIndex = this.GetTrackDropInsertionIndex(point.Y);
            if (!this.externalTrackDragActive || insertionIndex != this.externalTrackDropIndex)
            {
                this.externalTrackDragActive = true;
                this.externalTrackDropIndex = insertionIndex;
                this.pictureBox_pattern.Invalidate();
            }
        }

        private void ClearExternalTrackDrop()
        {
            if (!this.externalTrackDragActive && this.externalTrackDropIndex < 0)
            {
                return;
            }

            this.externalTrackDragActive = false;
            this.externalTrackDropIndex = -1;
            this.pictureBox_pattern.Invalidate();
        }

        private static bool TryGetDraggedSamples(IDataObject? data, out AudioObj[] samples)
        {
            if (data is not null
                && data.GetDataPresent(typeof(AudioObj[]))
                && data.GetData(typeof(AudioObj[])) is AudioObj[] audioArray)
            {
                samples = audioArray.Where(sample => sample is not null).ToArray();
                return samples.Length > 0;
            }

            if (data is not null
                && data.GetDataPresent(typeof(IEnumerable<AudioObj>))
                && data.GetData(typeof(IEnumerable<AudioObj>)) is IEnumerable<AudioObj> audioEnumerable)
            {
                samples = audioEnumerable.Where(sample => sample is not null).ToArray();
                return samples.Length > 0;
            }

            samples = [];
            return false;
        }

        private int GetTrackDropInsertionIndex(int pointerY)
        {
            if (this.samples.Count == 0)
            {
                return 0;
            }

            Rectangle grid = this.GetGridBounds();
            if (pointerY <= grid.Top)
            {
                return 0;
            }

            if (pointerY >= grid.Bottom)
            {
                return this.samples.Count;
            }

            float cellHeight = grid.Height / (float)this.samples.Count;
            float rowPosition = (pointerY - grid.Top) / cellHeight;
            int row = Math.Clamp((int)rowPosition, 0, this.samples.Count - 1);
            return Math.Clamp(row + (rowPosition - row >= 0.5f ? 1 : 0), 0, this.samples.Count);
        }

        private void InsertSampleTrack(AudioObj draggedSample, int displayIndex, int sourceIndex)
        {
            AudioObj sourceSample = CloneSampleWithMetadata(draggedSample);
            AudioObj editorSample = CloneSampleWithMetadata(sourceSample);
            BreakbeatTrackSettings settings = new();
            int patternColumns = this.pattern.Count > 0
                ? this.pattern[0].Length
                : Math.Max(1, this.bars * this.currentResolution);

            this.samples.Insert(displayIndex, editorSample);
            this.originalSampleOrder.Insert(sourceIndex, editorSample);
            this.sourceSampleOrder.Insert(sourceIndex, sourceSample);
            this.pattern.Insert(displayIndex, new bool[patternColumns]);
            this.trackSettings.Insert(displayIndex, settings);
            this.rowLabels.Insert(displayIndex, GetTrackLabel(sourceSample));
            if (this.trackSettingsBySample is not null)
            {
                this.trackSettingsBySample[sourceSample] = settings;
            }

            BreakbeatPatternNote[] originalNotes = this.notes.ToArray();
            int MapTrackIndex(int trackIndex) => trackIndex >= displayIndex ? trackIndex + 1 : trackIndex;
            for (int index = 0; index < this.notes.Count; index++)
            {
                this.notes[index] = this.notes[index] with { TrackIndex = MapTrackIndex(this.notes[index].TrackIndex) };
            }

            BreakbeatPatternNote[] selectedNotes = this.selectedNotes.ToArray();
            this.selectedNotes.Clear();
            foreach (BreakbeatPatternNote note in selectedNotes)
            {
                this.selectedNotes.Add(note with { TrackIndex = MapTrackIndex(note.TrackIndex) });
            }

            for (int index = 0; index < this.copiedNotes.Count; index++)
            {
                this.copiedNotes[index] = this.copiedNotes[index] with { TrackIndex = MapTrackIndex(this.copiedNotes[index].TrackIndex) };
            }

            for (int index = 0; index < this.pastePreviewNotes.Count; index++)
            {
                this.pastePreviewNotes[index] = this.pastePreviewNotes[index] with { TrackIndex = MapTrackIndex(this.pastePreviewNotes[index].TrackIndex) };
            }

            if (this.hoverPitchNote is not null)
            {
                this.hoverPitchNote = this.hoverPitchNote with { TrackIndex = MapTrackIndex(this.hoverPitchNote.TrackIndex) };
            }

            this.TrackAdded?.Invoke(sourceSample, sourceIndex);
            this.RegisterPatternNotesChanged(originalNotes
                .Concat(this.notes)
                .Select(note => (BreakbeatPatternNote?)note)
                .ToArray());
            this.pictureBox_pattern.Invalidate();
        }

        private static AudioObj CloneSampleWithMetadata(AudioObj sample)
        {
            AudioObj clone = sample.Clone();
            clone.SampleTag = sample.SampleTag;
            clone.Tag = sample.Tag;
            foreach ((string key, string value) in sample.CustomTags.Values)
            {
                clone.CustomTags[key] = value;
            }

            return clone;
        }

        private static string GetTrackLabel(AudioObj sample)
        {
            string name = string.IsNullOrWhiteSpace(sample.Name) ? "Sample" : sample.Name.Trim();
            return sample.Tag is DrumsetElement element ? $"{element}: {name}" : name;
        }

        private int GetRowInsertionIndex(int pointerY)
        {
            Rectangle grid = this.GetGridBounds();
            float cellHeight = grid.Height / (float)Math.Max(1, this.samples.Count);
            float rowPosition = Math.Clamp((pointerY - grid.Top) / cellHeight, 0f, this.samples.Count);
            int row = Math.Min(this.samples.Count - 1, (int)rowPosition);
            int insertion = rowPosition - row >= 0.5f ? row + 1 : row;
            return Math.Clamp(insertion, 0, this.samples.Count);
        }

        private void CompleteRowReorder()
        {
            int source = this.rowReorderSource;
            int destination = this.rowReorderTarget;
            if (source < 0 || source >= this.samples.Count)
            {
                return;
            }

            if (destination > source)
            {
                destination--;
            }

            destination = Math.Clamp(destination, 0, this.samples.Count - 1);
            if (destination == source)
            {
                return;
            }

            BreakbeatPatternNote[] originalNotes = this.notes.ToArray();
            AudioObj sample = this.samples[source];
            this.samples.RemoveAt(source);
            this.samples.Insert(destination, sample);
            MoveRow(this.pattern, source, destination);
            MoveRow(this.trackSettings, source, destination);
            MoveRow(this.rowLabels, source, destination);

            int MapTrackIndex(int trackIndex)
            {
                if (trackIndex == source)
                {
                    return destination;
                }

                if (source < destination && trackIndex > source && trackIndex <= destination)
                {
                    return trackIndex - 1;
                }

                if (destination < source && trackIndex >= destination && trackIndex < source)
                {
                    return trackIndex + 1;
                }

                return trackIndex;
            }

            for (int index = 0; index < this.notes.Count; index++)
            {
                BreakbeatPatternNote note = this.notes[index];
                this.notes[index] = note with { TrackIndex = MapTrackIndex(note.TrackIndex) };
            }

            BreakbeatPatternNote[] selected = this.selectedNotes.ToArray();
            this.selectedNotes.Clear();
            foreach (BreakbeatPatternNote note in selected)
            {
                this.selectedNotes.Add(note with { TrackIndex = MapTrackIndex(note.TrackIndex) });
            }

            for (int index = 0; index < this.copiedNotes.Count; index++)
            {
                BreakbeatPatternNote note = this.copiedNotes[index];
                this.copiedNotes[index] = note with { TrackIndex = MapTrackIndex(note.TrackIndex) };
            }

            this.RegisterPatternNotesChanged(originalNotes
                .Concat(this.notes)
                .Select(note => (BreakbeatPatternNote?)note)
                .ToArray());
        }

        private static void MoveRow<T>(List<T> rows, int source, int destination)
        {
            if (source >= rows.Count || destination >= rows.Count || source == destination)
            {
                return;
            }

            T row = rows[source];
            rows.RemoveAt(source);
            rows.Insert(destination, row);
        }

        private static void MoveRow(string[] rows, int source, int destination)
        {
            if (source >= rows.Length || destination >= rows.Length || source == destination)
            {
                return;
            }

            string row = rows[source];
            if (source < destination)
            {
                Array.Copy(rows, source + 1, rows, source, destination - source);
            }
            else
            {
                Array.Copy(rows, destination, rows, destination + 1, source - destination);
            }

            rows[destination] = row;
        }

        private Rectangle GetSelectionBounds()
        {
            int left = Math.Min(this.selectionStartPoint.X, this.selectionCurrentPoint.X);
            int top = Math.Min(this.selectionStartPoint.Y, this.selectionCurrentPoint.Y);
            int right = Math.Max(this.selectionStartPoint.X, this.selectionCurrentPoint.X);
            int bottom = Math.Max(this.selectionStartPoint.Y, this.selectionCurrentPoint.Y);
            return Rectangle.FromLTRB(left, top, Math.Max(left + 1, right), Math.Max(top + 1, bottom));
        }

        private Point ClampToGrid(Point point)
        {
            Rectangle grid = this.GetGridBounds();
            return new Point(
                Math.Clamp(point.X, grid.Left, grid.Right - 1),
                Math.Clamp(point.Y, grid.Top, grid.Bottom - 1));
        }

        private RectangleF GetNoteBounds(BreakbeatPatternNote note)
        {
            Rectangle grid = this.GetGridBounds();
            double patternTicks = this.bars * (double)BreakbeatGenerator_V2.PatternTicksPerBar;
            double virtualGridWidth = grid.Width * this.horizontalZoom;
            float cellHeight = grid.Height / (float)Math.Max(1, this.samples.Count);
            float left = grid.Left + (float)(note.StartTick / patternTicks * virtualGridWidth) - this.hScrollBar_pattern.Value;
            float width = (float)(note.DurationTicks / patternTicks * virtualGridWidth);
            float top = grid.Top + note.TrackIndex * cellHeight;
            return new RectangleF(left, top, Math.Max(1f, width), Math.Max(1f, cellHeight));
        }

        private void SelectNotesInRectangle()
        {
            RectangleF selection = this.GetSelectionBounds();
            foreach (BreakbeatPatternNote note in this.notes)
            {
                if (note.TrackIndex >= 0 && note.TrackIndex < this.samples.Count
                    && this.GetNoteBounds(note).IntersectsWith(selection))
                {
                    this.selectedNotes.Add(note);
                }
            }
        }

        private void CopySelectedNotes()
        {
            if (this.selectedNotes.Count == 0)
            {
                return;
            }

            this.copiedNotes.Clear();
            this.copiedNotes.AddRange(this.notes.Where(this.selectedNotes.Contains));
        }

        private void StartPastePreview()
        {
            if (this.copiedNotes.Count == 0)
            {
                return;
            }

            this.pastePreviewActive = true;
            Point pointer = this.hasPatternPointer
                ? this.lastPatternPointer
                : new Point(this.GetGridBounds().Left, this.GetGridBounds().Top);
            this.UpdatePastePreview(pointer);
            this.pictureBox_pattern.Invalidate();
        }

        private void UpdatePastePreview(Point point)
        {
            if (this.copiedNotes.Count == 0)
            {
                this.pastePreviewValid = false;
                return;
            }

            Point gridPoint = this.ClampToGrid(point);
            _ = this.TryGetCell(gridPoint, out int anchorRow, out int anchorColumn);
            int minimumRow = this.copiedNotes.Min(note => note.TrackIndex);
            int minimumTick = this.copiedNotes.Min(note => note.StartTick);
            int anchorTick = anchorColumn * this.GetTicksPerStep();
            this.pastePreviewNotes.Clear();
            foreach (BreakbeatPatternNote note in this.copiedNotes)
            {
                this.pastePreviewNotes.Add(note with
                {
                    TrackIndex = anchorRow + note.TrackIndex - minimumRow,
                    StartTick = anchorTick + note.StartTick - minimumTick
                });
            }

            int patternEndTick = this.bars * BreakbeatGenerator_V2.PatternTicksPerBar;
            this.pastePreviewValid = this.pastePreviewNotes.All(note =>
                note.TrackIndex >= 0
                && note.TrackIndex < this.samples.Count
                && note.StartTick >= 0
                && (long)note.StartTick + note.DurationTicks <= patternEndTick
                && !this.HasOverlappingNote(note));
            for (int first = 0; this.pastePreviewValid && first < this.pastePreviewNotes.Count; first++)
            {
                for (int second = first + 1; second < this.pastePreviewNotes.Count; second++)
                {
                    if (this.NotesOverlap(this.pastePreviewNotes[first], this.pastePreviewNotes[second]))
                    {
                        this.pastePreviewValid = false;
                        break;
                    }
                }
            }
        }

        private bool NotesOverlap(BreakbeatPatternNote first, BreakbeatPatternNote second)
        {
            return first.TrackIndex == second.TrackIndex
                && first.StartTick < (long)second.StartTick + second.DurationTicks
                && second.StartTick < (long)first.StartTick + first.DurationTicks;
        }

        private void PlacePastePreview()
        {
            if (!this.pastePreviewActive || !this.pastePreviewValid)
            {
                return;
            }

            List<BreakbeatPatternNote> placedNotes = this.pastePreviewNotes.ToList();
            this.notes.AddRange(placedNotes);
            this.RegisterPatternNotesChanged(placedNotes.Select(note => (BreakbeatPatternNote?)note).ToArray());
            this.selectedNotes.Clear();
            foreach (BreakbeatPatternNote note in placedNotes)
            {
                this.selectedNotes.Add(note);
                this.RemoveCoveredRetriggersIfNeeded(note);
            }

            this.CancelPastePreview();
            if (this.checkBox_preHear.Checked && placedNotes.Count > 0 && this.hearCancellationTokenSource is null)
            {
                _ = this.PrehearNoteAsync(placedNotes[0]);
            }
        }

        private void CancelPastePreview()
        {
            this.pastePreviewActive = false;
            this.pastePreviewValid = false;
            this.pastePreviewNotes.Clear();
            this.pictureBox_pattern.Invalidate();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (this.pictureBox_pattern.ContainsFocus)
            {
                Keys key = keyData & Keys.KeyCode;
                Keys modifiers = keyData & Keys.Modifiers;
                if (key == Keys.Delete && modifiers == Keys.None && this.selectedNotes.Count > 0)
                {
                    BreakbeatPatternNote[] removedNotes = this.selectedNotes.ToArray();
                    this.notes.RemoveAll(this.selectedNotes.Contains);
                    this.selectedNotes.Clear();
                    this.RegisterPatternNotesChanged(removedNotes.Select(note => (BreakbeatPatternNote?)note).ToArray());
                    this.pictureBox_pattern.Invalidate();
                    return true;
                }

                if (modifiers == Keys.Control && key == Keys.C && this.selectedNotes.Count > 0)
                {
                    this.CopySelectedNotes();
                    return true;
                }

                if (modifiers == Keys.Control && key == Keys.V && this.copiedNotes.Count > 0)
                {
                    this.StartPastePreview();
                    return true;
                }

                if (key == Keys.Escape && modifiers == Keys.None && this.pastePreviewActive)
                {
                    this.CancelPastePreview();
                    return true;
                }
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void DeleteNotesAt(int row, int tick)
        {
            BreakbeatPatternNote[] removedNotes = this.notes
                .Where(note => note.TrackIndex == row && tick >= note.StartTick && tick < (long)note.StartTick + note.DurationTicks)
                .ToArray();
            if (removedNotes.Length == 0)
            {
                return;
            }

            this.notes.RemoveAll(note => removedNotes.Contains(note));
            this.RegisterPatternNotesChanged(removedNotes.Select(note => (BreakbeatPatternNote?)note).ToArray());
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

            float noteWidth = right - left;
            if (point.X < left + noteWidth / 3f)
            {
                return ResizeEdge.Left;
            }

            return point.X >= right - noteWidth / 3f ? ResizeEdge.Right : ResizeEdge.Move;
        }

        private void UpdatePatternCursor(Point point)
        {
            if (this.TryGetHeaderBar(point, out _))
            {
                this.pictureBox_pattern.Cursor = Cursors.Hand;
                return;
            }

            if (this.TryGetRowReorderSource(point, out _))
            {
                this.pictureBox_pattern.Cursor = Cursors.SizeAll;
                return;
            }

            if (!this.TryGetCell(point, out int row, out _))
            {
                this.pictureBox_pattern.Cursor = Cursors.Cross;
                return;
            }

            int noteIndex = this.FindNoteIndexAt(row, this.PointToTick(point));
            this.pictureBox_pattern.Cursor = noteIndex < 0
                ? Cursors.Cross
                : this.GetResizeEdge(this.notes[noteIndex], point) == ResizeEdge.Move
                    ? Cursors.Hand
                    : Cursors.SizeWE;
        }

        private bool HasResizeDistance(Point point)
        {
            Rectangle grid = this.GetGridBounds();
            float cellWidth = (float)(grid.Width * this.horizontalZoom / Math.Max(1, this.bars * this.currentResolution));
            int minimumDistance = Math.Max(1, (int)Math.Ceiling(cellWidth / 2f));
            return Math.Abs(point.X - this.resizeStartX) >= minimumDistance;
        }

        private bool HasMoveDistance(Point point)
        {
            Rectangle grid = this.GetGridBounds();
            float cellWidth = (float)(grid.Width * this.horizontalZoom / Math.Max(1, this.bars * this.currentResolution));
            float rowHeight = grid.Height / (float)Math.Max(1, this.samples.Count);
            int minimumHorizontalDistance = Math.Max(1, (int)Math.Ceiling(cellWidth / 2f));
            int minimumVerticalDistance = Math.Max(1, (int)Math.Ceiling(rowHeight / 2f));
            return Math.Abs(point.X - this.resizeStartPoint.X) >= minimumHorizontalDistance
                || Math.Abs(point.Y - this.resizeStartPoint.Y) >= minimumVerticalDistance;
        }

        private void pictureBox_pattern_MouseWheel(object? sender, MouseEventArgs e)
        {
            Point pointer = e.Location;
            if (sender is Control source)
            {
                pointer = this.pictureBox_pattern.PointToClient(source.PointToScreen(e.Location));
            }

            bool handled = false;
            if ((ModifierKeys & Keys.Alt) == Keys.Alt && e.Delta != 0)
            {
                if (this.TryGetCell(pointer, out int row, out _))
                {
                    int noteIndex = this.FindNoteIndexAt(row, this.PointToTick(pointer));
                    if (noteIndex >= 0)
                    {
                        BreakbeatPatternNote originalNote = this.notes[noteIndex];
                        float currentVolume = float.IsFinite(originalNote.VolumePercent)
                            ? Math.Clamp(originalNote.VolumePercent, 0f, 250f)
                            : 100f;
                        int wheelSteps = Math.Max(1, Math.Abs(e.Delta) / 120);
                        float volumePercent = Math.Clamp(
                            currentVolume + Math.Sign(e.Delta) * wheelSteps * 5f,
                            0f,
                            250f);
                        if (volumePercent != originalNote.VolumePercent)
                        {
                            BreakbeatPatternNote updatedNote = originalNote with { VolumePercent = volumePercent };
                            this.notes[noteIndex] = updatedNote;
                            this.RegisterPatternNotesChanged(originalNote, updatedNote);
                            this.volumeGestureNote = updatedNote;
                            this.volumeGestureChanged = true;
                            this.timer_pitchGestureRelease.Stop();
                            this.timer_pitchGestureRelease.Start();
                            if (this.selectedNotes.Remove(originalNote))
                            {
                                this.selectedNotes.Add(updatedNote);
                            }

                            this.notePreviewCancellationTokenSource?.Cancel();
                            this.pictureBox_pattern.Invalidate();
                        }
                    }
                }

                handled = true;
            }
            else if ((ModifierKeys & Keys.Shift) == Keys.Shift && e.Delta != 0)
            {
                if (this.TryGetCell(pointer, out int row, out _))
                {
                    int noteIndex = this.FindNoteIndexAt(row, this.PointToTick(pointer));
                    if (noteIndex >= 0)
                    {
                        BreakbeatPatternNote originalNote = this.notes[noteIndex];
                        int wheelSteps = Math.Max(1, Math.Abs(e.Delta) / 120);
                        int quarterSteps = (int)Math.Round(originalNote.PitchSemitones * 4f)
                            + Math.Sign(e.Delta) * wheelSteps;
                        BreakbeatPatternNote updatedNote = originalNote with
                        {
                            PitchSemitones = Math.Clamp(quarterSteps, -96, 96) / 4f
                        };
                        if (updatedNote.PitchSemitones != originalNote.PitchSemitones)
                        {
                            this.notes[noteIndex] = updatedNote;
                            this.RegisterPatternNotesChanged(originalNote, updatedNote);
                            this.pitchGestureNote = updatedNote;
                            this.pitchGestureChanged = true;
                            this.timer_pitchGestureRelease.Stop();
                            this.timer_pitchGestureRelease.Start();
                            if (this.selectedNotes.Remove(originalNote))
                            {
                                this.selectedNotes.Add(updatedNote);
                            }

                            this.notePreviewCancellationTokenSource?.Cancel();
                            this.pictureBox_pattern.Invalidate();
                        }
                    }
                }

                handled = true;
            }
            else if ((ModifierKeys & Keys.Control) == Keys.Control && e.Delta != 0)
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

        private void BreakbeatPatternEditorDialog_KeyUp(object? sender, KeyEventArgs e)
        {
            if (IsShiftKey(e.KeyCode) && (ModifierKeys & Keys.Shift) != Keys.Shift)
            {
                this.CompletePitchGesture();
            }

            if ((e.KeyCode is Keys.Menu or Keys.LMenu or Keys.RMenu) && (ModifierKeys & Keys.Alt) != Keys.Alt)
            {
                this.CompleteVolumeGesture();
            }
        }

        private void timer_pitchGestureRelease_Tick(object? sender, EventArgs e)
        {
            if ((ModifierKeys & Keys.Shift) != Keys.Shift)
            {
                this.CompletePitchGesture();
            }

            if ((ModifierKeys & Keys.Alt) != Keys.Alt)
            {
                this.CompleteVolumeGesture();
            }
        }

        private void CompletePitchGesture()
        {
            this.timer_pitchGestureRelease.Stop();
            BreakbeatPatternNote? changedNote = this.pitchGestureChanged ? this.pitchGestureNote : null;
            this.pitchGestureNote = null;
            this.pitchGestureChanged = false;
            if (changedNote is not null && this.checkBox_preHear.Checked && this.hearCancellationTokenSource is null)
            {
                _ = this.PrehearNoteAsync(changedNote);
            }
        }

        private void CompleteVolumeGesture()
        {
            BreakbeatPatternNote? changedNote = this.volumeGestureChanged ? this.volumeGestureNote : null;
            this.volumeGestureNote = null;
            this.volumeGestureChanged = false;
            if (changedNote is not null
                && this.checkBox_preHear.Checked
                && this.hearCancellationTokenSource is null
                )
            {
                _ = this.PrehearNoteAsync(changedNote);
            }
        }

        private static bool IsShiftKey(Keys keyCode)
        {
            return keyCode is Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey;
        }

        private void pictureBox_pattern_MouseEnter(object? sender, EventArgs e)
        {
            this.pictureBox_pattern.Focus();
            Point pointer = this.pictureBox_pattern.PointToClient(Cursor.Position);
            this.lastPatternPointer = pointer;
            this.hasPatternPointer = true;
            this.UpdatePitchTooltipHover(pointer);
        }

        private void pictureBox_pattern_MouseLeave(object? sender, EventArgs e)
        {
            this.timer_pitchTooltip.Stop();
            this.hoverPitchNote = null;
            this.hoverPitchTooltipVisible = false;
            this.toolTip_pattern.Hide(this.pictureBox_pattern);
        }

        private void UpdatePitchTooltipHover(Point pointer)
        {
            BreakbeatPatternNote? note = null;
            if (!this.drawing && !this.rowReorderActive && !this.selectingNotes && !this.pastePreviewActive)
            {
                for (int index = this.notes.Count - 1; index >= 0; index--)
                {
                    BreakbeatPatternNote candidate = this.notes[index];
                    if (Math.Abs(candidate.PitchSemitones) >= 0.0001f
                        && this.GetNoteBounds(candidate).Contains(pointer))
                    {
                        note = candidate;
                        break;
                    }
                }
            }

            if (Equals(note, this.hoverPitchNote))
            {
                if (note is not null && this.hoverPitchTooltipVisible)
                {
                    this.ShowPitchTooltip(note, pointer);
                }

                return;
            }

            this.timer_pitchTooltip.Stop();
            this.toolTip_pattern.Hide(this.pictureBox_pattern);
            this.hoverPitchTooltipVisible = false;
            this.hoverPitchNote = note;
            if (note is not null)
            {
                this.timer_pitchTooltip.Start();
            }
        }

        private void timer_pitchTooltip_Tick(object? sender, EventArgs e)
        {
            this.timer_pitchTooltip.Stop();
            if (this.hoverPitchNote is not BreakbeatPatternNote note
                || !Equals(this.GetPitchNoteAt(this.lastPatternPointer), note))
            {
                this.UpdatePitchTooltipHover(this.lastPatternPointer);
                return;
            }

            this.ShowPitchTooltip(note, this.lastPatternPointer);
            this.hoverPitchTooltipVisible = true;
        }

        private BreakbeatPatternNote? GetPitchNoteAt(Point pointer)
        {
            for (int index = this.notes.Count - 1; index >= 0; index--)
            {
                BreakbeatPatternNote candidate = this.notes[index];
                if (Math.Abs(candidate.PitchSemitones) >= 0.0001f
                    && this.GetNoteBounds(candidate).Contains(pointer))
                {
                    return candidate;
                }
            }

            return null;
        }

        private void ShowPitchTooltip(BreakbeatPatternNote note, Point pointer)
        {
            string pitch = FormatPitchSemitones(note.PitchSemitones);
            this.toolTip_pattern.Show(
                pitch,
                this.pictureBox_pattern,
                new Point(pointer.X + 12, pointer.Y + 16),
                2500);
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
            if (this.resizingNote is null || this.resizeOriginalNote is null)
            {
                return;
            }

            bool hasGestureDistance = this.resizingEdge == ResizeEdge.Move
                ? this.HasMoveDistance(point)
                : this.HasResizeDistance(point);
            if (!hasGestureDistance)
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

            BreakbeatPatternNote updated = this.resizingEdge == ResizeEdge.Move
                ? this.MoveNoteToPoint(this.resizeOriginalNote, point)
                : this.ResizeNoteToPoint(this.resizingNote, point);
            int index = this.notes.FindIndex(note => ReferenceEquals(note, this.resizingNote));
            if (index >= 0 && !this.HasOverlappingNote(updated, this.resizingNote))
            {
                this.notes[index] = updated;
                this.resizingNote = updated;
                this.resizeChanged = updated != this.resizeOriginalNote;
            }
        }

        private BreakbeatPatternNote MoveNoteToPoint(BreakbeatPatternNote note, Point point)
        {
            int stepTicks = this.GetTicksPerStep();
            int columnDelta = this.PointToColumn(point) - this.PointToColumn(this.resizeStartPoint);
            Point gridPoint = this.ClampToGrid(point);
            Point startGridPoint = this.ClampToGrid(this.resizeStartPoint);
            _ = this.TryGetCell(gridPoint, out int targetRow, out _);
            _ = this.TryGetCell(startGridPoint, out int startRow, out _);
            int patternEndTick = this.bars * BreakbeatGenerator_V2.PatternTicksPerBar;
            int maximumStartTick = Math.Max(0, patternEndTick - note.DurationTicks);

            return note with
            {
                TrackIndex = Math.Clamp(note.TrackIndex + targetRow - startRow, 0, this.samples.Count - 1),
                StartTick = Math.Clamp(note.StartTick + columnDelta * stepTicks, 0, maximumStartTick)
            };
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
                int trackIndex = this.GetOriginalTrackIndex(note.TrackIndex);
                if (trackIndex < 0 || trackIndex >= result.Count || note.DurationTicks <= 0)
                {
                    continue;
                }

                int startColumn = Math.Clamp((int)Math.Round(note.StartTick / (double)stepTicks), 0, columns - 1);
                result[trackIndex][startColumn] = true;
            }

            return result;
        }

        private int GetOriginalTrackIndex(int currentTrackIndex)
        {
            if (currentTrackIndex < 0 || currentTrackIndex >= this.samples.Count)
            {
                return -1;
            }

            return this.originalSampleOrder.IndexOf(this.samples[currentTrackIndex]);
        }

        private Rectangle GetGridBounds()
        {
            int width = Math.Max(1, this.pictureBox_pattern.ClientSize.Width);
            int height = Math.Max(1, this.pictureBox_pattern.ClientSize.Height);
            int labelWidth = Math.Clamp(width / 4, 130, 260);
            int left = Math.Min(labelWidth, Math.Max(1, width - 16));
            int top = Math.Min(26, Math.Max(1, height - 1));
            return new Rectangle(left, top, Math.Max(1, width - left - 24), Math.Max(1, height - top - 6));
        }

        private int PointToTick(Point point)
        {
            Rectangle grid = this.GetGridBounds();
            double virtualGridWidth = grid.Width * this.horizontalZoom;
            double virtualX = point.X - grid.Left + this.hScrollBar_pattern.Value;
            double fraction = Math.Clamp(virtualX / virtualGridWidth, 0, 1);
            return (int)Math.Round(fraction * this.bars * BreakbeatGenerator_V2.PatternTicksPerBar);
        }

        private void RegisterPatternNotesChanged(params BreakbeatPatternNote?[] changedNotes)
        {
            this.patternNotesRevision++;
            if (this.hearCancellationTokenSource is null)
            {
                this.pendingLivePreviewRegions.Clear();
                return;
            }

            foreach (BreakbeatPatternNote? note in changedNotes)
            {
                if (note is null || note.DurationTicks <= 0)
                {
                    continue;
                }

                int startTick = Math.Max(0, note.StartTick);
                int endTick = (int)Math.Min(int.MaxValue, (long)startTick + note.DurationTicks);
                this.pendingLivePreviewRegions.Add((startTick, Math.Max(startTick + 1, endTick)));
            }
        }

        private async Task<AudioObj> RenderHearBufferAsync(
            IReadOnlyList<BreakbeatPatternNote> patternNotes,
            IReadOnlyList<int> selectedBars,
            CancellationToken cancellationToken)
        {
            float bpm = (float)this.numericUpDown_bpm.Value;
            AudioObj? renderedPattern = await BreakbeatGenerator_V2.RenderPatternNotesAsync(
                patternNotes,
                this.samples,
                this.bars,
                bpm,
                this.currentResolution,
                this.swing,
                "BreakbeatPreview",
                cancellationToken);

            if (renderedPattern is null)
            {
                int silentBars = selectedBars.Count > 0 ? selectedBars.Count : this.bars;
                const int sampleRate = 44100;
                const int channels = 2;
                int frames = Math.Max(1, (int)Math.Ceiling(silentBars * 240.0 / Math.Max(1.0, bpm) * sampleRate));
                float[] silence = new float[checked(frames * channels)];
                return new AudioObj
                {
                    Name = "BreakbeatPreview_Silence",
                    Data = silence,
                    SampleRate = sampleRate,
                    Channels = channels,
                    Length = silence.Length,
                    Duration = TimeSpan.FromSeconds(frames / (double)sampleRate),
                    BitDepth = 32,
                    Bpm = bpm
                };
            }

            if (selectedBars.Count == 0)
            {
                return renderedPattern;
            }

            try
            {
                return BreakbeatGenerator_V2.ExtractPatternBars(renderedPattern, selectedBars, bpm);
            }
            finally
            {
                renderedPattern.Dispose();
            }
        }

        private bool IsCaretOverChangedNote()
        {
            if (this.previewAudio is null || this.pendingLivePreviewRegions.Count == 0)
            {
                return false;
            }

            double secondsPerBar = 240.0 / Math.Max(1.0, (double)this.Bpm);
            double timelineSeconds = this.previewAudio.CurrentTime.TotalSeconds;
            if (this.previewBarMap.Length > 0)
            {
                double selectedDuration = this.previewBarMap.Length * secondsPerBar;
                double sequenceTime = timelineSeconds % selectedDuration;
                int selectedIndex = Math.Min((int)(sequenceTime / secondsPerBar), this.previewBarMap.Length - 1);
                timelineSeconds = this.previewBarMap[selectedIndex] * secondsPerBar
                    + sequenceTime - selectedIndex * secondsPerBar;
            }

            int caretTick = (int)Math.Floor(Math.Max(0, timelineSeconds) / secondsPerBar * BreakbeatGenerator_V2.PatternTicksPerBar);
            return this.pendingLivePreviewRegions.Any(region => caretTick >= region.StartTick && caretTick < region.EndTick);
        }

        private void SwapLivePreviewBuffer(AudioObj replacement)
        {
            if (this.previewAudio is null)
            {
                return;
            }

            AudioObj current = this.previewAudio;
            long samplePosition = current.Position * (long)Math.Max(1, current.Channels);
            current.SwapPlaybackData(replacement.Data, replacement.SampleRate, replacement.Channels, samplePosition);
            current.Data = replacement.Data;
            current.SampleRate = replacement.SampleRate;
            current.Channels = replacement.Channels;
            current.Length = replacement.Length;
            current.Duration = replacement.Duration;
        }

        private async void button_hear_Click(object? sender, EventArgs e)
        {
            Interlocked.Increment(ref this.notePreviewRequestVersion);
            if (this.hearCancellationTokenSource is not null)
            {
                this.notePreviewCancellationTokenSource?.Cancel();
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
            this.pendingLivePreviewRegions.Clear();
            this.button_hear.Text = "Stop";
            AudioObj? pendingLivePreview = null;
            int pendingLiveRevision = -1;
            try
            {
                int[] selectedBarsForPreview = this.selectedBars.OrderBy(bar => bar).ToArray();
                int appliedNotesRevision = this.patternNotesRevision;
                BreakbeatPatternNote[] initialNotes = this.GetNotesWithoutCoveredRetriggers().ToArray();
                this.previewAudio = await this.RenderHearBufferAsync(initialNotes, selectedBarsForPreview, cancellationTokenSource.Token);

                if (selectedBarsForPreview.Length > 0)
                {
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
                    this.previewBarMap = [];
                }

                this.previewAudio.SetPosition(0);

                this.timer_previewCaret.Start();
                await this.previewAudio.PlayAsync(cancellationTokenSource.Token, initialVolume: 1f);
                while (this.previewAudio.Playing && !cancellationTokenSource.IsCancellationRequested)
                {
                    if (pendingLivePreview is not null && pendingLiveRevision != this.patternNotesRevision)
                    {
                        pendingLivePreview.Dispose();
                        pendingLivePreview = null;
                        pendingLiveRevision = -1;
                    }

                    if (pendingLivePreview is null && this.patternNotesRevision != appliedNotesRevision)
                    {
                        await Task.Delay(100, cancellationTokenSource.Token);
                        int requestedRevision = this.patternNotesRevision;
                        if (requestedRevision != appliedNotesRevision)
                        {
                            BreakbeatPatternNote[] currentNotes = this.GetNotesWithoutCoveredRetriggers().ToArray();
                            try
                            {
                                AudioObj replacement = await this.RenderHearBufferAsync(
                                    currentNotes,
                                    selectedBarsForPreview,
                                    cancellationTokenSource.Token);
                                if (requestedRevision == this.patternNotesRevision)
                                {
                                    pendingLivePreview = replacement;
                                    pendingLiveRevision = requestedRevision;
                                }
                                else
                                {
                                    replacement.Dispose();
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                throw;
                            }
                            catch (Exception ex)
                            {
                                LogCollection.Log("Live breakbeat preview update failed.");
                                LogCollection.Log(ex);
                                await Task.Delay(250, cancellationTokenSource.Token);
                            }
                        }
                    }

                    if (pendingLivePreview is not null
                        && pendingLiveRevision == this.patternNotesRevision
                        && !this.IsCaretOverChangedNote())
                    {
                        this.SwapLivePreviewBuffer(pendingLivePreview);
                        pendingLivePreview.Dispose();
                        pendingLivePreview = null;
                        appliedNotesRevision = pendingLiveRevision;
                        pendingLiveRevision = -1;
                        this.pendingLivePreviewRegions.Clear();
                    }

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
                pendingLivePreview?.Dispose();
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
            long requestVersion = Interlocked.Increment(ref this.notePreviewRequestVersion);
            await this.StopWholePatternPreviewAsync();
            await this.StopNotePreviewAsync();
            if (requestVersion != Volatile.Read(ref this.notePreviewRequestVersion) || this.IsDisposed)
            {
                return;
            }

            CancellationTokenSource cancellationTokenSource = new();
            this.notePreviewCancellationTokenSource = cancellationTokenSource;
            AudioObj? notePreviewAudio = null;
            try
            {
                int previewBars = Math.Max(1, (int)Math.Ceiling(note.DurationTicks / (double)BreakbeatGenerator_V2.PatternTicksPerBar));
                BreakbeatPatternNote previewNote = note with { StartTick = 0 };
                notePreviewAudio = await BreakbeatGenerator_V2.RenderPatternNotesAsync(
                    [previewNote],
                    this.samples,
                    previewBars,
                    (float)this.numericUpDown_bpm.Value,
                    this.currentResolution,
                    0f,
                    "BreakbeatNotePreview",
                    cancellationTokenSource.Token);

                if (notePreviewAudio is null
                    || cancellationTokenSource.IsCancellationRequested
                    || requestVersion != Volatile.Read(ref this.notePreviewRequestVersion))
                {
                    return;
                }

                this.notePreviewAudio = notePreviewAudio;
                await notePreviewAudio.PlayAsync(cancellationTokenSource.Token, initialVolume: 1f);
                while (notePreviewAudio.Playing && !cancellationTokenSource.IsCancellationRequested)
                {
                    await Task.Delay(20, cancellationTokenSource.Token);
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
                try
                {
                    if (notePreviewAudio?.Playing == true)
                    {
                        await notePreviewAudio.StopAsync();
                    }
                }
                finally
                {
                    notePreviewAudio?.Dispose();
                    if (ReferenceEquals(this.notePreviewAudio, notePreviewAudio))
                    {
                        this.notePreviewAudio = null;
                    }

                    if (ReferenceEquals(this.notePreviewCancellationTokenSource, cancellationTokenSource))
                    {
                        this.notePreviewCancellationTokenSource = null;
                    }

                    cancellationTokenSource.Dispose();
                }
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
            AudioObj? notePreviewAudio = this.notePreviewAudio;
            if (notePreviewAudio?.Playing == true)
            {
                await notePreviewAudio.StopAsync();
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

        private async void button_save_Click(object? sender, EventArgs e)
        {
            if (this.saveInProgress)
            {
                return;
            }

            bool closeAfterSave = (ModifierKeys & Keys.Control) == Keys.Control;
            this.saveInProgress = true;
            this.button_save.Enabled = false;
            string originalText = this.button_save.Text;
            this.button_save.Text = "Rendering...";
            try
            {
                if (this.SaveRequested is Func<BreakbeatPatternEditorDialog, Task> saveRequested)
                {
                    await saveRequested(this);
                }

                if (closeAfterSave && !this.IsDisposed && !this.Disposing)
                {
                    this.DialogResult = DialogResult.OK;
                    this.Close();
                }
            }
            catch (Exception ex)
            {
                LogCollection.Log("Breakbeat pattern save failed.");
                LogCollection.Log(ex);
                if (!this.IsDisposed && !this.Disposing)
                {
                    WindowMainStaticHelpers.ShowErrorWithCopyButton(this, "Breakbeat Pattern Save", ex);
                }
            }
            finally
            {
                if (!this.IsDisposed && !this.Disposing)
                {
                    this.button_save.Enabled = true;
                    this.button_save.Text = originalText;
                    this.saveInProgress = false;
                }
            }
        }

        private void button_help_Click(object? sender, EventArgs e)
        {
            string helpText = string.Join(Environment.NewLine,
            [
                "EDIT",
                "Click empty / drag: add hit / longer note. Click note: play. Right-click / drag: erase.",
                "Shift-drag: select notes. Del: erase selection. Ctrl+C: copy. Ctrl+V: preview; click to place, Esc/right-click to cancel.",
                "Shift+wheel on a note: pitch +/-0.25 semitones (up to +/-24).",
                "Alt+wheel on a note: volume +/-5% (0-250%); 100% is default.",
                "Right-click a sample name: configure persistent defaults for new notes on that track.",
                "Drag a sample name left of the grid to reorder sample tracks.",
                "Pre-hear toggles auto-preview; clicking a note always plays it.",
                "",
                "RESIZE / MODE",
                "Drag left / right thirds to resize. Drag the center (hand cursor) to move the note. Ctrl-drag or Ctrl-click a resized note: switch TimeStretch / Varispeed.",
                "Ctrl-click empty: add a short note; repeat to switch mode. TimeStretch keeps pitch; Varispeed changes it.",
                "",
                "COLORS / VIEW",
                "Green: normal. Blue: TimeStretch. Yellow: Varispeed; light shades mean shorter notes.",
                "Ctrl+wheel: zoom at pointer. Wheel / bottom bar: horizontal scroll. Steps per bar: powers of 2, up to 256.",
                "",
                "BARS / PLAYBACK",
                "Click bar numbers to toggle selection. Hear plays all once, or selected bars in order on a loop.",
                "+: add bar. -: remove last bar. Save renders and keeps the editor open; Ctrl-click Save renders and closes. Cancel discards unsaved edits."
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