using ModularAudience.Audio.Processors_V1;
using NAudience.Core;
using System;
using System.ComponentModel;
using System.Threading;
using System.Windows.Forms;

namespace ModularAudience.Forms.Modules.Dialogs
{
    public partial class AutoSamplesDialog : Form
    {
        internal readonly AudioObj OriginalAudio;

        public readonly BindingList<AudioObj> ResultSamples = [];

		internal int CutMinDuration => (int) this.numericUpDown_minDuration.Value;
        internal int CutMaxDuration => (int) this.numericUpDown_maxDuration.Value;
        internal int CutSilenceDuration => (int) this.numericUpDown_silenceDuration.Value;

        private CancellationTokenSource? cuttingCts;


        public AutoSamplesDialog(AudioObj audio)
        {
            this.InitializeComponent();
            this.OriginalAudio = audio.Clone();

            this.progressBar_cutting.Minimum = 0;
            this.progressBar_cutting.Maximum = 100;
            this.progressBar_cutting.Value = 0;
            this.label_status.Text = "Ready to cut samples";
		}

        private async void button_cut_Click(object sender, EventArgs e)
        {
            if (this.cuttingCts != null)
            {
                return;
            }

            this.ResultSamples.Clear();
            this.ToggleInputs(false);
            this.cuttingCts = new CancellationTokenSource();
            var progress = new Progress<double>(value =>
            {
                double clamped = Math.Max(0d, Math.Min(1d, value));
                int percent = (int) Math.Round(clamped * 100d);
                percent = Math.Max(this.progressBar_cutting.Minimum, Math.Min(this.progressBar_cutting.Maximum, percent));
                this.progressBar_cutting.Value = percent;
                this.label_status.Text = $"Processing… {percent}%";
            });

            try
            {
                var clips = await AutoSampleCutter.CutAutoSamplesAsync(
                    this.OriginalAudio,
                    this.CutMinDuration,
                    this.CutMaxDuration,
                    this.CutSilenceDuration,
                    progress,
                    this.cuttingCts.Token).ConfigureAwait(true);

                foreach (var clip in clips)
                {
                    this.ResultSamples.Add(clip);
                }

                if (this.ResultSamples.Count == 0)
                {
                    this.label_status.Text = "No regions detected – adjust settings.";
                    MessageBox.Show(this, "No regions matched the current thresholds. Try lowering the minimum duration or silence requirement.", "Auto Samples", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    this.label_status.Text = $"{this.ResultSamples.Count} samples ready";
                    this.DialogResult = DialogResult.OK;
                    this.Close();
                }
            }
            catch (OperationCanceledException)
            {
                this.label_status.Text = "Processing canceled";
            }
            catch (Exception ex)
            {
                this.label_status.Text = "Processing failed";
                MessageBox.Show(this, $"Auto sample cutting failed:{Environment.NewLine}{ex.Message}", "Auto Samples", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                this.cuttingCts?.Dispose();
                this.cuttingCts = null;
                this.progressBar_cutting.Value = 0;
                this.ToggleInputs(true);
            }
        }

        private void button_cancel_Click(object sender, EventArgs e)
        {
            if (this.cuttingCts != null)
            {
                this.cuttingCts.Cancel();
                return;
            }

            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try { this.cuttingCts?.Cancel(); } catch { }
            this.cuttingCts?.Dispose();
            this.cuttingCts = null;
            try { this.OriginalAudio.Dispose(); } catch { }
            base.OnFormClosed(e);
        }

        private void ToggleInputs(bool enabled)
        {
            this.button_cut.Enabled = enabled;
            this.numericUpDown_minDuration.Enabled = enabled;
            this.numericUpDown_maxDuration.Enabled = enabled;
            this.numericUpDown_silenceDuration.Enabled = enabled;
            this.button_cancel.Text = enabled ? "Cancel" : "Stop";
        }
    }
}
