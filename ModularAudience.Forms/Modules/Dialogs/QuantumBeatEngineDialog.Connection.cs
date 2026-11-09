using ModularAudience.Forms.Quantum;
using ModularAudience.Quantum.Models;

namespace ModularAudience.Forms.Modules.Dialogs;

public partial class QuantumBeatEngineDialog
{
    private QuantumConnectionSettings CaptureConnection() => new()
    {
        ProfileName = string.IsNullOrWhiteSpace(this.connectionProfileCombo.Text) ? "Default" : this.connectionProfileCombo.Text.Trim(),
        Authentication = this.authCombo.SelectedIndex == 1 ? QuantumAuthenticationMode.IamBearerToken : QuantumAuthenticationMode.ApiKey,
        Secret = this.credentialText.Text.Trim(), ServiceCrn = this.crnText.Text.Trim(), Backend = this.backendCombo.Text.Trim(),
        BaseAddress = this.endpointCombo.Text.Trim(), ApiVersion = this.apiVersionText.Text.Trim(), PythonExecutable = this.pythonText.Text.Trim(),
        RequestTimeoutSeconds = (int)this.N("RequestTimeout"), JobTimeoutSeconds = (int)this.N("JobTimeout"),
        PollIntervalMs = (int)this.N("PollInterval"), ReadRetries = (int)this.N("ReadRetries")
    };

    private void ApplyConnection(QuantumConnectionSettings settings)
    {
        bool previous = this.loading; this.loading = true;
        try
        {
            this.connectionProfileCombo.Text = settings.ProfileName; this.authCombo.SelectedIndex = settings.Authentication == QuantumAuthenticationMode.IamBearerToken ? 1 : 0;
            this.credentialText.Text = settings.Secret; this.crnText.Text = settings.ServiceCrn; this.backendCombo.Text = settings.Backend;
            this.endpointCombo.Text = settings.BaseAddress; this.apiVersionText.Text = settings.ApiVersion; this.pythonText.Text = settings.PythonExecutable;
            this.SetNumber("RequestTimeout", settings.RequestTimeoutSeconds); this.SetNumber("JobTimeout", settings.JobTimeoutSeconds);
            this.SetNumber("PollInterval", settings.PollIntervalMs); this.SetNumber("ReadRetries", settings.ReadRetries);
            this.checks["AllowCloudJobs"].Checked = false; this.checks["ShowCredential"].Checked = false; this.credentialText.UseSystemPasswordChar = true;
        }
        finally { this.loading = previous; }
        this.connectionDirty = false;
    }

    private void ConnectionChanged()
    {
        if (this.loading || this.closing || this.released) return;
        this.session.Disconnect(); this.checks["AllowCloudJobs"].Checked = false; this.connectionDirty = true;
        this.SetStatus("Connection settings changed. Test / Connect again before submitting an IBM job.");
        this.ScheduleSave(); this.UpdateAvailability();
    }

    internal Task ConnectAsync() => this.RunWorkAsync(async token =>
    {
        var settings = this.CaptureConnection();
        if (settings.Secret.Length == 0 || settings.ServiceCrn.Length == 0)
            throw new ArgumentException("Enter an API key or IAM bearer token and an instance CRN.");
        this.SetStatus("Checking IBM authentication and reading accessible backends. No job is being submitted...");
        var backends = await this.session.ConnectAsync(settings.ToOptions(), token);
        token.ThrowIfCancellationRequested();
        this.loading = true;
        try
        {
            this.backendCombo.Items.Clear(); this.backendCombo.Items.AddRange(backends.Select(backend => (object)backend.Name).ToArray());
            this.backendCombo.Text = this.session.ConnectedBackend;
            this.backendsGrid.DataSource = backends.ToArray();
        }
        finally { this.loading = false; }
        this.connectionDirty = true; this.SaveConnectionIfChanged(); this.ScheduleSave();
        this.SetStatus($"IBM connection established. {backends.Count} backend(s) accessible. No quantum job was submitted. Enable cloud-job permission when ready.");
    });

    private void Disconnect()
    {
        if (this.busy) return;
        this.session.Disconnect(); this.checks["AllowCloudJobs"].Checked = false; this.UpdateAvailability();
        this.SetStatus("Disconnected. Saved encrypted profiles were kept. Local simulation remains available.");
    }

    private void SaveConnection()
    {
        if (this.busy) return;
        try
        {
            this.connectionDirty = true; this.SaveConnectionIfChanged();
            this.SetStatus("Connection profile saved in AppData JSON. The credential is protected with Windows DPAPI for this user.");
        }
        catch (Exception error) { this.ShowError(error); }
    }
    private void SaveConnectionIfChanged()
    {
        if (!this.connectionDirty) return;
        this.store.SaveConnection(this.CaptureConnection()); this.connectionDirty = false; this.RefreshConnectionProfileNames();
    }
    private void RefreshConnectionProfileNames()
    {
        string selected = this.connectionProfileCombo.Text;
        bool previous = this.loading; this.loading = true;
        try { this.connectionProfileCombo.Items.Clear(); this.connectionProfileCombo.Items.AddRange(this.store.ConnectionNames()); this.connectionProfileCombo.Text = selected; }
        finally { this.loading = previous; }
    }
    private void LoadSelectedConnection()
    {
        if (this.busy) return;
        try
        {
            this.session.Disconnect(); this.ApplyConnection(this.store.LoadConnection(this.connectionProfileCombo.Text.Trim()));
            this.UpdateAvailability(); this.ScheduleSave();
            this.SetStatus("Encrypted connection profile loaded. Test / Connect explicitly; no network request has been made.");
        }
        catch (Exception error) { this.ShowError(error); }
    }
    private void ForgetConnection()
    {
        if (this.busy) return;
        try
        {
            this.store.DeleteConnection(this.connectionProfileCombo.Text.Trim()); this.session.Disconnect();
            this.loading = true; this.credentialText.Clear(); this.checks["AllowCloudJobs"].Checked = false; this.loading = false;
            this.connectionDirty = false; this.RefreshConnectionProfileNames(); this.UpdateAvailability();
            this.SetStatus("Saved connection profile deleted and in-memory credentials cleared. IBM platform jobs were not cancelled.");
        }
        catch (Exception error) { this.loading = false; this.ShowError(error); }
    }
}
