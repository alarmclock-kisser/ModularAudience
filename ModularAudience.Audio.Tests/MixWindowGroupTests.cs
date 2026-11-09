using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Forms.Modules.Dialogs;

namespace ModularAudience.Audio.Tests;

[TestClass]
[DoNotParallelize]
public sealed class MixWindowGroupTests
{
    [STATestMethod]
    public void GroupModalDialogBlocksOnlyItsMixClientAndSiblings_NotOtherApplicationWindowsOrRootTitleBar()
    {
        using var context = new WinFormsTestContext(); using var root = Root("Mix"); using var other = Root("Other collection");
        using var group = new MixWindowGroup(root); using var dialog = Root("Effect", false);
        var accept = new Button { DialogResult = DialogResult.OK, Text = "Apply" }; dialog.Controls.Add(accept);
        Task<DialogResult> answer = group.ShowAsync(dialog);
        Assert.IsFalse(dialog.Modal); Assert.AreSame(root, dialog.Owner); Assert.IsTrue(group.IsBlocked);
        Assert.IsFalse(root.Controls[0].Enabled); Assert.IsTrue(root.Enabled); Assert.IsTrue(IsWindowEnabled(root.Handle));
        Assert.IsTrue(other.Enabled); Assert.IsTrue(other.Controls[0].Enabled); Assert.IsTrue(IsWindowEnabled(other.Handle));
        ((TextBox)other.Controls[0]).Text = "still interactive"; Assert.AreEqual("still interactive", other.Controls[0].Text);
        accept.PerformClick(); Drain(answer); Assert.AreEqual(DialogResult.OK, answer.Result);
        Assert.IsFalse(group.IsBlocked); Assert.IsTrue(root.Controls[0].Enabled);
    }

    [STATestMethod]
    public void NestedDialogsDisableOnlyLowerGroupWindowsAndRestoreTheirPreviousState()
    {
        using var context = new WinFormsTestContext(); using var root = Root("Mix"); using var unrelated = Root("Unrelated mix");
        using var group = new MixWindowGroup(root); using var first = Root("Options", false); using var second = Root("Preset dialog", false);
        group.Show(first); group.Show(second, first);
        Assert.IsFalse(first.Enabled); Assert.IsTrue(second.Enabled); Assert.AreSame(first, second.Owner);
        Assert.IsTrue(unrelated.Enabled); Assert.IsTrue(unrelated.Controls[0].Enabled);
        second.Close(); Assert.IsTrue(first.Enabled); Assert.IsTrue(group.IsBlocked);
        first.Close(); Assert.IsFalse(group.IsBlocked); Assert.IsTrue(root.Controls[0].Enabled);
    }

    [STATestMethod]
    public void MinimizeAndRestoreIncludesAllOwnedDialogsWhileTheRootRemainsEnabled()
    {
        using var context = new WinFormsTestContext(); using var root = Root("Mix"); using var other = Root("Other window");
        using var group = new MixWindowGroup(root); using var first = Root("Effect", false); using var second = Root("Nested", false);
        group.Show(first); group.Show(second, first);
        root.WindowState = FormWindowState.Minimized; group.SynchronizeWindowState();
        Assert.IsFalse(first.Visible); Assert.IsFalse(second.Visible); Assert.IsTrue(other.Visible); Assert.IsTrue(other.Enabled);
        Assert.IsTrue(root.Enabled); Assert.IsTrue(IsWindowEnabled(root.Handle));
        root.WindowState = FormWindowState.Normal; group.SynchronizeWindowState();
        Assert.IsTrue(first.Visible); Assert.IsTrue(second.Visible); Assert.IsFalse(first.Enabled); Assert.IsTrue(second.Enabled);
    }

    [STATestMethod]
    public void DialogOpenedDuringMinimizationWaitsForRestore_AndClosingTheGroupCancelsAwaiters()
    {
        using var context = new WinFormsTestContext(); using var root = Root("Mix"); using var group = new MixWindowGroup(root); using var dialog = Root("Pending", false);
        dialog.Hide(); root.WindowState = FormWindowState.Minimized;
        Task<DialogResult> answer = group.ShowAsync(dialog); Assert.IsFalse(dialog.Visible); Assert.IsFalse(answer.IsCompleted);
        root.WindowState = FormWindowState.Normal; group.SynchronizeWindowState(); Assert.IsTrue(dialog.Visible);
        group.Dispose(); Drain(answer); Assert.AreEqual(DialogResult.Cancel, answer.Result); Assert.IsTrue(dialog.IsDisposed);
    }

    [STATestMethod]
    public void NativeDialogCancelledBeforeFirstShowDoesNotLeakItsAwaiterOrStartAWorker()
    {
        using var context = new WinFormsTestContext(); using var root = Root("Mix"); using var group = new MixWindowGroup(root);
        root.WindowState = FormWindowState.Minimized; group.SynchronizeWindowState(); int started = 0;
        Task<int> work = group.ShowNativeAsync("Pending native dialog", _ => { Interlocked.Increment(ref started); return 1; });
        Assert.IsFalse(work.IsCompleted); group.Dispose(); WaitUntil(() => work.IsCompleted);
        Assert.IsTrue(work.IsCanceled); Assert.AreEqual(0, started);
    }

    [STATestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void NativeModalWindowRunsOnSeparateStaDoesNotDisableOtherWindowsAndFollowsMinimize(bool filePicker, bool closeGroup)
    {
        using var context = new WinFormsTestContext(); using var root = Root("Mix"); using var other = Root("Other window");
        using var group = new MixWindowGroup(root);
        int uiThread = Environment.CurrentManagedThreadId, worker = 0; uint nativeThread = 0;
        ApartmentState apartment = ApartmentState.Unknown; IntPtr nativeOwner = IntPtr.Zero, popup = IntPtr.Zero;
        Task<DialogResult> work = group.ShowNativeAsync("Native dialog host", owner =>
        {
            worker = Environment.CurrentManagedThreadId; apartment = Thread.CurrentThread.GetApartmentState(); nativeOwner = owner.Handle;
            Volatile.Write(ref nativeThread, GetCurrentThreadId());
            if (filePicker)
            {
                using var dialog = new OpenFileDialog { Filter = "Mix projects|*.mamix", InitialDirectory = Path.GetTempPath(), CheckFileExists = true };
                return dialog.ShowDialog(owner);
            }
            using var color = new ColorDialog { FullOpen = true }; return color.ShowDialog(owner);
        });
        try
        {
            WaitUntil(() =>
            {
                uint id = Volatile.Read(ref nativeThread); if (id == 0) return false;
                EnumThreadWindows(id, (handle, _) =>
                {
                    var name = new StringBuilder(128); GetClassName(handle, name, name.Capacity);
                    if (name.ToString() == "#32770" && IsWindowVisible(handle) && GetDlgItem(handle, 2) != IntPtr.Zero) popup = handle;
                    return true;
                }, IntPtr.Zero);
                return popup != IntPtr.Zero && IsWindowVisible(popup);
            });
            Assert.AreNotEqual(uiThread, worker); Assert.AreEqual(ApartmentState.STA, apartment);
            Assert.AreNotEqual(root.Handle, nativeOwner); Assert.IsTrue(IsWindowEnabled(root.Handle)); Assert.IsTrue(IsWindowEnabled(other.Handle));
            root.WindowState = FormWindowState.Minimized; group.SynchronizeWindowState(); WaitUntil(() => !IsWindowVisible(popup));
            root.WindowState = FormWindowState.Normal; group.SynchronizeWindowState(); WaitUntil(() => IsWindowVisible(popup));
            Assert.IsTrue(IsWindowEnabled(root.Handle)); Assert.IsTrue(IsWindowEnabled(other.Handle));
            Assert.IsTrue(IsWindowEnabled(popup));
            if (closeGroup)
            {
                group.Dispose(); WaitUntil(() => work.IsCompleted); Assert.IsTrue(work.IsCanceled);
                WaitUntil(() => !IsWindow(popup)); Assert.IsTrue(other.Enabled); return;
            }
            IntPtr cancel = GetDlgItem(popup, 2); Assert.AreNotEqual(IntPtr.Zero, cancel);
            PostMessage(cancel, 0x00F5, IntPtr.Zero, IntPtr.Zero); // BM_CLICK, scoped to this test's Cancel button.
            Drain(work); Assert.AreEqual(DialogResult.Cancel, work.Result); Assert.IsFalse(group.IsBlocked);
        }
        finally { if (popup != IntPtr.Zero && IsWindow(popup)) PostMessage(popup, 0x0010, IntPtr.Zero, IntPtr.Zero); group.Dispose(); }
    }

    private static Form Root(string title, bool show = true)
    {
        var form = new Form { Text = title, Size = new Size(420, 220), MinimizeBox = true }; form.Controls.Add(new TextBox { Text = title }); if (show) form.Show(); return form;
    }
    private static void Drain(Task task)
    {
        WaitUntil(() => task.IsCompleted); task.GetAwaiter().GetResult();
        SynchronizationContext? context = SynchronizationContext.Current; Application.DoEvents(); SynchronizationContext.SetSynchronizationContext(context);
    }
    private static void WaitUntil(Func<bool> done)
    {
        SynchronizationContext? context = SynchronizationContext.Current; var watch = Stopwatch.StartNew();
        while (!done() && watch.Elapsed < TimeSpan.FromSeconds(10)) { Application.DoEvents(); SynchronizationContext.SetSynchronizationContext(context); Thread.Sleep(1); }
        Assert.IsTrue(done(), "Window operation must complete without blocking the application's UI thread.");
    }
    private delegate bool EnumWindowProc(IntPtr handle, IntPtr value);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumThreadWindows(uint id, EnumWindowProc callback, IntPtr value);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowEnabled(IntPtr handle);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(IntPtr handle);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr GetDlgItem(IntPtr owner, int id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr handle, StringBuilder name, int maximum);
}
