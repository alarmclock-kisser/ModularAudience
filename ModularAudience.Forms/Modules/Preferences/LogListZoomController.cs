using ModularAudience.Forms.Settings;

namespace ModularAudience.Forms;

internal sealed class LogListZoomController : IDisposable
{
    private readonly ListBox list;
    private readonly Font originalFont;
    private Font? ownedFont;

    internal LogListZoomController(ListBox list, float size)
    {
        this.list = list;
        this.originalFont = list.Font;
        this.Apply(size);
        this.list.MouseWheel += this.MouseWheel;
    }

    internal float Size => this.ownedFont?.SizeInPoints ?? ApplicationPreferences.DefaultLogFontSize;
    internal event EventHandler? Changed;

    internal void Apply(float size)
    {
        size = float.IsFinite(size) ? Math.Clamp(size, ApplicationPreferences.MinimumLogFontSize, ApplicationPreferences.MaximumLogFontSize)
            : ApplicationPreferences.DefaultLogFontSize;
        if (this.ownedFont is not null && Math.Abs(this.Size - size) < 0.01f) return;
        bool integral = this.list.IntegralHeight;
        Rectangle bounds = this.list.Bounds;
        Font? previous = this.ownedFont;
        var font = new Font("Consolas", size, FontStyle.Regular, GraphicsUnit.Point);
        this.ownedFont = font;
        this.list.IntegralHeight = false;
        this.list.Font = font;
        this.list.ItemHeight = Math.Max(10, (int)Math.Round(size * 1.7f));
        this.list.IntegralHeight = integral;
        this.list.Bounds = bounds;
        this.list.Invalidate();
        previous?.Dispose();
    }

    internal void Zoom(int delta)
    {
        float previous = this.Size;
        this.Apply(previous + Math.Sign(delta));
        if (Math.Abs(previous - this.Size) >= 0.01f) this.Changed?.Invoke(this, EventArgs.Empty);
    }

    private void MouseWheel(object? sender, MouseEventArgs e)
    {
        if ((Control.ModifierKeys & Keys.Control) == 0) return;
        this.Zoom(e.Delta);
        if (e is HandledMouseEventArgs handled) handled.Handled = true;
    }

    public void Dispose()
    {
        this.list.MouseWheel -= this.MouseWheel;
        if (!this.list.IsDisposed && !this.list.Disposing) this.list.Font = this.originalFont;
        this.ownedFont?.Dispose();
        this.ownedFont = null;
    }
}
