using System.Text.RegularExpressions;

namespace ModularAudience.Ytdlp;

public enum YtdlpFailureKind
{
    Unknown, TransientNetwork, ConnectionRefused, Certificate, Authentication, RateLimited, AccessDenied, BrowserCookies
}

public sealed class YtdlpProcessException : InvalidOperationException
{
    public int ExitCode { get; }
    public YtdlpFailureKind FailureKind { get; }
    public TimeSpan? RetryAfter { get; }
    public DateTimeOffset? RetryNotBeforeUtc { get; }

    public YtdlpProcessException(int exitCode, string error, string diagnosis = "")
        : base($"yt-dlp exited with code {exitCode}.\n{YtdlpDiagnosticRedactor.Redact(error)}\n{diagnosis}")
    {
        ExitCode = exitCode;
        FailureKind = Classify(error);
        var schedule = ParseRetryAfter(error);
        RetryAfter = schedule.Delay;
        RetryNotBeforeUtc = schedule.Deadline;
    }

    private static (TimeSpan? Delay, DateTimeOffset? Deadline) ParseRetryAfter(string error)
    {
        var match = Regex.Match(error, @"\bRetry-After\s*:\s*([^\r\n]+)", RegexOptions.IgnoreCase);
        if (!match.Success) return (null, null);
        string value = match.Groups[1].Value.Trim();
        var now = DateTimeOffset.UtcNow;
        if (long.TryParse(value, out long seconds) && seconds >= 0 && seconds <= (DateTimeOffset.MaxValue - now).TotalSeconds)
            return (TimeSpan.FromSeconds(seconds), now.AddSeconds(seconds));
        if (DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var deadline))
            return (deadline > now ? deadline - now : TimeSpan.Zero, deadline);
        // Unknown or pathological server waits must not turn into an immediate retry.
        return (TimeSpan.MaxValue, DateTimeOffset.MaxValue);
    }

    public static YtdlpFailureKind Classify(string error)
    {
        string terminal = error.Split('\n').LastOrDefault(line => line.TrimStart().StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase)) ?? error;
        bool Has(params string[] values) => values.Any(value => terminal.Contains(value, StringComparison.OrdinalIgnoreCase));
        if (Has("CERTIFICATE_VERIFY_FAILED", "certificate verify failed")) return YtdlpFailureKind.Certificate;
        if (Has("cookie database", "decrypt cookies", "decrypt cookie", "DPAPI", "app-bound", "failed to load cookies")) return YtdlpFailureKind.BrowserCookies;
        if (Has("HTTP Error 429", "Too Many Requests")) return YtdlpFailureKind.RateLimited;
        if (Has("DRM", "members-only", "members only", "not available in your country", "copyright", "video is private")) return YtdlpFailureKind.AccessDenied;
        if (Has("sign in", "login required", "log in", "authentication required", "use --cookies")) return YtdlpFailureKind.Authentication;
        if (Has("10061", "connection refused")) return YtdlpFailureKind.ConnectionRefused;
        if (Has("HTTP Error 502", "HTTP Error 503", "HTTP Error 504", "timed out", "connection reset")) return YtdlpFailureKind.TransientNetwork;
        return YtdlpFailureKind.Unknown;
    }
}
