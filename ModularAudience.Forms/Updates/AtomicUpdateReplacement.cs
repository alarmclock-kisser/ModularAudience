using System.Security.Cryptography;
using System.Text;

namespace ModularAudience.Forms.Updates;

internal static class AtomicUpdateReplacement
{
    internal static string CreateStagedPath(string targetPath) =>
        targetPath + ".update-" + Guid.NewGuid().ToString("N") + ".tmp";

    internal static void ValidatePaths(string targetPath, string stagedPath)
    {
        if (!Path.IsPathFullyQualified(targetPath) || !Path.IsPathFullyQualified(stagedPath))
            throw new InvalidDataException("Update paths must be absolute.");
        string target = Path.GetFullPath(targetPath);
        string staged = Path.GetFullPath(stagedPath);
        string prefix = target + ".update-";
        if (!target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            !staged.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !staged.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParseExact(staged[prefix.Length..^4], "N", out _))
            throw new InvalidDataException("The update staging path does not match the executable.");
    }

    internal static string? Apply(string targetPath, string stagedPath, string originalHash, string updateHash)
    {
        ValidatePaths(targetPath, stagedPath);
        ValidateHash(originalHash);
        ValidateHash(updateHash);
        byte[] pathHash = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(targetPath).ToUpperInvariant()));
        using var mutex = new Mutex(false, @"Local\ModularAudience.UpdateInstall." + Convert.ToHexString(pathHash));
        bool acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(30)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("Another update is already installing this executable.");
            return ReplaceUnderLock(targetPath, stagedPath, originalHash, updateHash);
        }
        finally
        {
            if (acquired) mutex.ReleaseMutex();
        }
    }

    private static string? ReplaceUnderLock(string targetPath, string stagedPath, string originalHash, string updateHash)
    {
        if (!HashMatches(targetPath, originalHash))
            throw new InvalidDataException("The installed executable changed since the update was offered. No replacement was made.");
        if (!HashMatches(stagedPath, updateHash))
            throw new InvalidDataException("The staged update failed its SHA256 verification. No replacement was made.");

        string backupPath = targetPath + ".update-backup-" + Guid.NewGuid().ToString("N") + ".bak";
        try
        {
            File.Replace(stagedPath, targetPath, backupPath);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            if (File.Exists(backupPath) && !File.Exists(targetPath))
            {
                try { File.Move(backupPath, targetPath); }
                catch (Exception restoreError) when (restoreError is IOException or UnauthorizedAccessException)
                {
                    throw new IOException($"The update failed and the original could not be restored. Backup: {backupPath}",
                        new AggregateException(error, restoreError));
                }
            }
            throw new IOException($"The update could not replace the executable. Close other instances and check folder permissions. Backup, if present: {backupPath}", error);
        }

        try
        {
            File.Delete(backupPath);
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return $"The update was installed, but the old executable could not be deleted: {backupPath}\n{error.Message}";
        }
    }

    internal static bool HashMatches(string path, string expectedHash)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.SequentialScan);
        return string.Equals(Convert.ToHexString(SHA256.HashData(stream)), expectedHash, StringComparison.OrdinalIgnoreCase);
    }

    internal static void ValidateHash(string hash)
    {
        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit))
            throw new InvalidDataException("An update SHA256 hash is invalid.");
    }
}
