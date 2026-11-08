using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.CoOp;

namespace ModularAudience.CoOp.Tests;

[TestClass]
public sealed class CoOpEndpointTests
{
    [TestMethod]
    [DataRow("192.168.1.20:47823", "192.168.1.20", 47823)]
    [DataRow("localhost:1234", "localhost", 1234)]
    [DataRow("my-host", "my-host", CoOpDefaults.DefaultPort)]
    [DataRow("[::1]:47823", "::1", 47823)]
    [DataRow("[fe80::1]", "fe80::1", CoOpDefaults.DefaultPort)]
    [DataRow("::1", "::1", CoOpDefaults.DefaultPort)]
    public void TryParse_ValidAddresses(string input, string expectedHost, int expectedPort)
    {
        bool parsed = CoOpEndpoint.TryParse(input, out CoOpEndpoint endpoint);

        Assert.IsTrue(parsed);
        Assert.AreEqual(expectedHost, endpoint.Host);
        Assert.AreEqual(expectedPort, endpoint.Port);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow(":47823")]
    [DataRow("host:0")]
    [DataRow("host:65536")]
    [DataRow("host:not-a-port")]
    [DataRow("[::1]:")]
    public void TryParse_InvalidAddresses(string input)
    {
        Assert.IsFalse(CoOpEndpoint.TryParse(input, out _));
    }

    [TestMethod]
    public void ToString_RoundTripsIpv4AndIpv6()
    {
        Assert.AreEqual("10.0.0.5:47823", new CoOpEndpoint("10.0.0.5", 47823).ToString());
        Assert.AreEqual("[::1]:47823", new CoOpEndpoint("::1", 47823).ToString());
    }
}
