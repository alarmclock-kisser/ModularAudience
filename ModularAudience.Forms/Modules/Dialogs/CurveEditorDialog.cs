using System.Drawing.Drawing2D;
using System.Globalization;
using ModularAudience.Audio.Effects.Curves;
using ModularAudience.Forms.Effects;

namespace ModularAudience.Forms.Modules.Dialogs;

/// <summary>Modeless shape editor shared by all effect options. Only OK changes the caller; only Save writes a shape preset.</summary>
internal sealed class CurveEditorDialog : Form
{
    private readonly EffectCurveTarget target;
    private readonly Func<double> currentTarget;
    private readonly Action<EffectParameterCurve> accepted;
    private readonly CurvePresetStore store;
    private readonly EffectPresetBar presets = new("curvePresets");
    private readonly TabControl modes = new() { Name = "curveMode", Dock = DockStyle.Fill };
    private readonly TextBox equation = new() { Name = "curveEquation", Dock = DockStyle.Top, Font = new Font(FontFamily.GenericMonospace, 11) };
    private readonly DataGridView points = new() { Name = "curveCheckpoints", Dock = DockStyle.Fill, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        AllowUserToAddRows = true, AllowUserToDeleteRows = true, RowHeadersWidth = 45, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
    private readonly ComboBox interpolation = new() { Name = "curveInterpolation", DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly Dictionary<string, NumericUpDown> coefficients = new(StringComparer.Ordinal);
    private readonly NumericUpDown start, end;
    private readonly CheckBox enabled = new() { Name = "enableCurve", Text = "Enable this curve", Checked = true, AutoSize = true };
    private readonly CheckBox follow = new() { Name = "curveFollowTarget", Text = "End follows current parameter", Checked = true, AutoSize = true };
    private readonly ComboBox scale = new() { Name = "curveValueScale", DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    private readonly PictureBox preview = new() { Name = "curvePreview", Dock = DockStyle.Fill, BackColor = Color.FromArgb(25, 28, 33) };
    private readonly Label status = new() { Name = "curveValidation", Dock = DockStyle.Fill, AutoEllipsis = true };
    private readonly Button apply = new() { Name = "applyCurve", Text = "OK / Apply Curve", AutoSize = true };
    private readonly ToolTip tips = new() { IsParameterHelp = true };
    private double[]? plotted;
    private bool syncing, refreshing;

    internal CurveEditorDialog(EffectCurveTarget target, EffectParameterCurve? binding, Func<double> currentTarget,
        Action<EffectParameterCurve> accepted, CurvePresetStore? store = null)
    {
        this.target = target; this.currentTarget = currentTarget; this.accepted = accepted; this.store = store ?? new();
        this.Name = "CurveEditorDialog"; this.Text = "Edit Curve — " + target.Label;
        this.ShowInTaskbar = false; this.MinimizeBox = false;
        this.ClientSize = new Size(1000, 800); this.MinimumSize = new Size(860, 710); this.StartPosition = FormStartPosition.CenterParent;
        this.start = this.RangeNumber("curveStart"); this.end = this.RangeNumber("curveEnd");
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 8 };
        root.ColumnStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.AutoSize));
        root.RowStyles.Add(new(SizeType.Absolute, 245)); root.RowStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.Absolute, 48)); root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.AutoSize));
        root.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(950, 0), Text =
            "x (or t) is normalized source time, 0–1. y is a normalized shape, clamped to 0–1 before mapping to the parameter range.\n" +
            (target.InterpolateSpeed ? "Time Stretch interpolates SPEED, not duration: 1× starts at source BPM; the end follows Target BPM. Curves always use continuous FFT-PV." :
            "Each option has its own independent curve. Multiple curves can be active together.") }, 0, 0);
        root.Controls.Add(this.presets, 0, 1);
        var range = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        this.scale.Items.AddRange(["Linear values", "Logarithmic values"]); this.scale.SelectedIndex = 0; this.scale.Enabled = !target.InterpolateSpeed;
        range.Controls.AddRange([this.enabled, new Label { Text = target.InterpolateSpeed ? "Start speed (×)" : "Start value", AutoSize = true }, this.start,
            new Label { Text = target.InterpolateSpeed ? "End speed (×)" : "End value", AutoSize = true }, this.end, this.follow, this.scale]);
        root.Controls.Add(range, 0, 2);
        TabPage equationPage = new("Equation"), pointsPage = new("Checkpoints");
        this.modes.TabPages.AddRange([equationPage, pointsPage]);
        var equationLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(8) };
        equationLayout.RowStyles.Add(new(SizeType.Absolute, 35)); equationLayout.RowStyles.Add(new(SizeType.Absolute, 74)); equationLayout.RowStyles.Add(new(SizeType.Percent, 100));
        equationLayout.Controls.Add(this.equation, 0, 0);
        var variables = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
        foreach (string key in new[] { "f", "p", "a", "b", "c", "d" })
        {
            var number = new NumericUpDown { Name = "curveVariable" + key.ToUpperInvariant(), Minimum = key == "f" ? 0 : key == "p" ? -1000 : -10000,
                Maximum = key is "f" or "p" ? 1000 : 10000, DecimalPlaces = 6, Increment = 0.1m, Width = 110 };
            this.coefficients.Add(key, number);
            var field = new FlowLayoutPanel { Width = 140, Height = 68, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            field.Controls.Add(new Label { Text = key switch { "f" => "f: cycles", "p" => "p: phase (cycles)", _ => key + ": shape parameter" }, AutoSize = true });
            field.Controls.Add(number); variables.Controls.Add(field); number.ValueChanged += (_, _) => this.ValidateCurve();
        }
        equationLayout.Controls.Add(variables, 0, 1);
        equationLayout.Controls.Add(new Label { Dock = DockStyle.Fill, Text =
            "Operators: +  -  *  /  %  ^  (power); constants: pi, e; variables: x/t, f, p, a, b, c, d. Use decimal points.\n" +
            "Functions: sin, cos, tan, asin, acos, atan, sinh, cosh, tanh, abs, sqrt, exp, log/ln, log10, floor, ceil, round, sign,\n" +
            "min(v1,v2), max(v1,v2), pow(v,p), clamp(v,min,max), smoothstep(x), step(edge,x). Trig uses radians.\n" +
            "Examples: 0.5+0.5*sin(2*pi*(f*x+p)); x^a; exp(-a*x). Undefined arithmetic/poles are rejected, not silently repaired." }, 0, 2);
        equationPage.Controls.Add(equationLayout);
        this.points.Columns.Add("X", "x — position (0–1)"); this.points.Columns.Add("Y", "y — amount (0–1)");
        this.interpolation.Items.AddRange(["Linear", "Smooth S-curve", "Hold / steps"]); this.interpolation.SelectedIndex = 0;
        var pointActions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        Button add = new() { Name = "addCurvePoint", Text = "Add checkpoint", AutoSize = true }, remove = new() { Name = "removeCurvePoint", Text = "Remove selected", AutoSize = true };
        pointActions.Controls.AddRange([new Label { Text = "Interpolation", AutoSize = true }, this.interpolation, add, remove,
            new Label { Text = "Keep rows in x order. First x = 0; last x = 1. Up to 512 points.", AutoSize = true }]);
        pointsPage.Controls.Add(this.points); pointsPage.Controls.Add(pointActions);
        add.Click += (_, _) => { int row = this.points.Rows.Add(); this.points.CurrentCell = this.points.Rows[row].Cells[0]; this.points.BeginEdit(true); };
        remove.Click += (_, _) => { foreach (DataGridViewRow row in this.points.SelectedRows.Cast<DataGridViewRow>().Where(row => !row.IsNewRow).ToArray()) this.points.Rows.Remove(row); this.ValidateCurve(); };
        this.points.DataError += (_, e) => { e.ThrowException = false; this.status.Text = "Enter numeric checkpoint values using decimal points."; };
        this.points.CellValueChanged += (_, _) => this.ValidateCurve(); this.points.RowsRemoved += (_, _) => this.ValidateCurve();
        this.interpolation.SelectedIndexChanged += (_, _) => this.ValidateCurve(); this.modes.SelectedIndexChanged += (_, _) => this.ValidateCurve();
        this.equation.TextChanged += (_, _) => this.ValidateCurve();
        this.start.ValueChanged += (_, _) => this.ValidateCurve(); this.end.ValueChanged += (_, _) => this.ValidateCurve(); this.scale.SelectedIndexChanged += (_, _) => this.ValidateCurve();
        this.follow.CheckedChanged += (_, _) => { this.UpdateEnd(); this.ValidateCurve(); };
        this.enabled.CheckedChanged += (_, _) => this.ValidateCurve();
        this.preview.Paint += this.PaintPreview;
        root.Controls.Add(this.modes, 0, 3); root.Controls.Add(this.preview, 0, 4); root.Controls.Add(this.status, 0, 5);
        root.Controls.Add(new Label { AutoSize = true, Text = "OK activates the curve for this effect only. Save stores the SHAPE globally; parameter ranges/timing stay with the effect. Built-ins are read-only." }, 0, 6);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Name = "cancelCurve", Text = "Cancel", AutoSize = true }; cancel.Click += (_, _) => this.Close();
        actions.Controls.AddRange([cancel, this.apply]); root.Controls.Add(actions, 0, 7); this.Controls.Add(root); this.AcceptButton = this.apply; this.CancelButton = cancel;
        this.apply.Click += (_, _) => this.Safe(this.AcceptCurve);
        this.presets.Presets.SelectedIndexChanged += (_, _) => { if (!this.refreshing) this.Safe(this.LoadSelectedPreset); this.UpdatePresetButtons(); };
        this.presets.Presets.DropDown += (_, _) => this.Safe(() => this.RefreshPresets(this.presets.Presets.SelectedItem as CurvePreset));
        this.presets.LoadButton.Click += (_, _) => this.Safe(this.LoadSelectedPreset); this.presets.SaveButton.Click += (_, _) => this.Safe(this.SavePreset);
        this.presets.RenameButton.Click += (_, _) => this.Safe(this.RenamePreset); this.presets.DeleteButton.Click += (_, _) => this.Safe(this.DeletePreset);
        this.presets.DefaultsButton.Click += (_, _) => this.SetShape(new());
        this.Activated += (_, _) => { this.UpdateEnd(); this.ValidateCurve(); };
        this.SetBinding(binding ?? new() { StartValue = target.NeutralValue });
        this.Safe(() => this.RefreshPresets(null));
    }

    internal EffectParameterCurve CurrentBinding
    {
        get
        {
            this.points.EndEdit();
            var curve = new EffectCurve { Mode = (EffectCurveMode)this.modes.SelectedIndex, Equation = this.equation.Text,
                Cycles = (double)this.coefficients["f"].Value, Phase = (double)this.coefficients["p"].Value,
                A = (double)this.coefficients["a"].Value, B = (double)this.coefficients["b"].Value, C = (double)this.coefficients["c"].Value, D = (double)this.coefficients["d"].Value,
                Interpolation = (EffectCurveInterpolation)this.interpolation.SelectedIndex,
                Points = this.modes.SelectedIndex == 1 ? this.ReadPoints() : [new(0, 0), new(1, 1)] };
            var binding = new EffectParameterCurve { Enabled = this.enabled.Checked, Curve = curve,
                StartValue = this.FromDisplay((double)this.start.Value), EndValue = this.follow.Checked ? null : this.FromDisplay((double)this.end.Value),
                Scale = (EffectCurveValueScale)this.scale.SelectedIndex };
            new EffectAutomationSettings { Parameters = new() { [this.target.Key] = binding } }.Validate([this.target], _ => this.currentTarget());
            return binding;
        }
    }
    internal void SetBinding(EffectParameterCurve binding)
    {
        this.syncing = true;
        try
        {
            this.enabled.Checked = binding.Enabled; this.start.Value = (decimal)this.ToDisplay(binding.StartValue);
            this.follow.Checked = binding.EndValue is null; this.end.Value = (decimal)this.ToDisplay(binding.EndValue ?? this.currentTarget()); this.scale.SelectedIndex = (int)binding.Scale;
            this.SetShape(binding.Curve);
        }
        finally { this.syncing = false; }
        this.UpdateEnd(); this.ValidateCurve();
    }
    private void SetShape(EffectCurve curve)
    {
        bool wasSyncing = this.syncing; this.syncing = true;
        try
        {
            this.equation.Text = curve.Equation; this.modes.SelectedIndex = (int)curve.Mode; this.interpolation.SelectedIndex = (int)curve.Interpolation;
            this.coefficients["f"].Value = (decimal)curve.Cycles; this.coefficients["p"].Value = (decimal)curve.Phase;
            this.coefficients["a"].Value = (decimal)curve.A; this.coefficients["b"].Value = (decimal)curve.B; this.coefficients["c"].Value = (decimal)curve.C; this.coefficients["d"].Value = (decimal)curve.D;
            this.points.Rows.Clear(); foreach (EffectCurvePoint point in curve.Points) this.points.Rows.Add(point.X.ToString("G17", CultureInfo.InvariantCulture), point.Y.ToString("G17", CultureInfo.InvariantCulture));
        }
        finally { this.syncing = wasSyncing; }
        if (!this.syncing) this.ValidateCurve();
    }
    internal void AcceptCurve() { EffectParameterCurve binding = this.CurrentBinding; this.accepted(binding.Copy()); this.DialogResult = DialogResult.OK; this.Close(); }
    internal void SavePreset()
    {
        CurvePreset saved = this.store.Save(this.presets.PresetName.Text, this.CurrentBinding.Curve);
        this.presets.PresetName.Text = saved.Name; this.RefreshPresets(saved); this.status.Text = "Saved curve shape: " + saved.Name;
    }
    internal void LoadSelectedPreset()
    {
        if (this.presets.Presets.SelectedItem is not CurvePreset selected) return;
        CurvePreset loaded = selected.IsBuiltIn ? selected : this.store.Load(selected.Name);
        this.presets.PresetName.Text = loaded.IsBuiltIn ? string.Empty : loaded.Name; this.SetShape(loaded.Curve);
        this.status.Text = "Loaded shape: " + loaded.Name + ". Range and timing are unchanged. Save a variant with a new name (blank = Curve#N).";
    }
    internal void RenamePreset()
    {
        if (this.presets.Presets.SelectedItem is not CurvePreset { IsBuiltIn: false } selected) return;
        CurvePreset renamed = this.store.Rename(selected.Name, this.presets.PresetName.Text); this.presets.PresetName.Text = renamed.Name; this.RefreshPresets(renamed);
    }
    internal void DeletePreset()
    {
        if (this.presets.Presets.SelectedItem is not CurvePreset { IsBuiltIn: false } selected) return;
        this.store.Delete(selected.Name); this.presets.PresetName.Clear(); this.RefreshPresets(null);
        this.status.Text = "User preset deleted. The current unsaved curve is unchanged.";
    }
    private void RefreshPresets(CurvePreset? selected)
    {
        this.refreshing = true;
        try
        {
            IReadOnlyList<CurvePreset> available;
            try { available = this.store.ListAvailable(); }
            catch (Exception error) when (EffectOptionsEditor.IsEffectError(error)) { available = this.store.FactoryPresets(); this.status.Text = error.Message; }
            this.presets.Presets.Items.Clear(); this.presets.Presets.Items.AddRange(available.Cast<object>().ToArray());
            this.presets.Presets.SelectedItem = selected is null ? null : available.FirstOrDefault(preset => preset.BuiltInId == selected.BuiltInId &&
                (preset.IsBuiltIn || preset.Name.Equals(selected.Name, StringComparison.OrdinalIgnoreCase)));
        }
        finally { this.refreshing = false; }
        this.UpdatePresetButtons();
    }
    private void UpdatePresetButtons() => this.presets.UpdateState(false, this.presets.Presets.SelectedItem is CurvePreset,
        this.presets.Presets.SelectedItem is CurvePreset { IsBuiltIn: false });
    private EffectCurvePoint[] ReadPoints() => this.points.Rows.Cast<DataGridViewRow>().Where(row => !row.IsNewRow).Select(row =>
    {
        if (!double.TryParse(Convert.ToString(row.Cells[0].Value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out double x) ||
            !double.TryParse(Convert.ToString(row.Cells[1].Value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out double y))
            throw new ArgumentException("Every checkpoint row needs numeric x and y values (use decimal points).");
        return new EffectCurvePoint(x, y);
    }).ToArray();
    private void ValidateCurve()
    {
        if (this.syncing) return;
        try
        {
            CompiledEffectCurve curve = this.CurrentBinding.Curve.Compile();
            this.plotted = Enumerable.Range(0, 601).Select(i => curve.At(i / 600d)).ToArray();
            this.status.ForeColor = SystemColors.ControlText; this.status.Text = "Valid curve. Preview shows the clamped shape; no audio or presets have been changed.";
            this.apply.Enabled = this.presets.SaveButton.Enabled = true;
        }
        catch (Exception error) when (EffectOptionsEditor.IsEffectError(error))
        {
            this.plotted = null; this.status.ForeColor = Color.Firebrick; this.status.Text = error.Message;
            this.apply.Enabled = this.presets.SaveButton.Enabled = false;
        }
        this.preview.Invalidate();
    }
    private NumericUpDown RangeNumber(string name) => new() { Name = name, Width = 110, DecimalPlaces = this.target.Integral ? 0 : 6,
        Minimum = (decimal)(this.target.InterpolateSpeed ? 1 / this.target.Maximum : this.target.Minimum),
        Maximum = (decimal)(this.target.InterpolateSpeed ? 1 / this.target.Minimum : this.target.Maximum), Increment = this.target.Integral ? 1 : 0.01m };
    private double ToDisplay(double value) => this.target.InterpolateSpeed ? 1 / value : value;
    private double FromDisplay(double value) => this.target.InterpolateSpeed ? 1 / value : value;
    private void UpdateEnd()
    {
        this.end.Enabled = !this.follow.Checked;
        if (this.follow.Checked) this.end.Value = Math.Clamp((decimal)this.ToDisplay(this.currentTarget()), this.end.Minimum, this.end.Maximum);
        this.tips.SetToolTip(this.end, this.target.InterpolateSpeed ? "Speed multiplier relative to Source BPM. The duration factor is its reciprocal." : "Parameter value when the curve reaches y=1.");
    }
    private void Safe(Action action) { try { action(); } catch (Exception error) when (EffectOptionsEditor.IsEffectError(error)) { this.status.ForeColor = Color.Firebrick; this.status.Text = error.Message; } }
    private void PaintPreview(object? sender, PaintEventArgs e)
    {
        Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        var area = new RectangleF(42, 24, Math.Max(1, this.preview.ClientSize.Width - 60), Math.Max(1, this.preview.ClientSize.Height - 54));
        using var grid = new Pen(Color.FromArgb(65, 72, 81)); using var line = new Pen(Color.DeepSkyBlue, 2);
        for (int i = 0; i <= 4; i++) { float t = i / 4f; g.DrawLine(grid, area.Left, area.Top + area.Height * t, area.Right, area.Top + area.Height * t);
            g.DrawLine(grid, area.Left + area.Width * t, area.Top, area.Left + area.Width * t, area.Bottom); }
        g.DrawString("1", this.Font, Brushes.Gainsboro, 18, area.Top - 6); g.DrawString("0", this.Font, Brushes.Gainsboro, 18, area.Bottom - 8);
        g.DrawString("x = 0", this.Font, Brushes.Gainsboro, area.Left, area.Bottom + 3); g.DrawString("x = 1", this.Font, Brushes.Gainsboro, area.Right - 40, area.Bottom + 3);
        g.DrawString("Normalized curve (0–1)", this.Font, Brushes.Gainsboro, area.Left, 2);
        if (this.plotted is not null) g.DrawLines(line, this.plotted.Select((y, i) => new PointF(area.Left + area.Width * i / (this.plotted.Length - 1), area.Bottom - area.Height * (float)y)).ToArray());
    }
    protected override void Dispose(bool disposing) { if (disposing) { this.tips.Dispose(); this.equation.Font.Dispose(); } base.Dispose(disposing); }
}
