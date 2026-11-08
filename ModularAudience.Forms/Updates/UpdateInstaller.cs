using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Text;
using ModularAudience.Audio;
using ModularAudience.Ytdlp;

namespace ModularAudience.Forms.Updates;

internal static class UpdateInstaller
{
    private const string HelperArgument = "--install-github-update";
    private const string PipePrefix = "ModularAudience.Update.";
    private const string EventPrefix = @"Local\ModularAudience.UpdateCommit.";
    private static string WorkspaceRoot
    {
        get
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(appData) || !Path.IsPathFullyQualified(appData))
                throw new DirectoryNotFoundException("The local AppData directory is unavailable.");
            return Path.Combine(appData, "ModularAudience", "UpdateStaging");
        }
    }

    internal static bool IsHelperInvocation(string[] args) =>
        args.Length > 0 && args[0] == HelperArgument;

    internal static async Task DownloadAndLaunchAsync(ApplicationRelease release, string targetPath,
        string originalHash, string settingsPath, IProgress<string> status, IProgress<YtdlpDownloadProgress> progress,
        Action onCommitted, CancellationToken cancellationToken)
    {
        string workspace = Path.Combine(WorkspaceRoot, Guid.NewGuid().ToString("N"));
        string helperPath = Path.Combine(workspace, ApplicationRelease.AssetName);
        string stagedPath = AtomicUpdateReplacement.CreateStagedPath(targetPath);
        AtomicUpdateReplacement.ValidatePaths(targetPath, stagedPath);
        AtomicUpdateReplacement.ValidateHash(originalHash);
        bool handedOff = false;
        bool workspaceCreated = false;
        bool stagedFileCreated = false;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(30));
        try
        {
            if (Directory.Exists(workspace))
                throw new IOException("The update staging directory already exists.");
            Directory.CreateDirectory(workspace);
            workspaceCreated = true;
            string expectedHash;
            await using (var destination = new FileStream(stagedPath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                stagedFileCreated = true;
                status.Report("Checking the release SHA256...");
                expectedHash = await GitHubReleaseSource.GetChecksumAsync(release.Asset, release.Assets, timeout.Token);
                await GitHubReleaseSource.DownloadAsync(release.Asset, helperPath, expectedHash,
                    timeout.Token, status, progress);
                status.Report("Preparing the verified update...");
                await using var source = new FileStream(helperPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                await source.CopyToAsync(destination, timeout.Token);
                await destination.FlushAsync(timeout.Token);
                destination.Flush(flushToDisk: true);
            }
            if (!string.Equals(await GitHubReleaseSource.HashAsync(stagedPath, timeout.Token), expectedHash,
                StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The prepared update failed its SHA256 verification.");

            status.Report("Starting the update installer...");
            await StartHelperAsync(helperPath, targetPath, stagedPath, originalHash, expectedHash, settingsPath,
                release.Number, () => { handedOff = true; onCommitted(); }, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("The update download or installer preparation timed out.");
        }
        finally
        {
            if (!handedOff)
            {
                if (stagedFileCreated) TryCleanupFile(stagedPath);
                if (workspaceCreated) TryCleanupWorkspace(workspace);
            }
        }
    }

    private static async Task StartHelperAsync(string helperPath, string targetPath, string stagedPath,
        string originalHash, string updateHash, string settingsPath, long releaseNumber, Action onCommit, CancellationToken token)
    {
        string id = Guid.NewGuid().ToString("N");
        string pipeName = PipePrefix + id;
        using var commit = new EventWaitHandle(false, EventResetMode.ManualReset, EventPrefix + id);
        using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var parent = Process.GetCurrentProcess();
        var start = new ProcessStartInfo(helperPath) { UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in new[]
        {
            HelperArgument, pipeName, parent.Id.ToString(CultureInfo.InvariantCulture),
            parent.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture),
            targetPath, stagedPath, originalHash, updateHash, releaseNumber.ToString(CultureInfo.InvariantCulture),
            settingsPath
        })
            start.ArgumentList.Add(argument);
        using var helper = Process.Start(start) ?? throw new InvalidOperationException("The update installer could not be started.");
        using var readyTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        readyTimeout.CancelAfter(TimeSpan.FromMinutes(3));
        try
        {
            using var exitCancellation = new CancellationTokenSource();
            Task connection = pipe.WaitForConnectionAsync(readyTimeout.Token);
            Task exit = helper.WaitForExitAsync(exitCancellation.Token);
            if (await Task.WhenAny(connection, exit) == exit)
            {
                await exit;
                throw new InvalidOperationException($"The update installer exited before connecting (exit code {helper.ExitCode}).");
            }
            exitCancellation.Cancel();
            await connection;
            using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, leaveOpen: true);
            string? reply = await reader.ReadLineAsync(readyTimeout.Token);
            if (reply is not null && reply.StartsWith("ERROR ", StringComparison.Ordinal))
                throw new InvalidOperationException(reply[6..]);
            if (reply != "READY")
                throw new InvalidOperationException("The update installer did not confirm that it is ready.");
            token.ThrowIfCancellationRequested();
            if (!commit.Set()) throw new InvalidOperationException("The update installer could not be authorized.");
            onCommit();
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException("The update installer did not become ready in time.");
        }
    }

    internal static void RunHelper(string[] args)
    {
        HelperOptions? options = null;
        try
        {
            options = ParseHelperArguments(args);
            RunHelper(options);
        }
        catch (Exception error) when (UpdateDiagnostics.IsExpected(error))
        {
            UpdateDiagnostics.ShowHelperResult($"The application update failed.\n\n{error}", warning: true, options?.SettingsPath);
        }
    }

    private static void RunHelper(HelperOptions options)
    {
        using var pipe = new NamedPipeClientStream(".", options.PipeName, PipeDirection.InOut, PipeOptions.None);
        pipe.Connect(180_000);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
        bool committed = false;
        try
        {
            using var parent = ValidateHelper(options);
            using var commit = EventWaitHandle.OpenExisting(options.EventName);
            writer.WriteLine("READY");
            if (!commit.WaitOne(TimeSpan.FromMinutes(3))) return;
            committed = true;
            if (!parent.WaitForExit(600_000))
                throw new TimeoutException("The original application did not close. The update was not installed.");
            CompleteInstallation(options);
        }
        catch (Exception error) when (UpdateDiagnostics.IsExpected(error))
        {
            if (committed) throw;
            try { writer.WriteLine("ERROR " + error.Message.Replace('\r', ' ').Replace('\n', ' ')); }
            catch (Exception pipeError) when (pipeError is IOException or ObjectDisposedException)
            {
                string message = $"The update installer could not report its error to the original process: {error}. {pipeError.Message}";
                string? logError = UpdateDiagnostics.WriteHelperLog(message, warning: true, options.SettingsPath);
                if (logError is not null)
                    MessageBox.Show($"{message}\n\nThe update log could not be written: {logError}",
                        "Application update warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    private static Process ValidateHelper(HelperOptions options)
    {
        AtomicUpdateReplacement.ValidatePaths(options.TargetPath, options.StagedPath);
        AtomicUpdateReplacement.ValidateHash(options.OriginalHash);
        AtomicUpdateReplacement.ValidateHash(options.UpdateHash);
        if (ApplicationRelease.RunningReleaseNumber() != options.ReleaseNumber)
            throw new InvalidDataException("The downloaded executable does not contain the expected release number.");
        string helperPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("The update installer path is unavailable.");
        if (!AtomicUpdateReplacement.HashMatches(helperPath, options.UpdateHash) ||
            !AtomicUpdateReplacement.HashMatches(options.StagedPath, options.UpdateHash))
            throw new InvalidDataException("The downloaded or staged executable failed its SHA256 verification.");

        Process parent = Process.GetProcessById(options.ParentId);
        bool validated = false;
        try
        {
            string? parentPath = parent.MainModule?.FileName;
            if (parent.HasExited || parent.StartTime.ToUniversalTime().Ticks != options.ParentStartTicks ||
                parentPath is null || !string.Equals(Path.GetFullPath(parentPath), Path.GetFullPath(options.TargetPath),
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The original application process no longer matches the executable.");
            validated = true;
            return parent;
        }
        finally
        {
            if (!validated) parent.Dispose();
        }
    }

    private static void CompleteInstallation(HelperOptions options)
    {
        var installation = new ApplicationUpdateInstallation(options.TargetPath, options.StagedPath,
            options.OriginalHash, options.UpdateHash, options.ReleaseNumber, options.SettingsPath);
        ApplicationUpdateInstallationResult result = installation.Complete(UpdatedApplicationLauncher.Start);
        string warnings = result.Warnings.Count > 0 ? "\n\n" + string.Join("\n\n", result.Warnings) : string.Empty;
        if (!result.Restarted)
        {
            UpdateDiagnostics.ShowHelperResult(
                $"Release {options.ReleaseNumber} was installed, but the updated application could not be started automatically.\n\n" +
                $"Start it using your existing shortcut or this executable:\n{options.TargetPath}\n\n{result.RestartError}{warnings}",
                warning: true, options.SettingsPath);
            return;
        }

        // Successful updates are silent: the new application is already open.
        // Non-fatal cleanup/settings warnings go to the log, not an OK dialog.
        UpdateDiagnostics.WriteHelperLog($"Release {options.ReleaseNumber} was installed and restarted.{warnings}",
            result.Warnings.Count > 0, options.SettingsPath);
    }

    private static HelperOptions ParseHelperArguments(string[] args)
    {
        if (args.Length != 10 || args[0] != HelperArgument ||
            !args[1].StartsWith(PipePrefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(args[1][PipePrefix.Length..], "N", out _) ||
            !int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out int parentId) || parentId <= 0 ||
            !long.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out long parentStart) || parentStart <= 0 ||
            !long.TryParse(args[8], NumberStyles.None, CultureInfo.InvariantCulture, out long release) || release <= 0 ||
            !Path.IsPathFullyQualified(args[9]) ||
            !string.Equals(Path.GetFileName(args[9]), "update-settings.json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The update installer arguments are invalid.");
        return new HelperOptions(args[1], EventPrefix + args[1][PipePrefix.Length..], parentId, parentStart,
            args[4], args[5], args[6], args[7], release, args[9]);
    }

    internal static void CleanupOldWorkspaces()
    {
        if (!Directory.Exists(WorkspaceRoot)) return;
        foreach (string directory in Directory.EnumerateDirectories(WorkspaceRoot))
        {
            try
            {
                var info = new DirectoryInfo(directory);
                if (!Guid.TryParseExact(info.Name, "N", out _) ||
                    (info.Attributes & FileAttributes.ReparsePoint) != 0 ||
                    info.CreationTimeUtc > DateTime.UtcNow.AddHours(-1))
                    continue;
                TryCleanupWorkspace(directory);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                LogManager.LogWarning($"An old update staging directory could not be inspected: {directory}. {error.Message}");
            }
        }
    }

    private static void TryCleanupWorkspace(string directory)
    {
        try
        {
            if (!Directory.Exists(directory)) return;
            string helper = Path.Combine(directory, ApplicationRelease.AssetName);
            if (File.Exists(helper)) File.Delete(helper);
            Directory.Delete(directory);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            LogManager.LogWarning($"Update staging could not be removed: {directory}. {error.Message}");
        }
    }

    private static void TryCleanupFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            LogManager.LogWarning($"The staged update could not be removed: {path}. {error.Message}");
        }
    }

    private sealed record HelperOptions(string PipeName, string EventName, int ParentId, long ParentStartTicks,
        string TargetPath, string StagedPath, string OriginalHash, string UpdateHash, long ReleaseNumber, string SettingsPath);
}
