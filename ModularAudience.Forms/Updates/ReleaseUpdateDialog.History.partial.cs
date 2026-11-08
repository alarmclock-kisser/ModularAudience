using ModularAudience.Audio;

namespace ModularAudience.Forms.Updates;

internal sealed partial class ReleaseUpdateDialog
{
    private CancellationTokenSource? activeHistoryLoad;
    private bool historyRequested;
    private bool historyStopped;

    private async void History_Shown(object? sender, EventArgs e)
    {
        if (this.historyRequested || this.historyStopped || this.IsDisposed || this.Disposing || this.loadHistory is null) return;
        this.historyRequested = true;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        this.activeHistoryLoad = cancellation;
        try
        {
            ReleaseCommitHistory history = await this.loadHistory(cancellation.Token);
            if (this.IsDisposed || this.Disposing || this.historyStopped || cancellation.IsCancellationRequested) return;
            this.SetHistoryText(ReleaseCommitHistoryFormatter.Format(history, this.currentRelease, this.release.Number));
        }
        catch (OperationCanceledException) when (this.historyStopped)
        {
            // The dialog closed or an update started. History is optional.
        }
        catch (OperationCanceledException)
        {
            this.ShowHistoryUnavailable("The GitHub request timed out.");
        }
        catch (Exception error) when (UpdateDiagnostics.IsExpected(error))
        {
            this.ShowHistoryUnavailable(error.Message);
            LogManager.LogWarning($"Release commit history could not be loaded: {error.Message}");
        }
        finally
        {
            this.activeHistoryLoad = null;
        }
    }

    private void SetHistoryText(string text)
    {
        this.historyTextBox.Text = text;
        this.historyTextBox.SelectionStart = 0;
        this.historyTextBox.SelectionLength = 0;
        this.historyTextBox.ScrollToCaret();
    }

    private void ShowHistoryUnavailable(string reason)
    {
        if (this.IsDisposed || this.Disposing || this.historyStopped) return;
        this.SetHistoryText($"Commit messages could not be loaded.\r\n\r\n{reason}\r\n\r\nYou can still update, skip, or dismiss this release.");
    }

    private void CancelHistoryLoad()
    {
        this.historyStopped = true;
        this.activeHistoryLoad?.Cancel();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        this.CancelHistoryLoad();
        base.OnFormClosed(e);
    }
}
