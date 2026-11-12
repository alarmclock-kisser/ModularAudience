using ModularAudience.Audio.Synthesis;
using ModularAudience.Forms.Modules.Dialogs;

namespace ModularAudience.Forms;

public partial class WindowMain
{
    private readonly Dictionary<SynthFamily, InstrumentSampleSynthDialog> sampleSynthesizers = [];
    private ToolStripMenuItem CreateSampleSynthesizerMenu()
        => CreateSampleSynthesizerMenu(this.ShowSampleSynthesizer);

    internal static ToolStripMenuItem CreateSampleSynthesizerMenu(Action<SynthFamily> onSelected)
    {
        var menu = new ToolStripMenuItem("Sample Synthesizer") { Name = "toolStripMenuItem_sampleSynthesizer" };
        foreach (var family in Enum.GetValues<SynthFamily>())
        {
            var selected = family;
            menu.DropDownItems.Add(InstrumentSynthCatalog.FamilyName(family), null, (_, _) => onSelected(selected));
        }
        return menu;
    }

    private void ShowSampleSynthesizer(SynthFamily family)
    {
        if (!this.sampleSynthesizers.TryGetValue(family, out InstrumentSampleSynthDialog? dialog) || dialog.IsDisposed)
        {
            dialog = new InstrumentSampleSynthDialog(family);
            this.sampleSynthesizers[family] = dialog;
        }

        dialog.Show();
        dialog.Activate();
    }
}
