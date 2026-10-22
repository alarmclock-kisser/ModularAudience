namespace ModularAudience.Ytdlp;

public enum YtdlpIpVersion { Automatic, IPv4, IPv6 }

public sealed record YtdlpNetworkOptions
{
    public YtdlpIpVersion IpVersion { get; init; }
    // null inherits the process environment; empty explicitly disables proxies.
    public string? ProxyUrl { get; init; }
    public bool UseSystemCertificateTrust { get; init; }

    public void Validate() => AddArguments([]);

    internal void AddArguments(List<string> arguments)
    {
        if (!Enum.IsDefined(IpVersion)) throw new ArgumentException("Invalid IP version.");
        if (UseSystemCertificateTrust) arguments.AddRange(["--compat-options", "no-certifi"]);
        if (IpVersion != YtdlpIpVersion.Automatic)
            arguments.Add(IpVersion == YtdlpIpVersion.IPv4 ? "--force-ipv4" : "--force-ipv6");
        if (ProxyUrl is null) return;
        if (ProxyUrl.Length > 2048 || ProxyUrl.Any(char.IsControl)) throw new ArgumentException("Invalid proxy URL.");
        if (ProxyUrl.Length > 0 && (!Uri.TryCreate(ProxyUrl, UriKind.Absolute, out var proxy) ||
            proxy.Scheme is not ("http" or "https") || string.IsNullOrEmpty(proxy.Host) ||
            !string.IsNullOrEmpty(proxy.UserInfo) || !string.IsNullOrEmpty(proxy.Query) ||
            !string.IsNullOrEmpty(proxy.Fragment) || proxy.AbsolutePath != "/"))
            throw new ArgumentException("Use an HTTP(S) proxy URL without credentials, path, query or fragment.");
        arguments.AddRange(["--proxy", ProxyUrl]);
    }
}
