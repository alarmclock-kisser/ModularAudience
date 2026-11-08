using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.CoOp;
using ModularAudience.CoOp.Windows;

namespace ModularAudience.CoOp.Tests;

[TestClass]
public sealed class WindowsFirewallTests
{
    [TestMethod]
    public void BuildAddRuleArguments_UsesPortRangeAndProtocol()
    {
        string arguments = WindowsFirewall.BuildAddRuleArguments(
            new CoOpFirewallRule("My Rule", "TCP", 47823, 47842));

        StringAssert.Contains(arguments, "name=\"My Rule\"");
        StringAssert.Contains(arguments, "dir=in");
        StringAssert.Contains(arguments, "action=allow");
        StringAssert.Contains(arguments, "protocol=TCP");
        StringAssert.Contains(arguments, "localport=47823-47842");
        StringAssert.Contains(arguments, "profile=any");
        StringAssert.Contains(arguments, "enable=yes");
    }

    [TestMethod]
    public void BuildAddRuleArguments_UsesSinglePortWhenRangeIsOnePort()
    {
        string arguments = WindowsFirewall.BuildAddRuleArguments(
            new CoOpFirewallRule("My Rule", "UDP", 47824, 47824));

        StringAssert.Contains(arguments, "localport=47824");
        Assert.IsFalse(arguments.Contains("47824-47824", StringComparison.Ordinal));
    }

    [TestMethod]
    public void BuildShowAndDeleteArguments_QuoteRuleName()
    {
        StringAssert.Contains(WindowsFirewall.BuildShowRuleArguments("My Rule"), "name=\"My Rule\"");
        StringAssert.Contains(WindowsFirewall.BuildDeleteRuleArguments("My Rule"), "name=\"My Rule\"");
    }

    [TestMethod]
    public void CreateRules_CoverDefaultRanges()
    {
        CoOpFirewallRule tcp = WindowsFirewall.CreateTcpRule();
        CoOpFirewallRule udp = WindowsFirewall.CreateUdpRule();

        Assert.AreEqual(WindowsFirewall.TcpRuleName, tcp.Name);
        Assert.AreEqual(CoOpDefaults.DefaultPort, tcp.StartPort);
        Assert.AreEqual(CoOpDefaults.DefaultPort + CoOpDefaults.PortSearchCount - 1, tcp.EndPort);
        Assert.AreEqual(WindowsFirewall.UdpRuleName, udp.Name);
        Assert.AreEqual(CoOpDefaults.DefaultDiscoveryPort, udp.StartPort);
        Assert.AreEqual(CoOpDefaults.DefaultDiscoveryPort + CoOpDefaults.DiscoveryPortCount - 1, udp.EndPort);
    }
}
