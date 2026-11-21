#nullable enable
namespace ModularAudience.Forms.Modules.Dialogs;

partial class MixPatternAtomizeDialog
{
    private System.ComponentModel.IContainer components = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.components?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        this.components = new System.ComponentModel.Container();
        this.label_info = new Label();
        this.label_sensitivity = new Label();
        this.comboBox_sensitivity = new ComboBox();
        this.label_minSlice = new Label();
        this.numeric_minSlice = new NumericUpDown();
        this.label_tail = new Label();
        this.numeric_tail = new NumericUpDown();
        this.label_minRms = new Label();
        this.numeric_minRms = new NumericUpDown();
        this.label_minDuration = new Label();
        this.numeric_minDuration = new NumericUpDown();
        this.checkBox_singleFallback = new CheckBox();
        this.button_ok = new Button();
        this.button_cancel = new Button();
        this.SuspendLayout();

        this.label_info.AutoSize = true;
        this.label_info.Location = new Point(14, 14);
        this.label_info.MaximumSize = new Size(420, 0);
        this.label_info.Text = "At least one selected segment contains multiple onsets. Configure one atomic pass for all selected segments.";

        this.label_sensitivity.AutoSize = true;
        this.label_sensitivity.Location = new Point(14, 70);
        this.label_sensitivity.Text = "Sensitivity";
        this.comboBox_sensitivity.DropDownStyle = ComboBoxStyle.DropDownList;
        this.comboBox_sensitivity.Items.AddRange(new object[] { "Conservative", "Balanced", "Aggressive" });
        this.comboBox_sensitivity.Location = new Point(160, 66);
        this.comboBox_sensitivity.Size = new Size(150, 23);
        this.comboBox_sensitivity.SelectedIndex = 1;

        AddNumber(this.label_minSlice, "Minimum slice (ms)", this.numeric_minSlice, 104, 0, 5000, 60);
        AddNumber(this.label_tail, "Tail padding (ms)", this.numeric_tail, 138, 0, 1000, 30);
        AddNumber(this.label_minRms, "Minimum RMS", this.numeric_minRms, 172, 0, 1, 0.001M);
        this.numeric_minRms.DecimalPlaces = 4;
        this.numeric_minRms.Increment = 0.001M;
        AddNumber(this.label_minDuration, "Minimum duration (ms)", this.numeric_minDuration, 206, 0, 10000, 20);

        this.checkBox_singleFallback.AutoSize = true;
        this.checkBox_singleFallback.Checked = true;
        this.checkBox_singleFallback.Location = new Point(14, 244);
        this.checkBox_singleFallback.Text = "Allow single-atom fallback";

        this.button_ok.DialogResult = DialogResult.OK;
        this.button_ok.Location = new Point(260, 282);
        this.button_ok.Size = new Size(78, 27);
        this.button_ok.Text = "Atomize";
        this.button_cancel.DialogResult = DialogResult.Cancel;
        this.button_cancel.Location = new Point(345, 282);
        this.button_cancel.Size = new Size(78, 27);
        this.button_cancel.Text = "Cancel";

        this.AcceptButton = this.button_ok;
        this.CancelButton = this.button_cancel;
        this.ClientSize = new Size(438, 326);
        this.Controls.AddRange(new Control[]
        {
            this.label_info,
            this.label_sensitivity,
            this.comboBox_sensitivity,
            this.label_minSlice,
            this.numeric_minSlice,
            this.label_tail,
            this.numeric_tail,
            this.label_minRms,
            this.numeric_minRms,
            this.label_minDuration,
            this.numeric_minDuration,
            this.checkBox_singleFallback,
            this.button_ok,
            this.button_cancel
        });
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.StartPosition = FormStartPosition.CenterParent;
        this.Text = "Atomize Mix Selection";
        this.ResumeLayout(false);
        this.PerformLayout();

        void AddNumber(
            Label label,
            string text,
            NumericUpDown numeric,
            int y,
            decimal minimum,
            decimal maximum,
            decimal value)
        {
            label.AutoSize = true;
            label.Location = new Point(14, y + 4);
            label.Text = text;
            numeric.Location = new Point(160, y);
            numeric.Size = new Size(150, 23);
            numeric.Minimum = minimum;
            numeric.Maximum = maximum;
            numeric.Value = value;
        }
    }

    private Label label_info = null!;
    private Label label_sensitivity = null!;
    private ComboBox comboBox_sensitivity = null!;
    private Label label_minSlice = null!;
    private NumericUpDown numeric_minSlice = null!;
    private Label label_tail = null!;
    private NumericUpDown numeric_tail = null!;
    private Label label_minRms = null!;
    private NumericUpDown numeric_minRms = null!;
    private Label label_minDuration = null!;
    private NumericUpDown numeric_minDuration = null!;
    private CheckBox checkBox_singleFallback = null!;
    private Button button_ok = null!;
    private Button button_cancel = null!;
}
