using ModularAudience.Audio;
using ModularAudience.Forms;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ModularAudience.Forms.Modules.Dialogs
{
    public partial class PianoRollEditor : Form
    {
        private sealed class PianoTrackState
        {
            public required AudioObj Audio { get; init; }
            public required string Name { get; init; }
            public required bool[] Steps { get; init; }
        }

        private readonly record struct RollLayoutInfo(int ContentWidth, int ContentHeight, int RowHeight, int NameWidth, int StepWidth, bool ShowStepNumbers);

        private const int RollStepCount = 32;
        private const int RollPadding = 5;
        private const int RollRowSpacing = 2;
        private const int RollRowMinHeight = 24;
        private const int RollRowMaxHeight = 72;
        private const int RollStepSpacing = 3;
        private const int RollNameMinWidth = 96;
        private const int RollNameMaxWidth = 240;
        private const int RollStepMinWidth = 6;
        private static readonly Color StepDefaultBack = SystemColors.Control;

        private readonly AudioCollection AudioC = new();
        private readonly List<PianoTrackState> trackRows = [];
        private readonly List<AudioObj> pendingInitialSamples = [];
        private readonly SemaphoreSlim rebuildSemaphore = new(1, 1);
        private readonly Random random = new();
        private readonly Lock playbackLock = new();
        private int contextMenuRowIndex = -1;

        private CancellationTokenSource? playbackCts;
        private Task? playbackTask;
        private bool initialPatternLoadCompleted;
        private bool isPlaying;
        private volatile int currentStep;

        public float Bpm => (float) this.numericUpDown_bpm.Value;
        public float Volume => 1.0f;

        public PianoRollEditor()
        {
            this.InitializeComponent();
            this.KeyPreview = true;
            this.panel_roll.Visible = false;

            this.pendingInitialSamples.AddRange(Array.Empty<AudioObj>());
            this.StartPosition = FormStartPosition.Manual;
            this.Location = WindowsScreenHelper.GetCenterStartingPoint(this);

            this.AllowDrop = true;
            this.DragEnter += this.PianoRollEditor_DragEnter;
            this.DragDrop += this.PianoRollEditor_DragDrop;
            this.FormClosing += this.PianoRollEditor_FormClosing;
            this.Shown += this.PianoRollEditor_Shown;

            this.AudioC.Audios.ListChanged += this.AudioC_Audios_ListChanged;
            this.InitializeContextMenu();

            this.MinimumSize = new Size(860, this.MinimumSize.Height);
            int maxHeight = Math.Max(this.MinimumSize.Height, Screen.FromControl(this).WorkingArea.Height - 24);
            this.MaximumSize = new Size(1400, maxHeight);
            this.Resize += this.PianoRollEditor_Resize;
        }

        public PianoRollEditor(IEnumerable<AudioObj>? samples) : this()
        {
            if (samples != null)
            {
                this.pendingInitialSamples.AddRange(samples.Where(sample => sample != null));
            }
        }

        private async void PianoRollEditor_Shown(object? sender, EventArgs e)
        {
            try
            {
                if (this.initialPatternLoadCompleted)
                {
                    return;
                }

                this.initialPatternLoadCompleted = true;
                await this.RebuildTrackPanelsAsync();

                if (this.pendingInitialSamples.Count > 0)
                {
                    await this.AddTracksAsync(this.pendingInitialSamples);
                    this.pendingInitialSamples.Clear();
                }

                await this.ResizePanelsAndButtonsAsync();
            }
            catch (Exception ex)
            {
                try { LogCollection.Log($"PianoRollEditor Shown error: {ex.Message}"); } catch { }
                try { this.Close(); } catch { }
            }
        }

        private void PianoRollEditor_FormClosing(object? sender, FormClosingEventArgs e)
        {
            this.StopPlayback();
            this.AudioC.Audios.ListChanged -= this.AudioC_Audios_ListChanged;
            this.DragEnter -= this.PianoRollEditor_DragEnter;
            this.DragDrop -= this.PianoRollEditor_DragDrop;
            this.Shown -= this.PianoRollEditor_Shown;
            try { this.rebuildSemaphore.Dispose(); } catch { }
            try { this.contextMenuStrip_rows?.Dispose(); } catch { }
            this.AudioC.Dispose();
        }

        private async void AudioC_Audios_ListChanged(object? sender, ListChangedEventArgs e)
        {
            try
            {
                if (!this.initialPatternLoadCompleted)
                {
                    return;
                }

                await this.RebuildTrackPanelsAsync(this.CaptureTrackStates());
            }
            catch (Exception ex)
            {
                try { LogCollection.Log($"PianoRollEditor ListChanged error: {ex.Message}"); } catch { }
            }
        }

        private async Task AddTracksAsync(IEnumerable<AudioObj> samples)
        {
            List<AudioObj> tracks = samples.Where(sample => sample != null).Select(CreateEditorAudio).ToList();
            if (tracks.Count == 0)
            {
                return;
            }

            List<List<bool>>? restoreStates = this.initialPatternLoadCompleted ? this.CaptureTrackStates() : null;

            this.AudioC.Audios.ListChanged -= this.AudioC_Audios_ListChanged;
            try
            {
                foreach (AudioObj track in tracks)
                {
                    this.AudioC.Audios.Add(track);
                }
            }
            finally
            {
                this.AudioC.Audios.ListChanged += this.AudioC_Audios_ListChanged;
            }

            if (!this.initialPatternLoadCompleted)
            {
                return;
            }

            await this.RebuildTrackPanelsAsync(restoreStates);
            await this.ResizePanelsAndButtonsAsync();
        }

        private static AudioObj CreateEditorAudio(AudioObj source)
        {
            AudioObj editorAudio = new()
            {
                Id = source.Id,
                FilePath = source.FilePath,
                Data = source.Data,
                SampleRate = source.SampleRate,
                SampleRateFactor = source.SampleRateFactor,
                Channels = source.Channels,
                BitDepth = source.BitDepth,
                Length = source.Length,
                Duration = source.Duration,
                Tag = source.Tag,
                Bpm = source.Bpm,
                ScannedBpm = source.ScannedBpm,
                Timing = source.Timing,
                ScannedTiming = source.ScannedTiming,
                Key = source.Key,
                ScannedKey = source.ScannedKey,
                Volume = source.Volume,
                ChunkSize = source.ChunkSize,
                OverlapSize = source.OverlapSize,
                StretchFactor = source.StretchFactor,
                ScrollOffset = source.ScrollOffset,
                StartingOffset = source.StartingOffset,
                SampleTag = source.SampleTag,
                DrawBeatGrid = source.DrawBeatGrid,
                BeatGrid = source.BeatGrid
            };

            editorAudio.Rename(GetPreferredAudioName(source));
            editorAudio.SelectionStart = source.SelectionStart;
            editorAudio.SelectionEnd = source.SelectionEnd;
            editorAudio.LoopEnabled = source.LoopEnabled;
            return editorAudio;
        }

        private static string GetPreferredAudioName(AudioObj audio, int index = -1)
        {
            string?[] candidates =
            [
                audio.Name,
                audio.OriginalName,
                string.IsNullOrWhiteSpace(audio.FilePath) ? null : Path.GetFileNameWithoutExtension(audio.FilePath)
            ];

            foreach (string? candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate) && !string.Equals(candidate, "untitled", StringComparison.OrdinalIgnoreCase))
                {
                    return candidate.Trim();
                }
            }

            return index >= 0 ? $"Track {index + 1}" : "Track";
        }

        private void PianoRollEditor_DragEnter(object? sender, DragEventArgs e)
        {
            if (e.Data != null && (e.Data.GetDataPresent(typeof(AudioObj)) ||
                                   e.Data.GetDataPresent(typeof(AudioObj[])) ||
                                   e.Data.GetDataPresent(typeof(List<AudioObj>)) ||
                                   e.Data.GetDataPresent(typeof(IEnumerable<AudioObj>)) ||
                                   e.Data.GetDataPresent(typeof(ListBox.SelectedObjectCollection))))
            {
                e.Effect = DragDropEffects.Copy;
                return;
            }

            e.Effect = DragDropEffects.None;
        }

        private async void PianoRollEditor_DragDrop(object? sender, DragEventArgs e)
        {
            if (e.Data == null)
            {
                return;
            }

            List<AudioObj> dropped = ExtractDroppedSamples(e.Data);
            if (dropped.Count == 0)
            {
                return;
            }

            await this.AddTracksAsync(dropped);
        }

        private static List<AudioObj> ExtractDroppedSamples(IDataObject data)
        {
            if (data.GetDataPresent(typeof(AudioObj[])) && data.GetData(typeof(AudioObj[])) is AudioObj[] audioArray)
            {
                return audioArray.Where(audio => audio != null).ToList();
            }

            if (data.GetDataPresent(typeof(List<AudioObj>)) && data.GetData(typeof(List<AudioObj>)) is List<AudioObj> audioList)
            {
                return audioList.Where(audio => audio != null).ToList();
            }

            if (data.GetDataPresent(typeof(IEnumerable<AudioObj>)) && data.GetData(typeof(IEnumerable<AudioObj>)) is IEnumerable<AudioObj> enumerable)
            {
                return enumerable.Where(audio => audio != null).ToList();
            }

            if (data.GetDataPresent(typeof(ListBox.SelectedObjectCollection)) && data.GetData(typeof(ListBox.SelectedObjectCollection)) is ListBox.SelectedObjectCollection selected)
            {
                return selected.Cast<object>().OfType<AudioObj>().ToList();
            }

            if (data.GetDataPresent(typeof(AudioObj)) && data.GetData(typeof(AudioObj)) is AudioObj audio)
            {
                return [audio];
            }

            if (data.GetDataPresent(DataFormats.Serializable))
            {
                object? serializable = data.GetData(DataFormats.Serializable);
                if (serializable is AudioObj serializableAudio)
                {
                    return [serializableAudio];
                }

                if (serializable is IEnumerable<AudioObj> serializableList)
                {
                    return serializableList.Where(audioItem => audioItem != null).ToList();
                }
            }

            return [];
        }

        private async Task RebuildTrackPanelsAsync(List<List<bool>>? restoreStates = null)
        {
            await this.rebuildSemaphore.WaitAsync().ConfigureAwait(false);

            try
            {
                var audioSnapshot = this.AudioC.Audios.ToList();
                var rows = await Task.Run(() =>
                {
                    var rebuilt = new List<PianoTrackState>(audioSnapshot.Count);
                    for (int i = 0; i < audioSnapshot.Count; i++)
                    {
                        bool[] steps = new bool[RollStepCount];
                        if (restoreStates != null && i < restoreStates.Count)
                        {
                            List<bool> source = restoreStates[i];
                            int copyLength = Math.Min(RollStepCount, source.Count);
                            for (int step = 0; step < copyLength; step++)
                            {
                                steps[step] = source[step];
                            }
                        }

                        AudioObj audio = audioSnapshot[i];
                        rebuilt.Add(new PianoTrackState
                        {
                            Audio = audio,
                            Name = GetPreferredAudioName(audio, i),
                            Steps = steps
                        });
                    }

                    return rebuilt;
                }).ConfigureAwait(false);

                void ApplyRows()
                {
                    this.trackRows.Clear();
                    this.trackRows.AddRange(rows);
                    this.FitWindowHeightToTracks();
                    this.UpdateRollViewport();
                    this.panel_roll.Visible = this.trackRows.Count > 0;
                    this.panel_roll.Invalidate();
                }

                if (this.IsDisposed)
                {
                    return;
                }

                if (this.InvokeRequired)
                {
                    this.Invoke(ApplyRows);
                }
                else
                {
                    ApplyRows();
                }
            }
            finally
            {
                this.rebuildSemaphore.Release();
            }
        }

        private Task ResizePanelsAndButtonsAsync()
        {
            this.UpdateRollViewport();
            this.panel_roll.Invalidate();
            return Task.CompletedTask;
        }

        private void FitWindowHeightToTracks()
        {
            if (this.IsDisposed || !this.IsHandleCreated)
            {
                return;
            }

            try
            {
                int rowCount = this.trackRows.Count;
                if (rowCount <= 0)
                {
                    return;
                }

                int headerBottom = Math.Max(this.label_info_dragndrop.Bottom, Math.Max(this.button_playback.Bottom, this.label_info_bpm.Bottom));
                int top = Math.Max(0, headerBottom + 6);
                int bottomMargin = 20;
                int nonClientHeight = this.Height - this.ClientSize.Height;
                int desiredRowHeight = Math.Max(RollRowMinHeight, 24);
                int desiredPanelHeight = (RollPadding * 2) + (rowCount * desiredRowHeight) + Math.Max(0, (rowCount - 1) * RollRowSpacing);
                int desiredHeight = top + desiredPanelHeight + bottomMargin + nonClientHeight;

                Rectangle workArea = Screen.FromControl(this).WorkingArea;
                int maxHeight = Math.Max(this.MinimumSize.Height, workArea.Height - 24);
                this.MaximumSize = new Size(this.MaximumSize.Width, maxHeight);

                if (this.Height != Math.Clamp(desiredHeight, this.MinimumSize.Height, maxHeight))
                {
                    this.Height = Math.Clamp(desiredHeight, this.MinimumSize.Height, maxHeight);
                }
            }
            catch (Exception ex)
            {
                try { LogCollection.Log($"PianoRollEditor layout error: {ex.Message}"); } catch { }
            }
        }

        private void UpdateRollViewport()
        {
            int headerBottom = Math.Max(this.label_info_dragndrop.Bottom, Math.Max(this.button_playback.Bottom, this.label_info_bpm.Bottom));
            int top = Math.Max(0, headerBottom + 6);
            int left = Math.Max(0, this.panel_roll.Left);
            int rightMargin = 12;
            int bottomMargin = 20;
            int width = Math.Max(64, this.ClientSize.Width - left - rightMargin);
            int height = Math.Max(80, this.ClientSize.Height - top - bottomMargin);

            this.panel_roll.Dock = DockStyle.None;
            this.panel_roll.Location = new Point(left, top);
            this.panel_roll.Size = new Size(width, height);
            this.panel_roll.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            this.panel_roll.AutoScroll = true;
            this.panel_roll.Padding = Padding.Empty;
            this.panel_roll.Margin = Padding.Empty;

            RollLayoutInfo layout = this.GetRollLayout();
            bool needsScroll = layout.ContentHeight > this.panel_roll.ClientSize.Height;
            this.panel_roll.AutoScroll = needsScroll;
            this.panel_roll.AutoScrollMinSize = needsScroll ? new Size(0, Math.Max(0, layout.ContentHeight)) : Size.Empty;

            if (!needsScroll)
            {
                this.panel_roll.AutoScrollPosition = Point.Empty;
            }
        }

        private RollLayoutInfo GetRollLayout()
        {
            int rowCount = this.trackRows.Count;
            int viewportWidth = Math.Max(64, this.panel_roll.ClientSize.Width);
            int viewportHeight = Math.Max(80, this.panel_roll.ClientSize.Height);
            int usableHeight = Math.Max(RollRowMinHeight, viewportHeight - (RollPadding * 2));
            int totalSpacing = Math.Max(0, (rowCount - 1) * RollRowSpacing);
            int rowHeight = rowCount == 0
                ? RollRowMinHeight
                : Math.Max(RollRowMinHeight, Math.Min(RollRowMaxHeight, (usableHeight - totalSpacing) / Math.Max(1, rowCount)));

            int contentHeight = rowCount == 0 ? 0 : (RollPadding * 2) + (rowCount * rowHeight) + totalSpacing;
            int availableWidth = Math.Max(64, viewportWidth - (RollPadding * 2));
            if (contentHeight > viewportHeight)
            {
                availableWidth = Math.Max(64, availableWidth - SystemInformation.VerticalScrollBarWidth);
            }

            int spacingWidth = Math.Max(0, (RollStepCount - 1) * RollStepSpacing);
            int nameWidth = Math.Min(RollNameMaxWidth, Math.Max(RollNameMinWidth, availableWidth / 3));
            int stepWidth = Math.Max(RollStepMinWidth, (availableWidth - nameWidth - RollPadding - spacingWidth) / Math.Max(1, RollStepCount));

            if (stepWidth == RollStepMinWidth)
            {
                int targetNameWidth = Math.Max(96, availableWidth - (RollStepCount * stepWidth) - spacingWidth - RollPadding);
                nameWidth = Math.Min(nameWidth, targetNameWidth);
                stepWidth = Math.Max(RollStepMinWidth, (availableWidth - nameWidth - RollPadding - spacingWidth) / Math.Max(1, RollStepCount));
            }

            int contentWidth = nameWidth + RollPadding + (RollStepCount * stepWidth) + spacingWidth;
            bool showStepNumbers = stepWidth >= 22 && rowHeight >= 24;
            return new RollLayoutInfo(contentWidth, contentHeight, rowHeight, nameWidth, stepWidth, showStepNumbers);
        }

        private Rectangle GetRowBounds(int rowIndex, RollLayoutInfo layout)
        {
            int y = RollPadding + (rowIndex * (layout.RowHeight + RollRowSpacing));
            return new Rectangle(RollPadding, y, layout.ContentWidth, layout.RowHeight);
        }

        private Rectangle GetNameBounds(int rowIndex, RollLayoutInfo layout)
        {
            Rectangle rowRect = this.GetRowBounds(rowIndex, layout);
            return new Rectangle(rowRect.Left + 4, rowRect.Top, layout.NameWidth - 4, rowRect.Height);
        }

        private Rectangle GetStepBounds(int rowIndex, int stepIndex, RollLayoutInfo layout)
        {
            Rectangle rowRect = this.GetRowBounds(rowIndex, layout);
            int x = rowRect.Left + layout.NameWidth + RollPadding + (stepIndex * (layout.StepWidth + RollStepSpacing));
            return new Rectangle(x, rowRect.Top + 4, layout.StepWidth, Math.Max(14, rowRect.Height - 8));
        }

        private void panel_roll_Paint(object sender, PaintEventArgs e)
        {
            try
            {
                e.Graphics.Clear(this.panel_roll.BackColor);

                if (this.trackRows.Count == 0)
                {
                    TextRenderer.DrawText(
                        e.Graphics,
                        "Drop Samples here to add tracks",
                        this.Font,
                        this.panel_roll.ClientRectangle,
                        SystemColors.GrayText,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                    return;
                }

                RollLayoutInfo layout = this.GetRollLayout();
                Point scrollOffset = this.panel_roll.AutoScrollPosition;
                Rectangle clipRect = new(
                    e.ClipRectangle.X - scrollOffset.X,
                    e.ClipRectangle.Y - scrollOffset.Y,
                    e.ClipRectangle.Width,
                    e.ClipRectangle.Height);

                e.Graphics.TranslateTransform(scrollOffset.X, scrollOffset.Y);

                using SolidBrush rowBrushEven = new(Color.FromArgb(245, 245, 245));
                using SolidBrush rowBrushOdd = new(Color.FromArgb(230, 230, 230));
                using SolidBrush activeBrush = new(Color.FromArgb(58, 122, 255));
                using SolidBrush inactiveBrush = new(StepDefaultBack);
                using SolidBrush currentActiveBrush = new(Color.FromArgb(232, 86, 86));
                using SolidBrush currentInactiveBrush = new(Color.Orange);
                using Pen borderPen = new(Color.DimGray);

                int slotHeight = layout.RowHeight + RollRowSpacing;
                int firstVisibleRow = Math.Max(0, (clipRect.Top - RollPadding) / Math.Max(1, slotHeight));
                int lastVisibleRow = Math.Min(this.trackRows.Count - 1, Math.Max(firstVisibleRow, (clipRect.Bottom - RollPadding) / Math.Max(1, slotHeight)));

                for (int rowIndex = firstVisibleRow; rowIndex <= lastVisibleRow; rowIndex++)
                {
                    PianoTrackState row = this.trackRows[rowIndex];
                    Rectangle rowRect = this.GetRowBounds(rowIndex, layout);
                    if (!rowRect.IntersectsWith(clipRect))
                    {
                        continue;
                    }

                    e.Graphics.FillRectangle(rowIndex % 2 == 0 ? rowBrushEven : rowBrushOdd, rowRect);

                    Rectangle nameRect = this.GetNameBounds(rowIndex, layout);
                    if (nameRect.IntersectsWith(clipRect) && nameRect.Width > 32)
                    {
                        TextRenderer.DrawText(
                            e.Graphics,
                            row.Name,
                            this.Font,
                            nameRect,
                            Color.Black,
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                    }

                    for (int step = 0; step < RollStepCount; step++)
                    {
                        Rectangle stepRect = this.GetStepBounds(rowIndex, step, layout);
                        if (!stepRect.IntersectsWith(clipRect))
                        {
                            continue;
                        }

                        bool active = row.Steps[step];
                        bool isCurrent = this.isPlaying && step == this.currentStep;
                        Brush brush = isCurrent
                            ? (active ? currentActiveBrush : currentInactiveBrush)
                            : (active ? activeBrush : inactiveBrush);

                        e.Graphics.FillRectangle(brush, stepRect);
                        e.Graphics.DrawRectangle(borderPen, stepRect);

                        if (layout.ShowStepNumbers)
                        {
                            TextRenderer.DrawText(
                                e.Graphics,
                                (step + 1).ToString(),
                                this.Font,
                                stepRect,
                                active || isCurrent ? Color.White : Color.Black,
                                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                try { LogCollection.Log($"PianoRollEditor Paint error: {ex.Message}"); } catch { }
            }
        }

        private void panel_roll_MouseClick(object sender, MouseEventArgs e)
        {
            if (this.trackRows.Count == 0)
            {
                return;
            }

            RollLayoutInfo layout = this.GetRollLayout();
            Point contentPoint = new(e.X - this.panel_roll.AutoScrollPosition.X, e.Y - this.panel_roll.AutoScrollPosition.Y);
            int rowIndex = this.GetRowIndexFromPoint(contentPoint, layout);
            if (rowIndex < 0)
            {
                return;
            }

            if (e.Button == MouseButtons.Right)
            {
                this.contextMenuRowIndex = rowIndex;
                this.contextMenuStrip_rows?.Show(this.panel_roll, e.Location);
                return;
            }

            if (e.Button == MouseButtons.Left)
            {
                int stepIndex = this.GetStepIndexFromPoint(contentPoint, rowIndex, layout);
                if (stepIndex >= 0)
                {
                    this.ToggleStep(rowIndex, stepIndex);
                }
            }
        }

        private int GetRowIndexFromPoint(Point contentPoint, RollLayoutInfo layout)
        {
            int relativeY = contentPoint.Y - RollPadding;
            if (relativeY < 0)
            {
                return -1;
            }

            int slotHeight = layout.RowHeight + RollRowSpacing;
            int rowIndex = relativeY / slotHeight;
            if (rowIndex < 0 || rowIndex >= this.trackRows.Count)
            {
                return -1;
            }

            return this.GetRowBounds(rowIndex, layout).Contains(contentPoint) ? rowIndex : -1;
        }

        private int GetStepIndexFromPoint(Point contentPoint, int rowIndex, RollLayoutInfo layout)
        {
            for (int step = 0; step < RollStepCount; step++)
            {
                if (this.GetStepBounds(rowIndex, step, layout).Contains(contentPoint))
                {
                    return step;
                }
            }

            return -1;
        }

        private void ToggleStep(int rowIndex, int stepIndex)
        {
            if (rowIndex < 0 || rowIndex >= this.trackRows.Count)
            {
                return;
            }

            bool[] steps = this.trackRows[rowIndex].Steps;
            if (stepIndex < 0 || stepIndex >= steps.Length)
            {
                return;
            }

            steps[stepIndex] = !steps[stepIndex];
            this.panel_roll.Invalidate();
        }

        private List<List<bool>> CaptureTrackStates()
        {
            var states = new List<List<bool>>(this.trackRows.Count);
            foreach (PianoTrackState row in this.trackRows)
            {
                states.Add([.. row.Steps]);
            }

            return states;
        }

        private void InitializeContextMenu()
        {
            this.contextMenuStrip_rows = new ContextMenuStrip();
            var remove = new ToolStripMenuItem("Remove Track");
            remove.Click += this.removeTrackToolStripMenuItem_Click;
            this.contextMenuStrip_rows.Items.Add(remove);
        }

        private async void removeTrackToolStripMenuItem_Click(object? sender, EventArgs e)
        {
            int rowIndex = this.contextMenuRowIndex;
            if (rowIndex < 0 || rowIndex >= this.trackRows.Count)
            {
                return;
            }

            AudioObj audio = this.trackRows[rowIndex].Audio;
            List<List<bool>> states = this.CaptureTrackStates();
            if (rowIndex < states.Count)
            {
                states.RemoveAt(rowIndex);
            }

            this.AudioC.Audios.ListChanged -= this.AudioC_Audios_ListChanged;
            try
            {
                this.AudioC.Audios.Remove(audio);
            }
            finally
            {
                this.AudioC.Audios.ListChanged += this.AudioC_Audios_ListChanged;
            }

            await this.RebuildTrackPanelsAsync(states);
        }

        private void button_playback_Click(object sender, EventArgs e)
        {
            if (this.isPlaying)
            {
                this.StopPlayback();
            }
            else
            {
                this.StartPlayback();
            }
        }

        private void StartPlayback()
        {
            if (this.isPlaying || this.trackRows.Count == 0)
            {
                return;
            }

            this.isPlaying = true;
            this.currentStep = 0;
            this.button_playback.Text = "■";

            this.playbackCts = new CancellationTokenSource();
            this.playbackTask = Task.Run(() => this.PlaybackLoopAsync(this.playbackCts.Token));
        }

        private void StopPlayback()
        {
            if (!this.isPlaying)
            {
                return;
            }

            this.isPlaying = false;
            this.button_playback.Text = "▶";

            try
            {
                this.playbackCts?.Cancel();
                this.playbackTask?.Wait(500);
            }
            catch { }
            finally
            {
                try { this.playbackCts?.Dispose(); } catch { }
                this.playbackCts = null;
                this.playbackTask = null;
            }

            try
            {
                if (this.IsHandleCreated && !this.IsDisposed)
                {
                    this.BeginInvoke((MethodInvoker) (() =>
                    {
                        this.currentStep = -1;
                        this.panel_roll.Invalidate();
                    }));
                }
            }
            catch { }
        }

        private async Task PlaybackLoopAsync(CancellationToken cancellationToken)
        {
            DateTimeOffset nextStepTime = DateTimeOffset.UtcNow;
            int stepIndex = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                if (nextStepTime <= now)
                {
                    int scheduledStep = stepIndex % RollStepCount;
                    List<int> activeTracks = this.GetActiveTrackIndicesForStep(scheduledStep);
                    foreach (int trackIndex in activeTracks)
                    {
                        if (trackIndex >= 0 && trackIndex < this.trackRows.Count)
                        {
                            _ = this.PlayTrackAsync(this.trackRows[trackIndex].Audio, cancellationToken);
                        }
                    }

                    try
                    {
                        if (this.IsHandleCreated && !this.IsDisposed)
                        {
                            this.BeginInvoke((MethodInvoker) (() =>
                            {
                                this.currentStep = scheduledStep;
                                this.panel_roll.Invalidate();
                            }));
                        }
                    }
                    catch { }

                    nextStepTime = nextStepTime + TimeSpan.FromMilliseconds(GetStepIntervalMs(this.Bpm));
                    stepIndex++;
                }

                try
                {
                    await Task.Delay(5, cancellationToken).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        private static int GetStepIntervalMs(float bpm)
        {
            if (bpm <= 0f)
            {
                return 125;
            }

            return (int) (60000.0f / bpm / 4.0f);
        }

        private List<int> GetActiveTrackIndicesForStep(int stepIndex)
        {
            List<int> activeTracks = [];
            for (int i = 0; i < this.trackRows.Count; i++)
            {
                if (stepIndex >= 0 && stepIndex < this.trackRows[i].Steps.Length && this.trackRows[i].Steps[stepIndex])
                {
                    activeTracks.Add(i);
                }
            }

            return activeTracks;
        }

        private async Task PlayTrackAsync(AudioObj audio, CancellationToken cancellationToken)
        {
            if (audio.Data == null || audio.Data.Length == 0)
            {
                return;
            }

            try
            {
                await audio.PlayAsync(cancellationToken, initialVolume: Math.Clamp(this.Volume, 0f, 1f)).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                try { LogCollection.Log($"PianoRollEditor playback failed for '{audio.Name}': {ex.Message}"); } catch { }
            }
        }

        private void numericUpDown_bpm_ValueChanged(object sender, EventArgs e)
        {
            if (this.isPlaying)
            {
                this.panel_roll.Invalidate();
            }
        }

        private void PianoRollEditor_Resize(object? sender, EventArgs e)
        {
            try
            {
                _ = this.ResizePanelsAndButtonsAsync();
            }
            catch (Exception ex)
            {
                try { LogCollection.Log($"PianoRollEditor Resize error: {ex.Message}"); } catch { }
            }
        }

        private void panel_roll_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            this.button_playback_Click(sender, EventArgs.Empty);
        }

        private void label_info_dragndrop_DragEnter(object sender, DragEventArgs e)
        {
            this.PianoRollEditor_DragEnter(sender, e);
        }

        private void label_info_dragndrop_DragDrop(object sender, DragEventArgs e)
        {
            this.PianoRollEditor_DragDrop(sender, e);
        }

        private void panel_roll_DragEnter(object sender, DragEventArgs e)
        {
            this.PianoRollEditor_DragEnter(sender, e);
        }

        private void panel_roll_DragDrop(object sender, DragEventArgs e)
        {
            this.PianoRollEditor_DragDrop(sender, e);
        }
    }
}
