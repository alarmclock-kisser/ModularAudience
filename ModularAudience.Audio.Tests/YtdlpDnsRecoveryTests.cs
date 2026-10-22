using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Ytdlp;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class YtdlpDnsRecoveryTests
{
    [TestMethod]
    public void Download_DnsFailureWithForcedIpv6_OffersExplicitIpv4Recovery()
    {
        var error = new YtdlpProcessException(1,
            "ERROR: [youtube] HAKUNou_JCU: Unable to download API page: Failed to resolve 'www.youtube.com' ([Errno 11001] getaddrinfo failed)");
        var options = new YtdlpDownloadOptions
        {
            Network = new() { IpVersion = YtdlpIpVersion.IPv6, ProxyUrl = string.Empty }
        };
        bool canRecover = YtdlpRecoveryRules.CanApply(error, YtdlpRecoveryAction.IPv4, options);
        Assert.IsTrue(canRecover, "DNS failure with forced IPv6 must offer an explicit IPv4 recovery without disabling TLS.");
    }
}
