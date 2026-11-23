using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.CoOp.Networking;
using ModularAudience.CoOp.Protocol;
using ModularAudience.CoOp.Security;
using ModularAudience.CoOp.Synchronization;

namespace ModularAudience.CoOp.Tests;

[TestClass]
public sealed class CoOpConnectionTests
{
    [TestMethod]
    public async Task ConcurrentSends_DoNotInterleaveFrames()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var client = new TcpClient();
        Task connecting = client.ConnectAsync(IPAddress.Loopback, port);
        using TcpClient peer = await listener.AcceptTcpClientAsync();
        await connecting;
        var serializer = new JsonCoOpMessageSerializer();
        await using var connection = new CoOpConnection(client, serializer);
        await connection.StartAsync();
        await connection.StartAsync().WaitAsync(TimeSpan.FromSeconds(1));

        Task sending = Task.WhenAll(Enumerable.Range(0, 32)
            .Select(index => connection.SendAsync(new PingMessage(index))));
        var received = new List<long>();
        for (int index = 0; index < 32; index++)
        {
            byte[] frame = await CoOpFrameCodec.ReadFrameAsync(peer.GetStream()).AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            received.Add(((PingMessage)serializer.Deserialize(frame)).Timestamp);
        }

        await sending;
        CollectionAssert.AreEquivalent(Enumerable.Range(0, 32).Select(index => (long)index).ToArray(), received);
    }

    [TestMethod]
    public async Task Dispose_IsSafeWhenCalledConcurrently()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var client = new TcpClient();
        Task connecting = client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        using TcpClient peer = await listener.AcceptTcpClientAsync();
        await connecting;
        await using var connection = new CoOpConnection(client, new JsonCoOpMessageSerializer());
        await connection.StartAsync();

        await Task.WhenAll(connection.DisposeAsync().AsTask(), connection.DisposeAsync().AsTask())
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.IsFalse(connection.IsConnected);
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => connection.SendAsync(new PingMessage(1)));
    }

    [TestMethod]
    public async Task HostDisconnectDuringHandshake_FailsPromptlyAndResetsClientState()
    {
        await using var scope = new SessionTestScope();
        CoOpSessionManager client = scope.CreateManager("Client");
        using X509Certificate2 certificate = CoOpSecurity.LoadOrCreateHostCertificate(
            Path.Combine(scope.Root, "handshake-host.pfx"));
        await using var server = new CoOpServer(new JsonCoOpMessageSerializer(), certificate);
        CoOpConnection? accepted = null;
        server.ClientConnected += (_, args) =>
        {
            accepted = args.Connection;
            _ = args.Connection.DisposeAsync();
        };
        await server.StartAsync(0, 1, IPAddress.Loopback);
        int port = server.Port;
        Task connecting = client.ConnectAsync("127.0.0.1", port);

        await Assert.ThrowsAsync<IOException>(async () => await connecting.WaitAsync(TimeSpan.FromSeconds(2)));

        Assert.AreEqual(CoOpSessionState.Disconnected, client.State);
        Assert.IsNull(client.HostPeer);
        Assert.IsNull(client.SessionAddress);
        if (accepted != null)
        {
            await accepted.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task Client_ConnectsToLocalHostnameUsingDirectTcpAndReportsActualEndpoints()
    {
        var serializer = new JsonCoOpMessageSerializer();
        string root = Path.Combine(Path.GetTempPath(), "ModularAudience-CoOp-ClientTest", Guid.NewGuid().ToString("N"));
        using X509Certificate2 certificate = CoOpSecurity.LoadOrCreateHostCertificate(Path.Combine(root, "host.pfx"));
        await using var server = new CoOpServer(serializer, certificate);
        TaskCompletionSource<CoOpConnection> accepted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        server.ClientConnected += (_, args) => accepted.TrySetResult(args.Connection);
        await server.StartAsync(0, 1, IPAddress.Loopback);
        int port = server.Port;
        Task<CoOpConnection> connecting = CoOpClient.ConnectAsync(
            "localhost",
            port,
            serializer,
            TimeSpan.FromSeconds(3));
        await using CoOpConnection connection = await connecting;
        await connection.StartAsync();
        CoOpConnection peerConnection = await accepted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        await connection.SendAsync(new PingMessage(42));
        TaskCompletionSource<CoOpMessage> received = new(TaskCreationOptions.RunContinuationsAsynchronously);
        peerConnection.MessageReceived += (_, args) => received.TrySetResult(args.Message);
        await peerConnection.StartAsync();
        CoOpMessage message = await received.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.AreEqual(new IPEndPoint(IPAddress.Loopback, port).ToString(), connection.RemoteEndPoint);
        Assert.AreEqual(peerConnection.RemoteEndPoint, connection.LocalEndPoint);
        Assert.AreEqual(42L, ((PingMessage)message).Timestamp);
        await peerConnection.DisposeAsync();
        try { Directory.Delete(root, recursive: true); } catch { }
    }
}
