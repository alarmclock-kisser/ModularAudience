using ModularAudience.Audio;
using ModularAudience.Forms.Helpers;
using ModularAudience.Forms.Modules;
using System.ComponentModel;

namespace ModularAudience.Forms
{
    public partial class WindowMain
    {
        private void Register_ListBox_Log()
        {
            this.listBox_log.Items.Clear();
            this.listBox_log.DataSource = LogCollection.Logs;
            this.listBox_log.HorizontalScrollbar = true;
            // Enable double buffering to reduce white/blank flicker when scrolling or updating
            try
            {
                var prop = this.listBox_log.GetType().GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                prop?.SetValue(this.listBox_log, true, null);
            }
            catch { }

            // Subscribe to posted log events and add to the BindingList on the UI thread.
            // Ensure we don't double-subscribe by removing previous handler if present.
            try
            {
                if (Instance?._logPostedWithTimestampHandler != null)
                {
                    try { LogCollection.NewLogPostedWithTimestamp -= Instance._logPostedWithTimestampHandler; } catch { }
                    Instance._logPostedWithTimestampHandler = null;
                }
            }
            catch { }

            Instance?._logPostedWithTimestampHandler = (ts, full) =>
            {
                WindowMainStaticHelpers.InvokeIfRequired(Instance, () =>
                {
                    try
                    {
                        // Insert chronologically based on timestamp (ascending). If same timestamp, append.
                        int insertAt = LogCollection.Logs.Count;
                        for (int i = 0; i < LogCollection.Logs.Count; i++)
                        {
                            string item = LogCollection.Logs[i];
                            try
                            {
                                int a = item.IndexOf('[');
                                int b = item.IndexOf(']');
                                if (a >= 0 && b > a)
                                {
                                    string inner = item.Substring(a + 1, b - a - 1);
                                    if (DateTime.TryParseExact(inner, LogCollection.TimeFormat, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime parsed))
                                    {
                                        if (ts < parsed)
                                        {
                                            insertAt = i;
                                            break;
                                        }
                                    }
                                }
                            }
                            catch { }
                        }

                        // Avoid inserting exact duplicate if already present at or adjacent to insert position
                        bool duplicate = false;
                        try
                        {
                            if (insertAt < LogCollection.Logs.Count && LogCollection.Logs[insertAt] == full)
                            {
                                duplicate = true;
                            }

                            if (insertAt - 1 >= 0 && LogCollection.Logs[insertAt - 1] == full)
                            {
                                duplicate = true;
                            }
                        }
                        catch { }

                        if (!duplicate)
                        {
                            LogCollection.Logs.Insert(insertAt, full);
                        }

                        // Trim FIFO
                        while (LogCollection.Logs.Count > LogCollection.MaxLogCount)
                        {
                            try { LogCollection.Logs.RemoveAt(0); } catch { break; }
                        }

                        if (LogCollection.AutoScroll)
                        {
                            try { this.listBox_log.TopIndex = LogCollection.Logs.Count - 1; } catch { }
                        }
                    }
                    catch { }
                });
            };

            try { LogCollection.NewLogPostedWithTimestamp += Instance?._logPostedWithTimestampHandler; } catch { }

            // NOTE: Do not subscribe to NewLogPosted as we handle chronological insertion via NewLogPostedWithTimestamp.

            this.listBox_log.DoubleClick += (s, e) =>
            {
                if (this.listBox_log.SelectedItem is string selectedLog)
                {
                    Clipboard.SetText(selectedLog);
                }
            };
        }

        private async void button_import_Click(object sender, EventArgs e)
        {
            IEnumerable<string> filesToImport = [];
            bool fromResources = false;

            if (ModifierKeys.HasFlag(Keys.Shift))
            {
                using FolderBrowserDialog folderBrowserDialog = new()
                {
                    Description = "Select Resource Folder to Import Audio Files From",
                    SelectedPath = this.lastImportFolder,
                    ShowNewFolderButton = false
                };

                if (folderBrowserDialog.ShowDialog() != DialogResult.OK)
                {
                    return;
                }

                this.lastImportFolder = folderBrowserDialog.SelectedPath;
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
                string? resourceFile = this.TryGetRandomResourceFile();
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
                string initialDir = this.AudioC.ImportDirectory;
                if (ModifierKeys.HasFlag(Keys.Control))
                {
                    initialDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources");
                    LogCollection.Log("Import: Using Resources folder as initial directory.");
                }

                using OpenFileDialog openFileDialog = new()
                {
                    InitialDirectory = initialDir,
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

        private string? TryGetRandomResourceFile()
        {
            return WindowMainStaticHelpers.TryGetRandomResourceFile(AllowedImportExtensions, ResourceRandom);
        }

        private void WindowMain_DragEnter(object? sender, DragEventArgs e)
        {
            try
            {
                if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    var items = e.Data.GetData(DataFormats.FileDrop) as string[] ?? [];
                    if (items.Any(p => !string.IsNullOrWhiteSpace(p) &&
                        (Directory.Exists(p) || AllowedImportExtensions.Contains(Path.GetExtension(p)))))
                    {
                        e.Effect = DragDropEffects.Copy;
                        return;
                    }
                }
            }
            catch { }

            e.Effect = DragDropEffects.None;
        }

        private async void WindowMain_DragDrop(object? sender, DragEventArgs e)
        {
            if (e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                LogCollection.Log("DragDrop: no FileDrop data.");
                return;
            }

            string[] dropped;
            try
            {
                dropped = e.Data.GetData(DataFormats.FileDrop) as string[] ?? [];
            }
            catch (Exception ex)
            {
                LogCollection.Log($"DragDrop: failed to read dropped data: {ex.Message}");
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
                            .Where(f => AllowedImportExtensions.Contains(Path.GetExtension(f)));
                        collectedPaths.AddRange(found);
                    }
                    else if (File.Exists(path) && AllowedImportExtensions.Contains(Path.GetExtension(path)))
                    {
                        collectedPaths.Add(path);
                    }
                }
                catch (Exception ex)
                {
                    LogCollection.Log($"DragDrop: error scanning '{path}': {ex.Message}");
                }
            }

            var validPaths = collectedPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (validPaths.Count == 0)
            {
                LogCollection.Log("DragDrop: No allowed audio files found in drop.");
                return;
            }

            try { this.lastImportFolder = Path.GetDirectoryName(validPaths[0]) ?? this.lastImportFolder; } catch { }
            await this.ImportAndPlaceAsync(validPaths, fromResources: false);
        }

        private async Task ImportAndPlaceAsync(IEnumerable<string> filePaths, bool fromResources)
        {
            var validPaths = filePaths.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
            if (validPaths.Count == 0)
            {
                return;
            }

            var loaded = (await this.AudioC.LoadManyAsync(validPaths)).ToList();
            var pairs = validPaths
                .Select((p, i) => (Path: Path.GetFullPath(p), Audio: i < loaded.Count ? loaded[i] : null))
                .Where(x => x.Audio != null)
                .Select(x => (x.Path, Audio: x.Audio!))
                .ToList();

            var importedAudios = pairs.Select(x => x.Audio!).ToList();
            if (importedAudios.Count == 0)
            {
                return;
            }

            foreach (var audio in importedAudios)
            {
                LogCollection.Log(fromResources ? $"{audio.Name} imported from resources." : $"{audio.Name} imported.");
            }

            this.PlaceImportedAudios(pairs, importedAudios);

            foreach (var audio in importedAudios)
            {
                this.AudioC.Audios.Remove(audio);
            }
        }

        internal void PlaceImportedAudios(List<(string Path, AudioObj Audio)> importedPairs, List<AudioObj> importedAudios)
        {
            WindowMainStaticHelpers.InvokeIfRequired(Instance, () =>
            {
                bool prevSuppress = SuppressCollectionViewPositioning;
                SuppressCollectionViewPositioning = true;
                try
                {
                    var pairs = importedPairs;
                    if (this.AllInOneBag)
                    {
                        var targetView = CollectionViews.LastOrDefault(cv => cv != null && !cv.IsDisposed);
                        if (targetView == null)
                        {
                            targetView = new AudioCollectionView([]);
                        }

                        int num = GetCollectionNumber(targetView);
                        foreach (var audio in importedAudios)
                        {
                            targetView.AudioC.Audios.Add(audio);
                            AudioCollectionTags[audio.Id] = num;
                        }
                        targetView.Show();
                    }

                    else if (this.StructuredImports)
                    {
                        var groups = pairs.GroupBy(x => Path.GetDirectoryName(x.Path) ?? string.Empty);
                        foreach (var g in groups)
                        {
                            var audios = g.Select(x => x.Audio).ToList();
                            if (audios.Count == 0)
                            {
                                continue;
                            }

                            var newView = new AudioCollectionView(audios);
                            string folderName = Path.GetFileName(g.Key);
                            if (string.IsNullOrWhiteSpace(folderName))
                            {
                                folderName = g.Key;
                            }

                            try { newView.Rename(folderName); } catch { }
                            int num = GetCollectionNumber(newView);
                            foreach (var audio in audios)
                            {
                                AudioCollectionTags[audio.Id] = num;
                            }

                            newView.Show();
                        }
                    }
                    else
                    {
                        var audioList = pairs.Select(x => x.Audio).ToList();
                        var last = CollectionViews.LastOrDefault();
                        if (last == null)
                        {
                            var newView = new AudioCollectionView(audioList);
                            int num = GetCollectionNumber(newView);
                            foreach (var audio in audioList)
                            {
                                AudioCollectionTags[audio.Id] = num;
                            }
                            newView.Show();
                        }
                        else if (last.AudioCount == 0)
                        {
                            int num = GetCollectionNumber(last);
                            foreach (var audio in audioList)
                            {
                                last.AudioC.Audios.Add(audio);
                                AudioCollectionTags[audio.Id] = num;
                            }
                            last.Show();
                        }
                        else
                        {
                            var newView = new AudioCollectionView(audioList);
                            int num = GetCollectionNumber(newView);
                            foreach (var audio in audioList)
                            {
                                AudioCollectionTags[audio.Id] = num;
                            }
                            newView.Show();
                        }
                    }
                }
                finally
                {
                    SuppressCollectionViewPositioning = prevSuppress;
                }
            });

            foreach (var audio in importedAudios)
            {
                this.AudioC.Audios.Remove(audio);
            }
        }
    }
}
