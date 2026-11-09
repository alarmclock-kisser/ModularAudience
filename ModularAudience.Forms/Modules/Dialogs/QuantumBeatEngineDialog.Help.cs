using ModularAudience.Forms.Quantum;

namespace ModularAudience.Forms.Modules.Dialogs;

public partial class QuantumBeatEngineDialog
{
    private void InitializeHelpMenu()
    {
        foreach (var (title, _) in QuantumHelpDialog.Topics)
        {
            var item = new ToolStripMenuItem(title);
            item.Click += (_, _) => this.ShowHelpTopic(title);
            this.helpMenu.DropDownItems.Add(item);
        }
        this.helpMenu.DropDownItems.Add(new ToolStripSeparator());
        this.helpMenu.DropDownItems.Add("Open AppData settings folder", null, (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(this.store.RootDirectory);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(this.store.RootDirectory) { UseShellExecute = true });
            }
            catch { this.SetStatus("The settings folder could not be opened. Check AppData access permissions.", true); }
        });
    }

    private void ShowHelpTopic(string topic)
    {
        if (this.helpWindow is not QuantumHelpDialog help || help.IsDisposed)
        { help = new QuantumHelpDialog(); this.helpWindow = help; }
        help.SelectTopic(topic); help.Show(this); help.BringToFront();
    }
}
