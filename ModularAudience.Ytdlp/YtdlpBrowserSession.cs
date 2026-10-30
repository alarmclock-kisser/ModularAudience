namespace ModularAudience.Ytdlp;

public enum YtdlpBrowser { Edge, Chrome, Firefox }

public sealed record YtdlpBrowserSession
{
    public override string ToString() => "YouTube browser session (cookie contents omitted)";
    public YtdlpBrowser Browser { get; init; }
    public string Profile { get; init; } = string.Empty;
    public bool ConsentGranted { get; init; }
    public string? CookieFilePath { get; init; }
    public string? CookieContents { get; init; }

    public void Validate()
    {
        if (!ConsentGranted) throw new ArgumentException("Explicit consent is required to read a browser session for this retry.");
        if (CookieContents is not null)
        {
            if (CookieContents.Length > 1024 * 1024 || !CookieContents.StartsWith("# Netscape HTTP Cookie File\n", StringComparison.Ordinal))
                throw new ArgumentException("Invalid browser cookie response.");
            return;
        }
        if (CookieFilePath is not null)
        {
            if (string.IsNullOrWhiteSpace(CookieFilePath) || CookieFilePath.Any(char.IsControl) || !Path.IsPathFullyQualified(CookieFilePath))
                throw new ArgumentException("Select an exported Netscape cookies.txt file using Browse.");
            var file = new FileInfo(CookieFilePath);
            if (!file.Exists || file.Length > 10 * 1024 * 1024)
                throw new ArgumentException("The selected cookie file is missing or larger than 10 MiB.");
            using var reader = file.OpenText();
            string? header = reader.ReadLine();
            if (header is null || !(header.Contains("Netscape HTTP Cookie File", StringComparison.OrdinalIgnoreCase) ||
                header.Contains("HTTP Cookie File", StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Export cookies in Netscape cookies.txt format; JSON exports are not supported.");
            return;
        }
        if (!Enum.IsDefined(Browser)) throw new ArgumentException("Unsupported browser.");
        if (string.IsNullOrWhiteSpace(Profile) || Profile.Length > 120 || Profile is "." or ".." ||
            Profile != Profile.Trim() || Profile.EndsWith('.') ||
            Profile.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-' or ' ')))
            throw new ArgumentException("Enter a local profile name, such as Default, Profile 1 or abc.default-release; paths are not allowed.");
    }

    internal void AddArguments(List<string> arguments)
    {
        Validate();
        if (CookieContents is not null) return; // Materialized only inside the owned process scratch directory.
        if (CookieFilePath is not null)
        {
            arguments.AddRange(["--cookies", Path.GetFullPath(CookieFilePath)]);
            return;
        }
        arguments.AddRange(["--cookies-from-browser", Browser.ToString().ToLowerInvariant() + ":" + Profile]);
    }
}
