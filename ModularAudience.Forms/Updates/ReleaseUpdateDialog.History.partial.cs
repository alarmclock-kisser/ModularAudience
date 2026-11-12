using System.Runtime.InteropServices;
using ModularAudience.Audio;

namespace ModularAudience.Forms.Updates;

internal sealed partial class ReleaseUpdateDialog
{
    private const int EmLineScroll = 0x00B6;
    private const int EmGetFirstVisibleLine = 0x00CE;
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
            string text = ReleaseCommitHistoryFormatter.Format(history, this.currentRelease, this.release.Number, out int latestReleaseStart);
            this.SetHistoryText(text, latestReleaseStart);
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

    private void SetHistoryText(string text, int scrollPosition = 0)
    {
        int position = Math.Clamp(scrollPosition, 0, text.Length);
        this.historyTextBox.Text = text;
        if (position > 0)
        {
            int targetLine = this.historyTextBox.GetLineFromCharIndex(position);
            int lastLine = this.historyTextBox.GetLineFromCharIndex(this.historyTextBox.TextLength);
            int visibleLines = Math.Max(1, this.historyTextBox.ClientSize.Height / this.historyTextBox.Font.Height + 1);
            int paddingLines = Math.Max(0, visibleLines - (lastLine - targetLine) - 1);
            if (paddingLines > 0)
                this.historyTextBox.AppendText(string.Concat(Enumerable.Repeat(Environment.NewLine, paddingLines)));
        }
        this.historyTextBox.SelectionStart = position;
        this.historyTextBox.SelectionLength = 0;
        this.historyTextBox.ScrollToCaret();
        int targetVisibleLine = this.historyTextBox.GetLineFromCharIndex(position);
        int firstVisibleLine = SendMessage(this.historyTextBox.Handle, EmGetFirstVisibleLine, IntPtr.Zero, IntPtr.Zero).ToInt32();
        int linesToScroll = targetVisibleLine - firstVisibleLine;
        if (linesToScroll != 0)
            SendMessage(this.historyTextBox.Handle, EmLineScroll, IntPtr.Zero, new IntPtr(linesToScroll));
    }

    private void WordWrap_CheckedChanged(object? sender, EventArgs e)
    {
        this.historyTextBox.WordWrap = this.wordWrapCheckBox.Checked;
        this.historyTextBox.ScrollBars = this.wordWrapCheckBox.Checked ? ScrollBars.Vertical : ScrollBars.Both;
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

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
