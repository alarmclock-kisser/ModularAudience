using ModularAudience.Audio.Effects.Curves;
using ModularAudience.Forms.Modules.Dialogs;

namespace ModularAudience.Forms.Effects;

/// <summary>A parameter owns its own curve, toggle and equation label. Opening/cancelling the child is side-effect-free.</summary>
internal sealed class CurveBindingControl : UserControl
{
    private readonly EffectCurveTarget target;
    private readonly Func<double> currentValue;
    private readonly Action<Form, Form>? showChildDialog;
    private readonly CheckBox enabled = new() { Text = "Curve", AutoSize = true };
    private readonly Button edit = new() { Text = "Edit Curve ...", AutoSize = true };
    private readonly Label equation = new() { AutoEllipsis = true, Width = 215, Height = 32, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolTip tips = new() { IsParameterHelp = true };
    private EffectParameterCurve? binding;
    private CurveEditorDialog? dialog;
    private bool syncing;

    internal CurveBindingControl(EffectCurveTarget target, Func<double> currentValue, Action<Form, Form>? showChildDialog = null)
    {
        this.target = target; this.currentValue = currentValue; this.showChildDialog = showChildDialog; this.Width = 410; this.Height = 38;
        this.Name = target.Key + "CurveControl"; this.enabled.Name = target.Key + "CurveEnabled";
        this.edit.Name = target.Key + "EditCurve"; this.equation.Name = target.Key + "CurveEquation";
        var row = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        row.Controls.AddRange([this.enabled, this.edit, this.equation]); this.Controls.Add(row);
        this.enabled.CheckedChanged += (_, _) =>
        {
            if (this.syncing || this.binding is null) return;
            this.binding = this.binding with { Enabled = this.enabled.Checked }; this.UpdateLabel(); this.SettingsChanged?.Invoke(this, EventArgs.Empty);
        };
        this.edit.Click += (_, _) => this.EditCurve(); this.Apply(null);
        this.EnabledChanged += (_, _) => { if (this.dialog is { IsDisposed: false }) this.dialog.Enabled = this.Enabled; };
    }
    internal event EventHandler? SettingsChanged;
    internal EffectParameterCurve? CaptureSettings() => this.binding?.Copy();
    internal void Apply(EffectParameterCurve? value)
    {
        this.syncing = true;
        try { this.binding = value?.Copy(); this.enabled.Enabled = value is not null; this.enabled.Checked = value?.Enabled == true; this.UpdateLabel(); }
        finally { this.syncing = false; }
    }
    internal void AcceptCurve(EffectParameterCurve value) { this.Apply(value); this.SettingsChanged?.Invoke(this, EventArgs.Empty); }
    private void EditCurve()
    {
        if (this.dialog is { IsDisposed: false }) { this.dialog.Activate(); return; }
        var child = new CurveEditorDialog(this.target, this.binding, this.currentValue, this.AcceptCurve);
        this.dialog = child; child.FormClosed += (_, _) => { if (ReferenceEquals(this.dialog, child)) this.dialog = null; };
        Form? owner = this.FindForm();
        if (owner is null) child.Show();
        else if (this.showChildDialog is not null) this.showChildDialog(child, owner);
        else child.Show(owner);
    }
    private void UpdateLabel()
    {
        this.equation.Text = this.binding is null ? "No curve (constant parameter)" : (this.binding.Enabled ? "" : "Off — ") + this.binding.Curve.Summary;
        this.tips.SetToolTip(this.equation, this.equation.Text);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { this.dialog?.Close(); this.tips.Dispose(); }
        base.Dispose(disposing);
    }
}
