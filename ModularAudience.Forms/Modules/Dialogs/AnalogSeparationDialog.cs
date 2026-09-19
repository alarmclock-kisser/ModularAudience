using ModularAudience.Audio;
using ModularAudience.Audio.Processors_V4;
using ModularAudience.Forms.Helpers;
using ModularAudience.Forms.Modules;
using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace ModularAudience.Forms
{
    public partial class AnalogSeparationDialog : Form
    {
        private readonly AudioObj OriginalAudio;
        private readonly List<BandRow> bandRows = [];
        private readonly System.Windows.Forms.Timer selectionStateTimer;
        private bool operationRunning;

        private sealed class BandRow
        {
            public TextBox NameBox = null!;
            public NumericUpDown LowBox = null!;
            public NumericUpDown HighBox = null!;
            public Button RemoveButton = null!;
        }

        public AnalogSeparationDialog(AudioObj audioObj)
        {
            this.InitializeComponent();
            this.OriginalAudio = audioObj;
            this.selectionStateTimer = new System.Windows.Forms.Timer { Interval = 200 };
            this.selectionStateTimer.Tick += (_, _) => this.UpdateSelectionButtonState();
            this.FormClosed += (_, _) => this.selectionStateTimer.Dispose();
            this.selectionStateTimer.Start();

            int processorCount = Environment.ProcessorCount;
            this.numeric_threads.Value = Math.Clamp(processorCount / 2, 1, processorCount);

            this.AddBandRow("SubBass", 20, 120);
            this.AddBandRow("Bass", 120, 250);
            this.AddBandRow("LowMid", 250, 2000);
            this.AddBandRow("Mid", 2000, 4000);
            this.AddBandRow("High", 4000, 20000);
            this.UpdateSelectionButtonState();
        }

        private void AddBandRow(string name, double lowHz, double highHz)
        {
            var row = new BandRow();

            row.NameBox = new TextBox
            {
                Location = new Point(0, 0),
                Size = new Size(120, 23),
                Text = name
            };

            row.LowBox = new NumericUpDown
            {
                Location = new Point(128, 0),
                Size = new Size(70, 23),
                Minimum = 1,
                Maximum = 24000,
                Value = (decimal) Math.Clamp(lowHz, 1, 24000)
            };

            row.HighBox = new NumericUpDown
            {
                Location = new Point(206, 0),
                Size = new Size(70, 23),
                Minimum = 1,
                Maximum = 24000,
                Value = (decimal) Math.Clamp(highHz, 1, 24000)
            };

            row.RemoveButton = new Button
            {
                Location = new Point(284, 0),
                Size = new Size(30, 23),
                Text = "✖"
            };
            row.RemoveButton.Click += (_, _) => this.RemoveBandRow(row);

            var container = new Panel
            {
                Size = new Size(320, 27),
                Margin = new Padding(0, 0, 0, 4)
            };
            container.Controls.Add(row.NameBox);
            container.Controls.Add(row.LowBox);
            container.Controls.Add(row.HighBox);
            container.Controls.Add(row.RemoveButton);

            this.bandRows.Add(row);
            this.flowLayoutPanel_bands.Controls.Add(container);
        }

        private void RemoveBandRow(BandRow row)
        {
            if (this.bandRows.Count <= 1)
            {
                return;
            }

            this.flowLayoutPanel_bands.Controls.Remove(row.RemoveButton.Parent!);
            this.bandRows.Remove(row);
        }

        private void button_AddBand_Click(object sender, EventArgs e)
        {
            double lastHigh = this.bandRows.Count > 0 ? (double) this.bandRows[^1].HighBox.Value : 20;
            double newLow = Math.Min(lastHigh, 23999);
            double newHigh = Math.Min(newLow + 1000, 24000);
            this.AddBandRow($"Band {this.bandRows.Count + 1}", newLow, newHigh);
        }

        private TrackView? GetSelectedTrackView()
        {
            TrackView? trackView = WindowMain.LastSelectedTrackView;
            if (trackView == null || trackView.IsDisposed || trackView.Disposing)
            {
                return null;
            }

            AudioObj audio = trackView.OriginalAudio;
            if (audio.Data == null || audio.Data.Length == 0 || audio.Channels <= 0 ||
                audio.SelectionStart < 0 || audio.SelectionEnd <= audio.SelectionStart)
            {
                return null;
            }

            return trackView;
        }

        private void UpdateSelectionButtonState()
        {
            this.button_fromSelection.Enabled = !this.operationRunning && this.GetSelectedTrackView() != null;
        }

        private void SetOperationRunning(bool running)
        {
            this.operationRunning = running;
            this.button_autoBands.Enabled = !running;
            this.button_addBand.Enabled = !running;
            this.button_run.Enabled = !running;
            this.UpdateSelectionButtonState();
        }

        private void ReplaceBandRows(IReadOnlyList<AnalogSeparationBand> bands)
        {
            while (this.flowLayoutPanel_bands.Controls.Count > 0)
            {
                Control control = this.flowLayoutPanel_bands.Controls[0];
                this.flowLayoutPanel_bands.Controls.RemoveAt(0);
                control.Dispose();
            }

            this.bandRows.Clear();
            foreach (var band in bands)
            {
                this.AddBandRow(band.Name, band.LowHz, band.HighHz);
            }
        }

        private async void button_AutoBands_Click(object sender, EventArgs e)
        {
            this.SetOperationRunning(true);
            this.progressBar_separating.Style = ProgressBarStyle.Marquee;
            this.label_status.Text = "Analyzing spectrum...";

            try
            {
                var bands = await AnalogSeparationProcessor.AnalyzeBandsAsync(
                    this.OriginalAudio,
                    maxBands: 8,
                    windowSize: (int) this.numeric_windowSize.Value,
                    threads: Math.Clamp((int) this.numeric_threads.Value, 1, Environment.ProcessorCount));

                if (bands.Count == 0)
                {
                    this.label_status.Text = "No usable spectrum found.";
                    return;
                }

                this.ReplaceBandRows(bands);

                this.label_status.Text = $"Suggested {bands.Count} bands.";
            }
            catch (Exception ex)
            {
                this.label_status.Text = "Analysis failed.";
                WindowMainStaticHelpers.ShowErrorWithCopyButton(this, "Automatic Band Analysis Failed", ex);
            }
            finally
            {
                this.progressBar_separating.Style = ProgressBarStyle.Blocks;
                this.SetOperationRunning(false);
            }
        }

        private async void button_FromSelection_Click(object sender, EventArgs e)
        {
            TrackView? trackView = this.GetSelectedTrackView();
            if (trackView == null)
            {
                this.UpdateSelectionButtonState();
                return;
            }

            AudioObj selectedAudio = trackView.OriginalAudio;
            this.SetOperationRunning(true);
            this.progressBar_separating.Style = ProgressBarStyle.Marquee;
            this.label_status.Text = "Analyzing selection...";

            try
            {
                var bands = await AnalogSeparationProcessor.AnalyzeSelectionBandsAsync(
                    selectedAudio,
                    selectedAudio.SelectionStart,
                    selectedAudio.SelectionEnd,
                    maxBands: 8,
                    windowSize: (int) this.numeric_windowSize.Value,
                    threads: Math.Clamp((int) this.numeric_threads.Value, 1, Environment.ProcessorCount));

                if (bands.Count == 0)
                {
                    this.label_status.Text = "No usable spectrum found in selection.";
                    return;
                }

                int addedBandCount = 0;
                foreach (var band in bands)
                {
                    bool alreadyExists = this.bandRows.Any(row =>
                        (double) row.LowBox.Value == band.LowHz &&
                        (double) row.HighBox.Value == band.HighHz);
                    if (alreadyExists)
                    {
                        continue;
                    }

                    this.AddBandRow(band.Name, band.LowHz, band.HighHz);
                    addedBandCount++;
                }

                this.label_status.Text = addedBandCount > 0
                    ? $"Added {addedBandCount} bands from selection."
                    : "Selection bands already exist.";
            }
            catch (Exception ex)
            {
                this.label_status.Text = "Selection analysis failed.";
                WindowMainStaticHelpers.ShowErrorWithCopyButton(this, "Selection Band Analysis Failed", ex);
            }
            finally
            {
                this.progressBar_separating.Style = ProgressBarStyle.Blocks;
                this.SetOperationRunning(false);
            }
        }

        private List<AnalogSeparationBand> GetBands()
        {
            List<AnalogSeparationBand> bands = [];
            foreach (var row in this.bandRows)
            {
                string name = string.IsNullOrWhiteSpace(row.NameBox.Text) ? "Band" : row.NameBox.Text.Trim();
                bands.Add(new AnalogSeparationBand(name, (double) row.LowBox.Value, (double) row.HighBox.Value));
            }

            return bands;
        }

        private async void button_Run_Click(object sender, EventArgs e)
        {
            var bands = this.GetBands();
            foreach (var band in bands)
            {
                if (band.LowHz >= band.HighHz)
                {
                    MessageBox.Show(this, $"Band '{band.Name}' has an invalid range (low >= high).", "Invalid Band", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            var settings = new AnalogSeparationSettings(
                (int) this.numeric_windowSize.Value,
                (double) this.numeric_overlap.Value / 100.0,
                (int) this.numeric_sharpness.Value,
                this.checkBox_subtract.Checked,
                Math.Clamp((int) this.numeric_threads.Value, 1, Environment.ProcessorCount),
                this.checkBox_timbreAware.Checked,
                this.checkBox_harmonicAware.Checked);

            this.SetOperationRunning(true);
            this.progressBar_separating.Value = 0;
            this.progressBar_separating.Maximum = 1000;
            this.label_status.Text = "Separating...";

            IProgress<double> progress = new Progress<double>(percent =>
            {
                this.progressBar_separating.Value = Math.Clamp((int) (percent * this.progressBar_separating.Maximum), 0, this.progressBar_separating.Maximum);
            });

            try
            {
                var result = await AnalogSeparationProcessor.SeparateAsync(this.OriginalAudio, bands, settings, progress);

                if (result.Stems.Count == 0)
                {
                    this.label_status.Text = "No stems produced.";
                    return;
                }

                string collectionTitle = $"Separated x{result.Stems.Count} - {this.OriginalAudio.Name}";
                AudioCollectionView acv = new([], collectionTitle);
                this.Invoke(() =>
                {
                    foreach (var stem in result.Stems)
                    {
                        acv.AudioC.Audios.Add(stem);
                    }
                });

                acv.Show();
                this.label_status.Text = "Done.";
            }
            catch (Exception ex)
            {
                this.label_status.Text = "Failed.";
                Exception root = ex is AggregateException agg && agg.InnerExceptions.Count > 0 ? agg.InnerExceptions[0] : ex;
                WindowMainStaticHelpers.ShowErrorWithCopyButton(this, "Analog Separation Failed", root);
            }
            finally
            {
                this.SetOperationRunning(false);
            }
        }
    }
}
