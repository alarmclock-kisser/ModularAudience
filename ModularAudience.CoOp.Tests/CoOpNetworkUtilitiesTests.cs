using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ModularAudience.CoOp.Tests;

[TestClass]
public sealed class CoOpNetworkUtilitiesTests
{
    [TestMethod]
    public void ConnectionAddresses_PreferLocalSubnetWithoutSilentWanFallback()
    {
        IPAddress lan = IPAddress.Parse("192.168.43.25");
        IPAddress wan = IPAddress.Parse("203.0.113.25");
        IPAddress unrelatedPrivate = IPAddress.Parse("10.23.0.25");
        CoOpLocalIPv4Network[] networks = [Network("192.168.43.10")];

        IReadOnlyList<IPAddress> result = CoOpNetworkUtilities.SelectConnectionAddresses(
            [wan, unrelatedPrivate, IPAddress.Parse("2001:db8::25"), lan], networks);

        CollectionAssert.AreEqual(new[] { lan }, result.ToArray());
    }

    [TestMethod]
    public void ConnectionAddresses_DoNotRewriteExplicitRemoteAddresses()
    {
        IPAddress wan = IPAddress.Parse("203.0.113.25");

        IReadOnlyList<IPAddress> result = CoOpNetworkUtilities.SelectConnectionAddresses(
            [wan], [Network("192.168.43.10")]);

        CollectionAssert.AreEqual(new[] { wan }, result.ToArray());
    }

    [TestMethod]
    public void ConnectionAddresses_DoNotPreferTunnelOverPhysicalLan()
    {
        IPAddress lan = IPAddress.Parse("192.168.43.25");
        IPAddress vpn = IPAddress.Parse("10.8.0.25");
        CoOpLocalIPv4Network[] networks = [Network("10.8.0.10", tunnel: true), Network("192.168.43.10")];

        IReadOnlyList<IPAddress> result = CoOpNetworkUtilities.SelectConnectionAddresses([vpn, lan], networks);

        CollectionAssert.AreEqual(new[] { lan }, result.ToArray());
    }

    [TestMethod]
    public void AdvertisedAddress_PrefersLanAdapterWithGatewayOverVirtualOrTunnelAddresses()
    {
        CoOpLocalIPv4Network lan = Network("192.168.43.10");
        CoOpLocalIPv4Network virtualAdapter = Network("172.20.0.1", gateway: false);
        CoOpLocalIPv4Network tunnel = Network("10.8.0.10", tunnel: true);
        CoOpLocalIPv4Network unknownAdapter = Network("10.1.0.10") with { IsLanAdapter = false };

        IReadOnlyList<CoOpLocalIPv4Network> result = CoOpNetworkUtilities.OrderLocalNetworks(
            [tunnel, virtualAdapter, unknownAdapter, lan]);

        Assert.AreSame(lan, result[0]);
        Assert.AreSame(tunnel, result[^1]);
    }

    [TestMethod]
    public void DiscoveryTargets_UsePhysicalSubnetBroadcastsNotTunnelOrDefaultRoutes()
    {
        IReadOnlyList<IPAddress> addresses = CoOpNetworkUtilities.GetDiscoveryBroadcastAddresses(
            [Network("10.8.0.10", tunnel: true), Network("192.168.43.10"), Network("192.168.43.11")]);

        CollectionAssert.AreEquivalent(
            new[] { IPAddress.Loopback, IPAddress.Parse("192.168.43.255") }, addresses.ToArray());
    }

    [TestMethod]
    public void DiscoveryTargets_KeepLoopbackAndLimitedBroadcastFallbackWithoutAdapterInformation()
    {
        IReadOnlyList<IPAddress> addresses = CoOpNetworkUtilities.GetDiscoveryBroadcastAddresses([]);

        CollectionAssert.AreEquivalent(new[] { IPAddress.Loopback, IPAddress.Broadcast }, addresses.ToArray());
    }

    [TestMethod]
    [DataRow("192.168.43.25", "255.255.255.0", true)]
    [DataRow("192.168.44.25", "255.255.255.0", false)]
    [DataRow("192.168.44.25", "255.255.0.0", true)]
    [DataRow("::ffff:192.168.43.25", "255.255.255.0", true)]
    [DataRow("2001:db8::25", "255.255.255.0", false)]
    [DataRow("203.0.113.25", "0.0.0.0", false)]
    [DataRow("192.168.43.25", "255.0.255.0", false)]
    public void SubnetDetection_UsesActualValidMaskRatherThanPrivateAddressHeuristics(string remote, string mask, bool expected)
    {
        CoOpLocalIPv4Network network = Network("192.168.43.10") with { SubnetMask = IPAddress.Parse(mask) };

        Assert.AreEqual(expected, CoOpNetworkUtilities.IsInSameSubnet(IPAddress.Parse(remote), network));
    }

    private static CoOpLocalIPv4Network Network(string address, bool gateway = true, bool tunnel = false) =>
        new(IPAddress.Parse(address), IPAddress.Parse("255.255.255.0"), gateway, IsLanAdapter: !tunnel, IsTunnel: tunnel);
}
