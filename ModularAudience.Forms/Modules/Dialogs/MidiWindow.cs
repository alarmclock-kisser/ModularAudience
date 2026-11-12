using ModularAudience.Audio;
using ModularAudience.Audio.Midi;
using ModularAudience.Audio.Omr;
using System.Drawing.Drawing2D;

namespace ModularAudience.Forms.Modules.Dialogs
{
    public partial class MidiWindow : Form
    {
        private MidiFileData midiFile;
        private List<MidiTrackData> tracks;
        private AudioObj? customSample;
        private AudioCollectionView? customCollection;
        private readonly ContextMenuStrip customInstrumentMenu = new();
        private sealed class CollectionSnapshot : IDisposable
        {
            public List<AudioObj> Samples { get; } = [];
            public void Dispose() { foreach (var sample in Samples) sample.Dispose(); }
        }
        private CollectionSnapshot CaptureCustomCollection()
        {
            var snapshot = new CollectionSnapshot();
            try
            {
                if (this.SelectedInstrument == MidiInstrument.CustomCollection && this.customCollection is { IsDisposed: false } view)
                    foreach (var sample in view.AudioC.Audios)
                        if (sample.Channels > 0 && sample.SampleRate > 0 && sample.Data.Length >= sample.Channels)
                            snapshot.Samples.Add(sample.Clone());
                return snapshot;
            }
            catch { snapshot.Dispose(); throw; }
        }
        private readonly MidiSampleRenderCache sampleRenderCache = new();
        private AudioObj? previewAudio;
        private CancellationTokenSource? previewCts;
        private readonly HashSet<Task> activeMidiRenders = [];
        private CancellationTokenSource? saveRenderCts;
        private bool closingMidiWindow;
        private bool allowMidiClose;

        private async Task<AudioObj> TrackMidiRenderAsync(Func<AudioObj> render, CancellationToken token)
        {
            var task = Task.Run(render, token);
            this.activeMidiRenders.Add(task);
            try { return await task; }
            finally { this.activeMidiRenders.Remove(task); }
        }
        private Point? midiSelectionStart;
        private Point? midiSelectionCurrent;

        public MidiWindow(string? filePath, MidiFileData? midiFileData = null)
        {
            this.InitializeComponent();
            Hints.HintContent.Apply(this.toolTip_midi, this, Hints.HintContent.Midi);
            this.components ??= new System.ComponentModel.Container();
            this.components.Add(this.customInstrumentMenu);
            this.midiFile = midiFileData ?? MidiFileData.Load(filePath ?? throw new ArgumentNullException(nameof(filePath)));
            this.tracks = this.midiFile.Tracks.ToList();
            this.Text = $"MIDI Renderer - {Path.GetFileNameWithoutExtension(this.midiFile.FilePath)}";
            this.numericUpDown_bpm.Value = (decimal)Math.Clamp(this.midiFile.DefaultBpm, 20.0, 400.0);
            this.comboBox_instrument.Items.AddRange([
            "Sine", "Saw", "Square", "Triangle", "Noise", "Pluck / Karplus-Strong", "Custom Sample", "Custom Collection"]);
            this.comboBox_instrument.SelectedIndex = 0;
            this.comboBox_renderQuality.Items.AddRange(["Best", "Balanced", "Fast", "Draft"]);
            this.comboBox_renderQuality.SelectedIndex = (int)MidiRenderQuality.Balanced;
            this.RefreshMidiPages(1);

            // Right-click on the Merge button: "Merge all / Un-merge all".
            var mergeMenu = new ContextMenuStrip();
            var mergeAllItem = new ToolStripMenuItem("Merge all");
            mergeAllItem.Click += (_, _) =>
            {
                bool allMerged = this.mergedTrackIndices.Count > 0 &&
                    this.mergedTrackIndices.SetEquals(this.midiFile.Tracks.Select(t => t.Index));
                if (allMerged)
                {
                    this.mergedTrackIndices.Clear();
                }
                else
                {
                    this.mergedTrackIndices.Clear();
                    foreach (int idx in this.midiFile.Tracks.Select(t => t.Index)) this.mergedTrackIndices.Add(idx);
                }
                this.previewCts?.Cancel();
                this.RefreshMidiPages((int)this.numericUpDown_track.Value);
            };
            mergeMenu.Opening += (_, _) =>
            {
                bool allMerged = this.mergedTrackIndices.Count > 0 &&
                    this.mergedTrackIndices.SetEquals(this.midiFile.Tracks.Select(t => t.Index));
                mergeAllItem.Text = allMerged ? "Un-merge all" : "Merge all";
            };
            mergeMenu.Items.Add(mergeAllItem);
            this.button_merge.ContextMenuStrip = mergeMenu;
        }

        private MidiTrackData SelectedTrack => this.tracks[Math.Clamp((int)this.numericUpDown_track.Value - 1, 0, this.tracks.Count - 1)];

        public MidiInstrument SelectedInstrument => (MidiInstrument)Math.Clamp(this.comboBox_instrument.SelectedIndex, 0, 7);

        public double PreviewBpm => (double)this.numericUpDown_bpm.Value;

        public MidiFileData CurrentMidiFile => this.midiFile;

        public void SelectPage(int pageNumber) => this.RefreshMidiPages(pageNumber);

        private MidiRenderQuality SelectedRenderQuality => (MidiRenderQuality)Math.Clamp(this.comboBox_renderQuality.SelectedIndex, 0, 3);

        private void pictureBox_midi_Paint(object? sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.HighQuality;
            e.Graphics.Clear(Color.FromArgb(24, 24, 28));
            if (this.tracks.Count == 0) return;
            MidiTrackData track = this.SelectedTrack;
            int left = 38;
            int top = 12;
            int width = Math.Max(1, this.pictureBox_midi.ClientSize.Width - left - 12);
            int height = Math.Max(1, this.pictureBox_midi.ClientSize.Height - top - 16);
            using Pen gridPen = new(Color.FromArgb(48, 48, 56));
            using Font font = new("Segoe UI", 8f);
            for (int note = 0; note <= 127; note += 12)
            {
                int y = top + height - (int)(note / 127.0 * height);
                e.Graphics.DrawLine(gridPen, left, y, left + width, y);
                e.Graphics.DrawString(MidiNoteName(note), font, Brushes.Gainsboro, 2, y - 7);
            }

            long lengthTicks = Math.Max(1, track.LengthTicks);
            foreach (MidiNoteData note in track.Notes)
            {
                float x = left + (float)(note.StartTick / (double)lengthTicks * width);
                float noteWidth = Math.Max(2f, (float)(note.DurationTicks / (double)lengthTicks * width));
                float y = top + height - (float)((note.NoteNumber + 1) / 128.0 * height);
                float noteHeight = Math.Max(3f, height / 128f + 1f);
                using Brush brush = new SolidBrush(ChannelColor(note.Channel));
                e.Graphics.FillRectangle(brush, x, y, Math.Min(noteWidth, left + width - x), noteHeight);
            }

            if (this.previewAudio?.PlayerPlaying == true && this.previewAudio.Duration > TimeSpan.Zero)
            {
                double progress = Math.Clamp(this.previewAudio.CurrentTime.TotalSeconds / this.previewAudio.Duration.TotalSeconds, 0.0, 1.0);
                float caretX = left + (float)(progress * width);
                using Pen caretPen = new(Color.Red, 2f);
                e.Graphics.DrawLine(caretPen, caretX, top, caretX, top + height);
            }
            e.Graphics.DrawString(track.Name, font, Brushes.White, left, 2);

            if (this.midiSelectionStart is Point selectionStart && this.midiSelectionCurrent is Point selectionCurrent)
            {
                Rectangle selectionRectangle = NormalizeRectangle(selectionStart, selectionCurrent);
                selectionRectangle.Intersect(new Rectangle(left, top, width, height));
                if (selectionRectangle.Width > 0 && selectionRectangle.Height > 0)
                {
                    using Brush selectionBrush = new SolidBrush(Color.FromArgb(55, Color.DeepSkyBlue));
                    using Pen selectionPen = new(Color.DeepSkyBlue, 2f) { DashStyle = DashStyle.Dash };
                    e.Graphics.FillRectangle(selectionBrush, selectionRectangle);
                    e.Graphics.DrawRectangle(selectionPen, selectionRectangle);
                }

                using Brush anchorBrush = new SolidBrush(Color.White);
                e.Graphics.FillEllipse(anchorBrush, selectionStart.X - 3, selectionStart.Y - 3, 6, 6);
                if (selectionCurrent != selectionStart)
                {
                    e.Graphics.FillEllipse(anchorBrush, selectionCurrent.X - 3, selectionCurrent.Y - 3, 6, 6);
                }
            }
        }

        public async Task<AudioObj> RenderMidiAsync(MidiFileData midi, int trackIndex, CancellationToken cancellationToken = default, double pitchFrequency = 440.0, IProgress<double>? progress = null)
        {
            MidiInstrument instrument = this.SelectedInstrument;
            double bpm = (double)this.numericUpDown_bpm.Value;
            using AudioObj? selectedCustomSample = this.customSample?.Clone();
            using var collection = this.CaptureCustomCollection();
            MidiRenderQuality quality = this.SelectedRenderQuality;
            return await this.TrackMidiRenderAsync(
                () => MidiAudioRenderer.Render(midi, trackIndex, instrument, bpm, selectedCustomSample, cancellationToken: cancellationToken, pitchFrequency: pitchFrequency,
                    previewQuality: true, progress: progress, renderQuality: quality, sampleCache: this.sampleRenderCache,
                    customCollection: collection.Samples), cancellationToken);
        }

        private static Rectangle NormalizeRectangle(Point first, Point second)
        {
            return Rectangle.FromLTRB(
                Math.Min(first.X, second.X),
                Math.Min(first.Y, second.Y),
                Math.Max(first.X, second.X),
                Math.Max(first.Y, second.Y));
        }

        private void pictureBox_midi_MouseEnter(object? sender, EventArgs e) => this.pictureBox_midi.Cursor = Cursors.Cross;

        private void pictureBox_midi_MouseLeave(object? sender, EventArgs e)
        {
            if (this.midiSelectionStart == null)
            {
                this.pictureBox_midi.Cursor = Cursors.Default;
            }
        }

        private void pictureBox_midi_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || this.tracks.Count == 0)
            {
                return;
            }

            // Allow rectangle selection on the merged track too, so the MIDI editor can be opened on it.

            this.midiSelectionStart = this.ClampToMidiCanvas(e.Location);
            this.midiSelectionCurrent = this.midiSelectionStart;
            this.pictureBox_midi.Capture = true;
            this.pictureBox_midi.Invalidate();
        }

        private void pictureBox_midi_MouseMove(object? sender, MouseEventArgs e)
        {
            if (this.midiSelectionStart is not Point)
            {
                return;
            }

            this.midiSelectionCurrent = this.ClampToMidiCanvas(e.Location);
            this.pictureBox_midi.Invalidate();
        }

        private void pictureBox_midi_MouseUp(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || this.midiSelectionStart is not Point start)
            {
                return;
            }

            Point end = this.ClampToMidiCanvas(e.Location);
            this.midiSelectionCurrent = end;
            this.pictureBox_midi.Capture = false;
            this.midiSelectionStart = null;
            this.midiSelectionCurrent = null;
            this.pictureBox_midi.Invalidate();

            Rectangle rectangle = NormalizeRectangle(start, end);
            if (rectangle.Width < 2 || rectangle.Height < 2)
            {
                return;
            }

            MidiEditSelection selection = this.CreateSelection(rectangle);
            MidiEditor editor = new(selection, this);
            editor.Show(this);
        }

        private Point ClampToMidiCanvas(Point point)
        {
            int left = 38;
            int top = 12;
            int width = Math.Max(1, this.pictureBox_midi.ClientSize.Width - left - 12);
            int height = Math.Max(1, this.pictureBox_midi.ClientSize.Height - top - 16);
            return new Point(
                Math.Clamp(point.X, left, left + width),
                Math.Clamp(point.Y, top, top + height));
        }

        private MidiEditSelection CreateSelection(Rectangle rectangle)
        {
            MidiTrackData track = this.SelectedTrack;
            int left = 38;
            int top = 12;
            int width = Math.Max(1, this.pictureBox_midi.ClientSize.Width - left - 12);
            int height = Math.Max(1, this.pictureBox_midi.ClientSize.Height - top - 16);
            long startTick = (long)Math.Floor((rectangle.Left - left) / (double)width * Math.Max(1, track.LengthTicks));
            long endTick = (long)Math.Ceiling((rectangle.Right - left) / (double)width * Math.Max(1, track.LengthTicks));
            int lowestNote = Math.Clamp((int)Math.Floor((top + height - rectangle.Bottom) / (double)height * 128), 0, 127);
            int highestNote = Math.Clamp((int)Math.Ceiling((top + height - rectangle.Top) / (double)height * 128) - 1, 0, 127);
            return MidiFileData.CreateEditSelection(this.midiFile, track.Index, startTick, endTick, lowestNote, Math.Max(lowestNote, highestNote));
        }

        private static string MidiNoteName(int note)
        {
            string[] names = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
            return names[note % 12] + (note / 12 - 1);
        }

        private static Color ChannelColor(int channel)
        {
            Color[] colors = [Color.DeepSkyBlue, Color.LightGreen, Color.Orange, Color.Violet, Color.Coral, Color.Khaki, Color.Turquoise, Color.Pink];
            return colors[Math.Abs(channel) % colors.Length];
        }

        private void numericUpDown_track_ValueChanged(object? sender, EventArgs e)
        {
            this.previewCts?.Cancel();
            this.UpdateMergeButton();
            this.pictureBox_midi.Invalidate();
        }

        private void numericUpDown_bpm_ValueChanged(object? sender, EventArgs e) => this.label_status.Text = $"BPM: {this.numericUpDown_bpm.Value:0.00}";

        private void comboBox_instrument_SelectedIndexChanged(object? sender, EventArgs e)
        {
            this.button_customInstrument.Enabled = this.SelectedInstrument is MidiInstrument.CustomSample or MidiInstrument.CustomCollection;
            if (this.SelectedInstrument != MidiInstrument.CustomSample)
            {
                this.customSample?.Dispose(); this.customSample = null;
            }
        }

        private void button_customInstrument_Click(object? sender, EventArgs e)
        {
            // Keep the menu alive through WinForms' item-click/close processing.
            // The form's component container owns it and disposes it with the window.
            ContextMenuStrip menu = this.customInstrumentMenu;
            while (menu.Items.Count > 0) menu.Items[0].Dispose();
            if (this.SelectedInstrument == MidiInstrument.CustomCollection)
            {
                foreach (var view in WindowMain.CollectionViews.Where(view => !view.IsDisposed))
                {
                    var selected = view;
                    menu.Items.Add(new ToolStripMenuItem($"{view.Text} ({view.AudioC.Audios.Count} samples)", null, (_, _) =>
                    {
                        this.customCollection = selected;
                        this.label_status.Text = $"Custom collection: {selected.Text}";
                    }) { Enabled = view.AudioC.Audios.Any(s => s.Channels > 0 && s.SampleRate > 0 && s.Data.Length >= s.Channels) });
                }
                if (menu.Items.Count == 0) menu.Items.Add(new ToolStripMenuItem("No audio collections are currently open") { Enabled = false });
                menu.Show(this.button_customInstrument, 0, this.button_customInstrument.Height);
                return;
            }
            foreach (AudioObj audio in WindowMain.CollectionViews.Where(view => !view.IsDisposed).SelectMany(view => view.AudioC.Audios).DistinctBy(audio => audio.Id))
            {
                AudioObj selected = audio;
                menu.Items.Add(new ToolStripMenuItem(selected.Name, null, (_, _) =>
                {
                    this.customSample?.Dispose(); this.customSample = selected.Clone();
                    this.label_status.Text = $"Custom sample: {selected.Name}";
                }));
            }
            if (menu.Items.Count == 0)
            {
                menu.Items.Add(new ToolStripMenuItem("No audio objects are currently open") { Enabled = false });
            }
            menu.Show(this.button_customInstrument, 0, this.button_customInstrument.Height);
        }

        private async void button_preview_Click(object? sender, EventArgs e)
        {
            if (this.previewCts != null)
            {
                await this.StopPreviewAsync();
                return;
            }

            if (this.tracks.Count == 0) return;

            CancellationTokenSource? runCts = null;
            ProgressDialog? progressDialog = null;
            try
            {
                this.button_preview.Enabled = true;
                this.button_preview.Text = "Cancel rendering";
                this.label_status.Text = "Rendering preview...";
                runCts = new CancellationTokenSource();
                this.previewCts = runCts;
                MidiInstrument instrument = this.SelectedInstrument;
                double bpm = (double)this.numericUpDown_bpm.Value;
                MidiFileData midi = this.CreateOutputMidi(currentPageOnly: true);
                int trackIndex = midi.Tracks[0].Index;
                MidiRenderQuality quality = this.SelectedRenderQuality;
                using AudioObj? sample = this.customSample?.Clone();
                using var collection = this.CaptureCustomCollection();
                Progress<double> progress = new(value => progressDialog?.Report(value));
                progressDialog = new ProgressDialog("Rendering MIDI preview...", progress, ct: runCts.Token, cancellationSource: runCts, windowCloseDelay: 0.0d);
                progressDialog.Show(this);
                progressDialog.BringToFront();
                AudioObj audio = await this.TrackMidiRenderAsync(() => MidiAudioRenderer.Render(midi, trackIndex, instrument, bpm, sample,
                    cancellationToken: runCts.Token, pitchFrequency: midi.PitchFrequency, previewQuality: true, progress: progress,
                    renderQuality: quality, sampleCache: this.sampleRenderCache, customCollection: collection.Samples), runCts.Token);
                if (runCts.IsCancellationRequested || this.closingMidiWindow) { audio.Dispose(); return; }
                progressDialog.Complete();
                this.previewAudio = audio;
                this.button_preview.Text = "Stop Preview";
                this.button_preview.Enabled = true;
                this.label_status.Text = "Playing";
                this.timer_previewCaret.Start();
                TaskCompletionSource playbackStopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
                await audio.PlayAsync(runCts.Token, () => playbackStopped.TrySetResult());
                await playbackStopped.Task.WaitAsync(runCts.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                LogManager.Log($"MIDI preview failed: {ex}");
                if (!this.IsDisposed && !this.closingMidiWindow)
                    ModularAudience.Forms.Helpers.WindowMainStaticHelpers.ShowErrorWithCopyButton(this, "MIDI preview failed", ex);
            }
            finally
            {
                if (progressDialog != null && !progressDialog.IsDisposed)
                { progressDialog.Close(); progressDialog.Dispose(); }
                if (ReferenceEquals(this.previewCts, runCts))
                {
                    this.ResetPreviewState();
                }
                runCts?.Dispose();
            }
        }

        private async void button_save_Click(object? sender, EventArgs e)
        {
            await this.SaveAudioAsync(currentPageOnly: false);
        }

        private async void renderCurrentPageToolStripMenuItem_Click(object? sender, EventArgs e)
        {
            await this.SaveAudioAsync(currentPageOnly: true);
        }

        private async Task SaveAudioAsync(bool currentPageOnly)
        {
            if (!this.button_save.Enabled || this.closingMidiWindow || this.tracks.Count == 0) return;
            ProgressDialog? progressDialog = null;
            CancellationTokenSource? renderCts = null;
            try
            {
                this.button_save.Enabled = false;
                string renderMessage = currentPageOnly ? "Rendering current MIDI page..." : "Rendering all MIDI pages...";
                this.label_status.Text = renderMessage;
                MidiFileData midi = this.CreateOutputMidi(currentPageOnly, merge: true);
                int trackIndex = midi.Tracks[0].Index;
                MidiInstrument instrument = this.SelectedInstrument;
                double bpm = (double)this.numericUpDown_bpm.Value;
                using AudioObj? customSample = this.customSample?.Clone();
                using var collection = this.CaptureCustomCollection();
                MidiRenderQuality quality = this.SelectedRenderQuality;
                renderCts = new CancellationTokenSource();
                this.saveRenderCts = renderCts;
                Progress<double> progress = new(value => progressDialog?.Report(value));
                progressDialog = new ProgressDialog(renderMessage, progress, ct: renderCts.Token, cancellationSource: renderCts);
                progressDialog.Show(this);
                progressDialog.BringToFront();
                AudioObj audio = await this.TrackMidiRenderAsync(() => MidiAudioRenderer.Render(midi, trackIndex, instrument, bpm, customSample, cancellationToken: renderCts.Token, pitchFrequency: midi.PitchFrequency,
                    progress: progress, renderQuality: quality, sampleCache: this.sampleRenderCache, customCollection: collection.Samples), renderCts.Token);
                if (renderCts.IsCancellationRequested || this.closingMidiWindow) { audio.Dispose(); return; }
                string midiName = Path.GetFileNameWithoutExtension(this.midiFile?.FilePath ?? "MIDI");
                string sampleName = this.SelectedInstrument.ToString();
                var renderedCollection = new AudioCollectionView([]);
                renderedCollection.AudioC.Audios.Add(audio);
                audio = null; // collection owns the rendered audio
                renderedCollection.Rename($"{midiName}_rendered{sampleName}");
                renderedCollection.Show();
                progressDialog.Complete();
                this.label_status.Text = "Audio object saved";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                LogManager.Log($"MIDI render failed: {ex}");
                if (!this.IsDisposed && !this.closingMidiWindow)
                    ModularAudience.Forms.Helpers.WindowMainStaticHelpers.ShowErrorWithCopyButton(this, "MIDI rendering failed", ex);
            }
            finally
            {
                if (progressDialog != null && !progressDialog.IsDisposed)
                {
                    progressDialog.Close();
                    progressDialog.Dispose();
                }

                if (ReferenceEquals(this.saveRenderCts, renderCts)) this.saveRenderCts = null;
                renderCts?.Dispose();
                if (!this.IsDisposed) this.button_save.Enabled = true;
            }
        }

        public async Task StopPreviewAsync()
        {
            this.previewCts?.Cancel();
            if (this.previewAudio != null) await this.previewAudio.StopAsync();
            // The render/preview owner disposes its CTS in finally, after its progress
            // dialog is gone. Stop must not dispose a source still used by that dialog.
            if (this.previewCts != null && !this.IsDisposed)
                this.label_status.Text = "Cancelling; waiting for the current V1 operation to finish…";
        }

        public async Task PreviewMidiAsync(MidiFileData midi, int trackIndex)
        {
            var previous = this.previewCts;
            await this.StopPreviewAsync();
            while (previous != null && ReferenceEquals(this.previewCts, previous) && !this.IsDisposed)
                await Task.Delay(10);
            if (this.IsDisposed || this.closingMidiWindow) return;
            MidiInstrument instrument = this.SelectedInstrument;
            double bpm = (double)this.numericUpDown_bpm.Value;
            using AudioObj? customSample = this.customSample?.Clone();
            using var collection = this.CaptureCustomCollection();
            MidiRenderQuality quality = this.SelectedRenderQuality;
            CancellationTokenSource? runCts = null;
            try
            {
                runCts = new CancellationTokenSource();
                this.previewCts = runCts;
                AudioObj audio = await this.TrackMidiRenderAsync(() => MidiAudioRenderer.Render(midi, trackIndex, instrument, bpm, customSample, cancellationToken: runCts.Token, pitchFrequency: midi.PitchFrequency,
                    previewQuality: true, renderQuality: quality, sampleCache: this.sampleRenderCache, customCollection: collection.Samples), runCts.Token);
                if (runCts.IsCancellationRequested || this.closingMidiWindow) { audio.Dispose(); return; }
                this.previewAudio = audio;
                this.timer_previewCaret.Start();
                TaskCompletionSource playbackStopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
                await audio.PlayAsync(runCts.Token, () => playbackStopped.TrySetResult(), initialVolume: 1f);
                await playbackStopped.Task.WaitAsync(runCts.Token);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                if (ReferenceEquals(this.previewCts, runCts))
                {
                    this.ResetPreviewState();
                }
                runCts?.Dispose();
            }
        }

        private void ResetPreviewState()
        {
            if (this.IsDisposed) return;
            this.timer_previewCaret.Stop();
            this.previewCts = null;
            this.previewAudio?.Dispose();
            this.previewAudio = null;
            this.button_preview.Text = "Preview";
            this.button_preview.Enabled = true;
            this.label_status.Text = "Ready";
        }

        private void timer_previewCaret_Tick(object? sender, EventArgs e) => this.pictureBox_midi.Invalidate();

        private void MidiWindow_Resize(object? sender, EventArgs e) => this.pictureBox_midi.Invalidate();

        private async void MidiWindow_FormClosing(object? sender, FormClosingEventArgs e)
        {
            if (this.allowMidiClose) return;
            e.Cancel = true;
            if (this.closingMidiWindow) return;
            this.closingMidiWindow = true;
            this.saveRenderCts?.Cancel();
            await this.StopPreviewAsync();
            try { await Task.WhenAll(this.activeMidiRenders.ToArray()); }
            catch (OperationCanceledException) { }
            catch (Exception error) { LogManager.Log(error); }
            // Let render continuations release progress windows and sources first.
            await Task.Yield();
            this.allowMidiClose = true;
            if (!this.IsDisposed) this.Close();
        }

        public void ApplyEdit(MidiFileData midiFileData)
        {
            this.midiFile = midiFileData;
            this.previewCts?.Cancel();
            this.numericUpDown_bpm.Value = (decimal)Math.Clamp(this.midiFile.DefaultBpm, 20.0, 400.0);
            this.RefreshMidiPages((int)this.numericUpDown_track.Value);
        }

        private async void button_export_Click(object sender, EventArgs e)
        {
            await this.ExportMidiAsync(currentPageOnly: false);
        }

        private async void exportCurrentPageToolStripMenuItem_Click(object? sender, EventArgs e)
        {
            await this.ExportMidiAsync(currentPageOnly: true);
        }

        private async void exportMergedToolStripMenuItem_Click(object? sender, EventArgs e)
        {
            await this.ExportMidiAsync(currentPageOnly: false, merge: true);
        }

        private async Task ExportMidiAsync(bool currentPageOnly, bool merge = false)
        {
            if (!this.button_export.Enabled || this.closingMidiWindow || this.tracks.Count == 0) return;
            MidiFileData midi = this.CreateOutputMidi(currentPageOnly, merge);
            string suffix = currentPageOnly ? $"_page_{this.numericUpDown_track.Value}" : merge ? "_merged" : "_exported";
            using SaveFileDialog sfd = new()
            {
                Title = "Export MIDI File",
                Filter = "MIDI Files (*.mid)|*.mid",
                DefaultExt = "mid",
                AddExtension = true,
                FileName = Path.GetFileNameWithoutExtension(midi.FilePath) + suffix + ".mid"
            };

            if (sfd.ShowDialog(this) == DialogResult.OK)
            {
                try
                {
                    this.button_export.Enabled = false;
                    this.label_status.Text = "Exporting MIDI...";
                    string? exportedPath = await midi.ExportAsync(sfd.FileName);
                    if (exportedPath == null) throw new IOException("The MIDI file could not be exported. See the log for details.");
                    this.label_status.Text = $"MIDI exported to {exportedPath}";
                }
                catch (Exception ex)
                {
                    LogManager.Log($"MIDI export failed: {ex}");
                    ModularAudience.Forms.Helpers.WindowMainStaticHelpers.ShowErrorWithCopyButton(this, "MIDI export failed", ex);
                }
                finally
                {
                    if (!this.IsDisposed) this.button_export.Enabled = true;
                }
            }
        }

        private async void button_fromImage_Click(object sender, EventArgs e)
        {
            // OFD at MyMusic for image file import (png, jpg, bmp, pdf)
            using OpenFileDialog ofd = new()
            {
                Title = "Import MIDI from Image",
                Filter = "Image Files (*.png;*.jpg;*.jpeg;*.bmp;*.pdf)|*.png;*.jpg;*.jpeg;*.bmp;*.pdf",
                DefaultExt = "png",
                AddExtension = true,
                FileName = Path.GetFileNameWithoutExtension(this.midiFile.FilePath) + "_imported.png"
            };
            if (ofd.ShowDialog(this) == DialogResult.OK)
            {
                try
                {
                    this.button_fromImage.Enabled = false;
                    this.label_status.Text = "Importing MIDI from image...";

                    using ImageObj? img = await ImageObj.LoadAsync(ofd.FileName);
                    if (img == null)
                    {
                        throw new Exception("Failed to load image.");
                    }

                    var omr = await ImageToOmrObjParser.ParseAsync(img);
                    if (omr == null)
                    {
                        throw new Exception("Failed to parse image to OMR.");
                    }

                    var midi = OmrToMidiObjConverter.Convert(omr);
                    if (midi == null)
                    {
                        throw new Exception("Failed to convert OMR to MIDI.");
                    }

                    if (!(midi.Tracks.FirstOrDefault()?.Notes.Count > 0))
                    {
                        using var viewDlg = new ImageViewDialog(img);
                        if (viewDlg.ShowDialog(this) != DialogResult.OK || viewDlg.GeneratedMidiFile == null)
                        {
                            this.label_status.Text = "MIDI image import cancelled.";
                            return;
                        }

                        midi = viewDlg.GeneratedMidiFile;
                    }

                    this.mergedTrackIndices.Clear();
                    this.ApplyEdit(midi);
                    this.label_status.Text = $"MIDI imported from {ofd.FileName}";
                }
                catch (Exception ex)
                {
                    LogManager.Log($"MIDI import failed: {ex}");
                    ModularAudience.Forms.Helpers.WindowMainStaticHelpers.ShowErrorWithCopyButton(this, "MIDI import failed", ex);
                }
                finally
                {
                    this.button_fromImage.Enabled = true;
                }
            }
        }
    }
}
