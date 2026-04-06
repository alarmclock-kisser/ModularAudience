using ModularAudience.Audio;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.ComponentModel;

namespace ModularAudience.Forms.Modules
{
    internal sealed class BufferedPatternPanel : Panel
    {
        public BufferedPatternPanel()
        {
            this.DoubleBuffered = true;
            this.ResizeRedraw = true;
        }
    }

    internal sealed class SoftLimiterSampleProvider(ISampleProvider source) : ISampleProvider
    {
        public WaveFormat WaveFormat => source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int read = source.Read(buffer, offset, count);
            const float drive = 1.1f;
            const float makeup = 0.88f;

            for (int i = 0; i < read; i++)
            {
                float sample = buffer[offset + i] * drive;
                buffer[offset + i] = MathF.Tanh(sample) * makeup;
            }

            return read;
        }
    }

    public partial class DrumRollEditor : Form
    {
        private sealed class PatternRowState
        {
            public required AudioObj Audio { get; init; }
            public required string Name { get; init; }
            public required bool[] Steps { get; init; }
        }

        private readonly record struct PatternLayoutInfo(int ContentWidth, int ContentHeight, int RowHeight, int NameWidth, int StepWidth, bool ShowStepNumbers);

        private const int PatternPadding = 5;
        private const int PatternRowSpacing = 2;
        private const int PatternRowMinHeight = 24;
        private const int PatternRowMaxHeight = 72;
        private const int PatternStepSpacing = 3;
        private const int PatternNameMinWidth = 72;
        private const int PatternNameMaxWidth = 240;
        private const int PatternStepMinWidth = 6;
        private const int TargetRowHeight = 24;
        private const int SchedulingLookaheadMs = 60;
        private const int OutputDesiredLatencyMs = 45;
        private const int VisualDelayCompensationMs = 20;

        private readonly AudioCollection AudioC = new();
        private AudioCollectionView? CollectionView = null;

        public float Bpm => (float) this.numericUpDown_bpm.Value;
        public int Hits => this.domainUpDown_hits.SelectedItem is null ? 16 : int.Parse(this.domainUpDown_hits.SelectedItem.ToString() ?? "16");
        public float Volume => (float) this.numericUpDown_volume.Value / 100.0f;
        private bool InterleavedPlaybackEnabled => this.checkBox_interleaved.Checked;

        internal int RerollInterval => (int) this.numericUpDown_rerollInterval.Value;
        internal int RerollCountdown { get; private set; } = -1;
        internal bool InterleavedRandom { get; private set; } = false;

        internal readonly BindingList<Panel> Panels = [];
        private readonly List<PatternRowState> patternRows = [];
        private readonly SemaphoreSlim rebuildSemaphore = new(1, 1);
        private readonly Random random = new();
        private ContextMenuStrip? rowContextMenu;
        private int contextMenuRowIndex = -1;

        private WaveOutEvent? waveOut;
        private MixingSampleProvider? mixer;
        private readonly WaveFormat outputFormat;

        // Scheduler-specific
        private CancellationTokenSource? schedulerCts;
        private Task? schedulerTask;
        private readonly Lock outputLock = new();
        private volatile int currentStep = 0;
        private bool isPlaying = false;
        private volatile float schedulerBpm;
        private volatile int schedulerHits;
        private bool updatingWindowLayout = false;
        private bool initialPatternLoadCompleted = false;


        public DrumRollEditor(IEnumerable<AudioObj>? samples = null)
        {
            this.InitializeComponent();
            this.KeyPreview = true;
            this.panel_pattern.Visible = false;
            this.button_hit.Visible = false;
            this.domainUpDown_hits.SelectedIndex = this.domainUpDown_hits.Items.IndexOf("16");

            this.outputFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
            this.waveOut = null;
            this.mixer = null;

            if (samples != null)
            {
                foreach (AudioObj sample in samples)
                {
                    this.AudioC.Audios.Add(sample.Clone());
                }
            }

            this.StartPosition = FormStartPosition.Manual;
            this.Location = WindowsScreenHelper.GetCenterStartingPoint(this);

            this.AllowDrop = true;
            this.DragEnter += this.DrumRollEditor_DragEnter;
            this.DragDrop += this.DrumRollEditor_DragDrop;
            this.FormClosing += this.Form_Closing;
            this.Shown += this.DrumRollEditor_Shown;

            this.AudioC.Audios.ListChanged += this.AudioC_Audios_ListChanged;
            this.InitializeRowContextMenu();

            // Set min/max width
            this.MinimumSize = new Size(720, this.MinimumSize.Height);
            int screenMaxHeight = Math.Max(this.MinimumSize.Height, (Screen.FromControl(this).WorkingArea.Height) - 24);
            this.MaximumSize = new Size(1280, screenMaxHeight);
            this.Resize += this.DrumRollEditor_Resize;

        }

        private void Form_Closing(object? sender, FormClosingEventArgs e)
        {
            this.StopPlayback();

            // Events entfernen
            this.AudioC.Audios.ListChanged -= this.AudioC_Audios_ListChanged;
            this.domainUpDown_hits.SelectedItemChanged -= this.domainUpDown_hits_SelectedItemChanged;
            this.DragEnter -= this.DrumRollEditor_DragEnter;
            this.DragDrop -= this.DrumRollEditor_DragDrop;
            this.Shown -= this.DrumRollEditor_Shown;
            try { this.rowContextMenu?.Dispose(); } catch { }
            try { this.rebuildSemaphore.Dispose(); } catch { }
            this.AudioC.Dispose();
        }

        private void button_playback_Click(object sender, EventArgs e)
        {
            if (this.isPlaying)
            {
                this.StopPlayback();
            }
            else
            {
                this.StartPlayback();
            }
        }



        private async void AudioC_Audios_ListChanged(object? sender, ListChangedEventArgs e)
        {
            try
            {
                if (!this.initialPatternLoadCompleted)
                {
                    return;
                }

                await this.RebuildPatternPanelsAsync(this.CapturePatternButtonStates());
            }
            catch (Exception ex)
            {
                try { LogCollection.Log($"DrumRollEditor ListChanged error: {ex.Message}"); } catch { }
            }
        }

        private async void DrumRollEditor_Shown(object? sender, EventArgs e)
        {
            try
            {
                if (this.initialPatternLoadCompleted)
                {
                    return;
                }

                this.initialPatternLoadCompleted = true;
                await this.RebuildPatternPanelsAsync();
                await this.ResizePanelsAndButtonsAsync();
            }
            catch (Exception ex)
            {
                try { LogCollection.Log($"DrumRollEditor Shown error: {ex.Message}"); } catch { }
                try { this.Close(); } catch { }
            }
        }

        private async void domainUpDown_hits_SelectedItemChanged(object? sender, EventArgs e)
        {
            try
            {
                List<List<bool>> restoreStates = this.CapturePatternButtonStates();

                // Update scheduler-safe copy sofort auf UI-Thread
                try
                {
                    this.schedulerHits = this.Hits;
                }
                catch { }

                await this.RebuildPatternPanelsAsync(restoreStates);
                await this.ResizePanelsAndButtonsAsync();
                if (this.isPlaying)
                {
                    this.currentStep = 0;
                }
            }
            catch (Exception ex)
            {
                try { LogCollection.Log($"DrumRollEditor HitsChanged error: {ex.Message}"); } catch { }
            }
        }

        private void DrumRollEditor_DragEnter(object? sender, DragEventArgs e)
        {
            if (e.Data != null)
            {
                if (e.Data.GetDataPresent(typeof(AudioObj)) ||
                    e.Data.GetDataPresent(typeof(List<AudioObj>)) ||
                    e.Data.GetDataPresent(typeof(IEnumerable<AudioObj>)) ||
                    e.Data.GetDataPresent(typeof(ListBox.SelectedObjectCollection)))
                {
                    e.Effect = DragDropEffects.Copy;
                    return;
                }
                if (e.Data.GetDataPresent(DataFormats.Serializable))
                {
                    var data = e.Data.GetData(DataFormats.Serializable);
                    if (data is AudioObj || data is IEnumerable<AudioObj>)
                    {
                        e.Effect = DragDropEffects.Copy;
                        return;
                    }
                }
            }
            e.Effect = DragDropEffects.None;
        }

        private void DrumRollEditor_DragDrop(object? sender, DragEventArgs e)
        {
            if (e.Data == null)
            {
                return;
            }

            // Einzelnes AudioObj
            if (e.Data.GetDataPresent(typeof(AudioObj)))
            {
                var audio = e.Data.GetData(typeof(AudioObj)) as AudioObj;
                if (audio != null && !this.AudioC.Audios.Contains(audio))
                {
                    this.AudioC.Audios.Add(audio);
                }
                return;
            }
            // Liste von AudioObj
            if (e.Data.GetDataPresent(typeof(List<AudioObj>)))
            {
                var audioList = e.Data.GetData(typeof(List<AudioObj>)) as List<AudioObj>;
                if (audioList != null)
                {
                    foreach (var audio in audioList)
                    {
                        if (!this.AudioC.Audios.Contains(audio))
                        {
                            this.AudioC.Audios.Add(audio);
                        }
                    }
                }
                return;
            }
            // IEnumerable<AudioObj>
            if (e.Data.GetDataPresent(typeof(IEnumerable<AudioObj>)))
            {
                var enumerable = e.Data.GetData(typeof(IEnumerable<AudioObj>)) as IEnumerable<AudioObj>;
                if (enumerable != null)
                {
                    foreach (var audio in enumerable)
                    {
                        if (audio is AudioObj a && !this.AudioC.Audios.Contains(a))
                        {
                            this.AudioC.Audios.Add(a);
                        }
                    }
                }
                return;
            }
            // Drag aus ListBox.SelectedObjectCollection
            if (e.Data.GetDataPresent(typeof(ListBox.SelectedObjectCollection)))
            {
                var selected = e.Data.GetData(typeof(ListBox.SelectedObjectCollection)) as ListBox.SelectedObjectCollection;
                if (selected != null)
                {
                    foreach (var item in selected)
                    {
                        if (item is AudioObj audio && !this.AudioC.Audios.Contains(audio))
                        {
                            this.AudioC.Audios.Add(audio);
                        }
                    }
                }
                return;
            }
            // Drag als Serializable
            if (e.Data.GetDataPresent(DataFormats.Serializable))
            {
                var data = e.Data.GetData(DataFormats.Serializable);
                if (data is AudioObj audio && !this.AudioC.Audios.Contains(audio))
                {
                    this.AudioC.Audios.Add(audio);
                    return;
                }
                if (data is IEnumerable<AudioObj> list)
                {
                    foreach (var a in list)
                    {
                        if (!this.AudioC.Audios.Contains(a))
                        {
                            this.AudioC.Audios.Add(a);
                        }
                    }
                    return;
                }
            }
        }



        private async Task RebuildPatternPanelsAsync(List<List<bool>>? restoreStates = null)
        {
            await this.rebuildSemaphore.WaitAsync().ConfigureAwait(false);

            try
            {
                var audioSnapshot = this.AudioC.Audios.ToList();
                int hits = Math.Max(1, this.Hits);

                var rows = await Task.Run(() =>
                {
                    var rebuiltRows = new List<PatternRowState>(audioSnapshot.Count);
                    for (int i = 0; i < audioSnapshot.Count; i++)
                    {
                        bool[] steps = new bool[hits];
                        if (restoreStates != null && i < restoreStates.Count)
                        {
                            List<bool> source = restoreStates[i];
                            int copyLength = Math.Min(hits, source.Count);
                            for (int step = 0; step < copyLength; step++)
                            {
                                steps[step] = source[step];
                            }
                        }

                        AudioObj audio = audioSnapshot[i];
                        rebuiltRows.Add(new PatternRowState
                        {
                            Audio = audio,
                            Name = string.IsNullOrWhiteSpace(audio.Name) ? "untitled" : audio.Name,
                            Steps = steps
                        });
                    }

                    return rebuiltRows;
                }).ConfigureAwait(false);

                void ApplyRows()
                {
                    this.patternRows.Clear();
                    this.patternRows.AddRange(rows);
                    this.Panels.Clear();
                    this.FitWindowHeightToPattern();
                    this.UpdatePatternViewport();
                    this.panel_pattern.Visible = this.patternRows.Count > 0;
                    this.panel_pattern.Invalidate();
                }

                if (this.IsDisposed)
                {
                    return;
                }

                if (this.InvokeRequired)
                {
                    this.Invoke(ApplyRows);
                }
                else
                {
                    ApplyRows();
                }
            }
            finally
            {
                this.rebuildSemaphore.Release();
            }
        }

        private List<int> GetActiveTrackIndicesForStep(int stepIndex)
        {
            List<int> activeTracks = new List<int>();
            for (int trackIndex = 0; trackIndex < this.patternRows.Count; trackIndex++)
            {
                bool[] steps = this.patternRows[trackIndex].Steps;
                if (stepIndex >= 0 && stepIndex < steps.Length && steps[stepIndex])
                {
                    activeTracks.Add(trackIndex);
                }
            }

            return activeTracks;
        }

        private static List<int> GetActiveTrackIndicesForStep(IReadOnlyList<List<bool>> patternStates, int stepIndex)
        {
            List<int> activeTracks = new List<int>();
            for (int trackIndex = 0; trackIndex < patternStates.Count; trackIndex++)
            {
                List<bool> steps = patternStates[trackIndex];
                if (stepIndex >= 0 && stepIndex < steps.Count && steps[stepIndex])
                {
                    activeTracks.Add(trackIndex);
                }
            }

            return activeTracks;
        }

        private List<int> SelectTracksForStep(List<int> activeTracks, int stepIndex)
        {
            if (activeTracks.Count <= 1 || !this.InterleavedPlaybackEnabled)
            {
                return activeTracks;
            }

            return new List<int> { activeTracks[stepIndex % activeTracks.Count] };
        }

        private static float ComputeStepGain(int simultaneousTracks)
        {
            if (simultaneousTracks <= 1)
            {
                return 0.92f;
            }

            return Math.Clamp(0.88f / MathF.Sqrt(simultaneousTracks), 0.2f, 0.88f);
        }

        private Task ResizePanelsAndButtonsAsync()
        {
            this.UpdatePatternViewport();
            this.panel_pattern.Invalidate();
            return Task.CompletedTask;
        }

        private void FitWindowHeightToPattern()
        {
            if (this.updatingWindowLayout || !this.IsHandleCreated)
            {
                return;
            }

            try
            {
                this.updatingWindowLayout = true;

                int rowCount = this.patternRows.Count;
                if (rowCount <= 0)
                {
                    return;
                }

                int headerBottom = Math.Max(
                    this.label_info_dragndrop.Bottom,
                    Math.Max(
                        this.button_export.Bottom,
                        Math.Max(
                            this.button_playback.Bottom,
                            Math.Max(this.button_randomize.Bottom, this.checkBox_interleaved.Bottom))));

                int top = Math.Max(0, headerBottom + 6);
                int bottomMargin = 20;
                int nonClientHeight = this.Height - this.ClientSize.Height;
                int desiredRowHeight = Math.Max(TargetRowHeight, PatternRowMinHeight);
                int desiredPanelHeight = (PatternPadding * 2) + (rowCount * desiredRowHeight) + Math.Max(0, (rowCount - 1) * PatternRowSpacing);
                int desiredHeight = top + desiredPanelHeight + bottomMargin + nonClientHeight;

                Rectangle workArea = Screen.FromControl(this).WorkingArea;
                int maxHeight = Math.Max(this.MinimumSize.Height, workArea.Height - 24);
                int clampedHeight = Math.Clamp(desiredHeight, this.MinimumSize.Height, maxHeight);
                this.MaximumSize = new Size(this.MaximumSize.Width, maxHeight);

                if (this.Height != clampedHeight)
                {
                    this.Height = clampedHeight;
                }
            }
            finally
            {
                this.updatingWindowLayout = false;
            }
        }



        private void RandomizeAllPanels(bool interleaved = false)
        {
            int hits = this.Hits;
            if (hits <= 0 || this.patternRows.Count == 0)
            {
                return;
            }

            double density = (double) this.numericUpDown_randomDensity.Value / 100d;
            double accent = (double) this.numericUpDown_randomAccent.Value / 100d;
            double streak = (double) this.numericUpDown_randomStreak.Value / 100d;
            double variation = (double) this.numericUpDown_randomVariation.Value / 100d;

            foreach (var row in this.patternRows)
            {
                Array.Clear(row.Steps, 0, row.Steps.Length);
            }

            if (interleaved)
            {
                for (int step = 0; step < hits; step++)
                {
                    int selectedRow = -1;
                    double bestScore = 0d;
                    for (int rowIndex = 0; rowIndex < this.patternRows.Count; rowIndex++)
                    {
                        double rowDensity = this.GetRowDensity(density, variation);
                        double chance = this.GetStepChance(rowDensity, accent, step, hits);
                        double roll = this.random.NextDouble();
                        if (roll < chance)
                        {
                            double score = chance - roll;
                            if (selectedRow < 0 || score > bestScore)
                            {
                                selectedRow = rowIndex;
                                bestScore = score;
                            }
                        }
                    }

                    if (selectedRow >= 0)
                    {
                        this.SetRandomizedStep(this.patternRows[selectedRow], step, hits, streak);
                    }
                }
            }
            else
            {
                foreach (var row in this.patternRows)
                {
                    double rowDensity = this.GetRowDensity(density, variation);
                    for (int step = 0; step < hits; step++)
                    {
                        double chance = this.GetStepChance(rowDensity, accent, step, hits);
                        if (this.random.NextDouble() < chance)
                        {
                            this.SetRandomizedStep(row, step, hits, streak);
                        }
                    }

                    if (!row.Steps.Any(step => step))
                    {
                        int fallbackStep = this.GetPreferredFallbackStep(hits);
                        row.Steps[fallbackStep] = true;
                    }
                }
            }

            this.panel_pattern.Invalidate();
        }



        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if ((e.KeyCode == Keys.Back || e.KeyCode == Keys.Space) && !e.Handled)
            {
                this.button_playback_Click(this, EventArgs.Empty);
                e.Handled = true;
                return;
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // R / Ctrl+R -> Randomize (interleaved when Ctrl gedrückt)
            if (keyData == Keys.R || keyData == (Keys.Control | Keys.R))
            {
                try
                {
                    bool interleaved = this.InterleavedPlaybackEnabled || (keyData & Keys.Control) == Keys.Control;
                    this.InterleavedRandom = interleaved;
                    this.RandomizeAllPanels(interleaved);
                }
                catch { }
                return true;
            }

            // Up / Down -> BPM anpassen (funktioniert auch wenn ein Child Control Fokus hat)
            if (keyData == Keys.Up || keyData == Keys.Down)
            {
                try
                {
                    float step = ModifierKeys.HasFlag(Keys.Control) ? 0.1f : 1.0f;
                    decimal current = this.numericUpDown_bpm.Value;
                    decimal delta = (decimal) (keyData == Keys.Up ? step : -step);
                    decimal next = Math.Clamp(current + delta, this.numericUpDown_bpm.Minimum, this.numericUpDown_bpm.Maximum);
                    this.numericUpDown_bpm.Value = next;
                }
                catch { }
                return true;
            }

            // Button E + Ctrl -> Export
            if (keyData == (Keys.Control | Keys.E))
            {
                try
                {
                    this.button_export_Click(this, EventArgs.Empty);
                }
                catch { }
                return true;
            }

            // Space / Back handled weiterhin in OnKeyDown - fallthrough ansonsten
            return base.ProcessCmdKey(ref msg, keyData);
        }


        private static readonly Color StepActiveFore = Color.White;
        private static readonly Color StepDefaultFore = Color.Black;
        private static readonly Color StepDefaultBack = SystemColors.Control;



        private void HandleCurrentStepUI(int step, int hits)
        {
            PatternLayoutInfo layout = this.GetPatternLayout();
            int previousStep = this.currentStep;

            if (previousStep >= 0 && previousStep < hits)
            {
                this.InvalidatePlaybackStep(previousStep, layout);
            }

            if (step >= 0 && step < hits)
            {
                this.InvalidatePlaybackStep(step, layout);
            }
        }

        private void InvalidatePlaybackStep(int stepIndex, PatternLayoutInfo layout)
        {
            if (this.patternRows.Count == 0 || stepIndex < 0 || stepIndex >= Math.Max(1, this.Hits))
            {
                return;
            }

            Rectangle firstStepRect = this.GetStepBounds(0, stepIndex, layout);
            Rectangle lastStepRect = this.GetStepBounds(this.patternRows.Count - 1, stepIndex, layout);
            Rectangle invalidateRect = Rectangle.FromLTRB(
                firstStepRect.Left - 1,
                firstStepRect.Top - 1,
                lastStepRect.Right + 1,
                lastStepRect.Bottom + 1);

            invalidateRect.Offset(this.panel_pattern.AutoScrollPosition);
            this.panel_pattern.Invalidate(invalidateRect);
        }

        private async Task SchedulerLoop(CancellationToken cancellationToken)
        {
            // Start etwas in der Zukunft, damit wir Lookahead nutzen können
            DateTimeOffset nextScheduledTime = DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(SchedulingLookaheadMs);
            int stepIndex = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;

                // schedule any steps that fall within now + lookahead
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (nextScheduledTime <= now + TimeSpan.FromMilliseconds(SchedulingLookaheadMs))
                    {
                        // read live-safe copies
                        float bpmNow = this.schedulerBpm > 0 ? this.schedulerBpm : this.Bpm;
                        int hitsNow = this.schedulerHits > 0 ? this.schedulerHits : this.Hits;
                        if (hitsNow <= 0)
                        {
                            hitsNow = 1;
                        }

                        List<int> activeTracks = this.GetActiveTrackIndicesForStep(stepIndex % hitsNow);
                        List<int> scheduledTracks = this.SelectTracksForStep(activeTracks, stepIndex);
                        float stepGain = ComputeStepGain(scheduledTracks.Count);

                        // For each track, if the button at step is active, schedule audio
                        for (int scheduledTrackIndex = 0; scheduledTrackIndex < scheduledTracks.Count; scheduledTrackIndex++)
                        {
                            int trackIdx = scheduledTracks[scheduledTrackIndex];
                            if (trackIdx >= this.AudioC.Audios.Count)
                            {
                                continue;
                            }

                            var audio = this.AudioC.Audios[trackIdx];
                            try
                            {
                                this.ScheduleAudioAt(audio, nextScheduledTime, cancellationToken, stepGain);
                            }
                            catch
                            {
                            }
                        }

                        // schedule UI update to run exactly at nextScheduledTime
                        int scheduledUiStep = stepIndex % hitsNow;
                        DateTimeOffset uiUpdateTime = nextScheduledTime;
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var delay = uiUpdateTime + TimeSpan.FromMilliseconds(VisualDelayCompensationMs) - DateTimeOffset.UtcNow;
                                if (delay > TimeSpan.Zero)
                                {
                                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                                }
                            }
                            catch (TaskCanceledException) { return; }
                            catch { }
                            try
                            {
                                if (this.IsHandleCreated && !this.IsDisposed)
                                {
                                    this.BeginInvoke((MethodInvoker) (() =>
                                    {
                                        this.HandleCurrentStepUI(scheduledUiStep, this.Hits);
                                        this.currentStep = scheduledUiStep;
                                    }));
                                }
                            }
                            catch { }
                        }, cancellationToken);

                        // Schritt vorwärts: Intervall aus aktuellen Scheduler-Werten berechnen
                        int intervalMs = ComputeIntervalMsFromValues(
                            this.schedulerBpm > 0 ? this.schedulerBpm : this.Bpm,
                            this.schedulerHits > 0 ? this.schedulerHits : this.Hits);

                        nextScheduledTime = nextScheduledTime + TimeSpan.FromMilliseconds(intervalMs);
                        stepIndex++;

                        // Reroll-Logik: wenn RerollInterval > 0, zählen wir abgeschlossene Pattern-Zyklen
                        try
                        {
                            int hitsForCycle = hitsNow;
                            if (hitsForCycle > 0 && (stepIndex % hitsForCycle) == 0)
                            {
                                // Wir sind am Ende eines vollen Durchlaufs (hits Schritte)
                                if (this.RerollInterval > 0)
                                {
                                    // Initialisieren falls notwendig
                                    if (this.RerollCountdown <= 0)
                                    {
                                        this.RerollCountdown = this.RerollInterval;
                                    }

                                    this.RerollCountdown--;

                                    if (this.RerollCountdown <= 0)
                                    {
                                        // Execute reroll on UI thread (RandomizeAllPanels verändert UI)
                                        try
                                        {
                                            if (this.IsHandleCreated && !this.IsDisposed)
                                            {
                                                this.Invoke((MethodInvoker) (() =>
                                                {
                                                    try
                                                    {
                                                        // Führe Randomize aus, nutze aktuellen InterleavedRandom-Status
                                                        this.RandomizeAllPanels(this.InterleavedRandom);
                                                    }
                                                    catch { }
                                                }));
                                            }
                                        }
                                        catch { }
                                        finally
                                        {
                                            // Reset Countdown auf aktuellen Interval-Wert (sofern noch >0)
                                            this.RerollCountdown = this.RerollInterval > 0 ? this.RerollInterval : -1;
                                        }
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                    else
                    {
                        break;
                    }
                }

                // kurze Pause, responsive zu Änderungen
                try
                {
                    await Task.Delay(Math.Max(5, SchedulingLookaheadMs / 4), cancellationToken).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        private void ScheduleAudioAt(AudioObj audio, DateTimeOffset playAt, CancellationToken cancellationToken, float stepGain)
        {
            if (audio.Data == null || audio.Data.LongLength == 0)
            {
                return;
            }

            // Build the sample provider chain (resample, mono->stereo, volume)
            ISampleProvider provider;
            var sampleProvider = new SampleData(audio.Data, audio.SampleRate, audio.Channels);
            provider = sampleProvider;

            // Resample if needed
            if (sampleProvider.WaveFormat.SampleRate != this.outputFormat.SampleRate)
            {
                provider = new WdlResamplingSampleProvider(provider, this.outputFormat.SampleRate);
            }

            // Mono -> Stereo if needed
            if (provider.WaveFormat.Channels == 1 && this.outputFormat.Channels == 2)
            {
                provider = new MonoToStereoSampleProvider(provider);
            }

            var finalVolumeProvider = new VolumeSampleProvider(provider)
            {
                Volume = Math.Clamp(this.Volume * stepGain, 0.02f, 1.0f)
            };

            // Calculate delay relative to now
            TimeSpan delay = playAt - DateTimeOffset.UtcNow;
            OffsetSampleProvider? offsetProvider = null;
            if (delay > TimeSpan.Zero)
            {
                offsetProvider = new OffsetSampleProvider(finalVolumeProvider)
                {
                    // Delay until playAt. OffsetSampleProvider fills zeros until delay elapses.
                    DelayBy = delay,
                    // Ensure it doesn't trim leading silence:
                    LeadOut = TimeSpan.Zero
                };
            }

            lock (this.outputLock)
            {
                // Ensure output system is up
                this.EnsureOutputReady();

                // Add to mixer (with or without offset)
                if (offsetProvider != null)
                {
                    this.mixer?.AddMixerInput(offsetProvider);
                }
                else
                {
                    this.mixer?.AddMixerInput(finalVolumeProvider);
                }
            }
        }

        private void Button_playback_Click(object sender, EventArgs e)
        {
            if (this.isPlaying)
            {
                this.StopPlayback();
            }
            else
            {
                this.StartPlayback();
            }
        }

        private void EnsureOutputReady()
        {
            lock (this.outputLock)
            {
                if (this.mixer == null)
                {
                    this.mixer = new MixingSampleProvider(this.outputFormat) { ReadFully = true };
                }
                if (this.waveOut == null)
                {
                    this.waveOut = new WaveOutEvent()
                    {
                        DesiredLatency = OutputDesiredLatencyMs,
                        NumberOfBuffers = 2
                    };
                    this.waveOut.Init(new SoftLimiterSampleProvider(this.mixer));
                }
                if (this.waveOut.PlaybackState != PlaybackState.Playing)
                {
                    try
                    {
                        this.waveOut.Play();
                    }
                    catch
                    {
                        // ignore errors on Play
                    }
                }
            }
        }

        private void StartPlayback()
        {
            if (this.isPlaying)
            {
                return;
            }

            this.isPlaying = true;
            this.currentStep = 0;
            this.button_playback.Text = "■";

            // initiale Scheduler-Werte von UI übernehmen (auf UI-Thread)
            this.schedulerBpm = this.Bpm;
            this.schedulerHits = this.Hits;

            // Reroll-Countdown neu initialisieren, falls RerollInterval aktiv ist
            try
            {
                if (this.RerollInterval > 0)
                {
                    this.RerollCountdown = this.RerollInterval;
                }
                else
                {
                    this.RerollCountdown = -1;
                }
            }
            catch { this.RerollCountdown = -1; }

            // Play scheduler
            this.schedulerCts = new CancellationTokenSource();
            this.schedulerTask = Task.Run(() => this.SchedulerLoop(this.schedulerCts.Token));

            // Keep numericUpDown subscription to update BPM live
            this.numericUpDown_bpm.ValueChanged += this.Bpm_ValueChanged;
        }

        private void StopPlayback()
        {
            if (!this.isPlaying)
            {
                return;
            }

            this.isPlaying = false;
            this.button_playback.Text = "▶";

            // Cancel scheduler and wait
            try
            {
                this.schedulerCts?.Cancel();
                this.schedulerTask?.Wait(500);
            }
            catch { }
            finally
            {
                try { this.schedulerCts?.Dispose(); } catch { }
                this.schedulerCts = null;
                this.schedulerTask = null;
            }

            // Reset UI highlight
            try
            {
                if (this.IsHandleCreated && !this.IsDisposed)
                {
                    this.BeginInvoke((MethodInvoker) (() =>
                    {
                        this.HandleCurrentStepUI(-1, this.Hits);
                        this.currentStep = -1;
                    }));
                }
            }
            catch { }

            // Unsubscribe BPM update
            try
            {
                this.numericUpDown_bpm.ValueChanged -= this.Bpm_ValueChanged;
            }
            catch { }

            // Stop playback but keep output alive for fast restart
            lock (this.outputLock)
            {
                try { this.waveOut?.Stop(); } catch { }
            }

            // Reroll-Countdown zurücksetzen, damit beim nächsten Start neu initialisiert wird
            try
            {
                this.RerollCountdown = -1;
            }
            catch { }
        }




        public async Task<AudioObj> GenerateSampleAsync()
        {
            // UI-state sicher erfassen (auf UI-Thread)
            int hits = this.Hits;
            float bpm = this.Bpm;
            int sampleRate = 44100;
            int channels = 2;

            if (bpm <= 0f || hits <= 0)
            {
                // Fallbackwerte
                bpm = Math.Max(1f, bpm);
                hits = Math.Max(1, hits);
            }

            // Button-Zustände und Audiodaten als Snapshots erfassen, um Cross-Thread-Access zu vermeiden
            var patternStates = this.CapturePatternButtonStates(); // List<List<bool>> - UI-thread
            bool interleavedPlayback = this.InterleavedPlaybackEnabled;
            var audioSnapshots = new List<(float[] Data, int Channels, int SampleRate)>();
            int panelCount = this.patternRows.Count;
            for (int trackIdx = 0; trackIdx < panelCount; trackIdx++)
            {
                if (trackIdx >= this.AudioC.Audios.Count)
                {
                    audioSnapshots.Add((Array.Empty<float>(), 1, sampleRate));
                    continue;
                }

                var audio = this.AudioC.Audios[trackIdx];
                if (audio?.Data == null || audio.Data.Length == 0)
                {
                    audioSnapshots.Add((Array.Empty<float>(), Math.Max(1, audio?.Channels ?? 1), audio?.SampleRate > 0 ? audio.SampleRate : sampleRate));
                    continue;
                }

                // Kopie der Audiodaten anfertigen, damit Background-Task nicht auf UI-Objekte zeigt
                float[] copy = new float[audio.Data.Length];
                Array.Copy(audio.Data, copy, audio.Data.Length);
                audioSnapshots.Add((copy, Math.Max(1, audio.Channels), audio.SampleRate > 0 ? audio.SampleRate : sampleRate));
            }

            // Dauer / Samples berechnen
            float secondsPerStep = 60f / bpm * 4f / hits; // 4/4-Takt
            int totalSamples = (int) (secondsPerStep * hits * sampleRate);
            if (totalSamples <= 0)
            {
                totalSamples = 1;
            }

            // Heavy CPU-Arbeit auf ThreadPool ausführen
            var mixBuffer = await Task.Run(() =>
            {
                var mix = new float[totalSamples * channels];

                int usableTracks = Math.Min(audioSnapshots.Count, patternStates.Count);
                for (int step = 0; step < hits; step++)
                {
                    List<int> activeTracks = GetActiveTrackIndicesForStep(patternStates, step);
                    if (activeTracks.Count == 0)
                    {
                        continue;
                    }

                    if (interleavedPlayback && activeTracks.Count > 1)
                    {
                        activeTracks = [activeTracks[step % activeTracks.Count]];
                    }

                    float stepGain = ComputeStepGain(activeTracks.Count);
                    int stepStart = (int) (step * secondsPerStep * sampleRate);

                    foreach (int trackIdx in activeTracks)
                    {
                        if (trackIdx >= usableTracks)
                        {
                            continue;
                        }

                        var audioSnap = audioSnapshots[trackIdx];
                        if (audioSnap.Data == null || audioSnap.Data.Length == 0)
                        {
                            continue;
                        }

                        int audioChannels = audioSnap.Channels > 0 ? audioSnap.Channels : 1;
                        float[] audioData = audioSnap.Data;
                        int audioLen = audioData.Length / audioChannels;

                        for (int n = 0; n < audioLen; n++)
                        {
                            int mixPos = (stepStart + n) * channels;
                            int srcPos = n * audioChannels;
                            if (mixPos + channels > mix.Length)
                            {
                                break;
                            }

                            for (int c = 0; c < channels; c++)
                            {
                                float sample = audioData[srcPos + (c % audioChannels)];
                                mix[mixPos + c] += sample * this.Volume * stepGain;
                            }
                        }
                    }
                }

                // Clipping sanft begrenzen
                for (int i = 0; i < mix.Length; i++)
                {
                    mix[i] = MathF.Tanh(mix[i] * 1.1f) * 0.9f;
                }

                return mix;
            }).ConfigureAwait(false);

            // Ergebnis-Objekt erstellen (leichtgewichtiger UI-unabhängiger Schritt)
            var result = new AudioObj
            {
                Data = mixBuffer,
                SampleRate = sampleRate,
                Channels = channels,
                Duration = TimeSpan.FromSeconds(secondsPerStep * hits),
                Length = mixBuffer.Length,
                BitDepth = 32,
                Bpm = bpm
            };

            result.Rename("DrumRollMix_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));

            return result;
        }

        private async void button_export_Click(object sender, EventArgs e)
        {
            bool ctrlFlag = (ModifierKeys & Keys.Control) == Keys.Control;

            var mixed = await this.GenerateSampleAsync();

            if (this.CollectionView == null)
            {
                this.CollectionView = new AudioCollectionView([mixed]);
                this.CollectionView.Rename("Drum Roll Edits");
            }
            else
            {
                this.CollectionView.AudioC.Audios.Add(mixed);
            }
            this.CollectionView.Show();
            this.Focus();

            if (ctrlFlag)
            {
                string? exported = await this.AudioC.Exporter.ExportWavAsync(mixed);
            }
        }

        private List<List<bool>> CapturePatternButtonStates()
        {
            var states = new List<List<bool>>();
            foreach (PatternRowState row in this.patternRows)
            {
                states.Add([.. row.Steps]);
            }

            return states;
        }



        private void RestorePatternButtonStates(List<List<bool>> states)
        {
            for (int i = 0; i < this.patternRows.Count && i < states.Count; i++)
            {
                bool[] rowSteps = this.patternRows[i].Steps;
                List<bool> source = states[i];
                int copyLength = Math.Min(rowSteps.Length, source.Count);
                for (int step = 0; step < copyLength; step++)
                {
                    rowSteps[step] = source[step];
                }
            }

            this.panel_pattern.Invalidate();
        }

        private void InitializeRowContextMenu()
        {
            this.rowContextMenu = new ContextMenuStrip();

            var editItem = new ToolStripMenuItem("Edit Sample");
            editItem.Click += this.editSampleToolStripMenuItem_Click;

            var removeItem = new ToolStripMenuItem("Remove");
            removeItem.Click += this.removeSampleToolStripMenuItem_Click;

            var randomizeItem = new ToolStripMenuItem("Randomize Row");
            randomizeItem.Click += this.randomizeRowToolStripMenuItem_Click;

            this.rowContextMenu.Items.Add(editItem);
            this.rowContextMenu.Items.Add(removeItem);
            this.rowContextMenu.Items.Add(new ToolStripSeparator());
            this.rowContextMenu.Items.Add(randomizeItem);
        }

        private void UpdatePatternViewport()
        {
            int headerBottom = Math.Max(
                this.label_info_dragndrop.Bottom,
                Math.Max(
                    this.button_export.Bottom,
                    Math.Max(
                        this.button_playback.Bottom,
                        Math.Max(this.button_randomize.Bottom, this.checkBox_interleaved.Bottom))));

            int top = Math.Max(0, headerBottom + 6);
            int left = Math.Max(0, this.panel_pattern.Left);
            int rightMargin = 12;
            int bottomMargin = 20;
            int width = Math.Max(64, this.ClientSize.Width - left - rightMargin);
            int height = Math.Max(80, this.ClientSize.Height - top - bottomMargin);

            this.panel_pattern.Dock = DockStyle.None;
            this.panel_pattern.Location = new Point(left, top);
            this.panel_pattern.Size = new Size(width, height);
            this.panel_pattern.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            this.panel_pattern.AutoScroll = true;
            this.panel_pattern.Padding = Padding.Empty;
            this.panel_pattern.Margin = Padding.Empty;

            PatternLayoutInfo layout = this.GetPatternLayout();
            bool needsScroll = layout.ContentHeight > this.panel_pattern.ClientSize.Height;
            this.panel_pattern.AutoScroll = needsScroll;
            this.panel_pattern.AutoScrollMinSize = needsScroll
                ? new Size(0, Math.Max(0, layout.ContentHeight))
                : Size.Empty;

            if (!needsScroll)
            {
                this.panel_pattern.AutoScrollPosition = Point.Empty;
            }

            try
            {
                this.panel_pattern.HorizontalScroll.Enabled = false;
                this.panel_pattern.HorizontalScroll.Visible = false;
            }
            catch { }
        }

        private PatternLayoutInfo GetPatternLayout()
        {
            int hits = Math.Max(1, this.Hits);
            int rowCount = this.patternRows.Count;
            int viewportWidth = Math.Max(64, this.panel_pattern.ClientSize.Width);
            int viewportHeight = Math.Max(80, this.panel_pattern.ClientSize.Height);
            int usableHeight = Math.Max(PatternRowMinHeight, viewportHeight - (PatternPadding * 2));
            int totalSpacing = Math.Max(0, (rowCount - 1) * PatternRowSpacing);
            int rowHeight = rowCount == 0
                ? PatternRowMinHeight
                : Math.Max(PatternRowMinHeight, Math.Min(PatternRowMaxHeight, (usableHeight - totalSpacing) / Math.Max(1, rowCount)));

            int contentHeight = rowCount == 0
                ? 0
                : (PatternPadding * 2) + (rowCount * rowHeight) + totalSpacing;

            int availableWidth = Math.Max(64, viewportWidth - (PatternPadding * 2));
            if (contentHeight > viewportHeight)
            {
                availableWidth = Math.Max(64, availableWidth - SystemInformation.VerticalScrollBarWidth);
            }

            int spacingWidth = Math.Max(0, (hits - 1) * PatternStepSpacing);
            int nameWidth = Math.Min(PatternNameMaxWidth, Math.Max(PatternNameMinWidth, availableWidth / 3));
            int stepWidth = Math.Max(PatternStepMinWidth, (availableWidth - nameWidth - PatternPadding - spacingWidth) / Math.Max(1, hits));

            if (stepWidth == PatternStepMinWidth)
            {
                int targetNameWidth = Math.Max(96, availableWidth - (hits * stepWidth) - spacingWidth - PatternPadding);
                nameWidth = Math.Min(nameWidth, targetNameWidth);
                stepWidth = Math.Max(PatternStepMinWidth, (availableWidth - nameWidth - PatternPadding - spacingWidth) / Math.Max(1, hits));
            }

            int contentWidth = nameWidth + PatternPadding + (hits * stepWidth) + spacingWidth;
            bool showStepNumbers = hits <= 20 && stepWidth >= 22 && rowHeight >= 24;

            return new PatternLayoutInfo(contentWidth, contentHeight, rowHeight, nameWidth, stepWidth, showStepNumbers);
        }

        private Rectangle GetRowBounds(int rowIndex, PatternLayoutInfo layout)
        {
            int y = PatternPadding + (rowIndex * (layout.RowHeight + PatternRowSpacing));
            return new Rectangle(PatternPadding, y, layout.ContentWidth, layout.RowHeight);
        }

        private Rectangle GetNameBounds(int rowIndex, PatternLayoutInfo layout)
        {
            Rectangle rowRect = this.GetRowBounds(rowIndex, layout);
            return new Rectangle(rowRect.Left + 4, rowRect.Top, layout.NameWidth - 4, rowRect.Height);
        }

        private Rectangle GetStepBounds(int rowIndex, int stepIndex, PatternLayoutInfo layout)
        {
            Rectangle rowRect = this.GetRowBounds(rowIndex, layout);
            int x = rowRect.Left + layout.NameWidth + PatternPadding + (stepIndex * (layout.StepWidth + PatternStepSpacing));
            return new Rectangle(x, rowRect.Top + 4, layout.StepWidth, Math.Max(14, rowRect.Height - 8));
        }

        private int GetRowIndexFromPoint(Point contentPoint, PatternLayoutInfo layout)
        {
            int relativeY = contentPoint.Y - PatternPadding;
            if (relativeY < 0)
            {
                return -1;
            }

            int slotHeight = layout.RowHeight + PatternRowSpacing;
            int rowIndex = relativeY / slotHeight;
            if (rowIndex < 0 || rowIndex >= this.patternRows.Count)
            {
                return -1;
            }

            Rectangle rowRect = this.GetRowBounds(rowIndex, layout);
            return rowRect.Contains(contentPoint) ? rowIndex : -1;
        }

        private int GetStepIndexFromPoint(Point contentPoint, int rowIndex, PatternLayoutInfo layout)
        {
            for (int step = 0; step < Math.Max(1, this.Hits); step++)
            {
                if (this.GetStepBounds(rowIndex, step, layout).Contains(contentPoint))
                {
                    return step;
                }
            }

            return -1;
        }

        private static Color GetStepBackColor(bool active, bool isCurrentStep)
        {
            if (isCurrentStep)
            {
                return active ? Color.Red : Color.Orange;
            }

            return active ? Color.Green : StepDefaultBack;
        }

        private double GetRowDensity(double density, double variation)
        {
            double swing = (this.random.NextDouble() * 2d) - 1d;
            return Math.Clamp(density + (swing * variation * 0.35d), 0.02d, 0.98d);
        }

        private double GetStepChance(double density, double accent, int step, int hits)
        {
            int quarter = Math.Max(1, hits / 4);
            int halfQuarter = Math.Max(1, quarter / 2);
            double weight = 1d;

            if (step == 0)
            {
                weight += 0.7d * accent;
            }
            else if ((step % quarter) == 0)
            {
                weight += 0.45d * accent;
            }
            else if ((step % halfQuarter) == 0)
            {
                weight += 0.2d * accent;
            }
            else
            {
                weight -= 0.12d * accent;
            }

            return Math.Clamp(density * weight, 0.01d, 0.99d);
        }

        private void SetRandomizedStep(PatternRowState row, int step, int hits, double streak)
        {
            if (step < 0 || step >= row.Steps.Length)
            {
                return;
            }

            row.Steps[step] = true;
            if (step + 1 < hits && this.random.NextDouble() < streak)
            {
                row.Steps[step + 1] = true;
            }

            if (step + 2 < hits && this.random.NextDouble() < (streak * 0.45d))
            {
                row.Steps[step + 2] = true;
            }
        }

        private int GetPreferredFallbackStep(int hits)
        {
            if (hits >= 4)
            {
                return this.random.Next(2) == 0 ? 0 : Math.Max(0, hits / 2);
            }

            return 0;
        }

        private void ToggleStep(int rowIndex, int stepIndex)
        {
            if (rowIndex < 0 || rowIndex >= this.patternRows.Count)
            {
                return;
            }

            bool[] steps = this.patternRows[rowIndex].Steps;
            if (stepIndex < 0 || stepIndex >= steps.Length)
            {
                return;
            }

            steps[stepIndex] = !steps[stepIndex];
            this.panel_pattern.Invalidate();
        }

        private void RandomizeSingleRow(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= this.patternRows.Count)
            {
                return;
            }

            PatternRowState row = this.patternRows[rowIndex];
            int hits = Math.Max(1, this.Hits);
            double density = this.GetRowDensity((double) this.numericUpDown_randomDensity.Value / 100d, (double) this.numericUpDown_randomVariation.Value / 100d);
            double accent = (double) this.numericUpDown_randomAccent.Value / 100d;
            double streak = (double) this.numericUpDown_randomStreak.Value / 100d;

            Array.Clear(row.Steps, 0, row.Steps.Length);
            for (int step = 0; step < hits; step++)
            {
                if (this.random.NextDouble() < this.GetStepChance(density, accent, step, hits))
                {
                    this.SetRandomizedStep(row, step, hits, streak);
                }
            }

            if (!row.Steps.Any(step => step))
            {
                row.Steps[this.GetPreferredFallbackStep(hits)] = true;
            }

            this.panel_pattern.Invalidate();
        }

        private async void editSampleToolStripMenuItem_Click(object? sender, EventArgs e)
        {
            int rowIndex = this.contextMenuRowIndex;
            if (rowIndex < 0 || rowIndex >= this.patternRows.Count)
            {
                return;
            }

            AudioObj audio = this.patternRows[rowIndex].Audio;
            List<List<bool>> states = this.CapturePatternButtonStates();

            var tv = new TrackView(audio)
            {
                StartPosition = FormStartPosition.Manual,
                Location = WindowsScreenHelper.GetCenterStartingPoint(this)
            };

            tv.FormClosed += async (_, _) =>
            {
                try
                {
                    if (tv.DialogResult == DialogResult.OK || tv.DialogResult == DialogResult.None)
                    {
                        AudioObj edited = tv.OriginalAudio ?? audio;
                        int audioIndex = this.AudioC.Audios.IndexOf(audio);
                        if (audioIndex >= 0 && !ReferenceEquals(audio, edited))
                        {
                            this.AudioC.Audios[audioIndex] = edited;
                        }
                        else
                        {
                            await this.RebuildPatternPanelsAsync(states);
                        }
                    }
                }
                catch { }
                finally
                {
                    try { tv.Dispose(); } catch { }
                }
            };

            tv.Show();
            try { tv.BringToFront(); } catch { }
        }

        private async void removeSampleToolStripMenuItem_Click(object? sender, EventArgs e)
        {
            int rowIndex = this.contextMenuRowIndex;
            if (rowIndex < 0 || rowIndex >= this.patternRows.Count)
            {
                return;
            }

            AudioObj audio = this.patternRows[rowIndex].Audio;
            List<List<bool>> states = this.CapturePatternButtonStates();
            if (rowIndex < states.Count)
            {
                states.RemoveAt(rowIndex);
            }

            this.AudioC.Audios.ListChanged -= this.AudioC_Audios_ListChanged;
            try
            {
                this.AudioC.Audios.Remove(audio);
            }
            finally
            {
                this.AudioC.Audios.ListChanged += this.AudioC_Audios_ListChanged;
            }

            await this.RebuildPatternPanelsAsync(states);
        }

        private void randomizeRowToolStripMenuItem_Click(object? sender, EventArgs e)
        {
            this.RandomizeSingleRow(this.contextMenuRowIndex);
        }

        private void button_randomize_Click(object sender, EventArgs e)
        {
            bool interleaved = this.InterleavedPlaybackEnabled || (ModifierKeys & Keys.Control) == Keys.Control;
            this.InterleavedRandom = interleaved;
            this.RandomizeAllPanels(interleaved);
        }

        private void checkBox_interleaved_CheckedChanged(object sender, EventArgs e)
        {
            this.InterleavedRandom = this.InterleavedPlaybackEnabled;
        }

        private void panel_pattern_Paint(object sender, PaintEventArgs e)
        {
            try
            {
                e.Graphics.Clear(this.panel_pattern.BackColor);

                if (this.patternRows.Count == 0)
                {
                    TextRenderer.DrawText(
                        e.Graphics,
                        "Drop Sample here to add",
                        this.Font,
                        this.panel_pattern.ClientRectangle,
                        SystemColors.GrayText,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                    return;
                }

                PatternLayoutInfo layout = this.GetPatternLayout();
                Point scrollOffset = this.panel_pattern.AutoScrollPosition;
                Rectangle clipRect = new(
                    e.ClipRectangle.X - scrollOffset.X,
                    e.ClipRectangle.Y - scrollOffset.Y,
                    e.ClipRectangle.Width,
                    e.ClipRectangle.Height);

                e.Graphics.TranslateTransform(scrollOffset.X, scrollOffset.Y);

                using SolidBrush rowBrushEven = new(Color.FromArgb(245, 245, 245));
                using SolidBrush rowBrushOdd = new(Color.FromArgb(230, 230, 230));
                using SolidBrush activeBrush = new(Color.Green);
                using SolidBrush inactiveBrush = new(StepDefaultBack);
                using SolidBrush currentActiveBrush = new(Color.Red);
                using SolidBrush currentInactiveBrush = new(Color.Orange);
                using Pen borderPen = new(Color.DimGray);

                int slotHeight = layout.RowHeight + PatternRowSpacing;
                int firstVisibleRow = Math.Max(0, (clipRect.Top - PatternPadding) / Math.Max(1, slotHeight));
                int lastVisibleRow = Math.Min(this.patternRows.Count - 1, Math.Max(firstVisibleRow, (clipRect.Bottom - PatternPadding) / Math.Max(1, slotHeight)));

                for (int rowIndex = firstVisibleRow; rowIndex <= lastVisibleRow; rowIndex++)
                {
                    PatternRowState row = this.patternRows[rowIndex];
                    Rectangle rowRect = this.GetRowBounds(rowIndex, layout);
                    if (!rowRect.IntersectsWith(clipRect))
                    {
                        continue;
                    }

                    e.Graphics.FillRectangle(rowIndex % 2 == 0 ? rowBrushEven : rowBrushOdd, rowRect);

                    Rectangle nameRect = this.GetNameBounds(rowIndex, layout);
                    if (nameRect.IntersectsWith(clipRect) && nameRect.Width > 32)
                    {
                        TextRenderer.DrawText(
                            e.Graphics,
                            row.Name,
                            this.Font,
                            nameRect,
                            Color.Black,
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                    }

                    int visibleStepCount = Math.Min(Math.Max(1, this.Hits), row.Steps.Length);
                    for (int step = 0; step < visibleStepCount; step++)
                    {
                        bool isCurrentStep = this.isPlaying && step == this.currentStep;
                        Rectangle stepRect = this.GetStepBounds(rowIndex, step, layout);
                        if (!stepRect.IntersectsWith(clipRect))
                        {
                            continue;
                        }

                        Brush brush = GetStepBackColor(row.Steps[step], isCurrentStep) switch
                        {
                            var color when color == Color.Red => currentActiveBrush,
                            var color when color == Color.Orange => currentInactiveBrush,
                            var color when color == Color.Green => activeBrush,
                            _ => inactiveBrush
                        };

                        e.Graphics.FillRectangle(brush, stepRect);
                        e.Graphics.DrawRectangle(borderPen, stepRect);

                        if (layout.ShowStepNumbers)
                        {
                            TextRenderer.DrawText(
                                e.Graphics,
                                (step + 1).ToString(),
                                this.Font,
                                stepRect,
                                row.Steps[step] || isCurrentStep ? StepActiveFore : StepDefaultFore,
                                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                try { LogCollection.Log($"DrumRollEditor Paint error: {ex.Message}"); } catch { }
            }
        }

        private void panel_pattern_MouseClick(object sender, MouseEventArgs e)
        {
            if (this.patternRows.Count == 0)
            {
                return;
            }

            PatternLayoutInfo layout = this.GetPatternLayout();
            Point contentPoint = new(e.X - this.panel_pattern.AutoScrollPosition.X, e.Y - this.panel_pattern.AutoScrollPosition.Y);
            int rowIndex = this.GetRowIndexFromPoint(contentPoint, layout);
            if (rowIndex < 0)
            {
                return;
            }

            if (e.Button == MouseButtons.Right)
            {
                this.contextMenuRowIndex = rowIndex;
                this.rowContextMenu?.Show(this.panel_pattern, e.Location);
                return;
            }

            if (e.Button == MouseButtons.Left)
            {
                int stepIndex = this.GetStepIndexFromPoint(contentPoint, rowIndex, layout);
                if (stepIndex >= 0)
                {
                    this.ToggleStep(rowIndex, stepIndex);
                }
            }
        }

        private void DrumRollEditor_Resize(object? sender, EventArgs e)
        {
            _ = this.ResizePanelsAndButtonsAsync();
        }



        private static int ComputeIntervalMsFromValues(float bpm, int hits)
        {
            if (bpm <= 0f || hits <= 0)
            {
                return 100;
            }

            return (int) (60000.0f / bpm * 4.0f / hits);
        }

        private void Bpm_ValueChanged(object? sender, EventArgs e)
        {
            // live übernehmen: sichere Kopie aktualisieren (ValueChanged läuft auf UI-Thread)
            try
            {
                this.schedulerBpm = this.Bpm;
            }
            catch { }
            // keine weitere Aktion nötig: Scheduler liest schedulerBpm regelmäßig
        }
    }
}
