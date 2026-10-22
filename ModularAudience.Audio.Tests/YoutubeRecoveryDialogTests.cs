using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Forms.Modules.Dialogs;
using ModularAudience.Ytdlp;
using System.Reflection;
using System.Windows.Forms;

namespace ModularAudience.Audio.Tests;

[TestClass]
[DoNotParallelize]
public sealed class YoutubeRecoveryDialogTests
{
    [STATestMethod]
    public void AuthenticationDialog_RequiresFreshConsent_AndTlsDialogNeverOffersCookies()
    {
        using (var dialog = CreateDialog("ERROR: Sign in to confirm"))
        {
            Assert.IsFalse(Field<TextBox>(dialog, "diagnosticsText").Text.Contains("SENSITIVE_COOKIE_VALUE"));
            var retry = Field<Button>(dialog, "retryButton");
            Assert.IsFalse(retry.Enabled);
            Invoke(dialog, "Retry_Click", null, EventArgs.Empty);
            Assert.IsNull(Property(dialog, "Decision"));
            Field<CheckBox>(dialog, "consentCheck").Checked = true;
            Assert.IsTrue(retry.Enabled);
            Invoke(dialog, "Retry_Click", null, EventArgs.Empty);
            var decision = (YtdlpRecoveryDecision)Property(dialog, "Decision")!;
            Assert.AreEqual(YtdlpRecoveryAction.BrowserSession, decision.Action);
            Assert.IsTrue(decision.Session!.ConsentGranted);
            Assert.AreEqual("Default", decision.Session.Profile);
        }
        using (var fresh = CreateDialog("ERROR: Sign in to confirm"))
            Assert.IsFalse(Field<CheckBox>(fresh, "consentCheck").Checked);
        using var tls = CreateDialog("ERROR: CERTIFICATE_VERIFY_FAILED");
        Assert.AreEqual(1, Field<ComboBox>(tls, "actionCombo").Items.Count);
        Invoke(tls, "Retry_Click", null, EventArgs.Empty);
        var trust = (YtdlpRecoveryDecision)Property(tls, "Decision")!;
        Assert.AreEqual(YtdlpRecoveryAction.SystemCertificateTrust, trust.Action);
        Assert.IsNull(trust.Session);
    }

    [STATestMethod]
    public void BatchRecovery_ExcludesImportedAndCleanupFailures_AndIgnoresOldCallbacks()
    {
        var previousContext = SynchronizationContext.Current;
        try
        {
            using var cancellation = new CancellationTokenSource();
            using var window = new YoutubeBrowserWindow(_ => Assert.Fail("Recovery must not reimport successful audio."));
            var context = new QueuedContext();
            SetField(window, "operationCancellation", cancellation);
            YtdlpVideo[] videos = [new() { Title = "Imported" }, new() { Title = "Authentication" }, new() { Title = "Cleanup failed" }];
            var batch = Invoke(window, "InitializeBatch", (object)videos)!;
            var jobs = (Array)Property(batch, "Jobs")!;
            var imported = jobs.GetValue(0)!;
            var failed = jobs.GetValue(1)!;
            var cleanup = jobs.GetValue(2)!;
            foreach (object job in jobs) SetProperty(job, "Terminal", true);
            SetProperty(imported, "Status", "Completed");
            SetProperty(imported, "HandedOff", true);
            SetProperty(imported, "Error", new YtdlpProcessException(1, "ERROR: HTTP Error 503"));
            SetProperty(failed, "Error", new YtdlpProcessException(1, "ERROR: Sign in"));
            SetProperty(cleanup, "Error", new AggregateException(new YtdlpProcessException(1, "ERROR: HTTP Error 503"), new IOException("Cleanup failed")));
            var eligible = (Array)typeof(YoutubeBrowserWindow).GetMethod("RecoverableJobs", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [batch])!;
            Assert.AreEqual(1, eligible.Length);
            Assert.AreSame(failed, eligible.GetValue(0));
            SynchronizationContext.SetSynchronizationContext(context);
            var stale = (IProgress<YtdlpDownloadProgress>)Invoke(window, "CreateDownloadProgress", batch, failed)!;
            var staleText = (IProgress<string>)Invoke(window, "CreateJobTextProgress", batch, failed, false)!;
            Invoke(window, "ResetForRecovery", failed);
            SetProperty(failed, "NetworkStage", true);
            stale.Report(new() { Status = "finished", Percent = 100 });
            staleText.Report("[ExtractAudio] Old attempt");
            context.Drain();
            Assert.IsNull(Property(failed, "Latest"));
            Assert.AreEqual(false, Property(failed, "DownloadFinished"));
            SynchronizationContext.SetSynchronizationContext(context);
            var current = (IProgress<YtdlpDownloadProgress>)Invoke(window, "CreateDownloadProgress", batch, failed)!;
            current.Report(new() { Status = "downloading", Percent = 25 });
            context.Drain();
            Assert.IsNotNull(Property(failed, "Latest"));
            Assert.AreEqual("Completed", Property(imported, "Status"));
            Assert.AreEqual(1, Property(failed, "Attempt"));
            SetField(window, "operationCancellation", null);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
    }

    private sealed class QueuedContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> queue = new();
        public override void Post(SendOrPostCallback callback, object? state) => queue.Enqueue((callback, state));
        internal void Drain()
        {
            while (queue.TryDequeue(out var item)) item.Callback(item.State);
        }
    }

    private static Form CreateDialog(string error)
    {
        var type = typeof(YoutubeBrowserWindow).Assembly.GetType("ModularAudience.Forms.Modules.Dialogs.YoutubeRecoveryDialog")!;
        YtdlpProcessException[] failures = [new(1, error)];
        return (Form)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null,
            [failures, new YtdlpDownloadOptions(), "Cookie: SENSITIVE_COOKIE_VALUE\n" + error], null)!;
    }

    private static T Field<T>(object target, string name) => (T)target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void SetField(object target, string name, object? value) => target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    private static object? Property(object target, string name) => target.GetType()
        .GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target);
    private static void SetProperty(object target, string name, object? value) => target.GetType()
        .GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    private static object? Invoke(object target, string name, params object?[] args) => target.GetType()
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
}
