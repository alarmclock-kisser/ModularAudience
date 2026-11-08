#nullable enable

namespace ModularAudience.Forms.Updates;

partial class ReleaseUpdateDialog
{
    private System.ComponentModel.IContainer? components;
    private Label messageLabel = null!;
    private CheckBox dontAskCheckBox = null!;
    private ProgressBar downloadProgressBar = null!;
    private Label statusLabel = null!;
    private Button updateButton = null!;
    private Button laterButton = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            activeDownload?.Cancel();
            components?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        messageLabel = new Label();
        dontAskCheckBox = new CheckBox();
        downloadProgressBar = new ProgressBar();
        statusLabel = new Label();
        updateButton = new Button();
        laterButton = new Button();
        SuspendLayout();
        messageLabel.Location = new Point(14, 14);
        messageLabel.Name = "messageLabel";
        messageLabel.Size = new Size(492, 68);
        messageLabel.TabIndex = 0;
        dontAskCheckBox.AutoSize = true;
        dontAskCheckBox.Location = new Point(17, 86);
        dontAskCheckBox.Name = "dontAskCheckBox";
        dontAskCheckBox.Size = new Size(207, 19);
        dontAskCheckBox.TabIndex = 1;
        dontAskCheckBox.Text = "Don't ask again at startup";
        downloadProgressBar.Location = new Point(17, 118);
        downloadProgressBar.Name = "downloadProgressBar";
        downloadProgressBar.Size = new Size(486, 23);
        downloadProgressBar.TabIndex = 2;
        downloadProgressBar.Visible = false;
        statusLabel.Location = new Point(17, 148);
        statusLabel.Name = "statusLabel";
        statusLabel.Size = new Size(486, 32);
        statusLabel.TabIndex = 3;
        updateButton.Location = new Point(314, 188);
        updateButton.Name = "updateButton";
        updateButton.Size = new Size(91, 28);
        updateButton.TabIndex = 4;
        updateButton.Text = "Update now";
        updateButton.UseVisualStyleBackColor = true;
        updateButton.Click += Update_Click;
        laterButton.Location = new Point(412, 188);
        laterButton.Name = "laterButton";
        laterButton.Size = new Size(91, 28);
        laterButton.TabIndex = 5;
        laterButton.Text = "Not now";
        laterButton.UseVisualStyleBackColor = true;
        laterButton.Click += Later_Click;
        AcceptButton = updateButton;
        CancelButton = laterButton;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(520, 232);
        Controls.Add(messageLabel);
        Controls.Add(dontAskCheckBox);
        Controls.Add(downloadProgressBar);
        Controls.Add(statusLabel);
        Controls.Add(updateButton);
        Controls.Add(laterButton);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "ReleaseUpdateDialog";
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "Application update";
        ResumeLayout(false);
        PerformLayout();
    }
}
