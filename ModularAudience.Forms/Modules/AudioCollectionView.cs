using ModularAudience.Audio;
using ModularAudience.Forms.Modules;
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


        public AudioCollectionView(IEnumerable<AudioObj> audios)
        {
            this.InitializeComponent();
            this.StartPosition = FormStartPosition.Manual;

            WindowMain.CollectionViews.Add(this);

            this.Text = "Audio Collection #" + (WindowMain.CollectionViews.Where(cv => !cv.IsDisposed).Count()).ToString("D2");

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
            this.listBox_audios.MouseClick += this.listBox_audios_MouseClick;
            this.listBox_audios.DoubleClick += this.listBox_audios_DoubleClick;
            this.listBox_audios.SelectedIndexChanged += this.listBox_audios_SelectedIndexChanged;
            this.checkBox_autoPlay.CheckedChanged += this.checkBox_autoPlay_CheckedChanged;
            this.DoubleClick += this.Form_DoubleClick;
            alarmclockkisser.DragNDrop.Forms.ListBoxExtensions.Register_ListBox_DragNDrop(this.listBox_audios, true);
            this.listBox_audios.DrawItem += this.listBox_audios_DrawItem;


            this.FormClosing += async (s, e) =>
            {
                e.Cancel = true;
                await this.CancelAutoPlayAsync(stopCollection: true).ConfigureAwait(false);
                this.Hide();
                this.AudioC.Dispose();

                WindowMain.CollectionViews.Remove(this);
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
                        {
                            requiredWidth = itemWidth;
                        }
                    }
                }
            }
            // Add scrollbar width if needed
            if (this.listBox_audios.Items.Count > this.listBox_audios.ClientSize.Height / this.listBox_audios.ItemHeight)
            {
                requiredWidth += SystemInformation.VerticalScrollBarWidth;
            }

            requiredWidth = Math.Min(Math.Max(requiredWidth + 40, minWidth), maxWidth);
            if (this.Width < requiredWidth)
            {
                this.Width = requiredWidth;
            }

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
                    string durationText = FormatDurationText(audio);
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

                                WindowMain.TrackViews.Where(tv => tv.OriginalAudio.Id == selectedAudio.Id).ToList().ForEach(tv => tv.Rename(input));
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
                    };
                    ToolStripMenuItem toNewCollectionItem = new("To new Collection");
                    toNewCollectionItem.Click += (s, ev) =>
                    {
                        List<AudioObj> toMove = this.listBox_audios.SelectedItems.Cast<AudioObj>().OfType<AudioObj>().ToList();
                        if (toMove.Count == 0 && index >= 0 && index < this.listBox_audios.Items.Count && this.listBox_audios.Items[index] is AudioObj fallback)
                        {
                            toMove.Add(fallback);
                        }
                        if (toMove.Count == 0)
                        {
                            return;
                        }
                        // Neue Collection erstellen und hinzufügen
                        var newView = new AudioCollectionView(toMove);
                        newView.Show();
                        // Aus aktueller Collection entfernen
                        foreach (var audio in toMove)
                        {
                            this.AudioC.Audios.Remove(audio);
                        }
                    };
                    contextMenu.Items.AddRange([renameItem, deleteItem, toNewCollectionItem]);
                    contextMenu.Show(this.listBox_audios, e.Location);
                }
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

        private async void listBox_audios_MouseClick(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || (Control.ModifierKeys & Keys.Control) != 0)
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

            WindowMain.CollectionViews.Where(cv => cv != this).ToList().ForEach(cv => cv.UnselectAll());
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
                    this.listBox_audios.EndUpdate();
                }
            }

            await this.AudioC.StopAllAsync();
        }


        private async void listBox_audios_SelectedIndexChanged(object? sender, EventArgs e)
        {

        }


        private async void checkBox_autoPlay_CheckedChanged(object? sender, EventArgs e)
        {
            if (!this.checkBox_autoPlay.Checked)
            {
                await this.CancelAutoPlayAsync(stopCollection: true).ConfigureAwait(false);
            }
        }


        private static string FormatDurationText(AudioObj audio)
        {
            TimeSpan duration = ResolveDuration(audio);
            if (duration.TotalMilliseconds > 0 && duration.TotalMilliseconds < 8000)
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

        private void AudioCollectionView_Resize(object? sender, EventArgs e)
        {
            this.AdjustLayout();
        }

        private void AdjustLayout()
        {
            // Anchor checkBox_autoPlay to top-right corner
            this.checkBox_autoPlay.Location = new Point(this.ClientSize.Width - this.checkBox_autoPlay.Width - 10, 10);

            // Button stays in its logical position (assuming it's already positioned)

            // ListBox: dock to top (below button), bottom to form, left and right to form
            int top = this.button_export.Bottom + 5;
            int bottom = this.ClientSize.Height - 10;
            int left = 10;
            int right = this.ClientSize.Width - 10;

            this.listBox_audios.Location = new Point(left, top);
            this.listBox_audios.Size = new Size(Math.Max(0, right - left), Math.Max(0, bottom - top));
        }



        protected override void WndProc(ref Message m)
        {
            const int WM_NCLBUTTONDBLCLK = 0x00A3; // Non-client left button double-click
            if (m.Msg == WM_NCLBUTTONDBLCLK)
            {
                try
                {
                    // Dialog auf UI-Thread öffnen
                    this.BeginInvoke(new Action(this.ShowCollectionRenameDialog));
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
            if (string.IsNullOrEmpty(input))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(input) && input != current)
            {
                this.Text = input;
            }
        }

        internal void Rename(string newName)
        {
            this.Text = newName;
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
