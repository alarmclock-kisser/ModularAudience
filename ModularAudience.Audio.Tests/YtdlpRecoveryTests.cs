using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Ytdlp;
using System.Reflection;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class YtdlpRecoveryTests
{
    [TestMethod]
    public async Task ProcessFailure_Http503_PreservesStructuredFailureForSafeRecovery()
    {
        using var timeout = new CancellationTokenSource(System.Diagnostics.Debugger.IsAttached
            ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(30));
        var processType = typeof(YtdlpClient).Assembly.GetType("ModularAudience.Ytdlp.YtdlpProcess")!;
        var run = processType.GetMethod("RunAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
        string executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        string[] arguments = ["-NoProfile", "-NonInteractive", "-Command",
            "[Console]::Error.WriteLine('Cookie: '+('SENSITIVE_COOKIE_VALUE'*1400)); [Console]::Error.WriteLine('ERROR: [youtube] v586fXgj50Y: HTTP Error 503: Service Unavailable'); exit 1"];
        var progress = new CaptureProgress();
        var task = (Task<string>)run.Invoke(null,
            [executable, arguments, Path.GetTempPath(), progress, false, timeout.Token])!;
        Exception? failure = null;
        try { await task; }
        catch (Exception error) { failure = error; }
        Assert.IsNotNull(failure);
        StringAssert.Contains(failure.Message, "HTTP Error 503");
        Assert.AreEqual("YtdlpProcessException", failure.GetType().Name,
            "Recovery needs a typed process error; generic exceptions cannot safely distinguish transient, TLS and authentication failures.");
        Assert.AreEqual(YtdlpFailureKind.TransientNetwork, ((YtdlpProcessException)failure).FailureKind);
        Assert.AreEqual(1, progress.Messages.Count(message => message.StartsWith(YtdlpRetryPolicy.ProgressPrefix)));
        Assert.IsFalse(failure.Message.Contains("SENSITIVE_COOKIE_VALUE"));
        Assert.IsFalse(progress.Messages.Any(message => message.Contains("SENSITIVE_COOKIE_VALUE")));
        StringAssert.Contains(failure.Message, "Cookie: [redacted]");
    }

    [TestMethod]
    public async Task RetryPolicy_RefreshesOnceThenStops_AndCancellationPreventsNextAttempt()
    {
        int attempts = 0, pauses = 0;
        Task<string> Fail(int attempt, CancellationToken token)
        {
            attempts++;
            throw new YtdlpProcessException(1, "ERROR: HTTP Error 503");
        }
        await Assert.ThrowsExactlyAsync<YtdlpProcessException>(() => Execute(Fail, (delay, token) =>
        {
            pauses++;
            Assert.AreEqual(TimeSpan.FromSeconds(2), delay);
            return Task.CompletedTask;
        }, CancellationToken.None));
        Assert.AreEqual(2, attempts);
        Assert.AreEqual(1, pauses);
        attempts = 0;
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => Execute(Fail, (delay, token) =>
        {
            cancellation.Cancel();
            return Task.CompletedTask;
        }, cancellation.Token));
        Assert.AreEqual(1, attempts);
    }

    [TestMethod]
    public async Task RetryPolicy_PermanentFailuresAndLongServerWaits_NeverLoop()
    {
        foreach (string text in new[] { "CERTIFICATE_VERIFY_FAILED", "[WinError 10061]", "Sign in to confirm", "HTTP Error 429", "DRM protected", "HTTP Error 503\nRetry-After: 120" })
        {
            int attempts = 0;
            var failure = new YtdlpProcessException(1, text);
            Assert.IsFalse(YtdlpRetryPolicy.CanRetry(failure), text);
            await Assert.ThrowsExactlyAsync<YtdlpProcessException>(() => Execute((attempt, token) =>
            {
                attempts++;
                throw failure;
            }, (_, _) => { Assert.Fail("A permanent failure must not schedule a retry."); return Task.CompletedTask; }, CancellationToken.None));
            Assert.AreEqual(1, attempts, text);
        }
        var delayed = new YtdlpProcessException(1, "HTTP Error 503\nRetry-After: 120");
        Assert.IsFalse(YtdlpRecoveryRules.CanApply(delayed, YtdlpRecoveryAction.RetrySameSettings, new()));
        var dated = new YtdlpProcessException(1, "HTTP Error 503\nRetry-After: " + DateTimeOffset.UtcNow.AddMinutes(2).ToString("R"));
        Assert.IsFalse(YtdlpRetryPolicy.CanRetry(dated));
        Assert.IsFalse(YtdlpRetryPolicy.CanRetry(new(1, "HTTP Error 503\nRetry-After: malformed")));
        TimeSpan requested = TimeSpan.Zero;
        Assert.AreEqual("done", await Execute((attempt, token) => attempt == 1
            ? Task.FromException<string>(new YtdlpProcessException(1, "HTTP Error 503\nRetry-After: 7")) : Task.FromResult("done"),
            (delay, token) => { requested = delay; return Task.CompletedTask; }, CancellationToken.None));
        Assert.AreEqual(TimeSpan.FromSeconds(7), requested);
    }

    [TestMethod]
    public void BrowserSession_RequiresConsentAndLocalProfile_AndNeverDisablesTls()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new YtdlpBrowserSession { Profile = "Default" }.Validate());
        foreach (string profile in new[] { "", "../Default", "C:\\Users\\other\\Profile", "Default::container", ".." })
        {
            var invalid = new YtdlpBrowserSession { Profile = profile, ConsentGranted = true };
            Assert.ThrowsExactly<ArgumentException>(invalid.Validate, profile);
        }
        var session = new YtdlpBrowserSession { Browser = YtdlpBrowser.Edge, Profile = "Profile 1", ConsentGranted = true };
        var arguments = new List<string>();
        typeof(YtdlpBrowserSession).GetMethod("AddArguments", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(session, [arguments]);
        CollectionAssert.AreEqual(new[] { "--cookies-from-browser", "edge:Profile 1" }, arguments);
        var options = new YtdlpDownloadOptions();
        var recovered = YtdlpRecoveryRules.Apply(YtdlpFailureKind.Certificate, new(YtdlpRecoveryAction.SystemCertificateTrust), options);
        typeof(YtdlpNetworkOptions).GetMethod("AddArguments", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(recovered.Network, [arguments]);
        Assert.IsTrue(arguments.Contains("no-certifi"));
        Assert.IsFalse(arguments.Contains("--no-check-certificates"));
        Assert.IsFalse(options.Network.UseSystemCertificateTrust);
        Assert.IsNull(options.BrowserSession);
        VerifySessionScratchIsJobLocal(arguments);
    }

    [TestMethod]
    public void RecoveryRules_UseCookiesOnlyForAuthentication_AndPreserveOriginalOptions()
    {
        var options = new YtdlpDownloadOptions { WavBitDepth = 24, Network = new() { ProxyUrl = "http://proxy.example:8080" } };
        foreach (var kind in Enum.GetValues<YtdlpFailureKind>())
            Assert.AreEqual(kind == YtdlpFailureKind.Authentication,
                YtdlpRecoveryRules.CanApply(kind, YtdlpRecoveryAction.BrowserSession, options), kind.ToString());
        var session = new YtdlpBrowserSession { Browser = YtdlpBrowser.Firefox, Profile = "abc.default-release", ConsentGranted = true };
        var recovered = YtdlpRecoveryRules.Apply(YtdlpFailureKind.Authentication, new(YtdlpRecoveryAction.BrowserSession, session), options);
        Assert.AreSame(session, recovered.BrowserSession);
        Assert.IsNull(options.BrowserSession);
        Assert.AreEqual(options.Network, recovered.Network);
        Assert.AreEqual(24, recovered.WavBitDepth);
        Assert.ThrowsExactly<InvalidOperationException>(() => YtdlpRecoveryRules.Apply(YtdlpFailureKind.Certificate, new(YtdlpRecoveryAction.BrowserSession, session), options));
        Assert.AreEqual(YtdlpFailureKind.Certificate, YtdlpProcessException.Classify("WARNING: HTTP Error 503\nERROR: CERTIFICATE_VERIFY_FAILED"));
        Assert.AreEqual(YtdlpFailureKind.AccessDenied, YtdlpProcessException.Classify("ERROR: DRM protected. Sign in"));
        Assert.AreEqual(YtdlpFailureKind.BrowserCookies, YtdlpProcessException.Classify("ERROR: Could not decrypt cookies with DPAPI"));
    }

    private static void VerifySessionScratchIsJobLocal(List<string> arguments)
    {
        string job = Path.Combine(Path.GetTempPath(), "MA-session-scratch-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(job);
        try
        {
            var start = new System.Diagnostics.ProcessStartInfo("not-started.exe");
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            var type = typeof(YtdlpClient).Assembly.GetType("ModularAudience.Ytdlp.YtdlpSessionScratch")!;
            string scratch;
            using ((IDisposable)Activator.CreateInstance(type, [start, job, null])!)
            {
                scratch = start.Environment["TEMP"]!;
                Assert.AreEqual(Path.GetFullPath(job), Path.GetDirectoryName(scratch));
                Assert.AreEqual(scratch, start.Environment["TMP"]);
                Assert.IsTrue(Directory.Exists(scratch));
                Assert.AreEqual(0, Directory.GetFiles(scratch).Length);
            }
            Assert.IsFalse(Directory.Exists(scratch));
        }
        finally { Directory.Delete(job, recursive: true); }
    }

    private static Task<string> Execute(Func<int, CancellationToken, Task<string>> action,
        Func<TimeSpan, CancellationToken, Task> delay, CancellationToken token) =>
        (Task<string>)typeof(YtdlpRetryPolicy).GetMethod("ExecuteCoreAsync", BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(typeof(string)).Invoke(null, [action, null, delay, token])!;

    private sealed class CaptureProgress : IProgress<string>
    {
        internal System.Collections.Concurrent.ConcurrentQueue<string> Messages { get; } = new();
        public void Report(string value) => Messages.Enqueue(value);
    }
}
