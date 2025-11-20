using ModularAudience.Audio;
using ModularAudience.Forms.Modules;
using System.Collections.Concurrent;
using System.ComponentModel;

namespace ModularAudience.Forms
{
    public partial class WindowMain : Form
    {
        public readonly AudioCollection AudioC = new();

        internal static readonly BindingList<AudioCollectionView> CollectionViews = [];
        internal static readonly BindingList<TrackView> TrackViews = [];

        // Map AudioObj.Id -> Collection number (01-based) to restore distribution
        private static readonly Dictionary<Guid, int> _audioCollectionTags = new();

        public WindowMain()
        {
            this.InitializeComponent();
            this.StartPosition = FormStartPosition.Manual;
            this.Location = WindowsScreenHelper.GetCornerPosition(this, false, true);

            TrackViews.ListChanged += this.TrackViews_ListChanged;

            this.FormClosing += this.WindowMain_FormClosing; // neues zentrales Handling
            this.checkBox_singleCollection.CheckedChanged += this.checkBox_singleCollection_CheckedChanged;
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
            IEnumerable<string> filesToImport = Array.Empty<string>();
            bool fromResources = false;

            if (ModifierKeys.HasFlag(Keys.Alt))
            {
                var resourcesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources");
                string[] allowedExts = [".wav", ".mp3", ".flac"];
                var resourceFiles = Directory.Exists(resourcesPath)
                    ? Directory.GetFiles(resourcesPath).Where(f => allowedExts.Contains(Path.GetExtension(f).ToLowerInvariant())).ToArray()
                    : Array.Empty<string>();
                if (resourceFiles.Length <= 0)
                {
                    LogCollection.Log("No resource audio files found for import.");
                    return;
                }
                Random rand = new();
                filesToImport = new[] { resourceFiles[rand.Next(resourceFiles.Length)] };
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
            // Laden und Liste neu importierter Audios bestimmen ohne spätere Dispose
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

            // Logging nur für neu importierte
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
                    _audioCollectionTags[audio.Id] = 1;
                }
                first.Show();
                // Nur Referenzen aus temporärem Loader entfernen, nicht disposen
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
                    _audioCollectionTags[audio.Id] = num;
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
                    _audioCollectionTags[audio.Id] = num;
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
                    _audioCollectionTags[audio.Id] = num;
                }
                newView.Show();
            }

            // Entfernen aus temporärer Sammlung ohne Dispose (damit Views gültige Objekte behalten)
            foreach (var audio in importedAudios)
            {
                this.AudioC.Audios.Remove(audio);
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

        // Event for TrackViews ListChanged to make last TrackView order to other trackviews
        private void TrackViews_ListChanged(object? sender, ListChangedEventArgs e)
        {
            if (e.ListChangedType == ListChangedType.ItemAdded)
            {
                TrackView addedTrackView = TrackViews[e.NewIndex];

                // Try to position just beyond aligned with last trackview if not out of screen, then go one column right and start from top
                if (TrackViews.Count > 1)
                {
                    addedTrackView = TrackViews[0];
                    for (int i = 1; i < TrackViews.Count - 1; i++)
                    {
                        var tv = TrackViews[i];
                        if (tv.Location.Y > addedTrackView.Location.Y)
                        {
                            addedTrackView = tv;
                        }
                    }
                }

                var newX = addedTrackView.Location.X;
                var newY = addedTrackView.Location.Y + addedTrackView.Height + 5;
                if (newY + addedTrackView.Height > Screen.PrimaryScreen?.WorkingArea.Height)
                {
                    newX += addedTrackView.Width + 5;
                    newY = 0;
                }

                addedTrackView.Location = new Point(newX, newY);
                addedTrackView.Show();
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
                    _audioCollectionTags[audio.Id] = num;
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
            int maxNum = _audioCollectionTags.Count > 0 ? _audioCollectionTags.Values.Max() : 1;
            if (maxNum < 1)
            {
                maxNum = 1;
            }

            // Genug Views bereitstellen
            while (CollectionViews.Count < maxNum)
            {
                var emptyView = new AudioCollectionView(Array.Empty<NAudience.Core.AudioObj>());
                CollectionViews.Add(emptyView);
            }

            // Alle leeren und ausblenden
            foreach (var cv in CollectionViews)
            {
                cv.AudioC.Audios.Clear();
                cv.Hide();
            }

            // Verteilen gemäß Tags
            foreach (var audio in allAudios)
            {
                int num = 1;
                if (!_audioCollectionTags.TryGetValue(audio.Id, out num))
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
        }
    }
}
