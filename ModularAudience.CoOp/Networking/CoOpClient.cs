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

        var client = new TcpClient { NoDelay = true };
        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        try
        {
            await client.ConnectAsync(host, port, linked.Token).ConfigureAwait(false);
        }
        catch
        {
            client.Dispose();
            throw;
        }

        var connection = new CoOpConnection(client, serializer);
        await connection.StartAsync().ConfigureAwait(false);
        return connection;
    }
}
