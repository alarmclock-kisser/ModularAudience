using ModularAudience.Audio.Vocal;
using System.Buffers;
using System.Globalization;
using System.Text;

namespace ModularAudience.Forms.Modules.Dialogs;

public sealed class VocalEmphasisSyntaxDialog : Form
{
    public VocalEmphasisSyntax Syntax { get; private set; }
    private readonly DataGridView grid = new() { Dock = DockStyle.Fill, AllowUserToAddRows = true, AllowUserToDeleteRows = true, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
    private readonly System.Windows.Forms.Timer markerFlashTimer = new() { Interval = 180 };
    private DataGridViewRow? flashingRow;
    private Color flashingRowOriginalColor;

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
        grid.EditingControlShowing += Grid_EditingControlShowing;
        grid.CellValidating += Grid_CellValidating;
        markerFlashTimer.Tick += (_, _) => RestoreFlashingRow();
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

    internal static bool IsValidMarker(string marker)
    {
        if (string.IsNullOrEmpty(marker)) return true;
        ReadOnlySpan<char> remaining = marker.AsSpan();
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out Rune rune, out int consumed) != OperationStatus.Done
                || !IsSpecialMarkerCategory(Rune.GetUnicodeCategory(rune)))
                return false;
            remaining = remaining[consumed..];
        }
        return true;
    }

    private void Grid_EditingControlShowing(object? sender, DataGridViewEditingControlShowingEventArgs e)
    {
        if (e.Control is not TextBox editingControl) return;
        editingControl.KeyPress -= MarkerEditor_KeyPress;
        if (grid.CurrentCell?.OwningColumn?.Name == "Marker")
            editingControl.KeyPress += MarkerEditor_KeyPress;
    }

    private void MarkerEditor_KeyPress(object? sender, KeyPressEventArgs e)
    {
        if (e.KeyChar == '\b') return;
        if (char.IsSurrogate(e.KeyChar)) return;
        if (!IsSpecialMarkerCategory(char.GetUnicodeCategory(e.KeyChar)))
        {
            e.Handled = true;
            FlashCurrentRow();
        }
    }

    private void Grid_CellValidating(object? sender, DataGridViewCellValidatingEventArgs e)
    {
        if (grid.Columns[e.ColumnIndex].Name != "Marker") return;
        string marker = e.FormattedValue?.ToString() ?? string.Empty;
        if (!IsValidMarker(marker))
        {
            e.Cancel = true;
            grid.Rows[e.RowIndex].ErrorText = "Use printable punctuation or symbols only; letters, digits, and whitespace are not allowed.";
            FlashRow(e.RowIndex);
            return;
        }
        grid.Rows[e.RowIndex].ErrorText = string.Empty;
    }

    private static bool IsSpecialMarkerCategory(UnicodeCategory category) => category is
        UnicodeCategory.ConnectorPunctuation or UnicodeCategory.DashPunctuation
        or UnicodeCategory.OpenPunctuation or UnicodeCategory.ClosePunctuation
        or UnicodeCategory.InitialQuotePunctuation or UnicodeCategory.FinalQuotePunctuation
        or UnicodeCategory.OtherPunctuation or UnicodeCategory.MathSymbol
        or UnicodeCategory.CurrencySymbol or UnicodeCategory.ModifierSymbol
        or UnicodeCategory.OtherSymbol;

    private void FlashCurrentRow()
    {
        int rowIndex = grid.CurrentCell?.RowIndex ?? -1;
        FlashRow(rowIndex);
    }

    private void FlashRow(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= grid.Rows.Count || grid.Rows[rowIndex].IsNewRow) return;
        RestoreFlashingRow();
        flashingRow = grid.Rows[rowIndex];
        flashingRowOriginalColor = flashingRow.DefaultCellStyle.BackColor;
        flashingRow.DefaultCellStyle.BackColor = Color.LightCoral;
        markerFlashTimer.Start();
    }

    private void RestoreFlashingRow()
    {
        markerFlashTimer.Stop();
        if (flashingRow is not null && !flashingRow.IsNewRow)
            flashingRow.DefaultCellStyle.BackColor = flashingRowOriginalColor;
        flashingRow = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) markerFlashTimer.Dispose();
        base.Dispose(disposing);
    }
}

public static class VocalEmphasisSyntaxCloneExt
{
    public static VocalEmphasisSyntax Clone(this VocalEmphasisSyntax s) => new()
    {
        Rules = s.Rules.Select(r => r.Clone()).ToList(),
    };
}