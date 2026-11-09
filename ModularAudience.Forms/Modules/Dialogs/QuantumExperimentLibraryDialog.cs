using ModularAudience.Forms.Quantum;

namespace ModularAudience.Forms.Modules.Dialogs;

/// <summary>Modeless save/load/rename/delete UI; never disables other application windows.</summary>
internal sealed class QuantumExperimentLibraryDialog : Form
{
    private readonly QuantumBeatEngineDialog editor;
    private readonly QuantumStudioStore store;
    private readonly ListBox names = new() { Name = "listBox_experiments", Dock = DockStyle.Fill };
    private readonly TextBox name = new() { Name = "textBox_experimentName", Dock = DockStyle.Fill };
    private readonly Label info = new() { Dock = DockStyle.Fill, Text = "Save named setups without audio PCM or credentials.", Padding = new Padding(3) };
    private readonly CheckBox confirmDelete = new() { Name = "checkBox_confirmDelete", Text = "Confirm deletion of the selected saved experiment", AutoSize = true };
    private readonly CheckBox confirmOverwrite = new() { Name = "checkBox_confirmOverwrite", Text = "Allow replacing an existing saved experiment", AutoSize = true };
    private readonly Button delete = new() { Text = "Delete", Enabled = false, AutoSize = true };

    internal QuantumExperimentLibraryDialog(QuantumBeatEngineDialog editor, QuantumStudioStore store)
    {
        this.editor = editor; this.store = store;
        this.Text = "Quantum experiments · Save / Load / Rename / Delete"; this.Name = "QuantumExperimentLibraryDialog";
        this.ClientSize = new Size(600, 440); this.MinimumSize = new Size(520, 390); this.StartPosition = FormStartPosition.CenterParent;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 6 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 25)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 27)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 27)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        root.Controls.Add(this.info, 0, 0); root.Controls.Add(this.names, 0, 1); root.Controls.Add(this.name, 0, 2);
        root.Controls.Add(this.confirmOverwrite, 0, 3); root.Controls.Add(this.confirmDelete, 0, 4);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var save = new Button { Text = "Save", AutoSize = true }; var load = new Button { Text = "Load", AutoSize = true }; var rename = new Button { Text = "Rename", AutoSize = true }; var close = new Button { Text = "Close", AutoSize = true };
        buttons.Controls.AddRange([save, load, rename, this.delete, close]); root.Controls.Add(buttons, 0, 5); this.Controls.Add(root);
        this.names.SelectedIndexChanged += (_, _) => { this.name.Text = this.names.SelectedItem as string ?? ""; this.confirmDelete.Checked = false; this.confirmOverwrite.Checked = false; };
        this.confirmDelete.CheckedChanged += (_, _) => this.delete.Enabled = this.confirmDelete.Checked && this.names.SelectedItem is not null;
        save.Click += (_, _) => this.Act(() =>
        {
            string target = this.name.Text.Trim();
            if (this.store.ExperimentNames().Contains(target, StringComparer.OrdinalIgnoreCase) && !this.confirmOverwrite.Checked)
            { this.info.Text = "Enable replacement before overwriting an existing experiment."; return; }
            this.editor.SaveNamedExperiment(target); this.RefreshNames(target); this.info.Text = "Experiment saved. Credentials remain separate.";
        });
        load.Click += (_, _) => this.Act(() => { if (this.names.SelectedItem is string selected) { this.editor.LoadNamedExperiment(selected); this.info.Text = "Experiment loaded. This window and the editor remain open."; } });
        rename.Click += (_, _) => this.Act(() =>
        {
            if (this.names.SelectedItem is not string selected) return;
            string target = this.name.Text.Trim(); this.editor.RenameNamedExperiment(selected, target); this.RefreshNames(target); this.info.Text = "Experiment renamed.";
        });
        this.delete.Click += (_, _) => this.Act(() =>
        {
            if (!this.confirmDelete.Checked || this.names.SelectedItem is not string selected) return;
            this.editor.DeleteNamedExperiment(selected); this.RefreshNames(null); this.confirmDelete.Checked = false; this.info.Text = "Saved experiment deleted. The current editor setup was not reset.";
        });
        close.Click += (_, _) => this.Close();
        this.RefreshNames(null);
    }

    internal void SelectAction(string action, string? selected)
    {
        this.RefreshNames(selected); this.info.Text = action + " a named experiment. Current editor settings are also restored from AppData.";
        if (action == "Save" && this.name.Text.Length == 0) this.name.Text = "Quantum experiment";
        if (action == "Rename") { this.name.Focus(); this.name.SelectAll(); }
    }
    private void RefreshNames(string? selected)
    {
        this.names.Items.Clear(); this.names.Items.AddRange(this.store.ExperimentNames());
        if (selected is not null) this.names.SelectedItem = this.names.Items.Cast<string>().FirstOrDefault(name => string.Equals(name, selected, StringComparison.OrdinalIgnoreCase));
    }
    private void Act(Action action)
    {
        try { action(); }
        catch (Exception error) { this.info.Text = QuantumBeatEngineDialog.EnglishError(error); }
    }
}
