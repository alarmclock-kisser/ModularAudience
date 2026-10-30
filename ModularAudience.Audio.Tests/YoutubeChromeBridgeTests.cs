using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Forms.Modules.Dialogs;
using ModularAudience.Ytdlp;
using System.Reflection;
using System.Diagnostics;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class YoutubeChromeBridgeTests
{
    [TestMethod]
    public void ChromeTransfer_FiltersDomainsAndCreatesDisposableProcessCookieJar()
    {
        var type = typeof(YoutubeBrowserWindow).Assembly.GetType("ModularAudience.Forms.Modules.Dialogs.YoutubeChromeBridge")!;
        var session = (YtdlpBrowserSession)type.GetMethod("ParseSession", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, ["""
            {"cookies":[
              {"domain":".youtube.com","path":"/","secure":true,"httpOnly":true,"expirationDate":2000000000,"name":"SID","value":"test-value"},
              {"domain":"other.example","path":"/","secure":true,"httpOnly":true,"expirationDate":0,"name":"OTHER","value":"excluded"}
            ]}
            """])!;
        session.Validate();
        Assert.IsFalse(session.ToString().Contains("test-value"));
        foreach (string name in new[] { "manifest.json", "background.js", "connect.js" })
        {
            using var resource = typeof(YoutubeBrowserWindow).Assembly.GetManifestResourceStream("ModularAudience.BrowserBridge." + name);
            Assert.IsNotNull(resource, "Extension resource must be bundled: " + name);
        }
        StringAssert.Contains(session.CookieContents!, "#HttpOnly_.youtube.com\tTRUE\t/\tTRUE\t2000000000\tSID\ttest-value");
        Assert.IsFalse(session.CookieContents!.Contains("excluded"));
        string directory = Path.Combine(Path.GetTempPath(), "ma-bridge-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var start = new ProcessStartInfo("not-started.exe");
            start.ArgumentList.Add("--");
            start.ArgumentList.Add("https://www.youtube.com/watch?v=abcdefghijk");
            var scratchType = typeof(YtdlpClient).Assembly.GetType("ModularAudience.Ytdlp.YtdlpSessionScratch")!;
            string cookiePath;
            using ((IDisposable)Activator.CreateInstance(scratchType, [start, directory, null, session.CookieContents])!)
            {
                Assert.AreEqual("--cookies", start.ArgumentList[0]);
                cookiePath = start.ArgumentList[1];
                Assert.AreEqual(session.CookieContents, File.ReadAllText(cookiePath));
                Assert.AreEqual("--", start.ArgumentList[2]);
            }
            Assert.IsFalse(File.Exists(cookiePath));
        }
        finally { Directory.Delete(directory, true); }
    }
}
