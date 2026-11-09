using ModularAudience.Audio.Effects;

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

    internal static ToolStripMenuItem CreateApplyMenu(Action<AudioEffectKind> execute)
    {
        var root = new ToolStripMenuItem("Apply Effect ...") { Name = "menuItem_applyEffects" };
        foreach (var group in AudioEffectCatalog.All.GroupBy(effect => effect.Group))
        {
            // ToolStrip treats a single ampersand as a mnemonic marker.
            var category = new ToolStripMenuItem(group.Key.Replace("&", "&&")) { Name = "effectGroup_" + group.First().Kind };
            foreach (AudioEffectDefinition effect in group)
            {
                ToolStripMenuItem command = Create(effect.Name, effect.OpensOptions, (_, _) => execute(effect.Kind));
                command.Name = "effect_" + effect.Kind; command.Tag = effect.Kind; command.ToolTipText = effect.Description;
                category.DropDownItems.Add(command);
            }
            root.DropDownItems.Add(category);
        }
        return root;
    }
}
