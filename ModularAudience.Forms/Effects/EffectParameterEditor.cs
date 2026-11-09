using ModularAudience.Audio.Effects;
using ModularAudience.Audio.Effects.Curves;

namespace ModularAudience.Forms.Effects;

internal sealed class EffectParameterEditor : UserControl
{
    private readonly AudioEffectDefinition definition;
    private readonly Dictionary<string, Control> fields = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CurveBindingControl> curves = new(StringComparer.Ordinal);
    private readonly CurveTimingEditor timing = new();
    private bool hadAutomation;
    private readonly ToolTip tips = new() { IsParameterHelp = true };
    private readonly Func<(double Bpm, double ScannedBpm)>? readTempo;
    private readonly NumericUpDown? sourceTempo, targetTempo;
    private bool syncing;

    internal EffectParameterEditor(AudioEffectDefinition definition, Func<(double Bpm, double ScannedBpm)>? readTempo = null,
        Action<Form, Form>? showChildDialog = null)
    {
        this.definition = definition; this.readTempo = readTempo; this.Dock = DockStyle.Fill;
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true, Padding = new Padding(6) };
        this.Controls.Add(panel);
        panel.Controls.Add(this.timing); this.timing.SettingsChanged += this.Changed;
        IReadOnlyList<EffectCurveTarget> targets = EffectCurveTargets.ForEffect(definition.Kind);
        foreach (EffectCurveTarget target in targets.Where(target => target.Key is EffectCurveTargets.Amount or EffectCurveTargets.FadeEnvelope))
        {
            var curve = new CurveBindingControl(target, () => target.Key == EffectCurveTargets.Amount ? 100 : definition.Kind == AudioEffectKind.FadeIn ? 1 : 0, showChildDialog);
            this.curves.Add(target.Key, curve); curve.SettingsChanged += this.Changed;
            var field = new FlowLayoutPanel { Width = 430, Height = 84, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(6) };
            field.Controls.Add(new Label { Text = target.Label, Width = 420, Height = 28 }); field.Controls.Add(curve); panel.Controls.Add(field);
        }
        if (definition.Kind is AudioEffectKind.TimeStretch or AudioEffectKind.VarispeedShift)
        {
            this.sourceTempo = new NumericUpDown { Name = "sourceBpm", Minimum = 5, Maximum = 420, Value = 120, DecimalPlaces = 3, Width = 130 };
            this.targetTempo = new NumericUpDown { Name = "targetBpm", Minimum = 0.625m, Maximum = 3360, Value = 120, DecimalPlaces = 3, Width = 130 };
            var tempos = new FlowLayoutPanel { Width = 650, Height = 68 };
            tempos.Controls.AddRange([new Label { Text = "Source BPM", AutoSize = true }, this.sourceTempo,
                new Label { Text = "Target BPM", AutoSize = true }, this.targetTempo,
                new Label { Text = definition.Kind == AudioEffectKind.TimeStretch
                    ? "Curves sweep speed to Target BPM (continuous FFT-PV, pitch preserved). Presets save the factor, not source metadata."
                    : "Varispeed changes speed AND pitch. Curves sweep speed to Target BPM. Presets save speed, not source metadata.", AutoSize = true }]);
            panel.Controls.Add(tempos);
            this.sourceTempo.ValueChanged += this.TempoChanged; this.targetTempo.ValueChanged += this.TempoChanged;
        }
        foreach (EffectParameter parameter in definition.Parameters)
        {
            Control control;
            if (parameter.Kind == EffectParameterKind.Boolean)
            {
                var check = new CheckBox { Text = parameter.Label, AutoSize = true };
                check.CheckedChanged += this.Changed; control = check;
            }
            else if (parameter.Kind == EffectParameterKind.Choice)
            {
                var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 270, DropDownWidth = 420 };
                combo.Items.AddRange(parameter.Choices!.Cast<object>().ToArray()); combo.SelectedIndexChanged += this.Changed; control = combo;
            }
            else
            {
                int decimals = parameter.Integral ? 0 : Math.Max(parameter.Key == "Factor" ? 6 : 1, DecimalPlaces(parameter.Increment));
                var number = new NumericUpDown { Minimum = (decimal)parameter.Minimum, Maximum = (decimal)parameter.Maximum,
                    Increment = (decimal)parameter.Increment, DecimalPlaces = decimals, Width = 160, ThousandsSeparator = true };
                number.ValueChanged += this.Changed; control = number;
            }
            control.Name = parameter.Key; this.fields.Add(parameter.Key, control); this.tips.SetToolTip(control, parameter.Description);
            var field = new FlowLayoutPanel { Width = 430, Height = 116, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(6) };
            if (parameter.Kind != EffectParameterKind.Boolean) field.Controls.Add(new Label { Text = parameter.Label, Width = 420, Height = 30 });
            field.Controls.Add(control); panel.Controls.Add(field);
            EffectCurveTarget? target = targets.FirstOrDefault(target => target.Key == parameter.Key);
            if (target is not null)
            {
                var curve = new CurveBindingControl(target, () => this.ReadValue(parameter.Key), showChildDialog); this.curves.Add(parameter.Key, curve);
                curve.SettingsChanged += this.Changed; field.Controls.Add(curve);
            }
        }
        this.ApplySettings(definition.CreateDefaults());
    }

    internal event EventHandler? SettingsChanged;
    internal AudioEffectSettings CaptureSettings()
    {
        var values = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (key, control) in this.fields)
            values[key] = control switch { NumericUpDown number => (double)number.Value, ComboBox combo => combo.SelectedIndex, CheckBox check => check.Checked ? 1 : 0, _ => 0 };
        var bindings = this.curves.Where(pair => pair.Value.CaptureSettings() is not null).ToDictionary(pair => pair.Key, pair => pair.Value.CaptureSettings()!, StringComparer.Ordinal);
        EffectCurveTiming clock = this.timing.CaptureSettings();
        var settings = new AudioEffectSettings { Effect = this.definition.Kind, Parameters = values,
            Automation = bindings.Count > 0 || this.hadAutomation || clock != new EffectCurveTiming() ? new() { Parameters = bindings, Timing = clock } : null };
        settings.Validate(); return settings;
    }

    internal void ApplySettings(AudioEffectSettings settings)
    {
        settings.Validate();
        if (settings.Effect != this.definition.Kind) throw new ArgumentException("The settings belong to another effect.");
        this.syncing = true;
        try
        {
            this.hadAutomation = settings.Automation is not null;
            this.timing.Apply(settings.Automation?.Timing ?? new());
            foreach (var (key, curve) in this.curves) curve.Apply(settings.Automation?.Parameters.GetValueOrDefault(key));
            foreach (var (key, control) in this.fields)
                switch (control)
                {
                    case NumericUpDown number: number.Value = (decimal)settings[key]; break;
                    case ComboBox combo: combo.SelectedIndex = settings.Choice(key); break;
                    case CheckBox check: check.Checked = settings.Flag(key); break;
                }
            if (this.sourceTempo is not null)
            {
                var tempo = this.readTempo?.Invoke() ?? default;
                double candidate = double.IsFinite(tempo.Bpm) && tempo.Bpm is >= 5 and <= 420 ? tempo.Bpm :
                    double.IsFinite(tempo.ScannedBpm) && tempo.ScannedBpm is >= 5 and <= 420 ? tempo.ScannedBpm : (double)this.sourceTempo.Value;
                this.sourceTempo.Value = (decimal)candidate;
                this.UpdateTargetTempo();
            }
        }
        finally { this.syncing = false; }
        this.SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Changed(object? sender, EventArgs e)
    {
        if (this.syncing) return;
        if (sender is Control { Name: "Factor" or "Speed" })
        {
            this.syncing = true; try { this.UpdateTargetTempo(); } finally { this.syncing = false; }
        }
        this.SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
    private double ReadValue(string key) => this.fields[key] switch
    {
        NumericUpDown number => (double)number.Value, ComboBox combo => combo.SelectedIndex, CheckBox check => check.Checked ? 1 : 0,
        _ => throw new InvalidOperationException("Unknown effect parameter control.")
    };
    private void UpdateTargetTempo()
    {
        if (this.sourceTempo is not null && this.targetTempo is not null)
        {
            var value = (NumericUpDown)this.fields[this.definition.Kind == AudioEffectKind.TimeStretch ? "Factor" : "Speed"];
            decimal speed = this.definition.Kind == AudioEffectKind.TimeStretch ? 1 / value.Value : value.Value;
            this.targetTempo.Value = Math.Clamp(this.sourceTempo.Value * speed, this.targetTempo.Minimum, this.targetTempo.Maximum);
        }
    }
    private void TempoChanged(object? sender, EventArgs e)
    {
        if (this.syncing || this.sourceTempo is null || this.targetTempo is null) return;
        this.syncing = true;
        try
        {
            var factor = (NumericUpDown)this.fields[this.definition.Kind == AudioEffectKind.TimeStretch ? "Factor" : "Speed"];
            decimal value = this.definition.Kind == AudioEffectKind.TimeStretch ? this.sourceTempo.Value / this.targetTempo.Value : this.targetTempo.Value / this.sourceTempo.Value;
            factor.Value = Math.Clamp(value, factor.Minimum, factor.Maximum);
            this.UpdateTargetTempo();
        }
        finally { this.syncing = false; }
        this.SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
    private static int DecimalPlaces(double step)
    {
        int places = 0;
        while (places < 6 && Math.Abs(step - Math.Round(step)) > 1e-8) { places++; step *= 10; }
        return places;
    }
    protected override void Dispose(bool disposing) { if (disposing) this.tips.Dispose(); base.Dispose(disposing); }
}
