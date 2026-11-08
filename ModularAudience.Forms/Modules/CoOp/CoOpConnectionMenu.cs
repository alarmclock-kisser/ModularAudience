using ModularAudience.CoOp.Networking;
using ModularAudience.CoOp.Synchronization;

namespace ModularAudience.Forms;

internal sealed class CoOpHostSelectionEventArgs(CoOpDiscoveredHost host) : EventArgs
{
    internal CoOpDiscoveredHost Host { get; } = host;
}

/// <summary>
/// One manual address action plus direct, explicitly committed LAN selection.
/// Populating the discovery results must never connect to a host automatically.
/// </summary>
internal sealed class CoOpConnectionMenu : IDisposable
{
    internal CoOpConnectionMenu()
    {
        ConnectToAddress = new ToolStripMenuItem("Connect to address...")
        {
            CheckOnClick = false,
            ToolTipText = "Enter a host name or IP address. Select a discovered host below to connect directly."
        };
        Hosts = new ToolStripComboBox
        {
            AutoSize = false,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 260,
            ToolTipText = "Select a local co-op host to connect."
        };
        ConnectToAddress.Click += (_, _) => ConnectToAddressRequested?.Invoke(this, EventArgs.Empty);
        Hosts.ComboBox.SelectionChangeCommitted += this.HostSelectionCommitted;
        UpdateState(CoOpSessionState.Disconnected, isBusy: false);
    }

    internal ToolStripMenuItem ConnectToAddress { get; }
    internal ToolStripComboBox Hosts { get; }

    internal event EventHandler? ConnectToAddressRequested;
    internal event EventHandler<CoOpHostSelectionEventArgs>? HostSelected;

    internal void SetHosts(IReadOnlyList<CoOpDiscoveredHost> hosts)
    {
        Hosts.Items.Clear();
        foreach (CoOpDiscoveredHost host in hosts) Hosts.Items.Add(host);
        Hosts.SelectedIndex = -1;
    }

    internal void UpdateState(CoOpSessionState state, bool isBusy)
    {
        ConnectToAddress.Checked = state == CoOpSessionState.Connected;
        ConnectToAddress.Enabled = state == CoOpSessionState.Disconnected && !isBusy;
        Hosts.Enabled = state == CoOpSessionState.Disconnected && !isBusy && Hosts.Items.Count > 0;
    }

    private void HostSelectionCommitted(object? sender, EventArgs e)
    {
        if (Hosts.Enabled && Hosts.SelectedItem is CoOpDiscoveredHost host)
            HostSelected?.Invoke(this, new CoOpHostSelectionEventArgs(host));
    }

    public void Dispose()
    {
        Hosts.ComboBox.SelectionChangeCommitted -= this.HostSelectionCommitted;
        ConnectToAddress.Dispose();
        Hosts.Dispose();
    }
}
