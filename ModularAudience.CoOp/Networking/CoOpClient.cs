using System.Net;
using System.Net.Sockets;
using ModularAudience.CoOp.Protocol;

namespace ModularAudience.CoOp.Networking;

public static class CoOpClient
{
    public static async Task<CoOpConnection> ConnectAsync(
        string host,
        int port,
        ICoOpMessageSerializer serializer,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentNullException.ThrowIfNull(serializer);
        if (port is <= 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        IPAddress[] resolved = IPAddress.TryParse(host, out IPAddress? literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, linked.Token).ConfigureAwait(false);
        IReadOnlyList<IPAddress> addresses = CoOpNetworkUtilities.SelectConnectionAddresses(
            resolved, CoOpNetworkUtilities.GetLocalIPv4Networks());
        SocketException? lastError = null;
        foreach (IPAddress address in addresses)
        {
            linked.Token.ThrowIfCancellationRequested();
            TcpClient? client = null;
            try
            {
                client = new TcpClient(address.AddressFamily) { NoDelay = true };
                await client.ConnectAsync(address, port, linked.Token).ConfigureAwait(false);
                var connection = new CoOpConnection(client, serializer);
                await connection.StartAsync().ConfigureAwait(false);
                return connection;
            }
            catch (SocketException error)
            {
                client?.Dispose();
                lastError = error;
            }
            catch
            {
                client?.Dispose();
                throw;
            }
        }

        throw lastError ?? new SocketException((int)SocketError.HostNotFound);
    }
}
