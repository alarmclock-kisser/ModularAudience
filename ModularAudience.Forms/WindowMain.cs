using ModularAudience.Audio;
using ModularAudience.Forms.Modules;
using NAudience.Core;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace ModularAudience.Forms
{
    public partial class WindowMain : Form
    {
        public static WindowMain? Instance { get; private set; }

        public readonly AudioCollection AudioC = new();

        internal static readonly BindingList<AudioCollectionView> CollectionViews = [];
        internal static readonly BindingList<TrackView> TrackViews = [];
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

            if (ModifierKeys.HasFlag(Keys.Alt))
            {
                string? resourceFile = TryGetRandomResourceFile();
                if (resourceFile == null)
                {
                    LogCollection.Log("No resource audio files found for import.");
                    return;
                }
                filesToImport = new[] { resourceFile };
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
            var importedAudios = loaded.Where(a => a != null).Cast<NAudience.Core.AudioObj>().ToList();
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
            int originX = workingArea.Left + TrackViewScreenMargin.Left;
            int originY = workingArea.Top + TrackViewScreenMargin.Top;
            int currentX = originX;
            int currentY = originY;
            int columnWidth = 0;
            int maxRight = workingArea.Right - TrackViewScreenMargin.Right;
            int maxBottom = workingArea.Bottom - TrackViewScreenMargin.Bottom;

            foreach (var view in TrackViews.Where(tv => tv != null && !tv.IsDisposed))
            {
                if (view == null || view.IsDisposed)
                {
                    continue;
                }

                view.StartPosition = FormStartPosition.Manual;
                view.Location = new Point(currentX, currentY);

                columnWidth = Math.Max(columnWidth, view.Width);
                currentY += view.Height + TrackViewSpacing.Height;

                bool exceedsBottom = currentY + view.Height > maxBottom;
                if (exceedsBottom)
                {
                    currentX += columnWidth + TrackViewSpacing.Width;
                    currentY = originY;
                    columnWidth = 0;

                    if (currentX + view.Width > maxRight)
                    {
                        currentX = originX;
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
                this.PositionCollectionViews();
            }
        }

        private void checkBox_singleCollection_CheckedChanged(object? sender, EventArgs e)
        {
            if (this.checkBox_singleCollection.Checked)
            {
                MergeCollectionsToSingle();
            }
            else
            {
                RebuildCollectionsFromTags();
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
            var allAudios = new List<NAudience.Core.AudioObj>();
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
                view.Location = location;
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
            _audioCollectionTags[audio.Id] = num;
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

            float scannedTiming = await BeatScanner.ScanTimingAsync(LastSelectedTrackView.OriginalAudio);

            this.textBox_scanTimingResult.Text = GetTimingString(scannedTiming);
            LastSelectedTrackView.OriginalAudio.ScannedTiming = scannedTiming;
        }

        private async void button_scanKey_Click(object sender, EventArgs e)
        {
            if (LastSelectedTrackView == null)
            {
                return;
            }

            string scannedKey = await BeatScanner.ScanKeyAsync(LastSelectedTrackView.OriginalAudio);
            
            this.textBox_scanKeyResult.Text = scannedKey;
            LastSelectedTrackView.OriginalAudio.ScannedKey = scannedKey;
        }

        public static string GetTimingString(float timing)
        {
            if (timing <= 0f)
                return "-";

            // Standard: 4/4 als Basis
            // timing = Bruchteil eines Takts (z.B. 0.25 = Viertel, 0.5 = Halbe, 1.0 = Ganze, 0.75 = punktierte Halbe, etc.)
            // Wir suchen Nenner als Potenz von 2 (1, 2, 4, 8, 16, ...)
            int maxDenominator = 64; // bis 1/64-Noten
            for (int denom = 1; denom <= maxDenominator; denom *= 2)
            {
                float num = timing * denom * 4; // 4/4 als Basis
                if (Math.Abs(num - MathF.Round(num)) < 0.0001f)
                {
                    int numerator = (int)MathF.Round(num);
                    int denominator = denom * 4;
                    return $"{numerator} / {denominator}";
                }
            }
            // Fallback: Dezimalwert
            return timing.ToString("0.###");
        }
    }
}
