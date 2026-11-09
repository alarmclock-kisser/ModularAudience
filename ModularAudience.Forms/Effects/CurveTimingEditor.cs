using ModularAudience.Audio.Effects.Curves;

namespace ModularAudience.Forms.Effects;

internal sealed class CurveTimingEditor : FlowLayoutPanel
{
    private readonly ComboBox scope = new() { Name = "curveScope", DropDownStyle = ComboBoxStyle.DropDownList, Width = 235 };
    private readonly CheckBox gaps = new() { Name = "curveIncludeGaps", Text = "Include gaps in curve time", AutoSize = true, Checked = true };
    private readonly CheckBox silence = new() { Name = "curveIncludeSilence", Text = "Include silence in curve time", AutoSize = true, Checked = true };
    private readonly NumericUpDown threshold = new() { Name = "curveSilenceThreshold", Minimum = -120, Maximum = -12, Value = -80, DecimalPlaces = 2, Increment = 0.5m, Width = 75 };
    private readonly NumericUpDown minimum = new() { Name = "curveMinimumSilence", Minimum = 1, Maximum = 10000, Value = 50, DecimalPlaces = 2, Width = 90 };
    private bool syncing;
    internal CurveTimingEditor()
    {
        this.Name = "curveTiming"; this.Width = 810; this.Height = 90; this.WrapContents = true;
        this.scope.Items.AddRange(["Restart curve per segment", "One curve across selection"]); this.scope.SelectedIndex = 0;
        this.Controls.AddRange([new Label { Text = "Curve timing", AutoSize = true, Margin = new Padding(3, 7, 3, 0) }, this.scope, this.gaps, this.silence,
            new Label { Text = "Silence below (dBFS)", AutoSize = true }, this.threshold,
            new Label { Text = "For at least (ms)", AutoSize = true }, this.minimum,
            new Label { Text = "Source-time clock; overlapping segments share time. Skipping gaps/silence changes the curve clock, not the audio selection.", AutoSize = true }]);
        this.scope.SelectedIndexChanged += this.Changed; this.gaps.CheckedChanged += this.Changed; this.silence.CheckedChanged += this.Changed;
        this.threshold.ValueChanged += this.Changed; this.minimum.ValueChanged += this.Changed; this.UpdateState();
    }
    internal event EventHandler? SettingsChanged;
    internal EffectCurveTiming CaptureSettings() => new() { Scope = (EffectCurveScope)this.scope.SelectedIndex, IncludeGaps = this.gaps.Checked,
        IncludeSilence = this.silence.Checked, SilenceThresholdDb = (double)this.threshold.Value, MinimumSilenceMs = (double)this.minimum.Value };
    internal void Apply(EffectCurveTiming settings)
    {
        settings.Validate(); this.syncing = true;
        try { this.scope.SelectedIndex = (int)settings.Scope; this.gaps.Checked = settings.IncludeGaps; this.silence.Checked = settings.IncludeSilence;
            this.threshold.Value = (decimal)settings.SilenceThresholdDb; this.minimum.Value = (decimal)settings.MinimumSilenceMs; }
        finally { this.syncing = false; }
        this.UpdateState(); this.SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
    private void Changed(object? sender, EventArgs e) { this.UpdateState(); if (!this.syncing) this.SettingsChanged?.Invoke(this, EventArgs.Empty); }
    private void UpdateState() { this.gaps.Enabled = this.scope.SelectedIndex == 1; this.threshold.Enabled = this.minimum.Enabled = !this.silence.Checked; }
}
