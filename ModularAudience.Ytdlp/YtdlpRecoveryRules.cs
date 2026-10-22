namespace ModularAudience.Ytdlp;

public enum YtdlpRecoveryAction { RetrySameSettings, SystemCertificateTrust, IPv4, BrowserSession }

public sealed record YtdlpRecoveryDecision(YtdlpRecoveryAction Action, YtdlpBrowserSession? Session = null);

public static class YtdlpRecoveryRules
{
    public static bool CanApply(YtdlpProcessException error, YtdlpRecoveryAction action, YtdlpDownloadOptions options) =>
        (error.RetryAfter is null || error.RetryAfter <= TimeSpan.FromSeconds(30)) && CanApply(error.FailureKind, action, options);

    public static bool CanApply(YtdlpFailureKind kind, YtdlpRecoveryAction action, YtdlpDownloadOptions options) => action switch
    {
        YtdlpRecoveryAction.RetrySameSettings => kind is YtdlpFailureKind.TransientNetwork or YtdlpFailureKind.ConnectionRefused,
        YtdlpRecoveryAction.SystemCertificateTrust => kind == YtdlpFailureKind.Certificate && !options.Network.UseSystemCertificateTrust,
        YtdlpRecoveryAction.IPv4 => kind is (YtdlpFailureKind.ConnectionRefused or YtdlpFailureKind.NameResolution) && options.Network.IpVersion != YtdlpIpVersion.IPv4,
        YtdlpRecoveryAction.BrowserSession => kind == YtdlpFailureKind.Authentication && options.BrowserSession is null,
        _ => false
    };

    public static YtdlpDownloadOptions Apply(YtdlpFailureKind kind, YtdlpRecoveryDecision decision, YtdlpDownloadOptions options)
    {
        if (!CanApply(kind, decision.Action, options)) throw new InvalidOperationException("This recovery action is not applicable to the reported failure.");
        if (decision.Action == YtdlpRecoveryAction.BrowserSession)
        {
            if (decision.Session is null) throw new ArgumentException("Select a browser session and grant consent.");
            decision.Session.Validate();
        }
        return decision.Action switch
        {
            YtdlpRecoveryAction.SystemCertificateTrust => options with { Network = options.Network with { UseSystemCertificateTrust = true } },
            YtdlpRecoveryAction.IPv4 => options with { Network = options.Network with { IpVersion = YtdlpIpVersion.IPv4 } },
            YtdlpRecoveryAction.BrowserSession => options with { BrowserSession = decision.Session },
            _ => options
        };
    }
}
