using ModularAudience.Audio;
using ModularAudience.Audio.Arrangement;
using ModularAudience.Audio.Processing;
using ModularAudience.Forms.Helpers;
using ModularAudience.Forms.Modules;

namespace ModularAudience.Forms.Modules.Dialogs;

public sealed partial class DecompileConfigDialog : Form
{
    private readonly AudioObj sourceSnapshot;
    private CancellationTokenSource? activeCancellation;
    private ProgressDialog? activeProgressDialog;
    private Task? activeRun;
    private Task? demucsInitializationTask;
    private bool closeRequested;
    private bool sourceSnapshotDisposed;
    private bool sourceSnapshotTransferred;
    private bool closingAcceptedRun;
    private readonly string settingsPath;

    public DecompileConfigDialog(AudioObj source)
        : this(source, DecompileDialogSettings.SettingsPath)
    {
    }

    internal DecompileConfigDialog(AudioObj source, string? settingsPath)
    {
        ArgumentNullException.ThrowIfNull(source);
        this.sourceSnapshot = source.Clone();
        this.settingsPath = string.IsNullOrWhiteSpace(settingsPath)
            ? DecompileDialogSettings.SettingsPath
            : settingsPath;
        try
        {
            this.InitializeComponent();
            this.LoadPersistentSettings();
            this.panel_separationContent.Controls.Add(this.panel_demucs);
            this.panel_separationContent.Controls.Add(this.panel_analog);
            this.panel_separationContent.Controls.Add(this.panel_deterministic);
            this.panel_atomizerContent.Controls.Add(this.panel_atomizerV4);
            this.panel_atomizerContent.Controls.Add(this.panel_atomizerLegacy);
            this.InitializeDecompileUi();
            this.ApplyPersistentSettings();
        }
        catch
        {
            this.sourceSnapshot.Dispose();
            this.sourceSnapshotDisposed = true;
            throw;
        }
    }

    internal void ShowIndependent()
    {
        this.Owner = null;
        this.Show();
    }

    internal AudioObj TransferSourceSnapshotOwnership()
    {
        if (this.sourceSnapshotTransferred || this.sourceSnapshotDisposed || this.sourceSnapshot.IsDisposed)
        {
            throw new InvalidOperationException("The decompile source snapshot has already been transferred or disposed.");
        }

        this.sourceSnapshotTransferred = true;
        return this.sourceSnapshot;
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        this.demucsInitializationTask = this.InitializeDemucsUiAsync();
        try
        {
            await this.demucsInitializationTask.ConfigureAwait(true);
        }
        finally
        {
            this.demucsInitializationTask = null;
            if (this.closeRequested && !this.IsDisposed && !this.Disposing)
            {
                this.BeginInvoke(this.Close);
            }
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        this.SavePersistentSettings();
        bool workIsRunning = this.activeRun is { IsCompleted: false }
            || this.analogBandAnalysis is { IsCompleted: false }
            || this.demucsInitializationTask is { IsCompleted: false };
        if (workIsRunning && !this.closingAcceptedRun)
        {
            this.closeRequested = true;
            this.activeCancellation?.Cancel();
            this.activeProgressDialog?.SetStatus("Cancelling and waiting for the current safe stage...");
            e.Cancel = true;
        }

        base.OnFormClosing(e);
    }

    private void InitializeDecompileUi()
    {
        this.Text = $"Audio Decompile - {GetSourceName(this.sourceSnapshot)}";
        this.comboBox_separationMethod.SelectedIndex = 0;
        this.comboBox_atomizer.SelectedIndex = 0;
        this.checkBox_projectOutput.Checked = true;
        this.checkBox_collectionOutput.Checked = false;
        this.checkBox_collectionDeduplicate.Checked = true;
        this.checkBox_atomizerDeduplicate.Checked = true;
        this.checkBox_atomizerDeduplicate.CheckedChanged += (_, _) =>
            this.checkBox_collectionDeduplicate.Checked = this.checkBox_atomizerDeduplicate.Checked;
        this.checkBox_collectionDeduplicate.CheckedChanged += (_, _) =>
            this.checkBox_atomizerDeduplicate.Checked = this.checkBox_collectionDeduplicate.Checked;
        this.InitializeAnalogUi();
        this.InitializeDeterministicUi();
        this.UpdateSeparationPanel();
        this.UpdateAtomizerPanel();
        this.UpdateOutputControls();
        this.UpdateBpmLabel();
    }

    private async void button_decompile_Click(object? sender, EventArgs e)
    {
        if (this.activeRun is { IsCompleted: false })
        {
            return;
        }

        AudioDecompileOptions? options = null;
        IAudioStemSeparator? separator = null;
        CancellationTokenSource? cancellation = null;
        ProgressDialog? progressDialog = null;
        AudioDecompileResult? result = null;
        AudioObj? runSource = null;
        try
        {
            options = this.ReadDecompileOptions();
            separator = this.CreateSeparator((int)this.numeric_workers.Value);
            options.Validate();
            this.SavePersistentSettings();
            cancellation = new CancellationTokenSource();
            this.activeCancellation = cancellation;
            string sourceName = GetSourceName(this.sourceSnapshot);
            progressDialog = new ProgressDialog(
                $"Decompiling '{sourceName}'...",
                new Progress<double>(),
                windowCloseDelay: 0.0d,
                ct: cancellation.Token,
                cancellationSource: cancellation);
            this.activeProgressDialog = progressDialog;
            var progress = new Progress<AudioDecompileProgress>(update =>
            {
                progressDialog.Report(update.Fraction);
                string status = string.IsNullOrWhiteSpace(update.Detail)
                    ? update.Stage
                    : $"{update.Stage} - {update.Detail}";
                progressDialog.SetStatus(status);
                if (!this.IsDisposed && !this.Disposing)
                {
                    this.label_status.Text = $"{update.Fraction:P0} - {status}";
                }
            });

            this.SetRunState(true);
            progressDialog.Show();
            runSource = this.TransferSourceSnapshotOwnership();
            Task<AudioDecompileResult> run = AudioDecompilePipeline.RunAsync(
                runSource,
                separator,
                options,
                progress,
                cancellation.Token);
            this.activeRun = run;
            this.closingAcceptedRun = true;
            this.Close();
            result = await run.ConfigureAwait(true);
            cancellation.Token.ThrowIfCancellationRequested();
            string completionStatus = result.Warnings.Count == 0
                ? "Decompile complete. Output windows are open."
                : $"Decompile complete with {result.Warnings.Count} warning(s); see the log.";
            this.PublishResult(result);
            result = null;
            progressDialog.Complete();
            if (!this.IsDisposed && !this.Disposing)
            {
                this.label_status.Text = completionStatus;
            }
        }
        catch (OperationCanceledException)
        {
            LogManager.Log($"Audio decompile cancelled for '{runSource?.Name ?? "Audio"}'.");
            progressDialog?.Close();
        }
        catch (Exception error)
        {
            Exception root = error is AggregateException aggregate && aggregate.InnerExceptions.Count > 0
                ? aggregate.InnerExceptions[0]
                : error;
            LogManager.Log($"Audio decompile failed for '{runSource?.Name ?? "Audio"}': {root}");
            progressDialog?.Close();
            WindowMainStaticHelpers.ShowErrorWithCopyButton(
                !this.IsDisposed && !this.Disposing ? this : WindowMain.Instance,
                "Audio Decompile Failed",
                root);
        }
        finally
        {
            result?.Dispose();
            if (runSource is not null && this.sourceSnapshotTransferred)
            {
                runSource.Dispose();
                this.sourceSnapshotDisposed = true;
            }
            cancellation?.Dispose();
            if (ReferenceEquals(this.activeCancellation, cancellation))
            {
                this.activeCancellation = null;
            }

            if (ReferenceEquals(this.activeProgressDialog, progressDialog))
            {
                this.activeProgressDialog = null;
            }

            this.activeRun = null;
            if (!this.IsDisposed && !this.Disposing)
            {
                this.SetRunState(false);
                if (this.closeRequested)
                {
                    this.BeginInvoke(this.Close);
                }
            }
        }
    }

    private void PublishResult(AudioDecompileResult result)
    {
        if (result.HasProject)
        {
            MixArrangement? project = result.TakeProject()
                ?? throw new InvalidOperationException("The decompile pipeline returned no project.");
            MixTrackView? view = null;
            try
            {
                view = new MixTrackView();
                view.LoadDecompiledProject(project);
                project = null;
                view.Show();
            }
            catch
            {
                view?.Dispose();
                throw;
            }
            finally
            {
                project?.Dispose();
            }
        }
        if (result.HasCollections)
        {
            IReadOnlyList<AudioDecompileCollection> collections = result.TakeCollections();
            List<AudioCollectionView> published = [];
            try
            {
                foreach (AudioDecompileCollection collection in collections)
                {
                    published.Add(new AudioCollectionView(
                        collection.Atomics,
                        $"{result.SourceName} - {collection.StemName} Atomics"));
                }
            }
            catch
            {
                foreach (AudioCollectionView view in published)
                {
                    if (!view.IsDisposed)
                    {
                        view.Close();
                    }
                }

                foreach (AudioDecompileCollection collection in collections.Skip(published.Count))
                {
                    foreach (AudioObj atomic in collection.Atomics)
                    {
                        atomic.Dispose();
                    }
                }

                throw;
            }
        }

        foreach (string warning in result.Warnings)
        {
            LogManager.Log($"Audio decompile warning: {warning}");
        }

        if (result.Warnings.Count > 0 && !this.IsDisposed && !this.Disposing)
        {
            this.label_status.Text = $"Decompile complete with {result.Warnings.Count} warning(s); see the log.";
        }
    }

    private void SetRunState(bool running)
    {
        this.tabControl_main.Enabled = !running;
        this.button_decompile.Enabled = !running;
        this.button_close.Enabled = true;
        this.label_status.Text = running
            ? "Decompile is running. Progress and cancellation are available in the progress window."
            : this.label_status.Text;
    }

    private void button_close_Click(object? sender, EventArgs e) => this.Close();

    private void CleanupSourceSnapshot()
    {
        if (this.sourceSnapshotTransferred || this.sourceSnapshotDisposed || this.sourceSnapshot.IsDisposed)
        {
            return;
        }

        this.sourceSnapshotDisposed = true;
        this.sourceSnapshot.Dispose();
    }

    private void UpdateBpmLabel()
    {
        string sourceName = GetSourceName(this.sourceSnapshot);
        if (this.sourceSnapshot.Bpm > 0)
        {
            this.label_bpm.Text = $"BPM for {sourceName}: {this.sourceSnapshot.Bpm:F3} from audio tag.";
        }
        else if (this.sourceSnapshot.ScannedBpm > 0)
        {
            this.label_bpm.Text = $"BPM for {sourceName}: {this.sourceSnapshot.ScannedBpm:F3} from a previous scan.";
        }
        else
        {
            this.label_bpm.Text = $"BPM for {sourceName}: will be estimated at run time with BeatScanner.EstimateBpmAsync.";
        }
    }

    private static string GetSourceName(AudioObj source) =>
        string.IsNullOrWhiteSpace(source.Name) ? "Audio" : source.Name.Trim();
}
