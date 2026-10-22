using ModularAudience.Ytdlp;

namespace ModularAudience.Forms.Modules.Dialogs;

internal sealed partial class YoutubeRecoveryDialog : Form
{
    private readonly YtdlpRecoveryAction[] availableActions;
    internal YtdlpRecoveryDecision? Decision { get; private set; }

    internal YoutubeRecoveryDialog(IReadOnlyList<YtdlpProcessException> errors, YtdlpDownloadOptions options, string diagnostics)
    {
        InitializeComponent();
        diagnosticsText.Text = YtdlpDiagnosticRedactor.Redact(diagnostics);
        availableActions = Enum.GetValues<YtdlpRecoveryAction>()
            .Where(action => errors.Any(error => YtdlpRecoveryRules.CanApply(error, action, options))).ToArray();
        for (int index = actionCombo.Items.Count - 1; index >= 0; index--)
            if (!availableActions.Contains((YtdlpRecoveryAction)index)) actionCombo.Items.RemoveAt(index);
        if (availableActions.Length > 0) actionCombo.SelectedIndex = 0;
        else
        {
            validationLabel.Text = "No safe automatic recovery applies. Review the diagnostics; browser cookies cannot repair TLS, rate limits or access restrictions.";
            UpdateAvailability();
        }
    }

    private bool UsesBrowser => actionCombo.SelectedIndex >= 0 &&
        availableActions[actionCombo.SelectedIndex] == YtdlpRecoveryAction.BrowserSession;

    private void Action_Changed(object? sender, EventArgs e)
    {
        consentCheck.Checked = false;
        validationLabel.Text = string.Empty;
        UpdateAvailability();
    }

    private void Consent_Changed(object? sender, EventArgs e) => UpdateAvailability();

    private void UpdateAvailability()
    {
        sessionPanel.Visible = UsesBrowser;
        privacyLabel.Visible = UsesBrowser;
        consentCheck.Visible = UsesBrowser;
        retryButton.Enabled = actionCombo.SelectedIndex >= 0 && (!UsesBrowser || consentCheck.Checked);
    }

    private void Retry_Click(object? sender, EventArgs e)
    {
        if (!retryButton.Enabled || actionCombo.SelectedIndex < 0) return;
        try
        {
            YtdlpBrowserSession? session = UsesBrowser ? new()
            {
                Browser = (YtdlpBrowser)browserCombo.SelectedIndex,
                Profile = profileText.Text,
                ConsentGranted = consentCheck.Checked
            } : null;
            session?.Validate();
            Decision = new(availableActions[actionCombo.SelectedIndex], session);
            DialogResult = DialogResult.OK;
        }
        catch (ArgumentException error) { validationLabel.Text = error.Message; }
    }

    private void Copy_Click(object? sender, EventArgs e)
    {
        try { Clipboard.SetText(diagnosticsText.Text); }
        catch (Exception error) { validationLabel.Text = "Unable to copy diagnostics: " + error.Message; }
    }
}
