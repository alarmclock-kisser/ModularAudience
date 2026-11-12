namespace ModularAudience.Forms.Modules.Dialogs;

internal sealed partial class BreakbeatPatternRemixDialog
{
    private readonly ComboBox combo_rollPitchMode = new();
    private readonly NumericUpDown numeric_rollPitchStep = new();

    private void InitializeRollPitchControls()
    {
        this.combo_rollPitchMode.Name = "combo_rollPitchMode";
        this.combo_rollPitchMode.DropDownStyle = ComboBoxStyle.DropDownList;
        this.combo_rollPitchMode.Items.AddRange(["Same", "Ascending", "Descending"]);
        this.combo_rollPitchMode.Dock = DockStyle.Fill;

        this.numeric_rollPitchStep.Name = "numeric_rollPitchStep";
        this.numeric_rollPitchStep.Minimum = 0.25m;
        this.numeric_rollPitchStep.Maximum = 24m;
        this.numeric_rollPitchStep.Increment = 0.25m;
        this.numeric_rollPitchStep.DecimalPlaces = 2;
        this.numeric_rollPitchStep.Dock = DockStyle.Fill;
    }
}
