using ModularAudience.Audio;
using ModularAudience.Ytdlp;

namespace ModularAudience.Forms.Updates;

internal static class ApplicationUpdater
{
    internal static async Task CheckAtStartupAsync(Form owner)
    {
        using var closed = new CancellationTokenSource();
        FormClosedEventHandler onClosed = (_, _) => closed.Cancel();
        owner.FormClosed += onClosed;
        try
        {
            long? current = ApplicationRelease.RunningReleaseNumber();
            var settingsStore = new ReleaseUpdateSettingsStore();
            ReleaseUpdateSettings settings = settingsStore.LoadAndRecordRunningRelease(current);
            UpdateInstaller.CleanupOldWorkspaces();
            if (!settings.AskToUpdate) return;
            if (current is null || !string.IsNullOrEmpty(typeof(ApplicationRelease).Assembly.Location))
            {
                LogManager.LogInfo("Application update checks are available only for numbered single-file releases.");
                return;
            }

            string targetPath = Environment.ProcessPath
                ?? throw new InvalidOperationException("The current executable path is unavailable.");
            var initialFile = new ExecutableSnapshot(targetPath);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(closed.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            GitHubRelease latest;
            try { latest = await GitHubReleaseSource.GetLatestReleaseAsync(ApplicationRelease.Repository, timeout.Token); }
            catch (OperationCanceledException) when (!closed.IsCancellationRequested)
            {
                throw new TimeoutException("The GitHub release check timed out.");
            }
            // Re-read after the network await: another window or process may
            // have disabled checks or skipped this release in the meantime.
            settings = settingsStore.LoadAndRecordRunningRelease(null);
            if (!settings.AskToUpdate) return;
            ApplicationRelease? release = ApplicationRelease.NewerThan(latest, current.Value, settings.SkipReleaseNumber);
            if (release is null || closed.IsCancellationRequested) return;

            initialFile.EnsureUnchanged(targetPath);
            string originalHash = await GitHubReleaseSource.HashAsync(targetPath, closed.Token);
            initialFile.EnsureUnchanged(targetPath);
            if (closed.IsCancellationRequested || owner.IsDisposed ||
                !settingsStore.LoadAndRecordRunningRelease(null).ShouldOfferRelease(release.Number)) return;

            var historySource = new GitHubReleaseHistorySource();
            using var dialog = new ReleaseUpdateDialog(current.Value, release, targetPath, originalHash, settingsStore,
                token => historySource.GetAsync(current.Value, release.Number, token));
            if (dialog.ShowDialog(owner) == DialogResult.OK && !owner.IsDisposed)
                owner.Close();
        }
        catch (OperationCanceledException) when (closed.IsCancellationRequested)
        {
        }
        catch (Exception error) when (UpdateDiagnostics.IsExpected(error))
        {
            if (!owner.IsDisposed)
                UpdateDiagnostics.ShowError(owner, "Application update", "The update check could not be completed.", error);
            else
                LogManager.LogError($"The update check could not be completed: {error}");
        }
        finally
        {
            owner.FormClosed -= onClosed;
        }
    }

    private sealed class ExecutableSnapshot
    {
        private readonly long length;
        private readonly DateTime lastWriteTime;
        private readonly DateTime creationTime;

        internal ExecutableSnapshot(string path)
        {
            var file = new FileInfo(path);
            if (!file.Exists) throw new FileNotFoundException("The running executable was not found.", path);
            length = file.Length;
            lastWriteTime = file.LastWriteTimeUtc;
            creationTime = file.CreationTimeUtc;
        }

        internal void EnsureUnchanged(string path)
        {
            var current = new ExecutableSnapshot(path);
            if (current.length != length || current.lastWriteTime != lastWriteTime || current.creationTime != creationTime)
                throw new IOException("The executable changed while checking for updates. Start the installed version again.");
        }
    }
}
