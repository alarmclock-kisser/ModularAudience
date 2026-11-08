using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Forms;
using ModularAudience.Forms.Updates;
using ModularAudience.Ytdlp;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class ApplicationUpdateTests
{
    [TestMethod]
    public void SettingsDefaultToAskingAndPreserveOptOutAcrossReleaseChanges()
    {
        string directory = NewDirectory();
        string path = Path.Combine(directory, "update-settings.json");
        try
        {
            var store = new ReleaseUpdateSettingsStore(path);
            Assert.IsTrue(store.LoadAndRecordRunningRelease(127).AskToUpdate);
            using (var json = JsonDocument.Parse(File.ReadAllText(path)))
                Assert.AreEqual("AskToUpdate", json.RootElement.EnumerateObject().First().Name);

            store.DisableAsking();
            var updated = store.LoadAndRecordRunningRelease(128);
            Assert.IsFalse(updated.AskToUpdate);
            Assert.AreEqual(128L, updated.CurrentReleaseNumber);

            File.WriteAllText(path, """{"AskToUpdate":true,"CurrentReleaseNumber":128}""");
            updated = store.LoadAndRecordRunningRelease(129);
            Assert.IsTrue(updated.AskToUpdate);
            Assert.AreEqual(129L, updated.CurrentReleaseNumber);
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void InvalidSettingsAreNotOverwrittenWithEnabledDefaults()
    {
        string directory = NewDirectory();
        string path = Path.Combine(directory, "update-settings.json");
        try
        {
            const string invalid = """{"AskToUpdate":"false"}""";
            File.WriteAllText(path, invalid);
            var store = new ReleaseUpdateSettingsStore(path);
            Assert.ThrowsExactly<JsonException>(() => store.LoadAndRecordRunningRelease(127));
            Assert.AreEqual(invalid, File.ReadAllText(path));
        }
        finally { Directory.Delete(directory, true); }
    }

    [STATestMethod]
    public void UpdateMenuTogglesPersistedPreferenceAndKeepsItsStateWhenSavingFails()
    {
        string directory = NewDirectory();
        string path = Path.Combine(directory, "update-settings.json");
        try
        {
            var store = new ReleaseUpdateSettingsStore(path);
            store.LoadAndRecordRunningRelease(127);
            using var item = new ToolStripMenuItem();
            WindowMain.RefreshUpdateChecks(item, store);
            Assert.IsTrue(item.Checked);
            StringAssert.EndsWith(item.Text, ": On");

            WindowMain.ToggleUpdateChecks(item, store);
            Assert.IsFalse(item.Checked);
            StringAssert.EndsWith(item.Text, ": Off");
            var settings = store.LoadAndRecordRunningRelease(null);
            Assert.IsFalse(settings.AskToUpdate);
            Assert.AreEqual(127L, settings.CurrentReleaseNumber);

            store.SetAskToUpdate(true);
            WindowMain.RefreshUpdateChecks(item, store);
            Assert.IsTrue(item.Checked);
            StringAssert.EndsWith(item.Text, ": On");

            store.DisableAsking();
            WindowMain.RefreshUpdateChecks(item, store);
            Assert.IsFalse(item.Checked);
            StringAssert.EndsWith(item.Text, ": Off");
            WindowMain.ToggleUpdateChecks(item, store);
            Assert.IsTrue(store.LoadAndRecordRunningRelease(null).AskToUpdate);

            File.WriteAllText(path, """{"AskToUpdate":"invalid"}""");
            Assert.ThrowsExactly<JsonException>(() => WindowMain.ToggleUpdateChecks(item, store));
            Assert.IsTrue(item.Checked);
            StringAssert.EndsWith(item.Text, ": On");
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void ReleaseComparisonIsNumericAndRejectsUnexpectedAssets()
    {
        GitHubRelease release = Release(128);
        Assert.AreEqual(128L, ApplicationRelease.NewerThan(release, 99)!.Number);
        Assert.IsNull(ApplicationRelease.NewerThan(release, 128));
        Assert.IsNull(ApplicationRelease.NewerThan(release, 129));
        Assert.ThrowsExactly<InvalidDataException>(() =>
            ApplicationRelease.NewerThan(new GitHubRelease("release-12x", release.Assets), 1));
        Assert.ThrowsExactly<InvalidDataException>(() =>
            ApplicationRelease.NewerThan(new GitHubRelease("release-128", []), 127));
        var wrongUrl = release.Assets[0] with { Url = "https://example.com/ModularAudience.Forms.exe" };
        Assert.ThrowsExactly<InvalidDataException>(() =>
            ApplicationRelease.NewerThan(new GitHubRelease("release-128", [wrongUrl]), 127));
        Assert.ThrowsExactly<InvalidDataException>(() =>
            ApplicationRelease.NewerThan(new GitHubRelease("release-128", [release.Assets[0], release.Assets[0]]), 127));
    }

    [TestMethod]
    public void ReplacementPreservesOriginalOnChangedOrLockedFilesAndInstallsVerifiedUpdate()
    {
        string directory = NewDirectory();
        try
        {
            string target = Path.Combine(directory, "ModularAudience.Forms.exe");
            string staged = AtomicUpdateReplacement.CreateStagedPath(target);
            File.WriteAllText(target, "old executable");
            File.WriteAllText(staged, "new executable");
            string oldHash = Hash(target);
            string newHash = Hash(staged);

            File.WriteAllText(target, "changed executable");
            Assert.ThrowsExactly<InvalidDataException>(() =>
                AtomicUpdateReplacement.Apply(target, staged, oldHash, newHash));
            Assert.AreEqual("changed executable", File.ReadAllText(target));
            File.WriteAllText(target, "old executable");

            File.WriteAllText(staged, "corrupt download");
            Assert.ThrowsExactly<InvalidDataException>(() =>
                AtomicUpdateReplacement.Apply(target, staged, oldHash, newHash));
            Assert.AreEqual("old executable", File.ReadAllText(target));
            File.WriteAllText(staged, "new executable");

            using (var locked = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert.ThrowsExactly<IOException>(() =>
                    AtomicUpdateReplacement.Apply(target, staged, oldHash, newHash));
                Assert.AreEqual("old executable", File.ReadAllText(target));
            }

            Assert.IsNull(AtomicUpdateReplacement.Apply(target, staged, oldHash, newHash));
            Assert.AreEqual("new executable", File.ReadAllText(target));
            Assert.IsFalse(File.Exists(staged));
            Assert.HasCount(0, Directory.GetFiles(directory, "*.bak"));
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public async Task HelperReportsMismatchedReleaseBeforeTheOriginalCanClose()
    {
        string directory = NewDirectory();
        try
        {
            string target = Path.Combine(directory, "ModularAudience.Forms.exe");
            string staged = AtomicUpdateReplacement.CreateStagedPath(target);
            File.WriteAllText(target, "original executable");
            File.WriteAllText(staged, "downloaded executable");
            string id = Guid.NewGuid().ToString("N");
            string pipeName = "ModularAudience.Update." + id;
            using var commit = new EventWaitHandle(false, EventResetMode.ManualReset,
                @"Local\ModularAudience.UpdateCommit." + id);
            using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var parent = Process.GetCurrentProcess();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            long current = ApplicationRelease.RunningReleaseNumber() ?? 0;
            long wrongRelease = current == long.MaxValue ? 1 : current + 1;
            string[] args =
            [
                "--install-github-update", pipeName, parent.Id.ToString(CultureInfo.InvariantCulture),
                parent.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture),
                target, staged, new string('a', 64), new string('b', 64),
                wrongRelease.ToString(CultureInfo.InvariantCulture), Path.Combine(directory, "update-settings.json")
            ];
            Task helper = Task.Run(() => UpdateInstaller.RunHelper(args));
            await pipe.WaitForConnectionAsync(timeout.Token);
            using var reader = new StreamReader(pipe);
            string? reply = await reader.ReadLineAsync(timeout.Token);
            Assert.IsNotNull(reply);
            StringAssert.StartsWith(reply, "ERROR ");
            StringAssert.Contains(reply, "expected release number");
            await helper.WaitAsync(timeout.Token);
            Assert.AreEqual("original executable", File.ReadAllText(target));
            Assert.AreEqual("downloaded executable", File.ReadAllText(staged));
        }
        finally { Directory.Delete(directory, true); }
    }

    [STATestMethod]
    public void ClosingCheckedUpdateDialogPersistsDontAskAgain()
    {
        string directory = NewDirectory();
        try
        {
            var store = new ReleaseUpdateSettingsStore(Path.Combine(directory, "settings.json"));
            store.LoadAndRecordRunningRelease(127);
            using (var unchanged = new ReleaseUpdateDialog(127, ApplicationRelease.NewerThan(Release(128), 127)!,
                Path.Combine(directory, "ModularAudience.Forms.exe"), new string('a', 64), store))
            {
                unchanged.Show();
                unchanged.Close();
            }
            Assert.IsTrue(store.LoadAndRecordRunningRelease(127).AskToUpdate);
            using var dialog = new ReleaseUpdateDialog(127, ApplicationRelease.NewerThan(Release(128), 127)!,
                Path.Combine(directory, "ModularAudience.Forms.exe"), new string('a', 64), store);
            var check = (CheckBox)typeof(ReleaseUpdateDialog)
                .GetField("dontAskCheckBox", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
            dialog.Show();
            check.Checked = true;
            dialog.Close();
            Assert.IsFalse(store.LoadAndRecordRunningRelease(127).AskToUpdate);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static GitHubRelease Release(long number)
    {
        string tag = $"release-{number}";
        string url = $"https://github.com/{ApplicationRelease.Repository}/releases/download/{tag}/{ApplicationRelease.AssetName}";
        return new GitHubRelease(tag, [new ReleaseAsset(ApplicationRelease.AssetName, url, "sha256:" + new string('a', 64), 100)]);
    }

    private static string NewDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ModularAudience-update-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
