#nullable enable

namespace ModularAudience.Forms.Modules.Dialogs;

partial class BreakbeatBeatRecordingDialog
{
    private System.ComponentModel.IContainer? components = null;
    private Label label_inputDevice = null!;
    private ComboBox comboBox_inputDevice = null!;
    private Label label_status = null!;
    private Label label_elapsed = null!;
    private ProgressBar progressBar_inputLevel = null!;
    private Label label_inputLevel = null!;
    private Button button_stop = null!;
    private Button button_cancel = null!;
    private System.Windows.Forms.Timer timer_recording = null!;

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
        this.label_inputDevice = new Label();
        this.comboBox_inputDevice = new ComboBox();
        this.label_status = new Label();
        this.label_elapsed = new Label();
        this.progressBar_inputLevel = new ProgressBar();
        this.label_inputLevel = new Label();
        this.button_stop = new Button();
        this.button_cancel = new Button();
        this.timer_recording = new System.Windows.Forms.Timer(this.components);
        this.SuspendLayout();
        // 
        // label_inputDevice
        // 
        this.label_inputDevice.AutoSize = true;
        this.label_inputDevice.Location = new Point(14, 16);
        this.label_inputDevice.Name = "label_inputDevice";
        this.label_inputDevice.Size = new Size(75, 15);
        this.label_inputDevice.TabIndex = 0;
        this.label_inputDevice.Text = "Input device:";
        // 
        // comboBox_inputDevice
        // 
        this.comboBox_inputDevice.DropDownStyle = ComboBoxStyle.DropDownList;
        this.comboBox_inputDevice.FormattingEnabled = true;
        this.comboBox_inputDevice.Location = new Point(98, 12);
        this.comboBox_inputDevice.Name = "comboBox_inputDevice";
        this.comboBox_inputDevice.Size = new Size(354, 23);
        this.comboBox_inputDevice.TabIndex = 1;
        this.comboBox_inputDevice.SelectedIndexChanged += this.comboBox_inputDevice_SelectedIndexChanged;
        // 
        // label_status
        // 
        this.label_status.AutoSize = true;
        this.label_status.Location = new Point(14, 49);
        this.label_status.Name = "label_status";
        this.label_status.Size = new Size(194, 15);
        this.label_status.TabIndex = 2;
        this.label_status.Text = "Select an input device to start recording.";
        // 
        // label_elapsed
        // 
        this.label_elapsed.AutoSize = true;
        this.label_elapsed.Location = new Point(14, 78);
        this.label_elapsed.Name = "label_elapsed";
        this.label_elapsed.Size = new Size(145, 15);
        this.label_elapsed.TabIndex = 3;
        this.label_elapsed.Text = "Recording time: 00:00.000";
        // 
        // progressBar_inputLevel
        // 
        this.progressBar_inputLevel.Location = new Point(14, 106);
        this.progressBar_inputLevel.Maximum = 1000;
        this.progressBar_inputLevel.Name = "progressBar_inputLevel";
        this.progressBar_inputLevel.Size = new Size(300, 20);
        this.progressBar_inputLevel.TabIndex = 4;
        // 
        // label_inputLevel
        // 
        this.label_inputLevel.AutoSize = true;
        this.label_inputLevel.Location = new Point(322, 109);
        this.label_inputLevel.Name = "label_inputLevel";
        this.label_inputLevel.Size = new Size(130, 15);
        this.label_inputLevel.TabIndex = 5;
        this.label_inputLevel.Text = "Mic level";
        // 
        // button_stop
        // 
        this.button_stop.Location = new Point(290, 145);
        this.button_stop.Name = "button_stop";
        this.button_stop.Size = new Size(78, 28);
        this.button_stop.TabIndex = 6;
        this.button_stop.Text = "Stop";
        this.button_stop.UseVisualStyleBackColor = true;
        this.button_stop.Click += this.button_stop_Click;
        // 
        // button_cancel
        // 
        this.button_cancel.DialogResult = DialogResult.Cancel;
        this.button_cancel.Location = new Point(374, 145);
        this.button_cancel.Name = "button_cancel";
        this.button_cancel.Size = new Size(78, 28);
        this.button_cancel.TabIndex = 7;
        this.button_cancel.Text = "Cancel";
        this.button_cancel.UseVisualStyleBackColor = true;
        this.button_cancel.Click += this.button_cancel_Click;
        // 
        // timer_recording
        // 
        this.timer_recording.Interval = 250;
        this.timer_recording.Tick += this.timer_recording_Tick;
        // 
        // BreakbeatBeatRecordingDialog
        // 
        this.AcceptButton = this.button_stop;
        this.CancelButton = this.button_cancel;
        this.AutoScaleDimensions = new SizeF(7F, 15F);
        this.AutoScaleMode = AutoScaleMode.Font;
        this.ClientSize = new Size(466, 188);
        this.Controls.Add(this.button_cancel);
        this.Controls.Add(this.button_stop);
        this.Controls.Add(this.label_inputLevel);
        this.Controls.Add(this.progressBar_inputLevel);
        this.Controls.Add(this.label_elapsed);
        this.Controls.Add(this.label_status);
        this.Controls.Add(this.comboBox_inputDevice);
        this.Controls.Add(this.label_inputDevice);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.Name = "BreakbeatBeatRecordingDialog";
        this.StartPosition = FormStartPosition.CenterParent;
        this.Text = "Record Beat";
        this.Shown += this.BreakbeatBeatRecordingDialog_Shown;
        this.FormClosing += this.BreakbeatBeatRecordingDialog_FormClosing;
        this.ResumeLayout(false);
        this.PerformLayout();
    }
}
