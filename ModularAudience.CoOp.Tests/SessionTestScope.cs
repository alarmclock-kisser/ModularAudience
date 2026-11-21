using ModularAudience.CoOp.Synchronization;

namespace ModularAudience.CoOp.Tests;

internal sealed class SessionTestScope : IAsyncDisposable
{
    private readonly List<CoOpSessionManager> managers = [];

    public string Root { get; } = Path.Combine(
        Path.GetTempPath(),
        "ModularAudience-CoOp-Tests",
        Guid.NewGuid().ToString("N"));

    public CoOpSessionManager CreateManager(
        string displayName,
        string password = "test-session-password",
        Func<string, int, string, bool>? confirmCertificate = null,
        Func<string, int, bool>? confirmPasswordlessSession = null)
    {
        var manager = new CoOpSessionManager(new CoOpSessionOptions
        {
            DisplayName = displayName,
            PreferredPort = 0,
            EnableDiscoveryResponder = false,
            RemoteCollectionsRoot = this.Root,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            SessionPassword = password,
            CertificatePath = Path.Combine(this.Root, "host-certificate.pfx"),
            ConfirmServerCertificate = confirmCertificate ?? ((_, _, _) => true),
            ConfirmPasswordlessSession = confirmPasswordlessSession,
        });

        this.managers.Add(manager);
        return manager;
    }

    public async ValueTask DisposeAsync()
    {
        for (int i = this.managers.Count - 1; i >= 0; i--)
        {
            try { await this.managers[i].DisposeAsync(); } catch { }
        }

        try
        {
            if (Directory.Exists(this.Root))
            {
                Directory.Delete(this.Root, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup of temporary remote-collection data.
        }
    }
}
