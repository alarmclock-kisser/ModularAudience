using ModularAudience.Audio;
using ModularAudience.Audio.Midi;
using System.Drawing.Drawing2D;

namespace ModularAudience.Forms.Modules.Dialogs
{
    public partial class MidiWindow : Form
    {
        private MidiFileData midiFile;
        private List<MidiTrackData> tracks;
        private AudioObj? customSample;
        private AudioObj? previewAudio;
        private CancellationTokenSource? previewCts;
        private Point? midiSelectionStart;
        private Point? midiSelectionCurrent;

        public MidiWindow(string? filePath, MidiFileData? midiFileData = null)
        {
            this.InitializeComponent();
            this.midiFile = midiFileData ?? MidiFileData.Load(filePath ?? throw new ArgumentNullException(nameof(filePath)));
            this.tracks = this.midiFile.Tracks.ToList();
            this.Text = $"MIDI Renderer - {Path.GetFileNameWithoutExtension(this.midiFile.FilePath)}";
            this.numericUpDown_track.Maximum = Math.Max(1, this.tracks.Count);
            this.numericUpDown_track.Value = 1;
            this.numericUpDown_bpm.Value = (decimal) Math.Clamp(this.midiFile.DefaultBpm, 20.0, 400.0);
            this.comboBox_instrument.Items.AddRange([
            "Sine", "Saw", "Square", "Triangle", "Noise", "Pluck / Karplus-Strong", "Custom Sample"]);
            this.comboBox_instrument.SelectedIndex = 0;
            this.label_status.Text = $"Tracks: {this.tracks.Count}, Notes: {this.tracks.Sum(track => track.Notes.Count)}";
        }

        private MidiTrackData SelectedTrack => this.tracks[Math.Clamp((int) this.numericUpDown_track.Value - 1, 0, this.tracks.Count - 1)];

        public MidiInstrument SelectedInstrument => (MidiInstrument) Math.Clamp(this.comboBox_instrument.SelectedIndex, 0, 6);

        public double PreviewBpm => (double) this.numericUpDown_bpm.Value;

        private void pictureBox_midi_Paint(object? sender, PaintEventArgs e)
        {
            MidiTrackData track = this.SelectedTrack;
            e.Graphics.SmoothingMode = SmoothingMode.HighQuality;
            e.Graphics.Clear(Color.FromArgb(24, 24, 28));
            int left = 38;
            int top = 12;
            int width = Math.Max(1, this.pictureBox_midi.ClientSize.Width - left - 12);
            int height = Math.Max(1, this.pictureBox_midi.ClientSize.Height - top - 16);
            using Pen gridPen = new(Color.FromArgb(48, 48, 56));
            using Font font = new("Segoe UI", 8f);
            for (int note = 0; note <= 127; note += 12)
            {
                int y = top + height - (int) (note / 127.0 * height);
                e.Graphics.DrawLine(gridPen, left, y, left + width, y);
                e.Graphics.DrawString(MidiNoteName(note), font, Brushes.Gainsboro, 2, y - 7);
            }

            long lengthTicks = Math.Max(1, track.LengthTicks);
            foreach (MidiNoteData note in track.Notes)
            {
                float x = left + (float) (note.StartTick / (double) lengthTicks * width);
                float noteWidth = Math.Max(2f, (float) (note.DurationTicks / (double) lengthTicks * width));
                float y = top + height - (float) ((note.NoteNumber + 1) / 128.0 * height);
                float noteHeight = Math.Max(3f, height / 128f + 1f);
                using Brush brush = new SolidBrush(ChannelColor(note.Channel));
                e.Graphics.FillRectangle(brush, x, y, Math.Min(noteWidth, left + width - x), noteHeight);
            }

            if (this.previewAudio?.PlayerPlaying == true && this.previewAudio.Duration > TimeSpan.Zero)
            {
                double progress = Math.Clamp(this.previewAudio.CurrentTime.TotalSeconds / this.previewAudio.Duration.TotalSeconds, 0.0, 1.0);
                float caretX = left + (float) (progress * width);
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

        public async Task<AudioObj> RenderMidiAsync(MidiFileData midi, int trackIndex, CancellationToken cancellationToken = default, double pitchFrequency = 440.0)
        {
            MidiInstrument instrument = this.SelectedInstrument;
            double bpm = (double) this.numericUpDown_bpm.Value;
            AudioObj? selectedCustomSample = this.customSample;
            return await Task.Run(
                () => MidiAudioRenderer.Render(midi, trackIndex, instrument, bpm, selectedCustomSample, cancellationToken: cancellationToken, pitchFrequency: pitchFrequency),
                cancellationToken);
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
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            this.midiSelectionStart = ClampToMidiCanvas(e.Location);
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

            this.midiSelectionCurrent = ClampToMidiCanvas(e.Location);
            this.pictureBox_midi.Invalidate();
        }

        private void pictureBox_midi_MouseUp(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || this.midiSelectionStart is not Point start)
            {
                return;
            }

            Point end = ClampToMidiCanvas(e.Location);
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
            long startTick = (long) Math.Floor((rectangle.Left - left) / (double) width * Math.Max(1, track.LengthTicks));
            long endTick = (long) Math.Ceiling((rectangle.Right - left) / (double) width * Math.Max(1, track.LengthTicks));
            int lowestNote = Math.Clamp((int) Math.Floor((top + height - rectangle.Bottom) / (double) height * 128), 0, 127);
            int highestNote = Math.Clamp((int) Math.Ceiling((top + height - rectangle.Top) / (double) height * 128) - 1, 0, 127);
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

        private void numericUpDown_track_ValueChanged(object? sender, EventArgs e) => this.pictureBox_midi.Invalidate();

        private void numericUpDown_bpm_ValueChanged(object? sender, EventArgs e) => this.label_status.Text = $"BPM: {this.numericUpDown_bpm.Value:0.00}";

        private void comboBox_instrument_SelectedIndexChanged(object? sender, EventArgs e)
        {
            this.button_customInstrument.Enabled = this.SelectedInstrument == MidiInstrument.CustomSample;
            if (this.SelectedInstrument != MidiInstrument.CustomSample)
            {
                this.customSample = null;
            }
        }

        private void button_customInstrument_Click(object? sender, EventArgs e)
        {
            ContextMenuStrip menu = new();
            foreach (AudioObj audio in WindowMain.CollectionViews.Where(view => !view.IsDisposed).SelectMany(view => view.AudioC.Audios).DistinctBy(audio => audio.Id))
            {
                AudioObj selected = audio;
                menu.Items.Add(new ToolStripMenuItem(selected.Name, null, (_, _) =>
                {
                    this.customSample = selected;
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

            CancellationTokenSource? runCts = null;
            try
            {
                this.button_preview.Enabled = false;
                this.label_status.Text = "Rendering preview...";
                runCts = new CancellationTokenSource();
                this.previewCts = runCts;
                MidiInstrument instrument = this.SelectedInstrument;
                double bpm = (double) this.numericUpDown_bpm.Value;
                AudioObj audio = await Task.Run(() => MidiAudioRenderer.Render(this.midiFile, this.SelectedTrack.Index, instrument, bpm, this.customSample, cancellationToken: runCts.Token, pitchFrequency: this.midiFile.PitchFrequency), runCts.Token);
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
                LogCollection.Log($"MIDI preview failed: {ex}");
                MessageBox.Show(this, ex.Message, "MIDI preview failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                this.ResetPreviewState();
            }
            finally
            {
                if (ReferenceEquals(this.previewCts, runCts))
                {
                    this.ResetPreviewState();
                }
                else
                {
                    runCts?.Dispose();
                }
            }
        }

        private async void button_save_Click(object? sender, EventArgs e)
        {
            try
            {
                this.button_save.Enabled = false;
                this.label_status.Text = "Rendering MIDI...";
                MidiFileData midi = this.midiFile;
                int trackIndex = this.SelectedTrack.Index;
                MidiInstrument instrument = this.SelectedInstrument;
                double bpm = (double) this.numericUpDown_bpm.Value;
                AudioObj? customSample = this.customSample;
                AudioObj audio = await Task.Run(() => MidiAudioRenderer.Render(midi, trackIndex, instrument, bpm, customSample, pitchFrequency: midi.PitchFrequency));
                WindowMain.Instance?.PlaceRenderedAudio(audio);
                this.label_status.Text = "Audio object saved";
            }
            catch (Exception ex)
            {
                LogCollection.Log($"MIDI render failed: {ex}");
                MessageBox.Show(this, ex.Message, "MIDI rendering failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                this.button_save.Enabled = true;
            }
        }

        public async Task StopPreviewAsync()
        {
            this.previewCts?.Cancel();
            if (this.previewAudio != null) await this.previewAudio.StopAsync();
            this.ResetPreviewState();
        }

        public async Task PreviewMidiAsync(MidiFileData midi, int trackIndex)
        {
            await this.StopPreviewAsync();
            MidiInstrument instrument = this.SelectedInstrument;
            double bpm = (double) this.numericUpDown_bpm.Value;
            AudioObj? customSample = this.customSample;
            CancellationTokenSource? runCts = null;
            try
            {
                runCts = new CancellationTokenSource();
                this.previewCts = runCts;
                AudioObj audio = await Task.Run(() => MidiAudioRenderer.Render(midi, trackIndex, instrument, bpm, customSample, cancellationToken: runCts.Token, pitchFrequency: midi.PitchFrequency), runCts.Token);
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
                else
                {
                    runCts?.Dispose();
                }
            }
        }

        private void ResetPreviewState()
        {
            this.timer_previewCaret.Stop();
            this.previewCts?.Dispose();
            this.previewCts = null;
            this.previewAudio?.Dispose();
            this.previewAudio = null;
            this.button_preview.Text = "Preview";
            this.button_preview.Enabled = true;
            this.label_status.Text = "Ready";
        }

        private void timer_previewCaret_Tick(object? sender, EventArgs e) => this.pictureBox_midi.Invalidate();

        private void MidiWindow_Resize(object? sender, EventArgs e) => this.pictureBox_midi.Invalidate();

        private async void MidiWindow_FormClosing(object? sender, FormClosingEventArgs e) => await this.StopPreviewAsync();

        public void ApplyEdit(MidiFileData midiFileData)
        {
            this.midiFile = midiFileData;
            this.tracks = this.midiFile.Tracks.ToList();
            this.numericUpDown_track.Maximum = Math.Max(1, this.tracks.Count);
            this.numericUpDown_track.Value = 1;
            this.numericUpDown_bpm.Value = (decimal) Math.Clamp(this.midiFile.DefaultBpm, 20.0, 400.0);
            this.label_status.Text = $"Tracks: {this.tracks.Count}, Notes: {this.tracks.Sum(track => track.Notes.Count)}";
            this.label_status.Enabled = false;
            this.pictureBox_midi.Invalidate();

        }

        private async void button_export_Click(object sender, EventArgs e)
        {
            // SFD at MyMusic for MIDI file (*mid) export
            using SaveFileDialog sfd = new()
            {
                Title = "Export MIDI File",
                Filter = "MIDI Files (*.mid)|*.mid",
                DefaultExt = "mid",
                AddExtension = true,
                FileName = Path.GetFileNameWithoutExtension(this.midiFile.FilePath) + "_exported.mid"
            };

            if (sfd.ShowDialog(this) == DialogResult.OK)
            {
                try
                {
                    this.button_export.Enabled = false;
                    this.label_status.Text = "Exporting MIDI...";
                    string? exportedPath = await this.midiFile.ExportAsync(sfd.FileName);
                    this.label_status.Text = $"MIDI exported to {exportedPath}";
                }
                catch (Exception ex)
                {
                    LogCollection.Log($"MIDI export failed: {ex}");
                    MessageBox.Show(this, ex.Message, "MIDI export failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    this.button_export.Enabled = true;
                }
            }
        }
    }
}