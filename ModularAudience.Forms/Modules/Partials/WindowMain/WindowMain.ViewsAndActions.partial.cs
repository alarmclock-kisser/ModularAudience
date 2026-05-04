using ModularAudience.Audio;
using ModularAudience.Forms.Helpers;
using ModularAudience.Forms.Modules;
using ModularAudience.Forms.Modules.Dialogs;
using System.ComponentModel;

namespace ModularAudience.Forms
{
    public partial class WindowMain
    {
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

            TrackView? lastPlaced = null;
            foreach (var view in TrackViews.Where(tv => tv != null && !tv.IsDisposed))
            {
                if (view == null || view.IsDisposed)
                {
                    continue;
                }

                view.StartPosition = FormStartPosition.Manual;
                view.Location = new Point(currentX, currentY);
                lastPlaced = view;
                columnWidth = Math.Max(columnWidth, view.Width);
                currentY += view.Height + TrackViewSpacing.Height;

                bool exceedsBottom = currentY + view.Height > maxBottom;
                if (!exceedsBottom)
                {
                    continue;
                }

                if (lastPlaced != null)
                {
                    int proposedX = lastPlaced.Location.X + lastPlaced.Width + TrackViewSpacing.Width;
                    if (proposedX + view.Width > maxRight)
                    {
                        proposedX = workingArea.Left;
                    }

                    currentX = proposedX;
                }
                else
                {
                    currentX += columnWidth + TrackViewSpacing.Width;
                    if (currentX + view.Width > maxRight)
                    {
                        currentX = workingArea.Left;
                    }
                }

                currentY = workingArea.Top;
                columnWidth = 0;
            }
        }

        private void CollectionViews_ListChanged(object? sender, ListChangedEventArgs e)
        {
            if (e.ListChangedType == ListChangedType.ItemAdded)
            {
                var addedView = CollectionViews[e.NewIndex];
                if (addedView == null || addedView.IsDisposed)
                {
                    return;
                }

                if (addedView.IsHandleCreated)
                {
                    try { this.PositionCollectionView(addedView); } catch { }
                    return;
                }

                EventHandler? handleCreatedHandler = null;
                handleCreatedHandler = (s, ea) =>
                {
                    try
                    {
                        addedView.HandleCreated -= handleCreatedHandler;
                        if (!addedView.IsDisposed)
                        {
                            this.PositionCollectionView(addedView);
                        }
                    }
                    catch { }
                };
                addedView.HandleCreated += handleCreatedHandler;

                EventHandler? shownHandler = null;
                shownHandler = (s, ea) =>
                {
                    try
                    {
                        addedView.Shown -= shownHandler;
                        if (!addedView.IsDisposed)
                        {
                            this.PositionCollectionView(addedView);
                        }
                    }
                    catch { }
                };
                addedView.Shown += shownHandler;
            }

            this.UpdateTrackDependentUI();
        }

        private void checkBox_singleCollection_CheckedChanged(object? sender, EventArgs e)
        {
        }

        internal static int GetCollectionNumber(AudioCollectionView view)
        {
            return WindowMainStaticHelpers.GetCollectionNumber(CollectionViews, view);
        }

        private void MergeCollectionsToSingle()
        {
            if (CollectionViews.Count == 0)
            {
                return;
            }

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
        }

        private void RebuildCollectionsFromTags()
        {
            if (CollectionViews.Count == 0)
            {
                return;
            }

            var allAudios = new List<AudioObj>();
            foreach (var cv in CollectionViews)
            {
                allAudios.AddRange(cv.AudioC.Audios.ToList());
            }

            int maxNum = AudioCollectionTags.Count > 0 ? AudioCollectionTags.Values.Max() : 1;
            if (maxNum < 1)
            {
                maxNum = 1;
            }

            while (CollectionViews.Count < maxNum)
            {
                _ = new AudioCollectionView([]);
            }

            foreach (var cv in CollectionViews)
            {
                cv.AudioC.Audios.Clear();
                cv.Hide();
            }

            foreach (var audio in allAudios)
            {
                int num = AudioCollectionTags.TryGetValue(audio.Id, out int value) ? value : 1;
                int index = Math.Clamp(num - 1, 0, CollectionViews.Count - 1);
                CollectionViews[index].AudioC.Audios.Add(audio);
            }

            foreach (var cv in CollectionViews)
            {
                if (cv.AudioC.Audios.Count > 0)
                {
                    cv.Show();
                }
            }
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
                try
                {
                    view.Invoke(() => view.Location = location);
                }
                catch { }
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
            WindowMainStaticHelpers.UpdateCollectionTag(AudioCollectionTags, CollectionViews, audio, targetView);
        }

        internal void UpdateTrackDependentUI()
        {
            AudioObj? selectedAudio = this.GetSelectedAudioForCommands();

            this.button_scanBpm.Enabled = selectedAudio != null;
            this.button_scanTiming.Enabled = selectedAudio != null;
            this.button_scanKey.Enabled = selectedAudio != null;
            this.button_timeStretch.Enabled = LastSelectedTrackView != null || CollectionViews.Count > 0;
            this.button_export.Enabled = LastSelectedTrackView != null;
            this.comboBox_exportFormat.Enabled = LastSelectedTrackView != null;
            this.comboBox_exportBits.Enabled = LastSelectedTrackView != null;
            this.button_autoSamples.Enabled = LastSelectedTrackView != null;
            this.textBox_info.Text = LastSelectedTrackView != null ? LastSelectedTrackView.OriginalAudio.GetInfoString() : this.textBox_info.Text;

            if (selectedAudio != null)
            {
                this.textBox_scanBpmResult.Text = selectedAudio.ScannedBpm > 0 ? $"{selectedAudio.ScannedBpm:F3} BPM" : "";
                this.textBox_scanTimingResult.Text = selectedAudio.ScannedTiming > 0.0f ? WindowMainFormatHelpers.GetTimingString(selectedAudio.ScannedTiming) : "";
                this.textBox_scanKeyResult.Text = !string.IsNullOrEmpty(selectedAudio.ScannedKey) ? selectedAudio.ScannedKey : "";
            }
            else
            {
                this.textBox_scanBpmResult.Text = "";
                this.textBox_scanTimingResult.Text = "";
                this.textBox_scanKeyResult.Text = "";
            }
        }

        internal AudioObj? GetSelectedAudioForCommands()
        {
            if (LastSelectedTrackView != null && !LastSelectedTrackView.IsDisposed)
            {
                return LastSelectedTrackView.OriginalAudio;
            }

            return CollectionViews
                .Where(cv => cv != null && !cv.IsDisposed)
                .SelectMany(cv => cv.SelectedAudios)
                .FirstOrDefault();
        }

        internal void UpdateInfoText(AudioObj? audio)
        {
            this.textBox_info.Text = audio != null ? audio.GetInfoString() : string.Empty;
        }

        private void HighlightSelectedTrackView()
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

        internal void RefreshAllCollectionViews()
        {
            WindowMainStaticHelpers.RefreshAllCollectionViews(CollectionViews, this.UpdateTrackDependentUI);
        }

        private void button_browse_Click(object sender, EventArgs e)
        {
            string workingDir = this.AudioC.WorkingDirectory;
            if (!Directory.Exists(workingDir))
            {
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

        private void button_autoSamples_Click(object sender, EventArgs e)
        {
            if (LastSelectedTrackView == null || LastSelectedTrackView.IsDisposed)
            {
                MessageBox.Show(this, "No track selected.", "Auto Samples", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using var dlg = new AutoSamplesDialog(LastSelectedTrackView.OriginalAudio);
            dlg.ShowDialog(this);

            if (dlg.DialogResult != DialogResult.OK || dlg.ResultSamples.Count == 0)
            {
                return;
            }

            var samples = dlg.ResultSamples.ToList();
            AudioCollectionView collection = new(samples);
            int num = GetCollectionNumber(collection);
            foreach (var audio in samples)
            {
                AudioCollectionTags[audio.Id] = num;
            }
            collection.Rename("Samples [" + LastSelectedTrackView.OriginalAudio.Name + "]");
            collection.Show();
        }

        private void button_newBag_Click(object sender, EventArgs e)
        {
            if (this.AllInOneBag)
            {
                MessageBox.Show(this, "Cannot create a new collection while in single collection mode.", "New Collection", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            AudioCollectionView collection = new([]);
            collection.Show();
        }

        private void button_drumRoll_Click(object sender, EventArgs e)
        {
            try
            {
                DrumRollEditor editor = new(SelectedTracks.ToList());
                editor.Show();
            }
            catch (Exception ex)
            {
                try { LogCollection.Log($"DrumRoll button error: {ex.Message}"); } catch { }
                MessageBox.Show(ex.Message, "Drum Roll Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void button_pianoRoll_Click(object sender, EventArgs e)
        {
            try
            {
                PianoRollEditor editor = new(SelectedTracks.ToList());
                editor.Show();
            }
            catch (Exception ex)
            {
                try { LogCollection.Log($"PianoRoll button error: {ex.Message}"); } catch { }
                MessageBox.Show(ex.Message, "Piano Roll Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void button_loopControl_Click(object sender, EventArgs e)
        {
            WindowMainStaticHelpers.InvokeIfRequired(Instance, () =>
            {
                if (LoopControlWindow == null || LoopControlWindow.IsDisposed)
                {
                    LoopControlWindow = new LoopControl();
                }

                LoopControlWindow.Show();
            });
        }

        private void button_newTrack_Click(object sender, EventArgs e)
        {
            var cv = CollectionViews.LastOrDefault();
            if (cv == null)
            {
                cv = new AudioCollectionView([]);
            }

            var track = new AudioObj
            {
                Name = "New Track #" + cv.AudioCount.ToString("D2"),
                SampleRate = 44100,
                Channels = 2,
                BitDepth = 32
            };
            track.Rename(track.Name);

            cv.AudioC.Audios.Add(track);
            var trackView = new TrackView(track);
            trackView.Show();
            cv.Show();
        }

        private void button_breakbeatArchitect_Click(object sender, EventArgs e)
        {
            var breakbeatWindow = new BreakbeatGeneratorDialog(SelectedTracks);
            breakbeatWindow.Show();
        }

        private void button_pitchShift_Click(object sender, EventArgs e)
        {
            using var dlg = new PitchShiftDialog(SelectedTracks);
            dlg.ShowDialog(this);
        }

        private async void button_applyCloseAll_Click(object sender, EventArgs e)
        {
            var openTrackViews = TrackViews.Where(tv => tv != null && !tv.IsDisposed).ToList();
            var tasks = openTrackViews.Select(tv => tv.ApplyTrackAsync(true));
            await Task.WhenAll(tasks);
        }

        private void button_devMode_Click(object sender, EventArgs e)
        {
            DeveloperFunctionsWindow ??= new DeveloperFunctions();
        }

        private void button_cuda_Click(object sender, EventArgs e)
        {
            CudaFunctionsWindow ??= new CudaFunctions();
            CudaFunctionsWindow.Show();
        }

        private void listBox_log_DoubleClick(object sender, EventArgs e)
        {
            if (ModifierKeys.HasFlag(Keys.Control))
            {
                string allLogs = string.Join(Environment.NewLine, LogCollection.Logs);
                Clipboard.SetText(allLogs);
            }
            else if (this.listBox_log.SelectedItem != null)
            {
                Clipboard.SetText(this.listBox_log.SelectedItem.ToString() ?? "");
            }
        }
    }
}
