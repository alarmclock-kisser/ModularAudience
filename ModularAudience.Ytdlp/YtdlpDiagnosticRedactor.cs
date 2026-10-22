using System.Text.RegularExpressions;

namespace ModularAudience.Ytdlp;

public static partial class YtdlpDiagnosticRedactor
{
    public static string Redact(string text) => SensitiveHeader().Replace(text, "$1: [redacted]");

    [GeneratedRegex(@"\b(Cookie|Set-Cookie|Authorization|Proxy-Authorization)\s*:\s*[^\r\n]*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveHeader();
}
