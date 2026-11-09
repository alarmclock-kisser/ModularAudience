using ModularAudience.Forms.Hints;

namespace ModularAudience.Forms;

public partial class WindowMain
{
    private ToolStripMenuItem? enableToolTipsMenuItem;

    private void InitializeHintMenu()
    {
        this.enableToolTipsMenuItem = CreateHintMenuItem(this._preferencesStore,
            () => this._applicationPreferences.EnableToolTips, this.SaveApplicationPreference);
        this.contextMenuStrip_main.Items.Insert(0, this.enableToolTipsMenuItem);
        this.contextMenuStrip_main.Opening += (_, _) => this.enableToolTipsMenuItem.Checked = HintPolicy.Enabled;
        HintPolicy.Watch(this);
    }

    internal static ToolStripMenuItem CreateHintMenuItem(Settings.ApplicationPreferencesStore store,
        Func<bool> current, Action<Func<Settings.ApplicationPreferences>> save)
    {
        var item = new ToolStripMenuItem("Enable ToolTips (hints)")
        {
            Name = "enableToolTipsMenuItem", CheckOnClick = true, Checked = current(),
            ToolTipText = "Show interaction hints over waveforms, lists, buttons and menus throughout the app. Parameter explanations and complete log-message tooltips remain available."
        };
        item.Click += (_, _) =>
        {
            if (item.Checked != current()) save(() => store.SetEnableToolTips(item.Checked));
        };
        return item;
    }
}
