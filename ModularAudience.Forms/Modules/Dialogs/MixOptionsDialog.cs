using ModularAudience.Audio.Arrangement;
using ModularAudience.Forms.Theming;
using System.Text.Json;

namespace ModularAudience.Forms.Modules.Dialogs;

internal sealed class MixOptionsDialog : Form
{
    private readonly MixArrangement state;
    private readonly Action<MixArrangement> preview;
    private bool initializing;
    internal MixArrangement Options => state;
    public MixOptionsDialog(MixArrangement arrangement, Action<MixArrangement> preview)
    {
        this.preview = preview;
        state = new(); state.Restore(arrangement.Capture());
        Text = "Mix Options — appearance and beat grid"; Name = "MixOptionsDialog";
        ClientSize = new Size(650, 650); MinimumSize = new Size(600, 580); StartPosition = FormStartPosition.CenterParent;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.Percent, 100)); root.RowStyles.Add(new(SizeType.AutoSize));
        var fields = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 4 };
        for (int i = 0; i < 2; i++) { fields.ColumnStyles.Add(new(SizeType.Percent, 28)); fields.ColumnStyles.Add(new(SizeType.Percent, 22)); }
        int index = 0;
        NumericUpDown Number(string text, double value, decimal min, decimal max, Action<double> changed, decimal increment = 1)
        {
            int row = index / 2, col = index++ % 2 * 2;
            fields.Controls.Add(new Label { Text = text, AutoSize = true, Margin = new Padding(3, 7, 3, 3) }, col, row);
            var number = new NumericUpDown { Minimum = min, Maximum = max, Value = (decimal)value, DecimalPlaces = increment < 1 ? 1 : 0,
                Increment = increment, Dock = DockStyle.Fill };
            number.ValueChanged += (_, _) => { changed((double)number.Value); ApplyPreview(); }; fields.Controls.Add(number, col + 1, row); return number;
        }
        Number("Grid BPM", state.Bpm, 10, 400, v => state.Bpm = v, 0.5m);
        Number("Track height (px)", state.DisplayLaneHeight, 32, 600, v => state.DisplayLaneHeight = (int)v);
        Number("Zoom (pixels / sec)", state.Visual.PixelsPerSecond, 8, 2000, v => state.Visual.PixelsPerSecond = v);
        Number("Hue shift (degrees)", state.Visual.Hue, 0, 360, v => state.Visual.Hue = v);
        Number("Hue speed (degrees / sec)", state.Visual.HueSpeed, -720, 720, v => state.Visual.HueSpeed = v, 0.5m);
        var flags = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        var division = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70 };
        division.Items.AddRange(new object[] { "1/1", "1/2", "1/4", "1/8", "1/16", "1/32", "1/64" });
        division.SelectedIndex = (int)Math.Log2(state.SnapDivision);
        division.SelectedIndexChanged += (_, _) => { state.SnapDivision = 1 << division.SelectedIndex; ApplyPreview(); };
        flags.Controls.Add(new Label { Text = "Grid", AutoSize = true, Margin = new Padding(3, 7, 3, 3) }); flags.Controls.Add(division);
        void Check(string text, bool value, Action<bool> changed)
        { var check = new CheckBox { Text = text, Checked = value, AutoSize = true }; check.CheckedChanged += (_, _) => { changed(check.Checked); ApplyPreview(); }; flags.Controls.Add(check); }
        Check("Snap", state.Snap, v => state.Snap = v); Check("Show grid", state.Visual.ShowGrid, v => state.Visual.ShowGrid = v);
        Check("Hue cycle", state.Visual.HueCycle, v => state.Visual.HueCycle = v);
        Check("Waveforms", state.Visual.ShowWaveforms, v => state.Visual.ShowWaveforms = v); Check("Track colors", state.Visual.ColorTracks, v => state.Visual.ColorTracks = v);
        fields.Controls.Add(flags, 0, (index + 1) / 2); fields.SetColumnSpan(flags, 4);
        var colors = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.CellSelect };
        colors.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Color", ReadOnly = true });
        colors.Columns.Add(new DataGridViewTextBoxColumn { Name = "Hex", HeaderText = "HEX (#RRGGBB)" });
        colors.Columns.Add(new DataGridViewButtonColumn { Name = "Pick", HeaderText = "Color picker", Text = "Choose…", UseColumnTextForButtonValue = true });
        colors.Columns.Add(new DataGridViewTextBoxColumn { Name = "Swatch", HeaderText = "Preview", ReadOnly = true });
        void RefreshColors()
        {
            initializing = true; colors.Rows.Clear();
            foreach (var p in typeof(MixVisualOptions).GetProperties().Where(p => p.PropertyType == typeof(string)))
            {
                int row = colors.Rows.Add(p.Name, p.GetValue(state.Visual) ?? "", "Choose…", "■");
                Color color = ThemeFiles.ParseColor((string)p.GetValue(state.Visual)!);
                colors.Rows[row].Cells[3].Style.BackColor = color; colors.Rows[row].Cells[3].Style.SelectionBackColor = color;
            }
            initializing = false;
        }
        void Store(DataGridViewRow row)
        {
            try
            {
                Color c = ThemeFiles.ParseColor(row.Cells[1].Value?.ToString() ?? "");
                string text = ThemeFiles.FormatColor(Color.FromArgb(c.R, c.G, c.B));
                typeof(MixVisualOptions).GetProperty(row.Cells[0].Value!.ToString()!)!.SetValue(state.Visual, text);
                row.Cells[1].Value = text; row.Cells[3].Style.BackColor = c; row.Cells[3].Style.SelectionBackColor = c;
                row.ErrorText = ""; ApplyPreview();
            }
            catch (FormatException error) { row.ErrorText = error.Message; }
        }
        colors.CellEndEdit += (_, e) => { if (!initializing && e.RowIndex >= 0) Store(colors.Rows[e.RowIndex]); };
        colors.CellClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 2) return;
            var row = colors.Rows[e.RowIndex];
            using var dialog = new ColorDialog { FullOpen = true, Color = ThemeFiles.ParseColor((string)typeof(MixVisualOptions).GetProperty(row.Cells[0].Value!.ToString()!)!.GetValue(state.Visual)!) };
            if (dialog.ShowDialog(this) == DialogResult.OK) { row.Cells[1].Value = ThemeFiles.FormatColor(dialog.Color); Store(row); }
        };
        RefreshColors();
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var ok = new Button { Text = "Apply", AutoSize = true };
        ok.Click += (_, _) => { colors.EndEdit(); if (colors.Rows.Cast<DataGridViewRow>().Any(r => r.ErrorText.Length > 0)) return; DialogResult = DialogResult.OK; Close(); };
        buttons.Controls.Add(cancel); buttons.Controls.Add(ok); AcceptButton = ok; CancelButton = cancel;
        root.Controls.Add(fields, 0, 0); root.Controls.Add(colors, 0, 1); root.Controls.Add(buttons, 0, 2); Controls.Add(root);
    }
    private void ApplyPreview() { if (!initializing) preview(state); }
}
