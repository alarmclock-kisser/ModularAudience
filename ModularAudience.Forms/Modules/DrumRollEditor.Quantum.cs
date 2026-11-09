using System.Collections.Concurrent;
using System.Net;
using ModularAudience.Audio;
using ModularAudience.Forms.Quantum;
using ModularAudience.Quantum.Abstractions;
using ModularAudience.Quantum.Models;

namespace ModularAudience.Forms.Modules;

public partial class DrumRollEditor
{
    private readonly ConcurrentDictionary<AudioObj, PreparedQuantumPattern> quantumPatterns = new();
    private readonly CancellationTokenSource quantumLifetime = new();
    private readonly int quantumUiThreadId = Environment.CurrentManagedThreadId;
    private readonly IQuantumPatternService? quantumService;
    private CancellationTokenSource? quantumGenerationCts;
    private ToolStripMenuItem? quantumPatternMenuItem;
    private ToolStripMenuItem? clearQuantumPatternMenuItem;
    private bool quantumGenerationInProgress;
    private bool quantumResourcesDisposed;

    /// <summary>Generates and commits per-step DSP for an existing row. Call on the editor's UI thread.</summary>
    public async Task<QuantumPatternResult> ApplyQuantumPatternToRowAsync(int rowIndex, int qubits, int shots,
        CancellationToken cancellationToken)
    {
        if (Environment.CurrentManagedThreadId != this.quantumUiThreadId)
            throw new InvalidOperationException("Quantum pattern changes must be started on the editor's UI thread.");
        ObjectDisposedException.ThrowIf(this.quantumResourcesDisposed || this.IsDisposed, this);
        if (this.quantumService is null) throw new InvalidOperationException("No quantum pattern service has been registered.");
        if (this.quantumGenerationInProgress) throw new InvalidOperationException("A quantum pattern is already being generated.");
        if (rowIndex < 0 || rowIndex >= this.patternRows.Count) throw new ArgumentOutOfRangeException(nameof(rowIndex), "Select an existing sample row.");
        if (this.patternRows[rowIndex].IsLocked) throw new InvalidOperationException("Unlock the row before applying a quantum pattern.");
        cancellationToken.ThrowIfCancellationRequested();

        AudioObj source = this.patternRows[rowIndex].Audio;
        float[] originalData = source.Data;
        int originalRate = source.SampleRate, originalChannels = source.Channels;
        int steps = this.TotalSteps;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, this.quantumLifetime.Token);
        this.quantumGenerationInProgress = true;
        string previousInfo = this.label_info_dragndrop.Text;
        this.label_info_dragndrop.Text = "Generating quantum pattern...";
        PreparedQuantumPattern? prepared = null;
        try
        {
            QuantumPatternResult result = await this.quantumService.GenerateBreakbeatAsync(qubits, shots, linked.Token, steps);
            linked.Token.ThrowIfCancellationRequested();
            prepared = await QuantumAudioPatternRenderer.PrepareAsync(source, result, linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            PatternRowState? row = this.patternRows.FirstOrDefault(candidate => ReferenceEquals(candidate.Audio, source));
            if (this.quantumResourcesDisposed || row is null || row.IsLocked || this.TotalSteps != steps || prepared.Steps.Length != steps ||
                !ReferenceEquals(source.Data, originalData) || source.SampleRate != originalRate || source.Channels != originalChannels)
                throw new InvalidOperationException("The sample or grid changed while the quantum pattern was being generated. Try again.");

            bool resume = this.isPlaying;
            this.StopPlayback();
            this.ClearQuantumPattern(source);
            this.quantumPatterns[source] = prepared;
            prepared.Steps.CopyTo(row.Steps, 0);
            prepared = null;
            this.panel_pattern.Invalidate();
            if (resume) this.StartPlayback();
            return result;
        }
        finally
        {
            prepared?.Dispose();
            this.quantumGenerationInProgress = false;
            if (!this.quantumResourcesDisposed && !this.IsDisposed) this.label_info_dragndrop.Text = previousInfo;
        }
    }

    private void InitializeQuantumMenu()
    {
        this.quantumPatternMenuItem = new ToolStripMenuItem("Generate Quantum Pattern...");
        this.quantumPatternMenuItem.Click += this.GenerateQuantumPattern_Click;
        this.clearQuantumPatternMenuItem = new ToolStripMenuItem("Clear Quantum DSP");
        this.clearQuantumPatternMenuItem.Click += (_, _) =>
        {
            if (this.contextMenuRowIndex >= 0 && this.contextMenuRowIndex < this.patternRows.Count)
                this.ClearQuantumPattern(this.patternRows[this.contextMenuRowIndex].Audio);
        };
        this.rowContextMenu!.Items.Add(new ToolStripSeparator());
        this.rowContextMenu.Items.Add(this.quantumPatternMenuItem);
        this.rowContextMenu.Items.Add(this.clearQuantumPatternMenuItem);
        this.rowContextMenu.Opening += (_, _) =>
        {
            bool validRow = this.contextMenuRowIndex >= 0 && this.contextMenuRowIndex < this.patternRows.Count;
            this.quantumPatternMenuItem.Text = this.quantumGenerationCts is null ? "Generate Quantum Pattern..." : "Cancel Quantum Generation";
            this.quantumPatternMenuItem.Enabled = this.quantumGenerationCts is not null ||
                (this.quantumService is not null && validRow && !this.patternRows[this.contextMenuRowIndex].IsLocked && !this.quantumGenerationInProgress);
            this.clearQuantumPatternMenuItem.Enabled = validRow && !this.patternRows[this.contextMenuRowIndex].IsLocked &&
                this.quantumPatterns.ContainsKey(this.patternRows[this.contextMenuRowIndex].Audio);
        };
    }

    private async void GenerateQuantumPattern_Click(object? sender, EventArgs e)
    {
        if (this.quantumGenerationCts is not null) { this.quantumGenerationCts.Cancel(); return; }
        using var cts = new CancellationTokenSource();
        this.quantumGenerationCts = cts;
        try
        {
            await this.ApplyQuantumPatternToRowAsync(this.contextMenuRowIndex, 4, 1024, cts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!this.quantumResourcesDisposed && !this.IsDisposed)
                MessageBox.Show(this, GetQuantumErrorMessage(ex), "Quantum Pattern Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { this.quantumGenerationCts = null; }
    }

    // Framework/provider exception messages can be localized or contain credentials. Never display them verbatim.
    internal static string GetQuantumErrorMessage(Exception error) => error switch
    {
        TimeoutException => "Quantum pattern generation timed out. A remote IBM job may still be queued or running.",
        HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } =>
            "IBM Quantum authorization failed. Check your credentials, instance CRN and access permissions.",
        HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } =>
            "IBM Quantum is rate limiting requests. Wait before generating another pattern.",
        HttpRequestException { StatusCode: not null } =>
            "The quantum provider rejected the request. Check the backend, shot count and hardware ISA circuit.",
        HttpRequestException => "The quantum provider could not be reached. Check the connection and try again.",
        NotSupportedException => "The quantum provider does not support this circuit. Check its gates and measurement format.",
        ArgumentException => "The sample or quantum settings are invalid. Use a valid audio sample and supported settings.",
        InvalidOperationException => "Quantum generation could not complete. Check the row lock, sample and grid, then try again.",
        _ => "Quantum pattern generation failed. Please try again."
    };

    private AudioObj ResolveStepAudio(int trackIndex, int stepIndex)
    {
        AudioObj source = this.AudioC.Audios[trackIndex];
        return this.quantumPatterns.TryGetValue(source, out var pattern) && pattern.Samples.TryGetValue(stepIndex, out var rendered)
            ? rendered : source;
    }

    private void ClearQuantumPattern(AudioObj source)
    {
        if (this.quantumPatterns.TryRemove(source, out var previous)) previous.Dispose();
    }

    private void PruneQuantumPatterns(IReadOnlyCollection<AudioObj> audios, int stepCount)
    {
        foreach (var (source, pattern) in this.quantumPatterns)
            if (!audios.Contains(source) || pattern.Steps.Length != stepCount) this.ClearQuantumPattern(source);
    }

    private void DisposeQuantumResources()
    {
        if (this.quantumResourcesDisposed) return;
        this.quantumResourcesDisposed = true;
        this.quantumLifetime.Cancel();
        this.quantumGenerationCts?.Cancel();
        foreach (AudioObj source in this.quantumPatterns.Keys) this.ClearQuantumPattern(source);
        this.quantumLifetime.Dispose();
    }
}
