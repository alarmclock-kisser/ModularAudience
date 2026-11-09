namespace ModularAudience.Forms.Effects;

/// <summary>Shared naming rule for current and future parameterized effect commands.</summary>
internal static class EffectMenuFactory
{
    internal static ToolStripMenuItem Create(string name, bool opensParameters, EventHandler execute)
    {
        var item = new ToolStripMenuItem(name.TrimEnd(' ', '.') + (opensParameters ? " ..." : string.Empty));
        item.Click += execute;
        return item;
    }
}
