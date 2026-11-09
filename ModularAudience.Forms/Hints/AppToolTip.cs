using System.ComponentModel;

namespace ModularAudience.Forms.Hints;

public sealed class AppToolTip : System.Windows.Forms.ToolTip
{
    private readonly List<WeakReference<Control>> targets = [];
    private bool parameterHelp, alwaysAvailable, disposed;

    [DefaultValue(false)]
    public bool IsParameterHelp
    {
        get => this.parameterHelp;
        set { this.parameterHelp = value; this.ApplyHintPolicy(); }
    }

    [DefaultValue(false)]
    public bool AlwaysAvailable
    {
        get => this.alwaysAvailable;
        set { this.alwaysAvailable = value; this.ApplyHintPolicy(); }
    }

    public AppToolTip()
    {
        this.Popup += (_, e) => { if (!this.CanShow) e.Cancel = true; };
        HintPolicy.Register(this);
    }

    public AppToolTip(IContainer container) : this() => container.Add(this);
    private bool CanShow => !this.disposed && (this.IsParameterHelp || this.AlwaysAvailable || HintPolicy.Enabled);

    public new void SetToolTip(Control control, string? caption)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (this.IsParameterHelp && control is NumericUpDown number && !string.IsNullOrWhiteSpace(caption))
            caption += $"\nAllowed range: {number.Minimum} to {number.Maximum}; step: {number.Increment}.";
        if (!this.targets.Any(reference => reference.TryGetTarget(out var target) && ReferenceEquals(target, control)))
            this.targets.Add(new(control));
        base.SetToolTip(control, caption);
        if (this.IsParameterHelp && control is not Label && control.Parent is { } parent)
        {
            int index = parent.Controls.IndexOf(control);
            if (index > 0 && parent.Controls[index - 1] is Label label)
            {
                base.SetToolTip(label, caption);
                if (!this.targets.Any(reference => reference.TryGetTarget(out var target) && ReferenceEquals(target, label))) this.targets.Add(new(label));
            }
        }
    }

    internal void ApplyHintPolicy()
    {
        if (this.disposed) return;
        var owner = this.targets.Select(reference => reference.TryGetTarget(out var target) ? target : null)
            .FirstOrDefault(control => control is { IsDisposed: false, IsHandleCreated: true });
        if (owner?.InvokeRequired == true)
        {
            try { owner.BeginInvoke((Action)this.ApplyHintPolicy); } catch (InvalidOperationException) { }
            return;
        }
        this.Active = this.CanShow;
        if (!this.CanShow)
            foreach (var reference in this.targets)
                if (reference.TryGetTarget(out var control) && !control.IsDisposed) base.Hide(control);
        this.targets.RemoveAll(reference => !reference.TryGetTarget(out var control) || control.IsDisposed);
    }

    private bool Prepare(IWin32Window window)
    {
        if (!this.CanShow || !this.Active) return false;
        if (window is Control control && !this.targets.Any(reference => reference.TryGetTarget(out var target) && ReferenceEquals(target, control)))
            this.targets.Add(new(control));
        return true;
    }

    // ToolTip.Active alone does not reliably suppress programmatic Show calls.
    public new void Show(string? text, IWin32Window window) { if (this.Prepare(window)) base.Show(text, window); }
    public new void Show(string? text, IWin32Window window, int duration) { if (this.Prepare(window)) base.Show(text, window, duration); }
    public new void Show(string? text, IWin32Window window, Point point) { if (this.Prepare(window)) base.Show(text, window, point); }
    public new void Show(string? text, IWin32Window window, Point point, int duration) { if (this.Prepare(window)) base.Show(text, window, point, duration); }
    public new void Show(string? text, IWin32Window window, int x, int y) { if (this.Prepare(window)) base.Show(text, window, x, y); }
    public new void Show(string? text, IWin32Window window, int x, int y, int duration) { if (this.Prepare(window)) base.Show(text, window, x, y, duration); }

    protected override void Dispose(bool disposing)
    {
        this.disposed = true;
        if (disposing) this.targets.Clear();
        base.Dispose(disposing);
    }
}
