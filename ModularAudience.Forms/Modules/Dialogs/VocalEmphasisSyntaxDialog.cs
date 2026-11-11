using ModularAudience.Audio.Vocal;

namespace ModularAudience.Forms.Modules.Dialogs;

public sealed class VocalEmphasisSyntaxDialog : Form
{
    public VocalEmphasisSyntax Syntax { get; private set; }
    private readonly DataGridView grid = new() { Dock = DockStyle.Fill, AllowUserToAddRows = true, AllowUserToDeleteRows = true, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };

    public VocalEmphasisSyntaxDialog(VocalEmphasisSyntax initial)
    {
        Syntax = initial.Clone();
        Text = "Emphasis Syntax"; Name = "VocalEmphasisSyntaxDialog";
        ClientSize = new Size(640, 400); StartPosition = FormStartPosition.CenterParent;

        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Marker", HeaderText = "Marker", Width = 80 });
        grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = "Mode",
            HeaderText = "Mode",
            ValueType = typeof(VocalEmphasisMode),
            DataSource = Enum.GetValues<VocalEmphasisMode>(),
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Value", HeaderText = "Value", Width = 80 });
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Wrapping", HeaderText = "Wrapping" });
        grid.DefaultValuesNeeded += (_, e) =>
        {
            e.Row.Cells["Mode"].Value = VocalEmphasisMode.Strong;
            e.Row.Cells["Value"].Value = 1.0;
            e.Row.Cells["Wrapping"].Value = true;
        };

        foreach (var r in Syntax.Rules)
        {
            grid.Rows.Add(r.Marker, r.Mode, r.Value, r.Wrapping);
        }

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(6) };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        bottom.Controls.Add(ok); bottom.Controls.Add(cancel);

        Controls.Add(grid);
        Controls.Add(bottom);
        AcceptButton = ok; CancelButton = cancel;

        ok.Click += (_, _) => Apply();
    }

    private void Apply()
    {
        var rules = new List<VocalEmphasisRule>();
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.IsNewRow) continue;
            var marker = row.Cells["Marker"].Value?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(marker)) continue;
            var mode = row.Cells["Mode"].Value is VocalEmphasisMode m ? m : VocalEmphasisMode.Strong;
            double.TryParse(row.Cells["Value"].Value?.ToString(), out var val);
            if (val == 0) val = 1;
            var wrapping = row.Cells["Wrapping"].Value is true;
            rules.Add(new VocalEmphasisRule { Marker = marker, Mode = mode, Value = val, Wrapping = wrapping });
        }
        Syntax = new VocalEmphasisSyntax { Rules = rules };
    }
}

public static class VocalEmphasisSyntaxCloneExt
{
    public static VocalEmphasisSyntax Clone(this VocalEmphasisSyntax s) => new()
    {
        Rules = s.Rules.Select(r => r.Clone()).ToList(),
    };
}