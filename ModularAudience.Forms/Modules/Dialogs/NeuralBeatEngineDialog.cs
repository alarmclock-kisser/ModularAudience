using ModularAudience.Audio;
using ModularAudience.Generators;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ModularAudience.Forms.Modules.Dialogs
{
    public partial class NeuralBeatEngineDialog : Form
    {
        internal readonly AudioCollection Audios = new();

        private AsyncBeatGraphEngine? engine;
        private bool[][]? currentPattern;
        private string currentNodeId = "root";
        private CancellationTokenSource? generationCancellationTokenSource;
        private CancellationTokenSource? playbackCancellationTokenSource;
        private AudioObj? renderedBeat;
        private AudioCollectionView? exportedCollectionView;
        private bool feedbackGiven;
        private bool isGenerating;
        private bool isPlaying;

        public NeuralBeatEngineDialog(IEnumerable<AudioObj>? audios = null)
        {
            this.InitializeComponent();

            foreach (AudioObj audio in audios ?? [])
            {
                this.Audios.Audios.Add(audio.Clone());
            }

            this.listBox_samples.DataSource = this.Audios.Audios;
            this.listBox_samples.SelectedIndex = this.listBox_samples.Items.Count > 0 ? 0 : -1;
            this.numericUpDown_threadCount.Value = Math.Clamp(Environment.ProcessorCount, (int)this.numericUpDown_threadCount.Minimum, (int)this.numericUpDown_threadCount.Maximum);
            alarmclockkisser.DragNDrop.Forms.ListBoxExtensions.Register_ListBox_DragNDrop(this.listBox_samples, false);

            this.StartPosition = FormStartPosition.Manual;
            this.Location = WindowsScreenHelper.GetCornerPosition(this, false, false, WindowMain.CurrentScreenId);
        }

        private BeatEngineConfig CreateConfig()
        {
            if (this.Audios.Audios.Count < 2)
            {
                throw new InvalidOperationException("Add at least two samples before creating the engine.");
            }

            float minWeight = (float)this.numericUpDown_minWeight.Value;
            float maxWeight = (float)this.numericUpDown_maxWeight.Value;
            if (minWeight >= maxWeight)
            {
                throw new InvalidOperationException("Min weight must be lower than max weight.");
            }

            return new BeatEngineConfig
            {
                SampleCount = this.Audios.Audios.Count,
                Bars = (int)this.numericUpDown_bars.Value,
                BeatsPerBar = (int)this.numericUpDown_beatsPerBar.Value,
                StepsPerBeat = (int)this.numericUpDown_stepsPerBeat.Value,
                LearningRate = (float)this.numericUpDown_learningRate.Value,
                Temperature = (float)this.numericUpDown_temperature.Value,
                WeightDecay = (float)this.numericUpDown_weightDecay.Value,
                MinWeight = minWeight,
                MaxWeight = maxWeight,
                ThreadCount = (int)this.numericUpDown_threadCount.Value,
                Interleaved = (int)this.numericUpDown_interleaved.Value
            };
        }

        private async void button_generate_Click(object sender, EventArgs e)
        {
            await this.GenerateSequenceAsync("root", createEngine: true);
        }

        private async Task GenerateSequenceAsync(string nodeId, bool createEngine)
        {
            try
            {
                this.CancelGeneration();
                this.generationCancellationTokenSource = new CancellationTokenSource();

                if (createEngine)
                {
                    this.engine = new AsyncBeatGraphEngine(this.CreateConfig());
                    this.currentNodeId = "root";
                }

                if (this.engine is null)
                {
                    throw new InvalidOperationException("Create the engine before generating a beat.");
                }

                this.SetGenerationControlsEnabled(false);
                this.SetFeedbackEnabled(false);
                this.feedbackGiven = false;
                this.label_status.Text = "Generating sequence...";
                this.currentPattern = await this.engine.GeneratePatternAsync(nodeId, null, this.generationCancellationTokenSource.Token);
                this.currentNodeId = nodeId;
                await this.RenderCurrentPatternAsync();
                this.StartPlayback();
                this.button_remix.Enabled = true;
            }
            catch (OperationCanceledException)
            {
                this.label_status.Text = "Generation stopped.";
            }
            catch (Exception ex)
            {
                LogCollection.Log($"Neural beat generation failed: {ex}");
                this.label_status.Text = "Generation failed. See log for details.";
                MessageBox.Show(this, ex.Message, "Neural Beat Engine", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                this.generationCancellationTokenSource?.Dispose();
                this.generationCancellationTokenSource = null;
                this.SetGenerationControlsEnabled(true);
            }
        }

        private void SetGenerationControlsEnabled(bool enabled)
        {
            this.isGenerating = !enabled;
            this.button_generate.Enabled = !this.isGenerating && !this.isPlaying;
            this.button_stop.Enabled = this.isGenerating || this.isPlaying;
        }

        private void SetFeedbackEnabled(bool enabled)
        {
            this.button_feedback0.Enabled = enabled;
            this.button_feedback25.Enabled = enabled;
            this.button_feedback50.Enabled = enabled;
            this.button_feedback75.Enabled = enabled;
            this.button_feedback100.Enabled = enabled;
        }

        private void CancelGeneration()
        {
            this.generationCancellationTokenSource?.Cancel();
        }

        private void StartPlayback()
        {
            if (this.renderedBeat is null)
            {
                return;
            }

            this.StopPlayback();
            this.playbackCancellationTokenSource = new CancellationTokenSource();
            this.isPlaying = true;
            this.SetGenerationControlsEnabled(!this.isGenerating);
            _ = this.PlayRenderedBeatAsync(this.renderedBeat, this.playbackCancellationTokenSource.Token);
        }

        private async Task PlayRenderedBeatAsync(AudioObj beat, CancellationToken cancellationToken)
        {
            try
            {
                beat.LoopEnabled = this.checkBox_loopUntilFeedback.Checked;
                this.label_status.Text = "Playing generated beat...";
                await beat.PlayAsync(cancellationToken);

                TimeSpan duration = beat.Duration > TimeSpan.Zero ? beat.Duration : TimeSpan.FromSeconds(1);
                await Task.Delay(duration, cancellationToken);
                this.SetFeedbackEnabled(true);
                this.label_status.Text = this.checkBox_loopUntilFeedback.Checked
                    ? "Beat is looping. Choose feedback to train the engine."
                    : "Beat played. Choose feedback to train the engine.";

                if (!this.checkBox_loopUntilFeedback.Checked)
                {
                    this.isPlaying = false;
                    this.SetGenerationControlsEnabled(!this.isGenerating);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                LogCollection.Log($"Neural beat playback failed: {ex}");
                this.label_status.Text = "Playback failed. See log for details.";
            }
            finally
            {
                if (!this.checkBox_loopUntilFeedback.Checked || this.feedbackGiven || cancellationToken.IsCancellationRequested)
                {
                    this.isPlaying = false;
                    this.SetGenerationControlsEnabled(!this.isGenerating);
                }
            }
        }

        private void button_export_Click(object sender, EventArgs e)
        {
            if (this.renderedBeat is null)
            {
                return;
            }

            AudioObj exportedBeat = this.renderedBeat.Clone();
            this.exportedCollectionView ??= new AudioCollectionView([]);
            this.exportedCollectionView.AudioC.Audios.Add(exportedBeat);
            this.exportedCollectionView.Show();
            this.exportedCollectionView.BringToFront();
            int count = this.exportedCollectionView.AudioC.Audios.Count;
            this.exportedCollectionView.Rename($"Neural Beat{(count == 1 ? string.Empty : "s")} Generated {(float)this.numericUpDown_bpm.Value:F1} BPM");
            this.label_status.Text = "Exported generated beat to the audio collection.";
        }

        private async void button_stop_Click(object sender, EventArgs e)
        {
            this.CancelGeneration();
            await this.StopPlaybackAsync();
            this.label_status.Text = "Stopped.";
        }

        private void StopPlayback()
        {
            this.playbackCancellationTokenSource?.Cancel();
            this.playbackCancellationTokenSource?.Dispose();
            this.playbackCancellationTokenSource = null;
        }

        private async Task StopPlaybackAsync()
        {
            this.StopPlayback();
            if (this.renderedBeat is not null)
            {
                this.renderedBeat.LoopEnabled = false;
                await this.renderedBeat.StopAsync();
            }

            this.isPlaying = false;
            this.SetGenerationControlsEnabled(!this.isGenerating);
        }

        private async void button_feedback_Click(object sender, EventArgs e)
        {
            if (sender is not Button { Tag: string valueText } ||
                !float.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out float feedbackValue) ||
                this.engine is null ||
                this.currentPattern is null)
            {
                return;
            }

            try
            {
                this.SetFeedbackEnabled(false);
                this.feedbackGiven = true;
                await this.StopPlaybackAsync();
                this.label_status.Text = $"Applying {feedbackValue:P0} feedback...";
                await this.engine.ApplyFeedbackAsync(this.currentNodeId, this.currentPattern, feedbackValue);
                this.button_generate.Enabled = true;
                this.button_remix.Enabled = true;

                if (this.checkBox_loopFeedback.Checked)
                {
                    this.label_status.Text = $"Applied {feedbackValue:P0} feedback. Generating next beat...";
                    await this.GenerateSequenceAsync(this.currentNodeId, createEngine: false);
                }
                else
                {
                    this.label_status.Text = $"Applied {feedbackValue:P0} feedback. Generate another beat or create a remix.";
                }
            }
            catch (Exception ex)
            {
                LogCollection.Log($"Neural beat feedback failed: {ex}");
                this.label_status.Text = "Feedback failed. See log for details.";
                MessageBox.Show(this, ex.Message, "Neural Beat Engine", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void button_remix_Click(object sender, EventArgs e)
        {
            if (this.engine is null || this.currentPattern is null)
            {
                return;
            }

            try
            {
                await this.StopPlaybackAsync();
                this.button_remix.Enabled = false;
                this.label_status.Text = "Creating remix node...";
                string remixNodeId = await this.engine.CreateRemixNodeAsync(this.currentNodeId, (float)this.numericUpDown_mutation.Value);
                await this.GenerateSequenceAsync(remixNodeId, createEngine: false);
            }
            catch (Exception ex)
            {
                LogCollection.Log($"Neural beat remix failed: {ex}");
                this.label_status.Text = "Remix failed. See log for details.";
                MessageBox.Show(this, ex.Message, "Neural Beat Engine", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void NeuralBeatEngineDialog_FormClosing(object sender, FormClosingEventArgs e)
        {
            this.CancelGeneration();
            this.StopPlayback();
            if (this.renderedBeat is not null)
            {
                this.renderedBeat.LoopEnabled = false;
                _ = this.renderedBeat.StopAsync();
            }

            Image? image = this.pictureBox_beatMap.Image;
            this.pictureBox_beatMap.Image = null;
            image?.Dispose();
            this.Audios.Dispose();
        }

        private async Task RenderCurrentPatternAsync()
        {
            if (this.currentPattern is null)
            {
                throw new InvalidOperationException("Generate a pattern before rendering.");
            }

            List<bool[]> pattern = this.currentPattern.ToList();
            this.ShowBeatMap(pattern);
            this.label_status.Text = "Rendering beat audio...";
            this.renderedBeat = await BreakbeatGenerator_V2.RenderBreakbeatAsync(
                pattern,
                this.Audios.Audios,
                (float)this.numericUpDown_bpm.Value,
                (int)this.numericUpDown_beatsPerBar.Value * (int)this.numericUpDown_stepsPerBeat.Value,
                0f,
                "NeuralBeat");

            if (this.renderedBeat is null)
            {
                throw new InvalidOperationException("The beat could not be rendered from the selected samples.");
            }

            this.button_export.Enabled = true;
        }

        private void ShowBeatMap(IReadOnlyList<bool[]> pattern)
        {
            int width = Math.Max(1, this.pictureBox_beatMap.Width);
            int height = Math.Max(1, this.pictureBox_beatMap.Height);
            var bitmap = new Bitmap(width, height);

            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.FromArgb(247, 247, 247));
            if (pattern.Count > 0 && pattern[0].Length > 0)
            {
                const float leftMargin = 120f;
                const float topMargin = 8f;
                float cellWidth = Math.Max(1f, (width - leftMargin - 4f) / pattern[0].Length);
                float cellHeight = Math.Max(1f, (height - topMargin - 4f) / pattern.Count);
                int stepsPerBar = (int)this.numericUpDown_beatsPerBar.Value * (int)this.numericUpDown_stepsPerBeat.Value;

                using Font font = new("Segoe UI", Math.Max(6f, Math.Min(9f, cellHeight * .5f)));
                using Brush textBrush = new SolidBrush(Color.FromArgb(70, 70, 70));
                using Brush emptyBrush = new SolidBrush(Color.FromArgb(232, 232, 232));
                using Brush hitBrush = new SolidBrush(Color.FromArgb(60, 110, 255));
                using Pen gridPen = new(Color.FromArgb(210, 210, 210));
                using Pen barPen = new(Color.FromArgb(120, 120, 120), 2f);

                for (int row = 0; row < pattern.Count; row++)
                {
                    string name = row < this.Audios.Audios.Count ? this.Audios.Audios[row].Name : $"Sample {row + 1}";
                    graphics.DrawString(name, font, textBrush, new RectangleF(2, topMargin + row * cellHeight, leftMargin - 6, cellHeight));
                    for (int step = 0; step < pattern[row].Length; step++)
                    {
                        var cell = new RectangleF(leftMargin + step * cellWidth, topMargin + row * cellHeight, Math.Max(1f, cellWidth - 1f), Math.Max(1f, cellHeight - 1f));
                        graphics.FillRectangle(pattern[row][step] ? hitBrush : emptyBrush, cell);
                        graphics.DrawRectangle(gridPen, cell.X, cell.Y, cell.Width, cell.Height);
                    }
                }

                for (int step = stepsPerBar; step < pattern[0].Length; step += stepsPerBar)
                {
                    float x = leftMargin + step * cellWidth;
                    graphics.DrawLine(barPen, x, topMargin, x, height - 4);
                }
            }

            Image? previous = this.pictureBox_beatMap.Image;
            this.pictureBox_beatMap.Image = bitmap;
            previous?.Dispose();
        }

        private void removeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (this.listBox_samples.SelectedItem is AudioObj selectedAudio)
            {
                this.Audios.Audios.Remove(selectedAudio);
                this.listBox_samples.SelectedIndex = this.listBox_samples.Items.Count > 0 ? 0 : -1;
            }
        }
    }
}