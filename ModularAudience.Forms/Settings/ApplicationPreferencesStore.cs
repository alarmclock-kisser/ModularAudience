using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ModularAudience.Forms.Settings;

/// <summary>Atomic, field-specific updates prevent different instances from overwriting unrelated preferences.</summary>
internal sealed class ApplicationPreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string mutexName;

    internal static string DefaultPath
    {
        get
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify);
            if (!Path.IsPathFullyQualified(appData)) throw new DirectoryNotFoundException("The AppData directory is unavailable.");
            return Path.Combine(appData, "ModularAudience", "application-settings.json");
        }
    }

    internal string FilePath { get; }

    internal ApplicationPreferencesStore(string? path = null)
    {
        this.FilePath = Path.GetFullPath(path ?? DefaultPath);
        this.mutexName = @"Local\ModularAudience.ApplicationPreferences." + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(this.FilePath.ToUpperInvariant())));
    }

    internal ApplicationPreferences Load() => this.WithLock(() =>
    {
        bool exists = File.Exists(this.FilePath);
        ApplicationPreferences settings = this.Read();
        if (!exists) this.Write(settings);
        return settings;
    });

    internal ApplicationPreferences SetExport(string format, int bits) => this.Update(settings => settings with
    {
        ExportFormat = format, ExportBits = bits
    });

    internal ApplicationPreferences SetWorkingDirectory(string? directory) => this.Update(settings => settings with
    {
        WorkingDirectory = directory
    });

    internal ApplicationPreferences SetLogFontSize(float size) => this.Update(settings => settings with { LogFontSize = size });

    internal ApplicationPreferences SetKeepStructure(bool enabled) => this.Update(settings => settings with { KeepStructure = enabled });

    internal ApplicationPreferences SetAllInOneBag(bool enabled) => this.Update(settings => settings with { AllInOneBag = enabled });
    internal ApplicationPreferences SetEnableToolTips(bool enabled) => this.Update(settings => settings with { EnableToolTips = enabled });

    private ApplicationPreferences Update(Func<ApplicationPreferences, ApplicationPreferences> change) => this.WithLock(() =>
    {
        ApplicationPreferences settings = change(this.Read()).Normalize();
        this.Write(settings);
        return settings;
    });

    private ApplicationPreferences Read()
    {
        if (!File.Exists(this.FilePath)) return new ApplicationPreferences();
        if (new FileInfo(this.FilePath).Length > 65536) throw new InvalidDataException("Application preferences exceed 64 KiB.");
        return (JsonSerializer.Deserialize<ApplicationPreferences>(File.ReadAllText(this.FilePath))
            ?? throw new InvalidDataException("The application preferences file is empty.")).Normalize();
    }

    private void Write(ApplicationPreferences settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(this.FilePath)!);
        string temporary = this.FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, settings, JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(this.FilePath)) File.Replace(temporary, this.FilePath, null);
            else File.Move(temporary, this.FilePath);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private T WithLock<T>(Func<T> action)
    {
        using var mutex = new Mutex(false, this.mutexName);
        bool acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(5)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("Another instance is busy saving application preferences.");
            return action();
        }
        finally { if (acquired) mutex.ReleaseMutex(); }
    }
}
