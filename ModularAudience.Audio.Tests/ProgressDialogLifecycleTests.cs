using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Forms.Modules.Dialogs;
using System.Reflection;

namespace ModularAudience.Audio.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ProgressDialogLifecycleTests
{
    [STATestMethod]
    public void MidiPreviewStopCancelsButLeavesSourceAliveForOwningRenderCleanup()
    {
        var track = new ModularAudience.Audio.Midi.MidiTrackData { Index = 0 };
        var midi = ModularAudience.Audio.Midi.MidiFileData.CreateGenerated([track], 960, 120);
        using var window = new MidiWindow(null, midi);
        using var source = new CancellationTokenSource();
        typeof(MidiWindow).GetField("previewCts", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, source);
        window.StopPreviewAsync().GetAwaiter().GetResult();
        Assert.IsTrue(source.IsCancellationRequested);
        // Token access throws if StopPreviewAsync incorrectly disposed the caller-owned CTS.
        Assert.IsTrue(source.Token.IsCancellationRequested);
        typeof(MidiWindow).GetField("previewCts", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, null);
    }

    [STATestMethod]
    public void DelayedNativeCloseAfterSourceDisposalDoesNotThrow()
    {
        var source = new CancellationTokenSource();
        using var dialog = new ProgressDialog(windowCloseDelay: 0.01, ct: source.Token, cancellationSource: source);
        dialog.Show();
        source.Dispose();
        typeof(Form).GetMethod("OnFormClosing", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(dialog, [new FormClosingEventArgs(CloseReason.UserClosing, false)]);
        dialog.Close();
        typeof(ProgressDialog).GetMethod("timer_close_Tick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(dialog, [null, EventArgs.Empty]);
        Assert.IsTrue(dialog.IsDisposed);
    }

    [STATestMethod]
    public void CompletionNeverCancelsSuccessfulOperationAndLateReportsAreIgnored()
    {
        using var source = new CancellationTokenSource();
        using var dialog = new ProgressDialog(windowCloseDelay: 0, ct: source.Token, cancellationSource: source);
        dialog.Show(); dialog.Complete(); dialog.Report(0.5); dialog.Close();
        Assert.IsFalse(source.IsCancellationRequested);
        Assert.IsTrue(dialog.IsDisposed);
    }

    [STATestMethod]
    public void WorkerCancellationQueuesUiCloseWithoutRecursiveCancellation()
    {
        using var source = new CancellationTokenSource();
        using var dialog = new ProgressDialog(windowCloseDelay: 0, ct: source.Token, cancellationSource: source);
        dialog.Show();
        Task.Run(() => source.Cancel()).GetAwaiter().GetResult();
        for (int i = 0; i < 20 && !dialog.IsDisposed; i++) Application.DoEvents();
        Assert.IsTrue(dialog.IsDisposed);
    }
}
