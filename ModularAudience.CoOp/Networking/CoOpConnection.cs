using System.Net.Sockets;
using ModularAudience.CoOp.Protocol;

namespace ModularAudience.CoOp.Networking;

public sealed class CoOpMessageReceivedEventArgs(CoOpMessage message) : EventArgs
{
    public CoOpMessage Message { get; } = message;
}

public sealed class CoOpConnectionClosedEventArgs(Exception? error) : EventArgs
{
    public Exception? Error { get; } = error;
}

/// <summary>
/// A single framed JSON connection. Sending is serialized so concurrent
/// broadcasts cannot interleave frames on the wire.
/// </summary>
public sealed class CoOpConnection : IAsyncDisposable
{
    private readonly TcpClient client;
    private readonly NetworkStream stream;
    private readonly ICoOpMessageSerializer serializer;
    private readonly SemaphoreSlim sendLock = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly TaskCompletionSource disposalCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? receiveTask;
    private int closed;
    private int disposing;

    public CoOpConnection(TcpClient client, ICoOpMessageSerializer serializer)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        this.stream = client.GetStream();
        this.LocalEndPoint = client.Client.LocalEndPoint?.ToString() ?? "unknown";
        this.RemoteEndPoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
    }

    public Guid ConnectionId { get; } = Guid.NewGuid();

    public string RemoteEndPoint { get; }

    public string LocalEndPoint { get; }

    public bool IsConnected => Volatile.Read(ref this.closed) == 0 && this.client.Connected;

    public event EventHandler<CoOpMessageReceivedEventArgs>? MessageReceived;

    public event EventHandler<CoOpConnectionClosedEventArgs>? Closed;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref this.closed) != 0, this);
        if (this.receiveTask != null)
        {
            return Task.CompletedTask;
        }

        this.receiveTask = this.ReceiveLoopAsync(cancellationToken);
        return Task.CompletedTask;
    }

    public async Task SendAsync(CoOpMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref this.closed) != 0, this);

        byte[] payload = this.serializer.Serialize(message);
        await this.sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref this.closed) != 0, this);
            await CoOpFrameCodec.WriteFrameAsync(this.stream, payload, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            this.Close(ex);
            throw;
        }
        finally
        {
            this.sendLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref this.disposing, 1) != 0)
        {
            await this.disposalCompleted.Task.ConfigureAwait(false);
            return;
        }

        try
        {
            this.Close(null);
            if (this.receiveTask != null)
            {
                try
                {
                    await this.receiveTask.ConfigureAwait(false);
                }
                catch
                {
                    // The receive loop reports its own errors through the Closed event.
                }
            }

            this.lifetime.Dispose();
            this.stream.Dispose();
            this.client.Dispose();
            // Do not dispose sendLock while already queued senders may still
            // enter their finally blocks. It has no unmanaged wait handle.
        }
        finally
        {
            this.disposalCompleted.TrySetResult();
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken startupCancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(startupCancellationToken, this.lifetime.Token);
        CancellationToken cancellationToken = linked.Token;
        Exception? error = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                byte[] payload = await CoOpFrameCodec.ReadFrameAsync(this.stream, cancellationToken).ConfigureAwait(false);
                CoOpMessage message = this.serializer.Deserialize(payload);
                this.MessageReceived?.Invoke(this, new CoOpMessageReceivedEventArgs(message));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        catch (EndOfStreamException)
        {
            // The peer closed the connection.
        }
        catch (IOException ex)
        {
            error = ex;
        }
        catch (Exception ex)
        {
            error = ex;
        }
        finally
        {
            this.Close(error);
        }
    }

    private void Close(Exception? error)
    {
        if (Interlocked.Exchange(ref this.closed, 1) != 0)
        {
            return;
        }

        try { this.lifetime.Cancel(); } catch { }
        try { this.client.Close(); } catch { }
        this.Closed?.Invoke(this, new CoOpConnectionClosedEventArgs(error));
    }
}
