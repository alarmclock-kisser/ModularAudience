using System.ComponentModel;

namespace ModularAudience.Forms.Hints;

public sealed class AppContextMenuStrip : System.Windows.Forms.ContextMenuStrip
{
    [DefaultValue(false)]
    public bool IsParameterHelp { get; set; }
    public AppContextMenuStrip() => HintPolicy.Watch(this);
    public AppContextMenuStrip(IContainer container) : this() => container.Add(this);

    protected override void OnOpening(CancelEventArgs e)
    {
        base.OnOpening(e);
        HintPolicy.RefreshStrip(this, this.IsParameterHelp || HintPolicy.IsParameterContext(this.SourceControl));
    }
}
