using System.Runtime.InteropServices;
using System.Text;

namespace ModularAudience.Forms.Modules.Dialogs;

/// <summary>
/// Group-local modality without ShowDialog's thread-wide window disabling.
/// The root's client controls are blocked, never its title bar/minimize button.
/// </summary>
internal sealed class MixWindowGroup : IDisposable
{
    private sealed class Child(Form form, Form owner)
    {
        internal readonly Form Form = form, Owner = owner;
        internal readonly bool WasEnabled = form.Enabled;
        internal FormWindowState RestoreState = form.WindowState;
        internal bool ShowOnRestore = true;
    }
    private readonly Form root;
    private readonly List<Child> children = [];
    private readonly Dictionary<Control, bool> rootEnabled = [];
    private readonly Dictionary<Form, (uint Thread, HashSet<IntPtr> Hidden)> native = [];
    private readonly System.Windows.Forms.Timer nativeWindows = new() { Interval = 40 };
    private bool minimized, disposed;

    internal MixWindowGroup(Form root)
    {
        this.root = root; root.Resize += this.RootResized; root.ControlAdded += this.RootControlAdded;
        this.nativeWindows.Tick += (_, _) => this.SynchronizeNativeWindows();
    }
    internal bool IsBlocked => this.children.Any(child => !child.Form.IsDisposed);
    internal IReadOnlyList<Form> Windows => this.children.Select(child => child.Form).ToArray();

    internal void Show(Form form, Form? parent = null)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        if (this.children.Any(child => ReferenceEquals(child.Form, form))) { form.Activate(); return; }
        Form owner = parent is not null && this.children.Any(child => child.Form == parent) ? parent : this.root;
        var entry = new Child(form, owner); this.children.Add(entry);
        form.ShowInTaskbar = false; form.MinimizeBox = false; form.Owner = owner;
        form.FormClosed += (_, _) => this.Remove(form); form.Disposed += (_, _) => this.Remove(form);
        this.ApplyBlocking();
        if (this.root.WindowState == FormWindowState.Minimized) { this.minimized = true; entry.ShowOnRestore = true; }
        else form.Show(owner);
    }

    internal Task<DialogResult> ShowAsync(Form form, Form? parent = null)
    {
        var completion = new TaskCompletionSource<DialogResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Complete() => completion.TrySetResult(form.DialogResult == DialogResult.None ? DialogResult.Cancel : form.DialogResult);
        form.FormClosed += (_, _) => Complete(); form.Disposed += (_, _) => Complete();
        foreach (Button button in Descendants(form).OfType<Button>().Where(button => button.DialogResult != DialogResult.None))
            button.Click += (_, _) => { if (!form.IsDisposed) { form.DialogResult = button.DialogResult; form.Close(); } };
        this.Show(form, parent); return completion.Task;
    }

    internal async Task<DialogResult> AskAsync(string text, string title, MessageBoxButtons choices, Form? parent = null)
    {
        using var form = new Form { Name = "MixQuestionDialog", Text = title, ClientSize = new Size(560, 190),
            MinimumSize = new Size(400, 180), StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false };
        var label = new Label { Dock = DockStyle.Fill, Padding = new Padding(14), Text = text };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        DialogResult[] results = choices switch
        {
            MessageBoxButtons.YesNoCancel => [DialogResult.Cancel, DialogResult.No, DialogResult.Yes],
            MessageBoxButtons.YesNo => [DialogResult.No, DialogResult.Yes],
            MessageBoxButtons.OKCancel => [DialogResult.Cancel, DialogResult.OK], _ => [DialogResult.OK]
        };
        foreach (DialogResult result in results)
        {
            var button = new Button { Text = result.ToString(), DialogResult = result, AutoSize = true }; buttons.Controls.Add(button);
            if (result == DialogResult.Cancel) form.CancelButton = button;
            if (result is DialogResult.Yes or DialogResult.OK) form.AcceptButton = button;
        }
        form.Controls.Add(label); form.Controls.Add(buttons); return await this.ShowAsync(form, parent);
    }

    /// <summary>
    /// Native file/color dialogs run on their own STA with a mix-owned proxy HWND.
    /// Their native modal loop can disable the proxy, not the application's UI thread
    /// or the mix title bar. The proxy also ties their lifetime/minimize state to this group.
    /// </summary>
    internal async Task<T> ShowNativeAsync<T>(string title, Func<IWin32Window, T> action, Form? parent = null)
    {
        using var host = new Form { Name = "MixNativeDialogHost", Text = title, ClientSize = new Size(360, 70),
            StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false };
        host.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, Padding = new Padding(12) });
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellation = new CancellationTokenSource(); CancellationToken token = cancellation.Token;
        int started = 0;
        host.Shown += (_, _) =>
        {
            if (Interlocked.Exchange(ref started, 1) != 0) return;
            IntPtr handle = host.Handle;
            var thread = new Thread(() =>
            {
                uint id = GetCurrentThreadId();
                using CancellationTokenRegistration registration = token.Register(() => CloseNativeThreadWindows(id));
                try
                {
                    token.ThrowIfCancellationRequested();
                    this.root.BeginInvoke((Action)(() =>
                    {
                        if (!this.disposed && !host.IsDisposed) { this.native[host] = (id, []); this.nativeWindows.Start(); this.SynchronizeNativeWindows(); }
                    }));
                    T result = action(new WindowHandle(handle));
                    if (token.IsCancellationRequested) completion.TrySetCanceled(token); else completion.TrySetResult(result);
                }
                catch (OperationCanceledException) { completion.TrySetCanceled(token); }
                catch (Exception error) { completion.TrySetException(error); }
                finally
                {
                    if (!this.root.IsDisposed && this.root.IsHandleCreated)
                        try { this.root.BeginInvoke((Action)(() => { this.native.Remove(host); if (!host.IsDisposed) host.Close(); })); } catch (InvalidOperationException) { }
                    cancellation.Dispose();
                }
            }) { IsBackground = true, Name = "Mix native dialog" };
            thread.SetApartmentState(ApartmentState.STA); thread.Start();
        };
        int hostClosed = 0;
        void OnHostClosed()
        {
            if (Interlocked.Exchange(ref hostClosed, 1) != 0) return;
            if (!completion.Task.IsCompleted)
            {
                try { cancellation.Cancel(); } catch (ObjectDisposedException) { }
                // Once started, let the native loop return before completing the
                // task: callers may dispose their FileDialog immediately after
                // awaiting it, and must not race an active native event sink.
                if (Volatile.Read(ref started) == 0) completion.TrySetCanceled(token);
            }
            if (Volatile.Read(ref started) == 0) cancellation.Dispose();
        }
        host.FormClosed += (_, _) => OnHostClosed(); host.Disposed += (_, _) => OnHostClosed();
        this.Show(host, parent);
        try { return await completion.Task; }
        finally { if (!host.IsDisposed) host.Close(); this.native.Remove(host); }
    }

    internal void SynchronizeWindowState() => this.RootResized(this.root, EventArgs.Empty);
    private void RootResized(object? sender, EventArgs e)
    {
        if (this.disposed) return;
        bool minimizing = this.root.WindowState == FormWindowState.Minimized;
        if (minimizing == this.minimized) return;
        this.minimized = minimizing;
        if (minimizing)
        {
            // Snapshot every child before hiding any owner: hiding a parent can
            // automatically change a nested child's visibility as well.
            foreach (Child child in this.children)
            {
                child.ShowOnRestore = child.Form.Visible; child.RestoreState = child.Form.WindowState;
            }
            foreach (var value in this.native.Values)
                EnumThreadWindows(value.Thread, (handle, _) => { if (IsWindowVisible(handle) || IsNativeDialog(handle)) value.Hidden.Add(handle); return true; }, IntPtr.Zero);
            foreach (Child child in this.children.AsEnumerable().Reverse())
            {
                if (child.Form.IsHandleCreated) ShowOwnedPopups(child.Form.Handle, false);
                child.Form.Hide();
            }
            foreach (var value in this.native.Values)
                foreach (IntPtr handle in value.Hidden) ShowWindow(handle, 0);
        }
        else
        {
            foreach (Child child in this.children.ToArray())
                if (!child.Form.IsDisposed && child.ShowOnRestore)
                {
                    child.Form.WindowState = child.RestoreState; child.Form.Show(child.Owner);
                    ShowOwnedPopups(child.Form.Handle, true);
                }
            foreach (var value in this.native.Values) { foreach (IntPtr handle in value.Hidden) if (IsWindow(handle)) ShowWindow(handle, 5); value.Hidden.Clear(); }
            this.ApplyBlocking();
        }
    }
    private void RootControlAdded(object? sender, ControlEventArgs e)
    { if (this.IsBlocked && e.Control is not null) { this.rootEnabled[e.Control] = e.Control.Enabled; e.Control.Enabled = false; } }
    private void Remove(Form form)
    {
        this.children.RemoveAll(child => child.Form == form); this.native.Remove(form);
        if (!this.disposed) this.ApplyBlocking();
    }
    private void ApplyBlocking()
    {
        Child? active = this.children.LastOrDefault(child => !child.Form.IsDisposed);
        if (active is not null)
        {
            foreach (Control control in this.root.Controls)
            { this.rootEnabled.TryAdd(control, control.Enabled); control.Enabled = false; }
            foreach (Child child in this.children) if (!child.Form.IsDisposed) child.Form.Enabled = child == active && child.WasEnabled;
        }
        else
        {
            foreach (var pair in this.rootEnabled) if (!pair.Key.IsDisposed) pair.Key.Enabled = pair.Value;
            this.rootEnabled.Clear();
        }
        this.SynchronizeNativeWindows();
    }
    private void SynchronizeNativeWindows()
    {
        if (this.disposed) return;
        if (this.native.Count == 0) { this.nativeWindows.Stop(); return; }
        Form? active = this.children.LastOrDefault()?.Form;
        foreach (var pair in this.native.ToArray())
            EnumThreadWindows(pair.Value.Thread, (handle, _) =>
            {
                if (this.minimized && (IsWindowVisible(handle) || IsNativeDialog(handle))) { pair.Value.Hidden.Add(handle); ShowWindow(handle, 0); }
                EnableWindow(handle, active == pair.Key);
                return true;
            }, IntPtr.Zero);
    }
    private static IEnumerable<Control> Descendants(Control root)
    { foreach (Control control in root.Controls) { yield return control; foreach (Control child in Descendants(control)) yield return child; } }
    public void Dispose()
    {
        if (this.disposed) return; this.disposed = true;
        this.root.Resize -= this.RootResized; this.root.ControlAdded -= this.RootControlAdded;
        this.nativeWindows.Dispose();
        foreach (var value in this.native.Values.ToArray()) CloseNativeThreadWindows(value.Thread);
        foreach (Child child in this.children.ToArray()) { child.Form.Close(); if (!child.Form.IsDisposed) child.Form.Dispose(); }
        this.children.Clear(); this.native.Clear();
        foreach (var pair in this.rootEnabled) if (!pair.Key.IsDisposed) pair.Key.Enabled = pair.Value;
        this.rootEnabled.Clear();
    }
    private sealed class WindowHandle(IntPtr handle) : IWin32Window { public IntPtr Handle => handle; }
    private static bool IsNativeDialog(IntPtr handle)
    {
        var name = new StringBuilder(128); GetClassName(handle, name, name.Capacity); return name.ToString() == "#32770";
    }
    private static void CloseNativeThreadWindows(uint id) => EnumThreadWindows(id, (handle, _) =>
    {
        PostMessage(handle, 0x0010, IntPtr.Zero, IntPtr.Zero);
        if (IsNativeDialog(handle)) PostMessage(handle, 0x0111, new IntPtr(2), IntPtr.Zero); // IDCANCEL
        return true;
    }, IntPtr.Zero);
    private delegate bool EnumWindowProc(IntPtr handle, IntPtr value);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumThreadWindows(uint id, EnumWindowProc callback, IntPtr value);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShowOwnedPopups(IntPtr owner, [MarshalAs(UnmanagedType.Bool)] bool show);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShowWindow(IntPtr handle, int command);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(IntPtr handle);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnableWindow(IntPtr handle, [MarshalAs(UnmanagedType.Bool)] bool enable);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr handle, StringBuilder name, int maximum);
}
