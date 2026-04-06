namespace ModularAudience.Forms.Modules
{
    internal sealed class BufferedPatternPanel : Panel
    {
        public BufferedPatternPanel()
        {
            this.DoubleBuffered = true;
            this.ResizeRedraw = true;
        }
    }
}
