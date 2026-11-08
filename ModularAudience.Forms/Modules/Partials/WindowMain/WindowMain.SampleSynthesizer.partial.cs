using ModularAudience.Audio.Synthesis;
using ModularAudience.Forms.Modules.Dialogs;

namespace ModularAudience.Forms;

public partial class WindowMain
{
    private readonly Dictionary<SynthFamily, InstrumentSampleSynthDialog> sampleSynthesizers = [];
    private ToolStripMenuItem CreateSampleSynthesizerMenu()
    {
        var menu = new ToolStripMenuItem("Sample Synthesizer") { Name = "toolStripMenuItem_sampleSynthesizer" };
        foreach (var family in Enum.GetValues<SynthFamily>())
        {
            var selected = family;
            menu.DropDownItems.Add(InstrumentSynthCatalog.FamilyName(family), null, (_, _) =>
            {
                if (!sampleSynthesizers.TryGetValue(selected, out var dialog) || dialog.IsDisposed)
                {
                    dialog = new InstrumentSampleSynthDialog(selected); sampleSynthesizers[selected] = dialog;
                }
                dialog.Show(); dialog.Activate();
            });
        }
        return menu;
    }
}
