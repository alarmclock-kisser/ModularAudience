using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.CoOp.Networking;
using ModularAudience.CoOp.Synchronization;
using ModularAudience.Forms;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class CoOpConnectionMenuTests
{
    [STATestMethod]
    public void DiscoveryPopulationAndProgrammaticSelection_DoNotAutoConnect()
    {
        using var menu = new CoOpConnectionMenu();
        int connections = 0;
        menu.HostSelected += (_, _) => connections++;

        menu.SetHosts([Host()]);
        menu.UpdateState(CoOpSessionState.Disconnected, isBusy: false);

        Assert.AreEqual(-1, menu.Hosts.SelectedIndex);
        Assert.IsTrue(menu.Hosts.Enabled);
        menu.Hosts.SelectedIndex = 0;
        Assert.AreEqual(0, connections);
    }

    [STATestMethod]
    public void CommittingHostSelection_RequestsConnectionWithoutAnotherMenuClick()
    {
        using var menu = new CoOpConnectionMenu();
        CoOpDiscoveredHost host = Host();
        CoOpDiscoveredHost? selected = null;
        int connections = 0;
        menu.HostSelected += (_, e) => { connections++; selected = e.Host; };
        menu.SetHosts([host]);
        menu.UpdateState(CoOpSessionState.Disconnected, isBusy: false);
        menu.Hosts.SelectedIndex = 0;

        CommitSelection(menu);

        Assert.AreEqual(1, connections);
        Assert.AreSame(host, selected);
    }

    [STATestMethod]
    [DataRow(CoOpSessionState.Hosting, false)]
    [DataRow(CoOpSessionState.Connected, false)]
    [DataRow(CoOpSessionState.Disconnected, true)]
    public void ActiveOrBusySession_BlocksHostSelection(CoOpSessionState state, bool isBusy)
    {
        using var menu = new CoOpConnectionMenu();
        int connections = 0;
        menu.HostSelected += (_, _) => connections++;
        menu.SetHosts([Host()]);
        menu.UpdateState(state, isBusy);
        menu.Hosts.SelectedIndex = 0;

        CommitSelection(menu);

        Assert.IsFalse(menu.Hosts.Enabled);
        Assert.IsFalse(menu.ConnectToAddress.Enabled);
        Assert.AreEqual(0, connections);
        Assert.AreEqual(state == CoOpSessionState.Connected, menu.ConnectToAddress.Checked);
    }

    [STATestMethod]
    public void EmptyDiscovery_LeavesSingleManualConnectionActionAvailable()
    {
        using var menu = new CoOpConnectionMenu();
        int manualConnections = 0;
        menu.ConnectToAddressRequested += (_, _) => manualConnections++;
        menu.SetHosts([]);
        menu.UpdateState(CoOpSessionState.Disconnected, isBusy: false);

        menu.ConnectToAddress.PerformClick();

        Assert.AreEqual("Connect to address...", menu.ConnectToAddress.Text);
        Assert.IsTrue(menu.ConnectToAddress.Enabled);
        Assert.IsFalse(menu.Hosts.Enabled);
        Assert.AreEqual(1, manualConnections);
    }

    private static CoOpDiscoveredHost Host() => new(Guid.NewGuid(), "LAN host", "192.168.43.25", 47823, 1);

    private static void CommitSelection(CoOpConnectionMenu menu) => typeof(ComboBox)
        .GetMethod("OnSelectionChangeCommitted", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(menu.Hosts.ComboBox, [EventArgs.Empty]);
}
