using System.Security.Cryptography;
using ModularAudience.Forms.Quantum;
using ModularAudience.Quantum.Models;
using ModularAudience.Quantum.Services;

namespace ModularAudience.Forms.Modules.Dialogs;

public partial class QuantumBeatEngineDialog
{
    private readonly Guid recoverySessionId = Guid.NewGuid();

    private QuantumStudioWorkspace DefaultWorkspace()
    {
        var tracks = this.samples.Select((sample, index) => new QuantumTrackMapping(
            string.IsNullOrWhiteSpace(sample.Name) ? $"Sample {index + 1}" : sample.Name[..Math.Min(sample.Name.Length, 256)], index % 4)).ToArray();
        return new(new QuantumExperimentDocument(1, QuantumCircuitComposer.CreatePreset("Bell break", 4), new QuantumBeatOptions(), tracks,
            new QuantumCircuitRandomization()));
    }

    internal QuantumStudioWorkspace CaptureWorkspace() => new(
        new QuantumExperimentDocument(1, this.CaptureCircuit(false), this.CaptureOptions(), this.trackRows.Select(row => row.ToMapping()).ToArray(), this.CaptureRandomization()),
        (int)this.N("Shots"), this.C("FreshLocalSeed"), this.C("AutoSave"), this.C("AutoRender"), this.C("AutoPlay"), this.C("LoopPlayback"),
        this.providerCombo.SelectedIndex == 1, this.C("UsePreparedIsa"), this.C("UsePreparedIsa") ? this.hardwareQasmText.Text : "", (int)this.N("OptimizationLevel"),
        this.C("AutoQubits"), this.presetCombo.Text, string.IsNullOrWhiteSpace(this.connectionProfileCombo.Text) ? "Default" : this.connectionProfileCombo.Text.Trim(),
        this.loadedExperimentName, this.editorTabs.SelectedIndex, this.RestoreBounds.Width > 0 ? this.RestoreBounds.Width : this.Width,
        this.RestoreBounds.Height > 0 ? this.RestoreBounds.Height : this.Height);

    private void ApplyWorkspace(QuantumStudioWorkspace workspace)
    {
        bool previous = this.loading; this.loading = true;
        try
        {
            var project = workspace.Experiment; var options = project.Options;
            this.SetNumber("Qubits", project.Circuit.QubitCount);
            this.qasmVersionCombo.SelectedIndex = project.Circuit.Version == OpenQasmVersion.Version3 ? 1 : 0;
            this.gateRows.Clear(); foreach (var gate in project.Circuit.Gates) this.gateRows.Add(QuantumGateEditorRow.From(gate));
            this.trackRows.Clear();
            var defaults = this.DefaultWorkspace().Experiment.Tracks;
            bool sameTracks = project.Tracks.Count == defaults.Count && project.Tracks.Select(track => track.Name).SequenceEqual(defaults.Select(track => track.Name));
            foreach (var track in sameTracks ? project.Tracks : defaults) this.trackRows.Add(QuantumTrackEditorRow.From(track));
            var values = new Dictionary<string, double>
            {
                ["Bars"] = options.Bars, ["StepsPerBar"] = options.StepsPerBar, ["Bpm"] = options.Bpm, ["Seed"] = options.Seed,
                ["Density"] = options.Density * 100, ["Swing"] = options.Swing * 100, ["ModifierQubit"] = options.ModifierQubit,
                ["MinimumVelocity"] = options.MinimumVelocity * 100, ["MaximumVelocity"] = options.MaximumVelocity * 100,
                ["GhostChance"] = options.GhostChance * 100, ["GhostVelocityScale"] = options.GhostVelocityScale * 100,
                ["MinimumDurationSteps"] = options.MinimumDurationSteps, ["MaximumDurationSteps"] = options.MaximumDurationSteps,
                ["MinimumPitchCents"] = options.MinimumPitchCents, ["MaximumPitchCents"] = options.MaximumPitchCents, ["PitchStepCents"] = options.PitchStepCents,
                ["MinimumStretch"] = options.MinimumStretch, ["MaximumStretch"] = options.MaximumStretch, ["MinimumSpeed"] = options.MinimumSpeed,
                ["MaximumSpeed"] = options.MaximumSpeed, ["MaximumCutOffsetMs"] = options.MaximumCutOffsetMs, ["OutputGainDb"] = options.OutputGainDb,
                ["Layers"] = project.Randomization.Layers, ["HadamardChance"] = project.Randomization.HadamardChance * 100,
                ["EntanglementChance"] = project.Randomization.EntanglementChance * 100, ["MinimumAngleDegrees"] = project.Randomization.MinimumAngleRadians * 180 / Math.PI,
                ["MaximumAngleDegrees"] = project.Randomization.MaximumAngleRadians * 180 / Math.PI, ["Shots"] = workspace.Shots, ["OptimizationLevel"] = workspace.OptimizationLevel
            };
            foreach (var (key, value) in values) this.SetNumber(key, value);
            var flags = new Dictionary<string, bool>
            {
                ["Interleaved"] = options.Interleaved, ["QuantumVelocity"] = options.QuantumVelocity, ["GhostNotes"] = options.GhostNotes,
                ["VaryDuration"] = options.VaryDuration, ["PitchEnabled"] = options.PitchEnabled, ["TimeStretchEnabled"] = options.TimeStretchEnabled,
                ["VarispeedEnabled"] = options.VarispeedEnabled, ["CutEnabled"] = options.CutEnabled, ["ReverseEnabled"] = options.ReverseEnabled,
                ["FitToNoteDuration"] = options.FitToNoteDuration, ["TruncateNotes"] = options.TruncateNotes, ["KeepEndTails"] = options.KeepEndTails,
                ["NormalizeOutput"] = options.NormalizeOutput, ["AutoSave"] = workspace.AutoSave, ["AutoRender"] = workspace.AutoRender,
                ["AutoPlay"] = workspace.AutoPlay, ["LoopPlayback"] = workspace.LoopPlayback, ["FreshLocalSeed"] = workspace.FreshLocalSeed,
                ["AutoQubits"] = workspace.AutoQubits, ["UsePreparedIsa"] = workspace.UsePreparedIsa
            };
            foreach (var (key, value) in flags) this.checks[key].Checked = value;
            this.checks["AllowCloudJobs"].Checked = false;
            this.arrangementCombo.SelectedIndex = Math.Clamp((int)options.Arrangement, 0, 2);
            this.providerCombo.SelectedIndex = workspace.UseIbm ? 1 : 0;
            this.presetCombo.SelectedIndex = Math.Max(0, this.presetCombo.Items.IndexOf(workspace.CircuitPreset));
            this.hardwareQasmText.Text = workspace.PreparedIsa;
            this.connectionProfileCombo.Text = workspace.ConnectionProfile ?? "Default";
            this.loadedExperimentName = workspace.LoadedExperimentName;
            this.editorTabs.SelectedIndex = Math.Clamp(workspace.SelectedTab, 0, this.editorTabs.TabCount - 1);
            var area = Screen.FromControl(this).WorkingArea;
            this.Size = new Size(Math.Clamp(workspace.WindowWidth, this.MinimumSize.Width, Math.Max(this.MinimumSize.Width, area.Width)),
                Math.Clamp(workspace.WindowHeight, this.MinimumSize.Height, Math.Max(this.MinimumSize.Height, area.Height)));
            this.Text = "Quantum Beat Engine" + (this.loadedExperimentName is null ? "" : " — " + this.loadedExperimentName);
        }
        finally { this.loading = previous; }
    }

    private void RestorePersistentState()
    {
        try
        {
            var saved = this.store.LoadWorkspace();
            if (saved is not null) { this.ApplyWorkspace(saved); this.SetStatus("Workspace restored. No connection or cloud job has been started."); }
        }
        catch
        {
            this.ApplyWorkspace(this.DefaultWorkspace()); this.suppressAutomaticSave = true;
            this.SetStatus("The saved workspace could not be loaded. It will be kept; new settings will be saved in the Recovery folder until Edit > Reset.", true);
        }
        this.RefreshConnectionProfileNames();
        try
        {
            if (this.store.ConnectionNames().Contains(this.connectionProfileCombo.Text, StringComparer.OrdinalIgnoreCase))
                this.ApplyConnection(this.store.LoadConnection(this.connectionProfileCombo.Text));
        }
        catch (Exception error) { this.ShowError(error); }
        this.workspaceDirty = this.connectionDirty = false;
    }

    private void ScheduleSave()
    {
        if (this.loading || this.closing || this.released) return;
        this.workspaceDirty = true;
        this.Text = "Quantum Beat Engine" + (this.loadedExperimentName is null ? "" : " — " + this.loadedExperimentName) + " *";
        if (this.C("AutoSave")) { this.saveTimer.Stop(); this.saveTimer.Start(); }
    }

    private void TrySaveWorkspace()
    {
        if (this.loading || this.released || !this.workspaceDirty) return;
        try { this.SaveCurrentWorkspace(); this.SaveConnectionIfChanged(); }
        catch (Exception error) { this.ShowError(error); }
    }

    internal void SaveCurrentWorkspace()
    {
        var workspace = this.CaptureWorkspace();
        if (this.suppressAutomaticSave) this.store.SaveRecoveryWorkspace(workspace, this.recoverySessionId);
        else this.store.SaveWorkspace(workspace);
        if (workspace.AutoSave && this.loadedExperimentName is not null)
        {
            try { this.store.SaveExperiment(this.loadedExperimentName, workspace); }
            catch (ArgumentException) { this.SetStatus("Draft workspace saved. The named preset was kept unchanged because the circuit or mapping is not valid yet.", true); }
        }
        this.workspaceDirty = false;
        this.Text = "Quantum Beat Engine" + (this.loadedExperimentName is null ? "" : " — " + this.loadedExperimentName);
    }

    private void SaveExperiment()
    {
        if (this.loadedExperimentName is null) { this.ShowLibrary("Save"); return; }
        try { this.SaveNamedExperiment(this.loadedExperimentName); }
        catch (Exception error) { this.ShowError(error); }
    }

    internal void SaveNamedExperiment(string name)
    {
        this.store.SaveExperiment(name, this.CaptureWorkspace() with { LoadedExperimentName = name });
        this.loadedExperimentName = name; this.workspaceDirty = true; this.SaveCurrentWorkspace();
        this.UpdateAvailability(); this.SetStatus("Saved experiment '" + name + "'. Credentials are stored separately and encrypted.");
    }

    internal void LoadNamedExperiment(string name)
    {
        if (this.busy) throw new InvalidOperationException("Cancel the current operation before loading an experiment.");
        var workspace = this.store.LoadExperiment(name);
        this.Stop(); this.session.Disconnect(); this.ApplyWorkspace(workspace);
        this.sequence = null; this.lastExecution = null; this.rendered?.Dispose(); this.rendered = null;
        this.eventsGrid.DataSource = this.countsGrid.DataSource = null;
        this.RefreshCircuit(); this.RefreshSequenceImage(); this.ScheduleSave(); this.UpdateAvailability();
        this.SetStatus("Loaded experiment '" + name + "'. Source audio was not changed; reconnect explicitly before using IBM.");
    }

    internal void RenameNamedExperiment(string oldName, string newName)
    {
        if (this.busy) throw new InvalidOperationException("Wait for the current operation before renaming a saved experiment.");
        this.store.RenameExperiment(oldName, newName);
        if (string.Equals(this.loadedExperimentName, oldName, StringComparison.OrdinalIgnoreCase)) this.loadedExperimentName = newName;
        this.ScheduleSave(); this.UpdateAvailability(); this.SetStatus("Renamed saved experiment to '" + newName + "'.");
    }

    internal void DeleteNamedExperiment(string name)
    {
        if (this.busy) throw new InvalidOperationException("Wait for the current operation before deleting a saved experiment.");
        this.store.DeleteExperiment(name);
        if (string.Equals(this.loadedExperimentName, name, StringComparison.OrdinalIgnoreCase)) this.loadedExperimentName = null;
        this.ScheduleSave(); this.UpdateAvailability(); this.SetStatus("Deleted saved experiment '" + name + "'. The current editor setup remains available.");
    }

    private void ResetWorkspace()
    {
        if (this.busy) return;
        this.Stop(); this.session.Disconnect(); this.loadedExperimentName = null;
        this.ApplyWorkspace(this.DefaultWorkspace()); this.suppressAutomaticSave = false;
        this.sequence = null; this.lastExecution = null; this.rendered?.Dispose(); this.rendered = null;
        this.eventsGrid.DataSource = this.countsGrid.DataSource = null;
        this.RefreshCircuit(); this.RefreshSequenceImage(); this.ScheduleSave(); this.UpdateAvailability();
        this.SetStatus("Current setup reset. Saved experiments and encrypted connection profiles were not deleted.");
    }

    private void ShowLibrary(string action)
    {
        if (this.busy) return;
        if (this.libraryWindow is null || this.libraryWindow.IsDisposed) this.libraryWindow = new QuantumExperimentLibraryDialog(this, this.store);
        this.libraryWindow.SelectAction(action, this.loadedExperimentName); this.libraryWindow.Show(this); this.libraryWindow.BringToFront();
    }
}
