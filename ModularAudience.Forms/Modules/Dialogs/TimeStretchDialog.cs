using ModularAudience.Audio.Processors_V1;
using ModularAudience.Audio;
using ModularAudience.Audio.Processors_V2;
using MathNet.Numerics;
using System.Threading;

namespace ModularAudience.Forms.Modules.Dialogs
{
    public partial class TimeStretchDialog : Form
    {
        internal AudioObj Track;
        private readonly TrackView trackView;
        private bool isProcessing;


        private CancellationTokenSource? ProcessingCancellationSource = null;
        private System.Windows.Forms.Timer? ProcessingTimer = null;
        private DateTime ProcessingStarted = DateTime.MinValue;


        private static float LastTargetBpm = 120f;

        public TimeStretchDialog(TrackView trackView)
        {
            this.InitializeComponent();
            this.trackView = trackView;
            this.Track = trackView.OriginalAudio.Clone();

            this.Text = $"Time Stretch - {trackView.Name}";
            this.StartPosition = FormStartPosition.Manual;
            this.Location = WindowsScreenHelper.GetCornerPosition(this, false, false);

            this.numericUpDown_chunkSize.Tag = (int) this.numericUpDown_chunkSize.Value;
            this.numericUpDown_initialBpm.Value = this.Track.Bpm > 0 ? (decimal) this.Track.Bpm : this.Track.ScannedBpm > 30 ? (decimal) this.Track.ScannedBpm : 120;
            this.numericUpDown_threads.Minimum = 1;
            this.numericUpDown_threads.Maximum = Math.Max(Environment.ProcessorCount, 1);
            this.numericUpDown_threads.Value = Math.Max(Environment.ProcessorCount - 1, 1);
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

        private void numericUpDown_initialBpm_ValueChanged(object sender, EventArgs e)
        {
            double factor = (double) this.numericUpDown_initialBpm.Value / (double) this.numericUpDown_targetBpm.Value;
            this.numericUpDown_stretchFactor.Value = Math.Clamp((decimal) factor, this.numericUpDown_stretchFactor.Minimum, this.numericUpDown_stretchFactor.Maximum);
        }

        private void numericUpDown_targetBpm_ValueChanged(object sender, EventArgs e)
        {
            double factor = (double) this.numericUpDown_initialBpm.Value / (double) this.numericUpDown_targetBpm.Value;
            this.numericUpDown_stretchFactor.Value = Math.Clamp((decimal) factor, this.numericUpDown_stretchFactor.Minimum, this.numericUpDown_stretchFactor.Maximum);
            LastTargetBpm = (float) this.numericUpDown_targetBpm.Value;
        }

        private void numericUpDown_stretchFactor_ValueChanged(object sender, EventArgs e)
        {
            double targetBpm = (double) this.numericUpDown_initialBpm.Value / (double) this.numericUpDown_stretchFactor.Value;
            this.numericUpDown_targetBpm.Value = Math.Clamp((decimal) targetBpm, this.numericUpDown_targetBpm.Minimum, this.numericUpDown_targetBpm.Maximum);
        }

        private async void button_stretch_Click(object sender, EventArgs e)
        {
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

                var result = await TimeStretcher.TimeStretchAllThreadsAsync(
                    this.Track,
                    (int) this.numericUpDown_chunkSize.Value,
                    (float) this.numericUpDown_overlap.Value,
                    (double) this.numericUpDown_stretchFactor.Value,
                    keepData: false,
                    normalize: 1.0f,
                    maxWorkers: (int) this.numericUpDown_threads.Value,
                    progress: progress);

                await this.trackView.OriginalAudio.CreateUndoStepAsync();
                await this.trackView.ApplyStretchedAudioAsync(result);
                this.progressBar_stretching.Value = this.progressBar_stretching.Maximum;
                closeAfterSuccess = true;
                this.SetProcessingState(false);
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Fehler beim Time-Stretch: {ex.Message}", "Time Stretch", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            this.UseWaitCursor = processing;
        }

        private void TimeStretchDialog_FormClosing(object? sender, FormClosingEventArgs e)
        {
            if (this.isProcessing)
            {
                e.Cancel = true;
                return;
            }

            try { this.Track.Dispose(); } catch { }
        }

        private async void button_stretchV2_Click(object sender, EventArgs e)
        {
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

                var progress = new Progress<double>(percent =>
                {
                    int scaled = (int) Math.Round(percent * this.progressBar_stretching.Maximum);
                    this.progressBar_stretching.Value = Math.Clamp(scaled, this.progressBar_stretching.Minimum, this.progressBar_stretching.Maximum);
                });

                await this.trackView.OriginalAudio.CreateUndoStepAsync();

                int? chunkSize = this.checkBox_autoChunking.Checked ? null : (int?) this.numericUpDown_chunkSize.Value;
                float? overlap = this.checkBox_autoChunking.Checked ? null : (float?) this.numericUpDown_overlap.Value;

                try { this.button_stretchV2.Text = "Cancel"; } catch { }

                await TimeStretcher_V2.Timestretch_V2Async(
                    this.Track,
                    (double) this.numericUpDown_stretchFactor.Value,
                    chunkSize, overlap,
                    progress: progress,
                    ct: this.ProcessingCancellationSource.Token);

                await this.trackView.ApplyStretchedAudioAsync(this.Track);

                this.progressBar_stretching.Value = this.progressBar_stretching.Maximum;
                closeAfterSuccess = true;
                this.SetProcessingState(false);
                this.DialogResult = DialogResult.OK;
                this.Close();
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
    }
}
