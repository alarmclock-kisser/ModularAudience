using ModularAudience.Audio;
using ModularAudience.CoOp;
using ModularAudience.CoOp.Networking;
using ModularAudience.CoOp.Protocol;
using ModularAudience.CoOp.Security;
using ModularAudience.CoOp.Synchronization;
using ModularAudience.CoOp.Windows;
using ModularAudience.Forms.Modules.CoOp;

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
        private readonly CoOpCertificateTrustStore certificateTrustStore;
        private readonly CoOpPasswordStore passwordStore;
        private readonly CoOpDiscoveryClient discoveryClient = new();
        private readonly Dictionary<Guid, AudioCollectionView> remoteViews = [];
        private readonly SemaphoreSlim presetImportGate = new(1, 1);
        private bool disposed;

        public CoOpUiController(SynchronizationContext uiContext)
        {
            ArgumentNullException.ThrowIfNull(uiContext);

            string coOpDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ModularAudience",
                "CoOp");
            this.certificateTrustStore = new CoOpCertificateTrustStore(
                Path.Combine(coOpDirectory, "trusted-hosts.json"));
            this.passwordStore = new CoOpPasswordStore(Path.Combine(coOpDirectory, "session-password.dat"));
            try
            {
                this.SessionPassword = this.passwordStore.Load();
            }
            catch (Exception ex)
            {
                LogManager.Log("Loading the saved co-op session password failed.", ex);
                MessageBox.Show(
                    "The saved co-op password could not be decrypted. You can enter a new password; it will replace the saved value.\n\n" +
                    ex.Message,
                    "Could not load co-op password",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                this.SessionPassword = string.Empty;
            }
            this.session = new CoOpSessionManager(new CoOpSessionOptions
            {
                CertificatePath = Path.Combine(coOpDirectory, "host-certificate.pfx"),
                ConfirmServerCertificate = this.ConfirmServerCertificate,
                ConfirmPasswordlessSession = this.ConfirmPasswordlessSession,
            }, uiContext);
            this.session.StateChanged += this.Session_StateChanged;
            this.session.SharesChanged += this.Session_SharesChanged;
            this.session.RemoteCollectionAdded += this.Session_RemoteCollectionAdded;
            this.session.RemoteCollectionRemoved += this.Session_RemoteCollectionRemoved;
            this.session.RemoteCollectionStateChanged += this.Session_RemoteCollectionStateChanged;
            this.session.AudioSyncStateChanged += this.Session_AudioSyncStateChanged;
            this.session.Log += this.Session_Log;
            this.session.PeerJoined += this.Session_PeerJoined;
            this.session.PeerLeft += this.Session_PeerLeft;
            this.session.PresetSyncReceived += this.Session_PresetSyncReceived;
        }

        public event EventHandler<CoOpStateChangedEventArgs>? StateChanged;

        public CoOpSessionState State => this.session.State;

        public bool IsSessionActive => this.session.IsSessionActive;

        public int ConnectedPeerCount => this.session.ConnectedPeerCount;

        public string? SessionAddress => this.session.SessionAddress;

        public int SessionPort => this.session.SessionPort;

        public string? HostCertificateFingerprint => this.session.HostCertificateFingerprint;

        public string SessionPassword { get; private set; } = string.Empty;

        public void RememberSessionPassword(string password)
        {
            this.passwordStore.Save(password);
            this.SessionPassword = password;
        }

        public async Task<CoOpHostInfo> StartHostingAsync(string password, CancellationToken cancellationToken = default)
        {
            this.RememberSessionPassword(password);
            this.session.SetSessionPassword(password);
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

        public async Task ConnectAsync(
            CoOpEndpoint endpoint,
            string password,
            CancellationToken cancellationToken = default)
        {
            this.RememberSessionPassword(password);
            this.session.SetSessionPassword(password);
            await this.session.ConnectAsync(endpoint.Host, endpoint.Port, cancellationToken).ConfigureAwait(true);
            WindowMain.RaiseCoOpVisualsChanged();
        }

        private bool ConfirmServerCertificate(string host, int port, string fingerprint)
        {
            if (this.certificateTrustStore.IsTrusted(host, port, fingerprint))
            {
                return true;
            }

            string message =
                $"Trust the TLS certificate for {host}:{port}?\n\n" +
                $"SHA-256 fingerprint:\n{fingerprint}\n\n" +
                "Verify this fingerprint with the host through a separate trusted channel before accepting.";
            DialogResult result = MessageBox.Show(
                message,
                "Confirm co-op host certificate",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (result != DialogResult.Yes)
            {
                return false;
            }

            this.certificateTrustStore.Trust(host, port, fingerprint);
            return true;
        }

        private bool ConfirmPasswordlessSession(string host, int port)
        {
            return MessageBox.Show(
                $"The host at {host}:{port} does not require a password and accepts connections only from its local subnet. " +
                "Only continue if you trust this host and are connected to the same LAN.\n\nConnect anyway?",
                "Confirm passwordless local session",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) == DialogResult.Yes;
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

        public Task PushPresetsAsync(
            IReadOnlyCollection<CoOpPresetSyncItem> presets,
            CancellationToken cancellationToken = default)
        {
            return this.session.PushPresetsAsync(presets, cancellationToken);
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
            WindowMain.RaiseCoOpVisualsChanged();
        }

        private void Session_PeerLeft(object? sender, CoOpPeerEventArgs e)
        {
            LogManager.Log($"Co-op peer left: {e.Peer.DisplayName}.");
            WindowMain.RaiseCoOpVisualsChanged();
        }

        private void Session_PresetSyncReceived(object? sender, CoOpPresetSyncEventArgs e)
        {
            _ = this.ImportPresetsAsync(e.Message);
        }

        private async Task ImportPresetsAsync(CoOpPresetSyncMessage message)
        {
            await this.presetImportGate.WaitAsync().ConfigureAwait(false);
            try
            {
                string sourceName = message.SourceDisplayName;
                LogManager.LogInfo(
                    $"Preset synchronization started: receiving {message.Presets.Length} preset(s) from '{sourceName}'.");

                try
                {
                    var synchronizer = new CoOpPresetSynchronizer();
                    CoOpPresetSyncResult result = await Task.Run(
                        () => synchronizer.Import(sourceName, message.Presets)).ConfigureAwait(false);
                    LogManager.LogSuccess(
                        $"Preset synchronization completed: {result.CreatedCount} new preset(s) created from '{sourceName}'; " +
                        $"{result.DuplicatesSkipped} duplicate(s) skipped.");
                }
                catch (Exception ex)
                {
                    LogManager.Log($"Preset synchronization from '{sourceName}' failed.", ex);
                }
            }
            finally
            {
                this.presetImportGate.Release();
            }
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
