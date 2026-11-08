using System.Diagnostics;

namespace ModularAudience.Forms.Updates;

internal static class UpdatedApplicationLauncher
{
    internal static void Start(string targetPath) => Start(targetPath, StartProcess);

    internal static void Start(string targetPath, Action<ProcessStartInfo> startProcess)
    {
        ArgumentNullException.ThrowIfNull(startProcess);
        if (!Path.IsPathFullyQualified(targetPath) ||
            !targetPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The updated application path must be an absolute executable path.", nameof(targetPath));

        string executablePath = Path.GetFullPath(targetPath);
        try
        {
            startProcess(CreateStartInfo(executablePath, useShellExecute: false));
        }
        catch (Exception directError) when (UpdateDiagnostics.IsExpected(directError))
        {
            try
            {
                // Let Windows activate the executable if direct process creation
                // is unavailable. Never run the staging helper or its arguments.
                startProcess(CreateStartInfo(executablePath, useShellExecute: true));
            }
            catch (Exception shellError) when (UpdateDiagnostics.IsExpected(shellError))
            {
                throw new IOException("Windows could not start the updated application.",
                    new AggregateException(directError, shellError));
            }
        }
    }

    private static ProcessStartInfo CreateStartInfo(string executablePath, bool useShellExecute) => new(executablePath)
    {
        UseShellExecute = useShellExecute,
        WorkingDirectory = Path.GetDirectoryName(executablePath)!
    };

    private static void StartProcess(ProcessStartInfo startInfo)
    {
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Windows did not create an application process.");
    }
}
