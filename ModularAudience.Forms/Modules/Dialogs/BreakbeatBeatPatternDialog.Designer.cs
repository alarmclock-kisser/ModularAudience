#nullable enable

namespace ModularAudience.Forms.Modules.Dialogs;

partial class BreakbeatBeatPatternDialog
{
    private System.ComponentModel.IContainer? components = null;
    private Label label_bpm = null!;
    private NumericUpDown numericUpDown_bpm = null!;
    private Label label_resolution = null!;
    private ComboBox comboBox_resolution = null!;
    private Label label_drumElements = null!;
    private NumericUpDown numericUpDown_drumElements = null!;
    private Label label_groupingSummary = null!;
    private CheckBox checkBox_createLines = null!;
    private Label label_trackSummary = null!;
    private Button button_ok = null!;
    private Button button_cancel = null!;

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
        this.label_bpm = new Label();
        this.numericUpDown_bpm = new NumericUpDown();
        this.label_resolution = new Label();
        this.comboBox_resolution = new ComboBox();
        this.label_drumElements = new Label();
        this.numericUpDown_drumElements = new NumericUpDown();
        this.label_groupingSummary = new Label();
        this.checkBox_createLines = new CheckBox();
        this.label_trackSummary = new Label();
        this.button_ok = new Button();
        this.button_cancel = new Button();
        ((System.ComponentModel.ISupportInitialize)this.numericUpDown_bpm).BeginInit();
        ((System.ComponentModel.ISupportInitialize)this.numericUpDown_drumElements).BeginInit();
        this.SuspendLayout();
        // 
        // label_bpm
        // 
        this.label_bpm.AutoSize = true;
        this.label_bpm.Location = new Point(14, 17);
        this.label_bpm.Name = "label_bpm";
        this.label_bpm.Size = new Size(78, 15);
        this.label_bpm.TabIndex = 0;
        this.label_bpm.Text = "Estimated BPM:";
        // 
        // numericUpDown_bpm
        // 
        this.numericUpDown_bpm.DecimalPlaces = 2;
        this.numericUpDown_bpm.Increment = 0.5m;
        this.numericUpDown_bpm.Location = new Point(134, 13);
        this.numericUpDown_bpm.Maximum = 240m;
        this.numericUpDown_bpm.Minimum = 40m;
        this.numericUpDown_bpm.Name = "numericUpDown_bpm";
        this.numericUpDown_bpm.Size = new Size(110, 23);
        this.numericUpDown_bpm.TabIndex = 1;
        this.numericUpDown_bpm.Value = 120m;
        // 
        // label_resolution
        // 
        this.label_resolution.AutoSize = true;
        this.label_resolution.Location = new Point(14, 53);
        this.label_resolution.Name = "label_resolution";
        this.label_resolution.Size = new Size(63, 15);
        this.label_resolution.TabIndex = 2;
        this.label_resolution.Text = "Resolution:";
        // 
        // comboBox_resolution
        // 
        this.comboBox_resolution.DropDownStyle = ComboBoxStyle.DropDownList;
        this.comboBox_resolution.FormattingEnabled = true;
        this.comboBox_resolution.Location = new Point(134, 49);
        this.comboBox_resolution.Name = "comboBox_resolution";
        this.comboBox_resolution.Size = new Size(110, 23);
        this.comboBox_resolution.TabIndex = 3;
        // 
        // label_drumElements
        // 
        this.label_drumElements.AutoSize = true;
        this.label_drumElements.Location = new Point(14, 89);
        this.label_drumElements.Name = "label_drumElements";
        this.label_drumElements.Size = new Size(85, 15);
        this.label_drumElements.TabIndex = 4;
        this.label_drumElements.Text = "Drum elements:";
        // 
        // numericUpDown_drumElements
        // 
        this.numericUpDown_drumElements.Location = new Point(134, 85);
        this.numericUpDown_drumElements.Maximum = 16m;
        this.numericUpDown_drumElements.Minimum = 1m;
        this.numericUpDown_drumElements.Name = "numericUpDown_drumElements";
        this.numericUpDown_drumElements.Size = new Size(110, 23);
        this.numericUpDown_drumElements.TabIndex = 5;
        this.numericUpDown_drumElements.Value = 1m;
        this.numericUpDown_drumElements.ValueChanged += this.numericUpDown_drumElements_ValueChanged;
        // 
        // label_groupingSummary
        // 
        this.label_groupingSummary.AutoSize = true;
        this.label_groupingSummary.Location = new Point(14, 122);
        this.label_groupingSummary.Name = "label_groupingSummary";
        this.label_groupingSummary.Size = new Size(171, 15);
        this.label_groupingSummary.TabIndex = 6;
        this.label_groupingSummary.Text = "Detected taps and drum groups.";
        // 
        // checkBox_createLines
        // 
        this.checkBox_createLines.AutoSize = true;
        this.checkBox_createLines.Location = new Point(14, 151);
        this.checkBox_createLines.Name = "checkBox_createLines";
        this.checkBox_createLines.Size = new Size(215, 19);
        this.checkBox_createLines.TabIndex = 7;
        this.checkBox_createLines.Text = "Create new lines to fit drum count";
        this.checkBox_createLines.UseVisualStyleBackColor = true;
        // 
        // label_trackSummary
        // 
        this.label_trackSummary.AutoSize = true;
        this.label_trackSummary.Location = new Point(14, 177);
        this.label_trackSummary.Name = "label_trackSummary";
        this.label_trackSummary.Size = new Size(239, 15);
        this.label_trackSummary.TabIndex = 8;
        this.label_trackSummary.Text = "Existing tracks are assigned by order.";
        // 
        // button_ok
        // 
        this.button_ok.DialogResult = DialogResult.OK;
        this.button_ok.Location = new Point(313, 210);
        this.button_ok.Name = "button_ok";
        this.button_ok.Size = new Size(75, 28);
        this.button_ok.TabIndex = 9;
        this.button_ok.Text = "OK";
        this.button_ok.UseVisualStyleBackColor = true;
        // 
        // button_cancel
        // 
        this.button_cancel.DialogResult = DialogResult.Cancel;
        this.button_cancel.Location = new Point(394, 210);
        this.button_cancel.Name = "button_cancel";
        this.button_cancel.Size = new Size(75, 28);
        this.button_cancel.TabIndex = 10;
        this.button_cancel.Text = "Cancel";
        this.button_cancel.UseVisualStyleBackColor = true;
        // 
        // BreakbeatBeatPatternDialog
        // 
        this.AcceptButton = this.button_ok;
        this.CancelButton = this.button_cancel;
        this.AutoScaleDimensions = new SizeF(7F, 15F);
        this.AutoScaleMode = AutoScaleMode.Font;
        this.ClientSize = new Size(484, 252);
        this.Controls.Add(this.button_cancel);
        this.Controls.Add(this.button_ok);
        this.Controls.Add(this.label_trackSummary);
        this.Controls.Add(this.checkBox_createLines);
        this.Controls.Add(this.label_groupingSummary);
        this.Controls.Add(this.numericUpDown_drumElements);
        this.Controls.Add(this.label_drumElements);
        this.Controls.Add(this.comboBox_resolution);
        this.Controls.Add(this.label_resolution);
        this.Controls.Add(this.numericUpDown_bpm);
        this.Controls.Add(this.label_bpm);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.Name = "BreakbeatBeatPatternDialog";
        this.StartPosition = FormStartPosition.CenterParent;
        this.Text = "Record Beat Pattern";
        ((System.ComponentModel.ISupportInitialize)this.numericUpDown_bpm).EndInit();
        ((System.ComponentModel.ISupportInitialize)this.numericUpDown_drumElements).EndInit();
        this.ResumeLayout(false);
        this.PerformLayout();
    }
}
