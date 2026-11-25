using ModularAudience.Audio.Synthesis;
using ModularAudience.Audio.Synthesis.Runtime;
using ModularAudience.Forms.Modules.Dialogs;

namespace ModularAudience.Forms;

public partial class WindowMain
{
    private readonly Dictionary<SynthFamily, InstrumentSampleSynthDialog> sampleSynthesizers = [];
    private ToolStripMenuItem CreateSampleSynthesizerMenu()
        => CreateSampleSynthesizerMenu(this.ShowSampleSynthesizer, this.ShowInstrumentCoderDialog);

    internal static ToolStripMenuItem CreateSampleSynthesizerMenu(Action<SynthFamily> onSelected)
        => CreateSampleSynthesizerMenu(onSelected, null);

    internal static ToolStripMenuItem CreateSampleSynthesizerMenu(Action<SynthFamily> onSelected, Action? onOpenCoder)
    {
        var menu = new ToolStripMenuItem("Sample Synthesizer") { Name = "toolStripMenuItem_sampleSynthesizer" };
        foreach (var family in Enum.GetValues<SynthFamily>())
        {
            // Custom instruments get their own submenu below instead of a generic family entry.
            if (family == SynthFamily.Custom) continue;

            var selected = family;
            menu.DropDownItems.Add(InstrumentSynthCatalog.FamilyName(family), null, (_, _) => onSelected(selected));
        }

        // Separator after the last instrument family (Oriental Sounds).
        menu.DropDownItems.Add(new ToolStripSeparator());

        // Custom instruments submenu.
        var customMenu = new ToolStripMenuItem("Custom instruments");
        foreach (var name in CustomInstrumentStore.Shared.Names())
        {
            var selectedName = name;
            customMenu.DropDownItems.Add(name, null, (_, _) =>
            {
                var settings = new InstrumentSynthSettings { Family = SynthFamily.Custom, CustomInstrumentName = selectedName };
                settings.Validate();
                var dialog = new InstrumentSampleSynthDialog(SynthFamily.Custom, initialSettings: settings);
                dialog.Show();
                dialog.Activate();
            });
        }
        customMenu.DropDownItems.Add(new ToolStripSeparator());
        customMenu.DropDownItems.Add("Open Instrument Coder...", null, (_, _) =>
        {
            if (onOpenCoder != null) { onOpenCoder(); }
            else { new InstrumentCoderDialog().Show(); }
        });
        menu.DropDownItems.Add(customMenu);

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

    private void ShowInstrumentCoderDialog()
    {
        var dialog = new InstrumentCoderDialog();
        dialog.Show();
    }

}

