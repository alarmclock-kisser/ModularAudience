using System; // <-- Hinzugefügt für Progress<T>
using System.Windows.Forms; // <-- Hinzugefügt für Form, Timer etc;
using ModularAudience.Audio.Processors_V1;
using ModularAudience.Audio;
using ModularAudience.Audio.Processors_V2;
using MathNet.Numerics;
using System.Threading;
using ModularAudience.Forms.Modules;

namespace ModularAudience.Forms.Modules.Dialogs
{
    public partial class TimeStretchDialog : Form
    {
        internal IEnumerable<AudioObj> Tracks;
        private readonly TrackView? trackView;
        private bool isProcessing;

        /// <summary>
        /// When true the dialog only collects parameters; clicking Stretch / Stretch V2
        /// saves <see cref="ConfirmedSettings"/> and closes without processing any audio.
        /// </summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool IsConfigureMode { get; set; }

        /// <summary>Stretch settings confirmed by the user (populated when <see cref="IsConfigureMode"/> is true).</summary>
        public PlaylistStretchSettings? ConfirmedSettings { get; private set; }

        /// <summary>True if the user confirmed via Stretch V2, false for Stretch V1.</summary>
        public bool ConfirmedUsedV2 { get; private set; }
        public static bool Channeled { get; internal set; } = true;

        private CancellationTokenSource? ProcessingCancellationSource = null;
        private System.Windows.Forms.Timer? ProcessingTimer = null;
        private DateTime ProcessingStarted = DateTime.MinValue;
        private bool isUpdatingInitialBpm;
        private decimal previousInitialBpmValue;
        private bool isUpdatingTargetBpm;
        private decimal previousTargetBpmValue;


        private static float LastTargetBpm = 120f;
        private static float LastInitialBpm = 120f;

        public TimeStretchDialog(TrackView? trackView = null, IEnumerable<AudioObj>? audios = null, IEnumerable<string>? filePaths = null)
        {
            this.InitializeComponent();
            this.previousInitialBpmValue = this.numericUpDown_initialBpm.Value;
            this.previousTargetBpmValue = this.numericUpDown_targetBpm.Value;
            if (audios?.Count() > 0)
            {
                this.Tracks = audios;
            }
            else if (trackView != null)
            {
                this.trackView = trackView;
                this.Tracks = [trackView.OriginalAudio];
            }
            else
            {
                // Close if no valid input
                this.Tracks = [];
                this.Close();
            }

            this.Text = $"Time Stretch - {trackView?.Name ?? this.Tracks.Count() + " Tracks"}";
            if (audios?.Count() == 1)
            {
                this.Text = $"Time Stretch - {audios.First().Name}";
            }
            else if (filePaths?.Any() == true)
            {
                Dictionary<string, float> fileBpms = AudioObj.ReadFilesBpmTags(filePaths);

                this.Text = $"Time Stretch (each) <{fileBpms.Count}> Audio Files";

                float? minBpm = fileBpms.Values.Min();
                float? maxBpm = fileBpms.Values.Max();
                this.Text += $" [{minBpm?.ToString("0.#") ?? "?"} - {maxBpm?.ToString("0.#") ?? "?"} BPM]";
            }
            else
            {
                float? minBpm = this.Tracks.Min(t => t.Bpm > 0 ? t.Bpm : t.ScannedBpm > 30 ? t.ScannedBpm : null);
                float? maxBpm = this.Tracks.Max(t => t.Bpm > 0 ? t.Bpm : t.ScannedBpm > 30 ? t.ScannedBpm : null);
                this.Text += $"[{minBpm?.ToString("0.#") ?? "?"} - {maxBpm?.ToString("0.#") ?? "?"} BPM]";
            }
            this.StartPosition = FormStartPosition.Manual;
            this.Location = WindowsScreenHelper.GetCornerPosition(this, false, false, WindowMain.CurrentScreenId);

            this.numericUpDown_chunkSize.Tag = (int) this.numericUpDown_chunkSize.Value;
            this.numericUpDown_initialBpm.Value = this.GetSafeInitialBpm(this.Tracks.First());
            this.numericUpDown_threads.Minimum = 1;
            this.numericUpDown_threads.Maximum = Math.Max(Environment.ProcessorCount, 1);
            this.numericUpDown_threads.Value = Math.Max(Environment.ProcessorCount / 2, 1);
            this.numericUpDown_targetBpm.Value = (decimal) LastTargetBpm;


            this.FormClosing += this.TimeStretchDialog_FormClosing;
        }

        private void numericUpDown_chunkSize_ValueChanged(object sender, EventArgs e)
        {
            int prev = this.numericUpDown_chunkSize.Tag is int val ? val : 128;
            int curr = (int) this.numericUpDown_chunkSize.Value;

            if (curr > prev)
            {
                this.numericUpDown_chunkSize.Value = Math.Clamp(prev * 2, this.numericUpDown_chunkSize.Minimum, this.numericUpDown_chunkSize.Maximum);
            }
            else
            {
                this.numericUpDown_chunkSize.Value = Math.Clamp(prev / 2, this.numericUpDown_chunkSize.Minimum, this.numericUpDown_chunkSize.Maximum);
            }

            this.numericUpDown_chunkSize.Tag = (int) this.numericUpDown_chunkSize.Value;
        }

        private void numericUpDown_initialBpm_ValueChanged(object? sender, EventArgs e)
        {
            if (this.isUpdatingInitialBpm)
            {
                return;
            }

            decimal currentValue = this.numericUpDown_initialBpm.Value;

            // If ctrl down, double / halve the initial BPM
            if (System.Windows.Forms.Control.ModifierKeys.HasFlag(Keys.Control) && currentValue != this.previousInitialBpmValue)
            {
                decimal adjustedValue = currentValue;

                if (currentValue > this.previousInitialBpmValue)
                {
                    adjustedValue = Math.Min(this.previousInitialBpmValue * 2, this.numericUpDown_initialBpm.Maximum);
                }
                else if (currentValue < this.previousInitialBpmValue)
                {
                    adjustedValue = Math.Max(this.previousInitialBpmValue / 2, this.numericUpDown_initialBpm.Minimum);
                }

                if (adjustedValue != currentValue)
                {
                    this.isUpdatingInitialBpm = true;
                    try
                    {
                        this.numericUpDown_initialBpm.Value = adjustedValue;
                        currentValue = adjustedValue;
                    }
                    finally
                    {
                        this.isUpdatingInitialBpm = false;
                    }
                }
            }

            double factor = (double) currentValue / (double) this.numericUpDown_targetBpm.Value;
            this.numericUpDown_stretchFactor.Value = Math.Clamp((decimal) factor, this.numericUpDown_stretchFactor.Minimum, this.numericUpDown_stretchFactor.Maximum);
            this.previousInitialBpmValue = this.numericUpDown_initialBpm.Value;
            LastInitialBpm = (float) this.numericUpDown_initialBpm.Value;
        }

        private void numericUpDown_targetBpm_ValueChanged(object sender, EventArgs e)
        {
            if (this.isUpdatingTargetBpm)
            {
                return;
            }

            decimal currentValue = this.numericUpDown_targetBpm.Value;

            // If ctrl down, double / halve the target BPM
            if (System.Windows.Forms.Control.ModifierKeys.HasFlag(Keys.Control) && currentValue != this.previousTargetBpmValue)
            {
                decimal adjustedValue = currentValue;

                if (currentValue > this.previousTargetBpmValue)
                {
                    adjustedValue = Math.Min(this.previousTargetBpmValue * 2, this.numericUpDown_targetBpm.Maximum);
                }
                else if (currentValue < this.previousTargetBpmValue)
                {
                    adjustedValue = Math.Max(this.previousTargetBpmValue / 2, this.numericUpDown_targetBpm.Minimum);
                }

                if (adjustedValue != currentValue)
                {
                    this.isUpdatingTargetBpm = true;
                    try
                    {
                        this.numericUpDown_targetBpm.Value = adjustedValue;
                        currentValue = adjustedValue;
                    }
                    finally
                    {
                        this.isUpdatingTargetBpm = false;
                    }
                }
            }

            double factor = (double) this.numericUpDown_initialBpm.Value / (double) currentValue;
            this.numericUpDown_stretchFactor.Value = Math.Clamp((decimal) factor, this.numericUpDown_stretchFactor.Minimum, this.numericUpDown_stretchFactor.Maximum);
            this.previousTargetBpmValue = this.numericUpDown_targetBpm.Value;
            LastTargetBpm = (float) this.numericUpDown_targetBpm.Value;
        }

        private void numericUpDown_stretchFactor_ValueChanged(object sender, EventArgs e)
        {
            double targetBpm = (double) this.numericUpDown_initialBpm.Value / (double) this.numericUpDown_stretchFactor.Value;
            this.numericUpDown_targetBpm.Value = Math.Clamp((decimal) targetBpm, this.numericUpDown_targetBpm.Minimum, this.numericUpDown_targetBpm.Maximum);
        }

        private async void button_stretch_Click(object sender, EventArgs e)
        {
            if (this.IsConfigureMode)
            {
                this.SaveConfirmedSettings(useV2: false);
                this.DialogResult = DialogResult.OK;
                this.Close();
                return;
            }

            if (this.isProcessing)
            {
                return;
            }

            bool closeAfterSuccess = false;
            this.progressBar_stretching.Value = this.progressBar_stretching.Minimum;
            this.SetProcessingState(true);

            try
            {
                var progress = new Progress<double>(percent =>
                {
                    int scaled = (int) Math.Round(percent * this.progressBar_stretching.Maximum);
                    this.progressBar_stretching.Value = Math.Clamp(scaled, this.progressBar_stretching.Minimum, this.progressBar_stretching.Maximum);
                });

                if (this.Tracks.Count() > 1 || this.trackView == null)
                {
                    // Set window title to indicate multi-track processing
                    this.Text = $"Time Stretch - {this.Tracks.Count()} Tracks (Processing...)";

                    for (int i = 0; i < this.Tracks.Count(); i++)
                    {
                        this.Text = $"Time Stretch - {this.Tracks.Count()} Tracks (Processing {i + 1}/{this.Tracks.Count()})";
                        this.numericUpDown_initialBpm.Value = this.checkBox_fixed.Checked ? this.numericUpDown_initialBpm.Value : this.Tracks.ElementAt(i).Bpm > 0 ? (decimal) this.Tracks.ElementAt(i).Bpm : this.Tracks.ElementAt(i).ScannedBpm > 30 ? (decimal) this.Tracks.ElementAt(i).ScannedBpm : (decimal) LastInitialBpm;
                        float originalPeak = await this.Tracks.ElementAt(i).GetPeakAmplitudeAsync((int) this.numericUpDown_threads.Value);
                        await TimeStretcher.TimeStretchAllThreadsAsync(
                                                this.Tracks.ElementAt(i),
                                                (int) this.numericUpDown_chunkSize.Value,
                                                (float) this.numericUpDown_overlap.Value,
                                                (double) this.numericUpDown_stretchFactor.Value < 0.5f ? 2 * (double) this.numericUpDown_stretchFactor.Value : (double) this.numericUpDown_stretchFactor.Value,
                                                keepData: false,
                                                normalize: 1.0f,
                                                maxWorkers: (int) this.numericUpDown_threads.Value,
                                                progress: progress,
                                                offload: this.checkBox_offload.Checked, channeled: this.checkBox_channeled.Checked);

                        if (this.checkBox_trim.Checked)
                        {
                            // Trim silence after stretching
                            await BeatGridFinder.TrimSilenceAsync(this.Tracks.ElementAt(i));
                        }

                        if (originalPeak > 0f)
                        {
                            await this.Tracks.ElementAt(i).NormalizeAsync(originalPeak, (int) this.numericUpDown_threads.Value);
                        }
                    }
                    this.progressBar_stretching.Value = this.progressBar_stretching.Maximum;
                    closeAfterSuccess = true;
                    this.SetProcessingState(false);
                    this.DialogResult = DialogResult.OK;
                    this.Close();
                }
                else
                {
                    if (this.trackView == null)
                    {
                        throw new InvalidOperationException("Kein TrackView zum Anwenden des Time-Stretch gefunden.");
                    }

                    bool resumePlaybackAfterReplace = this.trackView.OriginalAudio.PlayerPlaying;
                    float originalPeak = await this.Tracks.First().GetPeakAmplitudeAsync((int) this.numericUpDown_threads.Value);

                    var result = await TimeStretcher.TimeStretchAllThreadsAsync(
                                            this.Tracks.First(),
                                            (int) this.numericUpDown_chunkSize.Value,
                                            (float) this.numericUpDown_overlap.Value,
                                            (double) this.numericUpDown_stretchFactor.Value < 0.5f ? 2 * (double) this.numericUpDown_stretchFactor.Value : (double) this.numericUpDown_stretchFactor.Value,
                                            keepData: false,
                                            normalize: 0.0f,
                                            maxWorkers: (int) this.numericUpDown_threads.Value,
                                            progress: progress,
                                            offload: this.checkBox_offload.Checked, channeled: this.checkBox_channeled.Checked);

                    if (this.checkBox_trim.Checked)
                    {
                        // Trim silence after stretching
                        await BeatGridFinder.TrimSilenceAsync(result);
                    }

                    if (originalPeak > 0f)
                    {
                        await result.NormalizeAsync(originalPeak, (int) this.numericUpDown_threads.Value);
                    }

                    await this.trackView.OriginalAudio.CreateUndoStepAsync();
                    double stretchFactor = (double) this.numericUpDown_stretchFactor.Value < 0.5f ? 2 * (double) this.numericUpDown_stretchFactor.Value : (double) this.numericUpDown_stretchFactor.Value;
                    await this.trackView.ApplyStretchedAudioAsync(result, stretchFactor, resumePlaybackAfterReplace);
                    this.progressBar_stretching.Value = this.progressBar_stretching.Maximum;
                    closeAfterSuccess = true;
                    this.SetProcessingState(false);
                    this.DialogResult = DialogResult.OK;
                    this.Close();
                }

            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Error Time-Stretching: {ex.Message}", "Time Stretch", MessageBoxButtons.OK, MessageBoxIcon.Error);
                this.progressBar_stretching.Value = this.progressBar_stretching.Minimum;
            }
            finally
            {
                if (!closeAfterSuccess && !this.IsDisposed)
                {
                    this.SetProcessingState(false);
                }
            }
        }

        private void button_cancel_Click(object sender, EventArgs e)
        {
            if (this.isProcessing)
            {
                return;
            }

            this.Close();
        }

        private void SetProcessingState(bool processing)
        {
            this.isProcessing = processing;
            this.button_stretch.Enabled = !processing;
            this.button_cancel.Enabled = !processing;
            this.numericUpDown_chunkSize.Enabled = !processing;
            this.numericUpDown_overlap.Enabled = !processing;
            this.numericUpDown_initialBpm.Enabled = !processing;
            this.numericUpDown_targetBpm.Enabled = !processing;
            this.numericUpDown_stretchFactor.Enabled = !processing;
            // this.UseWaitCursor = processing;
        }

        private void TimeStretchDialog_FormClosing(object? sender, FormClosingEventArgs e)
        {
            if (this.isProcessing)
            {
                e.Cancel = true;
                return;
            }

            try
            {
                this.ProcessingCancellationSource?.Cancel();
            }
            catch { }
        }

        private void SaveConfirmedSettings(bool useV2)
        {
            this.ConfirmedUsedV2 = useV2;
            this.ConfirmedSettings = new PlaylistStretchSettings(
                TargetBpm: (float) this.numericUpDown_targetBpm.Value,
                StretchFactor: (float) this.numericUpDown_stretchFactor.Value,
                ChunkSize: (int) this.numericUpDown_chunkSize.Value,
                Overlap: (float) this.numericUpDown_overlap.Value,
                Threads: (int) this.numericUpDown_threads.Value,
                UseV2: useV2,
                AutoChunking: this.checkBox_autoChunking.Checked,
                Offload: this.checkBox_offload.Checked,
                Channeled: this.checkBox_channeled.Checked,
                Trim: this.checkBox_trim.Checked,
                Fixed: this.checkBox_fixed.Checked
            );
        }

        private async void button_stretchV2_Click(object sender, EventArgs e)
        {
            if (this.IsConfigureMode)
            {
                this.SaveConfirmedSettings(useV2: true);
                this.DialogResult = DialogResult.OK;
                this.Close();
                return;
            }

            if (this.isProcessing)
            {
                // Cancel running processing
                try
                {
                    if (this.ProcessingCancellationSource != null && !this.ProcessingCancellationSource.IsCancellationRequested)
                    {
                        this.ProcessingCancellationSource.Cancel();
                    }
                }
                catch { }

                // Reset button text
                try { this.button_stretchV2.Text = "Stretch V2"; } catch { }
                return;
            }

            // Start processing with cancellation support
            this.ProcessingCancellationSource = new CancellationTokenSource();
            this.ProcessingStarted = DateTime.Now;
            bool closeAfterSuccess = false;
            this.progressBar_stretching.Value = this.progressBar_stretching.Minimum;
            this.SetProcessingState(true);

            try
            {
                if (this.trackView == null)
                {
                    throw new InvalidOperationException("Kein TrackView zum Anwenden des Time-Stretch gefunden.");
                }

                this.ProcessingTimer = new System.Windows.Forms.Timer
                {
                    Interval = 250
                };
                this.ProcessingTimer.Tick += (s, ev) =>
                {
                    if (!this.isProcessing)
                    {
                        this.ProcessingTimer.Stop();
                    }

                    TimeSpan elapsed = DateTime.Now - this.ProcessingStarted;

                    try { this.label_processingTime.Text = elapsed.ToString("mm\\:ss"); } catch { }
                };
                this.ProcessingTimer.Start();

                // Progress mapper: for multi-track, map each track to its portion of the progress bar
                var rawProgress = new Progress<double>(percent =>
                {
                    int scaled = (int) Math.Round(percent * this.progressBar_stretching.Maximum);
                    this.progressBar_stretching.Value = Math.Clamp(scaled, this.progressBar_stretching.Minimum, this.progressBar_stretching.Maximum);
                });

                int total = this.Tracks.Count();
                int index = 0;
                void ReportComposite(double local)
                {
                    double baseStart = (double) index / Math.Max(1, total);
                    double baseEnd = (double) (index + 1) / Math.Max(1, total);
                    double mapped = baseStart + (baseEnd - baseStart) * Math.Clamp(local, 0.0, 1.0);
                    ((IProgress<double>) rawProgress).Report(mapped); // <-- Fix: explizites Interface-Casting
                }

                var perTrackProgress = new Progress<double>(p => ReportComposite(p));

                await this.trackView.OriginalAudio.CreateUndoStepAsync();

                int? chunkSize = this.checkBox_autoChunking.Checked ? null : (int?) this.numericUpDown_chunkSize.Value;
                float? overlap = this.checkBox_autoChunking.Checked ? null : (float?) this.numericUpDown_overlap.Value;

                try { this.button_stretchV2.Text = "Cancel"; } catch { }

                if (total > 1)
                {
                    var results = new List<AudioObj>(total);
                    index = 0;
                    foreach (var t in this.Tracks)
                    {
                        this.numericUpDown_initialBpm.Value = this.checkBox_fixed.Checked ? this.numericUpDown_initialBpm.Value : t.Bpm > 0 ? (decimal) t.Bpm : t.ScannedBpm > 30 ? (decimal) t.ScannedBpm : (decimal) LastInitialBpm;

                        // Process each track in-place with V2
                        await TimeStretcher_V2.Timestretch_V2Async(
                            t,
                            (double) this.numericUpDown_stretchFactor.Value < 0.5f ? 2 * (double) this.numericUpDown_stretchFactor.Value : (double) this.numericUpDown_stretchFactor.Value,
                            chunkSize,
                            overlap,
                            perTrackProgress,
                            this.ProcessingCancellationSource.Token);

                        results.Add(t);
                        index++;
                    }

                    this.Tracks = results;
                    this.progressBar_stretching.Value = this.progressBar_stretching.Maximum;
                    closeAfterSuccess = true;
                    this.SetProcessingState(false);
                    this.DialogResult = DialogResult.OK;
                    this.Close();
                }
                else
                {
                    // Single track: process, then apply to TrackView
                    var track = this.Tracks.First();
                    bool resumePlaybackAfterReplace = this.trackView.OriginalAudio.PlayerPlaying;
                    float originalPeak = await track.GetPeakAmplitudeAsync((int) this.numericUpDown_threads.Value);

                    await TimeStretcher_V2.Timestretch_V2Async(
                        track,
                        (double) this.numericUpDown_stretchFactor.Value < 0.5f ? 2 * (double) this.numericUpDown_stretchFactor.Value : (double) this.numericUpDown_stretchFactor.Value,
                        chunkSize,
                        overlap,
                        perTrackProgress,
                        this.ProcessingCancellationSource.Token);

                    if (originalPeak > 0f)
                    {
                        await track.NormalizeAsync(originalPeak, (int) this.numericUpDown_threads.Value);
                    }

                    double stretchFactor = (double) this.numericUpDown_stretchFactor.Value < 0.5f ? 2 * (double) this.numericUpDown_stretchFactor.Value : (double) this.numericUpDown_stretchFactor.Value;
                    await this.trackView.ApplyStretchedAudioAsync(track, stretchFactor, resumePlaybackAfterReplace);
                    this.progressBar_stretching.Value = this.progressBar_stretching.Maximum;
                    closeAfterSuccess = true;
                    this.SetProcessingState(false);
                    this.DialogResult = DialogResult.OK;
                    this.Close();
                }
            }
            catch (OperationCanceledException)
            {
                // User cancelled - reset progress and UI
                this.progressBar_stretching.Value = this.progressBar_stretching.Minimum;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Fehler beim Time-Stretch: {ex.Message}", "Time Stretch", MessageBoxButtons.OK, MessageBoxIcon.Error);
                this.progressBar_stretching.Value = this.progressBar_stretching.Minimum;
            }
            finally
            {
                try { this.ProcessingTimer?.Stop(); } catch { }
                try { this.ProcessingTimer?.Dispose(); } catch { }
                this.ProcessingTimer = null;

                try { this.ProcessingCancellationSource?.Dispose(); } catch { }
                this.ProcessingCancellationSource = null;

                try { this.button_stretchV2.Text = "Stretch V2"; } catch { }

                if (!closeAfterSuccess && !this.IsDisposed)
                {
                    this.SetProcessingState(false);
                }
            }
        }

        private void checkBox_autoChunking_CheckedChanged(object sender, EventArgs e)
        {
            this.numericUpDown_chunkSize.Enabled = !this.checkBox_autoChunking.Checked;
            this.numericUpDown_overlap.Enabled = !this.checkBox_autoChunking.Checked;
        }

        private decimal GetSafeInitialBpm(AudioObj track)
        {
            decimal bpm = track.Bpm > 0
                ? (decimal) track.Bpm
                : track.ScannedBpm > 0
                    ? (decimal) track.ScannedBpm
                    : (decimal) LastInitialBpm;

            return Math.Clamp(bpm, this.numericUpDown_initialBpm.Minimum, this.numericUpDown_initialBpm.Maximum);
        }

        private void checkBox_channeled_CheckedChanged(object sender, EventArgs e)
        {
            TimeStretchDialog.Channeled = this.checkBox_channeled.Checked;
        }
    }
}
