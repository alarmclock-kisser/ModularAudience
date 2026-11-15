namespace ModularAudience.Forms.Controls;

public sealed class BreakbeatTempoNumericUpDown : NumericUpDown
{
    public override void UpButton()
    {
        if ((Control.ModifierKeys & Keys.Control) == Keys.Control)
        {
            this.Value = Math.Min(this.Maximum, this.Value * 2);
            return;
        }

        base.UpButton();
    }

    public override void DownButton()
    {
        if ((Control.ModifierKeys & Keys.Control) == Keys.Control)
        {
            this.Value = Math.Max(this.Minimum, this.Value / 2);
            return;
        }

        base.DownButton();
    }
}
