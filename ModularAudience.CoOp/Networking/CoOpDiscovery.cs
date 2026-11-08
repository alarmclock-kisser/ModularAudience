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
    int ProtocolVersion)
{
    public override string ToString()
    {
        return $"{this.DisplayName} ({this.Address}:{this.Port})";
    }
}

internal sealed record CoOpDiscoveryResponse(
    Guid PeerId,
    string DisplayName,
    int Port,
    int ProtocolVersion);

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

        var results = new Dictionary<Guid, CoOpDiscoveredHost>();
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
        {
            EnableBroadcast = true
        };
        socket.Bind(new IPEndPoint(IPAddress.Any, 0));

        byte[] probe = Encoding.UTF8.GetBytes(CoOpDefaults.DiscoveryProbe);
        IReadOnlyList<IPAddress> broadcastAddresses = this.probeAddresses ?? CoOpNetworkUtilities.GetDiscoveryBroadcastAddresses();
        for (int offset = 0; offset < this.portSearchCount; offset++)
        {
            int port = this.discoveryPort + offset;
            foreach (IPAddress address in broadcastAddresses)
            {
                try
                {
                    await socket.SendToAsync(probe, SocketFlags.None, new IPEndPoint(address, port), cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (SocketException)
                {
                    // Some adapters reject directed broadcasts. Other addresses may still work.
                }
            }
        }

        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        byte[] buffer = new byte[2048];

        while (!linked.IsCancellationRequested)
        {
            try
            {
                EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                SocketReceiveFromResult received = await socket.ReceiveFromAsync(
                    buffer,
                    SocketFlags.None,
                    remote,
                    linked.Token).ConfigureAwait(false);

                if (received.ReceivedBytes <= 0)
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

                string address = received.RemoteEndPoint is IPEndPoint ipEndPoint
                    ? ipEndPoint.Address.ToString()
                    : string.Empty;
                if (address.Length == 0)
                {
                    continue;
                }

                // Broadcasts and loopback can discover the same host repeatedly.
                // Prefer a LAN address that can also be shared with other peers.
                if (results.TryGetValue(response.PeerId, out CoOpDiscoveredHost? existing) &&
                    !IPAddress.IsLoopback(IPAddress.Parse(existing.Address)) &&
                    IPAddress.IsLoopback(IPAddress.Parse(address)))
                {
                    continue;
                }

                results[response.PeerId] = new CoOpDiscoveredHost(
                    response.PeerId,
                    response.DisplayName,
                    address,
                    response.Port,
                    response.ProtocolVersion);
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                break;
            }
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.ConnectionReset or SocketError.MessageSize)
            {
                // Probing an unused UDP port can produce ICMP reset errors on
                // Windows. It must not hide replies from the remaining hosts.
            }
            catch (SocketException)
            {
                break;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return results.Values
            .OrderBy(host => host.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(host => host.Address, StringComparer.Ordinal)
            .ToList();
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

    public CoOpDiscoveryResponder(Guid peerId, string displayName, int tcpPort)
    {
        this.response = new CoOpDiscoveryResponse(
            peerId,
            displayName,
            tcpPort,
            CoOpDefaults.ProtocolVersion);
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
                client = new UdpClient(AddressFamily.InterNetwork) { ExclusiveAddressUse = true };
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
