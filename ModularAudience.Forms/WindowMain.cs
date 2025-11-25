using ModularAudience.Audio;
using ModularAudience.Forms.Modules;
using ModularAudience.Core;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ModularAudience.Audio.Processors_V1;

namespace ModularAudience.Forms
{
    public partial class WindowMain : Form
    {
        public static WindowMain? Instance { get; private set; }

        public readonly AudioCollection AudioC = new();

        internal static readonly BindingList<AudioCollectionView> CollectionViews = [];
        internal static IEnumerable<AudioObj> SelectedTracks => CollectionViews.SelectMany(cv => cv.SelectedAudios);
        internal static IEnumerable<TrackView> PlayingTrackViews => TrackViews.Where(tv => tv.OriginalAudio.PlayerPlaying);
        internal static bool IsAnyTrackPlaying => PlayingTrackViews.Any();
        internal static IEnumerable<TrackView> SyncedTrackViews => TrackViews.Where(tv => tv.Synced);

        internal static readonly BindingList<TrackView> TrackViews = [];
        internal static List<int> TrackViewIds { get; set; } = [];
        private static TrackView? _lastSelectedTrackView = null;
        internal static TrackView? LastSelectedTrackView
        {
            get => _lastSelectedTrackView;
            set
            {
                if (!ReferenceEquals(_lastSelectedTrackView, value))
                {
                    _lastSelectedTrackView = value;
                    UpdateTrackDependentUI();
                    HighlightSelectedTrackView();
                }
            }
        }


        // Map AudioObj.Id -> Collection number (01-based) to restore distribution
        private static readonly Dictionary<Guid, int> AudioCollectionTags = [];
        private static readonly HashSet<string> AllowedImportExtensions = new(StringComparer.OrdinalIgnoreCase) { ".wav", ".mp3", ".flac" };
        private static readonly Random ResourceRandom = new();
        private static readonly Size CollectionCascadeOffset = new(26, 28);
        private const int CollectionBaseMargin = 5;
        private static readonly Padding TrackViewScreenMargin = new(20, 20, 20, 20);
        private static readonly Size TrackViewSpacing = new(15, 12);
        private bool suppressExportFormatEvent;

        // Recording timer
        private System.Windows.Forms.Timer? recordingTimer = null;
        private DateTime _infoCtrlToStopAppeared = DateTime.MinValue;

        internal static DrumRollEditor? DrumRoll { get; set; } = null;

        public WindowMain()
        {
            Instance = this;
            this.InitializeComponent();
            this.StartPosition = FormStartPosition.Manual;
            this.Location = WindowsScreenHelper.GetCornerPosition(this, false, true);

            TrackViews.ListChanged += this.TrackViews_ListChanged;
            CollectionViews.ListChanged += this.CollectionViews_ListChanged;

            this.FormClosing += this.WindowMain_FormClosing;
            this.checkBox_singleCollection.CheckedChanged += this.checkBox_singleCollection_CheckedChanged;
            this.LocationChanged += (_, __) => this.PositionCollectionViews();
            this.SizeChanged += (_, __) => this.PositionCollectionViews();

            this.InitializeExportControls();
            UpdateTrackDependentUI();
        }

        private async void WindowMain_FormClosing(object? sender, FormClosingEventArgs e)
        {
            // Kinder schliessen (deren FormClosing Handler lassen App-Exit jetzt durch)
            foreach (var cv in CollectionViews.ToList())
            {
                cv.Close();
            }
            foreach (var tv in TrackViews.ToList())
            {
                tv.Close();
            }

            await this.AudioC.ClearAsync();
            // Nicht abbrechen, echte Beendigung
            // e.Cancel bleibt false
            Application.ExitThread();
            Environment.Exit(0); // harte Prozessbeendigung falls noch Threads leben
        }

        private async void button_import_Click(object sender, EventArgs e)
        {
            IEnumerable<string> filesToImport = [];
            bool fromResources = false;

            if (ModifierKeys.HasFlag(Keys.Shift))
            {
                // FBD from Resources
                using FolderBrowserDialog folderBrowserDialog = new()
                {
                    Description = "Select Resource Folder to Import Audio Files From",
                    SelectedPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources"),
                    ShowNewFolderButton = false
                };

                if (folderBrowserDialog.ShowDialog() != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    filesToImport = Directory
                        .EnumerateFiles(folderBrowserDialog.SelectedPath, "*.*", SearchOption.AllDirectories)
                        .Where(f => AllowedImportExtensions.Contains(Path.GetExtension(f)));
                }
                catch (Exception ex)
                {
                    LogCollection.Log($"Failed to scan resources at '{folderBrowserDialog.SelectedPath}': {ex.Message}");
                    return;
                }
            }
            else if (ModifierKeys.HasFlag(Keys.Alt))
            {
                string? resourceFile = TryGetRandomResourceFile();
                if (resourceFile == null)
                {
                    LogCollection.Log("No resource audio files found for import.");
                    return;
                }
                filesToImport = [resourceFile];
                fromResources = true;
            }
            else
            {
                using OpenFileDialog openFileDialog = new()
                {
                    InitialDirectory = this.AudioC.ImportDirectory,
                    Filter = "Audio File|*.wav;*.mp3;*.flac",
                    Multiselect = true,
                    Title = "Import Audio Files / Loops",
                    RestoreDirectory = true
                };
                if (openFileDialog.ShowDialog() != DialogResult.OK)
                {
                    return;
                }
                filesToImport = openFileDialog.FileNames;
            }

            await this.ImportAndPlaceAsync(filesToImport, fromResources);
        }

        private async Task ImportAndPlaceAsync(IEnumerable<string> filePaths, bool fromResources)
        {
            // Laden und Liste neu importierter Audios bestimmen ohne sp�tere Dispose
            var validPaths = filePaths.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
            if (validPaths.Count == 0)
            {
                return;
            }

            // Laden
            var loaded = await this.AudioC.LoadManyAsync(validPaths);
            var importedAudios = loaded.Where(a => a != null).Cast<AudioObj>().ToList();
            if (importedAudios.Count == 0)
            {
                return;
            }

            // Logging nur f�r neu importierte
            foreach (var audio in importedAudios)
            {
                LogCollection.Log(fromResources ? $"{audio.Name} imported from resources." : $"{audio.Name} imported.");
            }

            // Single-Collection Modus: immer nur erste View pflegen
            if (this.checkBox_singleCollection.Checked)
            {
                var first = CollectionViews.FirstOrDefault();
                if (first == null)
                {
                    first = new AudioCollectionView(importedAudios);
                    CollectionViews.Add(first);
                }
                else
                {
                    foreach (var audio in importedAudios)
                    {
                        first.AudioC.Audios.Add(audio);
                    }
                }
                foreach (var audio in importedAudios)
                {
                    AudioCollectionTags[audio.Id] = 1;
                }
                first.Show();
                // Nur Referenzen aus tempor�rem Loader entfernen, nicht disposen
                foreach (var audio in importedAudios)
                {
                    this.AudioC.Audios.Remove(audio);
                }
                return;
            }

            var last = CollectionViews.LastOrDefault();
            if (last == null)
            {
                // Erste View erstellen
                var newView = new AudioCollectionView(importedAudios);
                CollectionViews.Add(newView);
                // Tags auf Nummer der neuen View setzen
                int num = GetCollectionNumber(newView);
                foreach (var audio in importedAudios)
                {
                    AudioCollectionTags[audio.Id] = num;
                }
                newView.Show();
            }
            else if (last.AudioCount == 0)
            {
                // In bestehende leere View importieren
                int num = GetCollectionNumber(last);
                foreach (var audio in importedAudios)
                {
                    last.AudioC.Audios.Add(audio);
                    AudioCollectionTags[audio.Id] = num;
                }
                last.Show();
            }
            else
            {
                // Neue View nur wenn letzte nicht leer ist
                var newView = new AudioCollectionView(importedAudios);
                CollectionViews.Add(newView);
                int num = GetCollectionNumber(newView);
                foreach (var audio in importedAudios)
                {
                    AudioCollectionTags[audio.Id] = num;
                }
                newView.Show();
            }

            // Entfernen aus tempor�rer Sammlung ohne Dispose (damit Views g�ltige Objekte behalten)
            foreach (var audio in importedAudios)
            {
                this.AudioC.Audios.Remove(audio);
            }

            // Positioning happens per-view on add; keep global layout untouched here.
        }

        private void InitializeExportControls()
        {
            var orderedFormats = AudioExporter.AvailableExportFormats.Keys
                .OrderBy(f => f.Equals(".wav", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            this.comboBox_exportFormat.BeginUpdate();
            this.comboBox_exportFormat.Items.Clear();
            foreach (var format in orderedFormats)
            {
                this.comboBox_exportFormat.Items.Add(format);
            }
            this.comboBox_exportFormat.EndUpdate();

            if (orderedFormats.Count == 0)
            {
                this.comboBox_exportFormat.SelectedIndex = -1;
                this.comboBox_exportBits.Items.Clear();
                this.comboBox_exportBits.SelectedIndex = -1;
                return;
            }

            string defaultFormat = orderedFormats.FirstOrDefault(f => f.Equals(".wav", StringComparison.OrdinalIgnoreCase)) ?? orderedFormats[0];

            this.suppressExportFormatEvent = true;
            this.comboBox_exportFormat.SelectedItem = defaultFormat;
            this.suppressExportFormatEvent = false;

            this.UpdateExportBitOptions(selectMiddleOnChange: true);
        }

        private void UpdateExportBitOptions(bool selectMiddleOnChange = false)
        {
            if (AudioExporter.AvailableExportFormats.Count == 0)
            {
                this.comboBox_exportBits.Items.Clear();
                this.comboBox_exportBits.SelectedIndex = -1;
                return;
            }

            string? selectedFormat = this.comboBox_exportFormat.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(selectedFormat) || !AudioExporter.AvailableExportFormats.ContainsKey(selectedFormat))
            {
                string fallback = AudioExporter.AvailableExportFormats.Keys.First();
                this.suppressExportFormatEvent = true;
                this.comboBox_exportFormat.SelectedItem = fallback;
                this.suppressExportFormatEvent = false;
                selectedFormat = fallback;
            }

            if (!AudioExporter.AvailableExportFormats.TryGetValue(selectedFormat!, out var bitOptions) || bitOptions.Length == 0)
            {
                this.comboBox_exportBits.Items.Clear();
                this.comboBox_exportBits.SelectedIndex = -1;
                return;
            }

            int? preferredBit = null;
            int middleIndex = Math.Clamp(bitOptions.Length / 2, 0, bitOptions.Length - 1);
            int middleBit = bitOptions[middleIndex];
            if (!selectMiddleOnChange && this.comboBox_exportBits.SelectedItem is int existing && bitOptions.Contains(existing))
            {
                preferredBit = existing;
            }
            else
            {
                preferredBit = middleBit;
            }

            this.comboBox_exportBits.BeginUpdate();
            this.comboBox_exportBits.Items.Clear();
            this.comboBox_exportBits.Items.AddRange(bitOptions.Cast<object>().ToArray());
            this.comboBox_exportBits.EndUpdate();

            if (preferredBit.HasValue)
            {
                this.comboBox_exportBits.SelectedItem = preferredBit.Value;
            }
        }

        private void button_browse_Click(object sender, EventArgs e)
        {
            string workingDir = this.AudioC.WorkingDirectory;
            if (!Directory.Exists(workingDir))
            {
                // FBD to select working directory
                using FolderBrowserDialog folderBrowserDialog = new()
                {
                    Description = "Select Working Directory for Audio Files",
                    SelectedPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    ShowNewFolderButton = true
                };
                if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
                {
                    workingDir = folderBrowserDialog.SelectedPath;
                    this.AudioC.WorkingDirectory = workingDir;
                }
            }
            else
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
                {
                    FileName = workingDir,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
        }

        // Event for TrackViews ListChanged to arrange TrackView windows in a grid
        private void TrackViews_ListChanged(object? sender, ListChangedEventArgs e)
        {
            if (e.ListChangedType == ListChangedType.ItemAdded)
            {
                this.ReflowTrackViews();
                TrackView newTrackView = TrackViews[e.NewIndex];
                newTrackView.Show();
            }
            else if (e.ListChangedType == ListChangedType.ItemDeleted || e.ListChangedType == ListChangedType.Reset)
            {
                this.ReflowTrackViews();
            }
        }

        private void ReflowTrackViews()
        {
            if (TrackViews.Count == 0)
            {
                return;
            }

            Rectangle workingArea = Screen.FromControl(this).WorkingArea;
            int currentX = workingArea.Left;
            int currentY = workingArea.Top;
            int columnWidth = 0;
            int maxRight = workingArea.Right - TrackViewScreenMargin.Right;
            int maxBottom = workingArea.Bottom - TrackViewScreenMargin.Bottom;

            bool isFirst = true;
            foreach (var view in TrackViews.Where(tv => tv != null && !tv.IsDisposed))
            {
                if (view == null || view.IsDisposed)
                {
                    continue;
                }

                view.StartPosition = FormStartPosition.Manual;

                view.Location = new Point(currentX, currentY);

                columnWidth = Math.Max(columnWidth, view.Width);

                if (isFirst)
                {
                    // Nach der ersten View direkt darunter ansetzen, ohne zusätzlichen Margin
                    currentY += view.Height + TrackViewSpacing.Height;
                    isFirst = false;
                }
                else
                {
                    currentY += view.Height + TrackViewSpacing.Height;
                }

                bool exceedsBottom = currentY + view.Height > maxBottom;
                if (exceedsBottom)
                {
                    currentX += columnWidth + TrackViewSpacing.Width;
                    currentY = workingArea.Top;
                    columnWidth = 0;

                    if (currentX + view.Width > maxRight)
                    {
                        currentX = workingArea.Left;
                    }
                }
            }
        }

        private void CollectionViews_ListChanged(object? sender, ListChangedEventArgs e)
        {
            if (e.ListChangedType == ListChangedType.ItemAdded)
            {
                var addedView = CollectionViews[e.NewIndex];
                this.PositionCollectionView(addedView);
            }
            else
            {
                // this.PositionCollectionViews();
            }
        }

        private void checkBox_singleCollection_CheckedChanged(object? sender, EventArgs e)
        {
            if (this.checkBox_singleCollection.Checked)
            {
                this.MergeCollectionsToSingle();
            }
            else
            {
                this.RebuildCollectionsFromTags();
            }
        }

        private static int GetCollectionNumber(AudioCollectionView view)
        {
            try
            {
                var text = view.Text;
                int idx = text.LastIndexOf('#');
                if (idx >= 0 && idx + 3 <= text.Length)
                {
                    var numStr = text.Substring(idx + 1, 2);
                    if (int.TryParse(numStr, out int num))
                    {
                        return num;
                    }
                }
            }
            catch { }
            // Fallback: 1-basierter Index
            int index = CollectionViews.ToList().IndexOf(view);
            return index >= 0 ? index + 1 : 1;
        }

        private void MergeCollectionsToSingle()
        {
            if (CollectionViews.Count == 0)
            {
                return;
            }

            // Alle aktuellen Tags nach Nummer des Views setzen
            foreach (var cv in CollectionViews)
            {
                int num = GetCollectionNumber(cv);
                foreach (var audio in cv.AudioC.Audios.ToList())
                {
                    AudioCollectionTags[audio.Id] = num;
                }
            }

            var baseView = CollectionViews[0];

            for (int i = 1; i < CollectionViews.Count; i++)
            {
                var cv = CollectionViews[i];
                var toMove = cv.AudioC.Audios.ToList();
                foreach (var audio in toMove)
                {
                    baseView.AudioC.Audios.Add(audio);
                }
                cv.AudioC.Audios.Clear();
                cv.Hide();
            }

            baseView.Show();
            this.PositionCollectionViews();
        }

        private void RebuildCollectionsFromTags()
        {
            if (CollectionViews.Count == 0)
            {
                return;
            }

            // Alle Audios einsammeln
            var allAudios = new List<AudioObj>();
            foreach (var cv in CollectionViews)
            {
                allAudios.AddRange(cv.AudioC.Audios.ToList());
            }

            // Max Nummer bestimmen
            int maxNum = AudioCollectionTags.Count > 0 ? AudioCollectionTags.Values.Max() : 1;
            if (maxNum < 1)
            {
                maxNum = 1;
            }

            // Genug Views bereitstellen
            while (CollectionViews.Count < maxNum)
            {
                var emptyView = new AudioCollectionView([]);
                CollectionViews.Add(emptyView);
            }

            // Alle leeren und ausblenden
            foreach (var cv in CollectionViews)
            {
                cv.AudioC.Audios.Clear();
                cv.Hide();
            }

            // Verteilen gem�� Tags
            foreach (var audio in allAudios)
            {
                int num = 1;
                if (!AudioCollectionTags.TryGetValue(audio.Id, out num))
                {
                    num = 1;
                }
                int index = Math.Clamp(num - 1, 0, CollectionViews.Count - 1);
                CollectionViews[index].AudioC.Audios.Add(audio);
            }

            // Anzeigen der Views mit Inhalt
            foreach (var cv in CollectionViews)
            {
                if (cv.AudioC.Audios.Count > 0)
                {
                    cv.Show();
                }
            }

            this.PositionCollectionViews();
        }

        private void PositionCollectionViews()
        {
            if (CollectionViews.Count == 0)
            {
                return;
            }

            Point basePoint = new(this.Location.X, this.Location.Y + this.Height + CollectionBaseMargin);
            for (int i = 0; i < CollectionViews.Count; i++)
            {
                var view = CollectionViews[i];
                if (view == null || view.IsDisposed)
                {
                    continue;
                }

                view.StartPosition = FormStartPosition.Manual;
                var offset = new Point(CollectionCascadeOffset.Width * i, CollectionCascadeOffset.Height * i);
                var location = new Point(basePoint.X + offset.X, basePoint.Y + offset.Y);
				view.Invoke(() =>
				{
                    view.Location = location;
				});
			}
        }

        private void PositionCollectionView(AudioCollectionView? view)
        {
            if (view == null || view.IsDisposed)
            {
                return;
            }

            Rectangle workingArea = Screen.FromControl(this).WorkingArea;
            AudioCollectionView? anchor = null;
            foreach (var existing in CollectionViews)
            {
                if (existing == view)
                {
                    break;
                }
                if (existing != null && !existing.IsDisposed)
                {
                    anchor = existing;
                }
            }

            Point basePoint = new(this.Location.X, this.Location.Y + this.Height + CollectionBaseMargin);
            Point targetLocation;
            if (anchor == null)
            {
                targetLocation = basePoint;
            }
            else
            {
                targetLocation = new Point(anchor.Location.X + CollectionCascadeOffset.Width, anchor.Location.Y + CollectionCascadeOffset.Height);
                bool exceedsBottom = targetLocation.Y + view.Height > workingArea.Bottom;
                if (exceedsBottom)
                {
                    targetLocation = new Point(anchor.Location.X + anchor.Width + CollectionCascadeOffset.Width, basePoint.Y);
                }

                bool exceedsRight = targetLocation.X + view.Width > workingArea.Right;
                if (exceedsRight)
                {
                    targetLocation = basePoint;
                }
            }

            view.StartPosition = FormStartPosition.Manual;
            view.Location = targetLocation;
        }

        internal static void UpdateCollectionTag(AudioObj audio, AudioCollectionView targetView)
        {
            if (audio == null || targetView == null)
            {
                return;
            }

            int num = GetCollectionNumber(targetView);
            AudioCollectionTags[audio.Id] = num;
        }

        private static void UpdateTrackDependentUI()
        {
            if (Instance == null)
            {
                return;
            }

            // Buttons aktivieren/deaktivieren
            Instance.button_scanBpm.Enabled = LastSelectedTrackView != null;
            Instance.button_scanTiming.Enabled = LastSelectedTrackView != null;
            Instance.button_scanKey.Enabled = LastSelectedTrackView != null;
            Instance.button_timeStretch.Enabled = LastSelectedTrackView != null;
            Instance.button_export.Enabled = LastSelectedTrackView != null;
            Instance.comboBox_exportFormat.Enabled = LastSelectedTrackView != null;
            Instance.comboBox_exportBits.Enabled = LastSelectedTrackView != null;
            Instance.button_autoSamples.Enabled = LastSelectedTrackView != null;
            Instance.textBox_info.Text = LastSelectedTrackView != null ? LastSelectedTrackView.OriginalAudio.GetInfoString() : "";

            // Set scanned values to textboxes
            if (LastSelectedTrackView != null)
            {
                Instance.textBox_scanBpmResult.Text = LastSelectedTrackView.OriginalAudio.ScannedBpm > 0 ? $"{LastSelectedTrackView.OriginalAudio.ScannedBpm:F3} BPM" : "";
                Instance.textBox_scanTimingResult.Text = LastSelectedTrackView.OriginalAudio.ScannedTiming > 0.0f ? GetTimingString(LastSelectedTrackView.OriginalAudio.ScannedTiming) : "";
                Instance.textBox_scanKeyResult.Text = !string.IsNullOrEmpty(LastSelectedTrackView.OriginalAudio.ScannedKey) ? LastSelectedTrackView.OriginalAudio.ScannedKey : "";
            }
            else
            {
                Instance.textBox_scanBpmResult.Text = "";
                Instance.textBox_scanTimingResult.Text = "";
                Instance.textBox_scanKeyResult.Text = "";
            }
        }

        private static string? TryGetRandomResourceFile()
        {
            var candidates = new List<string>();
            foreach (var root in EnumerateResourceRoots())
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                try
                {
                    candidates.AddRange(Directory
                        .EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                        .Where(f => AllowedImportExtensions.Contains(Path.GetExtension(f))));
                }
                catch (Exception ex)
                {
                    LogCollection.Log($"Failed to scan resources at '{root}': {ex.Message}");
                }
            }

            if (candidates.Count == 0)
            {
                return null;
            }

            lock (ResourceRandom)
            {
                return candidates[ResourceRandom.Next(candidates.Count)];
            }
        }

        private static IEnumerable<string> EnumerateResourceRoots()
        {
            DirectoryInfo? current = new(AppDomain.CurrentDomain.BaseDirectory);
            var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (current != null)
            {
                string candidate = Path.Combine(current.FullName, "Resources");
                if (yielded.Add(candidate))
                {
                    yield return candidate;
                }
                current = current.Parent;
            }
        }

        private async void button_scanBpm_Click(object sender, EventArgs e)
        {
            if (LastSelectedTrackView == null)
            {
                return;
            }

            double scannedBpm = await BeatScanner.ScanBpmAsync(LastSelectedTrackView.OriginalAudio);

            this.textBox_scanBpmResult.Text = scannedBpm.ToString("F3") + " BPM";
            LastSelectedTrackView.OriginalAudio.ScannedBpm = (float) scannedBpm;
        }

        private async void button_scanTiming_Click(object sender, EventArgs e)
        {
            if (LastSelectedTrackView == null)
            {
                return;
            }

            float scannedTiming = await BeatScanner_V2.ScanTimingAsync(LastSelectedTrackView.OriginalAudio);

            this.textBox_scanTimingResult.Text = GetTimingString(scannedTiming);
            LastSelectedTrackView.OriginalAudio.ScannedTiming = scannedTiming;
        }

        private async void button_scanKey_Click(object sender, EventArgs e)
        {
            if (LastSelectedTrackView == null)
            {
                return;
            }

            string scannedKey = await BeatScanner_V2.ScanKeyAsync(LastSelectedTrackView.OriginalAudio);

            this.textBox_scanKeyResult.Text = scannedKey;
            LastSelectedTrackView.OriginalAudio.ScannedKey = scannedKey;
        }

        public static string GetTimingString(float timing)
        {
            if (timing <= 0f)
            {
                return "-";
            }

            // Standard: 4/4 als Basis
            // timing = Bruchteil eines Takts (z.B. 0.25 = Viertel, 0.5 = Halbe, 1.0 = Ganze, 0.75 = punktierte Halbe, etc.)
            // Wir suchen Nenner als Potenz von 2 (1, 2, 4, 8, 16, ...)
            int maxDenominator = 64; // bis 1/64-Noten
            for (int denom = 1; denom <= maxDenominator; denom *= 2)
            {
                float num = timing * denom * 4; // 4/4 als Basis
                if (Math.Abs(num - MathF.Round(num)) < 0.0001f)
                {
                    int numerator = (int) MathF.Round(num);
                    int denominator = denom * 4;
                    return $"{numerator} / {denominator}";
                }
            }
            // Fallback: Dezimalwert
            return timing.ToString("0.###");
        }

        private void button_timeStretch_Click(object sender, EventArgs e)
        {
            if (LastSelectedTrackView == null || LastSelectedTrackView.IsDisposed)
            {
                MessageBox.Show(this, "No track selected.", "Time Stretch", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using var dlg = new Modules.Dialogs.TimeStretchDialog(LastSelectedTrackView);
            dlg.ShowDialog(this);
        }

        private async void button_export_Click(object sender, EventArgs e)
        {
            if (LastSelectedTrackView == null || LastSelectedTrackView.IsDisposed)
            {
                MessageBox.Show(this, "No track selected.", "Export Audio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            bool ctrlFlag = ModifierKeys.HasFlag(Keys.Control);
            string formatKey = NormalizeFormatExtension(this.comboBox_exportFormat.SelectedItem as string);
            string normalizedFormat = formatKey.TrimStart('.');
            int bits = ResolveBitSelection(formatKey, this.comboBox_exportBits.SelectedItem);

            string exportFilePath = this.AudioC.ExportPath;
            if (ctrlFlag)
            {
                SaveFileDialog saveFileDialog = new()
                {
                    Filter = $"{normalizedFormat.ToUpperInvariant()} files|*{formatKey}",
                    FileName = Path.GetFileName(exportFilePath),
                    InitialDirectory = Path.GetDirectoryName(exportFilePath) ?? this.AudioC.ExportPath,
                    OverwritePrompt = true,
                    Title = "Select Export File Location",
                    DefaultExt = normalizedFormat
                };

                if (saveFileDialog.ShowDialog() == DialogResult.OK)
                {
                    exportFilePath = saveFileDialog.FileName;
                }
                else
                {
                    return;
                }
            }

            string? resultPath;
            if (normalizedFormat.Equals("mp3", StringComparison.OrdinalIgnoreCase))
            {
                resultPath = await this.AudioC.Exporter.ExportMp3Async(LastSelectedTrackView.OriginalAudio, bits, Environment.ProcessorCount - 1, exportFilePath);
            }
            else
            {
                resultPath = await this.AudioC.Exporter.ExportWavAsync(LastSelectedTrackView.OriginalAudio, bits, exportFilePath);
            }

            if (string.IsNullOrEmpty(resultPath))
            {
                MessageBox.Show(this, "Export failed.", "Export Audio", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            else
            {
                MessageBox.Show(this, $"Exported to:\n{resultPath}", "Export Audio", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private static string NormalizeFormatExtension(string? formatCandidate)
        {
            if (string.IsNullOrWhiteSpace(formatCandidate))
            {
                return ".wav";
            }

            string normalized = formatCandidate.Trim();
            if (!normalized.StartsWith(".", StringComparison.Ordinal))
            {
                normalized = "." + normalized;
            }

            return normalized.ToLowerInvariant();
        }

        private static int ResolveBitSelection(string formatKey, object? selectedBit)
        {
            if (selectedBit is int bitValue)
            {
                return bitValue;
            }

            if (AudioExporter.AvailableExportFormats.TryGetValue(formatKey, out var bits) && bits.Length > 0)
            {
                if (formatKey.Equals(".wav", StringComparison.OrdinalIgnoreCase) && bits.Contains(24))
                {
                    return 24;
                }

                return bits[0];
            }

            var fallback = AudioExporter.AvailableExportFormats.FirstOrDefault();
            if (fallback.Value != null && fallback.Value.Length > 0)
            {
                return fallback.Value[0];
            }

            return 16;
        }

        private void comboBox_exportFormat_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (this.suppressExportFormatEvent)
            {
                return;
            }

            this.UpdateExportBitOptions(selectMiddleOnChange: true);
        }

        // Fügt eine Hilfsmethode zum sicheren Ausführen von Aktionen im UI-Thread hinzu
        internal static void InvokeIfRequired(Action action)
        {
            if (Instance == null || Instance.IsDisposed)
            {
                return;
            }

            if (Instance.InvokeRequired)
            {
                try { Instance.BeginInvoke(action); } catch { }
            }
            else
            {
                try { action(); } catch { }
            }
        }

        private void button_autoSamples_Click(object sender, EventArgs e)
        {
            if (LastSelectedTrackView == null || LastSelectedTrackView.IsDisposed)
            {
                MessageBox.Show(this, "No track selected.", "Auto Samples", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using var dlg = new Modules.Dialogs.AutoSamplesDialog(LastSelectedTrackView.OriginalAudio);
            dlg.ShowDialog(this);

            if (dlg.DialogResult != DialogResult.OK || dlg.ResultSamples.Count == 0)
            {
                return;
            }

            var samples = dlg.ResultSamples.ToList();
            AudioCollectionView collection = new(samples);
            CollectionViews.Add(collection);
            int num = GetCollectionNumber(collection);
            foreach (var audio in samples)
            {
                AudioCollectionTags[audio.Id] = num;
            }
            collection.Show();
        }

        private void button_newBag_Click(object sender, EventArgs e)
        {
            AudioCollectionView collection = new([]);
            CollectionViews.Add(collection);
            collection.Show();
        }

        private void button_drumRoll_Click(object sender, EventArgs e)
        {
            if (DrumRoll != null && !DrumRoll.IsDisposed)
            {
                DrumRoll.BringToFront();
                return;
            }

            DrumRoll = new(SelectedTracks);
            DrumRoll.Show();
        }




        private static void HighlightSelectedTrackView()
        {
            LastSelectedTrackView?.HighlightBorder();
            foreach (var tv in TrackViews)
            {
                if (tv != LastSelectedTrackView)
                {
                    tv.NormalightBorder();
                }
            }
        }


        internal static void RefreshAllCollectionViews()
        {
            foreach (var cv in CollectionViews)
            {
                cv.RefreshList();
            }
        }

        private async void button_record_Click(object sender, EventArgs e)
        {
            try
            {
                if (!AudioRecorder.IsRecording)
                {
                    // Timer anlegen, Event anhängen und starten
                    this.recordingTimer = new System.Windows.Forms.Timer { Interval = 500 };
                    this.recordingTimer.Tick += async (s, ev) => await this.RecordingTimer_TickAsync();
                    this.recordingTimer.Start();

                    // Sicherstellen, dass Aufnahme-Ordner existiert
                    string recordDir = this.AudioC.RecordPath;
                    try { System.IO.Directory.CreateDirectory(recordDir); } catch { }

                    // Aufnahme-Dateiname mit .wav-Endung
                    string fileName = "Recording" + DateTime.Now.ToString("_yyyyMMdd_HHmmss") + ".wav";
                    string fullPath = System.IO.Path.Combine(recordDir, fileName);

                    // Aufnahme starten (Dateiname mit Timestamp + .wav)
                    await AudioRecorder.StartRecording(fullPath);

                    // UI-Status
                    this.button_record.ForeColor = Color.Red;
                    this.label_stopRecordInfo.Visible = false;
                    this._infoCtrlToStopAppeared = DateTime.MinValue;
                    this.button_record.Enabled = true; // sicher stellen, dass Button aktiv bleibt beim Aufnehmen
                }
                else
                {
                    // Wenn kein Ctrl gedrückt ist: nur Info anzeigen (4s), Aufnahme läuft weiter
                    if (!ModifierKeys.HasFlag(Keys.Control))
                    {
                        this.label_stopRecordInfo.Visible = true;
                        this._infoCtrlToStopAppeared = DateTime.Now;
                    }
                    else
                    {
                        // Ctrl gedrückt: Aufnahme beenden, Button sofort deaktivieren bis Recorder wirklich gestoppt ist
                        this.button_record.Enabled = false;
                        AudioRecorder.StopRecording(normalizeOutput: true);

                        // Info kurz zeigen bis Timer das Ende erkennt
                        this.label_stopRecordInfo.Visible = true;
                        this._infoCtrlToStopAppeared = DateTime.Now;
                    }
                }
            }
            catch (Exception ex)
            {
                LogCollection.Log($"Recording button error: {ex.Message}");
                try { this.recordingTimer?.Stop(); this.recordingTimer?.Dispose(); } catch { }
                this.recordingTimer = null;
                this.button_record.ForeColor = Color.Black;
                this.label_stopRecordInfo.Visible = false;
                this.button_record.Enabled = true;
            }
        }

        private async Task RecordingTimer_TickAsync()
        {
            // Verbergen des Info-Labels nach 4s (wenn angezeigt)
            if (this.label_stopRecordInfo.Visible && this._infoCtrlToStopAppeared != DateTime.MinValue)
            {
                TimeSpan elapsedSinceInfoShown = DateTime.Now - this._infoCtrlToStopAppeared;
                if (elapsedSinceInfoShown.TotalSeconds >= 4)
                {
                    this.label_stopRecordInfo.Visible = false;
                    this._infoCtrlToStopAppeared = DateTime.MinValue;
                }
            }

            // Anzeige der Laufzeit aktualisieren
            if (AudioRecorder.IsRecording && AudioRecorder.RecordingTime.HasValue)
            {
                this.textBox_recordingTime.Text = AudioRecorder.RecordingTime.Value.ToString(@"hh\:mm\:ss");
            }
            else
            {
                // Wenn Aufnahme nicht mehr läuft, UI aufräumen und Timer stoppen
                this.textBox_recordingTime.Text = "";
                this.label_stopRecordInfo.Visible = false;

                if (this.recordingTimer != null)
                {
                    try
                    {
                        this.recordingTimer.Stop();
                        this.recordingTimer.Dispose();
                    }
                    catch { }
                    this.recordingTimer = null;
                }

                // Button-Farbe zurücksetzen und Button wieder aktivieren
                this.button_record.ForeColor = Color.Black;
                this.button_record.Enabled = true;
            }

            await Task.CompletedTask;
        }

    }
}





