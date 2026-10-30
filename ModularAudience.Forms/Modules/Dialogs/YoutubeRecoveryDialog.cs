using ModularAudience.Ytdlp;

namespace ModularAudience.Forms.Modules.Dialogs;

internal sealed partial class YoutubeRecoveryDialog : Form
{
    private readonly YtdlpRecoveryAction[] availableActions;
    internal YtdlpRecoveryDecision? Decision { get; private set; }
    private CancellationTokenSource? bridgeCancellation;

    internal YoutubeRecoveryDialog(IReadOnlyList<YtdlpProcessException> errors, YtdlpDownloadOptions options, string diagnostics)
    {
        InitializeComponent();
        diagnosticsText.Text = YtdlpDiagnosticRedactor.Redact(diagnostics);
        availableActions = Enum.GetValues<YtdlpRecoveryAction>()
            .Where(action => errors.Any(error => YtdlpRecoveryRules.CanApply(error, action, options))).ToArray();
        for (int index = actionCombo.Items.Count - 1; index >= 0; index--)
            if (!availableActions.Contains((YtdlpRecoveryAction)index)) actionCombo.Items.RemoveAt(index);
        if (errors.Any(error => error.FailureKind == YtdlpFailureKind.BrowserCookies))
            cookieFileCheck.Checked = true;
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
        browserCombo.Enabled = profileText.Enabled = !cookieFileCheck.Checked;
        cookieFileText.Enabled = cookieBrowseButton.Enabled = cookieFileCheck.Checked;
        retryButton.Enabled = actionCombo.SelectedIndex >= 0 && (!UsesBrowser ||
            (consentCheck.Checked && (!cookieFileCheck.Checked || !string.IsNullOrWhiteSpace(cookieFileText.Text))));
    }

    private void CookieSource_Changed(object? sender, EventArgs e)
    {
        consentCheck.Checked = false;
        UpdateAvailability();
    }

    private void CookieBrowse_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog { Title = "Select exported YouTube cookies (Netscape format)",
            Filter = "Cookie text files (*.txt)|*.txt|All files (*.*)|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        cookieFileText.Text = dialog.FileName;
        consentCheck.Checked = false;
        UpdateAvailability();
    }

    private async void ConnectChrome_Click(object? sender, EventArgs e)
    {
        if (bridgeCancellation is not null) return;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        bridgeCancellation = cancellation;
        try
        {
            string directory = YoutubeChromeBridge.InstallExtensionFiles();
            try { Clipboard.SetText(directory); }
            catch (System.Runtime.InteropServices.ExternalException) { /* The connection page also offers Copy folder. */ }
            connectChromeButton.Enabled = false;
            retryButton.Enabled = false;
            validationLabel.Text = "Chrome opens the connection and extensions tabs. Extension folder copied to clipboard. Enable Developer mode → Load unpacked → Ctrl+V in the folder dialog → Select folder. Then reload the connection tab. Connecting authorizes YouTube cookies for this window's downloads.";
            var session = await YoutubeChromeBridge.ConnectAsync(cancellation.Token);
            if (IsDisposed) return;
            bridgeCancellation = null;
            Decision = new(YtdlpRecoveryAction.BrowserSession, session);
            DialogResult = DialogResult.OK;
        }
        catch (OperationCanceledException)
        { if (!IsDisposed) validationLabel.Text = "Chrome connection cancelled or timed out. You can connect again."; }
        catch (Exception error)
        { if (!IsDisposed) validationLabel.Text = "Chrome connection failed: " + error.Message; }
        finally
        {
            bridgeCancellation = null;
            if (!IsDisposed) { connectChromeButton.Enabled = true; UpdateAvailability(); }
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        bridgeCancellation?.Cancel();
        base.OnFormClosing(e);
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
                CookieFilePath = cookieFileCheck.Checked ? cookieFileText.Text : null,
                ConsentGranted = consentCheck.Checked
            } : null;
            session?.Validate();
            Decision = new(availableActions[actionCombo.SelectedIndex], session);
            DialogResult = DialogResult.OK;
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
        { validationLabel.Text = error.Message; }
    }

    private void Copy_Click(object? sender, EventArgs e)
    {
        try { Clipboard.SetText(diagnosticsText.Text); }
        catch (Exception error) { validationLabel.Text = "Unable to copy diagnostics: " + error.Message; }
    }
}
