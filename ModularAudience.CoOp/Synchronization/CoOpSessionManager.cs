using ModularAudience.Audio;
using ModularAudience.CoOp.Networking;
using ModularAudience.CoOp.Protocol;

namespace ModularAudience.CoOp.Synchronization;

/// <summary>
/// Orchestrates hosting, joining and the bidirectional collection relay.
/// All state mutations are serialized and, when a UI synchronization context
/// is supplied, executed on that context.
/// </summary>
public sealed class CoOpSessionManager : IAsyncDisposable
{
    private readonly CoOpSessionOptions options;
    private readonly SynchronizationContext? uiContext;
    private readonly SemaphoreSlim sessionGate = new(1, 1);
    private readonly object peerGate = new();
    private readonly List<PeerSession> peers = [];
    private readonly Dictionary<Guid, CoOpLocalShare> localShares = [];
    private readonly Dictionary<Guid, CoOpRemoteCollection> remoteCollections = [];
    private readonly Dictionary<AudioCollection, Guid> collectionIds = [];
    private readonly Guid localPeerId = Guid.NewGuid();
    private CoOpServer? server;
    private CoOpDiscoveryResponder? discoveryResponder;
    private CoOpConnection? hostConnection;
    private CoOpPeerInfo? hostPeer;
    private CoOpSessionState state = CoOpSessionState.Disconnected;
    private TaskCompletionSource<WelcomeMessage>? pendingWelcome;
    private string? sessionAddress;
    private int sessionPort;
    private bool disposed;

    public CoOpSessionManager(CoOpSessionOptions options, SynchronizationContext? uiContext = null)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.uiContext = uiContext;
    }

    public Guid LocalPeerId => this.localPeerId;

    public string DisplayName => this.options.DisplayName;

    public CoOpSessionState State => this.state;

    public bool IsSessionActive => this.state is CoOpSessionState.Hosting or CoOpSessionState.Connected;

    public string? SessionAddress => this.sessionAddress;

    public int SessionPort => this.sessionPort;

    public CoOpPeerInfo? HostPeer => this.hostPeer;

    public IReadOnlyCollection<CoOpLocalShare> LocalShares
    {
        get
        {
            lock (this.localShares)
            {
                return this.localShares.Values.ToList();
            }
        }
    }

    public IReadOnlyCollection<CoOpRemoteCollection> RemoteCollections
    {
        get
        {
            lock (this.remoteCollections)
            {
                return this.remoteCollections.Values.ToList();
            }
        }
    }

    public IReadOnlyCollection<CoOpPeerInfo> Peers
    {
        get
        {
            lock (this.peerGate)
            {
                return this.peers
                    .Where(peer => peer.PeerId != Guid.Empty)
                    .Select(peer => new CoOpPeerInfo(peer.PeerId, peer.DisplayName))
                    .ToList();
            }
        }
    }

    public event EventHandler<CoOpStateChangedEventArgs>? StateChanged;

    public event EventHandler<CoOpPeerEventArgs>? PeerJoined;

    public event EventHandler<CoOpPeerEventArgs>? PeerLeft;

    public event EventHandler<CoOpRemoteCollectionEventArgs>? RemoteCollectionAdded;

    public event EventHandler<CoOpRemoteCollectionRemovedEventArgs>? RemoteCollectionRemoved;

    public event EventHandler<CoOpRemoteCollectionEventArgs>? RemoteCollectionStateChanged;

    public event EventHandler<CoOpRemoteAudioEventArgs>? RemoteAudioAdded;

    public event EventHandler<CoOpRemoteAudioEventArgs>? RemoteAudioUpdated;

    public event EventHandler<CoOpRemoteAudioEventArgs>? RemoteAudioRemoved;

    public event EventHandler<CoOpAudioSyncStateEventArgs>? AudioSyncStateChanged;

    public event EventHandler? SharesChanged;

    public event EventHandler<CoOpLogEventArgs>? Log;

    public Task<CoOpHostInfo> StartHostingAsync(CancellationToken cancellationToken = default)
    {
        return this.RunExclusiveAsync(async () =>
        {
            ObjectDisposedException.ThrowIf(this.disposed, this);
            if (this.state != CoOpSessionState.Disconnected)
            {
                throw new InvalidOperationException("A co-op session is already active.");
            }

            this.ClearSessionState();

            var newServer = new CoOpServer(this.options.Serializer);
            newServer.ClientConnected += this.Server_ClientConnected;
            newServer.ClientDisconnected += this.Server_ClientDisconnected;
            try
            {
                await newServer.StartAsync(
                    this.options.PreferredPort,
                    this.options.PortSearchCount,
                    this.options.BindAddress,
                    cancellationToken);

                this.server = newServer;
                this.sessionPort = newServer.Port;
                this.sessionAddress = CoOpNetworkUtilities.GetPreferredLocalAddress();

                if (this.options.EnableDiscoveryResponder)
                {
                    try
                    {
                        this.discoveryResponder = new CoOpDiscoveryResponder(
                            this.localPeerId,
                            this.options.DisplayName,
                            this.sessionPort);
                        this.discoveryResponder.Start(
                            this.options.DiscoveryPort,
                            this.options.DiscoveryPortSearchCount);
                    }
                    catch (Exception ex)
                    {
                        this.RaiseLog("Co-op discovery responder could not be started.", ex);
                    }
                }

                this.state = CoOpSessionState.Hosting;
                this.RaiseStateChanged();
                this.RaiseLog($"Co-op hosting started on {this.sessionAddress}:{this.sessionPort}.");
                return new CoOpHostInfo(this.sessionAddress, this.sessionPort, this.localPeerId);
            }
            catch
            {
                try { await newServer.DisposeAsync(); } catch { }
                throw;
            }
        }, cancellationToken);
    }

    public async Task<CoOpPeerInfo> ConnectAsync(
        string host,
        int port,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        if (this.state != CoOpSessionState.Disconnected)
        {
            throw new InvalidOperationException("A co-op session is already active.");
        }

        await this.RunExclusiveAsync(() =>
        {
            this.ClearSessionState();
            return Task.CompletedTask;
        }, cancellationToken);

        CoOpConnection? connection = null;
        try
        {
            connection = await CoOpClient.ConnectAsync(
                host,
                port,
                this.options.Serializer,
                this.options.ConnectTimeout,
                cancellationToken);

            this.hostConnection = connection;
            connection.MessageReceived += this.Connection_MessageReceived;
            connection.Closed += this.HostConnection_Closed;

            var welcomeSource = new TaskCompletionSource<WelcomeMessage>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            this.pendingWelcome = welcomeSource;

            await connection.SendAsync(
                new HelloMessage(this.localPeerId, this.options.DisplayName),
                cancellationToken);

            WelcomeMessage welcome = await welcomeSource.Task
                .WaitAsync(this.options.ConnectTimeout, cancellationToken);

            if (welcome.ProtocolVersion != CoOpDefaults.ProtocolVersion)
            {
                throw new CoOpProtocolException(
                    $"The host uses protocol version {welcome.ProtocolVersion}, but this client supports {CoOpDefaults.ProtocolVersion}.");
            }

            return await this.RunExclusiveAsync(async () =>
            {
                if (!ReferenceEquals(this.hostConnection, connection) || !connection.IsConnected)
                {
                    throw new IOException("The co-op host closed the connection during the handshake.");
                }

                this.pendingWelcome = null;
                this.hostPeer = new CoOpPeerInfo(welcome.PeerId, welcome.DisplayName);
                this.state = CoOpSessionState.Connected;
                this.sessionAddress = connection.RemoteEndPoint;
                this.sessionPort = port;
                this.RaiseStateChanged();
                this.RaiseLog($"Connected to co-op host {welcome.DisplayName} ({host}:{port}). " +
                    $"Direct TCP: {connection.LocalEndPoint} -> {connection.RemoteEndPoint}; no Internet relay.");
                await this.SendAllLocalSharesAsync(cancellationToken);
                return this.hostPeer;
            }, cancellationToken);
        }
        catch
        {
            if (connection != null)
            {
                try { await connection.DisposeAsync(); } catch { }
            }

            this.hostConnection = null;
            this.pendingWelcome = null;
            this.hostPeer = null;
            this.sessionAddress = null;
            this.sessionPort = 0;
            this.state = CoOpSessionState.Disconnected;
            lock (this.localShares)
            {
                foreach (CoOpLocalShare share in this.localShares.Values)
                {
                    share.Dispose();
                }
                this.localShares.Clear();
            }

            this.RaiseSharesChanged();
            this.RaiseStateChanged();
            throw;
        }
    }

    public Task<CoOpLocalShare> ShareCollectionAsync(
        AudioCollection collection,
        string? name = null,
        CancellationToken cancellationToken = default)
    {
        return this.RunExclusiveAsync(async () =>
        {
            ObjectDisposedException.ThrowIf(this.disposed, this);
            ArgumentNullException.ThrowIfNull(collection);
            if (!this.IsSessionActive)
            {
                throw new InvalidOperationException("Start or join a co-op session before sharing a collection.");
            }

            CoOpLocalShare? existing = this.FindLocalShare(collection);
            if (existing != null)
            {
                return existing;
            }

            Guid collectionId = this.GetOrCreateCollectionId(collection);
            var share = new CoOpLocalShare(
                collectionId,
                string.IsNullOrWhiteSpace(name) ? "Shared Collection" : name,
                collection,
                this.options.DisplayName);

            share.Tracker.ItemAdded += (_, e) => this.OnLocalShareItemAdded(share, e.Audio);
            share.Tracker.ItemChanged += (_, e) => this.OnLocalShareItemChanged(share, e.Audio);
            share.Tracker.ItemRemoved += (_, e) => this.OnLocalShareItemRemoved(share, e.Audio);
            share.AudioSyncStateChanged += (_, e) => this.AudioSyncStateChanged?.Invoke(this, e);

            lock (this.localShares)
            {
                this.localShares[collectionId] = share;
            }

            this.RaiseSharesChanged();
            try
            {
                await this.SendShareSequenceAsync(share, cancellationToken);
            }
            catch
            {
                lock (this.localShares)
                {
                    if (ReferenceEquals(this.localShares.GetValueOrDefault(collectionId), share))
                    {
                        this.localShares.Remove(collectionId);
                    }
                }

                share.Dispose();
                this.RaiseSharesChanged();
                throw;
            }

            this.RaiseLog($"Shared collection '{share.Name}'.");
            return share;
        }, cancellationToken);
    }

    public Task UnshareCollectionAsync(Guid collectionId, CancellationToken cancellationToken = default)
    {
        CoOpLocalShare? share;
        lock (this.localShares)
        {
            this.localShares.Remove(collectionId, out share);
        }

        if (share == null)
        {
            return Task.CompletedTask;
        }

        // Detach the tracker synchronously. Callers may be closing the owning
        // collection window, and disposing the collection must not look like a
        // removal to the other peers.
        share.Dispose();
        this.RaiseSharesChanged();

        return this.RunExclusiveAsync(async () =>
        {
            if (this.IsSessionActive)
            {
                await this.SendToSessionAsync(
                    new UnshareCollectionMessage(collectionId, "The owner unshared the collection."),
                    null,
                    cancellationToken);
            }

            this.RaiseLog($"Unshared collection '{share.Name}'.");
        }, cancellationToken);
    }

    public CoOpLocalShare? FindLocalShare(AudioCollection collection)
    {
        lock (this.localShares)
        {
            return this.localShares.Values.FirstOrDefault(
                share => ReferenceEquals(share.Collection, collection));
        }
    }

    public CoOpLocalShare? FindLocalShare(Guid collectionId)
    {
        lock (this.localShares)
        {
            return this.localShares.GetValueOrDefault(collectionId);
        }
    }

    public CoOpRemoteCollection? FindRemoteCollection(Guid collectionId)
    {
        lock (this.remoteCollections)
        {
            return this.remoteCollections.GetValueOrDefault(collectionId);
        }
    }

    public bool IsAudioPending(Guid collectionId, Guid audioId)
    {
        CoOpLocalShare? share = this.FindLocalShare(collectionId);
        if (share != null)
        {
            return share.IsAudioPending(audioId);
        }

        CoOpRemoteCollection? remote = this.FindRemoteCollection(collectionId);
        return remote?.IsAudioPending(audioId) == true;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        return this.RunExclusiveAsync(() => this.StopCoreAsync(cancellationToken), cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (this.disposed)
        {
            return;
        }

        try
        {
            await this.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Shutdown is best effort.
        }

        this.disposed = true;
        await this.DispatchAsync(() =>
        {
            foreach (CoOpRemoteCollection collection in this.RemoteCollections)
            {
                this.RemoteCollectionRemoved?.Invoke(this, new CoOpRemoteCollectionRemovedEventArgs(collection.CollectionId));
                try { collection.Dispose(); } catch { }
            }

            lock (this.remoteCollections)
            {
                this.remoteCollections.Clear();
            }

            return Task.CompletedTask;
        }).ConfigureAwait(false);

        this.sessionGate.Dispose();
    }

    private async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        if (this.state == CoOpSessionState.Disconnected)
        {
            return;
        }

        if (this.state == CoOpSessionState.Hosting)
        {
            await this.BroadcastAsync(new SessionEndedMessage("The host ended the session."), null, cancellationToken);
        }

        // Mark the session as inactive before closing the transport so the
        // per-peer Closed callbacks do not try to broadcast peer-left messages
        // while the server is being torn down.
        this.state = CoOpSessionState.Disconnected;

        if (this.discoveryResponder != null)
        {
            try { await this.discoveryResponder.DisposeAsync(); } catch { }
            this.discoveryResponder = null;
        }

        if (this.server != null)
        {
            try { await this.server.DisposeAsync(); } catch { }
            this.server = null;
        }

        if (this.hostConnection != null)
        {
            try { await this.hostConnection.DisposeAsync(); } catch { }
            this.hostConnection = null;
        }

        this.pendingWelcome = null;
        this.hostPeer = null;
        this.sessionAddress = null;
        this.sessionPort = 0;

        lock (this.localShares)
        {
            foreach (CoOpLocalShare share in this.localShares.Values)
            {
                share.Dispose();
            }
            this.localShares.Clear();
        }

        foreach (CoOpRemoteCollection collection in this.RemoteCollections)
        {
            collection.MarkUnshared();
        }

        lock (this.peerGate)
        {
            this.peers.Clear();
        }

        this.state = CoOpSessionState.Disconnected;
        this.RaiseSharesChanged();
        this.RaiseStateChanged();
        this.RaiseLog("Co-op session stopped.");
    }

    private void ClearSessionState()
    {
        // Received data belongs to the local user now. A new session must not
        // close its windows or dispose samples retained from an earlier session.
        foreach (CoOpRemoteCollection collection in this.RemoteCollections)
        {
            collection.MarkUnshared();
        }

        lock (this.localShares)
        {
            foreach (CoOpLocalShare share in this.localShares.Values)
            {
                share.Dispose();
            }
            this.localShares.Clear();
        }

        lock (this.collectionIds)
        {
            this.collectionIds.Clear();
        }

        lock (this.peerGate)
        {
            this.peers.Clear();
        }

        this.hostPeer = null;
        this.sessionAddress = null;
        this.sessionPort = 0;
    }

    private Guid GetOrCreateCollectionId(AudioCollection collection)
    {
        lock (this.collectionIds)
        {
            if (this.collectionIds.TryGetValue(collection, out Guid id))
            {
                return id;
            }

            id = Guid.NewGuid();
            this.collectionIds[collection] = id;
            return id;
        }
    }

    private void Server_ClientConnected(object? sender, CoOpServerEventArgs e)
    {
        var peer = new PeerSession(e.Connection);
        lock (this.peerGate)
        {
            this.peers.Add(peer);
        }

        e.Connection.MessageReceived += this.Connection_MessageReceived;
        e.Connection.Closed += this.ServerPeerConnection_Closed;
    }

    private void Server_ClientDisconnected(object? sender, CoOpServerEventArgs e)
    {
        PeerSession? peer = this.RemovePeer(e.Connection);
        if (peer != null)
        {
            _ = this.HandlePeerLeftAsync(peer, "The peer disconnected.");
        }
    }

    private void ServerPeerConnection_Closed(object? sender, CoOpConnectionClosedEventArgs e)
    {
        if (sender is CoOpConnection connection)
        {
            PeerSession? peer = this.RemovePeer(connection);
            if (peer != null)
            {
                _ = this.HandlePeerLeftAsync(peer, e.Error?.Message ?? "The peer disconnected.");
            }
        }
    }

    private PeerSession? RemovePeer(CoOpConnection connection)
    {
        lock (this.peerGate)
        {
            PeerSession? peer = this.peers.FirstOrDefault(candidate => ReferenceEquals(candidate.Connection, connection));
            if (peer != null)
            {
                this.peers.Remove(peer);
            }

            return peer;
        }
    }

    private PeerSession? FindPeer(CoOpConnection connection)
    {
        lock (this.peerGate)
        {
            return this.peers.FirstOrDefault(candidate => ReferenceEquals(candidate.Connection, connection));
        }
    }

    private void Connection_MessageReceived(object? sender, CoOpMessageReceivedEventArgs e)
    {
        if (sender is not CoOpConnection connection)
        {
            return;
        }

        _ = this.HandleIncomingAsync(connection, e.Message);
    }

    private async Task HandleIncomingAsync(CoOpConnection connection, CoOpMessage message)
    {
        try
        {
            await this.RunExclusiveAsync(
                () => this.HandleMessageCoreAsync(connection, message),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            this.RaiseLog($"Co-op message handling failed for '{message.GetType().Name}'.", ex);
        }
    }

    private async Task HandleMessageCoreAsync(CoOpConnection connection, CoOpMessage message)
    {
        if (ReferenceEquals(connection, this.hostConnection))
        {
            await this.HandleHostMessageAsync(message);
            return;
        }

        PeerSession? peer = this.FindPeer(connection);
        if (peer == null)
        {
            return;
        }

        await this.HandlePeerMessageAsync(peer, message);
    }

    private async Task HandleHostMessageAsync(CoOpMessage message)
    {
        switch (message)
        {
            case WelcomeMessage welcome:
                this.hostPeer = new CoOpPeerInfo(welcome.PeerId, welcome.DisplayName);
                this.pendingWelcome?.TrySetResult(welcome);
                break;
            case ShareCollectionMessage share:
                this.HandleShareCollectionMessage(share, null);
                break;
            case ShareCollectionCompletedMessage completed:
                this.FindRemoteCollection(completed.CollectionId)?.CompleteSynchronization();
                break;
            case AudioAddedMessage added:
                await this.HandleRemoteAudioAddedAsync(null, added.CollectionId, added.Audio);
                break;
            case AudioUpdatedMessage updated:
                await this.HandleRemoteAudioUpdatedAsync(null, updated.CollectionId, updated.Audio);
                break;
            case AudioRemovedMessage removed:
                await this.HandleRemoteAudioRemovedAsync(null, removed.CollectionId, removed.AudioId);
                break;
            case AudioChangeAcknowledgedMessage acknowledged:
                this.HandleAcknowledgement(acknowledged);
                break;
            case UnshareCollectionMessage unshared:
                this.HandleRemoteUnshare(unshared.CollectionId);
                break;
            case PeerJoinedMessage joined:
                this.AddRemotePeer(new CoOpPeerInfo(joined.PeerId, joined.DisplayName));
                break;
            case PeerLeftMessage left:
                this.RemoveRemotePeer(left.PeerId);
                break;
            case SessionEndedMessage ended:
                this.HandleHostEnded(ended.Reason);
                break;
            case ErrorMessage error:
                this.pendingWelcome?.TrySetException(new CoOpProtocolException(error.Message));
                this.RaiseLog($"Co-op host error: {error.Message}");
                break;
            case PingMessage ping:
                if (this.hostConnection != null)
                {
                    await this.hostConnection.SendAsync(new PongMessage(ping.Timestamp));
                }
                break;
        }
    }

    private async Task HandlePeerMessageAsync(PeerSession peer, CoOpMessage message)
    {
        if (peer.PeerId == Guid.Empty && message is not HelloMessage)
        {
            await peer.Connection.SendAsync(new ErrorMessage("Complete the co-op handshake first.", "handshake"));
            return;
        }

        switch (message)
        {
            case HelloMessage hello:
                await this.HandleHelloAsync(peer, hello);
                break;
            case ShareCollectionMessage share:
                await this.HandleClientShareAsync(peer, share);
                break;
            case ShareCollectionCompletedMessage completed:
                CoOpRemoteCollection? collection = this.FindRemoteCollection(completed.CollectionId);
                if (collection?.OwnerPeerId == peer.PeerId && !collection.IsUnshared)
                {
                    collection.CompleteSynchronization();
                    await this.BroadcastAsync(completed, peer.Connection, CancellationToken.None);
                }
                break;
            case AudioAddedMessage added:
                await this.HandleHostAudioAddedAsync(peer, added.CollectionId, added.Audio);
                break;
            case AudioUpdatedMessage updated:
                await this.HandleHostAudioUpdatedAsync(peer, updated.CollectionId, updated.Audio);
                break;
            case AudioRemovedMessage removed:
                await this.HandleHostAudioRemovedAsync(peer, removed.CollectionId, removed.AudioId);
                break;
            case UnshareCollectionMessage unshared:
                await this.HandleClientUnshareAsync(peer, unshared.CollectionId);
                break;
            case PingMessage ping:
                await peer.Connection.SendAsync(new PongMessage(ping.Timestamp));
                break;
            case ErrorMessage error:
                this.RaiseLog($"Co-op peer error from {peer.DisplayName}: {error.Message}");
                break;
        }
    }

    private async Task HandleHelloAsync(PeerSession peer, HelloMessage hello)
    {
        if (peer.PeerId != Guid.Empty)
        {
            return;
        }

        if (hello.ProtocolVersion != CoOpDefaults.ProtocolVersion)
        {
            await peer.Connection.SendAsync(new ErrorMessage(
                $"Unsupported protocol version {hello.ProtocolVersion}.",
                "protocol"));
            return;
        }

        peer.PeerId = hello.PeerId;
        peer.DisplayName = hello.DisplayName;

        await peer.Connection.SendAsync(new WelcomeMessage(this.localPeerId, this.options.DisplayName));

        List<PeerSession> existingPeers;
        lock (this.peerGate)
        {
            existingPeers = this.peers
                .Where(candidate => !ReferenceEquals(candidate, peer) && candidate.PeerId != Guid.Empty)
                .ToList();
        }

        foreach (PeerSession existing in existingPeers)
        {
            await peer.Connection.SendAsync(new PeerJoinedMessage(existing.PeerId, existing.DisplayName));
        }

        await this.SendSessionStateToAsync(peer.Connection);
        await this.BroadcastAsync(
            new PeerJoinedMessage(peer.PeerId, peer.DisplayName),
            peer.Connection,
            CancellationToken.None);

        this.PeerJoined?.Invoke(this, new CoOpPeerEventArgs(new CoOpPeerInfo(peer.PeerId, peer.DisplayName)));
        this.RaiseLog($"Peer '{peer.DisplayName}' joined the co-op session.");
    }

    private async Task SendSessionStateToAsync(CoOpConnection connection)
    {
        List<CoOpLocalShare> shares;
        lock (this.localShares)
        {
            shares = [.. this.localShares.Values];
        }

        foreach (CoOpLocalShare share in shares)
        {
            await connection.SendAsync(new ShareCollectionMessage(
                share.CollectionId,
                share.Name,
                this.localPeerId,
                this.options.DisplayName));

            foreach (AudioObj audio in share.Collection.Audios.ToList())
            {
                await connection.SendAsync(new AudioAddedMessage(
                    share.CollectionId,
                    CoOpAudioSnapshotMapper.Capture(audio)));
            }

            await connection.SendAsync(new ShareCollectionCompletedMessage(share.CollectionId));
        }

        List<CoOpRemoteCollection> remoteCollections;
        lock (this.remoteCollections)
        {
            remoteCollections = [.. this.remoteCollections.Values];
        }

        foreach (CoOpRemoteCollection collection in remoteCollections)
        {
            if (collection.IsUnshared)
            {
                continue;
            }

            await connection.SendAsync(new ShareCollectionMessage(
                collection.CollectionId,
                collection.Name,
                collection.OwnerPeerId,
                collection.OwnerDisplayName));

            foreach (CoOpAudioSnapshot snapshot in collection.GetLateJoinSnapshots())
            {
                await connection.SendAsync(new AudioAddedMessage(collection.CollectionId, snapshot));
            }

            await connection.SendAsync(new ShareCollectionCompletedMessage(collection.CollectionId));
        }
    }

    private async Task HandleClientShareAsync(PeerSession peer, ShareCollectionMessage share)
    {
        CoOpRemoteCollection? collection = this.FindRemoteCollection(share.CollectionId);
        if (share.CollectionId == Guid.Empty || share.OwnerPeerId != peer.PeerId ||
            this.FindLocalShare(share.CollectionId) != null ||
            (collection != null && collection.OwnerPeerId != peer.PeerId))
        {
            await peer.Connection.SendAsync(new ErrorMessage("Only the collection owner can share it.", "collectionOwner"));
            return;
        }

        share = share with { OwnerDisplayName = peer.DisplayName };
        bool isNew = collection == null;
        if (collection == null)
        {
            collection = this.CreateRemoteCollection(share);
        }
        else
        {
            collection.Name = share.Name;
            collection.OwnerDisplayName = share.OwnerDisplayName;
        }

        collection.BeginSynchronization(share.ReplaceExisting);
        await this.BroadcastAsync(share, peer.Connection, CancellationToken.None);
        if (isNew)
        {
            this.RemoteCollectionAdded?.Invoke(this, new CoOpRemoteCollectionEventArgs(collection));
        }
        else
        {
            this.RemoteCollectionStateChanged?.Invoke(this, new CoOpRemoteCollectionEventArgs(collection));
        }
    }

    private void HandleShareCollectionMessage(ShareCollectionMessage share, PeerSession? sender)
    {
        if (share.OwnerPeerId == this.localPeerId)
        {
            return;
        }

        CoOpRemoteCollection? collection = this.FindRemoteCollection(share.CollectionId);
        bool isNew = collection == null;
        if (collection == null)
        {
            collection = this.CreateRemoteCollection(share);
        }
        else
        {
            collection.Name = share.Name;
            collection.OwnerDisplayName = share.OwnerDisplayName;
        }

        collection.BeginSynchronization(share.ReplaceExisting);
        _ = sender;
        if (isNew)
        {
            this.RemoteCollectionAdded?.Invoke(this, new CoOpRemoteCollectionEventArgs(collection));
        }
        else
        {
            this.RemoteCollectionStateChanged?.Invoke(this, new CoOpRemoteCollectionEventArgs(collection));
        }
    }

    private CoOpRemoteCollection CreateRemoteCollection(ShareCollectionMessage share)
    {
        var collection = new CoOpRemoteCollection(
            share.CollectionId,
            share.Name,
            share.OwnerPeerId,
            share.OwnerDisplayName,
            this.options.RemoteCollectionsRoot,
            this.options.UsesDefaultWorkingDirectory);

        collection.StateChanged += (_, _) =>
            this.RemoteCollectionStateChanged?.Invoke(this, new CoOpRemoteCollectionEventArgs(collection));
        collection.AudioSyncStateChanged += (_, e) => this.AudioSyncStateChanged?.Invoke(this, e);

        collection.Tracker.ItemAdded += (_, e) => this.OnRemoteCollectionItemAdded(collection, e.Audio);
        collection.Tracker.ItemChanged += (_, e) => this.OnRemoteCollectionItemChanged(collection, e.Audio);
        collection.Tracker.ItemRemoved += (_, e) => collection.MarkLocallyDiscarded(e.Audio.Id);

        lock (this.remoteCollections)
        {
            this.remoteCollections[collection.CollectionId] = collection;
        }

        return collection;
    }

    private async Task HandleHostAudioAddedAsync(PeerSession sender, Guid collectionId, CoOpAudioSnapshot snapshot)
    {
        CoOpLocalShare? localShare = this.FindLocalShare(collectionId);
        if (localShare != null)
        {
            if (!this.ApplySnapshotToLocalShare(localShare, snapshot, isAdd: true))
            {
                await this.SendAcknowledgementAsync(sender, collectionId, snapshot);
                return;
            }
        }
        else
        {
            CoOpRemoteCollection? remote = this.FindRemoteCollection(collectionId);
            if (remote == null)
            {
                await sender.Connection.SendAsync(new ErrorMessage(
                    $"The shared collection '{collectionId}' is unknown.",
                    "unknownCollection"));
                return;
            }

            if (remote.IsUnshared || (remote.IsDeleted(snapshot.AudioId) && remote.OwnerPeerId != sender.PeerId))
            {
                await this.SendAcknowledgementAsync(sender, collectionId, snapshot);
                return;
            }

            remote.ApplyAdd(snapshot);
        }

        await this.BroadcastAsync(
            new AudioAddedMessage(collectionId, snapshot),
            sender.Connection,
            CancellationToken.None);
        await this.SendAcknowledgementAsync(sender, collectionId, snapshot);
    }

    private async Task HandleHostAudioUpdatedAsync(PeerSession sender, Guid collectionId, CoOpAudioSnapshot snapshot)
    {
        CoOpLocalShare? localShare = this.FindLocalShare(collectionId);
        if (localShare != null)
        {
            if (!this.ApplySnapshotToLocalShare(localShare, snapshot, isAdd: false))
            {
                await this.SendAcknowledgementAsync(sender, collectionId, snapshot);
                return;
            }
        }
        else
        {
            CoOpRemoteCollection? remote = this.FindRemoteCollection(collectionId);
            if (remote == null)
            {
                await sender.Connection.SendAsync(new ErrorMessage(
                    $"The shared collection '{collectionId}' is unknown.",
                    "unknownCollection"));
                return;
            }

            if (remote.IsUnshared || remote.IsDeleted(snapshot.AudioId))
            {
                await this.SendAcknowledgementAsync(sender, collectionId, snapshot);
                return;
            }

            remote.ApplyUpdate(snapshot);
        }

        await this.BroadcastAsync(
            new AudioUpdatedMessage(collectionId, snapshot),
            sender.Connection,
            CancellationToken.None);
        await this.SendAcknowledgementAsync(sender, collectionId, snapshot);
    }

    private async Task HandleHostAudioRemovedAsync(PeerSession sender, Guid collectionId, Guid audioId)
    {
        CoOpRemoteCollection? remote = this.FindRemoteCollection(collectionId);
        if (remote == null)
        {
            // Removals never delete audio from the host's own collection. The
            // owner of a local collection is the only peer allowed to remove
            // an item, and that removal already happened locally.
            return;
        }

        if (remote.OwnerPeerId != sender.PeerId)
        {
            // Other peers may edit tracks, but they must never force a
            // deletion on the collection owner or on other peers.
            return;
        }

        remote.ApplyRemove(audioId);
        await this.BroadcastAsync(
            new AudioRemovedMessage(collectionId, audioId),
            sender.Connection,
            CancellationToken.None);
    }

    private async Task HandleRemoteAudioAddedAsync(PeerSession? sender, Guid collectionId, CoOpAudioSnapshot snapshot)
    {
        CoOpRemoteCollection? remote = this.FindRemoteCollection(collectionId);
        if (remote != null)
        {
            AudioObj? audio = remote.ApplyAdd(snapshot);
            if (audio != null)
            {
                this.RemoteAudioAdded?.Invoke(this, new CoOpRemoteAudioEventArgs(remote, audio, snapshot));
            }
        }
        else
        {
            // The collection is owned by this peer. Updates relayed from another
            // peer must be applied to the original local collection as well.
            CoOpLocalShare? localShare = this.FindLocalShare(collectionId);
            if (localShare != null)
            {
                this.ApplySnapshotToLocalShare(localShare, snapshot, isAdd: true);
            }
        }

        if (sender != null)
        {
            await this.BroadcastAsync(
                new AudioAddedMessage(collectionId, snapshot),
                sender.Connection,
                CancellationToken.None);
            await this.SendAcknowledgementAsync(sender, collectionId, snapshot);
        }
    }

    private async Task HandleRemoteAudioUpdatedAsync(PeerSession? sender, Guid collectionId, CoOpAudioSnapshot snapshot)
    {
        CoOpRemoteCollection? remote = this.FindRemoteCollection(collectionId);
        if (remote != null)
        {
            AudioObj? audio = remote.ApplyUpdate(snapshot);
            if (audio != null)
            {
                this.RemoteAudioUpdated?.Invoke(this, new CoOpRemoteAudioEventArgs(remote, audio, snapshot));
            }
        }
        else
        {
            CoOpLocalShare? localShare = this.FindLocalShare(collectionId);
            if (localShare != null)
            {
                this.ApplySnapshotToLocalShare(localShare, snapshot, isAdd: false);
            }
        }

        if (sender != null)
        {
            await this.BroadcastAsync(
                new AudioUpdatedMessage(collectionId, snapshot),
                sender.Connection,
                CancellationToken.None);
            await this.SendAcknowledgementAsync(sender, collectionId, snapshot);
        }
    }

    private async Task HandleRemoteAudioRemovedAsync(PeerSession? sender, Guid collectionId, Guid audioId)
    {
        CoOpRemoteCollection? remote = this.FindRemoteCollection(collectionId);
        if (remote == null)
        {
            return;
        }

        AudioObj? audio = remote.ApplyRemove(audioId);
        if (audio != null)
        {
            this.RemoteAudioRemoved?.Invoke(this, new CoOpRemoteAudioEventArgs(
                remote,
                audio,
                CoOpAudioSnapshotMapper.Capture(audio)));
        }

        if (sender != null)
        {
            await this.BroadcastAsync(
                new AudioRemovedMessage(collectionId, audioId),
                sender.Connection,
                CancellationToken.None);
        }
    }

    private async Task HandleClientUnshareAsync(PeerSession peer, Guid collectionId)
    {
        CoOpRemoteCollection? remote = this.FindRemoteCollection(collectionId);
        if (remote == null || remote.OwnerPeerId != peer.PeerId)
        {
            return;
        }

        remote.MarkUnshared();
        await this.BroadcastAsync(
            new UnshareCollectionMessage(collectionId, "The owner unshared the collection."),
            peer.Connection,
            CancellationToken.None);
    }

    private void HandleRemoteUnshare(Guid collectionId)
    {
        CoOpRemoteCollection? remote = this.FindRemoteCollection(collectionId);
        remote?.MarkUnshared();
    }

    private void HandleAcknowledgement(AudioChangeAcknowledgedMessage acknowledged)
    {
        CoOpLocalShare? share = this.FindLocalShare(acknowledged.CollectionId);
        if (share != null)
        {
            share.Acknowledge(acknowledged.AudioId, acknowledged.Revision);
            return;
        }

        CoOpRemoteCollection? remote = this.FindRemoteCollection(acknowledged.CollectionId);
        remote?.Acknowledge(acknowledged.AudioId, acknowledged.Revision);
    }

    private bool ApplySnapshotToLocalShare(CoOpLocalShare share, CoOpAudioSnapshot snapshot, bool isAdd)
    {
        if (share.IsRemoved(snapshot.AudioId))
        {
            return false;
        }

        AudioObj? existing = share.Collection[snapshot.AudioId];
        using (share.Tracker.SuppressNotifications())
        {
            if (existing == null)
            {
                if (!isAdd)
                {
                    return false;
                }

                AudioObj audio = CoOpAudioSnapshotMapper.Create(snapshot);
                share.Collection.Audios.Add(audio);
                return true;
            }

            CoOpAudioSnapshotMapper.ApplyTo(existing, snapshot);
            int index = share.Collection.Audios.IndexOf(existing);
            if (index >= 0)
            {
                share.Collection.Audios.ResetItem(index);
            }
        }

        return true;
    }

    private void OnLocalShareItemAdded(CoOpLocalShare share, AudioObj audio)
    {
        share.MarkAdded(audio.Id);
        _ = this.HandleLocalShareChangeAsync(share, audio, CoOpChangeKind.Added);
    }

    private void OnLocalShareItemChanged(CoOpLocalShare share, AudioObj audio)
    {
        _ = this.HandleLocalShareChangeAsync(share, audio, CoOpChangeKind.Updated);
    }

    private void OnLocalShareItemRemoved(CoOpLocalShare share, AudioObj audio)
    {
        share.MarkRemoved(audio.Id);
        _ = this.HandleLocalShareRemovedAsync(share, audio.Id);
    }

    private void OnRemoteCollectionItemAdded(CoOpRemoteCollection collection, AudioObj audio)
    {
        _ = this.HandleRemoteCollectionChangeAsync(collection, audio, CoOpChangeKind.Added);
    }

    private void OnRemoteCollectionItemChanged(CoOpRemoteCollection collection, AudioObj audio)
    {
        _ = this.HandleRemoteCollectionChangeAsync(collection, audio, CoOpChangeKind.Updated);
    }

    private async Task HandleLocalShareChangeAsync(CoOpLocalShare share, AudioObj audio, CoOpChangeKind kind)
    {
        try
        {
            await this.RunExclusiveAsync(async () =>
            {
                if (!this.IsSessionActive || !ReferenceEquals(this.FindLocalShare(share.CollectionId), share) ||
                    !ReferenceEquals(share.Collection[audio.Id], audio))
                {
                    return;
                }

                CoOpAudioSnapshot snapshot = share.Capture(audio);
                share.MarkPending(audio.Id, snapshot.Revision);

                CoOpMessage message = kind == CoOpChangeKind.Added
                    ? new AudioAddedMessage(share.CollectionId, snapshot)
                    : new AudioUpdatedMessage(share.CollectionId, snapshot);

                await this.SendToSessionAsync(message, null, CancellationToken.None);

                if (this.state == CoOpSessionState.Hosting)
                {
                    share.Acknowledge(audio.Id, snapshot.Revision);
                }
            }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            this.RaiseLog($"Synchronizing local audio '{audio.Name}' failed.", ex);
        }
    }

    private async Task HandleLocalShareRemovedAsync(CoOpLocalShare share, Guid audioId)
    {
        try
        {
            await this.RunExclusiveAsync(async () =>
            {
                if (!this.IsSessionActive || !ReferenceEquals(this.FindLocalShare(share.CollectionId), share))
                {
                    return;
                }

                await this.SendToSessionAsync(
                    new AudioRemovedMessage(share.CollectionId, audioId),
                    null,
                    CancellationToken.None);
            }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            this.RaiseLog("Synchronizing a local audio removal failed.", ex);
        }
    }

    private async Task HandleRemoteCollectionChangeAsync(
        CoOpRemoteCollection collection,
        AudioObj audio,
        CoOpChangeKind kind)
    {
        try
        {
            await this.RunExclusiveAsync(async () =>
            {
                if (!this.IsSessionActive || collection.IsUnshared || collection.IsDeleted(audio.Id) ||
                    !ReferenceEquals(this.FindRemoteCollection(collection.CollectionId), collection) ||
                    !ReferenceEquals(collection.Collection[audio.Id], audio))
                {
                    return;
                }

                long revision = collection.NextRevision(audio.Id);
                CoOpAudioSnapshot snapshot = CoOpAudioSnapshotMapper.Capture(audio, revision);
                collection.RecordLocalChange(snapshot);
                collection.MarkPending(audio.Id, revision);

                CoOpMessage message = kind == CoOpChangeKind.Added
                    ? new AudioAddedMessage(collection.CollectionId, snapshot)
                    : new AudioUpdatedMessage(collection.CollectionId, snapshot);

                await this.SendToSessionAsync(message, null, CancellationToken.None);

                if (this.state == CoOpSessionState.Hosting)
                {
                    collection.Acknowledge(audio.Id, revision);
                }
            }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            this.RaiseLog($"Synchronizing a remote collection edit failed.", ex);
        }
    }

    private async Task SendShareSequenceAsync(CoOpLocalShare share, CancellationToken cancellationToken)
    {
        // Capture before the first network await: the owning window may close
        // and dispose its live audio while this transfer is still in progress.
        List<CoOpAudioSnapshot> snapshots = share.Collection.Audios.Select(share.Capture).ToList();
        await this.SendToSessionAsync(
            new ShareCollectionMessage(
                share.CollectionId,
                share.Name,
                this.localPeerId,
                this.options.DisplayName),
            null,
            cancellationToken);

        foreach (CoOpAudioSnapshot snapshot in snapshots)
        {
            share.MarkPending(snapshot.AudioId, snapshot.Revision);
            await this.SendToSessionAsync(
                new AudioAddedMessage(share.CollectionId, snapshot),
                null,
                cancellationToken);

            if (this.state == CoOpSessionState.Hosting)
            {
                share.Acknowledge(snapshot.AudioId, snapshot.Revision);
            }
        }

        await this.SendToSessionAsync(
            new ShareCollectionCompletedMessage(share.CollectionId),
            null,
            cancellationToken);
    }

    private async Task SendAllLocalSharesAsync(CancellationToken cancellationToken)
    {
        List<CoOpLocalShare> shares;
        lock (this.localShares)
        {
            shares = [.. this.localShares.Values];
        }

        foreach (CoOpLocalShare share in shares)
        {
            await this.SendShareSequenceAsync(share, cancellationToken);
        }
    }

    private async Task SendAcknowledgementAsync(
        PeerSession peer,
        Guid collectionId,
        CoOpAudioSnapshot snapshot)
    {
        try
        {
            await peer.Connection.SendAsync(new AudioChangeAcknowledgedMessage(
                collectionId,
                snapshot.AudioId,
                snapshot.Revision));
        }
        catch (Exception ex)
        {
            this.RaiseLog("Sending the co-op acknowledgement failed.", ex);
        }
    }

    private async Task SendToSessionAsync(
        CoOpMessage message,
        CoOpConnection? exceptConnection,
        CancellationToken cancellationToken)
    {
        if (this.state == CoOpSessionState.Hosting)
        {
            await this.BroadcastAsync(message, exceptConnection, cancellationToken);
            return;
        }

        if (this.state == CoOpSessionState.Connected && this.hostConnection != null)
        {
            await this.hostConnection.SendAsync(message, cancellationToken);
        }
    }

    private async Task BroadcastAsync(
        CoOpMessage message,
        CoOpConnection? exceptConnection,
        CancellationToken cancellationToken)
    {
        List<PeerSession> recipients;
        lock (this.peerGate)
        {
            recipients = this.peers
                .Where(peer => peer.PeerId != Guid.Empty &&
                    (exceptConnection == null || !ReferenceEquals(peer.Connection, exceptConnection)))
                .ToList();
        }

        foreach (PeerSession peer in recipients)
        {
            try
            {
                await peer.Connection.SendAsync(message, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                this.RaiseLog($"Sending a co-op message to '{peer.DisplayName}' failed.", ex);
            }
        }
    }

    private async Task HandlePeerLeftAsync(PeerSession peer, string reason)
    {
        try
        {
            await this.RunExclusiveAsync(async () =>
            {
                if (peer.PeerId == Guid.Empty || this.state != CoOpSessionState.Hosting)
                {
                    return;
                }

                await this.BroadcastAsync(
                    new PeerLeftMessage(peer.PeerId, reason),
                    peer.Connection,
                    CancellationToken.None);

                foreach (CoOpRemoteCollection collection in this.RemoteCollections)
                {
                    if (collection.OwnerPeerId == peer.PeerId)
                    {
                        collection.MarkUnshared();
                    }
                }

                this.PeerLeft?.Invoke(this, new CoOpPeerEventArgs(new CoOpPeerInfo(peer.PeerId, peer.DisplayName)));
                this.RaiseLog($"Peer '{peer.DisplayName}' left the co-op session.");
            }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            this.RaiseLog("Handling a co-op peer disconnect failed.", ex);
        }
    }

    private void HostConnection_Closed(object? sender, CoOpConnectionClosedEventArgs e)
    {
        if (sender is not CoOpConnection connection || !ReferenceEquals(connection, this.hostConnection))
        {
            return;
        }

        this.pendingWelcome?.TrySetException(new IOException("The co-op host closed the connection during the handshake.", e.Error));
        _ = this.RunExclusiveAsync(() =>
        {
            if (this.state != CoOpSessionState.Connected || !ReferenceEquals(connection, this.hostConnection))
            {
                return Task.CompletedTask;
            }

            this.pendingWelcome = null;
            this.hostPeer = null;
            this.hostConnection = null;
            this.state = CoOpSessionState.Disconnected;
            this.sessionAddress = null;
            this.sessionPort = 0;
            lock (this.peerGate)
            {
                this.peers.Clear();
            }
            foreach (CoOpRemoteCollection collection in this.RemoteCollections)
            {
                collection.MarkUnshared();
            }

            lock (this.localShares)
            {
                foreach (CoOpLocalShare share in this.localShares.Values)
                {
                    share.Dispose();
                }
                this.localShares.Clear();
            }

            this.RaiseSharesChanged();
            this.RaiseStateChanged("The co-op connection was closed.");
            this.RaiseLog("The co-op connection was closed.", e.Error);
            return Task.CompletedTask;
        }, CancellationToken.None);
    }

    private void HandleHostEnded(string? reason)
    {
        CoOpConnection? connection = this.hostConnection;
        this.hostConnection = null;
        if (connection != null)
        {
            _ = connection.DisposeAsync();
        }

        this.hostPeer = null;
        this.pendingWelcome = null;
        this.state = CoOpSessionState.Disconnected;
        this.sessionAddress = null;
        this.sessionPort = 0;
        lock (this.peerGate)
        {
            this.peers.Clear();
        }
        foreach (CoOpRemoteCollection collection in this.RemoteCollections)
        {
            collection.MarkUnshared();
        }

        lock (this.localShares)
        {
            foreach (CoOpLocalShare share in this.localShares.Values)
            {
                share.Dispose();
            }
            this.localShares.Clear();
        }

        this.RaiseSharesChanged();
        this.RaiseStateChanged(reason ?? "The host ended the co-op session.");
        this.RaiseLog(reason ?? "The host ended the co-op session.");
    }

    private void AddRemotePeer(CoOpPeerInfo peer)
    {
        if (peer.PeerId == this.localPeerId)
        {
            return;
        }

        lock (this.peerGate)
        {
            if (this.peers.All(candidate => candidate.PeerId != peer.PeerId))
            {
                var session = new PeerSession(null!) { PeerId = peer.PeerId, DisplayName = peer.DisplayName };
                this.peers.Add(session);
            }
        }

        this.PeerJoined?.Invoke(this, new CoOpPeerEventArgs(peer));
    }

    private void RemoveRemotePeer(Guid peerId)
    {
        CoOpPeerInfo? peer = null;
        lock (this.peerGate)
        {
            PeerSession? session = this.peers.FirstOrDefault(candidate => candidate.PeerId == peerId);
            if (session != null)
            {
                peer = new CoOpPeerInfo(session.PeerId, session.DisplayName);
                this.peers.Remove(session);
            }
        }

        if (peer != null)
        {
            foreach (CoOpRemoteCollection collection in this.RemoteCollections)
            {
                if (collection.OwnerPeerId == peerId)
                {
                    collection.MarkUnshared();
                }
            }

            this.PeerLeft?.Invoke(this, new CoOpPeerEventArgs(peer));
        }
    }

    private Task<T> RunExclusiveAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        return this.DispatchAsync(async () =>
        {
            await this.sessionGate.WaitAsync(cancellationToken);
            try
            {
                return await action();
            }
            finally
            {
                this.sessionGate.Release();
            }
        });
    }

    private Task RunExclusiveAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        return this.DispatchAsync(async () =>
        {
            await this.sessionGate.WaitAsync(cancellationToken);
            try
            {
                await action();
            }
            finally
            {
                this.sessionGate.Release();
            }
        });
    }

    private Task<T> DispatchAsync<T>(Func<Task<T>> action)
    {
        SynchronizationContext? context = this.uiContext;
        if (context == null || ReferenceEquals(SynchronizationContext.Current, context))
        {
            return action();
        }

        var source = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Post(
            async _ =>
            {
                try
                {
                    source.TrySetResult(await action());
                }
                catch (Exception ex)
                {
                    source.TrySetException(ex);
                }
            },
            null);
        return source.Task;
    }

    private Task DispatchAsync(Func<Task> action)
    {
        SynchronizationContext? context = this.uiContext;
        if (context == null || ReferenceEquals(SynchronizationContext.Current, context))
        {
            return action();
        }

        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Post(
            async _ =>
            {
                try
                {
                    await action();
                    source.TrySetResult();
                }
                catch (Exception ex)
                {
                    source.TrySetException(ex);
                }
            },
            null);
        return source.Task;
    }

    private void RaiseStateChanged(string? message = null)
    {
        this.StateChanged?.Invoke(this, new CoOpStateChangedEventArgs(
            this.state,
            this.state == CoOpSessionState.Disconnected ? null : this.sessionAddress,
            message));
    }

    private void RaiseSharesChanged()
    {
        this.SharesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RaiseLog(string message, Exception? exception = null)
    {
        this.Log?.Invoke(this, new CoOpLogEventArgs(message, exception));
    }

    private enum CoOpChangeKind
    {
        Added,
        Updated
    }

    private sealed class PeerSession(CoOpConnection connection)
    {
        public CoOpConnection Connection { get; } = connection;

        public Guid PeerId { get; set; }

        public string DisplayName { get; set; } = string.Empty;
    }
}
