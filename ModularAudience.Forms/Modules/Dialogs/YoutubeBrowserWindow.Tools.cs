using ModularAudience.Ytdlp;

namespace ModularAudience.Forms.Modules.Dialogs;

public partial class YoutubeBrowserWindow
{
    private bool EnsureBinaryDirectory()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(configuredBinariesPath))
            {
                ValidateToolsFolder(configuredBinariesPath);
                binaryManager.SetBinaryDirectory(configuredBinariesPath);
                AppendLog("YouTube tools folder: " + binaryManager.BinaryDirectory);
                return true;
            }
            // Retain an existing installation when upgrading from versions without this setting.
            if (Directory.Exists(YtdlpBinaryManager.DefaultBinaryDirectory) &&
                new YtdlpBinaryManager().HasInstalledTools)
            {
                ValidateToolsFolder(YtdlpBinaryManager.DefaultBinaryDirectory);
                PersistToolsFolder(YtdlpBinaryManager.DefaultBinaryDirectory);
                return true;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            binariesReady = false;
            AppendLog("Tools folder is unavailable; choose a folder. " + error.Message);
        }
        return ChooseToolsFolder();
    }

    private void ToolsFolder_Click(object? sender, EventArgs e)
    {
        if (busy || browsing || playlistEnqueueCancellation is not null || closing) return;
        if (ChooseToolsFolder()) InitializeToolsForSelectedPolicy();
    }

    private bool ChooseToolsFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose where YouTube downloader tools (yt-dlp, FFmpeg and Deno) are stored and updated",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(configuredBinariesPath) ? configuredBinariesPath : AppContext.BaseDirectory,
            ShowNewFolderButton = true
        };
        while (dialog.ShowDialog(this) == DialogResult.OK)
        {
            try
            {
                ValidateToolsFolder(dialog.SelectedPath);
                PersistToolsFolder(dialog.SelectedPath);
                return true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Text.Json.JsonException)
            {
                ReportError("Unable to use the selected tools folder", error);
            }
        }
        statusLabel.Text = binariesReady ? "Ready." : "Choose a tools folder or click Update tools to finish setup.";
        RefreshAvailability();
        return false;
    }

    private void PersistToolsFolder(string directory)
    {
        var manager = new YtdlpBinaryManager(directory);
        // Save immediately, before downloading, so rebuilding or an interrupted download retains the selection.
        var settings = YoutubeBrowserSettings.Load(out _);
        settings.YTDownloaderBinariesPath = manager.BinaryDirectory;
        settings.Save();
        configuredBinariesPath = manager.BinaryDirectory;
        binaryManager.SetBinaryDirectory(configuredBinariesPath);
        binariesReady = false;
        AppendLog("YouTube tools folder: " + configuredBinariesPath);
        RefreshAvailability();
    }

    private static void ValidateToolsFolder(string directory)
    {
        var manager = new YtdlpBinaryManager(directory);
        if (!Directory.Exists(manager.BinaryDirectory))
            throw new DirectoryNotFoundException("The configured tools folder no longer exists: " + manager.BinaryDirectory);
        string probe = Path.Combine(manager.BinaryDirectory, ".ytdlp-write-test-" + Guid.NewGuid().ToString("N"));
        using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
    }
}
