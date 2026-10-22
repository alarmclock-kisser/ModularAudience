using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Ytdlp;
using System.Globalization;
using System.Reflection;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class YtdlpRepairTests
{
    [TestMethod]
    public void WavBitDepth_MapsToActualPcmCodecsAndRejectsUnsupportedDepths()
    {
        var codec = typeof(YtdlpClient).GetMethod("WavCodec", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var pair in new[] { (8, "pcm_u8"), (16, "pcm_s16le"), (24, "pcm_s24le"), (32, "pcm_s32le") })
        {
            Assert.AreEqual(pair.Item2, codec.Invoke(null, [pair.Item1]));
            var arguments = AudioArguments("wav", new() { WavBitDepth = pair.Item1 });
            CollectionAssert.AreEqual(new[] { "--postprocessor-args", "ExtractAudio+ffmpeg_o:-c:a " + pair.Item2 }, arguments);
        }
        CollectionAssert.AreEqual(new[] { "--audio-quality", "256K" }, AudioArguments("mp3", new() { Mp3Bitrate = 256 }));
        var error = Assert.ThrowsExactly<TargetInvocationException>(() => codec.Invoke(null, [20]));
        Assert.IsInstanceOfType<ArgumentOutOfRangeException>(error.InnerException);
    }

    private static List<string> AudioArguments(string format, YtdlpDownloadOptions options)
    {
        var arguments = new List<string>();
        typeof(YtdlpClient).GetMethod("AddAudioQualityArguments", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [arguments, format, options]);
        return arguments;
    }

    [TestMethod]
    public void AssetTransfer_UsesMegabytesAndInvariantFormattingUnderGermanCulture()
    {
        var type = typeof(YtdlpClient).Assembly.GetType("ModularAudience.Ytdlp.AssetTransferProgress")!;
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var text = (string)type.GetMethod("Format", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, ["ffmpeg.zip", 12_500_000L, 100_000_000L])!;
            Assert.AreEqual("Downloading ffmpeg.zip: 12.50 / 100.00 MB", text);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [TestMethod]
    public void Network_DefaultRoutingMakesNoChanges_ExplicitRoutingUsesSafeArguments()
    {
        var add = typeof(YtdlpNetworkOptions).GetMethod("AddArguments", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var arguments = new List<string>();
        add.Invoke(new YtdlpNetworkOptions(), [arguments]);
        Assert.HasCount(0, arguments);
        add.Invoke(new YtdlpNetworkOptions { IpVersion = YtdlpIpVersion.IPv4, ProxyUrl = "" }, [arguments]);
        CollectionAssert.AreEqual(new[] { "--force-ipv4", "--proxy", "" }, arguments);
        foreach (var proxy in new[] { "file:///C:/test", "https://user:password@proxy.example", "https://proxy.example/path", "https://proxy.example\n--no-check-certificates" })
            Assert.ThrowsExactly<ArgumentException>(() => new YtdlpNetworkOptions { ProxyUrl = proxy }.Validate());
        new YtdlpNetworkOptions { IpVersion = YtdlpIpVersion.IPv6, ProxyUrl = "http://proxy.example:8080" }.Validate();
    }

    [TestMethod]
    public async Task Network_ConnectionRefusedExplainsTcpLayer_WithoutInventingFirewallCause()
    {
        string diagnosis = await YtdlpNetworkDiagnostics.InspectAsync("[WinError 10061] Connection refused", [], CancellationToken.None);
        StringAssert.Contains(diagnosis, "before TLS/audio conversion");
        StringAssert.Contains(diagnosis, "cannot identify which firewall");
        StringAssert.Contains(diagnosis, "No route or security settings were changed");
        Assert.AreEqual(string.Empty, await YtdlpNetworkDiagnostics.InspectAsync("HTTP Error 403", [], CancellationToken.None));
        StringAssert.Contains(await YtdlpNetworkDiagnostics.InspectAsync("CERTIFICATE_VERIFY_FAILED", [], CancellationToken.None), "Do not disable certificate verification");
        StringAssert.Contains(await YtdlpNetworkDiagnostics.InspectAsync("HTTP Error 503", [], CancellationToken.None), "upstream server or gateway");
    }
}
