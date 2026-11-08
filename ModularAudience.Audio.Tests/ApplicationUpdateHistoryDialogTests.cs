using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Forms.Updates;
using ModularAudience.Ytdlp;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class ApplicationUpdateHistoryDialogTests
{
    [STATestMethod]
    public void HistoryControl_IsReadOnlyMultilineScrollableAndDoesNotLoadBeforeShown()
    {
        using var files = new DialogFiles();
        int loads = 0;
        using ReleaseUpdateDialog dialog = files.CreateDialog(_ => { loads++; return Task.FromResult(History()); });
        TextBox history = Field<TextBox>(dialog, "historyTextBox");

        Assert.IsTrue(history.ReadOnly);
        Assert.IsTrue(history.Multiline);
        Assert.AreEqual(ScrollBars.Both, history.ScrollBars);
        Assert.IsFalse(history.WordWrap);
        Assert.AreEqual(0, loads);
    }

    [STATestMethod]
    public void History_LoadsOldestFirstAndIncludesPreviouslySkippedIntermediateVersions()
    {
        using var files = new DialogFiles();
        files.Store.SkipThroughRelease(129);
        using ReleaseUpdateDialog dialog = files.CreateDialog(_ => Task.FromResult(History()));
        try
        {
            dialog.Show();
            Application.DoEvents();
            string text = Field<TextBox>(dialog, "historyTextBox").Text;

            Assert.IsTrue(text.IndexOf("Build 128", StringComparison.Ordinal) < text.IndexOf("Build 129", StringComparison.Ordinal));
            Assert.IsTrue(text.IndexOf("Build 129", StringComparison.Ordinal) < text.IndexOf("Build 130", StringComparison.Ordinal));
            StringAssert.Contains(text, "First change");
            StringAssert.Contains(text, "Second change");
            StringAssert.Contains(text, "Third change");
            Assert.IsTrue(Field<Button>(dialog, "updateButton").Enabled);
            Assert.IsTrue(Field<Button>(dialog, "skipButton").Enabled);
        }
        finally { dialog.Close(); }
    }

    [STATestMethod]
    public void GitHubFailure_IsInlineAndDoesNotDisableUpdateOrSkip()
    {
        using var files = new DialogFiles();
        using ReleaseUpdateDialog dialog = files.CreateDialog(_ => Task.FromException<ReleaseCommitHistory>(new HttpRequestException("GitHub rate limit")));
        try
        {
            dialog.Show();
            Application.DoEvents();

            StringAssert.Contains(Field<TextBox>(dialog, "historyTextBox").Text, "could not be loaded");
            StringAssert.Contains(Field<TextBox>(dialog, "historyTextBox").Text, "GitHub rate limit");
            Assert.IsTrue(Field<Button>(dialog, "updateButton").Enabled);
            Assert.IsTrue(Field<Button>(dialog, "skipButton").Enabled);
        }
        finally { dialog.Close(); }
    }

    [STATestMethod]
    public void ClosingDialog_CancelsPendingMetadataRequest()
    {
        using var files = new DialogFiles();
        CancellationToken observed = default;
        using ReleaseUpdateDialog dialog = files.CreateDialog(async token =>
        {
            observed = token;
            await Task.Delay(Timeout.Infinite, token);
            return History();
        });

        dialog.Show();
        Application.DoEvents();
        Assert.IsTrue(observed.CanBeCanceled);
        dialog.Close();
        Application.DoEvents();

        Assert.IsTrue(observed.IsCancellationRequested);
    }

    [STATestMethod]
    public void MetadataTimeout_DoesNotBlockUpdating()
    {
        using var files = new DialogFiles();
        using ReleaseUpdateDialog dialog = files.CreateDialog(_ => Task.FromException<ReleaseCommitHistory>(new OperationCanceledException()));
        try
        {
            dialog.Show();
            Application.DoEvents();

            StringAssert.Contains(Field<TextBox>(dialog, "historyTextBox").Text, "timed out");
            Assert.IsTrue(Field<Button>(dialog, "updateButton").Enabled);
        }
        finally { dialog.Close(); }
    }

    private static ReleaseCommitHistory History() => new(
        [new(130, null, "Third change", null), new(128, null, "First change", null), new(129, null, "Second change", null)], []);

    private static T Field<T>(ReleaseUpdateDialog dialog, string name) => (T)typeof(ReleaseUpdateDialog)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;

    private sealed class DialogFiles : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "ModularAudience-update-history-test-" + Guid.NewGuid().ToString("N"));
        internal ReleaseUpdateSettingsStore Store { get; }

        internal DialogFiles()
        {
            Directory.CreateDirectory(this.directory);
            this.Store = new ReleaseUpdateSettingsStore(Path.Combine(this.directory, "update-settings.json"));
            this.Store.LoadAndRecordRunningRelease(127);
        }

        internal ReleaseUpdateDialog CreateDialog(Func<CancellationToken, Task<ReleaseCommitHistory>> load)
        {
            var asset = new ReleaseAsset(ApplicationRelease.AssetName, "https://github.com/unused.exe", null, 100);
            return new ReleaseUpdateDialog(127, new ApplicationRelease(130, asset, [asset]),
                Path.Combine(this.directory, ApplicationRelease.AssetName), new string('a', 64), this.Store, load);
        }

        public void Dispose() => Directory.Delete(this.directory, recursive: true);
    }
}
