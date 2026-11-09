using System.Security;
using System.Text.Json;
using ModularAudience.Audio;
using ModularAudience.Forms.Settings;

namespace ModularAudience.Forms;

public partial class WindowMain
{
    private readonly ApplicationPreferencesStore _preferencesStore;
    private ApplicationPreferences _applicationPreferences = new();
    private ApplicationPreferencesMonitor? _preferencesMonitor;
    private ExportOptionsController? _exportOptions;
    private LogListZoomController? _logZoom;
    private bool _applyingPreferences;

    private void InitializeImportPreferences()
    {
        // Restore before imports can run. Applying a preference must not write it back.
        this.checkBox_structure.CheckedChanged += (_, _) => this.SaveApplicationPreference(
            () => this._preferencesStore.SetKeepStructure(this.checkBox_structure.Checked));
        this.ApplyApplicationPreferences(this._applicationPreferences);
    }

    private ApplicationPreferences LoadInitialPreferences()
    {
        ApplicationPreferences settings;
        try { settings = this._preferencesStore.Load(); }
        catch (Exception error) when (IsPreferenceError(error))
        {
            LogManager.LogWarning($"Application preferences could not be loaded; defaults will be used. {error.Message}");
            settings = new ApplicationPreferences();
        }
        try { AudioStoragePaths.SetWorkingDirectory(settings.WorkingDirectory); }
        catch (Exception error) when (IsPreferenceError(error))
        {
            LogManager.LogWarning($"The saved audio directory is unavailable; using the default Music directory. {error.Message}");
            AudioStoragePaths.SetWorkingDirectory(null);
        }
        Hints.HintPolicy.SetEnabled(settings.EnableToolTips);
        return settings;
    }

    private void StartPreferencesMonitoring()
    {
        try
        {
            this._preferencesMonitor = new ApplicationPreferencesMonitor(this._preferencesStore.FilePath);
            this._preferencesMonitor.Changed += this.PreferencesFileChanged;
        }
        catch (Exception error) when (IsPreferenceError(error))
        {
            LogManager.LogWarning($"Monitoring preferences from other instances failed. {error.Message}");
        }
        this.Shown += (_, _) => this.ReloadApplicationPreferences();
        this.Disposed += (_, _) =>
        {
            this.StopPreferencesMonitoring();
            this._exportOptions?.Dispose();
            this._logZoom?.Dispose();
        };
        this.UpdateWorkingDirectoryToolTip();
    }

    private void PreferencesFileChanged(object? sender, EventArgs e)
    {
        if (this.IsDisposed || this.Disposing || this._coOpShutdownStarted || !this.IsHandleCreated) return;
        try { this.BeginInvoke((Action)this.ReloadApplicationPreferences); }
        catch (InvalidOperationException) { }
    }

    private void ReloadApplicationPreferences()
    {
        if (this.IsDisposed || this.Disposing || this._coOpShutdownStarted) return;
        // Read at dispatch time, not on the watcher thread, so queued events
        // cannot restore an older snapshot over a newer local preference.
        try { this.ApplyApplicationPreferences(this._preferencesStore.Load()); }
        catch (Exception error) when (IsPreferenceError(error))
        {
            LogManager.LogWarning($"Application preferences could not be refreshed. {error.Message}");
        }
    }

    private void SaveApplicationPreference(Func<ApplicationPreferences> save)
    {
        if (this._applyingPreferences) return;
        try { this.ApplyApplicationPreferences(save()); }
        catch (Exception error) when (IsPreferenceError(error))
        {
            LogManager.LogWarning($"Application preferences could not be saved. {error.Message}");
            this.ApplyApplicationPreferences(this._applicationPreferences);
        }
    }

    private void ApplyApplicationPreferences(ApplicationPreferences settings)
    {
        this._applyingPreferences = true;
        try
        {
            string requestedRoot = settings.WorkingDirectory ?? AudioStoragePaths.DefaultWorkingDirectory;
            if (!string.Equals(requestedRoot, AudioStoragePaths.WorkingDirectory, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(settings.WorkingDirectory, this._applicationPreferences.WorkingDirectory, StringComparison.OrdinalIgnoreCase))
            {
                try { AudioStoragePaths.SetWorkingDirectory(settings.WorkingDirectory); }
                catch (Exception error) when (IsPreferenceError(error))
                {
                    LogManager.LogWarning($"The configured audio directory is unavailable; the current directory is unchanged. {error.Message}");
                }
            }
            this._exportOptions?.Apply(settings.ExportFormat, settings.ExportBits);
            GlobalExportFormat = settings.ExportFormat.TrimStart('.');
            GlobalExportBits = settings.ExportBits;
            this._logZoom?.Apply(settings.LogFontSize);
            this.checkBox_structure.Checked = settings.KeepStructure;
            this.checkBox_oneBag.Checked = settings.AllInOneBag;
            this.button_newBag.Enabled = !settings.AllInOneBag;
            Hints.HintPolicy.SetEnabled(settings.EnableToolTips);
            if (this.enableToolTipsMenuItem is { } item) item.Checked = settings.EnableToolTips;
            this._applicationPreferences = settings;
            this.UpdateWorkingDirectoryToolTip();
        }
        finally { this._applyingPreferences = false; }
    }

    private void UpdateWorkingDirectoryToolTip() => this.toolTip_playlist.SetToolTip(this.button_browse,
        $"Audio directory: {this.AudioC.WorkingDirectory}\nClick: select main audio directory.\nCtrl+click: open current directory in Explorer.");

    private void StopPreferencesMonitoring()
    {
        this._preferencesMonitor?.Dispose();
        this._preferencesMonitor = null;
    }

    private static bool IsPreferenceError(Exception error) => error is IOException or UnauthorizedAccessException or
        JsonException or InvalidDataException or ArgumentException or SecurityException or NotSupportedException;
}
