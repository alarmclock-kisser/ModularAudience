using ModularAudience.Audio;
using ModularAudience.Generators;
using ModularAudience.Forms.Helpers;

namespace ModularAudience.Forms.Modules.Dialogs;

public partial class NeuralBeatEngineDialog
{
    private Button saveTrainingButton = null!, loadTrainingButton = null!, resetTrainingButton = null!, explorerButton = null!;
    private CheckBox embedSamplesCheck = null!;
    private Label sessionStatus = null!;
    private Label trainingIterationsLabel = null!;
    private List<BeatSampleSlot> slotMetadata = [];
    private NeuralBeatTreeWindow? explorer;
    private Button playPatternButton = null!;
    private bool restoringSessionOptions;
    private ToolTip optionToolTips = null!;
    private static string TrainingDumpDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        "ModularAudience", "_NeuralBeatTraining");
    private bool SessionCompatible => engine is null ? Audios.Count >= 1 : Audios.Count == engine.Config.SampleCount;
    private bool EditingAvailable => !isGenerating && !isTraining && !isSessionIo && !closing;

    private void InitializeSessionControls()
    {
        SuspendLayout();
        numericUpDown_minWeight.Minimum = -100;
        numericUpDown_maxWeight.Minimum = -100;
        numericUpDown_minWeight.Maximum = 0;
        numericUpDown_maxWeight.Minimum = 0;
        numericUpDown_threadCount.Maximum = Math.Max(1, Environment.ProcessorCount);
        numericUpDown_threadCount.ValueChanged += (_, _) =>
        {
            if (!restoringSessionOptions && !isSessionIo && !closing) engine?.SetThreadCount((int)numericUpDown_threadCount.Value);
        };
        foreach (var control in new[] { numericUpDown_learningRate, numericUpDown_temperature, numericUpDown_weightDecay,
            numericUpDown_minWeight, numericUpDown_maxWeight }) control.ValueChanged += LearningOption_Changed;
        var root = new TableLayoutPanel { Name = "trainingRootLayout", Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 1, RowCount = 7 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (int row in Enumerable.Range(0, 7)) root.RowStyles.Add(new RowStyle(row == 5 ? SizeType.Percent : SizeType.AutoSize, row == 5 ? 100 : 0));
        var toolbar = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        saveTrainingButton = SessionButton("Save training...", SaveTraining_Click);
        loadTrainingButton = SessionButton("Load training...", LoadTraining_Click);
        resetTrainingButton = SessionButton("Reset session", ResetTraining_Click);
        explorerButton = SessionButton("Training tree...", Explorer_Click);
        playPatternButton = SessionButton("Play current pattern", PlayPattern_Click);
        embedSamplesCheck = new CheckBox { Text = "Include sample audio", AutoSize = true, Margin = new Padding(8, 8, 3, 0) };
        toolbar.Controls.AddRange([saveTrainingButton, loadTrainingButton, resetTrainingButton, explorerButton, playPatternButton, embedSamplesCheck]);
        sessionStatus = new Label { AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(2, 4, 2, 6), MaximumSize = new Size(1400, 0) };
        trainingIterationsLabel = new Label { AutoSize = true, Margin = new Padding(8, 8, 3, 0), Text = "Training iterations: 0 | Active branch: 0" };
        toolbar.Controls.Add(trainingIterationsLabel);
        var parameters = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        foreach (int column in Enumerable.Range(0, 3)) parameters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, column == 2 ? 34 : 33));
        foreach (var group in new[] { groupBox_samples, groupBox_timing, groupBox_learning })
        { group.Dock = DockStyle.Fill; group.Margin = new Padding(3); group.MinimumSize = new Size(210, 220); }
        parameters.Controls.Add(groupBox_samples, 0, 0);
        parameters.Controls.Add(groupBox_timing, 1, 0);
        parameters.Controls.Add(groupBox_learning, 2, 0);
        groupBox_actions.Dock = DockStyle.Fill;
        groupBox_actions.MinimumSize = new Size(670, 69);
        groupBox_feedback.Dock = DockStyle.Fill;
        groupBox_feedback.MinimumSize = new Size(0, 0);
        groupBox_feedback.AutoSize = true;
        groupBox_feedback.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        flowLayoutPanel_feedback.Dock = DockStyle.Top;
        flowLayoutPanel_feedback.AutoSize = true;
        flowLayoutPanel_feedback.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        flowLayoutPanel_feedback.WrapContents = true;
        pictureBox_beatMap.Dock = DockStyle.Fill;
        // A PictureBox minimum height otherwise overflows its percent-sized row
        // when toolbar/status/feedback wrap. The pattern scales to the available space.
        pictureBox_beatMap.MinimumSize = Size.Empty;
        pictureBox_beatMap.Margin = Padding.Empty;
        // Keep image preferred-size changes out of the TableLayoutPanel's row sizing.
        var patternViewport = new Panel { Dock = DockStyle.Fill, Margin = new Padding(3, 6, 3, 6) };
        pictureBox_beatMap.Dock = DockStyle.None;
        pictureBox_beatMap.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        patternViewport.Controls.Add(pictureBox_beatMap);
        patternViewport.Resize += (_, _) => pictureBox_beatMap.Bounds = patternViewport.ClientRectangle;
        patternViewport.Layout += (_, _) =>
        {
            if (pictureBox_beatMap.Bounds != patternViewport.ClientRectangle)
                pictureBox_beatMap.Bounds = patternViewport.ClientRectangle;
        };
        label_status.Dock = DockStyle.Fill;
        root.Controls.Add(toolbar, 0, 0);
        root.Controls.Add(sessionStatus, 0, 1);
        root.Controls.Add(parameters, 0, 2);
        root.Controls.Add(groupBox_actions, 0, 3);
        root.Controls.Add(label_status, 0, 4);
        root.Controls.Add(patternViewport, 0, 5);
        root.Controls.Add(groupBox_feedback, 0, 6);
        root.Layout += (_, _) =>
        {
            if (pictureBox_beatMap.Bounds != patternViewport.ClientRectangle)
                pictureBox_beatMap.Bounds = patternViewport.ClientRectangle;
        };
        Controls.Add(root);
        sessionStatus.MaximumSize = new Size(Math.Max(100, root.ClientSize.Width - root.Padding.Horizontal - 6), 0);
        root.SizeChanged += (_, _) => sessionStatus.MaximumSize = new Size(Math.Max(100, root.ClientSize.Width - root.Padding.Horizontal - 6), 0);
        MinimumSize = new Size(760, 800);
        Size = new Size(940, 850);
        listBox_samples.AllowDrop = true;
        listBox_samples.DragEnter += Samples_DragEnter;
        listBox_samples.DragOver += Samples_DragEnter;
        listBox_samples.DragDrop += Samples_DragDrop;
        var replace = new ToolStripMenuItem("Replace slots: hold Shift while dropping samples") { Enabled = false };
        contextMenuStrip_samples.Items.Add(replace);
        contextMenuStrip_samples.Items.Add("Move slot up", null, (_, _) => MoveSample(-1));
        contextMenuStrip_samples.Items.Add("Move slot down", null, (_, _) => MoveSample(1));
        Audios.Audios.ListChanged += (_, _) =>
        {
            if (closing || IsDisposed || Disposing) return;
            SetFeedbackEnabled(false);
            UpdateSessionAvailability();
            if (currentPattern is not null) ShowBeatMap(currentPattern);
        };
        pictureBox_beatMap.Resize += (_, _) => { if (!closing && currentPattern is not null) ShowBeatMap(currentPattern); };
        InitializeOptionToolTips();
        ResumeLayout(true);
        UpdateSessionAvailability();
    }

    private static Button SessionButton(string text, EventHandler handler)
    { var button = new Button { Text = text, AutoSize = true, Padding = new Padding(4) }; button.Click += handler; return button; }

    private void UpdateSessionAvailability()
    {
        if (saveTrainingButton is null || closing || IsDisposed || Disposing) return;
        bool editable = EditingAvailable;
        groupBox_timing.Enabled = !closing;
        foreach (var control in new[] { numericUpDown_bars, numericUpDown_beatsPerBar, numericUpDown_stepsPerBeat, numericUpDown_interleaved })
            control.Enabled = editable && !sessionLocked;
        numericUpDown_bpm.Enabled = editable && !isPlaying;
        groupBox_learning.Enabled = !closing;
        foreach (var control in groupBox_learning.Controls.OfType<NumericUpDown>())
            control.Enabled = !isSessionIo && !closing;
        numericUpDown_mutation.Enabled = editable;
        listBox_samples.Enabled = editable;
        checkBox_loopUntilFeedback.Enabled = editable && !isPlaying;
        checkBox_loopFeedback.Enabled = editable;
        button_generate.Enabled = editable && SessionCompatible && !isPlaying;
        button_remix.Enabled = editable && SessionCompatible && engine is not null && !isPlaying;
        button_stop.Enabled = isGenerating || isPlaying;
        button_export.Enabled = editable && renderedBeat is not null;
        embedSamplesCheck.Enabled = editable;
        saveTrainingButton.Enabled = editable && engine is not null;
        loadTrainingButton.Enabled = resetTrainingButton.Enabled = editable && !isPlaying;
        explorerButton.Enabled = editable && engine is not null;
        playPatternButton.Enabled = editable && !isPlaying && SessionCompatible && currentPattern is not null;
        if (!SessionCompatible) SetFeedbackEnabled(false);
        var counts = engine?.GetTrainingIterations(currentNodeId) ?? (0L, 0);
        trainingIterationsLabel.Text = $"Training iterations: {counts.Item1:N0} | Active branch: {counts.Item2:N0}";
        sessionStatus.ForeColor = SessionCompatible ? SystemColors.ControlText : Color.DarkOrange;
        sessionStatus.Text = engine is null
            ? $"New session · {Audios.Count} samples. Drop samples from an Audio Collection. Generate locks the session structure."
            : $"Session · {currentNodeId} · {engine.Config.SampleCount} ordered slots · {engine.Config.Bars} bars × {engine.Config.BeatsPerBar} beats × {engine.Config.StepsPerBeat} steps. " +
              (SessionCompatible ? "Ready. Audio can be replaced without changing learned slot weights." : $"Paused: expected {engine.Config.SampleCount} samples, currently {Audios.Count}. Restore the sample count to generate/train.") +
               " Timing grid / extra-note limit locked. Learning settings remain adjustable; BPM changes apply to the next render.";
    }

    private bool EnsureSessionCompatible()
    {
        if (SessionCompatible) return true;
        UpdateSessionAvailability();
        label_status.Text = "Sample count does not match the session. Replace/add/remove samples to restore the expected slot count.";
        return false;
    }

    private bool PrepareSessionForGeneration(ref string nodeId)
    {
        if (engine is null) return true;
        var proposed = CreateConfig();
        if (proposed != engine.Config)
        {
            if (sessionLocked) throw new InvalidOperationException("The session structure is locked. Reset or load a session before changing it.");
            if (MessageBox.Show(this, "The settings differ from the loaded model. Start a new model with these settings? Save first to retain the loaded training tree.",
                "New model required", MessageBoxButtons.OKCancel) != DialogResult.OK) return false;
            explorer?.Close(); explorer = null;
            engine.Dispose(); engine = new AsyncBeatGraphEngine(proposed);
            currentNodeId = nodeId = "root"; slotMetadata = CaptureSlots();
        }
        sessionLocked = true;
        return true;
    }

    private async void PlayPattern_Click(object? sender, EventArgs e)
    {
        if (!EditingAvailable || currentPattern is null || !EnsureSessionCompatible()) return;
        try
        {
            isGenerating = true; UpdateSessionAvailability(); SetFeedbackEnabled(false);
            await StopPlaybackAsync();
            if (engine is not null)
            {
                ApplyConfig(engine.Config, (float)numericUpDown_bpm.Value);
                sessionLocked = true;
            }
            await RenderCurrentPatternAsync();
            StartPlayback();
        }
        catch (Exception error) { WindowMainStaticHelpers.ShowErrorWithCopyButton(this, "Play pattern", error); }
        finally { isGenerating = false; UpdateSessionAvailability(); }
    }

    private List<BeatSampleSlot> CaptureSlots() => Audios.Audios.Select(audio => new BeatSampleSlot
    { Name = audio.Name, SampleRate = audio.SampleRate, Channels = audio.Channels, SampleLength = audio.Data?.Length ?? 0, Volume = audio.Volume }).ToList();

    private async void SaveTraining_Click(object? sender, EventArgs e)
    {
        if (!EditingAvailable || engine is null) return;
        Directory.CreateDirectory(TrainingDumpDirectory);
        using var dialog = new SaveFileDialog { Filter = "Neural beat training (*.nbtrain)|*.nbtrain", DefaultExt = "nbtrain", AddExtension = true,
            InitialDirectory = TrainingDumpDirectory, FileName = "NeuralBeatTraining_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".nbtrain" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            isSessionIo = true; UpdateSessionAvailability();
            await StopPlaybackAsync();
            var slots = SessionCompatible ? CaptureSlots() : slotMetadata;
            var document = new BeatTrainingDocument { Model = engine.Snapshot(), ActiveNodeId = currentNodeId,
                Bpm = (float)numericUpDown_bpm.Value, Mutation = (float)numericUpDown_mutation.Value, Slots = slots,
                LoopPlayback = checkBox_loopUntilFeedback.Checked, LoopFeedback = checkBox_loopFeedback.Checked };
            await BeatTrainingArchive.SaveAsync(dialog.FileName, document, Audios.Audios.ToArray(), embedSamplesCheck.Checked);
            label_status.Text = "Training saved, including all branches, weights, patterns and feedback.";
        }
        catch (Exception error) { WindowMainStaticHelpers.ShowErrorWithCopyButton(this, "Save training", error); }
        finally { isSessionIo = false; UpdateSessionAvailability(); }
    }

    private async void LoadTraining_Click(object? sender, EventArgs e)
    {
        if (!EditingAvailable || isPlaying) return;
        Directory.CreateDirectory(TrainingDumpDirectory);
        using var dialog = new OpenFileDialog { Filter = "Neural beat training (*.nbtrain)|*.nbtrain", CheckFileExists = true,
            InitialDirectory = TrainingDumpDirectory };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (engine is not null && MessageBox.Show(this, "Replace the current in-memory session with this file? Save first to retain current changes.",
            "Load training", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
        try
        {
            isSessionIo = true; UpdateSessionAvailability();
            var loaded = await BeatTrainingArchive.LoadAsync(dialog.FileName);
            ApplyLoadedTraining(loaded);
            label_status.Text = "Training restored. Timing grid and extra-note limit locked; learning settings remain adjustable. Match the sample slots, then Generate to continue.";
        }
        catch (Exception error) { WindowMainStaticHelpers.ShowErrorWithCopyButton(this, "Load training", error); }
        finally { isSessionIo = false; UpdateSessionAvailability(); }
    }

    private void ApplyLoadedTraining(LoadedBeatTraining loaded)
    {
        explorer?.Close(); explorer = null;
        var previousEngine = engine;
        engine = loaded.Engine;
        currentNodeId = loaded.Document.ActiveNodeId;
        slotMetadata = loaded.Document.Slots;
        if (loaded.Samples.Count > 0)
        {
            foreach (var audio in Audios.Audios) audio.Dispose();
            Audios.Audios.Clear();
            foreach (var audio in loaded.Samples) Audios.Audios.Add(audio);
        }
        ApplyConfig(engine.Config, loaded.Document.Bpm);
        numericUpDown_mutation.Value = (decimal)loaded.Document.Mutation;
        checkBox_loopUntilFeedback.Checked = loaded.Document.LoopPlayback;
        checkBox_loopFeedback.Checked = loaded.Document.LoopFeedback;
        currentPattern = null; feedbackGiven = true; SetFeedbackEnabled(false);
        var oldMap = pictureBox_beatMap.Image; pictureBox_beatMap.Image = null; oldMap?.Dispose();
        renderedBeat?.Dispose(); renderedBeat = null;
        sessionLocked = true;
        previousEngine?.Dispose();
        UpdateSessionAvailability();
    }

    private void ApplyConfig(BeatEngineConfig config, float bpm)
    {
        restoringSessionOptions = true;
        try
        {
        numericUpDown_bars.Value = config.Bars; numericUpDown_beatsPerBar.Value = config.BeatsPerBar;
        numericUpDown_stepsPerBeat.Value = config.StepsPerBeat; numericUpDown_interleaved.Value = config.Interleaved;
        numericUpDown_learningRate.Value = (decimal)config.LearningRate; numericUpDown_temperature.Value = (decimal)config.Temperature;
        numericUpDown_weightDecay.Value = (decimal)config.WeightDecay; numericUpDown_minWeight.Value = (decimal)config.MinWeight;
        numericUpDown_maxWeight.Value = (decimal)config.MaxWeight;
        int threads = Math.Clamp(config.ThreadCount, 1, (int)numericUpDown_threadCount.Maximum);
        numericUpDown_threadCount.Value = threads; numericUpDown_bpm.Value = (decimal)bpm;
        engine?.SetThreadCount(threads);
        }
        finally { restoringSessionOptions = false; }
    }

    private void LearningOption_Changed(object? sender, EventArgs e)
    {
        if (restoringSessionOptions || isSessionIo || closing) return;
        if (numericUpDown_minWeight.Value >= numericUpDown_maxWeight.Value)
        {
            restoringSessionOptions = true;
            try
            {
                if (sender == numericUpDown_minWeight) numericUpDown_minWeight.Value = numericUpDown_maxWeight.Value - .01m;
                else numericUpDown_maxWeight.Value = numericUpDown_minWeight.Value + .01m;
            }
            finally { restoringSessionOptions = false; }
        }
        engine?.UpdateLearningSettings((float)numericUpDown_learningRate.Value, (float)numericUpDown_temperature.Value,
            (float)numericUpDown_weightDecay.Value, (float)numericUpDown_minWeight.Value, (float)numericUpDown_maxWeight.Value);
    }

    private void InitializeOptionToolTips()
    {
        optionToolTips = new ToolTip(components) { ShowAlways = true, AutoPopDelay = 20000, InitialDelay = 350, ReshowDelay = 100 };
        Explain(label_bars, numericUpDown_bars, "Number of bars in each pattern. More bars create a longer grid and more weights. Fixed for an existing session; Reset to change.");
        Explain(label_beatsPerBar, numericUpDown_beatsPerBar, "Beats in each bar (for example 3 or 4). Changes the musical grouping and total grid length. Fixed for an existing session.");
        Explain(label_stepsPerBeat, numericUpDown_stepsPerBeat, "Grid subdivisions per beat: 2 gives eighth notes, 4 gives sixteenth notes. Higher values allow finer rhythms and cost more memory. Fixed for an existing session.");
        Explain(label_bpm, numericUpDown_bpm, "Playback tempo in quarter-note beats per minute. Higher BPM plays the same rhythm faster without changing learned weights. Applies on the next render; stop playback to adjust.");
        Explain(label_interleaved, numericUpDown_interleaved, "Maximum additional simultaneous hits per step. 0 permits rest or one hit; 1 permits up to two. The model learns hit-count choices. Fixed because changing this reshapes its density weights.");
        Explain(label_learningRate, numericUpDown_learningRate, "Feedback strength (0–1). Higher values change preferences faster; lower values learn gradually. Zero disables reward/punishment updates, but weight decay still applies. Adjustable during a session; affects subsequent feedback.");
        Explain(label_temperature, numericUpDown_temperature, "Sampling randomness. Lower values strongly favor high-weight choices; higher values explore more alternatives. Does not erase learned weights. Adjustable during a session; affects the next generated pattern.");
        Explain(label_weightDecay, numericUpDown_weightDecay, "Fraction of each weight removed per feedback update (0–1). 0 retains learned preferences; higher values forget them faster. Also applies to neutral feedback. Affects subsequent training iterations.");
        Explain(label_minWeight, numericUpDown_minWeight, "Lower bound for all sample-slot and hit-count weights. More-negative values permit stronger discouragement. Raising the bound immediately clamps existing weights in every branch; this can reduce learned contrasts. Must be below Max weight and at or below zero.");
        Explain(label_maxWeight, numericUpDown_maxWeight, "Upper bound for all weights. Larger values permit stronger preferences, especially with low temperature. Lowering the bound immediately clamps existing weights in every branch. Must exceed Min weight and include zero.");
        Explain(label_threadCount, numericUpDown_threadCount, "Maximum parallel workers for batch generation. Limited to the logical processors available to this process. Does not change the learned model. Existing batches retain their concurrency limit; future batches use the new value. Single-pattern generation does not use all workers.");
        Explain(label_mutation, numericUpDown_mutation, "Random weight variation applied when creating a remix branch. 0 copies parent weights exactly; higher values explore farther from the parent. Parent weights are unchanged. Adjustable during a session; affects the next remix.");
        optionToolTips.SetToolTip(embedSamplesCheck, "Include lossless sample audio in the training dump. Without it, only the model and slot descriptions are stored; supply any replacement kit with the same ordered slot count.");
        optionToolTips.SetToolTip(checkBox_loopUntilFeedback, "Repeat the rendered beat while you listen. Feedback becomes available after one full pattern has played.");
        optionToolTips.SetToolTip(checkBox_loopFeedback, "After applying feedback, automatically generate the next pattern using the current branch and latest learning settings.");
        void Explain(Label label, Control control, string text) { optionToolTips.SetToolTip(label, text); optionToolTips.SetToolTip(control, text); }
    }

    private async void ResetTraining_Click(object? sender, EventArgs e)
    {
        if (!EditingAvailable) return;
        if (engine is not null && MessageBox.Show(this, "Reset this in-memory training session? Save first to retain its tree and weights.", "Reset training", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
        await StopPlaybackAsync();
        explorer?.Close(); explorer = null;
        engine?.Dispose(); engine = null; currentNodeId = "root"; currentPattern = null;
        renderedBeat?.Dispose(); renderedBeat = null; sessionLocked = false; feedbackGiven = false;
        var image = pictureBox_beatMap.Image; pictureBox_beatMap.Image = null; image?.Dispose();
        SetFeedbackEnabled(false); UpdateSessionAvailability();
    }

    private static AudioObj[] DroppedSamples(IDataObject? data) => data?.GetData(typeof(AudioObj[])) as AudioObj[]
        ?? (data?.GetData(typeof(IEnumerable<AudioObj>)) as IEnumerable<AudioObj>)?.ToArray() ?? [];

    private void Samples_DragEnter(object? sender, DragEventArgs e) =>
        e.Effect = EditingAvailable && DroppedSamples(e.Data).Length > 0 ? DragDropEffects.Copy : DragDropEffects.None;

    private async void Samples_DragDrop(object? sender, DragEventArgs e)
    {
        if (!EditingAvailable) return;
        var samples = DroppedSamples(e.Data);
        if (samples.Length == 0) return;
        isSessionIo = true; UpdateSessionAvailability();
        try
        {
            await StopPlaybackAsync();
            renderedBeat?.Dispose(); renderedBeat = null;
            bool replace = (e.KeyState & 4) != 0;
            int index = listBox_samples.IndexFromPoint(listBox_samples.PointToClient(new Point(e.X, e.Y)));
            if (replace && index < 0) index = listBox_samples.SelectedIndex;
            foreach (var source in samples)
            {
                var clone = source.Clone();
                if (replace && index >= 0 && index < Audios.Count)
                { var old = Audios.Audios[index]; Audios.Audios[index++] = clone; old.Dispose(); }
                else Audios.Audios.Add(clone);
            }
            label_status.Text = "Samples copied. Slot order maps audio to learned pattern rows. Hold Shift while dropping to replace slots.";
        }
        catch (Exception error) { WindowMainStaticHelpers.ShowErrorWithCopyButton(this, "Replace sample slots", error); }
        finally { isSessionIo = false; UpdateSessionAvailability(); }
    }

    private void MoveSample(int direction)
    {
        if (!EditingAvailable || isPlaying) return;
        int index = listBox_samples.SelectedIndex, next = index + direction;
        if (index < 0 || next < 0 || next >= Audios.Count) return;
        var sample = Audios.Audios[index]; Audios.Audios.RemoveAt(index); Audios.Audios.Insert(next, sample);
        renderedBeat?.Dispose(); renderedBeat = null;
        listBox_samples.SelectedIndex = next;
        UpdateSessionAvailability();
    }

    private void Explorer_Click(object? sender, EventArgs e)
    {
        if (engine is null || !EditingAvailable) return;
        explorer ??= new NeuralBeatTreeWindow(engine, () => EditingAvailable && !isPlaying,
            async (nodeId, record) =>
            {
                if (!EnsureSessionCompatible()) return;
                await StopPlaybackAsync();
                currentNodeId = nodeId;
                if (record is null) await GenerateSequenceAsync(nodeId, false);
                else
                {
                    isGenerating = true; UpdateSessionAvailability();
                    try
                    {
                        ApplyConfig(engine.Config, (float)numericUpDown_bpm.Value);
                        sessionLocked = true;
                        currentPattern = AsyncBeatGraphEngine.CopyPattern(record.Pattern); feedbackGiven = record.Feedback is not null;
                        await RenderCurrentPatternAsync(); StartPlayback();
                    }
                    finally { isGenerating = false; UpdateSessionAvailability(); }
                }
                UpdateSessionAvailability();
            }, () =>
            {
                if (engine.Snapshot().Branches.All(branch => branch.Id != currentNodeId)) { currentNodeId = "root"; currentPattern = null; SetFeedbackEnabled(false); }
                UpdateSessionAvailability();
            });
        explorer.FormClosed += (_, _) => explorer = null;
        explorer.Show(this); explorer.BringToFront(); explorer.RefreshTree();
    }

    private void RefreshExplorer() => explorer?.RefreshTree();
}
