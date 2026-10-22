using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Forms.Modules.Dialogs;
using ModularAudience.Ytdlp;
using System.Collections;
using System.Reflection;
using System.Windows.Forms;

namespace ModularAudience.Audio.Tests;

[TestClass]
[DoNotParallelize]
public sealed class YoutubeDownloadActionsTests
{
    [STATestMethod]
    public void Downloads_MultiSelectionCancelsOnlySelected_DeletesOnlyTerminalRows()
    {
        using var window = new YoutubeBrowserWindow(_ => Assert.Fail("No import expected."));
        _ = window.Handle;
        var list = (ListView)Field(window, "downloadJobsList").GetValue(window)!;
        _ = list.Handle;
        Field(window, "operationCancellation").SetValue(window, new CancellationTokenSource());
        YtdlpVideo[] videos = [new() { Id = "v586fXgj50Y", Title = "First" }, new() { Id = "bqwhPlX7daw", Title = "Second" }];
        var batch = Invoke(window, "InitializeBatch", (object)videos)!;
        var jobs = ((IEnumerable)batch.GetType().GetProperty("Jobs")!.GetValue(batch)!).Cast<object>().ToArray();
        Assert.IsTrue(list.MultiSelect);
        CollectionAssert.AreEqual(new[] { "Cancel selected", "Retry selected", "Delete selected" },
            list.ContextMenuStrip!.Items.Cast<ToolStripItem>().Select(item => item.Text).ToArray());
        list.Items[0].Selected = true;
        Invoke(window, "CancelJobs_Click", null, EventArgs.Empty);
        Assert.AreEqual(true, Property(jobs[0], "CancelRequested"));
        Assert.AreEqual(false, Property(jobs[1], "CancelRequested"));
        list.Items[1].Selected = true;
        Set(jobs[0], "Terminal", true);
        Set(jobs[0], "Status", "Cancelled");
        Assert.AreEqual(true, Invoke(window, "CanRetryJob", jobs[0]));
        Set(jobs[0], "HandedOff", true);
        Assert.AreEqual(false, Invoke(window, "CanRetryJob", jobs[0]), "Imported audio must never be retried.");
        Invoke(window, "DeleteJobs_Click", null, EventArgs.Empty);
        Assert.AreEqual(1, list.Items.Count, "A running row must survive Delete selected.");
        Assert.AreEqual("Second", list.Items[0].Text);
    }

    [STATestMethod]
    public void NetworkReset_UsesAutomaticEnvironmentAndRejectsEmptyExplicitProxy()
    {
        using var window = new YoutubeBrowserWindow(_ => Assert.Fail());
        var ip = (ComboBox)Field(window, "ipVersionCombo").GetValue(window)!;
        var proxy = (ComboBox)Field(window, "proxyModeCombo").GetValue(window)!;
        ip.SelectedIndex = 2;
        proxy.SelectedIndex = 2;
        ((TextBox)Field(window, "proxyText").GetValue(window)!).Clear();
        var error = Assert.ThrowsExactly<TargetInvocationException>(() => Invoke(window, "CaptureNetworkOptions"));
        Assert.IsInstanceOfType<ArgumentException>(error.InnerException);
        Invoke(window, "ResetNetwork_Click", null, EventArgs.Empty);
        var options = (YtdlpNetworkOptions)Invoke(window, "CaptureNetworkOptions")!;
        Assert.AreEqual(YtdlpIpVersion.Automatic, options.IpVersion);
        Assert.IsNull(options.ProxyUrl);
        Assert.IsFalse(options.UseSystemCertificateTrust);
    }

    private static FieldInfo Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static object? Invoke(object target, string name, params object?[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(target, args);
    private static object? Property(object target, string name) => target.GetType().GetProperty(name)!.GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetProperty(name)!.SetValue(target, value);
}
