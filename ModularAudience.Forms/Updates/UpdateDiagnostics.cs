using System.ComponentModel;
using System.Security;
using System.Security.Cryptography;
using System.Text.Json;
using ModularAudience.Audio;

namespace ModularAudience.Forms.Updates;

internal static class UpdateDiagnostics
{
    internal static bool IsExpected(Exception error) => error is IOException or InvalidDataException or UnauthorizedAccessException or
        HttpRequestException or JsonException or InvalidOperationException or OperationCanceledException or
        Win32Exception or SecurityException or ArgumentException or NotSupportedException or
        CryptographicException or TimeoutException or WaitHandleCannotBeOpenedException;

    internal static void ShowError(IWin32Window? owner, string title, string context, Exception error)
    {
        LogManager.LogError($"{context}: {error}");
        MessageBox.Show(owner, $"{context}\n\n{error.Message}", title, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    internal static void ShowHelperResult(string message, bool warning, string? settingsPath = null)
    {
        string? logError = WriteHelperLog(message, warning, settingsPath);
        if (logError is not null)
        {
            warning = true;
            message += $"\n\nThe update log could not be written: {logError}";
        }
        MessageBox.Show(message, warning ? "Application update warning" : "Application update",
            MessageBoxButtons.OK, warning ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
    }

    internal static string? WriteHelperLog(string message, bool warning, string? settingsPath = null)
    {
        string logMessage = $"{DateTimeOffset.UtcNow:O} {(warning ? "[ERROR]" : "[INFO]")} {message}{Environment.NewLine}";
        try
        {
            string directory = Path.GetDirectoryName(settingsPath ?? ReleaseUpdateSettingsStore.DefaultPath)!;
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "update.log"), logMessage);
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
        {
            return error.Message;
        }
    }
}
