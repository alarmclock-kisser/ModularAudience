using System.Net;
using System.Net.Sockets;
using ModularAudience.CoOp.Protocol;

namespace ModularAudience.CoOp.Networking;

public sealed class CoOpServerEventArgs(CoOpConnection connection) : EventArgs
{
    public CoOpConnection Connection { get; } = connection;
}

/// <summary>
/// Accepts incoming co-op TCP connections. The actual protocol handling is
/// performed by <see cref="Synchronization.CoOpSessionManager"/>.
/// </summary>
public sealed class CoOpServer : IAsyncDisposable
{
    private readonly ICoOpMessageSerializer serializer;
    private readonly List<CoOpConnection> connections = [];
    private readonly object connectionsGate = new();
    private readonly CancellationTokenSource lifetime = new();
    private TcpListener? listener;
    private Task? acceptTask;

    public CoOpServer(ICoOpMessageSerializer serializer)
    {
        this.serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    }

    public int Port { get; private set; }

    public event EventHandler<CoOpServerEventArgs>? ClientConnected;

    public event EventHandler<CoOpServerEventArgs>? ClientDisconnected;

    public Task StartAsync(
        int preferredPort = CoOpDefaults.DefaultPort,
        int portSearchCount = CoOpDefaults.PortSearchCount,
        IPAddress? bindAddress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (this.listener != null)
        {
            throw new InvalidOperationException("The co-op server is already running.");
        }

        bindAddress ??= IPAddress.Any;
        TcpListener? boundListener = null;
        int boundPort = 0;

        if (preferredPort == 0)
        {
            boundListener = new TcpListener(bindAddress, 0);
            boundListener.Start();
            boundPort = ((IPEndPoint)boundListener.LocalEndpoint).Port;
        }
        else
        {
            int attempts = Math.Max(1, portSearchCount);
            for (int offset = 0; offset < attempts; offset++)
            {
                int candidate = preferredPort + offset;
                if (candidate > 65535)
                {
                    break;
                }

                var candidateListener = new TcpListener(bindAddress, candidate);
                try
                {
                    candidateListener.Start();
                    boundListener = candidateListener;
                    boundPort = candidate;
                    break;
                }
                catch (SocketException) when (offset + 1 < attempts)
                {
                    candidateListener.Stop();
                }
            }
        }

        if (boundListener == null)
        {
            throw new IOException($"No free co-op port was found in the range {preferredPort}-{preferredPort + Math.Max(1, portSearchCount) - 1}.");
        }

        this.listener = boundListener;
        this.Port = boundPort;

        this.acceptTask = this.AcceptLoopAsync(this.lifetime.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        try { this.lifetime.Cancel(); } catch { }
        try { this.listener?.Stop(); } catch { }

        if (this.acceptTask != null)
        {
            try { await this.acceptTask.ConfigureAwait(false); } catch { }
        }

        List<CoOpConnection> snapshot;
        lock (this.connectionsGate)
        {
            snapshot = [.. this.connections];
            this.connections.Clear();
        }

        foreach (CoOpConnection connection in snapshot)
        {
            try { await connection.DisposeAsync().ConfigureAwait(false); } catch { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await this.StopAsync().ConfigureAwait(false);
        this.lifetime.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        TcpListener? activeListener = this.listener;
        if (activeListener == null)
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient tcpClient;
            try
            {
                tcpClient = await activeListener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
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

            try
            {
                tcpClient.NoDelay = true;
            }
            catch { }

            var connection = new CoOpConnection(tcpClient, this.serializer);
            lock (this.connectionsGate)
            {
                this.connections.Add(connection);
            }

            connection.Closed += this.Connection_Closed;
            this.ClientConnected?.Invoke(this, new CoOpServerEventArgs(connection));
            await connection.StartAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private void Connection_Closed(object? sender, CoOpConnectionClosedEventArgs e)
    {
        if (sender is not CoOpConnection connection)
        {
            return;
        }

        lock (this.connectionsGate)
        {
            this.connections.Remove(connection);
        }

        this.ClientDisconnected?.Invoke(this, new CoOpServerEventArgs(connection));
        _ = connection.DisposeAsync();
    }
}
