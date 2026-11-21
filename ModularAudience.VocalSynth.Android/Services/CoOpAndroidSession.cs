using System.Runtime.InteropServices;
using ModularAudience.CoOp;
using ModularAudience.CoOp.Networking;
using ModularAudience.CoOp.Protocol;
using ModularAudience.CoOp.Security;
using ModularAudience.Audio.Vocal;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Maui.Storage;

namespace ModularAudience.VocalSynth.Android.Services;

internal sealed record CoOpIncomingBatch(
    Guid CollectionId,
    string Name,
    string OwnerName,
    IReadOnlyList<CoOpAudioSnapshot> Audios);

internal sealed record CoOpIncomingTransfer(IReadOnlyList<CoOpIncomingBatch> Collections);

internal sealed class CoOpAndroidSession : IAsyncDisposable
{
    private readonly JsonCoOpMessageSerializer serializer = new();
    private readonly Dictionary<Guid, PendingCollection> pendingCollections = [];
    private readonly Dictionary<Guid, (string Name, List<CoOpAudioSnapshot> Audios)> outgoingCollections = [];
    private readonly Dictionary<string, Guid> outgoingCollectionIds = new(StringComparer.Ordinal);
    private readonly Dictionary<CoOpConnection, List<CoOpIncomingBatch>> completedCollections = [];
    private readonly HashSet<CoOpConnection> batchCapablePeers = [];
    private readonly List<CoOpConnection> peerConnections = [];
    private readonly object gate = new();
    private CoOpServer? server;
    private X509Certificate2? hostCertificate;
    private CoOpDiscoveryResponder? discoveryResponder;
    private CoOpConnection? hostConnection;
    private TaskCompletionSource<WelcomeMessage>? pendingWelcome;
    private TaskCompletionSource<AuthChallengeMessage>? pendingAuthChallenge;
    private bool hostSupportsBatchTransfers;
    private readonly Guid localPeerId = Guid.NewGuid();
    private readonly Dictionary<CoOpConnection, AuthChallengeMessage> peerChallenges = [];
    private readonly HashSet<CoOpConnection> authenticatedPeers = [];

    internal string DisplayName { get; set; } = $"ModularAudience Android {Environment.MachineName}";
    internal string SessionPassword { get; set; } = string.Empty;
    internal string State { get; private set; } = "Not connected";
    internal bool IsHosting => this.server != null;
    internal bool IsConnected => this.hostConnection?.IsConnected == true;
    internal string? Address { get; private set; }
    internal int Port { get; private set; }

    internal Func<CoOpIncomingTransfer, Task<bool>>? ConfirmIncomingTransfer { get; set; }
    internal Func<string, int, string, Task<bool>>? ConfirmServerCertificate { get; set; }
    internal event EventHandler? StateChanged;
    internal event Action<string>? StatusChanged;
    internal int ConnectedPeerCount => this.hostConnection?.IsConnected == true
        ? 1
        : this.peerConnections.Count(peer => peer.IsConnected);
    internal string? HostCertificateFingerprint => this.hostCertificate == null
        ? null
        : CoOpSecurity.GetCertificateFingerprint(this.hostCertificate);

    internal async Task<IReadOnlyList<CoOpDiscoveredHost>> DiscoverAsync(CancellationToken cancellationToken)
    {
        return await new CoOpDiscoveryClient().DiscoverAsync(
            TimeSpan.FromMilliseconds(CoOpDefaults.DefaultDiscoveryTimeoutMilliseconds),
            cancellationToken);
    }

    internal async Task HostAsync(CancellationToken cancellationToken = default)
    {
        if (this.IsHosting || this.hostConnection?.IsConnected == true)
            throw new InvalidOperationException("Disconnect before starting a new co-op session.");
        if (!CoOpSecurity.IsValidSessionPassword(this.SessionPassword))
            throw new InvalidOperationException("Set a co-op session password between 8 and 128 characters before hosting.");

        string certificatePath = Path.Combine(FileSystem.AppDataDirectory, "CoOp", "host-certificate.pfx");
        X509Certificate2 certificate = CoOpSecurity.LoadOrCreateHostCertificate(certificatePath);
        var newServer = new CoOpServer(this.serializer, certificate);
        newServer.ClientConnected += this.Server_ClientConnected;
        newServer.ClientDisconnected += this.Server_ClientDisconnected;
        try
        {
            await newServer.StartAsync(
                CoOpDefaults.DefaultPort,
                CoOpDefaults.PortSearchCount,
                cancellationToken: cancellationToken);
        }
        catch
        {
            await newServer.DisposeAsync();
            certificate.Dispose();
            throw;
        }
        this.server = newServer;
        this.hostCertificate = certificate;
        this.Port = newServer.Port;
        this.Address = CoOpNetworkUtilities.GetPreferredLocalAddress();

        var responder = new CoOpDiscoveryResponder(this.localPeerId, this.DisplayName, this.Port);
        try
        {
            responder.Start();
            this.discoveryResponder = responder;
        }
        catch (Exception ex)
        {
            await responder.DisposeAsync();
            this.StatusChanged?.Invoke($"Hosting is active, but LAN discovery failed: {ex.Message}");
        }

        this.SetState($"Hosting on {this.Address}:{this.Port}");
    }

    internal async Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        if (this.IsHosting || this.hostConnection?.IsConnected == true)
            throw new InvalidOperationException("Disconnect before joining another co-op session.");
        if (string.IsNullOrWhiteSpace(host) || port is <= 0 or > 65535)
            throw new ArgumentException("Enter a valid host address and port.");
        if (!CoOpSecurity.IsValidSessionPassword(this.SessionPassword))
            throw new InvalidOperationException("Enter a co-op session password between 8 and 128 characters.");

        this.SetState("Connecting...");
        var authChallenge = new TaskCompletionSource<AuthChallengeMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        this.pendingAuthChallenge = authChallenge;
        CoOpConnection connection = await CoOpClient.ConnectAsync(
            host,
            port,
            this.serializer,
            TimeSpan.FromSeconds(8),
            cancellationToken);
        string fingerprint = connection.RemoteCertificateFingerprint
            ?? throw new IOException("The co-op host did not present a TLS certificate.");
        bool trusted = this.ConfirmServerCertificate != null
            && await this.ConfirmServerCertificate(host, port, fingerprint);
        if (!trusted)
        {
            await connection.DisposeAsync();
            this.pendingAuthChallenge = null;
            throw new CoOpCertificateTrustException(host, port, fingerprint);
        }
        this.hostConnection = connection;
        connection.MessageReceived += this.Connection_MessageReceived;
        connection.Closed += this.Connection_Closed;
        await connection.StartAsync(cancellationToken);
        var welcome = new TaskCompletionSource<WelcomeMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        this.pendingWelcome = welcome;
        AuthChallengeMessage challenge = await authChallenge.Task.WaitAsync(TimeSpan.FromSeconds(8), cancellationToken);
        await connection.SendAsync(
            new HelloMessage(
                this.localPeerId,
                this.DisplayName,
                SupportsBatchTransfers: true,
                PasswordProof: CoOpSecurity.CreatePasswordProof(this.SessionPassword, challenge, this.localPeerId)),
            cancellationToken);
        WelcomeMessage hostInfo = await welcome.Task.WaitAsync(TimeSpan.FromSeconds(8), cancellationToken);
        if (hostInfo.ProtocolVersion != CoOpDefaults.ProtocolVersion)
            throw new CoOpProtocolException($"The host uses unsupported co-op protocol version {hostInfo.ProtocolVersion}.");
        this.hostSupportsBatchTransfers = hostInfo.SupportsBatchTransfers;
        this.pendingWelcome = null;
        this.pendingAuthChallenge = null;
        this.SetState($"Connected to {hostInfo.DisplayName} ({host}:{port})");
        await this.SendOutgoingCollectionsAsync(connection, this.hostSupportsBatchTransfers, cancellationToken);
    }

    internal async Task ShareItemsAsync(
        IEnumerable<(SoundboardItem Item, VocalAudioData Audio)> selectedItems,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selectedItems);
        if (!this.IsHosting && this.hostConnection?.IsConnected != true)
            throw new InvalidOperationException("Start hosting or connect before sending soundboard samples.");
        List<(SoundboardItem Item, CoOpAudioSnapshot Snapshot)> snapshots = [];
        foreach ((SoundboardItem item, VocalAudioData audio) in selectedItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] data = MemoryMarshal.AsBytes(audio.Data.AsSpan()).ToArray();
            snapshots.Add((item, new CoOpAudioSnapshot
            {
                AudioId = item.Id,
                Name = item.Name,
                OriginalName = item.Name,
                SampleData = data,
                SampleRate = audio.SampleRate,
                Channels = audio.Channels,
                BitDepth = 32,
                Length = audio.Data.LongLength,
                Duration = audio.Duration,
            }));
        }

        foreach (IGrouping<string, (SoundboardItem Item, CoOpAudioSnapshot Snapshot)> group
            in snapshots.GroupBy(item => item.Item.GroupName, StringComparer.Ordinal))
        {
            if (!this.outgoingCollectionIds.TryGetValue(group.Key, out Guid collectionId))
            {
                collectionId = Guid.NewGuid();
                this.outgoingCollectionIds[group.Key] = collectionId;
            }
            List<CoOpAudioSnapshot> groupSnapshots = group.Select(item => item.Snapshot).ToList();
            if (this.outgoingCollections.TryGetValue(collectionId, out var existing))
            {
                Dictionary<Guid, CoOpAudioSnapshot> combined = existing.Audios.ToDictionary(audio => audio.AudioId);
                foreach (CoOpAudioSnapshot snapshot in groupSnapshots)
                    combined[snapshot.AudioId] = snapshot;
                groupSnapshots = combined.Values.ToList();
            }
            this.outgoingCollections[collectionId] = (group.Key, groupSnapshots);
            string collectionName = group.Key;
            await this.SendToSessionAsync(
                new ShareCollectionMessage(
                    collectionId,
                    collectionName,
                    this.localPeerId,
                    this.DisplayName,
                    ReplaceExisting: false),
                cancellationToken);
            foreach (CoOpAudioSnapshot snapshot in group.Select(item => item.Snapshot))
            {
                await this.SendToSessionAsync(new AudioAddedMessage(collectionId, snapshot), cancellationToken);
            }
            await this.SendToSessionAsync(new ShareCollectionCompletedMessage(collectionId), cancellationToken);
        }

        if (snapshots.Count > 0)
            await this.SendBatchCompletedAsync(Guid.NewGuid(), cancellationToken);
        this.StatusChanged?.Invoke($"Sent {snapshots.Count} sample(s) in {snapshots.Select(item => item.Item.GroupName).Distinct().Count()} collection group(s).");
    }

    internal async Task DisconnectAsync()
    {
        CoOpConnection? hostConnection = this.hostConnection;
        this.hostConnection = null;
        this.pendingWelcome?.TrySetCanceled();
        this.pendingWelcome = null;
        this.pendingAuthChallenge?.TrySetCanceled();
        this.pendingAuthChallenge = null;
        if (hostConnection != null)
            await hostConnection.DisposeAsync();

        CoOpDiscoveryResponder? responder = this.discoveryResponder;
        this.discoveryResponder = null;
        if (responder != null)
            await responder.DisposeAsync();

        CoOpServer? server = this.server;
        this.server = null;
        if (server != null)
            await server.DisposeAsync();
        this.hostCertificate?.Dispose();
        this.hostCertificate = null;

        lock (this.gate)
        {
            this.peerConnections.Clear();
            this.pendingCollections.Clear();
            this.completedCollections.Clear();
            this.batchCapablePeers.Clear();
            this.peerChallenges.Clear();
            this.authenticatedPeers.Clear();
        }
        this.hostSupportsBatchTransfers = false;
        this.Address = null;
        this.Port = 0;
        this.SetState("Not connected");
    }

    public async ValueTask DisposeAsync() => await this.DisconnectAsync();

    private void Server_ClientConnected(object? sender, CoOpServerEventArgs e)
    {
        lock (this.gate)
        {
            this.peerConnections.Add(e.Connection);
            this.peerChallenges[e.Connection] = CoOpSecurity.CreateChallenge();
        }
        e.Connection.MessageReceived += this.Connection_MessageReceived;
        e.Connection.Closed += this.Connection_Closed;
        this.StatusChanged?.Invoke($"Peer connected from {e.Connection.RemoteEndPoint}.");
        _ = this.SendChallengeAsync(e.Connection);
    }

    private async Task SendChallengeAsync(CoOpConnection connection)
    {
        AuthChallengeMessage challenge;
        lock (this.gate)
            challenge = this.peerChallenges[connection];
        try
        {
            await connection.SendAsync(challenge);
        }
        catch (Exception ex)
        {
            this.StatusChanged?.Invoke($"Authentication challenge failed: {ex.Message}");
        }
    }

    private void Server_ClientDisconnected(object? sender, CoOpServerEventArgs e)
    {
        lock (this.gate)
        {
            this.peerConnections.Remove(e.Connection);
            this.batchCapablePeers.Remove(e.Connection);
            this.completedCollections.Remove(e.Connection);
            this.peerChallenges.Remove(e.Connection);
            this.authenticatedPeers.Remove(e.Connection);
        }
        this.StatusChanged?.Invoke($"Peer disconnected: {e.Connection.RemoteEndPoint}.");
    }

    private void Connection_Closed(object? sender, CoOpConnectionClosedEventArgs e)
    {
        if (ReferenceEquals(sender, this.hostConnection))
        {
            this.hostConnection = null;
            this.SetState("Connection closed");
        }
        if (e.Error != null)
            this.StatusChanged?.Invoke($"Co-op connection error: {e.Error.Message}");
    }

    private void Connection_MessageReceived(object? sender, CoOpMessageReceivedEventArgs e)
    {
        if (sender is CoOpConnection connection)
            _ = this.HandleMessageAsync(connection, e.Message);
    }

    private async Task HandleMessageAsync(CoOpConnection connection, CoOpMessage message)
    {
        try
        {
            if (ReferenceEquals(connection, this.hostConnection)
                && message is AuthChallengeMessage authChallenge)
            {
                this.pendingAuthChallenge?.TrySetResult(authChallenge);
                return;
            }

            if (this.server != null && message is not HelloMessage)
            {
                lock (this.gate)
                {
                    if (!this.authenticatedPeers.Contains(connection))
                        return;
                }
            }

            switch (message)
            {
                case WelcomeMessage welcome:
                    this.hostSupportsBatchTransfers = welcome.SupportsBatchTransfers;
                    this.pendingWelcome?.TrySetResult(welcome);
                    break;
                case HelloMessage hello when this.server != null:
                    if (hello.ProtocolVersion != CoOpDefaults.ProtocolVersion)
                    {
                        await connection.SendAsync(new ErrorMessage("Unsupported co-op protocol version.", "protocol"));
                        return;
                    }
                    AuthChallengeMessage challenge;
                    lock (this.gate)
                    {
                        if (!this.peerChallenges.TryGetValue(connection, out challenge))
                            return;
                    }
                    if (!CoOpSecurity.VerifyPasswordProof(
                        this.SessionPassword,
                        challenge,
                        hello.PeerId,
                        hello.PasswordProof ?? []))
                    {
                        await connection.SendAsync(new ErrorMessage("The co-op session password is incorrect.", "authentication"));
                        await connection.DisposeAsync();
                        return;
                    }
                    lock (this.gate)
                        this.authenticatedPeers.Add(connection);
                    if (hello.SupportsBatchTransfers)
                    {
                        lock (this.gate)
                            this.batchCapablePeers.Add(connection);
                    }
                    await connection.SendAsync(new WelcomeMessage(
                        this.localPeerId,
                        this.DisplayName,
                        SupportsBatchTransfers: true));
                    await this.SendOutgoingCollectionsAsync(connection, hello.SupportsBatchTransfers, CancellationToken.None);
                    break;
                case ShareCollectionMessage share:
                    lock (this.gate)
                    {
                        this.pendingCollections[share.CollectionId] = new PendingCollection(
                            share.Name,
                            share.OwnerDisplayName,
                            []);
                    }
                    break;
                case AudioAddedMessage added:
                    lock (this.gate)
                    {
                        if (this.pendingCollections.TryGetValue(added.CollectionId, out PendingCollection? pending))
                            pending.Audios.Add(added.Audio);
                    }
                    break;
                case ShareCollectionCompletedMessage completed:
                    PendingCollection? completedCollection;
                    lock (this.gate)
                    {
                        this.pendingCollections.Remove(completed.CollectionId, out completedCollection);
                    }
                    if (completedCollection is { Audios.Count: > 0 })
                    {
                        var batch = new CoOpIncomingBatch(
                            completed.CollectionId,
                            completedCollection.Name,
                            completedCollection.OwnerName,
                            completedCollection.Audios.ToArray());
                        if (this.SupportsBatchTransfersFor(connection))
                        {
                            lock (this.gate)
                            {
                                if (!this.completedCollections.TryGetValue(connection, out List<CoOpIncomingBatch>? batches))
                                    this.completedCollections[connection] = batches = [];
                                batches.Add(batch);
                            }
                        }
                        else
                        {
                            await this.ConfirmAndForwardAsync([batch], connection);
                        }
                    }
                    break;
                case ShareBatchCompletedMessage batchCompleted:
                    List<CoOpIncomingBatch>? completedBatches;
                    lock (this.gate)
                    {
                        this.completedCollections.Remove(connection, out completedBatches);
                    }
                    if (completedBatches is { Count: > 0 })
                        await this.ConfirmAndForwardAsync(completedBatches, connection);
                    break;
                case ErrorMessage error:
                    this.StatusChanged?.Invoke($"Co-op error: {error.Message}");
                    break;
            }
        }
        catch (Exception ex)
        {
            this.StatusChanged?.Invoke($"Co-op transfer failed: {ex.Message}");
        }
    }

    private async Task ConfirmAndForwardAsync(
        IReadOnlyList<CoOpIncomingBatch> collections,
        CoOpConnection source)
    {
        bool accepted = this.ConfirmIncomingTransfer != null
            && await this.ConfirmIncomingTransfer(new CoOpIncomingTransfer(collections));
        if (!accepted)
        {
            this.StatusChanged?.Invoke($"Declined {collections.Sum(collection => collection.Audios.Count)} sample(s).");
            return;
        }

        foreach (CoOpIncomingBatch collection in collections)
        {
            await this.ForwardCollectionAsync(collection, source);
        }
        this.StatusChanged?.Invoke($"Received {collections.Sum(collection => collection.Audios.Count)} sample(s) from {collections.Count} collection(s).");
    }

    private async Task ForwardCollectionAsync(
        CoOpIncomingBatch collection,
        CoOpConnection source)
    {
        if (this.server == null)
            return;
        CoOpConnection[] peers;
        lock (this.gate)
            peers = this.peerConnections.Where(peer => !ReferenceEquals(peer, source) && peer.IsConnected).ToArray();
        if (peers.Length == 0)
            return;

        ShareCollectionMessage share = new(
            collection.CollectionId,
            collection.Name,
            this.localPeerId,
            this.DisplayName);
        foreach (CoOpConnection peer in peers)
        {
            await peer.SendAsync(share);
            foreach (CoOpAudioSnapshot audio in collection.Audios)
                await peer.SendAsync(new AudioAddedMessage(collection.CollectionId, audio));
            await peer.SendAsync(new ShareCollectionCompletedMessage(collection.CollectionId));
        }
    }

    private async Task SendOutgoingCollectionsAsync(
        CoOpConnection connection,
        bool supportsBatchTransfers,
        CancellationToken cancellationToken)
    {
        Guid batchId = Guid.NewGuid();
        foreach ((Guid collectionId, (string name, List<CoOpAudioSnapshot> audios)) in this.outgoingCollections)
        {
            await connection.SendAsync(new ShareCollectionMessage(
                collectionId, name, this.localPeerId, this.DisplayName), cancellationToken);
            foreach (CoOpAudioSnapshot audio in audios)
                await connection.SendAsync(new AudioAddedMessage(collectionId, audio), cancellationToken);
            await connection.SendAsync(new ShareCollectionCompletedMessage(collectionId), cancellationToken);
        }
        if (supportsBatchTransfers && this.outgoingCollections.Count > 0)
            await connection.SendAsync(new ShareBatchCompletedMessage(batchId), cancellationToken);
    }

    private bool SupportsBatchTransfersFor(CoOpConnection connection)
    {
        if (ReferenceEquals(connection, this.hostConnection))
            return this.hostSupportsBatchTransfers;
        lock (this.gate)
            return this.batchCapablePeers.Contains(connection);
    }

    private async Task SendBatchCompletedAsync(Guid batchId, CancellationToken cancellationToken)
    {
        if (this.hostConnection?.IsConnected == true)
        {
            if (this.hostSupportsBatchTransfers)
                await this.hostConnection.SendAsync(new ShareBatchCompletedMessage(batchId), cancellationToken);
            return;
        }

        CoOpConnection[] peers;
        lock (this.gate)
            peers = this.peerConnections.Where(peer => peer.IsConnected && this.batchCapablePeers.Contains(peer)).ToArray();
        foreach (CoOpConnection peer in peers)
            await peer.SendAsync(new ShareBatchCompletedMessage(batchId), cancellationToken);
    }

    private async Task SendToSessionAsync(CoOpMessage message, CancellationToken cancellationToken)
    {
        if (this.hostConnection?.IsConnected == true)
        {
            await this.hostConnection.SendAsync(message, cancellationToken);
            return;
        }

        CoOpConnection[] peers;
        lock (this.gate)
            peers = this.peerConnections.Where(peer => peer.IsConnected).ToArray();
        if (peers.Length == 0)
            throw new IOException("No connected co-op peers are available.");
        foreach (CoOpConnection peer in peers)
            await peer.SendAsync(message, cancellationToken);
    }

    private void SetState(string state)
    {
        this.State = state;
        this.StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class PendingCollection(
        string name,
        string ownerName,
        List<CoOpAudioSnapshot> audios)
    {
        internal string Name { get; } = name;
        internal string OwnerName { get; } = ownerName;
        internal List<CoOpAudioSnapshot> Audios { get; } = audios;
    }
}
