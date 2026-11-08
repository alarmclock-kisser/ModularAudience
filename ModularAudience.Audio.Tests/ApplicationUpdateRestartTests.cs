using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Forms.Updates;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class ApplicationUpdateRestartTests
{
    [TestMethod]
    public void VerifiedUpdate_ReplacesExecutableAndRecordsReleaseBeforeRestart()
    {
        using var files = new InstallationFiles();
        var store = new ReleaseUpdateSettingsStore(files.SettingsPath);
        store.LoadAndRecordRunningRelease(127);
        store.DisableAsking();
        int starts = 0;

        ApplicationUpdateInstallationResult result = files.Installation.Complete(path =>
        {
            starts++;
            Assert.AreEqual(files.TargetPath, path);
            Assert.AreEqual("updated executable", File.ReadAllText(path));
            Assert.IsFalse(File.Exists(files.StagedPath));
            ReleaseUpdateSettings settings = store.LoadAndRecordRunningRelease(null);
            Assert.AreEqual(128L, settings.CurrentReleaseNumber);
            Assert.IsFalse(settings.AskToUpdate);
        });

        Assert.AreEqual(1, starts);
        Assert.IsTrue(result.Restarted);
        Assert.HasCount(0, result.Warnings);
    }

    [TestMethod]
    public void FailedVerification_DoesNotRestartOrReplaceOriginal()
    {
        using var files = new InstallationFiles();
        File.WriteAllText(files.StagedPath, "corrupt download");
        bool restarted = false;

        Assert.ThrowsExactly<InvalidDataException>(() => files.Installation.Complete(_ => restarted = true));

        Assert.IsFalse(restarted);
        Assert.AreEqual("original executable", File.ReadAllText(files.TargetPath));
    }

    [TestMethod]
    public void SettingsWarning_DoesNotPreventAutomaticRestart()
    {
        using var files = new InstallationFiles();
        File.WriteAllText(files.SettingsPath, "invalid JSON");
        bool restarted = false;

        ApplicationUpdateInstallationResult result = files.Installation.Complete(_ => restarted = true);

        Assert.IsTrue(restarted);
        Assert.IsTrue(result.Restarted);
        Assert.HasCount(1, result.Warnings);
        Assert.AreEqual("updated executable", File.ReadAllText(files.TargetPath));
        Assert.AreEqual("invalid JSON", File.ReadAllText(files.SettingsPath));
    }

    [TestMethod]
    public void FailedRestart_ReportsFailureWithoutRollingBackInstalledUpdate()
    {
        using var files = new InstallationFiles();
        var failure = new Win32Exception(5, "Process creation was denied.");

        ApplicationUpdateInstallationResult result = files.Installation.Complete(_ => throw failure);

        Assert.IsFalse(result.Restarted);
        Assert.AreSame(failure, result.RestartError);
        Assert.AreEqual("updated executable", File.ReadAllText(files.TargetPath));
        Assert.IsFalse(File.Exists(files.StagedPath));
        Assert.AreEqual(128L, new ReleaseUpdateSettingsStore(files.SettingsPath)
            .LoadAndRecordRunningRelease(null).CurrentReleaseNumber);
    }

    [TestMethod]
    public void Launcher_StartsInstalledPathInItsOwnDirectoryWithoutHelperArguments()
    {
        using var files = new InstallationFiles();
        var starts = new List<ProcessStartInfo>();

        UpdatedApplicationLauncher.Start(files.TargetPath, starts.Add);

        ProcessStartInfo start = starts.Single();
        Assert.AreEqual(files.TargetPath, start.FileName);
        Assert.AreEqual(files.Directory, start.WorkingDirectory);
        Assert.IsFalse(start.UseShellExecute);
        Assert.AreEqual(string.Empty, start.Arguments);
        Assert.HasCount(0, start.ArgumentList);
    }

    [TestMethod]
    public void Launcher_FallsBackToWindowsShellOnlyWhenDirectStartFails()
    {
        using var files = new InstallationFiles();
        var starts = new List<ProcessStartInfo>();

        UpdatedApplicationLauncher.Start(files.TargetPath, start =>
        {
            starts.Add(start);
            if (!start.UseShellExecute) throw new Win32Exception(5, "Direct start was denied.");
        });

        Assert.HasCount(2, starts);
        Assert.IsFalse(starts[0].UseShellExecute);
        Assert.IsTrue(starts[1].UseShellExecute);
        Assert.IsTrue(starts.All(start => start.FileName == files.TargetPath && start.ArgumentList.Count == 0));
    }

    [TestMethod]
    public void Launcher_PreservesBothErrorsWhenNeitherStartMethodWorks()
    {
        using var files = new InstallationFiles();
        int attempts = 0;

        IOException error = Assert.ThrowsExactly<IOException>(() => UpdatedApplicationLauncher.Start(files.TargetPath, _ =>
        {
            attempts++;
            throw new Win32Exception(5, "Start was denied.");
        }));

        Assert.AreEqual(2, attempts);
        Assert.IsInstanceOfType<AggregateException>(error.InnerException);
        Assert.HasCount(2, ((AggregateException)error.InnerException).InnerExceptions);
    }

    [TestMethod]
    public void Launcher_RejectsRelativePathsWithoutStartingAnything()
    {
        bool started = false;

        Assert.ThrowsExactly<ArgumentException>(() => UpdatedApplicationLauncher.Start(
            "ModularAudience.Forms.exe", _ => started = true));

        Assert.IsFalse(started);
    }

    private sealed class InstallationFiles : IDisposable
    {
        internal string Directory { get; } = Path.Combine(Path.GetTempPath(), "ModularAudience-update-restart-test-" + Guid.NewGuid().ToString("N"));
        internal string TargetPath { get; }
        internal string StagedPath { get; }
        internal string SettingsPath { get; }
        internal ApplicationUpdateInstallation Installation { get; }

        internal InstallationFiles()
        {
            System.IO.Directory.CreateDirectory(Directory);
            TargetPath = Path.Combine(Directory, "Modular Audience updated.exe");
            StagedPath = AtomicUpdateReplacement.CreateStagedPath(TargetPath);
            SettingsPath = Path.Combine(Directory, "update-settings.json");
            File.WriteAllText(TargetPath, "original executable");
            File.WriteAllText(StagedPath, "updated executable");
            Installation = new ApplicationUpdateInstallation(TargetPath, StagedPath, Hash(TargetPath), Hash(StagedPath), 128, SettingsPath);
        }

        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);

        private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    }
}
