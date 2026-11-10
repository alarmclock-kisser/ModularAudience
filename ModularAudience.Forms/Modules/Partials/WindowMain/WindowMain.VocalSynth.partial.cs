using ModularAudience.Audio.Vocal;
using ModularAudience.Forms.Modules.Dialogs;

namespace ModularAudience.Forms;

public partial class WindowMain
{
    private readonly Dictionary<string, VocalSynthDialog> vocalSynthesizers = [];
    private ToolStripMenuItem CreateVocalSynthMenu()
    {
        var menu = new ToolStripMenuItem("Vocalsynth ...") { Name = "toolStripMenuItem_vocalSynth" };
        menu.Click += (_, _) =>
        {
            const string key = "default";
            if (!vocalSynthesizers.TryGetValue(key, out var dialog) || dialog.IsDisposed)
            {
                dialog = new VocalSynthDialog();
                dialog.SetCollection(null);
                vocalSynthesizers[key] = dialog;
            }
            dialog.Show(); dialog.Activate();
        };
        return menu;
    }
}