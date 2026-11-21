using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace ModularAudience.CoOp.Networking;

public sealed record CoOpDiscoveredHost(
    Guid PeerId,
    string DisplayName,
    string Address,
    int Port,
    int ProtocolVersion,
    bool RequiresPassword = true)
{
    public override string ToString()
    {
        string passwordNote = this.RequiresPassword ? string.Empty : " — LAN only, no password";
        return $"{this.DisplayName} ({this.Address}:{this.Port}){passwordNote}";
    }
}

internal sealed record CoOpDiscoveryResponse(
    Guid PeerId,
    string DisplayName,
    int Port,
    int ProtocolVersion,
    bool RequiresPassword = true);

/// <summary>
/// Broadcasts a discovery probe into the local network and collects the
/// responses of hosts that are currently offering a co-op session.
/// </summary>
public interface ICoOpDiscoveryClient
{
    Task<IReadOnlyList<CoOpDiscoveredHost>> DiscoverAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

public sealed class CoOpDiscoveryClient : ICoOpDiscoveryClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly int discoveryPort;
    private readonly int portSearchCount;
    private readonly IReadOnlyList<IPAddress>? probeAddresses;

    public CoOpDiscoveryClient(
        int discoveryPort = CoOpDefaults.DefaultDiscoveryPort,
        int portSearchCount = CoOpDefaults.DiscoveryPortCount,
        IReadOnlyList<IPAddress>? probeAddresses = null)
    {
        if (discoveryPort is <= 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(discoveryPort));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(portSearchCount);
        this.discoveryPort = discoveryPort;
        this.portSearchCount = Math.Min(portSearchCount, 65536 - discoveryPort);
        this.probeAddresses = probeAddresses?.ToArray();
    }

    public async Task<IReadOnlyList<CoOpDiscoveredHost>> DiscoverAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (timeout <= TimeSpan.Zero)
        {
            timeout = TimeSpan.FromMilliseconds(CoOpDefaults.DefaultDiscoveryTimeoutMilliseconds);
        }

        var sockets = new List<(Socket Socket, IPAddress BroadcastAddress)>();
        if (this.probeAddresses != null)
        {
            Socket socket = CreateDiscoverySocket(IPAddress.Any);
            sockets.AddRange(this.probeAddresses.Select(address => (socket, address)));
        }
        else
        {
            foreach (CoOpDiscoveryProbeTarget target in CoOpNetworkUtilities.GetDiscoveryProbeTargets(
                         CoOpNetworkUtilities.GetLocalIPv4Networks()))
            {
                try
                {
                    sockets.Add((CreateDiscoverySocket(target.SourceAddress), target.BroadcastAddress));
                }
                catch (SocketException)
                {
                    // A device may expose virtual or transient interfaces that cannot be bound.
                }
            }
        }

        if (sockets.Count == 0)
        {
            sockets.Add((CreateDiscoverySocket(IPAddress.Any), IPAddress.Broadcast));
        }

        var results = new ConcurrentDictionary<Guid, CoOpDiscoveredHost>();
        using var receiverLifetime = new CancellationTokenSource();
        using var receiverCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            receiverLifetime.Token);
        Socket[] uniqueSockets = sockets.Select(entry => entry.Socket).Distinct().ToArray();
        Task[] receivers = uniqueSockets
            .Select(socket => ReceiveResponsesAsync(socket, results, receiverCancellation.Token))
            .ToArray();
        byte[] probe = Encoding.UTF8.GetBytes(CoOpDefaults.DiscoveryProbe);
        try
        {
            for (int offset = 0; offset < this.portSearchCount; offset++)
            {
                int port = this.discoveryPort + offset;
                foreach ((Socket socket, IPAddress address) in sockets)
                {
                    try
                    {
                        await socket.SendToAsync(probe, SocketFlags.None, new IPEndPoint(address, port), cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (SocketException)
                    {
                        // Some adapters reject directed broadcasts. Other interfaces may still work.
                    }
                }
            }

            await Task.Delay(timeout, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            receiverLifetime.Cancel();
            foreach (Socket socket in uniqueSockets)
            {
                socket.Dispose();
            }

            try
            {
                await Task.WhenAll(receivers).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (receiverCancellation.IsCancellationRequested)
            {
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return results.Values
            .OrderBy(host => host.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(host => host.Address, StringComparer.Ordinal)
            .ToList();
    }

    private static Socket CreateDiscoverySocket(IPAddress sourceAddress)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
        {
            EnableBroadcast = true,
            Ttl = 1,
        };
        try
        {
            socket.Bind(new IPEndPoint(sourceAddress, 0));
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static async Task ReceiveResponsesAsync(
        Socket socket,
        ConcurrentDictionary<Guid, CoOpDiscoveredHost> results,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[2048];
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                SocketReceiveFromResult received = await socket.ReceiveFromAsync(
                    buffer,
                    SocketFlags.None,
                    remote,
                    cancellationToken).ConfigureAwait(false);
                if (received.ReceivedBytes <= 0 ||
                    received.RemoteEndPoint is not IPEndPoint remoteEndpoint)
                {
                    continue;
                }

                CoOpDiscoveryResponse? response;
                try
                {
                    response = JsonSerializer.Deserialize<CoOpDiscoveryResponse>(
                        buffer.AsSpan(0, received.ReceivedBytes),
                        JsonOptions);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (response == null || response.PeerId == Guid.Empty ||
                    response.Port is <= 0 or > 65535 ||
                    response.ProtocolVersion != CoOpDefaults.ProtocolVersion)
                {
                    continue;
                }

                string address = remoteEndpoint.Address.ToString();
                var host = new CoOpDiscoveredHost(
                    response.PeerId,
                    response.DisplayName,
                    address,
                    response.Port,
                    response.ProtocolVersion,
                    response.RequiresPassword);
                results.AddOrUpdate(response.PeerId, host, (_, existing) =>
                    !IPAddress.IsLoopback(IPAddress.Parse(existing.Address)) &&
                    IPAddress.IsLoopback(IPAddress.Parse(address))
                        ? existing
                        : host);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.ConnectionReset or SocketError.MessageSize)
            {
                // An unused UDP port can produce ICMP reset errors on Windows.
            }
            catch (SocketException)
            {
                return;
            }
        }
    }
}

/// <summary>
/// Answers discovery probes while the local peer is hosting.
/// </summary>
public sealed class CoOpDiscoveryResponder : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly CancellationTokenSource lifetime = new();
    private readonly List<UdpClient> clients = [];
    private readonly List<Task> loops = [];
    private readonly CoOpDiscoveryResponse response;

    public CoOpDiscoveryResponder(
        Guid peerId,
        string displayName,
        int tcpPort,
        bool requiresPassword = true)
    {
        this.response = new CoOpDiscoveryResponse(
            peerId,
            displayName,
            tcpPort,
            CoOpDefaults.ProtocolVersion,
            requiresPassword);
    }

    public IReadOnlyList<int> Ports { get; private set; } = [];

    public void Start(int preferredPort = CoOpDefaults.DefaultDiscoveryPort, int portSearchCount = CoOpDefaults.DiscoveryPortCount)
    {
        if (this.clients.Count > 0)
        {
            throw new InvalidOperationException("The discovery responder is already running.");
        }

        int attempts = Math.Max(1, portSearchCount);
        for (int offset = 0; offset < attempts; offset++)
        {
            int candidate = preferredPort + offset;
            if (candidate > 65535)
            {
                break;
            }

            UdpClient? client = null;
            try
            {
                client = new UdpClient(AddressFamily.InterNetwork) { ExclusiveAddressUse = true, Ttl = 1 };
                client.Client.Bind(new IPEndPoint(IPAddress.Any, candidate));
                byte[] payload = JsonSerializer.SerializeToUtf8Bytes(this.response, JsonOptions);
                this.clients.Add(client);
                this.loops.Add(this.ListenAsync(client, payload, this.lifetime.Token));
                // Leave the remaining ports available to other local instances.
                break;
            }
            catch (SocketException)
            {
                client?.Dispose();
            }
        }

        if (this.clients.Count == 0)
        {
            throw new IOException("No free UDP discovery port was found.");
        }

        this.Ports = this.clients
            .Select(client => ((IPEndPoint)client.Client.LocalEndPoint!).Port)
            .ToList();
    }

    public async ValueTask DisposeAsync()
    {
        try { this.lifetime.Cancel(); } catch { }
        foreach (UdpClient client in this.clients)
        {
            try { client.Dispose(); } catch { }
        }

        foreach (Task loop in this.loops)
        {
            try { await loop.ConfigureAwait(false); } catch { }
        }

        this.lifetime.Dispose();
    }

    private async Task ListenAsync(UdpClient client, byte[] payload, CancellationToken cancellationToken)
    {
        byte[] probe = Encoding.UTF8.GetBytes(CoOpDefaults.DiscoveryProbe);
        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult received;
            try
            {
                received = await client.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                return;
            }

            if (!received.Buffer.AsSpan().SequenceEqual(probe))
            {
                continue;
            }

            try
            {
                await client.SendAsync(payload, received.RemoteEndPoint, cancellationToken).ConfigureAwait(false);
            }
            catch (SocketException)
            {
                // The requester vanished; keep listening.
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }
}
