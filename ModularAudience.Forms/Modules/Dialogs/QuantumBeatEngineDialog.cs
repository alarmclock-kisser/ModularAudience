using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using ModularAudience.Audio;
using ModularAudience.Forms.Effects;
using ModularAudience.Forms.Quantum;
using ModularAudience.Quantum.Models;
using ModularAudience.Quantum.Services;

namespace ModularAudience.Forms.Modules.Dialogs;

/// <summary>Modeless quantum circuit / breakbeat workbench. Network execution is always explicit.</summary>
public partial class QuantumBeatEngineDialog : Form
{
    private readonly IQuantumBeatSession session;
    private readonly QuantumStudioStore store;
    private readonly IEffectPreviewPlayer player;
    private readonly List<AudioObj> samples = [];
    private readonly BindingList<QuantumGateEditorRow> gateRows = [];
    private readonly BindingList<QuantumTrackEditorRow> trackRows = [];
    private readonly CancellationTokenSource lifetime = new();
    private readonly System.Windows.Forms.Timer saveTimer = new() { Interval = 850 };
    private readonly System.Windows.Forms.Timer elapsedTimer = new() { Interval = 100 };
    private readonly Stopwatch operationClock = new();
    private CancellationTokenSource? workCts;
    private Task activeWork = Task.CompletedTask;
    private bool loading = true, busy, closing, released, suppressAutomaticSave, closeAfterSaveFailure;
    private bool workspaceDirty, connectionDirty;
    private string? loadedExperimentName;
    private string measuredFingerprint = "";
    private int measuredQubits;
    private QuantumExecutionResult? lastExecution;
    private QuantumBeatSequence? sequence;
    private AudioObj? rendered;
    private AudioCollectionView? outputCollection;
    private QuantumExperimentLibraryDialog? libraryWindow;
    private Form? helpWindow;

    public QuantumBeatEngineDialog(IEnumerable<AudioObj>? samples = null)
        : this(samples ?? [], new QuantumBeatSession(), new QuantumStudioStore(), new EffectPreviewPlayer()) { }

    internal QuantumBeatEngineDialog(IEnumerable<AudioObj> sourceSamples, IQuantumBeatSession session,
        QuantumStudioStore store, IEffectPreviewPlayer player)
    {
        this.session = session; this.store = store; this.player = player;
        AudioObj[] sources = sourceSamples.ToArray();
        if (sources.Length > 32 || sources.Any(source => source is null) || sources.Sum(source => (long)source.Data.Length) > 67_108_864)
            throw new ArgumentException("Select at most 32 samples with no more than 256 MiB of combined source PCM.");
        this.InitializeComponent();
        try
        {
            foreach (AudioObj source in sources)
            {
                AudioObj copy = source.Clone(); copy.Tag = source.Tag; copy.SampleTag = source.SampleTag;
                this.samples.Add(copy);
            }
            this.gatesGrid.DataSource = this.gateRows;
            this.tracksGrid.DataSource = this.trackRows;
            this.ApplyWorkspace(this.DefaultWorkspace());
            this.RestorePersistentState();
            this.InitializeEvents();
            this.InitializeHelpMenu(); // Kept separate so help follows the finished UI/workflow.
            this.loading = false;
            this.RefreshCircuit();
            this.RefreshSequenceImage();
            this.UpdateAvailability();
        }
        catch { this.ReleaseResources(); throw; }
    }

    internal QuantumBeatSequence? CurrentSequence => this.sequence;
    internal AudioObj? RenderedAudio => this.rendered;
    internal bool IsWorking => this.busy;

    private void InitializeEvents()
    {
        foreach (var (key, number) in this.numbers)
        {
            if (key is "RequestTimeout" or "JobTimeout" or "PollInterval" or "ReadRetries") number.ValueChanged += (_, _) => this.ConnectionChanged();
            else number.ValueChanged += (_, _) => this.EditorChanged();
        }
        foreach (var (key, check) in this.checks)
        {
            if (key == "ShowCredential") check.CheckedChanged += (_, _) => this.credentialText.UseSystemPasswordChar = !check.Checked;
            else if (key == "AllowCloudJobs") check.CheckedChanged += (_, _) => this.UpdateAvailability();
            else if (key == "AutoSave") check.CheckedChanged += (_, _) =>
            {
                if (this.loading) return;
                this.saveTimer.Stop(); this.workspaceDirty = true;
                this.TrySaveWorkspace(); // Persist the checkbox itself even when switching auto-save off.
            };
            else check.CheckedChanged += (_, _) => this.EditorChanged();
        }
        this.providerCombo.SelectedIndexChanged += (_, _) => this.EditorChanged();
        this.qasmVersionCombo.SelectedIndexChanged += (_, _) => this.EditorChanged();
        this.presetCombo.SelectedIndexChanged += (_, _) => this.EditorChanged();
        this.arrangementCombo.SelectedIndexChanged += (_, _) => this.EditorChanged();
        this.editorTabs.SelectedIndexChanged += (_, _) => this.ScheduleSave();
        this.hardwareQasmText.TextChanged += (_, _) => { if (!this.hardwareQasmText.ReadOnly) this.EditorChanged(); };
        foreach (Control control in new Control[] { this.authCombo, this.credentialText, this.crnText, this.backendCombo, this.endpointCombo, this.apiVersionText, this.pythonText })
            control.TextChanged += (_, _) => this.ConnectionChanged();
        this.connectionProfileCombo.TextChanged += (_, _) => { if (!this.loading) { this.connectionDirty = true; this.ScheduleSave(); } };
        this.gatesGrid.CellValueChanged += (_, _) => this.EditorChanged();
        this.tracksGrid.CellValueChanged += (_, _) => this.EditorChanged();
        this.gatesGrid.CurrentCellDirtyStateChanged += (_, _) => { if (this.gatesGrid.IsCurrentCellDirty) this.gatesGrid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        this.tracksGrid.CurrentCellDirtyStateChanged += (_, _) => { if (this.tracksGrid.IsCurrentCellDirty) this.tracksGrid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        this.gatesGrid.SelectionChanged += (_, _) => { if (!this.loading) this.DrawCircuit(); };
        foreach (var grid in new[] { this.gatesGrid, this.tracksGrid }) grid.DataError += (_, args) =>
        { args.ThrowException = false; this.SetStatus("Enter a valid gate, integer qubit index or numeric parameter.", true); };
        this.circuitPicture.MouseClick += (_, args) => this.SelectCircuitPosition(args);
        this.saveTimer.Tick += (_, _) => { this.saveTimer.Stop(); this.TrySaveWorkspace(); };
        this.elapsedTimer.Tick += (_, _) => { if (this.busy) this.delayLabel.Text = FormattableString.Invariant($"Waiting: {this.operationClock.Elapsed.TotalMilliseconds:0} ms"); };
        this.FormClosing += this.ClosingStudio;
        this.ResizeEnd += (_, _) => this.ScheduleSave();
    }

    private decimal N(string name) => this.numbers[name].Value;
    private bool C(string name) => this.checks[name].Checked;
    private void SetNumber(string name, double value) => this.numbers[name].Value = Math.Clamp(double.IsFinite(value) ? (decimal)value : this.numbers[name].Minimum,
        this.numbers[name].Minimum, this.numbers[name].Maximum);

    private QuantumCircuitDefinition CaptureCircuit(bool validate = true)
    {
        this.gatesGrid.EndEdit(); this.tracksGrid.EndEdit();
        int width = (int)this.N("Qubits");
        if (this.C("AutoQubits"))
        {
            int gateWidth = this.gateRows.Select(row => Math.Max(row.Qubit, row.Gate == "CX" ? row.Target : 0) + 1).DefaultIfEmpty(4).Max();
            int trackWidth = this.trackRows.Select(row => row.TriggerQubit + 1).DefaultIfEmpty(4).Max();
            width = Math.Clamp(Math.Max(Math.Max(gateWidth, trackWidth), (int)this.N("ModifierQubit") + 1), 4, 16);
            bool previous = this.loading; this.loading = true; this.SetNumber("Qubits", width); this.loading = previous;
        }
        var circuit = new QuantumCircuitDefinition(width, Array.AsReadOnly(this.gateRows.Select(row => row.ToOperation()).ToArray()),
            this.qasmVersionCombo.SelectedIndex == 1 ? OpenQasmVersion.Version3 : OpenQasmVersion.Version2);
        if (validate) _ = QuantumCircuitComposer.Build(circuit);
        return circuit;
    }

    private QuantumBeatOptions CaptureOptions() => new()
    {
        Bars = (int)this.N("Bars"), StepsPerBar = (int)this.N("StepsPerBar"), Bpm = (double)this.N("Bpm"), Seed = (int)this.N("Seed"),
        Density = (double)this.N("Density") / 100, Swing = (double)this.N("Swing") / 100, Arrangement = (QuantumBarArrangement)this.arrangementCombo.SelectedIndex,
        Interleaved = this.C("Interleaved"), ModifierQubit = (int)this.N("ModifierQubit"), QuantumVelocity = this.C("QuantumVelocity"),
        MinimumVelocity = (double)this.N("MinimumVelocity") / 100, MaximumVelocity = (double)this.N("MaximumVelocity") / 100,
        GhostNotes = this.C("GhostNotes"), GhostChance = (double)this.N("GhostChance") / 100, GhostVelocityScale = (double)this.N("GhostVelocityScale") / 100,
        VaryDuration = this.C("VaryDuration"), MinimumDurationSteps = (double)this.N("MinimumDurationSteps"), MaximumDurationSteps = (double)this.N("MaximumDurationSteps"),
        PitchEnabled = this.C("PitchEnabled"), MinimumPitchCents = (double)this.N("MinimumPitchCents"), MaximumPitchCents = (double)this.N("MaximumPitchCents"), PitchStepCents = (double)this.N("PitchStepCents"),
        TimeStretchEnabled = this.C("TimeStretchEnabled"), MinimumStretch = (double)this.N("MinimumStretch"), MaximumStretch = (double)this.N("MaximumStretch"),
        VarispeedEnabled = this.C("VarispeedEnabled"), MinimumSpeed = (double)this.N("MinimumSpeed"), MaximumSpeed = (double)this.N("MaximumSpeed"),
        CutEnabled = this.C("CutEnabled"), MaximumCutOffsetMs = (double)this.N("MaximumCutOffsetMs"), ReverseEnabled = this.C("ReverseEnabled"),
        FitToNoteDuration = this.C("FitToNoteDuration"), TruncateNotes = this.C("TruncateNotes"), KeepEndTails = this.C("KeepEndTails"),
        NormalizeOutput = this.C("NormalizeOutput"), OutputGainDb = (double)this.N("OutputGainDb")
    };

    private QuantumCircuitRandomization CaptureRandomization() => new((int)this.N("Layers"), (double)this.N("HadamardChance") / 100,
        (double)this.N("EntanglementChance") / 100, (double)this.N("MinimumAngleDegrees") * Math.PI / 180,
        (double)this.N("MaximumAngleDegrees") * Math.PI / 180, (int)this.N("Seed"));

    private void EditorChanged()
    {
        if (this.loading || this.closing) return;
        this.RefreshCircuit(); this.ScheduleSave(); this.UpdateAvailability();
    }

    private void RefreshCircuit()
    {
        if (this.released) return;
        try
        {
            var circuit = this.CaptureCircuit();
            this.logicalQasmText.Text = QuantumCircuitComposer.Build(circuit);
            this.summaryLabel.Text = FormattableString.Invariant($"{this.samples.Count} samples · {circuit.QubitCount} qubits · {circuit.Gates.Count(gate => gate.Enabled)} gates · {(int)this.N("Bars") * (int)this.N("StepsPerBar")} steps");
            this.DrawCircuit();
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        { this.logicalQasmText.Text = "// Invalid circuit: check qubit indices, CX control/target and rotation angles."; this.SetStatus("The circuit is not valid yet. Check the gate table and qubit count.", true); }
        this.numbers["Qubits"].Enabled = !this.C("AutoQubits") && !this.busy;
        this.hardwareQasmText.ReadOnly = !this.C("UsePreparedIsa") || this.busy;
    }

    private void DrawCircuit()
    {
        try { this.ReplaceImage(this.circuitPicture, QuantumDiagramRenderer.Circuit(this.CaptureCircuit(false), this.gatesGrid.CurrentCell?.RowIndex ?? -1)); }
        catch (ArgumentException) { }
    }
    private void RefreshSequenceImage() => this.ReplaceImage(this.sequencePicture, QuantumDiagramRenderer.Sequence(this.sequence,
        this.samples.Select(sample => sample.Name).ToArray(), this.sequence?.Options.Bars ?? (int)this.N("Bars"), this.sequence?.Options.StepsPerBar ?? (int)this.N("StepsPerBar")));
    private void ReplaceImage(PictureBox picture, Image image) { Image? old = picture.Image; picture.Image = image; old?.Dispose(); }

    private void AddGate()
    {
        if (this.busy || this.gateRows.Count >= 512) return;
        this.gateRows.Add(new() { Gate = this.gateCombo.Text, Qubit = (int)this.N("GateQubit"), Target = (int)this.N("GateTarget"), AngleDegrees = (double)this.N("GateAngle") });
        this.EditorChanged();
    }
    private void RemoveGate()
    {
        if (!this.busy && this.gatesGrid.CurrentCell is { RowIndex: >= 0 } cell) { this.gateRows.RemoveAt(cell.RowIndex); this.EditorChanged(); }
    }
    private void MoveGate(int direction)
    {
        if (this.busy || this.gatesGrid.CurrentCell is not { RowIndex: >= 0 } cell) return;
        int index = cell.RowIndex, target = index + direction;
        if (target < 0 || target >= this.gateRows.Count) return;
        var row = this.gateRows[index]; this.gateRows.RemoveAt(index); this.gateRows.Insert(target, row);
        this.gatesGrid.CurrentCell = this.gatesGrid.Rows[target].Cells[1]; this.EditorChanged();
    }
    private void ReplaceGates(IReadOnlyList<QuantumGateOperation> operations)
    {
        if (this.busy) return;
        bool previous = this.loading; this.loading = true;
        this.gateRows.Clear(); foreach (var gate in operations) this.gateRows.Add(QuantumGateEditorRow.From(gate));
        this.loading = previous; this.EditorChanged();
    }
    private void LoadPreset()
    {
        try { this.ReplaceGates(QuantumCircuitComposer.CreatePreset(this.presetCombo.Text, (int)this.N("Qubits"), (double)this.N("Density") / 100).Gates); }
        catch (Exception error) { this.ShowError(error); }
    }
    private void RandomizeCircuit()
    {
        try { this.ReplaceGates(QuantumCircuitComposer.Randomize((int)this.N("Qubits"), this.CaptureRandomization()).Gates); }
        catch (Exception error) { this.ShowError(error); }
    }
    private void SelectCircuitPosition(MouseEventArgs args)
    {
        if (this.busy) return;
        int qubit = (int)Math.Round((args.Y - QuantumDiagramRenderer.CircuitTop) / (double)QuantumDiagramRenderer.WireHeight);
        if (qubit >= 0 && qubit < (int)this.N("Qubits")) this.SetNumber(args.Button == MouseButtons.Right ? "GateTarget" : "GateQubit", qubit);
        int column = (args.X - QuantumDiagramRenderer.CircuitLeft) / QuantumDiagramRenderer.GateWidth(this.gateRows.Count);
        if (args.X >= QuantumDiagramRenderer.CircuitLeft && column >= 0 && column < this.gateRows.Count)
            this.gatesGrid.CurrentCell = this.gatesGrid.Rows[column].Cells[1];
    }

    internal Task GenerateAsync() => this.RunWorkAsync(async token =>
    {
        var circuit = this.CaptureCircuit();
        var options = this.CaptureOptions(); var tracks = this.trackRows.Select(row => row.ToMapping()).ToArray();
        options.Validate(circuit.QubitCount, tracks);
        if (this.samples.Any(sample => sample.Data.Length < sample.Channels || sample.SampleRate <= 0 || sample.Channels < 1))
            throw new ArgumentException("Select samples containing complete audio frames.");
        bool cloud = this.providerCombo.SelectedIndex == 1;
        if (cloud && (!this.C("AllowCloudJobs") || !this.session.IsConnected))
            throw new InvalidOperationException("Connect to IBM and explicitly allow cloud jobs before generating.");
        string fingerprint = QuantumCircuitComposer.Build(circuit with { Version = OpenQasmVersion.Version2 });
        var execution = cloud
            ? await this.session.ExecuteIbmAsync(circuit, (int)this.N("Shots"), this.backendCombo.Text.Trim(), this.pythonText.Text.Trim(),
                (int)this.N("OptimizationLevel"), options.Seed, this.C("UsePreparedIsa") ? this.hardwareQasmText.Text : null, token)
            : await this.session.ExecuteLocalAsync(circuit, (int)this.N("Shots"), this.C("FreshLocalSeed") ? null : options.Seed, token);
        token.ThrowIfCancellationRequested();
        this.lastExecution = execution; this.measuredFingerprint = fingerprint; this.measuredQubits = circuit.QubitCount;
        if (cloud && !this.C("UsePreparedIsa"))
        {
            this.loading = true; this.hardwareQasmText.Text = execution.ExecutedQasm; this.loading = false;
        }
        this.countsGrid.DataSource = execution.Job.Counts.OrderByDescending(pair => pair.Value).Select(pair => new
        { Bitstring = pair.Key, Shots = pair.Value, Probability = pair.Value / (double)execution.Job.Counts.Sum(count => (long)count.Value) }).ToArray();
        await this.MapAndDisplayAsync(circuit, options, tracks, token);
        this.SetStatus(FormattableString.Invariant($"Generated {this.sequence!.Notes.Count} notes from {execution.Job.Counts.Count} measured states. Job: {execution.Job.JobId}. Compilation: {execution.CompilationLatency.TotalMilliseconds:0} ms."));
        this.ScheduleSave();
    });

    internal Task RemapAsync() => this.RunWorkAsync(async token =>
    {
        var circuit = this.CaptureCircuit();
        if (this.lastExecution is null || circuit.QubitCount != this.measuredQubits ||
            QuantumCircuitComposer.Build(circuit with { Version = OpenQasmVersion.Version2 }) != this.measuredFingerprint)
            throw new InvalidOperationException("Measure this circuit before remapping its results.");
        await this.MapAndDisplayAsync(circuit, this.CaptureOptions(), this.trackRows.Select(row => row.ToMapping()).ToArray(), token);
        this.SetStatus("Remapped cached measurements. No network request or quantum job was submitted.");
    });

    private async Task MapAndDisplayAsync(QuantumCircuitDefinition circuit, QuantumBeatOptions options, QuantumTrackMapping[] tracks, CancellationToken token)
    {
        var execution = this.lastExecution!;
        QuantumBeatSequence mapped = await Task.Run(() => new QuantumArrangementMapper().Map(execution.ExecutedQasm, execution.Job,
            circuit.QubitCount, options, tracks, token), token);
        token.ThrowIfCancellationRequested();
        this.sequence = mapped;
        this.eventsGrid.DataSource = mapped.Notes.Select(note => new
        {
            Track = mapped.Tracks[note.TrackIndex].Name, Bar = (int)(note.StartStep / options.StepsPerBar) + 1,
            Step = Math.Round(note.StartStep % options.StepsPerBar + 1, 3), StartMs = Math.Round(note.StartStep * 240_000 / options.Bpm / options.StepsPerBar, 2),
            DurationMs = Math.Round(note.DurationSteps * 240_000 / options.Bpm / options.StepsPerBar, 2),
            Velocity = Math.Round(note.Velocity, 3), PitchCents = Math.Round(note.PitchShiftCents, 1),
            Stretch = Math.Round(note.TimeStretchFactor, 3), Speed = Math.Round(note.VarispeedFactor, 3), CutMs = Math.Round(note.SliceCutOffsetMs, 2),
            note.Reverse, Ghost = note.IsGhost, note.Measurement
        }).ToArray();
        this.RefreshSequenceImage();
        this.Stop(); this.rendered?.Dispose(); this.rendered = null;
        if (this.C("AutoRender")) { await this.RenderCurrentSequenceAsync(token); if (this.C("AutoPlay")) this.PlayRendered(); }
    }

    internal Task RenderAsync() => this.RunWorkAsync(async token =>
    {
        if (this.sequence is null) throw new InvalidOperationException("Generate a sequence before rendering audio.");
        await this.RenderCurrentSequenceAsync(token); this.SetStatus("Rendered the current quantum sequence. Use Play or Add to collection.");
    });

    private async Task RenderCurrentSequenceAsync(CancellationToken token)
    {
        this.Stop(); this.SetStatus("Rendering quantum DSP variations and mixing sample tracks...");
        AudioObj? audio = await QuantumSequenceAudioRenderer.RenderAsync(this.sequence!, this.samples, token);
        try { token.ThrowIfCancellationRequested(); this.rendered?.Dispose(); this.rendered = audio; audio = null; }
        finally { audio?.Dispose(); }
    }

    private void Play()
    {
        if (this.busy || this.rendered is null) return;
        try { this.PlayRendered(); this.SetStatus("Playing the rendered quantum breakbeat."); }
        catch (Exception error) { this.ShowError(error); }
    }
    private void PlayRendered() { if (this.rendered is not null) this.player.Play(this.rendered.Data, this.rendered.SampleRate, this.rendered.Channels, this.C("LoopPlayback")); }
    private void Stop() { this.player.Stop(); }
    private void ExportToCollection()
    {
        if (this.rendered is null || this.busy) return;
        AudioObj copy = this.rendered.Clone();
        if (this.outputCollection is null || this.outputCollection.IsDisposed) this.outputCollection = new([copy], "Quantum Breakbeats");
        else { this.outputCollection.AudioC.Audios.Add(copy); this.outputCollection.Show(); }
        this.SetStatus("Added an independent copy to the Quantum Breakbeats collection.");
    }
    private void OpenWaveform() { if (this.rendered is not null && !this.busy) new TrackView(this.rendered.Clone()).Show(); }
    private void CopyText(string text) { try { if (!string.IsNullOrEmpty(text)) Clipboard.SetText(text); } catch { this.SetStatus("The clipboard is unavailable. Try copying again.", true); } }

    private Task RunWorkAsync(Func<CancellationToken, Task> action)
    {
        if (this.busy || this.closing || this.released) return Task.CompletedTask;
        this.activeWork = this.PerformWorkAsync(action); return this.activeWork;
    }
    private async Task PerformWorkAsync(Func<CancellationToken, Task> action)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(this.lifetime.Token);
        this.workCts = cts; this.busy = true; this.operationClock.Restart(); this.elapsedTimer.Start(); this.UpdateAvailability();
        try { await action(cts.Token); }
        catch (OperationCanceledException) { this.SetStatus("Operation cancelled. A submitted IBM job may still be queued or running; check IBM Quantum Platform."); }
        catch (Exception error) { this.ShowError(error); }
        finally
        {
            this.workCts = null; this.busy = false; this.operationClock.Stop();
            if (!this.released)
            {
                this.elapsedTimer.Stop();
                this.delayLabel.Text = this.lastExecution is null ? "Response delay: — ms" : FormattableString.Invariant($"Response delay: {this.lastExecution.Job.ExecutionLatency.TotalMilliseconds:0.0} ms");
                this.UpdateAvailability();
            }
        }
    }

    private void UpdateAvailability()
    {
        if (this.released) return;
        this.editorTabs.Enabled = !this.busy;
        this.connectionMenu.Enabled = !this.busy; this.editMenu.Enabled = !this.busy; this.circuitMenu.Enabled = !this.busy;
        this.patternMenu.Enabled = !this.busy; this.outputMenu.Enabled = !this.busy;
        this.generateButton.Enabled = !this.busy && this.samples.Count > 0 && (this.providerCombo.SelectedIndex == 0 || this.session.IsConnected && this.C("AllowCloudJobs"));
        this.remapButton.Enabled = !this.busy && this.lastExecution is not null;
        this.cancelButton.Enabled = this.busy;
        this.renderButton.Enabled = !this.busy && this.sequence is not null;
        this.playButton.Enabled = this.exportButton.Enabled = !this.busy && this.rendered is not null;
        this.stopButton.Enabled = this.rendered is not null;
        this.providerCombo.Enabled = this.qasmVersionCombo.Enabled = !this.busy; this.numbers["Shots"].Enabled = !this.busy;
        this.checks["FreshLocalSeed"].Enabled = !this.busy && this.providerCombo.SelectedIndex == 0;
        this.hardwareQasmText.ReadOnly = this.busy || !this.C("UsePreparedIsa");
        this.renameItem.Enabled = this.loadedExperimentName is not null;
        this.deleteResetItem.Text = this.loadedExperimentName is null ? "Reset current setup" : "Delete saved experiment...";
        this.connectionLabel.Text = this.session.IsConnected ? "IBM CONNECTED · " + this.session.ConnectedBackend
            : this.providerCombo.SelectedIndex == 0 ? "LOCAL SIMULATOR · no cloud job" : "IBM DISCONNECTED · connect and allow jobs";
    }

    private void SetStatus(string text, bool warning = false)
    {
        if (this.released || this.IsDisposed) return;
        this.statusLabel.Text = text; this.statusLabel.ForeColor = warning ? Color.Firebrick : SystemColors.ControlText;
    }
    private void ShowError(Exception error) => this.SetStatus(EnglishError(error), true);
    internal static string EnglishError(Exception error) => error switch
    {
        CryptographicException => "Saved credentials could not be decrypted for this Windows user. Re-enter the credential and save the connection profile again.",
        HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } => "IBM authorization failed. Check the API key / IAM token, instance CRN and access permissions.",
        HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } => "IBM Quantum is rate limiting requests. Wait before submitting another job.",
        HttpRequestException => "The IBM request failed. Check the endpoint, connection and selected backend. A submitted job may still exist.",
        TimeoutException => "The operation timed out. A submitted IBM job may still be running; check the platform before resubmitting.",
        JsonException or InvalidDataException => "The JSON data or saved workspace is invalid or unsupported. Existing files were kept; load another preset or reset the current setup.",
        UnauthorizedAccessException or IOException => "Unable to read or save quantum settings. Check the AppData path, file name, existing names and access permissions.",
        ArgumentException => "Invalid circuit, mapping or numeric settings. Check control/target qubits, range minima/maxima, sample formats and the 1,024-step limit.",
        InvalidOperationException => "The requested operation is not ready. Check the circuit, cached results, IBM connection, cloud-job permission and Qiskit Python environment. Open Help for setup instructions.",
        _ => "The quantum operation could not complete. Check the current settings and try again."
    };

    private void ClosingStudio(object? sender, FormClosingEventArgs args)
    {
        if (this.closing) return;
        if (!this.closeAfterSaveFailure)
        {
            try { this.SaveCurrentWorkspace(); this.SaveConnectionIfChanged(); }
            catch
            {
                args.Cancel = true; this.closeAfterSaveFailure = true;
                this.SetStatus("Settings could not be saved. Check AppData permissions, or close this window again to discard unsaved changes.", true); return;
            }
        }
        this.closing = true; this.lifetime.Cancel(); this.workCts?.Cancel(); this.Stop();
    }

    private void ReleaseResources()
    {
        if (this.released) return;
        this.released = true; this.saveTimer.Stop(); this.elapsedTimer.Stop(); this.saveTimer.Dispose(); this.elapsedTimer.Dispose();
        this.lifetime.Cancel(); this.workCts?.Cancel(); this.player.Dispose(); this.rendered?.Dispose(); this.rendered = null;
        this.circuitPicture?.Image?.Dispose(); this.sequencePicture?.Image?.Dispose();
        foreach (var sample in this.samples) sample.Dispose();
        this.credentialText?.Clear();
        if (this.activeWork.IsCompleted) this.session.Dispose(); else _ = DisposeSessionAfterAsync(this.activeWork, this.session);
        this.lifetime.Dispose();
    }
    private static async Task DisposeSessionAfterAsync(Task work, IQuantumBeatSession session)
    { try { await work.ConfigureAwait(false); } catch { } finally { session.Dispose(); } }
}
