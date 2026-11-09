using ModularAudience.Audio;
using ModularAudience.Forms.Helpers;
using ModularAudience.Ytdlp;
using System.Diagnostics;

namespace ModularAudience.Forms.Modules.Dialogs;

public partial class YoutubeBrowserWindow : Form
{
    private readonly Action<AudioObj> importAudio;
    private readonly YtdlpBinaryManager binaryManager = new();
    private readonly YtdlpClient client;
    private readonly Queue<string> logLines = new();
    private CancellationTokenSource? operationCancellation;
    private Task operation = Task.CompletedTask;
    private bool busy;
    private bool binariesReady;
    private bool closing;
    private bool closeAllowed;
    private bool restoringSettings;
    private bool resourcesReleased;
    private long lastProgressTick;
    private string? settingsWarning;
    private YtdlpBrowserSession? connectedSession;
    private bool pendingUpdateCheck;
    private bool updateCheckScheduled;
    private Func<CancellationToken, Task>? updateToolsProvider;

    public YoutubeBrowserWindow(Action<AudioObj> importAudio)
    {
        this.importAudio = importAudio ?? throw new ArgumentNullException(nameof(importAudio));
        client = new YtdlpClient(binaryManager);
        InitializeComponent();
        RestoreSettings();
        InitializeSearchHistory();
        Hints.HintPolicy.RegisterPopup(this, this.HideThumbnailPopup);
        client.ProgressChanged += Client_ProgressChanged;
        RefreshAvailability();
    }

    private void Window_Shown(object? sender, EventArgs e)
    {
        if (settingsWarning is not null)
        {
            AppendLog(settingsWarning);
            LogManager.LogWarning(settingsWarning);
        }
        if (EnsureBinaryDirectory()) InitializeToolsForSelectedPolicy();
    }

    private void Update_Click(object? sender, EventArgs e)
    {
        if (busy || browsing || playlistEnqueueCancellation is not null || closing || IsDisposed || resourcesReleased) return;
        if (EnsureBinaryDirectory()) StartToolUpdateCheck();
    }

    private void StartToolUpdateCheck()
    {
        pendingUpdateCheck = false;
        StartOperation("Checking tool releases...", token =>
            updateToolsProvider?.Invoke(token) ?? UpdateBinariesAsync(token));
    }

    private void InitializeToolsForSelectedPolicy()
    {
        if (checkForUpdates.Checked) { StartToolUpdateCheck(); return; }
        binariesReady = binaryManager.HasInstalledTools;
        statusLabel.Text = binariesReady ? "Ready. Automatic update checks are off." : "Tools are missing. Click Update tools to install them.";
        RefreshAvailability();
    }

    private void CheckForUpdates_Changed(object? sender, EventArgs e)
    {
        if (restoringSettings) return;
        SaveSettings();
        pendingUpdateCheck = checkForUpdates.Checked;
        TryStartPendingUpdateCheck();
    }

    private void TryStartPendingUpdateCheck()
    {
        if (!pendingUpdateCheck || !checkForUpdates.Checked || busy || browsing || playlistEnqueueCancellation is not null || closing || IsDisposed || resourcesReleased) return;
        pendingUpdateCheck = false;
        Update_Click(this, EventArgs.Empty);
    }

    private void Search_Click(object? sender, EventArgs e) => Search(false);
    private void Playlist_Click(object? sender, EventArgs e) => Search(true);

    private void Query_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter) return;
        e.SuppressKeyPress = true;
        Search(false);
    }

    private void Cancel_Click(object? sender, EventArgs e)
    {
        operationCancellation?.Cancel();
        browseCancellation?.Cancel();
        dateCancellation?.Cancel();
        cancelButton.Enabled = false;
        playlistEnqueueCancellation?.Cancel();
        statusLabel.Text = "Cancelling; waiting for active processing and cleanup...";
    }

    private void RefreshAvailability()
    {
        if (IsDisposed || resourcesReleased) return;
        bool available = CanBrowse;
        queryPanel.Enabled = available;
        optionsGroup.Enabled = available;
        updateButton.Enabled = !busy && !browsing && !closing && playlistEnqueueCancellation is null;
        toolsFolderButton.Enabled = updateButton.Enabled;
        cancelButton.Enabled = !closing && (browsing || playlistEnqueueCancellation is not null || busy && operationCancellation?.IsCancellationRequested == false);
        downloadButton.Enabled = CanEnqueueDownloads && SelectedVideos().Any(video => !video.IsChannel);
        relatedButton.Enabled = available && SelectedVideo() is { IsVideo: true };
        uploaderButton.Enabled = relatedButton.Enabled && !string.IsNullOrWhiteSpace(SelectedVideo()?.Uploader);
        openButton.Enabled = !closing && resultsList.SelectedItems.Count == 1;
        copyButton.Enabled = !closing && resultsList.SelectedItems.Count > 0;
        metadataButton.Enabled = available && SelectedVideo() is { IsVideo: true };
        showRelatedButton.Enabled = metadataButton.Enabled;
        operationProgress.Visible = busy || activeBatch is not null;
        UpdateOptionAvailability();
        if (pendingUpdateCheck && !updateCheckScheduled && !busy && !browsing && !closing && playlistEnqueueCancellation is null && IsHandleCreated)
        {
            updateCheckScheduled = true;
            BeginInvoke((Action)(() =>
            {
                updateCheckScheduled = false;
                TryStartPendingUpdateCheck();
            }));
        }
    }

    private YtdlpVideo? SelectedVideo() => resultsList.SelectedItems.Count == 1
        ? resultsList.SelectedItems[0].Tag as YtdlpVideo : null;

    private YtdlpVideo[] SelectedVideos() => resultsList.SelectedItems.Cast<ListViewItem>()
        .Select(item => (YtdlpVideo)item.Tag!).ToArray();

    private void Related_Click(object? sender, EventArgs e)
    {
        if (SelectedVideo() is not { } video) return;
        queryText.Text = SearchTerms(video.Title + " " + video.Uploader);
        Search(false, forceSearch: true);
    }

    private void Uploader_Click(object? sender, EventArgs e)
    {
        if (SelectedVideo() is not { } video || string.IsNullOrWhiteSpace(video.Uploader)) return;
        queryText.Text = SearchTerms(video.Uploader);
        Search(false, forceSearch: true);
    }

    private static string SearchTerms(string text) =>
        new(text.Where(c => !char.IsControl(c)).Take(500).ToArray());

    private void Open_Click(object? sender, EventArgs e)
    {
        if (SelectedVideo() is not { } video) return;
        try
        {
            var url = YtdlpParser.ValidateYoutubeUrl(video.Url);
            Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception error) { ReportError("Unable to open the YouTube URL", error); }
    }

    private void Copy_Click(object? sender, EventArgs e)
    {
        try
        {
            var urls = SelectedVideos().Select(v => YtdlpParser.ValidateYoutubeUrl(v.Url).AbsoluteUri);
            var text = string.Join(Environment.NewLine, urls);
            if (text.Length > 0) Clipboard.SetText(text);
        }
        catch (Exception error) { ReportError("Unable to copy YouTube URLs", error); }
    }

    private void AppendLog(string message)
    {
        if (IsDisposed || resourcesReleased) return;
        string line = message.Replace('\r', ' ').Replace('\n', ' ');
        if (line.Length > 1200) line = line[..1200];
        logLines.Enqueue($"{DateTime.Now:HH:mm:ss}  {line}");
        while (logLines.Count > 160) logLines.Dequeue();
        activityLog.Text = string.Join(Environment.NewLine, logLines);
        activityLog.SelectionStart = activityLog.TextLength;
        activityLog.ScrollToCaret();
    }

    private void ReportError(string title, Exception error)
    {
        LogManager.LogError($"YouTube browser: {title}. {error}");
        AppendLog($"Error: {title}. {error.Message}");
        if (!closing && !IsDisposed && !resourcesReleased)
            WindowMainStaticHelpers.ShowErrorWithCopyButton(this, title, error);
    }

    private async void Window_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (closeAllowed) return;
        e.Cancel = true;
        if (closing) return;
        closing = true;
        HideThumbnailPopup();
        playlistEnqueueCancellation?.Cancel();
        browseCancellation?.Cancel();
        operationCancellation?.Cancel();
        dateCancellation?.Cancel();
        previewCancellation?.Cancel();
        RefreshAvailability();
        statusLabel.Text = "Closing; waiting for processing and temporary-file cleanup...";
        try
        {
            await Task.Yield();
            await operation;
            await Task.WhenAll(browseTasks.ToArray());
            await Task.WhenAll(playlistEnqueueTasks.ToArray());
            await Task.WhenAll(dateTasks.ToArray());
            await Task.WhenAll(previewTasks.ToArray());
        }
        catch (Exception error)
        {
            LogManager.LogError($"YouTube browser: error while closing. {error}");
        }
        finally
        {
            SaveSettings();
            closeAllowed = true;
            Close();
        }
    }

    private void ReleaseResources()
    {
        if (resourcesReleased) return;
        resourcesReleased = true;
        playlistEnqueueCancellation?.Cancel();
        searchHistoryMenu?.Dispose();
        searchHistoryMenu = null;
        HideThumbnailPopup();
        browseCancellation?.Cancel();
        dateCancellation?.Cancel();
        progressTimer?.Stop();
        operationClock.Stop();
        operationCancellation?.Cancel();
        previewCancellation?.Cancel();
        client.ProgressChanged -= Client_ProgressChanged;
        connectedSession = null;
        client.MetadataSession = null;
        thumbnailPopup?.Dispose();
        thumbnailClient.Dispose();
        var image = thumbnailPicture.Image;
        thumbnailPicture.Image = null;
        image?.Dispose();
    }
}
