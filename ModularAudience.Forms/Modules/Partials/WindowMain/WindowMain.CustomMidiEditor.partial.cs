using ModularAudience.Forms.Modules.Dialogs;

namespace ModularAudience.Forms;

public partial class WindowMain
{
    internal static ToolStripMenuItem CreateMidiEditorMenuItem(Action onSelected)
    {
        ArgumentNullException.ThrowIfNull(onSelected);
        return new ToolStripMenuItem("Midi Editor", null, (_, _) => onSelected())
        {
            Name = "toolStripMenuItem_midiEditor"
        };
    }

    private void OpenCustomMidiEditor()
    {
        CustomMidiEditorDialog dialog = new();
        dialog.Show(this);
    }
}
