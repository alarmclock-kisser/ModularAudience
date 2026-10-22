using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace ModularAudience.Ytdlp;

public static partial class YtdlpNetworkDiagnostics
{
    public static bool IsConnectionRefused(string error) => error.Contains("10061", StringComparison.Ordinal) ||
        error.Contains("Connection refused", StringComparison.OrdinalIgnoreCase);

    public static async Task<string> InspectAsync(string error, IReadOnlyList<string> arguments, CancellationToken token)
    {
        if (YtdlpProcessException.Classify(error) == YtdlpFailureKind.NameResolution)
            return "Network diagnosis: DNS/address resolution failed before HTTPS or audio download. " + DescribeRoute(arguments) +
                " Forced IPv6 requires a usable IPv6 DNS/network route. Try Automatic or an explicitly confirmed IPv4 recovery; " +
                "if resolution still fails, check DNS and the authorized proxy/network. Cookies and disabling TLS verification cannot repair DNS. No settings were changed.";
        if (error.Contains("CERTIFICATE_VERIFY_FAILED", StringComparison.OrdinalIgnoreCase))
            return "Network diagnosis: TLS certificate-chain verification failed before media download. " +
                "Check the certificate chain and any authorized HTTPS inspection/proxy with your administrator. " +
                "Do not disable certificate verification; this is not a codec or retry problem. No trust settings were changed.";
        if (error.Contains("HTTP Error 503", StringComparison.OrdinalIgnoreCase))
            return "Network diagnosis: HTTP 503 Service Unavailable was returned by an upstream server or gateway. " +
                "This response does not establish a local audio-conversion defect; no successful download is claimed.";
        if (!IsConnectionRefused(error)) return string.Empty;
        var lines = new List<string>
        {
            "Network diagnosis: TCP connection refused before TLS/audio conversion. Additional retries or disabling certificate checks cannot repair this.",
            "yt-dlp ignores config files and plugins. " + DescribeRoute(arguments),
            "Process proxy variables (values/credentials redacted): " + string.Join(", ",
                new[] { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "NO_PROXY" }.Select(name =>
                    name + "=" + (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name)) ? "unset" : "set")))
        };
        var hosts = CdnHostPattern().Matches(error).Select(match => match.Value).Distinct(StringComparer.OrdinalIgnoreCase).Take(2);
        foreach (string host in hosts) lines.Add(await ProbeHostAsync(host, token));
        lines.Add("Direct TCP probes do not test a configured proxy and cannot identify which firewall, security product, gateway or remote server refused the original connection. Permit the required googlevideo.com HTTPS route or configure an authorized proxy explicitly. No route or security settings were changed.");
        return string.Join(Environment.NewLine, lines);
    }

    private static string DescribeRoute(IReadOnlyList<string> arguments)
    {
        int proxyIndex = arguments.ToList().IndexOf("--proxy");
        string route = proxyIndex < 0 ? "Proxy: inherited environment/default discovery."
            : arguments[proxyIndex + 1].Length == 0 ? "Proxy: explicitly disabled."
            : "Proxy: explicit HTTP(S) endpoint (redacted).";
        string ip = arguments.Contains("--force-ipv4") ? "IPv4" : arguments.Contains("--force-ipv6") ? "IPv6" : "automatic";
        return route + " IP selection: " + ip + ".";
    }

    private static async Task<string> ProbeHostAsync(string host, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, timeout.Token);
            var selected = addresses.GroupBy(address => address.AddressFamily).Select(group => group.First()).Take(2);
            var results = await Task.WhenAll(selected.Select(address => ProbeAddressAsync(address, timeout.Token)));
            string missing = !addresses.Any(address => address.AddressFamily == AddressFamily.InterNetworkV6)
                ? "; DNS returned no IPv6 address" : string.Empty;
            return $"Direct probe {host}: " + (results.Length == 0 ? "DNS returned no addresses." : string.Join("; ", results) + missing);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return $"Direct probe {host}: timed out."; }
        catch (SocketException error) { return $"Direct probe {host}: DNS {error.SocketErrorCode}."; }
    }

    private static async Task<string> ProbeAddressAsync(IPAddress address, CancellationToken token)
    {
        using var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, 443), token);
            return $"{address.AddressFamily} {address}:443 TCP reachable (not a download test)";
        }
        catch (SocketException error) { return $"{address.AddressFamily} {address}:443 {error.SocketErrorCode}"; }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return $"{address.AddressFamily} {address}:443 cancelled/timed out"; }
    }

    [GeneratedRegex(@"\b[a-zA-Z0-9-]+\.googlevideo\.com\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CdnHostPattern();
}
