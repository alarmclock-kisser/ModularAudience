using ModularAudience.Audio;
using ModularAudience.CoOp;
using ModularAudience.CoOp.Networking;
using ModularAudience.CoOp.Protocol;
using ModularAudience.CoOp.Security;
using ModularAudience.CoOp.Synchronization;
using ModularAudience.Forms.Helpers;
using ModularAudience.Forms.Modules.Dialogs;
using ModularAudience.Video;
using System.ComponentModel;
using System.Text;

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
        private ToolStripMenuItem? _coOpSynchronizePresetsItem;
        private ToolStripMenuItem? _coOpStatusItem;
        private ToolStripMenuItem? _coOpImportVideoItem;
        private CancellationTokenSource? _coOpDiscoveryCts;
        private int _coOpDiscoveryGeneration;
        private bool _coOpMenuBusy;
        private bool _coOpPresetSyncBusy;
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

            this._coOpSynchronizePresetsItem = new ToolStripMenuItem("Synchronize Presets")
            {
                Enabled = false
            };
            this._coOpSynchronizePresetsItem.Click += async (_, _) => await this.SynchronizePresetsAsync();

            this._coOpStatusItem = new ToolStripMenuItem("Not connected")
            {
                Enabled = false
            };

            this._coOpImportVideoItem = new ToolStripMenuItem("Import + convert Video")
            {
                Name = "importVideo"
            };
            this._coOpImportVideoItem.Click += async (_, _) => await this.ImportVideoFilesAsync();

            var menu = new ContextMenuStrip();
            menu.Items.AddRange(
            [
                ModularAudience.Forms.Modules.Dialogs.MixProjectMenu.CreateItem(),
                ModularAudience.Forms.Modules.Dialogs.MixProjectMenu.CreateNewItem(),
                new ToolStripSeparator(),
                this._coOpHostItem,
                this._coOpConnectionMenu.ConnectToAddress,
                this._coOpConnectionMenu.Hosts,
                new ToolStripSeparator(),
                this._coOpSynchronizePresetsItem,
                this._coOpDisconnectItem,
                this._coOpCopyAddressItem,
                new ToolStripSeparator(),
                this._coOpStatusItem,
                new ToolStripSeparator(),
                this._coOpImportVideoItem,
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

            if (this._coOpSynchronizePresetsItem != null)
            {
                this._coOpSynchronizePresetsItem.Enabled =
                    controller != null &&
                    controller.ConnectedPeerCount > 0 &&
                    !this._coOpMenuBusy &&
                    !this._coOpPresetSyncBusy;
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

        private async Task SynchronizePresetsAsync()
        {
            CoOpUiController? controller = this._coOpController;
            if (controller == null ||
                this._coOpMenuBusy ||
                this._coOpPresetSyncBusy ||
                this._coOpShutdownStarted ||
                controller.ConnectedPeerCount == 0)
            {
                return;
            }

            this._coOpPresetSyncBusy = true;
            this.UpdateCoOpMenuState();

            try
            {
                IReadOnlyList<CoOpPresetSyncItem> presets = await Task.Run(
                    () => new CoOpPresetSynchronizer().Capture());
                if (this._coOpShutdownStarted)
                {
                    return;
                }

                if (presets.Count == 0)
                {
                    LogManager.LogInfo("Preset synchronization skipped: no user-created presets were found.");
                    return;
                }

                int peerCount = controller.ConnectedPeerCount;
                LogManager.LogInfo(
                    $"Preset synchronization started: sending {presets.Count} preset(s) to {peerCount} co-op peer(s).");
                await controller.PushPresetsAsync(presets);
                LogManager.LogSuccess(
                    $"Preset synchronization sent {presets.Count} preset(s) to {peerCount} co-op peer(s).");
            }
            catch (Exception ex)
            {
                LogManager.Log("Preset synchronization failed.", ex);
                if (!this._coOpShutdownStarted)
                {
                    WindowMainStaticHelpers.ShowErrorWithCopyButton(this, "Preset synchronization failed", ex);
                }
            }
            finally
            {
                this._coOpPresetSyncBusy = false;
                this.UpdateCoOpMenuState();
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
                    : "No co-op hosts found; enter the host's local IP and port manually if needed";
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
                    using var passwordDialog = new ModularAudience.Forms.Modules.Dialogs.CoOpPasswordDialog(
                        "Set a co-op session password",
                        "Use 8–128 characters, or leave this blank to allow passwordless connections from the local subnet only.",
                        controller.SessionPassword);
                    DialogResult passwordResult = passwordDialog.ShowDialog(this);
                    string password = passwordDialog.Password;
                    controller.RememberSessionPassword(password);
                    if (passwordResult != DialogResult.OK)
                    {
                        return;
                    }

                    if (!string.IsNullOrEmpty(password) && !CoOpSecurity.IsValidSessionPassword(password))
                    {
                        MessageBox.Show(
                            this,
                            "A session password must be 8–128 characters. Clear the field for a local-network-only session.",
                            "Session password is too short",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                        return;
                    }

                    if (string.IsNullOrEmpty(password) &&
                        MessageBox.Show(
                            this,
                            "No password will be required. The host will accept connections only from its local subnet. " +
                            "Devices on other networks must use a password-protected session.\n\nHost without a password?",
                            "Confirm passwordless local hosting",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Warning,
                            MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    {
                        return;
                    }

                    CoOpHostInfo info = await controller.StartHostingAsync(password);
                    string address = $"{info.Address}:{info.Port}";
                    string passwordSummary = string.IsNullOrEmpty(password)
                        ? "None (local network only)"
                        : password;
                    this._lastCoOpAddress = address;
                    LogManager.LogSuccess($"Co-op hosting started. Address: {address}");
                    MessageBox.Show(
                        this,
                        $"Co-op hosting started.\n\nShare this address and session password with trusted peers:\n{address}\n\nSession password:\n{passwordSummary}\n\nHost certificate SHA-256 fingerprint:\n{controller.HostCertificateFingerprint}",
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
                using var passwordDialog = new ModularAudience.Forms.Modules.Dialogs.CoOpPasswordDialog(
                    "Enter the co-op session password",
                    $"Enter the password supplied by the host at {endpoint}. Leave blank only for a passwordless host on your local network.",
                    controller.SessionPassword);
                DialogResult passwordResult = passwordDialog.ShowDialog(this);
                string password = passwordDialog.Password;
                controller.RememberSessionPassword(password);
                if (passwordResult != DialogResult.OK)
                {
                    return;
                }

                if (!string.IsNullOrEmpty(password) && !CoOpSecurity.IsValidSessionPassword(password))
                {
                    MessageBox.Show(
                        this,
                        "A session password must be 8–128 characters. Clear the field only for a passwordless host on your local network.",
                        "Session password is too short",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                await controller.ConnectAsync(endpoint, password);
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

        private async Task ImportVideoFilesAsync()
        {
            string initialDir = this.lastVideoFolder;
            if (string.IsNullOrWhiteSpace(initialDir) || !Directory.Exists(initialDir))
            {
                initialDir = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            }

            using OpenFileDialog openFileDialog = new()
            {
                InitialDirectory = initialDir,
                Filter = VideoFormat.Filter,
                Multiselect = true,
                Title = "Video zu Audio konvertieren und importieren",
                RestoreDirectory = true,
            };

            if (openFileDialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            var videoFiles = openFileDialog.FileNames
                .Where(f => VideoFormat.IsSupported(f))
                .ToList();

            if (videoFiles.Count == 0)
            {
                LogManager.Log("Video import: Keine unterstützten Video-Dateien ausgewählt.");
                return;
            }

            // Remember the folder for next time.
            try
            {
                this.lastVideoFolder = Path.GetDirectoryName(videoFiles.First()) ?? this.lastVideoFolder;
            }
            catch { }

            // 1. FFmpeg sicherstellen (wiederverwende Ytdlp-Installation, falls vorhanden;
            //    sonst interaktiver Download mit Progress-Dialog)
            string? ffmpegPath = await this.EnsureFfmpegAvailableAsync();
            if (ffmpegPath == null)
            {
                LogManager.Log("Video import: ffmpeg nicht verfügbar, Abbruch.");
                return;
            }

            // 2. Konvertierung: temporäres WAV im Temp-Verzeichnis (nicht als Endprodukt speichern)
            string tempVideoDir = Path.Combine(Path.GetTempPath(), $"ModularAudienceVideoImport_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempVideoDir);

            var options = new VideoConverterOptions
            {
                MaxWorkers = Math.Max(1, Environment.ProcessorCount - 2),
                OutputFormat = AudioOutputFormat.Wav,
                OutputDirectory = tempVideoDir,
                FfmpegPath = ffmpegPath,
            };

            using var converter = new FfmpegVideoToAudioConverter(options);

            var progress = new Progress<double>(pct =>
            {
                // ProgressDialog marshaled selbst auf den UI-Thread.
            });

            var cts = new CancellationTokenSource();

            using var progressDlg = new ProgressDialog(
                title: "Video zu Audio konvertieren...",
                progress: progress,
                windowCloseDelay: 1.0d,
                ct: cts.Token);

            progressDlg.Show(this);

            List<string> tempAudioFiles = [];

            try
            {
                // 4. Konvertiere alle Videos parallel (MaxWorkers), Fehler stoppen nicht den Rest
                var results = await converter.ConvertManyAsync(videoFiles, progress, cts.Token)
                    .ConfigureAwait(false);

                progressDlg.Complete();

                // Erfolgreiche Konvertierungen: Temp-WAV-Pfade sammeln für AudioObj-Import
                var successResults = results
                    .Where(r => r.Success && !string.IsNullOrWhiteSpace(r.AudioFilePath) && File.Exists(r.AudioFilePath!))
                    .ToList();

                foreach (var r in successResults)
                {
                    tempAudioFiles.Add(r.AudioFilePath!);
                }

                if (tempAudioFiles.Count > 0)
                {
                    LogManager.Log($"Video import: {tempAudioFiles.Count} von {videoFiles.Count} Video(n) zu Audio konvertiert.");

                    // 5. Als AudioObj importieren (bestehende Import-Pipeline erstellt AudioObj aus den Dateien)
                    await this.ImportAndPlaceAsync(tempAudioFiles, fromResources: false);

                    // Temp-WAV-Dateien nach erfolgreichem Import löschen (AudioObj hält die Daten im RAM)
                    foreach (var tempPath in tempAudioFiles)
                    {
                        try { File.Delete(tempPath); } catch { }
                    }
                }

                // 6. Fehler am Ende gebündelt anzeigen (nicht währenddessen abbrechen)
                var failed = results.Where(r => !r.Success).ToList();
                if (failed.Count > 0)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine($"Folgende {failed.Count} Video(n) konnten nicht konvertiert werden:");
                    sb.AppendLine();
                    foreach (var r in failed)
                    {
                        sb.AppendLine($"• {Path.GetFileName(r.VideoFilePath)}");
                        sb.AppendLine($"    {r.ErrorMessage}");
                        sb.AppendLine();
                    }

                    ShowErrorWithCopyButton(
                        this,
                        "Video-zu-Audio Konvertierung – Fehler aufgetreten",
                        new Exception(sb.ToString()));
                }
                else if (tempAudioFiles.Count == 0)
                {
                    LogManager.Log("Video import: Keine Audio-Dateien konnten extrahiert werden.");
                }
            }
            catch (OperationCanceledException)
            {
                progressDlg.Close();
                LogManager.Log("Video import wurde abgebrochen.");
            }
            catch (Exception ex)
            {
                progressDlg.Close();
                LogManager.Log($"Video import fehlgeschlagen: {ex.Message}");
                ShowErrorWithCopyButton(this, "Video-zu-Audio Konvertierung fehlgeschlagen", ex);
            }
            finally
            {
                // Temp-Verzeichnis aufräumen (best effort)
                try
                {
                    if (Directory.Exists(tempVideoDir))
                    {
                        Directory.Delete(tempVideoDir, recursive: true);
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// Stellt sicher, dass ffmpeg.exe verfügbar ist. Wiederverwendet die von
        /// <see cref="FfmpegBinaryManager"/> / Ytdlp verwaltete Installation, sofern vorhanden.
        /// Falls nicht, wird interaktiv zum Download angeboten (nur ffmpeg, kein yt-dlp/deno).
        /// Gibt den Pfad zu ffmpeg.exe zurück oder null, wenn der Nutzer ablehnt.
        /// </summary>
        private async Task<string?> EnsureFfmpegAvailableAsync()
        {
            // 1. Bereits bekannte Standorte prüfen (ohne Update-Suche, ohne Download)
            string? knownPath = null;

            try
            {
                var ytdlpDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ModularAudience", "binaries_ytdlp");
                foreach (var dir in Directory.EnumerateDirectories(ytdlpDir, "ffmpeg-*", SearchOption.TopDirectoryOnly))
                {
                    var candidate = Path.Combine(dir, "ffmpeg.exe");
                    if (File.Exists(candidate))
                    {
                        knownPath = candidate;
                        break;
                    }
                }
            }
            catch { /* Verzeichnis existiert ggf. noch nicht */ }

            if (knownPath is not null)
            {
                return knownPath;
            }

            // 2. Neben der Entry Assembly oder in PATH
            var localCandidate = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
            if (File.Exists(localCandidate))
            {
                return localCandidate;
            }

            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var entry in pathEnv.Split(Path.PathSeparator))
            {
                var dir = entry.Trim().Trim('"');
                if (string.IsNullOrWhiteSpace(dir) || !Path.IsPathFullyQualified(dir))
                {
                    continue;
                }

                try
                {
                    var candidate = Path.Combine(dir, "ffmpeg.exe");
                    if (File.Exists(candidate))
                    {
                        return Path.GetFullPath(candidate);
                    }
                }
                catch { /* ignore invalid entries */ }
            }

            // 3. Nicht gefunden – interaktiven Download anbieten (nur ffmpeg)
            var result = MessageBox.Show(
                this,
                "ffmpeg.exe wurde nicht gefunden und wird für die Video-zu-Audio Konvertierung benötigt.\n\n" +
                "Möchten Sie FFmpeg jetzt herunterladen (ca. 100 MB, nur ffmpeg, kein yt-dlp)?",
                "FFmpeg fehlt",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button1);

            if (result != DialogResult.Yes)
            {
                return null;
            }

            // 4. Herunterladen mit echtem Progress über den ProgressDialog (nur ffmpeg)
            using var downloadCts = new CancellationTokenSource();
            var downloadProgress = new Progress<string>(msg =>
            {
                LogManager.Log($"FFmpeg Download: {msg}");
            });

            using var downloadDlg = new ProgressDialog(
                title: "FFmpeg wird heruntergeladen...",
                progress: null,
                windowCloseDelay: 0.0d,
                ct: downloadCts.Token);

            downloadDlg.Show(this);

            try
            {
                var manager = new FfmpegBinaryManager();
                string ffmpegPath = await manager.InstallFfmpegAsync(downloadProgress, downloadCts.Token)
                    .ConfigureAwait(false);

                downloadDlg.Complete();
                return ffmpegPath;
            }
            catch (OperationCanceledException)
            {
                downloadDlg.Close();
                LogManager.Log("FFmpeg Download abgebrochen.");
                return null;
            }
            catch (Exception ex)
            {
                downloadDlg.Close();
                LogManager.Log($"FFmpeg Download fehlgeschlagen: {ex.Message}");
                ShowErrorWithCopyButton(this, "FFmpeg Download fehlgeschlagen", ex);
                return null;
            }
        }
    }
}
