namespace ModularAudience.Forms.Controls;

public sealed class BufferedListView : ListView
{
    public BufferedListView()
    {
        this.DoubleBuffered = true;
        this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
    }
}
