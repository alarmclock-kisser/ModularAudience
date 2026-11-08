namespace ModularAudience.Forms.Updates;

/// <summary>
/// Installs a verified update after the original process has exited, then starts
/// the installed executable. Optional settings warnings must not prevent restart.
/// </summary>
internal sealed record ApplicationUpdateInstallation(
    string TargetPath,
    string StagedPath,
    string OriginalHash,
    string UpdateHash,
    long ReleaseNumber,
    string SettingsPath)
{
    internal ApplicationUpdateInstallationResult Complete(Action<string> restart)
    {
        ArgumentNullException.ThrowIfNull(restart);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ReleaseNumber);

        string? backupWarning = AtomicUpdateReplacement.Apply(TargetPath, StagedPath, OriginalHash, UpdateHash);
        var warnings = new List<string>();
        if (backupWarning is not null) warnings.Add(backupWarning);
        try { new ReleaseUpdateSettingsStore(SettingsPath).RecordInstalledRelease(ReleaseNumber); }
        catch (Exception error) when (UpdateDiagnostics.IsExpected(error))
        {
            warnings.Add($"The release number could not be saved to the update settings; it will be recorded on the next start.\n{error.Message}");
        }

        try
        {
            restart(TargetPath);
            return new ApplicationUpdateInstallationResult(warnings.AsReadOnly(), null);
        }
        catch (Exception error) when (UpdateDiagnostics.IsExpected(error))
        {
            // Installation succeeded. Do not roll back or claim the old version
            // is still installed just because launching the new one failed.
            return new ApplicationUpdateInstallationResult(warnings.AsReadOnly(), error);
        }
    }
}

internal sealed record ApplicationUpdateInstallationResult(IReadOnlyList<string> Warnings, Exception? RestartError)
{
    internal bool Restarted => RestartError is null;
}
