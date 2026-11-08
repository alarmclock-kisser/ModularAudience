using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;

namespace ModularAudience.CoOp;

/// <summary>
/// A host/port pair used to reach a co-op host.
/// </summary>
public readonly record struct CoOpEndpoint(string Host, int Port)
{
    public static CoOpEndpoint Parse(string value)
    {
        return TryParse(value, out CoOpEndpoint endpoint)
            ? endpoint
            : throw new FormatException($"'{value}' is not a valid co-op address. Use host:port.");
    }

    public static bool TryParse(string? value, out CoOpEndpoint endpoint)
    {
        endpoint = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string text = value.Trim();
        string host;
        int port = CoOpDefaults.DefaultPort;

        if (text.StartsWith('['))
        {
            int closingBracket = text.IndexOf(']');
            if (closingBracket <= 1)
            {
                return false;
            }

            host = text[1..closingBracket];
            string remainder = text[(closingBracket + 1)..];
            if (remainder.Length > 0)
            {
                if (!remainder.StartsWith(':') || !TryParsePort(remainder[1..], out port))
                {
                    return false;
                }
            }
        }
        else
        {
            int firstColon = text.IndexOf(':');
            int lastColon = text.LastIndexOf(':');
            if (firstColon >= 0 && firstColon == lastColon)
            {
                host = text[..firstColon];
                if (!TryParsePort(text[(firstColon + 1)..], out port))
                {
                    return false;
                }
            }
            else
            {
                // A bare IPv6 address without brackets uses the default port.
                host = text;
            }
        }

        host = host.Trim();
        if (host.Length == 0 || !IPAddress.TryParse(host, out _) && host.Any(char.IsWhiteSpace))
        {
            return false;
        }

        endpoint = new CoOpEndpoint(host, port);
        return true;
    }

    public override string ToString()
    {
        return this.Host.Contains(':', StringComparison.Ordinal)
            ? $"[{this.Host}]:{this.Port}"
            : $"{this.Host}:{this.Port}";
    }

    private static bool TryParsePort(string value, out int port)
    {
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out port)
            && port is > 0 and <= 65535;
    }
}
