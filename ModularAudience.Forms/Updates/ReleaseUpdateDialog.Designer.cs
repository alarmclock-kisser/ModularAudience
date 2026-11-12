#nullable enable

namespace ModularAudience.Forms.Updates;

partial class ReleaseUpdateDialog
{
    private System.ComponentModel.IContainer? components;
    private Label messageLabel = null!;
    private Label historyLabel = null!;
    private TextBox historyTextBox = null!;
    private CheckBox wordWrapCheckBox = null!;
    private CheckBox dontAskCheckBox = null!;
    private ProgressBar downloadProgressBar = null!;
    private Label statusLabel = null!;
    private Button updateButton = null!;
    private Button skipButton = null!;
    private Button laterButton = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.CancelHistoryLoad();
            activeDownload?.Cancel();
            components?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        messageLabel = new Label();
        historyLabel = new Label();
        historyTextBox = new TextBox();
        wordWrapCheckBox = new CheckBox();
        dontAskCheckBox = new CheckBox();
        downloadProgressBar = new ProgressBar();
        statusLabel = new Label();
        updateButton = new Button();
        skipButton = new Button();
        laterButton = new Button();
        SuspendLayout();
        messageLabel.Location = new Point(14, 14);
        messageLabel.Name = "messageLabel";
        messageLabel.Size = new Size(692, 68);
        messageLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        messageLabel.TabIndex = 0;
        historyLabel.Location = new Point(17, 90);
        historyLabel.Name = "historyLabel";
        historyLabel.Size = new Size(686, 20);
        historyLabel.Text = "Commit messages (oldest build first)";
        historyLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        historyLabel.TabIndex = 1;
        historyTextBox.Location = new Point(17, 116);
        historyTextBox.Name = "historyTextBox";
        historyTextBox.Size = new Size(686, 250);
        historyTextBox.Multiline = true;
        historyTextBox.ReadOnly = true;
        historyTextBox.ScrollBars = ScrollBars.Both;
        historyTextBox.WordWrap = false;
        historyTextBox.AcceptsReturn = true;
        historyTextBox.MaxLength = int.MaxValue;
        historyTextBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        historyTextBox.TabIndex = 2;
        wordWrapCheckBox.AutoSize = true;
        wordWrapCheckBox.Location = new Point(604, 380);
        wordWrapCheckBox.Name = "wordWrapCheckBox";
        wordWrapCheckBox.Size = new Size(82, 19);
        wordWrapCheckBox.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        wordWrapCheckBox.TabIndex = 3;
        wordWrapCheckBox.Text = "Word wrap";
        wordWrapCheckBox.CheckedChanged += WordWrap_CheckedChanged;
        dontAskCheckBox.AutoSize = true;
        dontAskCheckBox.Location = new Point(17, 380);
        dontAskCheckBox.Name = "dontAskCheckBox";
        dontAskCheckBox.Size = new Size(207, 19);
        dontAskCheckBox.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        dontAskCheckBox.TabIndex = 4;
        dontAskCheckBox.Text = "Don't ask again at startup";
        dontAskCheckBox.CheckedChanged += DontAsk_CheckedChanged;
        downloadProgressBar.Location = new Point(17, 410);
        downloadProgressBar.Name = "downloadProgressBar";
        downloadProgressBar.Size = new Size(686, 23);
        downloadProgressBar.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        downloadProgressBar.TabIndex = 5;
        downloadProgressBar.Visible = false;
        statusLabel.Location = new Point(17, 440);
        statusLabel.Name = "statusLabel";
        statusLabel.Size = new Size(686, 32);
        statusLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        statusLabel.TabIndex = 6;
        updateButton.Location = new Point(416, 484);
        updateButton.Name = "updateButton";
        updateButton.Size = new Size(91, 28);
        updateButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        updateButton.TabIndex = 7;
        updateButton.Text = "Update now";
        updateButton.UseVisualStyleBackColor = true;
        updateButton.Click += Update_Click;
        skipButton.Location = new Point(514, 484);
        skipButton.Name = "skipButton";
        skipButton.Size = new Size(91, 28);
        skipButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        skipButton.TabIndex = 8;
        skipButton.Text = "Skip this";
        skipButton.UseVisualStyleBackColor = true;
        skipButton.Click += Skip_Click;
        laterButton.Location = new Point(612, 484);
        laterButton.Name = "laterButton";
        laterButton.Size = new Size(91, 28);
        laterButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        laterButton.TabIndex = 9;
        laterButton.Text = "Not now";
        laterButton.UseVisualStyleBackColor = true;
        laterButton.Click += Later_Click;
        AcceptButton = updateButton;
        CancelButton = laterButton;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(720, 526);
        Controls.Add(messageLabel);
        Controls.Add(historyLabel);
        Controls.Add(historyTextBox);
        Controls.Add(wordWrapCheckBox);
        Controls.Add(dontAskCheckBox);
        Controls.Add(downloadProgressBar);
        Controls.Add(statusLabel);
        Controls.Add(updateButton);
        Controls.Add(skipButton);
        Controls.Add(laterButton);
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(600, 440);
        MaximizeBox = true;
        MinimizeBox = false;
        Name = "ReleaseUpdateDialog";
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "Application update";
        ResumeLayout(false);
        PerformLayout();
    }
}
