using System.ComponentModel;

namespace ModularAudience.Forms.Modules.Dialogs;

partial class YoutubeRecoveryDialog
{
    private IContainer? components;
    private TextBox diagnosticsText = null!;
    private ComboBox actionCombo = null!;
    private ComboBox browserCombo = null!;
    private TextBox profileText = null!;
    private CheckBox consentCheck = null!;
    private Label privacyLabel = null!;
    private Label validationLabel = null!;
    private Button retryButton = null!;
    private FlowLayoutPanel sessionPanel = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new Container();
        SuspendLayout();
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 8
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        for (int row = 2; row < 8; row++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label
        {
            AutoSize = true, Dock = DockStyle.Fill,
            Text = "YouTube recovery — only matching failed items will be retried. Successful imports are never repeated.\nOne assisted retry is allowed; rate limits, DRM and access restrictions are not bypassed.",
            Padding = new Padding(0, 0, 0, 8)
        }, 0, 0);
        diagnosticsText = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
            WordWrap = false, BackColor = SystemColors.Window, AccessibleName = "Copyable recovery diagnostics"
        };
        layout.Controls.Add(diagnosticsText, 0, 1);
        actionCombo = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Recovery action" };
        actionCombo.Items.AddRange(["Retry same settings (fresh media URLs)", "Retry with system certificate trust (verification remains enabled)", "Retry with IPv4 (explicit route change)", "Retry using a browser session (explicit consent required)"]);
        actionCombo.SelectedIndexChanged += Action_Changed;
        layout.Controls.Add(actionCombo, 0, 2);
        sessionPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        browserCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110, AccessibleName = "Browser for this retry" };
        browserCombo.Items.AddRange(["Edge", "Chrome", "Firefox"]);
        browserCombo.SelectedIndex = 0;
        profileText = new TextBox { Width = 230, MaxLength = 120, Text = "Default", AccessibleName = "Local browser profile name" };
        sessionPanel.Controls.Add(new Label { AutoSize = true, Text = "Browser", Margin = new Padding(3, 6, 3, 0) });
        sessionPanel.Controls.Add(browserCombo);
        sessionPanel.Controls.Add(new Label { AutoSize = true, Text = "Profile", Margin = new Padding(3, 6, 3, 0) });
        sessionPanel.Controls.Add(profileText);
        layout.Controls.Add(sessionPanel, 0, 3);
        privacyLabel = new Label
        {
            AutoSize = true, Dock = DockStyle.Fill,
            Text = "yt-dlp will read this profile's cookie database as the current Windows user; it can contain other websites' sessions.\nRelevant cookies are used for requests. No cookie-file export or cookie-value logging is requested.\nTemporary database copies stay in the job folder and are cleaned after process exit; cleanup failures are reported.\nThis retry operation can include one automatic repeat. Browser/Windows encryption is not bypassed.\nUse Default / Profile 1 for Chromium, or abc.default-release for Firefox. Paths are not accepted.",
            Padding = new Padding(0, 4, 0, 4)
        };
        layout.Controls.Add(privacyLabel, 0, 4);
        consentCheck = new CheckBox
        {
            Text = "I authorize reading this browser profile's cookies for this retry only.", AutoSize = true, Dock = DockStyle.Top
        };
        consentCheck.CheckedChanged += Consent_Changed;
        layout.Controls.Add(consentCheck, 0, 5);
        validationLabel = new Label { AutoSize = true, Dock = DockStyle.Fill, ForeColor = Color.DarkRed };
        layout.Controls.Add(validationLabel, 0, 6);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        var cancelButton = new Button { Text = "Keep failures / close", AutoSize = true, DialogResult = DialogResult.Cancel };
        retryButton = new Button { Text = "Retry matching failures", AutoSize = true };
        retryButton.Click += Retry_Click;
        var copyButton = new Button { Text = "Copy diagnostics", AutoSize = true };
        copyButton.Click += Copy_Click;
        buttons.Controls.AddRange([cancelButton, retryButton, copyButton]);
        layout.Controls.Add(buttons, 0, 7);
        Controls.Add(layout);
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(850, 650);
        MinimumSize = new Size(760, 600);
        Text = "YouTube recovery options";
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;
        AcceptButton = retryButton;
        CancelButton = cancelButton;
        ResumeLayout(true);
    }
}
