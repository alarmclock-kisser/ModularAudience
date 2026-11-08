using ModularAudience.Audio;
using ModularAudience.CoOp;
using ModularAudience.CoOp.Networking;
using ModularAudience.CoOp.Synchronization;
using ModularAudience.CoOp.Windows;

namespace ModularAudience.Forms
{
    /// <summary>
    /// Bridges the co-op session manager to the WinForms UI. The session manager
    /// owns all protocol state; this controller only opens collection windows and
    /// forwards user actions.
    /// </summary>
    internal sealed class CoOpUiController : IAsyncDisposable
    {
        private readonly CoOpSessionManager session;
        private readonly CoOpDiscoveryClient discoveryClient = new();
        private readonly Dictionary<Guid, AudioCollectionView> remoteViews = [];
        private bool disposed;

        public CoOpUiController(SynchronizationContext uiContext)
        {
            ArgumentNullException.ThrowIfNull(uiContext);

            this.session = new CoOpSessionManager(new CoOpSessionOptions(), uiContext);
            this.session.StateChanged += this.Session_StateChanged;
            this.session.SharesChanged += this.Session_SharesChanged;
            this.session.RemoteCollectionAdded += this.Session_RemoteCollectionAdded;
            this.session.RemoteCollectionRemoved += this.Session_RemoteCollectionRemoved;
            this.session.RemoteCollectionStateChanged += this.Session_RemoteCollectionStateChanged;
            this.session.AudioSyncStateChanged += this.Session_AudioSyncStateChanged;
            this.session.Log += this.Session_Log;
            this.session.PeerJoined += this.Session_PeerJoined;
            this.session.PeerLeft += this.Session_PeerLeft;
        }

        public event EventHandler<CoOpStateChangedEventArgs>? StateChanged;

        public CoOpSessionState State => this.session.State;

        public bool IsSessionActive => this.session.IsSessionActive;

        public string? SessionAddress => this.session.SessionAddress;

        public int SessionPort => this.session.SessionPort;

        public async Task<CoOpHostInfo> StartHostingAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                CoOpFirewallResult firewall = await WindowsFirewall
                    .EnsureCoOpRulesAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(true);

                if (firewall.Success)
                {
                    LogManager.LogInfo($"Co-op firewall: {firewall.Message}");
                }
                else
                {
                    LogManager.LogWarning($"Co-op firewall: {firewall.Message}");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Hosting can still work when the network already allows the ports.
                LogManager.Log("Co-op firewall preparation failed.", ex);
            }

            CoOpHostInfo info = await this.session.StartHostingAsync(cancellationToken).ConfigureAwait(true);
            WindowMain.RaiseCoOpVisualsChanged();
            return info;
        }

        public async Task ConnectAsync(CoOpEndpoint endpoint, CancellationToken cancellationToken = default)
        {
            await this.session.ConnectAsync(endpoint.Host, endpoint.Port, cancellationToken).ConfigureAwait(true);
            WindowMain.RaiseCoOpVisualsChanged();
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            await this.session.StopAsync(cancellationToken).ConfigureAwait(true);
            WindowMain.RaiseCoOpVisualsChanged();
        }

        public Task<IReadOnlyList<CoOpDiscoveredHost>> DiscoverAsync(CancellationToken cancellationToken = default)
        {
            return this.discoveryClient.DiscoverAsync(
                TimeSpan.FromMilliseconds(CoOpDefaults.DefaultDiscoveryTimeoutMilliseconds),
                cancellationToken);
        }

        public CoOpLocalShare? FindLocalShare(AudioCollection collection)
        {
            return this.session.FindLocalShare(collection);
        }

        public CoOpRemoteCollection? FindRemoteCollection(AudioCollection collection)
        {
            return this.session.RemoteCollections.FirstOrDefault(
                collectionCandidate => ReferenceEquals(collectionCandidate.Collection, collection));
        }

        public bool IsAudioDeleted(AudioCollection collection, Guid audioId)
        {
            return this.FindRemoteCollection(collection)?.IsDeleted(audioId) == true;
        }

        public bool IsAudioPending(AudioCollection collection, Guid audioId)
        {
            CoOpLocalShare? share = this.session.FindLocalShare(collection);
            if (share != null)
            {
                return share.IsAudioPending(audioId);
            }

            return this.FindRemoteCollection(collection)?.IsAudioPending(audioId) == true;
        }

        public async Task ShareCollectionAsync(
            AudioCollection collection,
            string? name,
            CancellationToken cancellationToken = default)
        {
            await this.session.ShareCollectionAsync(collection, name, cancellationToken).ConfigureAwait(true);
            WindowMain.RaiseCoOpVisualsChanged();
        }

        public async Task UnshareCollectionAsync(Guid collectionId, CancellationToken cancellationToken = default)
        {
            await this.session.UnshareCollectionAsync(collectionId, cancellationToken).ConfigureAwait(true);
            WindowMain.RaiseCoOpVisualsChanged();
        }

        public async Task ToggleShareAsync(
            AudioCollection collection,
            string? name,
            CancellationToken cancellationToken = default)
        {
            CoOpLocalShare? existing = this.session.FindLocalShare(collection);
            if (existing != null)
            {
                await this.UnshareCollectionAsync(existing.CollectionId, cancellationToken).ConfigureAwait(true);
                return;
            }

            await this.ShareCollectionAsync(collection, name, cancellationToken).ConfigureAwait(true);
        }

        public async ValueTask DisposeAsync()
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;
            this.CloseAllRemoteViews();

            try
            {
                await this.session.DisposeAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                LogManager.Log("Stopping the co-op session during shutdown failed.", ex);
            }
        }

        private void Session_StateChanged(object? sender, CoOpStateChangedEventArgs e)
        {
            this.StateChanged?.Invoke(this, e);
            WindowMain.RaiseCoOpVisualsChanged();
        }

        private void Session_SharesChanged(object? sender, EventArgs e)
        {
            WindowMain.RaiseCoOpVisualsChanged();
        }

        private void Session_RemoteCollectionAdded(object? sender, CoOpRemoteCollectionEventArgs e)
        {
            this.OpenRemoteCollectionView(e.Collection);
            WindowMain.RaiseCoOpVisualsChanged();
        }

        private void Session_RemoteCollectionRemoved(object? sender, CoOpRemoteCollectionRemovedEventArgs e)
        {
            this.CloseRemoteCollectionView(e.CollectionId);
            WindowMain.RaiseCoOpVisualsChanged();
        }

        private void Session_RemoteCollectionStateChanged(object? sender, CoOpRemoteCollectionEventArgs e)
        {
            WindowMain.RaiseCoOpVisualsChanged();
        }

        private void Session_AudioSyncStateChanged(object? sender, CoOpAudioSyncStateEventArgs e)
        {
            WindowMain.RaiseCoOpVisualsChanged();
        }

        private void Session_Log(object? sender, CoOpLogEventArgs e)
        {
            if (e.Exception != null)
            {
                LogManager.Log(e.Message, e.Exception);
            }
            else
            {
                LogManager.Log(e.Message);
            }
        }

        private void Session_PeerJoined(object? sender, CoOpPeerEventArgs e)
        {
            LogManager.Log($"Co-op peer joined: {e.Peer.DisplayName}.");
        }

        private void Session_PeerLeft(object? sender, CoOpPeerEventArgs e)
        {
            LogManager.Log($"Co-op peer left: {e.Peer.DisplayName}.");
        }

        private void OpenRemoteCollectionView(CoOpRemoteCollection collection)
        {
            if (this.disposed)
            {
                return;
            }

            if (this.remoteViews.TryGetValue(collection.CollectionId, out AudioCollectionView? existing) &&
                existing != null &&
                !existing.IsDisposed)
            {
                if (!existing.Visible)
                {
                    existing.Show();
                }

                existing.Activate();
                return;
            }

            string title = string.IsNullOrWhiteSpace(collection.OwnerDisplayName)
                ? collection.Name
                : $"{collection.Name} - {collection.OwnerDisplayName}";

            var view = new AudioCollectionView(collection.Collection, title);
            this.remoteViews[collection.CollectionId] = view;
            view.FormClosed += (_, _) => this.remoteViews.Remove(collection.CollectionId);
        }

        private void CloseRemoteCollectionView(Guid collectionId)
        {
            if (!this.remoteViews.Remove(collectionId, out AudioCollectionView? view))
            {
                return;
            }

            try
            {
                if (!view.IsDisposed)
                {
                    view.Close();
                }
            }
            catch
            {
                // A window that is already closing needs no further action.
            }
        }

        private void CloseAllRemoteViews()
        {
            foreach (AudioCollectionView view in this.remoteViews.Values.ToList())
            {
                try
                {
                    if (!view.IsDisposed)
                    {
                        view.Close();
                    }
                }
                catch
                {
                    // Best effort during shutdown.
                }
            }

            this.remoteViews.Clear();
        }
    }
}
