using System.Diagnostics;
using System.Text;

namespace ModularAudience.Ytdlp;

internal static class YtdlpProcess
{
    private const int MaximumOutputCharacters = 16 * 1024 * 1024;
    private const int MaximumErrorCharacters = 16 * 1024;

    internal static async Task<string> RunAsync(string executable, IEnumerable<string> arguments,
        string workingDirectory, IProgress<string>? progress, bool reportOutput, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = workingDirectory
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        if (!process.Start())
            throw new InvalidOperationException("Unable to start yt-dlp.");
        using var cancellation = token.Register(() => Kill(process));
        var stdout = ReadAsync(process.StandardOutput, false, reportOutput ? progress : null, process, token);
        var stderr = ReadAsync(process.StandardError, true, progress, process, token);
        try
        {
            await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(token));
            token.ThrowIfCancellationRequested();
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"yt-dlp exited with code {process.ExitCode}.\n{await stderr}");
            return await stdout;
        }
        catch
        {
            Kill(process);
            await DrainAfterFailureAsync(process, stdout, stderr);
            token.ThrowIfCancellationRequested();
            throw;
        }
    }

    private static async Task<string> ReadAsync(StreamReader reader, bool keepTail, IProgress<string>? progress,
        Process process, CancellationToken token)
    {
        try
        {
            var capture = new StringBuilder();
            var line = new StringBuilder();
            var buffer = new char[4096];
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), token)) > 0)
            {
                capture.Append(buffer, 0, count);
                if (keepTail && capture.Length > MaximumErrorCharacters)
                    capture.Remove(0, capture.Length - MaximumErrorCharacters);
                else if (!keepTail && capture.Length > MaximumOutputCharacters)
                    throw new InvalidDataException("yt-dlp output exceeded the allowed limit.");
                if (progress is not null)
                    ReportLines(buffer, count, line, progress);
            }
            if (line.Length > 0)
                progress?.Report(line.ToString());
            return capture.ToString();
        }
        catch
        {
            Kill(process);
            throw;
        }
    }

    private static void ReportLines(char[] buffer, int count, StringBuilder line, IProgress<string> progress)
    {
        for (var index = 0; index < count; index++)
        {
            var character = buffer[index];
            if (character is '\r' or '\n' || line.Length == 1024)
            {
                if (line.Length > 0)
                    progress.Report(line.ToString());
                line.Clear();
            }
            if (!char.IsControl(character))
                line.Append(character);
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        catch (NotSupportedException) { }
    }

    private static async Task DrainAfterFailureAsync(Process process, Task<string> stdout, Task<string> stderr)
    {
        try
        {
            await Task.WhenAll(stdout, stderr, process.WaitForExitAsync()).WaitAsync(TimeSpan.FromSeconds(10));
        }
        // Cleanup must not replace the original failure or cancellation with a reader's exception.
        catch { }
    }
}
