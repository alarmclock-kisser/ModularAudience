using ModularAudience.Ytdlp;

namespace ModularAudience.Forms.Updates;

internal sealed partial class ReleaseUpdateDialog : Form
{
    private readonly ApplicationRelease release;
    private readonly string targetPath;
    private readonly string originalHash;
    private readonly ReleaseUpdateSettingsStore settingsStore;
    private CancellationTokenSource? activeDownload;
    private bool closeWhenCancelled;
    private bool preferenceSaved;
    private bool installerCommitted;

    internal ReleaseUpdateDialog(long currentRelease, ApplicationRelease release, string targetPath,
        string originalHash, ReleaseUpdateSettingsStore settingsStore)
    {
        this.release = release;
        this.targetPath = targetPath;
        this.originalHash = originalHash;
        this.settingsStore = settingsStore;
        InitializeComponent();
        messageLabel.Text = $"Release {release.Number} is available (installed: {currentRelease}).\n\n" +
            "Download and install it? The application will close after verification. Start it again after installation.";
    }

    private async void Update_Click(object? sender, EventArgs e)
    {
        if (activeDownload is not null || !SavePreference()) return;
        using var cancellation = new CancellationTokenSource();
        activeDownload = cancellation;
        closeWhenCancelled = false;
        SetDownloading(true);
        var status = new Progress<string>(message =>
        {
            if (!IsDisposed && activeDownload is not null && !installerCommitted) statusLabel.Text = message;
        });
        var progress = new Progress<YtdlpDownloadProgress>(value =>
        {
            if (!IsDisposed && activeDownload is not null && !installerCommitted && value.Percent is double percent)
                downloadProgressBar.Value = (int)Math.Round(Math.Clamp(percent, 0, 100));
        });
        try
        {
            await UpdateInstaller.DownloadAndLaunchAsync(release, targetPath, originalHash, settingsStore.SettingsPath, status, progress,
                () =>
                {
                    installerCommitted = true;
                    laterButton.Enabled = false;
                    ControlBox = false;
                    statusLabel.Text = "Update verified. Closing the application...";
                }, cancellation.Token);
            activeDownload = null;
            DialogResult = DialogResult.OK;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            statusLabel.Text = "Update cancelled.";
        }
        catch (Exception error) when (UpdateDiagnostics.IsExpected(error))
        {
            UpdateDiagnostics.ShowError(this, "Application update", "The update could not be downloaded or prepared. The current executable was not replaced.", error);
        }
        finally
        {
            activeDownload = null;
            if (!IsDisposed)
            {
                if (closeWhenCancelled)
                {
                    closeWhenCancelled = false;
                    Close();
                }
                if (!IsDisposed && !installerCommitted) SetDownloading(false);
            }
        }
    }

    private void Later_Click(object? sender, EventArgs e)
    {
        if (activeDownload is not null)
        {
            RequestCancellation();
            return;
        }
        DialogResult = DialogResult.Cancel;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (activeDownload is not null)
        {
            e.Cancel = true;
            if (!installerCommitted) RequestCancellation();
        }
        else if (!SavePreference())
        {
            e.Cancel = true;
        }
        base.OnFormClosing(e);
    }

    private bool SavePreference()
    {
        if (!dontAskCheckBox.Checked || preferenceSaved) return true;
        try
        {
            settingsStore.DisableAsking();
            preferenceSaved = true;
            return true;
        }
        catch (Exception error) when (UpdateDiagnostics.IsExpected(error))
        {
            UpdateDiagnostics.ShowError(this, "Application update", "The update preference could not be saved.", error);
            return false;
        }
    }

    private void RequestCancellation()
    {
        if (installerCommitted) return;
        closeWhenCancelled = true;
        laterButton.Enabled = false;
        statusLabel.Text = "Cancelling the update...";
        activeDownload?.Cancel();
    }

    private void SetDownloading(bool downloading)
    {
        updateButton.Enabled = !downloading;
        dontAskCheckBox.Enabled = !downloading && !preferenceSaved;
        downloadProgressBar.Visible = downloading;
        laterButton.Enabled = true;
        laterButton.Text = downloading ? "Cancel" : "Not now";
        if (downloading)
        {
            downloadProgressBar.Value = 0;
            statusLabel.Text = "Preparing download...";
        }
    }
}
