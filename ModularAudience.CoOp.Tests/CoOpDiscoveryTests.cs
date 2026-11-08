using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.CoOp.Networking;

namespace ModularAudience.CoOp.Tests;

[TestClass]
public sealed class CoOpDiscoveryTests
{
    [TestMethod]
    public async Task Discovery_FindsHostAndDeduplicatesRepeatedProbes()
    {
        Guid peerId = Guid.NewGuid();
        await using var responder = new CoOpDiscoveryResponder(peerId, "Local host", 49123);
        responder.Start(0, 1);
        int port = responder.Ports.Single();
        var client = new CoOpDiscoveryClient(port, 1, [IPAddress.Loopback, IPAddress.Loopback]);

        IReadOnlyList<CoOpDiscoveredHost> hosts = await client.DiscoverAsync(TimeSpan.FromMilliseconds(250));

        CoOpDiscoveredHost host = hosts.Single();
        Assert.AreEqual(peerId, host.PeerId);
        Assert.AreEqual("Local host", host.DisplayName);
        Assert.AreEqual("127.0.0.1", host.Address);
        Assert.AreEqual(49123, host.Port);
    }

    [TestMethod]
    public async Task Discovery_FindsMultipleHostsOnTheSameMachine()
    {
        Guid firstId = Guid.NewGuid();
        Guid secondId = Guid.NewGuid();
        await using var first = new CoOpDiscoveryResponder(firstId, "First host", 49123);
        await using var second = new CoOpDiscoveryResponder(secondId, "Second host", 49124);
        first.Start(0, 1);
        int startPort = Math.Min(first.Ports.Single(), 65531);
        second.Start(startPort, 5);
        var client = new CoOpDiscoveryClient(startPort, 5, [IPAddress.Loopback]);

        IReadOnlyList<CoOpDiscoveredHost> hosts = await client.DiscoverAsync(TimeSpan.FromMilliseconds(300));

        Assert.AreNotEqual(first.Ports.Single(), second.Ports.Single());
        CollectionAssert.AreEquivalent(new[] { firstId, secondId }, hosts.Select(host => host.PeerId).ToArray());
    }

    [TestMethod]
    public async Task Discovery_IgnoresMalformedResponses()
    {
        using var responder = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)responder.Client.LocalEndPoint!).Port;
        var client = new CoOpDiscoveryClient(port, 1, [IPAddress.Loopback]);
        Task<IReadOnlyList<CoOpDiscoveredHost>> discovery = client.DiscoverAsync(TimeSpan.FromMilliseconds(250));
        UdpReceiveResult probe = await responder.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(2));
        await responder.SendAsync(Encoding.UTF8.GetBytes("not JSON"), probe.RemoteEndPoint);
        await responder.SendAsync(Encoding.UTF8.GetBytes("{}"), probe.RemoteEndPoint);

        Assert.AreEqual(0, (await discovery).Count);
    }

    [TestMethod]
    public async Task Discovery_PropagatesCancellation()
    {
        using var responder = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)responder.Client.LocalEndPoint!).Port;
        var client = new CoOpDiscoveryClient(port, 1, [IPAddress.Loopback]);
        using var cancellation = new CancellationTokenSource();
        Task<IReadOnlyList<CoOpDiscoveredHost>> discovery = client.DiscoverAsync(TimeSpan.FromSeconds(5), cancellation.Token);
        await responder.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await discovery);
    }
}
