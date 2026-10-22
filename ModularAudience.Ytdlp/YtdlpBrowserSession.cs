namespace ModularAudience.Ytdlp;

public enum YtdlpBrowser { Edge, Chrome, Firefox }

public sealed record YtdlpBrowserSession
{
    public YtdlpBrowser Browser { get; init; }
    public string Profile { get; init; } = string.Empty;
    public bool ConsentGranted { get; init; }

    public void Validate()
    {
        if (!ConsentGranted) throw new ArgumentException("Explicit consent is required to read a browser session for this retry.");
        if (!Enum.IsDefined(Browser)) throw new ArgumentException("Unsupported browser.");
        if (string.IsNullOrWhiteSpace(Profile) || Profile.Length > 120 || Profile is "." or ".." ||
            Profile != Profile.Trim() || Profile.EndsWith('.') ||
            Profile.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-' or ' ')))
            throw new ArgumentException("Enter a local profile name, such as Default, Profile 1 or abc.default-release; paths are not allowed.");
    }

    internal void AddArguments(List<string> arguments)
    {
        Validate();
        arguments.AddRange(["--cookies-from-browser", Browser.ToString().ToLowerInvariant() + ":" + Profile]);
    }
}
