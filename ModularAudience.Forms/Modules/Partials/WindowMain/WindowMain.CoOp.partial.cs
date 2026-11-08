using ModularAudience.Audio;
using ModularAudience.CoOp;
using ModularAudience.CoOp.Networking;
using ModularAudience.CoOp.Synchronization;
using ModularAudience.Forms.Helpers;
using System.ComponentModel;

namespace ModularAudience.Forms
{
    public partial class WindowMain
    {
        private CoOpUiController? _coOpController;
        private ContextMenuStrip? _coOpMenu;
        private ToolStripMenuItem? _coOpHostItem;
        private CoOpConnectionMenu? _coOpConnectionMenu;
        private ToolStripMenuItem? _coOpDisconnectItem;
        private ToolStripMenuItem? _coOpCopyAddressItem;
        private ToolStripMenuItem? _coOpStatusItem;
        private CancellationTokenSource? _coOpDiscoveryCts;
        private int _coOpDiscoveryGeneration;
        private bool _coOpMenuBusy;
        private bool _coOpShutdownStarted;
        private bool _coOpShutdownCompleted;
        private string _coOpDiscoveryStatus = "Not connected";
        private string _lastCoOpAddress = $"127.0.0.1:{CoOpDefaults.DefaultPort}";

        internal static CoOpUiController? CoOpController => Instance?._coOpController;

        internal static event EventHandler? CoOpVisualsChanged;

        internal static void RaiseCoOpVisualsChanged()
        {
            CoOpVisualsChanged?.Invoke(null, EventArgs.Empty);

            WindowMain? instance = Instance;
            if (instance == null || instance.IsDisposed || instance.Disposing)
            {
                return;
            }

            if (instance.InvokeRequired)
            {
                try
                {
                    instance.BeginInvoke((Action)instance.UpdateCoOpMenuState);
                }
                catch (InvalidOperationException)
                {
                    // The main window is closing.
                }

                return;
            }

            instance.UpdateCoOpMenuState();
        }

        private void InitializeCoOp()
        {
            SynchronizationContext uiContext = SynchronizationContext.Current
                ?? new WindowsFormsSynchronizationContext();

            this._coOpController = new CoOpUiController(uiContext);
            this._coOpController.StateChanged += this.CoOpController_StateChanged;
            this.InitializeCoOpMenu();
        }

        private void InitializeCoOpMenu()
        {
            this._coOpHostItem = new ToolStripMenuItem("Host co-op")
            {
                CheckOnClick = false
            };
            this._coOpHostItem.Click += async (_, _) => await this.ToggleHostCoOpAsync();

            this._coOpConnectionMenu = new CoOpConnectionMenu();
            this._coOpConnectionMenu.ConnectToAddressRequested += async (_, _) => await this.ConnectCoOpToAddressAsync();
            this._coOpConnectionMenu.HostSelected += async (_, e) =>
            {
                this._coOpMenu?.Close(ToolStripDropDownCloseReason.ItemClicked);
                await this.ConnectCoOpAsync(new CoOpEndpoint(e.Host.Address, e.Host.Port));
            };

            this._coOpDisconnectItem = new ToolStripMenuItem("Disconnect");
            this._coOpDisconnectItem.Click += async (_, _) => await this.DisconnectCoOpAsync();

            this._coOpCopyAddressItem = new ToolStripMenuItem("Copy co-op address");
            this._coOpCopyAddressItem.Click += (_, _) => this.CopyCoOpAddress();

            this._coOpStatusItem = new ToolStripMenuItem("Not connected")
            {
                Enabled = false
            };

            var menu = new ContextMenuStrip();
            menu.Items.AddRange(
            [
                this._coOpHostItem,
                this._coOpConnectionMenu.ConnectToAddress,
                this._coOpConnectionMenu.Hosts,
                this._coOpDisconnectItem,
                this._coOpCopyAddressItem,
                new ToolStripSeparator(),
                this._coOpStatusItem
            ]);
            menu.Opening += this.CoOpMenu_Opening;
            menu.Closed += (_, _) => this.CancelCoOpDiscovery();

            this._coOpMenu = menu;
            this.button_import.ContextMenuStrip = menu;
            this.UpdateCoOpMenuState();
        }

        private async void CoOpMenu_Opening(object? sender, CancelEventArgs e)
        {
            this.UpdateCoOpMenuState();
            if (this._coOpController?.IsSessionActive == true)
            {
                return;
            }

            await this.RefreshDiscoveredCoOpHostsAsync();
        }

        private void UpdateCoOpMenuState()
        {
            if (this.IsDisposed || this.Disposing)
            {
                return;
            }

            if (this.InvokeRequired)
            {
                try
                {
                    this.BeginInvoke((Action)this.UpdateCoOpMenuState);
                }
                catch (InvalidOperationException)
                {
                    // The main window is closing.
                }

                return;
            }

            CoOpUiController? controller = this._coOpController;
            bool hosting = controller?.State == CoOpSessionState.Hosting;
            bool connected = controller?.State == CoOpSessionState.Connected;
            bool active = hosting || connected;

            if (this._coOpHostItem != null)
            {
                this._coOpHostItem.Checked = hosting;
                this._coOpHostItem.Enabled = !connected && !this._coOpMenuBusy;
            }

            this._coOpConnectionMenu?.UpdateState(controller?.State ?? CoOpSessionState.Disconnected, this._coOpMenuBusy);

            if (this._coOpDisconnectItem != null)
            {
                this._coOpDisconnectItem.Enabled = active && !this._coOpMenuBusy;
            }

            if (this._coOpCopyAddressItem != null)
            {
                this._coOpCopyAddressItem.Enabled = hosting && !this._coOpMenuBusy;
            }

            if (this._coOpStatusItem != null)
            {
                if (hosting)
                {
                    this._coOpStatusItem.Text = $"Hosting on {controller!.SessionAddress}:{controller.SessionPort}";
                }
                else if (connected)
                {
                    this._coOpStatusItem.Text = $"Connected to {controller!.SessionAddress}";
                }
                else
                {
                    this._coOpStatusItem.Text = this._coOpDiscoveryStatus;
                }
            }
        }

        private async Task RefreshDiscoveredCoOpHostsAsync()
        {
            CoOpUiController? controller = this._coOpController;
            if (controller == null || controller.IsSessionActive)
            {
                return;
            }

            var cancellation = new CancellationTokenSource();
            CancellationTokenSource? previous = Interlocked.Exchange(ref this._coOpDiscoveryCts, cancellation);
            previous?.Cancel();
            previous?.Dispose();

            int generation = ++this._coOpDiscoveryGeneration;

            this._coOpConnectionMenu?.SetHosts([]);

            this._coOpDiscoveryStatus = "Scanning local network...";
            this.UpdateCoOpMenuState();

            try
            {
                IReadOnlyList<CoOpDiscoveredHost> hosts = await controller.DiscoverAsync(cancellation.Token);
                if (generation != this._coOpDiscoveryGeneration)
                {
                    return;
                }

                this._coOpConnectionMenu?.SetHosts(hosts);

                this._coOpDiscoveryStatus = hosts.Count > 0
                    ? $"{hosts.Count} co-op host(s) found"
                    : "No co-op hosts found";
            }
            catch (OperationCanceledException)
            {
                // The menu was closed or reopened; a newer scan owns the result.
            }
            catch (Exception ex)
            {
                if (generation == this._coOpDiscoveryGeneration)
                {
                    this._coOpDiscoveryStatus = "Co-op discovery failed";
                    LogManager.Log("Co-op discovery failed.", ex);
                }
            }
            finally
            {
                if (generation == this._coOpDiscoveryGeneration)
                {
                    this.UpdateCoOpMenuState();
                }
            }
        }

        private void CancelCoOpDiscovery()
        {
            this._coOpDiscoveryGeneration++;
            CancellationTokenSource? cancellation = Interlocked.Exchange(ref this._coOpDiscoveryCts, null);
            cancellation?.Cancel();
            cancellation?.Dispose();
        }

        private async Task ToggleHostCoOpAsync()
        {
            CoOpUiController? controller = this._coOpController;
            if (controller == null || this._coOpMenuBusy || controller.State == CoOpSessionState.Connected)
            {
                return;
            }

            this._coOpMenuBusy = true;
            this.UpdateCoOpMenuState();

            try
            {
                if (controller.State == CoOpSessionState.Hosting)
                {
                    await controller.StopAsync();
                    LogManager.Log("Co-op hosting stopped.");
                }
                else
                {
                    CoOpHostInfo info = await controller.StartHostingAsync();
                    string address = $"{info.Address}:{info.Port}";
                    this._lastCoOpAddress = address;
                    LogManager.LogSuccess($"Co-op hosting started. Address: {address}");
                    MessageBox.Show(
                        this,
                        $"Co-op hosting started.\n\nShare this address with the other peers:\n{address}",
                        "Co-op hosting",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                LogManager.Log("Starting co-op hosting failed.", ex);
                WindowMainStaticHelpers.ShowErrorWithCopyButton(this, "Co-op hosting failed", ex);
            }
            finally
            {
                this._coOpMenuBusy = false;
                this.UpdateCoOpMenuState();
                RaiseCoOpVisualsChanged();
            }
        }

        private async Task ConnectCoOpToAddressAsync()
        {
            CoOpUiController? controller = this._coOpController;
            if (controller == null || this._coOpMenuBusy || controller.IsSessionActive || this._coOpShutdownStarted)
            {
                return;
            }

            string input = Microsoft.VisualBasic.Interaction.InputBox(
                "Enter the co-op host address (for example 192.168.1.20:47823). Use its local IP address when both computers are on the same LAN or hotspot:",
                "Connect co-op",
                this._lastCoOpAddress);

            if (string.IsNullOrWhiteSpace(input))
            {
                return;
            }

            if (!CoOpEndpoint.TryParse(input, out CoOpEndpoint endpoint))
            {
                MessageBox.Show(
                    this,
                    $"'{input}' is not a valid co-op address. Use host:port.",
                    "Connect co-op",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            await this.ConnectCoOpAsync(endpoint);
        }

        private async Task ConnectCoOpAsync(CoOpEndpoint endpoint)
        {
            CoOpUiController? controller = this._coOpController;
            if (controller == null || this._coOpMenuBusy || controller.IsSessionActive || this._coOpShutdownStarted)
            {
                return;
            }

            this._coOpMenuBusy = true;
            this.UpdateCoOpMenuState();

            try
            {
                await controller.ConnectAsync(endpoint);
                this._lastCoOpAddress = endpoint.ToString();
                LogManager.LogSuccess($"Connected to co-op host {endpoint}.");
            }
            catch (Exception ex)
            {
                LogManager.Log($"Connecting to co-op host {endpoint} failed.", ex);
                if (!this._coOpShutdownStarted)
                {
                    WindowMainStaticHelpers.ShowErrorWithCopyButton(this, "Co-op connection failed", ex);
                    if (this._coOpConnectionMenu != null)
                    {
                        this._coOpConnectionMenu.Hosts.SelectedIndex = -1;
                    }
                }
            }
            finally
            {
                this._coOpMenuBusy = false;
                this.UpdateCoOpMenuState();
                RaiseCoOpVisualsChanged();
            }
        }

        private async Task DisconnectCoOpAsync()
        {
            CoOpUiController? controller = this._coOpController;
            if (controller == null || this._coOpMenuBusy || !controller.IsSessionActive)
            {
                return;
            }

            this._coOpMenuBusy = true;
            this.UpdateCoOpMenuState();

            try
            {
                await controller.StopAsync();
                LogManager.Log("Co-op session disconnected.");
            }
            catch (Exception ex)
            {
                LogManager.Log("Disconnecting the co-op session failed.", ex);
                WindowMainStaticHelpers.ShowErrorWithCopyButton(this, "Co-op disconnect failed", ex);
            }
            finally
            {
                this._coOpMenuBusy = false;
                this.UpdateCoOpMenuState();
                RaiseCoOpVisualsChanged();
            }
        }

        private void CopyCoOpAddress()
        {
            CoOpUiController? controller = this._coOpController;
            if (controller?.State != CoOpSessionState.Hosting)
            {
                return;
            }

            string address = $"{controller.SessionAddress}:{controller.SessionPort}";
            try
            {
                Clipboard.SetText(address);
                LogManager.Log($"Co-op address copied: {address}");
            }
            catch (Exception ex)
            {
                LogManager.Log($"Copying the co-op address failed: {ex.Message}");
            }
        }

        private void CoOpController_StateChanged(object? sender, CoOpStateChangedEventArgs e)
        {
            if (this.IsDisposed || this.Disposing)
            {
                return;
            }

            if (this.InvokeRequired)
            {
                try
                {
                    this.BeginInvoke((Action)(() => this.CoOpController_StateChanged(sender, e)));
                }
                catch (InvalidOperationException)
                {
                    // The main window is closing.
                }

                return;
            }

            if (!string.IsNullOrWhiteSpace(e.Message))
            {
                LogManager.Log($"Co-op: {e.Message}");
            }

            this.UpdateCoOpMenuState();
            RaiseCoOpVisualsChanged();
        }

        private async Task ShutdownCoOpAsync()
        {
            this.CancelCoOpDiscovery();

            CoOpUiController? controller = this._coOpController;
            this._coOpController = null;
            if (controller == null)
            {
                return;
            }

            controller.StateChanged -= this.CoOpController_StateChanged;

            try
            {
                Task disposeTask = controller.DisposeAsync().AsTask();
                Task completed = await Task.WhenAny(disposeTask, Task.Delay(TimeSpan.FromSeconds(3)));
                if (ReferenceEquals(completed, disposeTask))
                {
                    await disposeTask;
                }
            }
            catch (Exception ex)
            {
                LogManager.Log("Co-op shutdown failed.", ex);
            }
        }
    }
}
