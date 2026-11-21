using ModularAudience.CoOp.Networking;
using ModularAudience.VocalSynth.Android.Services;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Maui.Storage;

namespace ModularAudience.VocalSynth.Android;

public sealed class CoOpPage : ContentPage
{
    private readonly CoOpAndroidSession session;
    private readonly SoundboardStore store;
    private readonly Picker hostPicker = new() { Title = "LAN hosts" };
    private readonly Entry hostEntry = new() { Placeholder = "Host IP or name", Keyboard = Keyboard.Text };
    private readonly Entry portEntry = new() { Text = "47823", Placeholder = "Port", Keyboard = Keyboard.Numeric };
    private readonly Entry displayNameEntry = new() { Placeholder = "Device name" };
    private readonly Entry passwordEntry = new() { Placeholder = "Session password (optional; 8–128 characters)", IsPassword = true };
    private readonly Label statusLabel = new() { Text = "Not connected", LineBreakMode = LineBreakMode.WordWrap };
    private readonly Label transferLabel = new() { Text = "Select saved samples to send." };
    private readonly VerticalStackLayout sampleList = new() { Spacing = 6 };
    private readonly Button discoverButton = new() { Text = "Refresh LAN hosts" };
    private readonly Button connectButton = new() { Text = "Connect" };
    private readonly Button hostButton = new() { Text = "Host co-op" };
    private readonly Button disconnectButton = new() { Text = "Disconnect", IsEnabled = false };
    private readonly Button sendButton = new() { Text = "Send selected samples", IsEnabled = false };
    private readonly Dictionary<Guid, CheckBox> selections = [];
    private readonly SemaphoreSlim passwordSaveGate = new(1, 1);
    private Task passwordSaveTask = Task.CompletedTask;
    private bool busy;
    private bool passwordLoaded;
    private bool passwordSaveErrorShown;

    internal CoOpPage(CoOpAndroidSession session, SoundboardStore store)
    {
        this.session = session;
        this.store = store;
        this.Title = "Co-op";
        this.BackgroundColor = Color.FromArgb("#171717");
        this.displayNameEntry.Text = this.session.DisplayName;
        this.session.ConfirmIncomingTransfer = this.ConfirmAndSaveIncomingBatchAsync;
        this.session.ConfirmServerCertificate = this.ConfirmServerCertificateAsync;
        this.session.ConfirmPasswordlessSession = this.ConfirmPasswordlessSessionAsync;
        this.session.StateChanged += this.Session_StateChanged;
        this.session.StatusChanged += this.Session_StatusChanged;

        this.discoverButton.Clicked += async (_, _) => await this.RefreshHostsAsync();
        this.hostPicker.SelectedIndexChanged += (_, _) =>
        {
            if (this.hostPicker.SelectedItem is CoOpDiscoveredHost host)
            {
                this.hostEntry.Text = host.Address;
                this.portEntry.Text = host.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        };
        this.connectButton.Clicked += async (_, _) => await this.ConnectAsync();
        this.hostButton.Clicked += async (_, _) => await this.HostAsync();
        this.disconnectButton.Clicked += async (_, _) => await this.DisconnectAsync();
        this.sendButton.Clicked += async (_, _) => await this.SendSelectedAsync();
        this.displayNameEntry.TextChanged += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.NewTextValue))
                this.session.DisplayName = args.NewTextValue.Trim();
        };
        this.passwordEntry.TextChanged += (_, args) =>
        {
            string password = args.NewTextValue ?? string.Empty;
            this.session.SessionPassword = password;
            this.passwordSaveTask = this.SavePasswordAsync(password);
        };

        var hostAndPort = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 8,
        };
        hostAndPort.Children.Add(this.hostEntry);
        var portLayout = new HorizontalStackLayout
        {
            Spacing = 6,
            Children =
            {
                new Label { Text = "Port", VerticalOptions = LayoutOptions.Center },
                this.portEntry,
            },
        };
        Grid.SetColumn(portLayout, 1);
        hostAndPort.Children.Add(portLayout);

        VerticalStackLayout content = new()
        {
            Padding = new Thickness(16),
            Spacing = 12,
            Children =
            {
                new Label { Text = "Co-op connection", FontSize = 24, FontAttributes = FontAttributes.Bold },
                this.displayNameEntry,
                this.passwordEntry,
                this.hostPicker,
                this.discoverButton,
                hostAndPort,
                new HorizontalStackLayout { Spacing = 8, Children = { this.connectButton, this.hostButton, this.disconnectButton } },
                this.statusLabel,
                new BoxView { HeightRequest = 1, Color = Colors.DimGray },
                new Label { Text = "Send selected Soundboard samples", FontSize = 18, FontAttributes = FontAttributes.Bold },
                this.transferLabel,
                this.sampleList,
                this.sendButton,
            },
        };
        this.Content = new ScrollView { Content = content };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await this.LoadPasswordAsync();
        await this.ReloadSamplesAsync();
        if (this.hostPicker.ItemsSource == null)
            await this.RefreshHostsAsync();
        this.UpdateControls();
    }

    protected override async void OnDisappearing()
    {
        await this.passwordSaveTask;
        base.OnDisappearing();
    }

    private async Task LoadPasswordAsync()
    {
        if (this.passwordLoaded)
            return;
        this.passwordLoaded = true;
        try
        {
            string? password = await SecureStorage.Default.GetAsync("coop.sessionPassword");
            this.passwordEntry.Text = password ?? string.Empty;
            this.session.SessionPassword = password ?? string.Empty;
        }
        catch (Exception ex)
        {
            await this.ShowErrorAsync("Could not load the saved co-op password", ex);
        }
    }

    private async Task SavePasswordAsync(string password)
    {
        await this.passwordSaveGate.WaitAsync();
        try
        {
            await SecureStorage.Default.SetAsync("coop.sessionPassword", password);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Saving the co-op password failed: {ex}");
            if (!this.passwordSaveErrorShown)
            {
                this.passwordSaveErrorShown = true;
                await this.ShowErrorAsync("Could not save the co-op password", ex);
            }
        }
        finally
        {
            this.passwordSaveGate.Release();
        }
    }

    private async Task RefreshHostsAsync()
    {
        this.discoverButton.IsEnabled = false;
        this.statusLabel.Text = "Scanning the local network...";
        try
        {
            IReadOnlyList<CoOpDiscoveredHost> hosts = await this.session.DiscoverAsync(CancellationToken.None);
            this.hostPicker.ItemsSource = hosts.ToList();
            this.statusLabel.Text = hosts.Count == 0
                ? "No local hosts found. If the host PC is connected to this phone's hotspot, enter its hotspot-assigned IP and port below; a third hotspot is not required."
                : $"{hosts.Count} local co-op host(s) found.";
        }
        catch (Exception ex)
        {
            await this.ShowErrorAsync("LAN discovery failed", ex);
        }
        finally
        {
            this.discoverButton.IsEnabled = true;
            this.UpdateControls();
        }
    }

    private async Task ConnectAsync()
    {
        if (!int.TryParse(this.portEntry.Text, out int port) || port is <= 0 or > 65535)
        {
            await this.DisplayAlertAsync("Invalid port", "Enter a port between 1 and 65535.", "OK");
            return;
        }
        string password = this.passwordEntry.Text ?? string.Empty;
        if (!await this.ConfirmPasswordChoiceAsync(password, hosting: false))
            return;
        await this.passwordSaveTask;

        await this.RunBusyAsync(async () =>
        {
            this.session.SessionPassword = password;
            await this.session.ConnectAsync(this.hostEntry.Text ?? string.Empty, port);
            await this.DisplayAlertAsync("Co-op connected", "The connection is direct. Internet hosts must be reachable on the selected port.", "OK");
        });
    }

    private async Task HostAsync()
    {
        string password = this.passwordEntry.Text ?? string.Empty;
        if (!await this.ConfirmPasswordChoiceAsync(password, hosting: true))
            return;
        await this.passwordSaveTask;

        await this.RunBusyAsync(async () =>
        {
            this.session.DisplayName = string.IsNullOrWhiteSpace(this.displayNameEntry.Text)
                ? this.session.DisplayName
                : this.displayNameEntry.Text.Trim();
            this.session.SessionPassword = password;
            await this.session.HostAsync();
            string passwordSummary = string.IsNullOrEmpty(password)
                ? "None (local network only)"
                : password;
            await this.DisplayAlertAsync(
                "Co-op hosting started",
                $"Address: {this.session.Address ?? "Not detected"}:{this.session.Port}\n\nSession password: {passwordSummary}\n\nHost certificate SHA-256 fingerprint:\n{this.session.HostCertificateFingerprint}",
                "OK");
        });
    }

    private async Task<bool> ConfirmPasswordChoiceAsync(string password, bool hosting)
    {
        if (!string.IsNullOrEmpty(password) && !ModularAudience.CoOp.Security.CoOpSecurity.IsValidSessionPassword(password))
        {
            await this.DisplayAlertAsync(
                "Session password is too short",
                "Use 8–128 characters, or clear the field to continue without a password. Passwordless sessions are restricted to the local network.",
                "OK");
            return false;
        }

        if (!hosting || !string.IsNullOrEmpty(password))
            return true;

        string action = hosting ? "host" : "connect to";
        return await this.DisplayAlertAsync(
            "Passwordless co-op session",
            $"You are about to {action} without a session password. This is limited to devices on the same local network and offers less protection than a password-protected session. Continue?",
            "Continue",
            "Cancel");
    }

    private async Task DisconnectAsync()
    {
        await this.RunBusyAsync(() => this.session.DisconnectAsync());
    }

    private async Task ReloadSamplesAsync()
    {
        IReadOnlyList<SoundboardItem> items = await this.store.GetItemsAsync();
        this.sampleList.Children.Clear();
        this.selections.Clear();
        foreach (SoundboardItem item in items)
        {
            CheckBox checkBox = new() { VerticalOptions = LayoutOptions.Center };
            this.selections[item.Id] = checkBox;
            this.sampleList.Children.Add(new HorizontalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    checkBox,
                    new Label
                    {
                        Text = $"{item.Name}  ·  {item.GroupName}",
                        VerticalOptions = LayoutOptions.Center,
                        LineBreakMode = LineBreakMode.TailTruncation,
                    },
                },
            });
        }
        this.transferLabel.Text = items.Count == 0
            ? "No saved samples yet. Receive or create samples in Soundboard first."
            : $"{items.Count} saved sample(s) available to share.";
        this.UpdateControls();
    }

    private async Task SendSelectedAsync()
    {
        List<SoundboardItem> selected = (await this.store.GetItemsAsync())
            .Where(item => this.selections.TryGetValue(item.Id, out CheckBox? checkBox) && checkBox.IsChecked)
            .ToList();
        if (selected.Count == 0)
        {
            await this.DisplayAlertAsync("No samples selected", "Select one or more samples to send.", "OK");
            return;
        }

        await this.RunBusyAsync(async () =>
        {
            var audios = new List<(SoundboardItem Item, ModularAudience.Audio.Vocal.VocalAudioData Audio)>();
            foreach (SoundboardItem item in selected)
                audios.Add((item, await this.store.ReadAudioAsync(item)));
            await this.session.ShareItemsAsync(audios);
            await this.DisplayAlertAsync("Samples sent", $"{selected.Count} sample(s) sent to connected peers.", "OK");
        });
    }

    private async Task<bool> ConfirmAndSaveIncomingBatchAsync(CoOpIncomingTransfer transfer)
    {
        int audioCount = transfer.Collections.Sum(collection => collection.Audios.Count);
        string collectionSummary = string.Join(", ", transfer.Collections.Select(collection => collection.Name).Distinct());
        bool accepted = await MainThread.InvokeOnMainThreadAsync(() => this.DisplayAlertAsync(
            "Incoming Soundboard samples",
            $"A co-op peer is sharing {audioCount} sample(s) from {transfer.Collections.Count} collection(s): {collectionSummary}. Download this batch?",
            "Download",
            "Decline"));
        if (!accepted)
            return false;

        try
        {
            foreach (CoOpIncomingBatch batch in transfer.Collections)
            {
                foreach (var snapshot in batch.Audios)
                    await this.store.AddSnapshotAsync(snapshot, batch.Name);
            }
            await MainThread.InvokeOnMainThreadAsync(this.ReloadSamplesAsync);
            return true;
        }
        catch (Exception ex)
        {
            await this.ShowErrorAsync("Saving incoming samples failed", ex);
            return false;
        }
    }

    private async Task<bool> ConfirmServerCertificateAsync(string host, int port, string fingerprint)
    {
        string hostKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{host.ToLowerInvariant()}:{port}")));
        string preferenceKey = $"coop.trustedHost.{hostKey}";
        string savedFingerprint = Preferences.Default.Get(preferenceKey, string.Empty);
        if (string.Equals(savedFingerprint, fingerprint, StringComparison.OrdinalIgnoreCase))
            return true;

        string message = string.IsNullOrEmpty(savedFingerprint)
            ? $"Confirm this co-op host certificate before sending a password or audio:\n\n{host}:{port}\nSHA-256:\n{fingerprint}"
            : $"The pinned certificate for {host}:{port} changed.\n\nNew SHA-256:\n{fingerprint}\n\nOnly accept if you verified this change with the host.";
        bool trusted = await MainThread.InvokeOnMainThreadAsync(() =>
            this.DisplayAlertAsync("Trust co-op host certificate?", message, "Trust", "Cancel"));
        if (trusted)
            Preferences.Default.Set(preferenceKey, fingerprint.ToUpperInvariant());
        return trusted;
    }

    private Task<bool> ConfirmPasswordlessSessionAsync(string host, int port)
    {
        return MainThread.InvokeOnMainThreadAsync(() => this.DisplayAlertAsync(
            "Passwordless local session",
            $"The host at {host}:{port} does not require a password and accepts connections only from its local subnet. Continue?",
            "Connect",
            "Cancel"));
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        if (this.busy)
            return;
        this.busy = true;
        this.UpdateControls();
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            await this.ShowErrorAsync("Co-op operation failed", ex);
        }
        finally
        {
            this.busy = false;
            this.UpdateControls();
        }
    }

    private async Task ShowErrorAsync(string title, Exception exception)
    {
        System.Diagnostics.Debug.WriteLine($"{title}: {exception}");
        await this.DisplayAlertAsync(title, exception.Message, "OK");
    }

    private void Session_StateChanged(object? sender, EventArgs e)
        => MainThread.BeginInvokeOnMainThread(this.UpdateControls);

    private void Session_StatusChanged(string message)
        => MainThread.BeginInvokeOnMainThread(() =>
        {
            this.statusLabel.Text = message;
            this.UpdateControls();
        });

    private void UpdateControls()
    {
        bool active = this.session.IsHosting || this.session.IsConnected;
        this.statusLabel.Text = this.session.State;
        this.connectButton.IsEnabled = !this.busy && !active;
        this.hostButton.IsEnabled = !this.busy && !active;
        this.passwordEntry.IsEnabled = !this.busy && !active;
        this.disconnectButton.IsEnabled = !this.busy && active;
        this.sendButton.IsEnabled = !this.busy && active
            && this.session.ConnectedPeerCount > 0
            && this.selections.Count > 0;
        this.discoverButton.IsEnabled = !this.busy && !active;
    }

}
