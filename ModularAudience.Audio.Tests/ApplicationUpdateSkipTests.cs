using System.Reflection;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Forms;
using ModularAudience.Forms.Updates;
using ModularAudience.Ytdlp;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class ApplicationUpdateSkipTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void MissingSkipPreference_DefaultsToNullForNewAndLegacySettings(bool legacyFile)
    {
        using var files = new SettingsFiles();
        if (legacyFile) File.WriteAllText(files.Path, """{"AskToUpdate":true,"CurrentReleaseNumber":127}""");

        ReleaseUpdateSettings settings = files.Store.LoadAndRecordRunningRelease(127);

        Assert.IsNull(settings.SkipReleaseNumber);
        Assert.IsTrue(settings.AskToUpdate);
    }

    [TestMethod]
    public void Skip_PersistsInclusiveReleaseBoundaryWithoutDisablingChecks()
    {
        using var files = new SettingsFiles();
        files.Store.LoadAndRecordRunningRelease(127);

        files.Store.SkipThroughRelease(128);

        ReleaseUpdateSettings settings = new ReleaseUpdateSettingsStore(files.Path).LoadAndRecordRunningRelease(null);
        Assert.AreEqual(128L, settings.SkipReleaseNumber);
        Assert.AreEqual(127L, settings.CurrentReleaseNumber);
        Assert.IsTrue(settings.AskToUpdate);
        Assert.IsFalse(settings.ShouldOfferRelease(128));
        Assert.IsTrue(settings.ShouldOfferRelease(129));
        using var json = JsonDocument.Parse(File.ReadAllText(files.Path));
        Assert.AreEqual(128L, json.RootElement.GetProperty("SkipReleaseNumber").GetInt64());
    }

    [TestMethod]
    public void OlderDialog_CannotLowerSkipBoundarySavedByAnotherInstance()
    {
        using var files = new SettingsFiles();
        files.Store.LoadAndRecordRunningRelease(127);
        files.Store.SkipThroughRelease(130);

        new ReleaseUpdateSettingsStore(files.Path).SkipThroughRelease(128);

        Assert.AreEqual(130L, files.Store.LoadAndRecordRunningRelease(null).SkipReleaseNumber);
    }

    [TestMethod]
    [DataRow(true, false)]
    [DataRow(false, true)]
    public void ChangingAskToUpdate_ResetsSkipToNull(bool initial, bool requested)
    {
        using var files = new SettingsFiles();
        File.WriteAllText(files.Path, JsonSerializer.Serialize(new ReleaseUpdateSettings
        {
            AskToUpdate = initial, CurrentReleaseNumber = 127, SkipReleaseNumber = 128
        }));

        files.Store.SetAskToUpdate(requested);

        ReleaseUpdateSettings settings = files.Store.LoadAndRecordRunningRelease(null);
        Assert.AreEqual(requested, settings.AskToUpdate);
        Assert.AreEqual(127L, settings.CurrentReleaseNumber);
        Assert.IsNull(settings.SkipReleaseNumber);
        using var json = JsonDocument.Parse(File.ReadAllText(files.Path));
        Assert.AreEqual(JsonValueKind.Null, json.RootElement.GetProperty("SkipReleaseNumber").ValueKind);
    }

    [TestMethod]
    public void UnchangedAskSetting_DoesNotAccidentallyClearSkip()
    {
        using var files = new SettingsFiles();
        files.Store.LoadAndRecordRunningRelease(127);
        files.Store.SkipThroughRelease(128);

        files.Store.SetAskToUpdate(true);

        Assert.AreEqual(128L, files.Store.LoadAndRecordRunningRelease(null).SkipReleaseNumber);
    }

    [TestMethod]
    public void RecordingRunningOrInstalledRelease_PreservesSkip()
    {
        using var files = new SettingsFiles();
        files.Store.LoadAndRecordRunningRelease(127);
        files.Store.SkipThroughRelease(130);

        Assert.AreEqual(130L, files.Store.LoadAndRecordRunningRelease(128).SkipReleaseNumber);
        files.Store.RecordInstalledRelease(131);

        ReleaseUpdateSettings settings = files.Store.LoadAndRecordRunningRelease(null);
        Assert.AreEqual(131L, settings.CurrentReleaseNumber);
        Assert.AreEqual(130L, settings.SkipReleaseNumber);
    }

    [TestMethod]
    [DataRow(0L)]
    [DataRow(-1L)]
    public void InvalidPersistedSkip_IsRejectedWithoutOverwritingSettings(long invalidSkip)
    {
        using var files = new SettingsFiles();
        string json = JsonSerializer.Serialize(new ReleaseUpdateSettings { SkipReleaseNumber = invalidSkip });
        File.WriteAllText(files.Path, json);

        Assert.ThrowsExactly<InvalidDataException>(() => files.Store.LoadAndRecordRunningRelease(127));

        Assert.AreEqual(json, File.ReadAllText(files.Path));
    }

    [TestMethod]
    [DataRow(0L)]
    [DataRow(-1L)]
    public void SkipAction_RejectsInvalidReleaseNumbers(long invalidSkip)
    {
        using var files = new SettingsFiles();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => files.Store.SkipThroughRelease(invalidSkip));

        Assert.IsFalse(File.Exists(files.Path));
    }

    [TestMethod]
    [DataRow(127L, 126L, 128L, false)]
    [DataRow(125L, 127L, 128L, false)]
    [DataRow(127L, 128L, 128L, false)]
    [DataRow(127L, 129L, 128L, true)]
    [DataRow(129L, 129L, 128L, false)]
    [DataRow(127L, long.MaxValue, long.MaxValue, false)]
    public void StartupComparison_OffersOnlyReleasesAboveBothInstalledAndSkippedNumbers(
        long runningRelease, long latestRelease, long skippedRelease, bool expected)
    {
        ApplicationRelease? release = ApplicationRelease.NewerThan(Release(latestRelease), runningRelease, skippedRelease);

        Assert.AreEqual(expected, release is not null);
    }

    [TestMethod]
    public void SkippedRelease_DoesNotRequireAssetsOrDownloadPreparation()
    {
        Assert.IsNull(ApplicationRelease.NewerThan(new GitHubRelease("release-128", []), 127, 128));
    }

    [TestMethod]
    [DataRow(true, null, 128L, true)]
    [DataRow(false, null, 128L, false)]
    [DataRow(true, 128L, 127L, false)]
    [DataRow(true, 128L, 128L, false)]
    [DataRow(true, 128L, 129L, true)]
    [DataRow(false, 128L, 129L, false)]
    public void FinalPromptCheck_HonorsAskAndInclusiveSkipBoundary(bool ask, long? skip, long release, bool expected)
    {
        var settings = new ReleaseUpdateSettings { AskToUpdate = ask, SkipReleaseNumber = skip };

        Assert.AreEqual(expected, settings.ShouldOfferRelease(release));
    }

    [TestMethod]
    public void PromptRecheck_SeesReleaseSkippedByAnotherInstanceDuringStartup()
    {
        using var files = new SettingsFiles();
        ReleaseUpdateSettings initial = files.Store.LoadAndRecordRunningRelease(127);
        Assert.IsTrue(initial.ShouldOfferRelease(128));

        new ReleaseUpdateSettingsStore(files.Path).SkipThroughRelease(128);

        Assert.IsFalse(files.Store.LoadAndRecordRunningRelease(null).ShouldOfferRelease(128));
    }

    [STATestMethod]
    public void MenuOffThenOn_ResetsSkippedReleaseAndOffersItAgain()
    {
        using var files = new SettingsFiles();
        files.Store.LoadAndRecordRunningRelease(127);
        files.Store.SkipThroughRelease(128);
        using var item = new ToolStripMenuItem();
        WindowMain.RefreshUpdateChecks(item, files.Store);

        WindowMain.ToggleUpdateChecks(item, files.Store);
        Assert.IsNull(files.Store.LoadAndRecordRunningRelease(null).SkipReleaseNumber);
        Assert.IsFalse(item.Checked);
        WindowMain.ToggleUpdateChecks(item, files.Store);

        ReleaseUpdateSettings settings = files.Store.LoadAndRecordRunningRelease(null);
        Assert.IsTrue(item.Checked);
        Assert.IsTrue(settings.AskToUpdate);
        Assert.IsNull(settings.SkipReleaseNumber);
        Assert.IsTrue(settings.ShouldOfferRelease(128));
    }

    [STATestMethod]
    public void SkipButton_PersistsReleaseAndClosesDialogWithoutUpdating()
    {
        using var files = new SettingsFiles();
        files.Store.LoadAndRecordRunningRelease(127);
        using ReleaseUpdateDialog dialog = files.CreateDialog();
        Button skip = Control<Button>(dialog, "skipButton");
        dialog.Shown += (_, _) =>
        {
            skip.PerformClick();
            // A disabled/broken action must fail the assertion, not leave a
            // modal test window open indefinitely.
            if (dialog.DialogResult == DialogResult.None) dialog.Close();
        };

        DialogResult result = dialog.ShowDialog();

        Assert.AreEqual("Skip this", skip.Text);
        Assert.AreEqual(DialogResult.Ignore, result);
        ReleaseUpdateSettings settings = files.Store.LoadAndRecordRunningRelease(null);
        Assert.AreEqual(128L, settings.SkipReleaseNumber);
        Assert.IsTrue(settings.AskToUpdate);
        Assert.IsFalse(File.Exists(System.IO.Path.Combine(files.Directory, ApplicationRelease.AssetName)));
    }

    [STATestMethod]
    public void NotNow_DoesNotPersistSkipBoundary()
    {
        using var files = new SettingsFiles();
        files.Store.LoadAndRecordRunningRelease(127);
        using ReleaseUpdateDialog dialog = files.CreateDialog();
        dialog.Shown += (_, _) => Control<Button>(dialog, "laterButton").PerformClick();

        Assert.AreEqual(DialogResult.Cancel, dialog.ShowDialog());
        Assert.IsNull(files.Store.LoadAndRecordRunningRelease(null).SkipReleaseNumber);
    }

    [STATestMethod]
    public void GlobalOptOutAndSkip_AreMutuallyExclusiveChoices()
    {
        using var files = new SettingsFiles();
        files.Store.LoadAndRecordRunningRelease(127);
        using ReleaseUpdateDialog dialog = files.CreateDialog();
        Button skip = Control<Button>(dialog, "skipButton");
        CheckBox dontAsk = Control<CheckBox>(dialog, "dontAskCheckBox");

        dontAsk.Checked = true;
        Assert.IsFalse(skip.Enabled);
        skip.PerformClick();
        Assert.IsNull(files.Store.LoadAndRecordRunningRelease(null).SkipReleaseNumber);
        dontAsk.Checked = false;
        Assert.IsTrue(skip.Enabled);
    }

    [STATestMethod]
    public void DownloadInProgress_DisablesSkippingUntilFinished()
    {
        using var files = new SettingsFiles();
        files.Store.LoadAndRecordRunningRelease(127);
        using ReleaseUpdateDialog dialog = files.CreateDialog();
        using var download = new CancellationTokenSource();
        FieldInfo active = typeof(ReleaseUpdateDialog).GetField("activeDownload", BindingFlags.Instance | BindingFlags.NonPublic)!;
        MethodInfo state = typeof(ReleaseUpdateDialog).GetMethod("SetDownloading", BindingFlags.Instance | BindingFlags.NonPublic)!;
        try
        {
            active.SetValue(dialog, download);
            state.Invoke(dialog, [true]);
            Assert.IsFalse(Control<Button>(dialog, "skipButton").Enabled);
        }
        finally
        {
            active.SetValue(dialog, null);
        }

        state.Invoke(dialog, [false]);
        Assert.IsTrue(Control<Button>(dialog, "skipButton").Enabled);
    }

    private static T Control<T>(ReleaseUpdateDialog dialog, string field) => (T)typeof(ReleaseUpdateDialog)
        .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;

    private static GitHubRelease Release(long number)
    {
        string tag = $"release-{number}";
        string url = $"https://github.com/{ApplicationRelease.Repository}/releases/download/{tag}/{ApplicationRelease.AssetName}";
        return new GitHubRelease(tag, [new ReleaseAsset(ApplicationRelease.AssetName, url, "sha256:" + new string('a', 64), 100)]);
    }

    private sealed class SettingsFiles : IDisposable
    {
        internal string Directory { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ModularAudience-update-skip-test-" + Guid.NewGuid().ToString("N"));
        internal string Path { get; }
        internal ReleaseUpdateSettingsStore Store { get; }

        internal SettingsFiles()
        {
            System.IO.Directory.CreateDirectory(Directory);
            Path = System.IO.Path.Combine(Directory, "update-settings.json");
            Store = new ReleaseUpdateSettingsStore(Path);
        }

        internal ReleaseUpdateDialog CreateDialog() => new(127, ApplicationRelease.NewerThan(Release(128), 127)!,
            System.IO.Path.Combine(Directory, ApplicationRelease.AssetName), new string('a', 64), Store);

        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
