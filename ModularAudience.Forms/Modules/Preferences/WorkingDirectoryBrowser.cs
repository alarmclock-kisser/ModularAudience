using System.Diagnostics;

namespace ModularAudience.Forms;

internal static class WorkingDirectoryBrowser
{
    internal static string? Browse(string currentDirectory, bool controlClick,
        Func<string, string?> selectDirectory, Action<string> openDirectory)
    {
        if (controlClick)
        {
            openDirectory(currentDirectory);
            return null;
        }
        return selectDirectory(currentDirectory);
    }

    internal static ProcessStartInfo ExplorerStartInfo(string directory) => new(directory)
    {
        UseShellExecute = true,
        Verb = "open"
    };
}
