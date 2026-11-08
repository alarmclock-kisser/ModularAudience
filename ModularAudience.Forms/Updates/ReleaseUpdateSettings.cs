using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ModularAudience.Forms.Updates;

internal sealed class ReleaseUpdateSettings
{
    public bool AskToUpdate { get; set; } = true;
    public long? CurrentReleaseNumber { get; set; }
    public long? SkipReleaseNumber { get; set; }

    internal bool ShouldOfferRelease(long releaseNumber)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(releaseNumber);
        return AskToUpdate && (!SkipReleaseNumber.HasValue || releaseNumber > SkipReleaseNumber.Value);
    }
}

internal sealed class ReleaseUpdateSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string path;
    private readonly string mutexName;

    internal static string DefaultPath
    {
        get
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrEmpty(appData) || !Path.IsPathFullyQualified(appData))
                throw new DirectoryNotFoundException("The AppData directory is unavailable.");
            return Path.Combine(appData, "ModularAudience", "update-settings.json");
        }
    }

    internal string SettingsPath => path;

    internal ReleaseUpdateSettingsStore(string? path = null)
    {
        this.path = Path.GetFullPath(path ?? DefaultPath);
        byte[] pathHash = SHA256.HashData(Encoding.UTF8.GetBytes(this.path.ToUpperInvariant()));
        mutexName = @"Local\ModularAudience.UpdateSettings." + Convert.ToHexString(pathHash);
    }

    internal ReleaseUpdateSettings LoadAndRecordRunningRelease(long? releaseNumber)
    {
        if (releaseNumber is <= 0) throw new ArgumentOutOfRangeException(nameof(releaseNumber));
        return WithLock(() =>
        {
            bool exists = File.Exists(path);
            ReleaseUpdateSettings settings = LoadUnsafe();
            if (!exists || (releaseNumber.HasValue && settings.CurrentReleaseNumber != releaseNumber))
            {
                if (releaseNumber.HasValue) settings.CurrentReleaseNumber = releaseNumber;
                SaveUnsafe(settings);
            }
            return settings;
        });
    }

    internal void DisableAsking() => SetAskToUpdate(false);

    internal void SetAskToUpdate(bool askToUpdate)
    {
        WithLock(() =>
        {
            bool exists = File.Exists(path);
            ReleaseUpdateSettings settings = LoadUnsafe();
            if (!exists || settings.AskToUpdate != askToUpdate)
            {
                settings.AskToUpdate = askToUpdate;
                settings.SkipReleaseNumber = null;
                SaveUnsafe(settings);
            }
            return true;
        });
    }

    internal void SkipThroughRelease(long releaseNumber)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(releaseNumber);
        WithLock(() =>
        {
            ReleaseUpdateSettings settings = LoadUnsafe();
            // Concurrent instances must not lower a skip boundary saved by
            // another update dialog. Only toggling AskToUpdate resets it.
            if (!settings.SkipReleaseNumber.HasValue || releaseNumber > settings.SkipReleaseNumber.Value)
            {
                settings.SkipReleaseNumber = releaseNumber;
                SaveUnsafe(settings);
            }
            return true;
        });
    }

    internal void RecordInstalledRelease(long releaseNumber)
    {
        if (releaseNumber <= 0) throw new ArgumentOutOfRangeException(nameof(releaseNumber));
        WithLock(() =>
        {
            ReleaseUpdateSettings settings = LoadUnsafe();
            settings.CurrentReleaseNumber = releaseNumber;
            SaveUnsafe(settings);
            return true;
        });
    }

    private ReleaseUpdateSettings LoadUnsafe()
    {
        if (!File.Exists(path)) return new ReleaseUpdateSettings();
        if (new FileInfo(path).Length > 65536)
            throw new InvalidDataException("The update settings file exceeds 64 KiB.");
        var settings = JsonSerializer.Deserialize<ReleaseUpdateSettings>(File.ReadAllText(path))
            ?? throw new InvalidDataException("The update settings file is empty.");
        if (settings.CurrentReleaseNumber is <= 0)
            throw new InvalidDataException("CurrentReleaseNumber must be a positive release number or null.");
        if (settings.SkipReleaseNumber is <= 0)
            throw new InvalidDataException("SkipReleaseNumber must be a positive release number or null.");
        return settings;
    }

    private void SaveUnsafe(ReleaseUpdateSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, settings, JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private T WithLock<T>(Func<T> action)
    {
        using var mutex = new Mutex(false, mutexName);
        bool acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(10)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("The update settings are in use by another process.");
            return action();
        }
        finally
        {
            if (acquired) mutex.ReleaseMutex();
        }
    }
}
