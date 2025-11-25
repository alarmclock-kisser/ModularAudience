using ModularAudience.Audio;
using ModularAudience.Forms.Modules;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ModularAudience.Forms
{
    public partial class AudioCollectionView : Form
    {
        internal  readonly AudioCollection AudioC = new();

        internal IEnumerable<AudioObj> SelectedAudios => this.listBox_audios.SelectedItems.Cast<AudioObj>().OfType<AudioObj>();

        private Point? _dragOrigin;
        private AudioObj? _dragCandidate;
        private int _hoverInsertIndex = -1;
        private int _preDragSelection = -1;
        private List<AudioObj>? _dragSelectionSnapshot;
        private const double AutoPlayMaxSeconds = 10.0;
        private readonly Lock autoPlayGate = new();
        private CancellationTokenSource? autoPlayCts;
        private AudioObj? autoPlayCurrent;
        private int selectionVersion;
        private int lastMouseClickSelectionVersion = -1;
        private int selectionSuppressionDepth;
        private bool preserveDragSnapshot;
        private bool _dragStarted;
        private int _pendingSingleSelectionIndex = -1;
        private readonly SemaphoreSlim autoPlayLock = new(1, 1);
        private List<AudioObj>? _mouseDownSelectionSnapshot;
        private List<AudioObj> _committedSelection = [];
        private bool _restoringSelectionDuringDrag;
        private static readonly HashSet<char> InvalidFileNameChars = [.. Path.GetInvalidFileNameChars()];

        public int AudioCount => this.AudioC.Audios.Count;

		private System.Windows.Forms.Timer waveformPreviewTimer;
        private int waveformPreviewIndex = -1;
        private WaveformPreview? waveformPreviewForm;
        private Point lastMousePos;

        public AudioCollectionView(IEnumerable<AudioObj> audios)
        {
            this.InitializeComponent();
            this.StartPosition = FormStartPosition.Manual;

            this.Text = "Audio Collection #" + (WindowMain.CollectionViews.Count + 1).ToString("D2");

            foreach (AudioObj audio in audios)
            {
                this.AudioC.Audios.Add(audio);
            }

            this.listBox_audios.Items.Clear();
            this.listBox_audios.DataSource = this.AudioC.Audios;
            this.listBox_audios.DisplayMember = "Name";

            this.listBox_audios.SelectedIndex = -1;
            this.listBox_audios.AllowDrop = true;
            this.listBox_audios.DrawMode = DrawMode.OwnerDrawFixed;
            this.listBox_audios.MouseDown += this.listBox_audios_MouseDown;
            this.listBox_audios.MouseMove += this.listBox_audios_MouseMove;
            this.listBox_audios.MouseUp += this.listBox_audios_MouseUp;
            this.listBox_audios.MouseClick += this.listBox_audios_MouseClick;
            this.listBox_audios.DoubleClick += this.listBox_audios_DoubleClick;
            this.listBox_audios.DragEnter += this.listBox_audios_DragEnter;
            this.listBox_audios.DragOver += this.listBox_audios_DragOver;
            this.listBox_audios.DragDrop += this.listBox_audios_DragDrop;
            this.listBox_audios.DragLeave += this.listBox_audios_DragLeave;
            this.listBox_audios.GiveFeedback += this.listBox_audios_GiveFeedback;
            this.listBox_audios.DrawItem += this.listBox_audios_DrawItem;
            this.listBox_audios.SelectedIndexChanged += this.listBox_audios_SelectedIndexChanged;
            this.checkBox_autoPlay.CheckedChanged += this.checkBox_autoPlay_CheckedChanged;

            this.CacheCommittedSelection();

            this.FormClosing += async (s, e) =>
            {
                e.Cancel = true;
                await this.CancelAutoPlayAsync(stopCollection: true).ConfigureAwait(false);
                this.Hide();
                await this.AudioC.ClearAsync().ConfigureAwait(false);
                this.AudioC.Dispose();

                WindowMain.CollectionViews.Remove(this);
            };

            this.waveformPreviewTimer = new System.Windows.Forms.Timer { Interval = 700 };
            this.waveformPreviewTimer.Tick += this.WaveformPreviewTimer_Tick;
            this.listBox_audios.MouseMove += this.ListBox_audios_MouseMove_WaveformPreview;
            this.listBox_audios.MouseLeave += this.ListBox_audios_MouseLeave_WaveformPreview;

            // --- Breite automatisch anpassen, damit kein unnötiges horizontales Scrollen nötig ist ---
            int maxWidth = 1080; // Maximale Breite
            int minWidth = this.Width; // Designer-Default
            int requiredWidth = minWidth;
            using (Graphics g = this.listBox_audios.CreateGraphics())
            {
                for (int i = 0; i < this.listBox_audios.Items.Count; i++)
                {
                    if (this.listBox_audios.Items[i] is AudioObj audio)
                    {
                        string text = audio.Name ?? string.Empty;
                        Size textSize = TextRenderer.MeasureText(g, text, this.listBox_audios.Font);
                        int itemWidth = textSize.Width + 120; // Platz für Dauer, Padding, etc.
                        if (itemWidth > requiredWidth)
                            requiredWidth = itemWidth;
                    }
                }
            }
            // Add scrollbar width if needed
            if (this.listBox_audios.Items.Count > this.listBox_audios.ClientSize.Height / this.listBox_audios.ItemHeight)
                requiredWidth += SystemInformation.VerticalScrollBarWidth;
            requiredWidth = Math.Min(Math.Max(requiredWidth + 40, minWidth), maxWidth);
            if (this.Width < requiredWidth)
            {
                this.Width = requiredWidth;
            }
        }



        // ListBox entry richt-click event to show context menu for rename and delete
        private void listBox_audios_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                this._pendingSingleSelectionIndex = -1;
                this._dragStarted = false;
                int index = this.listBox_audios.IndexFromPoint(e.Location);
                if (index != ListBox.NoMatches)
                {
                    bool ctrlDown = (Control.ModifierKeys & Keys.Control) != 0;
                    AudioObj? clickedAudio = this.listBox_audios.Items[index] as AudioObj;
                    List<AudioObj> committedSelection = this._committedSelection?.ToList() ?? [];
                    bool clickedSelected = clickedAudio != null && committedSelection.Contains(clickedAudio);
                    bool hadMultiSelection = committedSelection.Count > 1;
                    this._mouseDownSelectionSnapshot = committedSelection;
                    if (!ctrlDown && (!clickedSelected || committedSelection.Count == 0) && clickedAudio != null)
                    {
                        this._mouseDownSelectionSnapshot = [clickedAudio];
                        clickedSelected = false;
                        hadMultiSelection = false;
                    }

                    this.preserveDragSnapshot = false;
                    this._dragSelectionSnapshot = null;
                    this._preDragSelection = this.listBox_audios.SelectedIndex;
                    this._dragOrigin = e.Location;
                    this._dragCandidate = clickedAudio;

                    if (!ctrlDown)
                    {
                        this._pendingSingleSelectionIndex = index;
                        if (clickedSelected && hadMultiSelection)
                        {
                            this.preserveDragSnapshot = true;
                            this._dragSelectionSnapshot = [.. this._mouseDownSelectionSnapshot];
                            this.RestoreSelectionFromList(this._dragSelectionSnapshot);
                            this.BeginInvoke(new Action(() =>
                            {
                                if (this.preserveDragSnapshot && this._dragSelectionSnapshot != null)
                                {
                                    this.RestoreSelectionFromList(this._dragSelectionSnapshot);
                                }
                            }));
                        }
                        else if (clickedAudio != null)
                        {
                            this._dragSelectionSnapshot = [clickedAudio];
                        }
                        this.ScheduleSelectionSnapshotCapture();
                    }
                    else
                    {
                        this._pendingSingleSelectionIndex = -1;
                        this.ScheduleSelectionSnapshotCapture();
                    }
                }
                else
                {
                    this.ResetDragState();
                }
                this.RefreshListVisuals();
                return;
            }

            if (e.Button == MouseButtons.Right)
            {
                int index = this.listBox_audios.IndexFromPoint(e.Location);
                if (index != ListBox.NoMatches)
                {
                    if (!this.listBox_audios.GetSelected(index))
                    {
                        this.listBox_audios.SelectedIndex = index;
                    }
                    ContextMenuStrip contextMenu = new();
                    ToolStripMenuItem renameItem = new("Rename");
                    renameItem.Click += (s, ev) =>
                    {
                        AudioObj? selectedAudio = (AudioObj?) this.listBox_audios.SelectedItem;
                        if (selectedAudio != null)
                        {
                            string input = Microsoft.VisualBasic.Interaction.InputBox("Enter new name:", "Rename Audio", selectedAudio.Name);
                            if (!string.IsNullOrWhiteSpace(input))
                            {
                                selectedAudio.Name = input;
                                this.listBox_audios.Refresh();
                            }
                        }
                    };
                    ToolStripMenuItem deleteItem = new("Delete");
                    deleteItem.Click += async (s, ev) =>
                    {
                        List<AudioObj> toDelete = this.listBox_audios.SelectedItems.Cast<AudioObj>().OfType<AudioObj>().ToList();
                        if (toDelete.Count == 0)
                        {
                            if (index >= 0 && index < this.listBox_audios.Items.Count && this.listBox_audios.Items[index] is AudioObj fallback)
                            {
                                toDelete.Add(fallback);
                            }
                        }

                        if (toDelete.Count == 0)
                        {
                            return;
                        }

                        int previousTopIndex = this.listBox_audios.TopIndex;

                        foreach (AudioObj audio in toDelete)
                        {
                            await this.AudioC.RemoveAsync(audio.Id);
                        }

                        this.listBox_audios.DataSource = null;
                        this.listBox_audios.DataSource = this.AudioC.Audios;
                        this.listBox_audios.DisplayMember = "Name";
                        this.RestoreScrollPosition(previousTopIndex);
                    };
                    contextMenu.Items.AddRange([renameItem, deleteItem]);
                    contextMenu.Show(this.listBox_audios, e.Location);
                }
            }
        }

        private void CaptureSelectionSnapshot(IEnumerable<AudioObj>? source = null)
        {
            IEnumerable<AudioObj> items = source ?? this.listBox_audios.SelectedItems.Cast<AudioObj>();
            this._dragSelectionSnapshot = items.OfType<AudioObj>().ToList();
        }

        private void ScheduleSelectionSnapshotCapture()
        {
            if (this.preserveDragSnapshot)
            {
                return;
            }

            this._dragSelectionSnapshot = null;
            this.BeginInvoke(new Action(() =>
            {
                this._dragCandidate = this.listBox_audios.SelectedItem as AudioObj;
                this.CaptureSelectionSnapshot();
            }));
        }

        private void RestoreDragSelectionSnapshot()
        {
            if (this._dragSelectionSnapshot == null)
            {
                return;
            }

            this.RestoreSelectionFromList(this._dragSelectionSnapshot);
        }

        private void RestoreSelectionFromList(IReadOnlyCollection<AudioObj> snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            this.WithSelectionSuppressed(() =>
            {
                this.listBox_audios.ClearSelected();
                foreach (var audio in snapshot)
                {
                    int idx = this.AudioC.Audios.IndexOf(audio);
                    if (idx >= 0)
                    {
                        this.listBox_audios.SetSelected(idx, true);
                    }
                }
            });
        }

        private bool IsPlainLeftMouseDrag()
        {
            return (Control.MouseButtons & MouseButtons.Left) == MouseButtons.Left &&
                   (Control.ModifierKeys & (Keys.Control | Keys.Shift)) == 0;
        }

        private IReadOnlyList<AudioObj>? GetVisualSelectionOverride()
        {
            if (this._dragStarted && this._dragSelectionSnapshot != null && this._dragSelectionSnapshot.Count > 0)
            {
                return this._dragSelectionSnapshot;
            }

            if (this.IsPlainLeftMouseDrag() && this._mouseDownSelectionSnapshot != null && this._mouseDownSelectionSnapshot.Count > 0)
            {
                return this._mouseDownSelectionSnapshot;
            }

            return null;
        }

        private bool IsAudioVisuallySelected(AudioObj audio, int index)
        {
            IReadOnlyList<AudioObj>? visualSelection = this.GetVisualSelectionOverride();
            if (visualSelection != null)
            {
                return visualSelection.Contains(audio);
            }

            return this.listBox_audios.GetSelected(index);
        }

        private void RefreshListVisuals()
        {
            this.listBox_audios.Invalidate();
        }

        private void RestoreScrollPosition(int previousTopIndex)
        {
            if (previousTopIndex < 0 || this.listBox_audios.Items.Count == 0)
            {
                return;
            }

            int maxTopIndex = this.listBox_audios.Items.Count - 1;
            int clamped = Math.Max(0, Math.Min(previousTopIndex, maxTopIndex));
            if (this.listBox_audios.TopIndex != clamped)
            {
                this.listBox_audios.TopIndex = clamped;
            }
        }

        private static string SanitizePathSegment(string? value)
        {
            string source = string.IsNullOrWhiteSpace(value) ? "Audio" : value!;
            char[] sanitized = source
                .Select(ch => InvalidFileNameChars.Contains(ch) ? '_' : ch)
                .ToArray();
            string result = new string(sanitized).Trim('_', ' ');
            return string.IsNullOrWhiteSpace(result) ? "Audio" : result;
        }

        private string BuildExportFolderName(string folderDirectory)
        {
            string baseName = SanitizePathSegment(this.Text);
            string exportFolderPath = Path.Combine(folderDirectory, baseName);
            if (!Directory.Exists(exportFolderPath))
            {
                return exportFolderPath;
            }

            try
            {
                // Ermittle alle direkten Unterordner, deren Name baseName oder baseName_### entspricht
                var siblings = Directory.EnumerateDirectories(folderDirectory, baseName + "*", SearchOption.TopDirectoryOnly)
                    .Select(path => Path.GetFileName(path) ?? string.Empty)
                    .Where(name => !string.IsNullOrEmpty(name))
                    .ToList();

                // Sammle numerische Suffixe; baseName ohne Suffix wird als 1 betrachtet
                var numbers = siblings
                    .Select(name =>
                    {
                        if (string.Equals(name, baseName, StringComparison.OrdinalIgnoreCase))
                        {
                            return 1;
                        }

                        if (name.Length > baseName.Length + 1 && name.StartsWith(baseName + "_", StringComparison.OrdinalIgnoreCase))
                        {
                            var suffix = name.Substring(baseName.Length + 1);
                            if (int.TryParse(suffix, out int n) && n >= 2)
                            {
                                return n;
                            }
                        }

                        return -1;
                    })
                    .Where(n => n > 0)
                    .ToList();

                int nextSuffix = 2;
                if (numbers.Count > 0)
                {
                    int max = numbers.Max();
                    nextSuffix = Math.Max(2, max + 1);
                }

                // Baue Kandidatenpfad (keine Endlosschleife)
                string candidateName = $"{baseName}_{nextSuffix}";
                return Path.Combine(folderDirectory, candidateName);
            }
            catch
            {
                // Fallback: begrenzte Suche bis zu einem hohen Wert, danach zufälliger Suffix
                for (int suffix = 2; suffix <= 9999; suffix++)
                {
                    string candidateName = $"{baseName}_{suffix}";
                    string candidatePath = Path.Combine(folderDirectory, candidateName);
                    if (!Directory.Exists(candidatePath))
                    {
                        return candidatePath;
                    }
                }

                // letzter Ausweg: eindeutiger Suffix
                string fallback = $"{baseName}_{Guid.NewGuid():N}".Substring(0, Math.Min(64, baseName.Length + 9));
                return Path.Combine(folderDirectory, fallback);
            }
        }

        private static string EnsureWavExtension(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return filePath;
            }

            return string.Equals(Path.GetExtension(filePath), ".wav", StringComparison.OrdinalIgnoreCase)
                ? filePath
                : Path.ChangeExtension(filePath, ".wav");
        }

        private void listBox_audios_MouseMove(object? sender, MouseEventArgs e)
        {
            if (!this._dragOrigin.HasValue || (e.Button & MouseButtons.Left) != MouseButtons.Left || this._dragCandidate == null)
            {
                return;
            }

            Size dragSize = SystemInformation.DragSize;
            Rectangle dragRect = new(
                this._dragOrigin.Value.X - dragSize.Width / 2,
                this._dragOrigin.Value.Y - dragSize.Height / 2,
                dragSize.Width,
                dragSize.Height);

            if (!dragRect.Contains(e.Location))
            {
                this.BeginAudioDrag();
            }
        }

        private void listBox_audios_MouseUp(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (!this._dragStarted && this._pendingSingleSelectionIndex >= 0 && this._pendingSingleSelectionIndex < this.listBox_audios.Items.Count)
                {
                    this.listBox_audios.SelectedIndex = this._pendingSingleSelectionIndex;
                }
                this.ResetDragState();
            }
        }

        private async void listBox_audios_MouseClick(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || !this.checkBox_autoPlay.Checked || (Control.ModifierKeys & Keys.Control) != 0)
            {
                return;
            }

            int index = this.listBox_audios.IndexFromPoint(e.Location);
            if (index == ListBox.NoMatches)
            {
                return;
            }

            if (this.listBox_audios.SelectedIndices.Count != 1)
            {
                return;
            }

            if (this.selectionVersion != this.lastMouseClickSelectionVersion)
            {
                this.lastMouseClickSelectionVersion = this.selectionVersion;
                return;
            }

            if (index != this.listBox_audios.SelectedIndex)
            {
                return;
            }

            if (this.listBox_audios.Items[index] is AudioObj audio)
            {
                await this.TriggerAutoPlayAsync(audio).ConfigureAwait(false);
            }
        }

        private async void listBox_audios_DoubleClick(object? sender, EventArgs e)
        {
            AudioObj? selectedAudio = (AudioObj?) this.listBox_audios.SelectedItem;
            if (selectedAudio != null)
            {
                WindowMain.TrackViews.Add(new TrackView(selectedAudio, this.AudioC));
            }

            await this.AudioC.StopAllAsync();
        }

        private void listBox_audios_DragEnter(object? sender, DragEventArgs e)
        {
            if (TryGetDragPayload(e.Data, out _))
            {
                e.Effect = DragDropEffects.Move;
                this.listBox_audios.Cursor = Cursors.Hand;
            }
            else
            {
                e.Effect = DragDropEffects.None;
                this.listBox_audios.Cursor = Cursors.No;
            }
        }

        private void listBox_audios_DragOver(object? sender, DragEventArgs e)
        {
            if (!TryGetDragPayload(e.Data, out _))
            {
                e.Effect = DragDropEffects.None;
                return;
            }

            e.Effect = DragDropEffects.Move;
            Point clientPoint = this.listBox_audios.PointToClient(new Point(e.X, e.Y));
            if (!this.listBox_audios.ClientRectangle.Contains(clientPoint))
            {
                this.HighlightInsertionIndex(-1);
                e.Effect = DragDropEffects.None;
                return;
            }

            this.AutoScrollDuringDrag(clientPoint.Y);
            clientPoint = this.listBox_audios.PointToClient(new Point(e.X, e.Y));
            int insertIndex = this.GetInsertionIndex(clientPoint);
            this.HighlightInsertionIndex(insertIndex);
        }

        private void listBox_audios_DragLeave(object? sender, EventArgs e)
        {
            this.listBox_audios.Cursor = Cursors.Default;
            this.HighlightInsertionIndex(-1);
        }

        private void listBox_audios_DragDrop(object? sender, DragEventArgs e)
        {
            this.listBox_audios.Cursor = Cursors.Default;
            if (!TryGetDragPayload(e.Data, out var payload) || payload.Audios.Count == 0)
            {
                this.HighlightInsertionIndex(-1);
                return;
            }

            Point clientPoint = this.listBox_audios.PointToClient(new Point(e.X, e.Y));
            int insertIndex = this.GetInsertionIndex(clientPoint);
            this.MoveAudio(payload, insertIndex);
            this.HighlightInsertionIndex(-1);
        }

        private void listBox_audios_GiveFeedback(object? sender, GiveFeedbackEventArgs e)
        {
            // Use a plus cursor for Copy, hand for Move, no for None
            e.UseDefaultCursors = false;
            if ((e.Effect & DragDropEffects.Copy) == DragDropEffects.Copy)
            {
                Cursor.Current = Cursors.Cross; // Plus symbol
            }
            else if ((e.Effect & DragDropEffects.Move) == DragDropEffects.Move)
            {
                Cursor.Current = Cursors.Hand;
            }
            else
            {
                Cursor.Current = Cursors.No;
            }
        }

        private void BeginAudioDrag()
        {
            this._pendingSingleSelectionIndex = -1;
            var selection = this.BuildDragSelection();
            if (selection.Count == 0)
            {
                return;
            }

            this.RestoreSelectionFromList(selection);
            this._dragStarted = true;
            this.RefreshListVisuals();
            var payload = new AudioDragPayload(this, selection);
            // --- PATCH: Add compatible DataObject for external drop targets (like DrumRollEditor) ---
            DataObject data = new DataObject();
            data.SetData(typeof(AudioDragPayload), payload);
            data.SetData(typeof(List<AudioObj>), selection);
            data.SetData(typeof(IEnumerable<AudioObj>), selection);
            data.SetData(DataFormats.Serializable, selection);
            try
            {
                this.listBox_audios.DoDragDrop(data, DragDropEffects.Copy | DragDropEffects.Move);
            }
            finally
            {
                this.ResetDragState();
            }
        }

        private List<AudioObj> BuildDragSelection()
        {
            if (this._dragSelectionSnapshot != null && this._dragSelectionSnapshot.Count > 0)
            {
                return this.NormalizeSelectionOrder(this._dragSelectionSnapshot);
            }

            var current = this.listBox_audios.SelectedItems.Cast<AudioObj>().OfType<AudioObj>().ToList();
            if (current.Count > 0)
            {
                return this.NormalizeSelectionOrder(current);
            }

            if (this._mouseDownSelectionSnapshot != null && this._mouseDownSelectionSnapshot.Count > 0)
            {
                return this.NormalizeSelectionOrder(this._mouseDownSelectionSnapshot);
            }

            if (this._committedSelection != null && this._committedSelection.Count > 0)
            {
                return this.NormalizeSelectionOrder(this._committedSelection);
            }

            if (this._dragCandidate != null)
            {
                return [this._dragCandidate];
            }

            return [];
        }

        private const int DragScrollMarginPixels = 20;
        private const int DragScrollStep = 1;

        private List<AudioObj> NormalizeSelectionOrder(IEnumerable<AudioObj> selection)
        {
            return selection
                .Select(audio => new { Audio = audio, Index = this.AudioC.Audios.IndexOf(audio) })
                .Where(entry => entry.Index >= 0)
                .OrderBy(entry => entry.Index)
                .Select(entry => entry.Audio)
                .ToList();
        }

        private void ResetDragState()
        {
            this._dragOrigin = null;
            this._dragCandidate = null;
            this._hoverInsertIndex = -1;
            this.listBox_audios.Cursor = Cursors.Default;
            this._dragSelectionSnapshot = null;
            this.preserveDragSnapshot = false;
            this._dragStarted = false;
            this._pendingSingleSelectionIndex = -1;
            this._mouseDownSelectionSnapshot = null;
            this.RefreshListVisuals();
        }

        private void AutoScrollDuringDrag(int mouseY)
        {
            if (this.listBox_audios.Items.Count == 0)
            {
                return;
            }

            int topMargin = DragScrollMarginPixels;
            int bottomMargin = this.listBox_audios.ClientSize.Height - DragScrollMarginPixels;

            if (mouseY < topMargin && this.listBox_audios.TopIndex > 0)
            {
                this.listBox_audios.TopIndex = Math.Max(0, this.listBox_audios.TopIndex - DragScrollStep);
            }
            else if (mouseY > bottomMargin)
            {
                int maxTop = Math.Max(0, this.listBox_audios.Items.Count - 1);
                if (this.listBox_audios.TopIndex < maxTop)
                {
                    this.listBox_audios.TopIndex = Math.Min(maxTop, this.listBox_audios.TopIndex + DragScrollStep);
                }
            }
        }

        private void WithSelectionSuppressed(Action action)
        {
            this.selectionSuppressionDepth++;
            try
            {
                action();
            }
            finally
            {
                this.selectionSuppressionDepth = Math.Max(0, this.selectionSuppressionDepth - 1);
            }
        }

        private int GetInsertionIndex(Point clientPoint)
        {
            int index = this.listBox_audios.IndexFromPoint(clientPoint);
            if (index == ListBox.NoMatches)
            {
                return this.AudioC.Audios.Count;
            }

            Rectangle itemRect = this.listBox_audios.GetItemRectangle(index);
            bool insertAfter = clientPoint.Y > itemRect.Top + itemRect.Height / 2;
            return insertAfter ? index + 1 : index;
        }

        private void HighlightInsertionIndex(int insertIndex)
        {
            int normalized = Math.Clamp(insertIndex, -1, Math.Max(0, this.AudioC.Audios.Count));
            if (this._hoverInsertIndex == normalized)
            {
                return;
            }

            this._hoverInsertIndex = normalized;
            this.RefreshListVisuals();
        }

        private void MoveAudio(AudioDragPayload payload, int insertIndex)
        {
            var itemsToMove = payload.Audios.Where(a => a != null).Distinct().ToList();
            if (itemsToMove.Count == 0)
            {
                return;
            }

            var sourceList = payload.SourceView.AudioC.Audios;
            var targetList = this.AudioC.Audios;
            bool sameView = payload.SourceView == this;
            int sourceTopIndex = payload.SourceView.listBox_audios.TopIndex;
            int targetTopIndex = this.listBox_audios.TopIndex;

            if (sameView)
            {
                var indexedItems = itemsToMove
                    .Select(a => new { Audio = a, Index = sourceList.IndexOf(a) })
                    .Where(entry => entry.Index >= 0)
                    .OrderBy(entry => entry.Index)
                    .ToList();

                if (indexedItems.Count == 0)
                {
                    return;
                }

                int targetIndex = Math.Clamp(insertIndex, 0, targetList.Count);
                int beforeCount = indexedItems.Count(entry => entry.Index < targetIndex);
                targetIndex -= beforeCount;

                foreach (var entry in indexedItems.OrderByDescending(e => e.Index))
                {
                    sourceList.RemoveAt(entry.Index);
                }

                targetIndex = Math.Clamp(targetIndex, 0, targetList.Count);
                foreach (var entry in indexedItems)
                {
                    targetList.Insert(targetIndex++, entry.Audio);
                }

                itemsToMove = indexedItems.Select(entry => entry.Audio).ToList();
            }
            else
            {
                foreach (var audio in itemsToMove)
                {
                    sourceList.Remove(audio);
                }

                insertIndex = Math.Clamp(insertIndex, 0, targetList.Count);
                foreach (var audio in itemsToMove)
                {
                    targetList.Insert(insertIndex++, audio);
                }
            }

            foreach (var audio in itemsToMove)
            {
                WindowMain.UpdateCollectionTag(audio, this);
            }

            if (targetList.Count > 0)
            {
                this.WithSelectionSuppressed(() =>
                {
                    this.listBox_audios.ClearSelected();
                    foreach (var audio in itemsToMove)
                    {
                        int idx = targetList.IndexOf(audio);
                        if (idx >= 0)
                        {
                            this.listBox_audios.SetSelected(idx, true);
                            this._preDragSelection = idx;
                        }
                    }
                });
            }

            this.RestoreScrollPosition(targetTopIndex);
            if (!sameView)
            {
                payload.SourceView.RestoreScrollPosition(sourceTopIndex);
            }
        }

        private static bool TryGetDragPayload(IDataObject? data, out AudioDragPayload payload)
        {
            payload = default!;
            if (data?.GetDataPresent(typeof(AudioDragPayload)) == true)
            {
                if (data.GetData(typeof(AudioDragPayload)) is AudioDragPayload dragPayload && dragPayload.Audios.Count > 0)
                {
                    payload = dragPayload;
                    return true;
                }
            }
            return false;
        }

        private void listBox_audios_DrawItem(object? sender, DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index < 0 || e.Index >= this.listBox_audios.Items.Count)
            {
                return;
            }

            if (this.listBox_audios.Items[e.Index] is not AudioObj audio)
            {
                return;
            }

            bool isSelected = this.IsAudioVisuallySelected(audio, e.Index);
            Color backColor = isSelected ? SystemColors.Highlight : this.listBox_audios.BackColor;
            Color foreColor = isSelected ? SystemColors.HighlightText : this.listBox_audios.ForeColor;
            using (SolidBrush brush = new(backColor))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }

            const int padding = 4;
            string durationText = "(" + FormatDurationText(audio) + ")";
            TextFormatFlags durationFlags = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            Size durationSize = TextRenderer.MeasureText(e.Graphics, durationText, e.Font, e.Bounds.Size, durationFlags | TextFormatFlags.NoPadding);
            int durationWidth = Math.Max(0, durationSize.Width + padding);
            int durationLeft = e.Bounds.Right - durationWidth;
            if (durationLeft < e.Bounds.Left + padding)
            {
                durationLeft = e.Bounds.Left + padding;
            }

            Rectangle durationRect = new(durationLeft, e.Bounds.Top, durationWidth, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, durationText, e.Font, durationRect, foreColor, durationFlags);

            int nameWidth = Math.Max(0, durationLeft - e.Bounds.Left - padding);
            Rectangle nameRect = new(e.Bounds.Left + padding, e.Bounds.Top, nameWidth, e.Bounds.Height);
            TextFormatFlags nameFlags = TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            TextRenderer.DrawText(e.Graphics, audio.Name ?? string.Empty, e.Font, nameRect, foreColor, nameFlags);

            bool drawTopInsertion = this._hoverInsertIndex == e.Index;
            bool drawBottomInsertion = this._hoverInsertIndex == e.Index + 1 && e.Index == this.AudioC.Audios.Count - 1;
            if (drawTopInsertion || drawBottomInsertion)
            {
                using Pen pen = new(SystemColors.Highlight, 2);
                if (drawTopInsertion)
                {
                    e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Top, e.Bounds.Right, e.Bounds.Top);
                }

                if (drawBottomInsertion)
                {
                    e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                }
            }

            e.DrawFocusRectangle();
        }

        private async void listBox_audios_SelectedIndexChanged(object? sender, EventArgs e)
        {
            this.selectionVersion++;

            try
            {
                if (this.selectionSuppressionDepth > 0)
                {
                    this.lastMouseClickSelectionVersion = this.selectionVersion;
                    return;
                }

                if ((Control.MouseButtons & MouseButtons.Left) == MouseButtons.Left && (Control.ModifierKeys & (Keys.Control | Keys.Shift)) == 0 && this._dragOrigin.HasValue)
                {
                    if (!this._restoringSelectionDuringDrag && this._mouseDownSelectionSnapshot != null && this._mouseDownSelectionSnapshot.Count > 0)
                    {
                        try
                        {
                            this._restoringSelectionDuringDrag = true;
                            this.RestoreSelectionFromList(this._mouseDownSelectionSnapshot);
                        }
                        finally
                        {
                            this._restoringSelectionDuringDrag = false;
                        }
                        this.lastMouseClickSelectionVersion = this.selectionVersion;
                        return;
                    }
                }

                if ((Control.ModifierKeys & Keys.Control) != 0 || !this.checkBox_autoPlay.Checked)
                {
                    this.lastMouseClickSelectionVersion = this.selectionVersion;
                    return;
                }

                if (this.listBox_audios.SelectedItem is not AudioObj)
                {
                    this.lastMouseClickSelectionVersion = this.selectionVersion;
                    return;
                }

                this.lastMouseClickSelectionVersion = this.selectionVersion;
            }
            finally
            {
                this.CacheCommittedSelection();
            }
        }


        private async void checkBox_autoPlay_CheckedChanged(object? sender, EventArgs e)
        {
            if (!this.checkBox_autoPlay.Checked)
            {
                await this.CancelAutoPlayAsync(stopCollection: true).ConfigureAwait(false);
            }
        }

        private void CacheCommittedSelection()
        {
            this._committedSelection = this.listBox_audios.SelectedItems.Cast<AudioObj>().OfType<AudioObj>().ToList();
        }

        private static string FormatDurationText(AudioObj audio)
        {
            TimeSpan duration = ResolveDuration(audio);
            if (duration.TotalMilliseconds > 0 && duration.TotalMilliseconds < 2500)
            {
                int ms = Math.Max(1, (int) Math.Round(duration.TotalMilliseconds));
                return ms.ToString("0", CultureInfo.InvariantCulture) + " ms";
            }

            int minutes = Math.Max(0, (int) duration.TotalMinutes);
            int seconds = Math.Clamp(duration.Seconds, 0, 59);
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", minutes, seconds);
        }

        private static TimeSpan ResolveDuration(AudioObj audio)
        {
            if (audio.Duration > TimeSpan.Zero)
            {
                return audio.Duration;
            }

            if (audio.Data != null && audio.Data.Length > 0 && audio.SampleRate > 0)
            {
                int channels = Math.Max(1, audio.Channels);
                double totalFrames = audio.Data.LongLength / (double) channels;
                double seconds = totalFrames / audio.SampleRate;
                if (seconds > 0)
                {
                    return TimeSpan.FromSeconds(seconds);
                }
            }

            return TimeSpan.Zero;
        }

        private async Task TriggerAutoPlayAsync(AudioObj audio)
        {
            if (audio == null)
            {
                return;
            }

            await this.autoPlayLock.WaitAsync().ConfigureAwait(false);
            var cts = new CancellationTokenSource();
            bool disposeCts = false;
            try
            {
                await this.CancelAutoPlayInternalAsync(stopCollection: false).ConfigureAwait(false);
                await this.AudioC.StopAllAsync().ConfigureAwait(false);

                lock (this.autoPlayGate)
                {
                    this.autoPlayCts = cts;
                    this.autoPlayCurrent = audio;
                }

                try
                {
                    await audio.StopAsync().ConfigureAwait(false);
                    await audio.PlayAsync(cts.Token, () => this.OnAutoPlayPlaybackStopped(cts), 0.66f).ConfigureAwait(false);
                    _ = this.StopAfterDelayAsync(audio, cts);
                }
                catch (OperationCanceledException)
                {
                    this.ReleaseAutoPlayReference(cts);
                    disposeCts = true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Auto-play failed: {ex.Message}");
                    this.ReleaseAutoPlayReference(cts);
                    disposeCts = true;
                    _ = audio.StopAsync();
                }
            }
            finally
            {
                this.autoPlayLock.Release();
                if (disposeCts)
                {
                    cts.Dispose();
                }
            }
        }

        private async Task StopAfterDelayAsync(AudioObj audio, CancellationTokenSource cts)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(AutoPlayMaxSeconds), cts.Token).ConfigureAwait(false);
                await audio.StopAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // expected when selection changes or dialog closes
            }
        }

        private async Task CancelAutoPlayAsync(bool stopCollection = false)
        {
            await this.autoPlayLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await this.CancelAutoPlayInternalAsync(stopCollection).ConfigureAwait(false);
            }
            finally
            {
                this.autoPlayLock.Release();
            }
        }

        private async Task CancelAutoPlayInternalAsync(bool stopCollection)
        {
            CancellationTokenSource? cts;
            AudioObj? current;
            lock (this.autoPlayGate)
            {
                cts = this.autoPlayCts;
                current = this.autoPlayCurrent;
                this.autoPlayCts = null;
                this.autoPlayCurrent = null;
            }

            if (cts != null)
            {
                try { cts.Cancel(); } catch { }
                cts.Dispose();
            }

            if (current != null)
            {
                try
                {
                    await current.StopAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Auto-play stop failed: {ex.Message}");
                }
            }

            if (stopCollection)
            {
                await this.AudioC.StopAllAsync().ConfigureAwait(false);
            }
        }
        private void OnAutoPlayPlaybackStopped(CancellationTokenSource cts)
        {
            bool dispose = false;
            lock (this.autoPlayGate)
            {
                if (!ReferenceEquals(this.autoPlayCts, cts))
                {
                    return;
                }

                this.autoPlayCts = null;
                this.autoPlayCurrent = null;
                dispose = true;
            }

            if (dispose)
            {
                cts.Dispose();
            }
        }

        private void ReleaseAutoPlayReference(CancellationTokenSource cts)
        {
            lock (this.autoPlayGate)
            {
                if (ReferenceEquals(this.autoPlayCts, cts))
                {
                    this.autoPlayCts = null;
                    this.autoPlayCurrent = null;
                }
            }
        }



        private async void button_export_Click(object sender, EventArgs e)
        {
            this.button_export.Enabled = false;

            try
            {
                bool ctrlDown = (Control.ModifierKeys & Keys.Control) != 0;
                bool shiftDown = (Control.ModifierKeys & Keys.Shift) != 0;

                List<AudioObj> selectedAudios = this.listBox_audios.SelectedItems.Cast<AudioObj>().OfType<AudioObj>().ToList();
                if (shiftDown)
                {
                    selectedAudios = this.AudioC.Audios.ToList();
                }

                if (selectedAudios.Count == 0)
                {
                    MessageBox.Show(this, "Please select at least one audio first.", "Export", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                if (ctrlDown && !shiftDown && selectedAudios.Count == 1)
                {
                    AudioObj audio = selectedAudios[0];
                    string defaultName = SanitizePathSegment(audio.Name) + ".wav";

                    using SaveFileDialog saveDialog = new()
                    {
                        Filter = "Wave Files (*.wav)|*.wav|All Files (*.*)|*.*",
                        DefaultExt = "wav",
                        FileName = defaultName,
                        AddExtension = true,
                        InitialDirectory = Directory.Exists(this.AudioC.ExportPath) ? this.AudioC.ExportPath : Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)
                    };

                    if (saveDialog.ShowDialog(this) != DialogResult.OK)
                    {
                        return;
                    }

                    string finalFile = EnsureWavExtension(saveDialog.FileName);
                    string? directory = Path.GetDirectoryName(finalFile);
                    if (string.IsNullOrEmpty(directory))
                    {
                        MessageBox.Show(this, "Invalid file name.", "Export", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    Directory.CreateDirectory(directory);
                    await this.AudioC.Exporter.ExportWavAsync(audio, 24, directory, writeBpmTag: true, customFilePath: finalFile);
                    return;
                }

                if (ctrlDown && selectedAudios.Count != 1 && !shiftDown)
                {
                    // fall through to folder dialog below
                }

                string exportFolder = this.AudioC.ExportPath;
                Directory.CreateDirectory(exportFolder);

                bool ctrlDownOnly = ctrlDown && !shiftDown;
                bool ctrlShift = ctrlDown && shiftDown;

                if (ctrlDownOnly && selectedAudios.Count == 1)
                {
                    AudioObj audio = selectedAudios[0];
                    string defaultName = SanitizePathSegment(audio.Name) + ".wav";

                    using SaveFileDialog saveDialog = new()
                    {
                        Filter = "Wave Files (*.wav)|*.wav|All Files (*.*)|*.*",
                        DefaultExt = "wav",
                        FileName = defaultName,
                        AddExtension = true,
                        InitialDirectory = Directory.Exists(this.AudioC.ExportPath) ? this.AudioC.ExportPath : Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)
                    };

                    if (saveDialog.ShowDialog(this) != DialogResult.OK)
                    {
                        return;
                    }

                    string finalFile = EnsureWavExtension(saveDialog.FileName);
                    string? directory = Path.GetDirectoryName(finalFile);
                    if (string.IsNullOrEmpty(directory))
                    {
                        MessageBox.Show(this, "Invalid file name.", "Export", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    Directory.CreateDirectory(directory);
                    await this.AudioC.Exporter.ExportWavAsync(audio, 24, directory, writeBpmTag: true, customFilePath: finalFile);
                    return;
                }

                if ((ctrlDownOnly && selectedAudios.Count > 1) || ctrlShift)
                {
                    using FolderBrowserDialog folderDialog = new()
                    {
                        Description = "Select export folder",
                        SelectedPath = exportFolder,
                        ShowNewFolderButton = true,
                        AutoUpgradeEnabled = false
                    };

                    if (folderDialog.ShowDialog(this) != DialogResult.OK)
                    {
                        return;
                    }

                    exportFolder = folderDialog.SelectedPath;
                }
                else if (!ctrlDown && (selectedAudios.Count > 1 || shiftDown))
                {
                    string folderName = this.BuildExportFolderName(exportFolder);
                    exportFolder = Path.Combine(exportFolder, folderName);
                    Directory.CreateDirectory(exportFolder);
                }

                var tasks = selectedAudios.Select(a => this.AudioC.Exporter.ExportWavAsync(a, 24, exportFolder));
                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Export failed: " + ex.Message, "Export", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                this.button_export.Enabled = true;
            }
        }

        internal void RefreshList()
        {
            this.listBox_audios.Invalidate();
        }




        protected override void WndProc(ref Message m)
        {
            const int WM_NCLBUTTONDBLCLK = 0x00A3; // Non-client left button double-click
            if (m.Msg == WM_NCLBUTTONDBLCLK)
            {
                try
                {
                    // Dialog auf UI-Thread öffnen
                    this.BeginInvoke(new Action(() => this.ShowCollectionRenameDialog()));
                }
                catch { }
                // Standardverhalten (Maximieren) unterdrücken
                return;
            }

            base.WndProc(ref m);
        }

        private void ShowCollectionRenameDialog()
        {
            string current = this.Text ?? string.Empty;
            // Microsoft.VisualBasic.Interaction.InputBox wird bereits im Projekt genutzt
            string input = Microsoft.VisualBasic.Interaction.InputBox("Enter new name for this collection:", "Rename Collection", current);
            if (!string.IsNullOrWhiteSpace(input) && input != current)
            {
                this.Text = input;
            }
        }









        private sealed class AudioDragPayload
        {
            public AudioCollectionView SourceView { get; }
            public IReadOnlyList<AudioObj> Audios { get; }
            public AudioObj? PrimaryAudio => this.Audios.Count > 0 ? this.Audios[0] : null;

            public AudioDragPayload(AudioCollectionView sourceView, IReadOnlyList<AudioObj> audios)
            {
                this.SourceView = sourceView ?? throw new ArgumentNullException(nameof(sourceView));
                this.Audios = audios ?? throw new ArgumentNullException(nameof(audios));
            }
        }

        private void ListBox_audios_MouseMove_WaveformPreview(object? sender, MouseEventArgs e)
        {
            int idx = this.listBox_audios.IndexFromPoint(e.Location);
            if (idx != this.waveformPreviewIndex || e.Location != this.lastMousePos)
            {
                this.waveformPreviewTimer.Stop();
                this.HideWaveformPreview();
                this.waveformPreviewIndex = idx;
                this.lastMousePos = e.Location;
                if (idx >= 0 && idx < this.listBox_audios.Items.Count)
                {
                    this.waveformPreviewTimer.Start();
                }
            }
        }

        private void ListBox_audios_MouseLeave_WaveformPreview(object? sender, EventArgs e)
        {
            this.waveformPreviewTimer.Stop();
            this.HideWaveformPreview();
            this.waveformPreviewIndex = -1;
        }

        private void WaveformPreviewTimer_Tick(object? sender, EventArgs e)
        {
            this.waveformPreviewTimer.Stop();
            if (this.waveformPreviewIndex < 0 || this.waveformPreviewIndex >= this.listBox_audios.Items.Count)
            {
                return;
            }

            if (this.listBox_audios.Items[this.waveformPreviewIndex] is AudioObj audio)
            {
                // Vorschau nur anzeigen, wenn Audio < 20s
                if (audio.Duration.TotalSeconds > 20.0)
                {
                    return;
                }
                if (audio.WaveformPreview != null)
                {
                    if (this.waveformPreviewForm == null || this.waveformPreviewForm.IsDisposed)
                    {
                        this.waveformPreviewForm = new WaveformPreview();
                    }

                    Point screenPos = this.listBox_audios.PointToScreen(this.lastMousePos);
                    screenPos.Offset(20, 10); // etwas rechts/unten von der Maus
                    this.waveformPreviewForm.ShowWaveform(audio.WaveformPreview, screenPos);
                }
            }
        }

        private void HideWaveformPreview()
        {
            if (this.waveformPreviewForm != null && this.waveformPreviewForm.Visible)
            {
                this.waveformPreviewForm.Hide();
            }
        }
    }
}
