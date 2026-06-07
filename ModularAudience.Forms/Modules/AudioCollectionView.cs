using ModularAudience.Audio;
using ModularAudience.Audio.Processors_V1;
using ModularAudience.Audio.Processors_V4;
using ModularAudience.Generators;
using ModularAudience.Forms.Modules;
using ModularAudience.Forms.Modules.Dialogs;
using ModularAudience.Forms.Helpers;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace ModularAudience.Forms
{
    public partial class AudioCollectionView : Form
    {
        internal readonly AudioCollection AudioC = new();

        internal IEnumerable<AudioObj> SelectedAudios => this.listBox_audios.SelectedItems.Cast<AudioObj>().OfType<AudioObj>();

        private const double AutoPlayMaxSeconds = 10.0;
        private readonly Lock autoPlayGate = new();
        private CancellationTokenSource? autoPlayCts;
        private AudioObj? autoPlayCurrent;
        private readonly SemaphoreSlim autoPlayLock = new(1, 1);
        private static readonly HashSet<char> InvalidFileNameChars = [.. Path.GetInvalidFileNameChars()];

        public int AudioCount => this.AudioC.Audios.Count;

        private System.Windows.Forms.Timer waveformPreviewTimer;
        private int waveformPreviewIndex = -1;
        private Point lastMousePos;
        private WaveformPreview? waveformPreviewForm;
        private bool ShowPreview => this.checkBox_preview.Checked;

        private const int MaxAutoGrowHeight = 480;
        private int _autoGrowAnchorHeight;
        private int FormListBoxClearance { get; set; } = 5;
        private int _resizeStartHeight;
        private bool _isUserResizing;
        private int _resizeStartWidth;
        private bool _lastUserResizeWasHorizontal;

        private Point _dragStartPoint;
        private bool _dragPending;
        // Drag-drop visual insert indicator index (0..Count), -1 when not active
        private int _dragInsertIndex = -1;
        private AtomizeSensitivity atomizeSensitivity = AtomizeSensitivity.Balanced;
        private int atomizeMinSliceMs = 80;
        private int atomizeTailPaddingMs = 30;
        private float breakbeatBpm = 87.5f;
        private int breakbeatBars = 4;
        private int breakbeatHitsPerBar = 12;
        private float breakbeatDensity = 0.45f;
        private float breakbeatComplexity = 1.15f;
        private int breakbeatResolution = 16;
        private float breakbeatSwing = 0.06f;
        private bool isPinned;

        public AudioCollectionView(IEnumerable<AudioObj> audios)
        {
            this.InitializeComponent();
            this.StartPosition = FormStartPosition.Manual;

            this.SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            this.UpdateStyles();

            WindowMain.CollectionViews.Add(this);

            this.Text = "Audio Collection #" + (WindowMain.CollectionViews.Where(cv => !cv.IsDisposed).Count()).ToString("D2");

            foreach (AudioObj audio in audios)
            {
                this.AudioC.Audios.Add(audio);
            }

            this.listBox_audios.Items.Clear();
            this.listBox_audios.DataSource = this.AudioC.Audios;
            this.listBox_audios.DisplayMember = "Name";

            this.listBox_audios.IntegralHeight = false;
            this.listBox_audios.HorizontalScrollbar = false;

            this.AudioC.Audios.ListChanged += this.Resize_Form_CollectionChanged;
            this.Resize += this.AudioCollectionView_Resize;
            this.ResizeBegin += this.AudioCollectionView_ResizeBegin;
            this.ResizeEnd += this.AudioCollectionView_ResizeEnd;

            this.listBox_audios.SelectedIndex = -1;
            this.listBox_audios.AllowDrop = true;
            this.AllowDrop = true;
            this.listBox_audios.DrawMode = DrawMode.OwnerDrawFixed;
            this.listBox_audios.MouseDown += this.listBox_audios_MouseDown;
            this.listBox_audios.MouseMove += this.listBox_audios_MouseMove_DragStart;
            this.listBox_audios.MouseClick += this.listBox_audios_MouseClick;
            this.listBox_audios.DoubleClick += this.listBox_audios_DoubleClick;
            this.listBox_audios.SelectedIndexChanged += this.listBox_audios_SelectedIndexChanged;
            this.checkBox_autoPlay.CheckedChanged += this.checkBox_autoPlay_CheckedChanged;
            this.DoubleClick += this.Form_DoubleClick;
            alarmclockkisser.DragNDrop.Forms.ListBoxExtensions.Register_ListBox_DragNDrop(this.listBox_audios, true);
            this.listBox_audios.DrawItem += this.listBox_audios_DrawItem;


            // Explicit DnD handlers to support inter-collection drag/drop
            this.listBox_audios.DragEnter += this.listBox_audios_DragEnter;
            this.listBox_audios.DragOver += this.listBox_audios_DragOver;
            this.listBox_audios.DragDrop += this.listBox_audios_DragDrop;
            this.listBox_audios.DragLeave += this.listBox_audios_DragLeave;
            this.DragEnter += this.listBox_audios_DragEnter;
            this.DragOver += this.listBox_audios_DragOver;
            this.DragDrop += this.listBox_audios_DragDrop;
            this.DragLeave += this.listBox_audios_DragLeave;

            this.FormClosing += async (s, e) =>
            {
                e.Cancel = true;
                await this.CancelAutoPlayAsync(stopCollection: true).ConfigureAwait(false);
                this.Hide();
                this.AudioC.Dispose();

                WindowMain.CollectionViews.Remove(this);
                GC.SuppressFinalize(this);
            };

            this.waveformPreviewTimer = new System.Windows.Forms.Timer { Interval = 600 };
            this.waveformPreviewTimer.Tick += this.WaveformPreviewTimer_Tick;
            this.listBox_audios.MouseMove += this.ListBox_audios_MouseMove_WaveformPreview;
            this.listBox_audios.MouseLeave += this.ListBox_audios_MouseLeave_WaveformPreview;

            // Set minimum and maximum sizes
            this.MinimumSize = new Size(200, 100);
            this.MaximumSize = new Size(480, 8192);

            // Add resize event handler
            this.Resize += this.AudioCollectionView_Resize;

            // Initial layout
            this.AdjustLayout();
            this.Resize_Form_CollectionChanged(this, EventArgs.Empty);
            // this.UpdateWidthToFitContent();
            this._autoGrowAnchorHeight = this.Height - this.FormListBoxClearance;

            this.Show();
        }


        public void listBox_audios_DrawItem(object? sender, DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index >= 0 && e.Index < this.listBox_audios.Items.Count)
            {
                AudioObj? audio = this.listBox_audios.Items[e.Index] as AudioObj;
                if (audio != null)
                {
                    string nameText = audio.Name ?? string.Empty;
                    string durationText = AudioCollectionViewHelpers.FormatDurationText(audio);
                    string bpmText = audio.Bpm > 0 ? $"[{audio.Bpm:F1}]" : audio.ScannedBpm > 0 ? $"[{audio.ScannedBpm:F1}]" : "[ ? ]";
                    // Textfarben basierend auf Auswahlstatus
                    Color textColor = (e.State & DrawItemState.Selected) == DrawItemState.Selected
                        ? SystemColors.HighlightText
                        : this.listBox_audios.ForeColor;
                    // Zeichne Namen
                    Rectangle nameRect = new(e.Bounds.Left + 2, e.Bounds.Top, e.Bounds.Width - 100, e.Bounds.Height);
                    TextRenderer.DrawText(e.Graphics, nameText, e.Font, nameRect, textColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                    // Zeichne Dauer rechtsbündig
                    Rectangle durationRect = new(e.Bounds.Right - 98, e.Bounds.Top, 96, e.Bounds.Height);
                    TextRenderer.DrawText(e.Graphics, durationText, e.Font, durationRect, textColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
                }
            }

            // Draw insertion indicator line if dragging over and this item borders the insertion point
            if (this._dragInsertIndex >= 0)
            {
                try
                {
                    using var pen = new Pen(SystemColors.HotTrack, 2);
                    // Draw line above this item
                    if (this._dragInsertIndex == e.Index)
                    {
                        int y = e.Bounds.Top;
                        e.Graphics.DrawLine(pen, e.Bounds.Left + 2, y, e.Bounds.Right - 2, y);
                    }
                    // Draw line below last item (append case) or between this and next item
                    if ((e.Index == this.listBox_audios.Items.Count - 1 && this._dragInsertIndex == this.listBox_audios.Items.Count)
                        || this._dragInsertIndex == e.Index + 1)
                    {
                        int y = e.Bounds.Bottom - 1;
                        e.Graphics.DrawLine(pen, e.Bounds.Left + 2, y, e.Bounds.Right - 2, y);
                    }
                }
                catch { }
            }

            e.DrawFocusRectangle();
        }

        private void listBox_audios_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                int index = this.listBox_audios.IndexFromPoint(e.Location);
                if (index != ListBox.NoMatches)
                {
                    if (!this.listBox_audios.GetSelected(index))
                    {
                        this.listBox_audios.SelectedIndex = index;
                    }
                    this.UpdateContextMenuState();
                    this.contextMenuStrip_audios.Show(this.listBox_audios, e.Location);
                }
            }
            else if (e.Button == MouseButtons.Left)
            {
                // Prepare possible drag, but do not start yet (allows double-click to work)
                int index = this.listBox_audios.IndexFromPoint(e.Location);
                if (index != ListBox.NoMatches)
                {
                    if (!this.listBox_audios.GetSelected(index))
                    {
                        this.listBox_audios.SelectedIndex = index;
                    }
                    // mark drag as pending and store start point
                    this._dragPending = true;
                    this._dragStartPoint = e.Location;
                }
            }
        }

        private void listBox_audios_MouseMove_DragStart(object? sender, MouseEventArgs e)
        {
            if (!this._dragPending || e.Button != MouseButtons.Left)
            {
                return;
            }

            // Only start drag when movement exceeds system drag threshold
            int dx = Math.Abs(e.X - this._dragStartPoint.X);
            int dy = Math.Abs(e.Y - this._dragStartPoint.Y);
            int thresh = SystemInformation.DragSize.Width / 2; // conservative threshold
            if (dx < thresh && dy < thresh)
            {
                return;
            }

            this._dragPending = false; // avoid re-entry

            if (this.listBox_audios.SelectedItems.Count > 0)
            {
                try
                {
                    var selected = this.listBox_audios.SelectedItems;
                    var list = selected.Cast<AudioObj>().OfType<AudioObj>().ToList();

                    var dataObj = new DataObject();
                    dataObj.SetData(typeof(ListBox.SelectedObjectCollection), selected);
                    dataObj.SetData(typeof(List<AudioObj>), list);
                    dataObj.SetData(typeof(IEnumerable<AudioObj>), list);
                    dataObj.SetData(typeof(AudioObj[]), list.ToArray());
                    // include source for move semantics
                    dataObj.SetData(typeof(ListBox), this.listBox_audios);
                    var srcForm = this.FindForm() as AudioCollectionView;
                    if (srcForm != null)
                    {
                        dataObj.SetData(typeof(AudioCollection), srcForm.AudioC);
                    }

                    // Allow both internal move and external copy targets
                    this.listBox_audios.DoDragDrop(dataObj, DragDropEffects.Move | DragDropEffects.Copy);
                }
                catch { }
            }
        }

        private void listBox_audios_DragEnter(object? sender, DragEventArgs e)
        {
            if (e.Data == null)
            {
                e.Effect = DragDropEffects.None;
                return;
            }

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var items = e.Data.GetData(DataFormats.FileDrop) as string[] ?? [];
                if (items.Any(p => !string.IsNullOrWhiteSpace(p) &&
                    (Directory.Exists(p) || WindowMain.AllowedImportExtensions.Contains(Path.GetExtension(p)))))
                {
                    e.Effect = DragDropEffects.Copy;
                    return;
                }

                e.Effect = DragDropEffects.None;
                return;
            }

            bool hasAudio = e.Data.GetDataPresent(typeof(AudioObj[])) || e.Data.GetDataPresent(typeof(IEnumerable<AudioObj>));
            if (!hasAudio)
            {
                e.Effect = DragDropEffects.None;
                return;
            }

            // Default: Move within collections unless Ctrl is held -> Copy
            bool ctrl = (Control.ModifierKeys & Keys.Control) != 0;
            if (e.AllowedEffect.HasFlag(DragDropEffects.Move) && !ctrl)
            {
                e.Effect = DragDropEffects.Move;
            }
            else if (e.AllowedEffect.HasFlag(DragDropEffects.Copy))
            {
                e.Effect = DragDropEffects.Copy;
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
        }

        private void listBox_audios_DragOver(object? sender, DragEventArgs e)
        {
            // Keep effect updated while hovering
            this.listBox_audios_DragEnter(sender, e);

            if (e.Effect == DragDropEffects.None)
            {
                if (this._dragInsertIndex != -1)
                {
                    this._dragInsertIndex = -1;
                    this.listBox_audios.Invalidate();
                }
                return;
            }

            try
            {
                // Compute insertion index (between items) based on cursor position
                Point client = this.listBox_audios.PointToClient(new Point(e.X, e.Y));
                int index = this.listBox_audios.IndexFromPoint(client);
                int newInsertIndex;
                if (index == ListBox.NoMatches)
                {
                    newInsertIndex = this.listBox_audios.Items.Count; // append
                }
                else
                {
                    Rectangle itemRect = this.listBox_audios.GetItemRectangle(index);
                    bool before = client.Y < itemRect.Top + itemRect.Height / 2;
                    newInsertIndex = before ? index : index + 1;
                    newInsertIndex = Math.Clamp(newInsertIndex, 0, this.listBox_audios.Items.Count);
                }

                if (newInsertIndex != this._dragInsertIndex)
                {
                    this._dragInsertIndex = newInsertIndex;
                    this.listBox_audios.Invalidate();
                }
            }
            catch { }
        }

        private void listBox_audios_DragLeave(object? sender, EventArgs e)
        {
            // Clear insertion indicator
            if (this._dragInsertIndex != -1)
            {
                this._dragInsertIndex = -1;
                this.listBox_audios.Invalidate();
            }
        }

        private void listBox_audios_DragDrop(object? sender, DragEventArgs e)
        {
            if (e.Data == null)
            {
                return;
            }

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                this.HandleFileDrop(e);
                this._dragInsertIndex = -1;
                this.listBox_audios.Invalidate();
                return;
            }

            List<AudioObj> dragged = [];
            if (e.Data.GetDataPresent(typeof(AudioObj[])))
            {
                if (e.Data.GetData(typeof(AudioObj[])) is AudioObj[] arr && arr.Length > 0)
                {
                    dragged.AddRange(arr);
                }
            }
            else if (e.Data.GetDataPresent(typeof(IEnumerable<AudioObj>)))
            {
                if (e.Data.GetData(typeof(IEnumerable<AudioObj>)) is IEnumerable<AudioObj> en)
                {
                    dragged.AddRange(en);
                }
            }

            if (dragged.Count == 0)
            {
                this._dragInsertIndex = -1;
                this.listBox_audios.Invalidate();
                return;
            }

            // Determine drop index (prefer live indicator if available)
            int dropIndex = this._dragInsertIndex;
            if (dropIndex < 0)
            {
                Point clientPoint = this.listBox_audios.PointToClient(new Point(e.X, e.Y));
                int idx = this.listBox_audios.IndexFromPoint(clientPoint);
                dropIndex = (idx == ListBox.NoMatches) ? this.AudioC.Audios.Count : idx;
            }
            dropIndex = Math.Clamp(dropIndex, 0, this.AudioC.Audios.Count);

            // Source info (if available)
            AudioCollection? srcCollection = null;
            ListBox? srcListBox = null;
            if (e.Data.GetDataPresent(typeof(AudioCollection)))
            {
                srcCollection = e.Data.GetData(typeof(AudioCollection)) as AudioCollection;
            }
            if (e.Data.GetDataPresent(typeof(ListBox)))
            {
                srcListBox = e.Data.GetData(typeof(ListBox)) as ListBox;
            }

            bool sameList = srcListBox != null && ReferenceEquals(srcListBox, this.listBox_audios);
            bool move = e.Effect == DragDropEffects.Move;

            if (sameList)
            {
                // Reorder within same list
                foreach (var audio in dragged)
                {
                    int currentIndex = this.AudioC.Audios.IndexOf(audio);
                    if (currentIndex < 0)
                    {
                        continue;
                    }
                    if (dropIndex > currentIndex)
                    {
                        dropIndex--;
                    }
                    this.AudioC.Audios.RemoveAt(currentIndex);
                    dropIndex = Math.Clamp(dropIndex, 0, this.AudioC.Audios.Count);
                    this.AudioC.Audios.Insert(dropIndex, audio);
                    dropIndex++;
                }
            }
            else
            {
                foreach (var audio in dragged)
                {
                    if (move && srcCollection != null)
                    {
                        try
                        {
                            srcCollection.Audios.Remove(audio);
                        }
                        catch { }
                    }

                    dropIndex = Math.Clamp(dropIndex, 0, this.AudioC.Audios.Count);
                    this.AudioC.Audios.Insert(dropIndex, audio);
                    dropIndex++;
                }
            }

            try
            {
                this.listBox_audios.DataSource = null;
                this.listBox_audios.DataSource = this.AudioC.Audios;
                this.listBox_audios.DisplayMember = "Name";
            }
            catch { }
            finally
            {
                this._dragInsertIndex = -1;
                this.listBox_audios.Invalidate();
            }
        }

        private async void HandleFileDrop(DragEventArgs e)
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is not string[] dropped)
            {
                return;
            }

            var collectedPaths = new List<string>();
            foreach (var path in dropped.Where(p => !string.IsNullOrWhiteSpace(p)))
            {
                try
                {
                    if (Directory.Exists(path))
                    {
                        var found = Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories)
                            .Where(f => WindowMain.AllowedImportExtensions.Contains(Path.GetExtension(f)));
                        collectedPaths.AddRange(found);
                    }
                    else if (File.Exists(path) && WindowMain.AllowedImportExtensions.Contains(Path.GetExtension(path)))
                    {
                        collectedPaths.Add(path);
                    }
                }
                catch (Exception ex)
                {
                    LogCollection.Log($"DragDrop ACV: error scanning '{path}': {ex.Message}");
                }
            }

            var validPaths = collectedPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (validPaths.Count == 0)
            {
                LogCollection.Log("DragDrop ACV: No allowed audio files found in drop.");
                return;
            }

            try
            {
                if (WindowMain.Instance == null || WindowMain.Instance.IsDisposed)
                {
                    return;
                }

                var loaded = await WindowMain.Instance.AudioC.LoadManyAsync(validPaths);
                var importedAudios = loaded.Where(a => a != null).Cast<AudioObj>().ToList();
                int collectionNumber = WindowMain.GetCollectionNumber(this);
                foreach (var audio in importedAudios)
                {
                    this.AudioC.Audios.Add(audio);
                    WindowMain.AudioCollectionTags[audio.Id] = collectionNumber;
                    WindowMain.Instance.AudioC.Audios.Remove(audio);
                    LogCollection.Log($"{audio.Name} imported into {this.Text}.");
                }
            }
            catch (Exception ex)
            {
                LogCollection.Log($"DragDrop ACV import failed: {ex.Message}");
            }
        }

        private string BuildExportFolderName(string folderDirectory)
        {
            string baseName = AudioCollectionViewHelpers.SanitizePathSegment(this.Text);
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

        private async void listBox_audios_MouseClick(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || (ModifierKeys & Keys.Control) != 0)
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

            if (index != this.listBox_audios.SelectedIndex)
            {
                return;
            }

            if (this.listBox_audios.Items[index] is AudioObj audio)
            {
                if (this.checkBox_autoPlay.Checked)
                {
                    await this.TriggerAutoPlayAsync(audio).ConfigureAwait(false);
                }
            }

            WindowMainStaticHelpers.UnselectAll(WindowMain.CollectionViews, this);
        }

        internal void UnselectAll()
        {
            this.listBox_audios.Invoke(new Action(this.listBox_audios.ClearSelected));
        }

        private async void Form_DoubleClick(object? sender, EventArgs e)
        {
            await this.CancelAutoPlayAsync();
            // Clicked not on an item: Select all
            this.listBox_audios.BeginUpdate();
            try
            {
                if (this.listBox_audios.SelectedIndices.Count == this.listBox_audios.Items.Count)
                {
                    this.listBox_audios.ClearSelected();
                }
                else
                {
                    this.listBox_audios.ClearSelected();
                    for (int i = 0; i < this.listBox_audios.Items.Count; i++)
                    {
                        this.listBox_audios.SetSelected(i, true);
                    }
                }
            }
            finally
            {
                WindowMainStaticHelpers.UnselectAll(WindowMain.CollectionViews, this);
                this.listBox_audios.EndUpdate();
            }
        }

        private async void listBox_audios_DoubleClick(object? sender, EventArgs e)
        {
            // First set really selected  item to the one under the mouse cursor
            this.listBox_audios.SelectedIndex = this.listBox_audios.IndexFromPoint(this.listBox_audios.PointToClient(Cursor.Position));
            AudioObj? selectedAudio = (AudioObj?) this.listBox_audios.SelectedItem;
            if (selectedAudio != null)
            {
                var tv = new TrackView(selectedAudio);

            }
            else
            {
                // Clicked not on an item: Select all
                this.listBox_audios.BeginUpdate();
                try
                {
                    if (this.listBox_audios.SelectedIndices.Count == this.listBox_audios.Items.Count)
                    {
                        this.listBox_audios.ClearSelected();
                    }
                    else
                    {
                        this.listBox_audios.ClearSelected();
                        for (int i = 0; i < this.listBox_audios.Items.Count; i++)
                        {
                            this.listBox_audios.SetSelected(i, true);
                        }
                    }
                }
                finally
                {
                    WindowMainStaticHelpers.UnselectAll(WindowMain.CollectionViews, this);
                    this.listBox_audios.EndUpdate();
                }
            }

            await this.AudioC.StopAllAsync();
        }

        private void Resize_Form_CollectionChanged(object? sender, EventArgs e)
        {
            // Maximale Höhe des Formulars (inkl. Rahmen)
            int maxFormHeight = this.MaximumSize.Height;

            // Höhe der ListBox-Einträge
            int itemCount = this.listBox_audios.Items.Count;
            int itemHeight = this.listBox_audios.ItemHeight;

            // Anzahl zusätzlicher, frei sichtbarer Slots (1-2 gewünscht)
            const int ExtraVisibleSlots = 2;

            // Hier: Clearance mit einbeziehen (kann positiv oder negativ sein)
            int listBoxHeight = itemCount * itemHeight + ExtraVisibleSlots * itemHeight + this.FormListBoxClearance;

            // Zusätzliche Höhe für andere Controls und Padding
            int topPadding = this.button_export.Top;
            int controlsHeight = this.button_export.Height + 5; // Button + Abstand
            controlsHeight += 10; // Abstand unten
            controlsHeight += this.checkBox_autoPlay.Height + 10; // Checkbox + Abstand oben

            int totalHeight = topPadding + controlsHeight + listBoxHeight;

            // Begrenzen auf Maximum
            totalHeight = Math.Min(totalHeight, maxFormHeight);
            // mindestens MinimumHeight
            totalHeight = Math.Max(totalHeight, this.MinimumSize.Height);

            // Wenn Benutzer gerade manuell resized, NICHT durch BindingList das Height überschreiben.
            if (this._isUserResizing)
            {
                // Nur Layout anpassen (keine automatische Höhe setzen)
                this.AdjustLayout();
                return;
            }

            // Berechne erlaubte maximale Höhe durch Auto-Grow:
            // Erlaubt wird: _autoGrowAnchorHeight + FormListBoxClearance + MaxAutoGrowHeight
            // (_autoGrowAnchorHeight wurde im ctor initial gesetzt; FormListBoxClearance wird bei manuellen Resizes aktualisiert)
            int allowedAutoHeight = this._autoGrowAnchorHeight + this.FormListBoxClearance + MaxAutoGrowHeight;

            // Zielhöhe nie über allowedAutoHeight (automatisches Wachstum begrenzen),
            // aber wir erlauben Shrink (wenn totalHeight kleiner ist).
            int newHeight = Math.Min(totalHeight, allowedAutoHeight);

            // Setze neue Höhe (mindestens MinimumSize)
            this.Height = Math.Max(this.MinimumSize.Height, newHeight);

            // Layout ggf. anpassen
            this.AdjustLayout();

            // Hinweis: Wenn die Form durch den Benutzer später manuell verändert wird,
            // bleibt _autoGrowAnchorHeight unangetastet (die anschließenden FormListBoxClearance‑Änderungen
            // erhöhen automatisch die erlaubte Auto‑Grow‑Grenze).
        }

        private void UpdateWidthToFitContent()
        {
            int maxWidth = this.MaximumSize.Width;
            // Designer default / Sicherheits-Minimum
            int minWidth = Math.Max(200, this.Width);
            int requiredWidth = minWidth;

            try
            {
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
                            {
                                requiredWidth = itemWidth;
                            }
                        }
                    }
                }

                // Scrollbar berücksichtigen (wenn ListBox vertikal scrollt)
                int visibleRows = this.listBox_audios.ItemHeight > 0 ? (this.listBox_audios.ClientSize.Height / this.listBox_audios.ItemHeight) : 0;
                if (visibleRows > 0 && this.listBox_audios.Items.Count > visibleRows)
                {
                    requiredWidth += SystemInformation.VerticalScrollBarWidth;
                }

                requiredWidth = Math.Min(Math.Max(requiredWidth + 40, minWidth), maxWidth);

                // WICHTIG: nur automatisch VERGRÖSSERN, nicht wenn der Benutzer zuletzt horizontal resized hat
                if (!this._lastUserResizeWasHorizontal && !this._isUserResizing && this.Width < requiredWidth)
                {
                    this.Width = requiredWidth;
                }
            }
            catch
            {
                // still silently fail during init
            }
        }


        private void listBox_audios_SelectedIndexChanged(object? sender, EventArgs e)
        {
            try
            {
                AudioObj? selectedAudio = this.GetSingleContextAudio();
                if (WindowMain.Instance == null || WindowMain.Instance.IsDisposed)
                {
                    return;
                }

                WindowMainStaticHelpers.InvokeIfRequired(WindowMain.Instance, () =>
                {
                    if (selectedAudio != null)
                    {
                        WindowMain.Instance.UpdateInfoText(selectedAudio);
                        WindowMain.Instance.UpdateTrackDependentUI();
                    }
                    else if (WindowMain.LastSelectedTrackView == null)
                    {
                        WindowMain.Instance.UpdateInfoText(null);
                        WindowMain.Instance.UpdateTrackDependentUI();
                    }
                });
            }
            catch (Exception ex)
            {
                try { LogCollection.Log($"AudioCollectionView selection info update failed: {ex.Message}"); } catch { }
            }
        }

        private void contextMenuStrip_audios_Opening(object sender, CancelEventArgs e)
        {
            this.UpdateContextMenuState();
            e.Cancel = this.GetContextAudios().Count == 0;
        }

        private void UpdateContextMenuState()
        {
            int selectedCount = this.GetContextAudios().Count;
            bool hasAny = selectedCount > 0;
            bool hasSingle = selectedCount == 1;

            this.menuToolStripItem_rename.Enabled = hasSingle;
            this.menuToolStripItem_clone.Enabled = hasSingle;
            this.menuToolStripItem_editTags.Enabled = hasAny;
            this.menuToolStripItem_editTags.Text = selectedCount > 1 ? "Edit Tags (Many)" : "Edit Tags";
            this.menuToolStripItem_splitEqualParts.Enabled = hasSingle;
            this.menuToolStripItem_generateBreakbeat.Enabled = selectedCount > 1;
            this.menuToolStripItem_generateBreakbeatRun.Enabled = selectedCount > 1;
            this.menuToolStripItem_atomize.Enabled = hasSingle;
            this.menuToolStripItem_atomizeRun.Enabled = hasSingle;
            this.menuToolStripItem_delete.Enabled = hasAny;
            this.menuToolStripItem_toNewCollection.Enabled = hasAny;
            this.menuToolStripItem_addIndexToNames.Checked = this.AudioC.AddIndexToNames;
            this.menuToolStripItem_aggregateMixSelected.Enabled = selectedCount > 1;
            this.menuToolStripItem_timeStretchSelected.Enabled = hasAny;
            this.menuToolStripItem_demucsSeparateSelected.Enabled = hasSingle;

            this.menuToolStripItem_atomizeSensitivityConservative.Checked = this.atomizeSensitivity == AtomizeSensitivity.Conservative;
            this.menuToolStripItem_atomizeSensitivityBalanced.Checked = this.atomizeSensitivity == AtomizeSensitivity.Balanced;
            this.menuToolStripItem_atomizeSensitivityAggressive.Checked = this.atomizeSensitivity == AtomizeSensitivity.Aggressive;

            this.menuToolStripItem_atomizeMinSlice40.Checked = this.atomizeMinSliceMs == 40;
            this.menuToolStripItem_atomizeMinSlice80.Checked = this.atomizeMinSliceMs == 80;
            this.menuToolStripItem_atomizeMinSlice140.Checked = this.atomizeMinSliceMs == 140;

            this.menuToolStripItem_atomizeTail10.Checked = this.atomizeTailPaddingMs == 10;
            this.menuToolStripItem_atomizeTail30.Checked = this.atomizeTailPaddingMs == 30;
            this.menuToolStripItem_atomizeTail60.Checked = this.atomizeTailPaddingMs == 60;

            this.menuToolStripItem_generateBreakbeatBpm80.Checked = Math.Abs(this.breakbeatBpm - 80f) < 0.01f;
            this.menuToolStripItem_generateBreakbeatBpm875.Checked = Math.Abs(this.breakbeatBpm - 87.5f) < 0.01f;
            this.menuToolStripItem_generateBreakbeatBpm100.Checked = Math.Abs(this.breakbeatBpm - 100f) < 0.01f;
            this.menuToolStripItem_generateBreakbeatBpm120.Checked = Math.Abs(this.breakbeatBpm - 120f) < 0.01f;
            this.menuToolStripItem_generateBreakbeatBpm140.Checked = Math.Abs(this.breakbeatBpm - 140f) < 0.01f;

            this.menuToolStripItem_generateBreakbeatBars1.Checked = this.breakbeatBars == 1;
            this.menuToolStripItem_generateBreakbeatBars2.Checked = this.breakbeatBars == 2;
            this.menuToolStripItem_generateBreakbeatBars4.Checked = this.breakbeatBars == 4;
            this.menuToolStripItem_generateBreakbeatBars8.Checked = this.breakbeatBars == 8;

            this.menuToolStripItem_generateBreakbeatHits6.Checked = this.breakbeatHitsPerBar == 6;
            this.menuToolStripItem_generateBreakbeatHits8.Checked = this.breakbeatHitsPerBar == 8;
            this.menuToolStripItem_generateBreakbeatHits12.Checked = this.breakbeatHitsPerBar == 12;
            this.menuToolStripItem_generateBreakbeatHits16.Checked = this.breakbeatHitsPerBar == 16;
            this.menuToolStripItem_generateBreakbeatHits24.Checked = this.breakbeatHitsPerBar == 24;

            this.menuToolStripItem_generateBreakbeatDensitySparse.Checked = Math.Abs(this.breakbeatDensity - 0.28f) < 0.01f;
            this.menuToolStripItem_generateBreakbeatDensityBalanced.Checked = Math.Abs(this.breakbeatDensity - 0.45f) < 0.01f;
            this.menuToolStripItem_generateBreakbeatDensityDense.Checked = Math.Abs(this.breakbeatDensity - 0.62f) < 0.01f;
            this.menuToolStripItem_generateBreakbeatDensityMax.Checked = Math.Abs(this.breakbeatDensity - 0.82f) < 0.01f;

            this.menuToolStripItem_generateBreakbeatComplexityLow.Checked = Math.Abs(this.breakbeatComplexity - 0.75f) < 0.01f;
            this.menuToolStripItem_generateBreakbeatComplexityBalanced.Checked = Math.Abs(this.breakbeatComplexity - 1.15f) < 0.01f;
            this.menuToolStripItem_generateBreakbeatComplexityBusy.Checked = Math.Abs(this.breakbeatComplexity - 1.45f) < 0.01f;
            this.menuToolStripItem_generateBreakbeatComplexityWild.Checked = Math.Abs(this.breakbeatComplexity - 1.90f) < 0.01f;

            this.menuToolStripItem_generateBreakbeatResolution16.Checked = this.breakbeatResolution == 16;
            this.menuToolStripItem_generateBreakbeatResolution32.Checked = this.breakbeatResolution == 32;

            this.menuToolStripItem_generateBreakbeatSwing0.Checked = Math.Abs(this.breakbeatSwing) < 0.001f;
            this.menuToolStripItem_generateBreakbeatSwing6.Checked = Math.Abs(this.breakbeatSwing - 0.06f) < 0.001f;
            this.menuToolStripItem_generateBreakbeatSwing12.Checked = Math.Abs(this.breakbeatSwing - 0.12f) < 0.001f;
            this.menuToolStripItem_generateBreakbeatSwing18.Checked = Math.Abs(this.breakbeatSwing - 0.18f) < 0.001f;
        }

        private List<AudioObj> GetContextAudios()
        {
            List<AudioObj> selected = this.listBox_audios.SelectedItems.Cast<AudioObj>().OfType<AudioObj>().ToList();
            if (selected.Count == 0 && this.listBox_audios.SelectedItem is AudioObj audio)
            {
                selected.Add(audio);
            }

            return selected;
        }

        private AudioObj? GetSingleContextAudio()
        {
            List<AudioObj> selected = this.GetContextAudios();
            return selected.Count == 1 ? selected[0] : null;
        }

        private void ResetAudioListBinding()
        {
            this.listBox_audios.DataSource = null;
            this.listBox_audios.DataSource = this.AudioC.Audios;
            this.listBox_audios.DisplayMember = "Name";
        }

        private void menuToolStripItem_rename_Click(object sender, EventArgs e)
        {
            AudioObj? selectedAudio = this.GetSingleContextAudio();
            if (selectedAudio == null)
            {
                return;
            }

            string input = Microsoft.VisualBasic.Interaction.InputBox("Enter new name:", "Rename Audio", selectedAudio.Name);
            if (string.IsNullOrWhiteSpace(input))
            {
                return;
            }

            selectedAudio.Rename(input);
            this.listBox_audios.Refresh();
            WindowMain.TrackViews.Where(tv => tv.OriginalAudio.Id == selectedAudio.Id).ToList().ForEach(tv => tv.Rename(input));
        }

        private void menuToolStripItem_clone_Click(object sender, EventArgs e)
        {
            AudioObj? selectedAudio = this.GetSingleContextAudio();
            if (selectedAudio == null)
            {
                return;
            }

            AudioObj cloned = selectedAudio.Clone();
            cloned.Name = selectedAudio.Name + " (Clone)";
            this.AudioC.Audios.Add(cloned);
        }

        private void menuToolStripItem_editTags_Click(object sender, EventArgs e)
        {
            List<AudioObj> toEdit = this.GetContextAudios();
            if (toEdit.Count == 0)
            {
                return;
            }

            using TagEditorDialog tagEditor = new(toEdit);
            tagEditor.ShowDialog(this);
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



        private void ListBox_audios_MouseMove_WaveformPreview(object? sender, MouseEventArgs e)
        {
            int idx = this.listBox_audios.IndexFromPoint(e.Location);
            if (idx != this.waveformPreviewIndex)
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
            else
            {
                // Update position for preview placement, but don't restart timer
                this.lastMousePos = e.Location;
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
                // Vorschau nur anzeigen, wenn Audio < 60s
                if (audio.Duration.TotalSeconds > 60.0)
                {
                    return;
                }
                if (audio.WaveformPreview != null && this.ShowPreview)
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

        private void toolStripComboBox_orderBy_SelectedChanged(object? sender, EventArgs e)
        {
            this.ApplyOrderBySelection();
        }

        private void toolStripComboBox_orderBy_SelectedIndexChanged(object? sender, EventArgs e)
        {
            this.ApplyOrderBySelection();
        }

        private void ApplyOrderBySelection()
        {
            string? selected = this.toolStripComboBox_orderBy.SelectedItem as string;
            if (string.IsNullOrEmpty(selected) || this.AudioCount <= 0)
            {
                return;
            }

            // Order by duration (shortest first)
            if (selected == "Duration")
            {
                this.AudioC.Audios.SortInPlace(a => a.Duration);
            }
            // Order by creation date
            else if (selected == "Created At")
            {
                this.AudioC.Audios.SortInPlace(a => a.CreatedAt);
            }
            // Order by name (alphabetical)
            else if (selected == "Name")
            {
                this.AudioC.Audios.SortInPlace(a => a.Name);
            }
            else if (selected == "BPM")
            {
                this.AudioC.Audios.SortInPlace(a => -(a.Bpm > 0f ? a.Bpm : a.ScannedBpm));
            }

            // Jump back to none selected and text "Order by"
            this.toolStripComboBox_orderBy.SelectedIndex = -1;
            this.toolStripComboBox_orderBy.Text = "Order by";
        }
    }
}
