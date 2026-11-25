using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Windows.Forms;
using ModularAudience.Audio;
using ModularAudience.Llama;

namespace ModularAudience.Forms.Modules.Dialogs;

/// <summary>
/// Trust-on-first-use for self-signed HTTPS llama-server certificates on remote hosts.
/// Loopback certificates are handled by <see cref="LlamaHttp.AcceptCertificate"/> and
/// never reach this class. A remote certificate is shown once with its SHA-256
/// fingerprint; if the user accepts, the fingerprint is pinned in userAppData.
/// </summary>
internal static class LlmCertificateTrust
{
    private static readonly object Gate = new();
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ModularAudience", "llm-certificate-trust.json");

    private static Dictionary<string, string> pins = new(StringComparer.OrdinalIgnoreCase);
    private static bool loaded;
    private static SynchronizationContext? uiContext;

    static LlmCertificateTrust()
    {
        // Register early so even a connection attempt before the main form's Shown
        // event goes through the trust prompt.
        LlamaHttp.CertificateValidator = Validate;
    }

    internal static void Initialize(SynchronizationContext? context)
    {
        lock (Gate)
        {
            uiContext = context;
        }
    }

    internal static bool Validate(HttpRequestMessage? request, X509Certificate2? certificate,
        X509Chain? chain, SslPolicyErrors errors)
    {
        Uri? uri = request?.RequestUri;
        if (uri is null || certificate is null)
        {
            return false;
        }

        string key = $"{uri.Scheme}://{uri.Host}:{uri.Port}";
        string fingerprint = Convert.ToHexString(SHA256.HashData(certificate.RawData));

        lock (Gate)
        {
            EnsureLoaded();
            if (pins.TryGetValue(key, out string? saved) &&
                string.Equals(saved, fingerprint, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        string message =
            $"The LLM server at {key} presented a certificate that Windows does not trust.\n\n" +
            $"Subject:  {certificate.Subject}\n" +
            $"Issuer:   {certificate.Issuer}\n" +
            $"SHA-256:  {FingerprintWithSeparators(fingerprint)}\n\n" +
            $"TLS errors: {errors}\n\n" +
            "Only trust this certificate if you verified the fingerprint with the server owner " +
            "through a separate trusted channel.\n\nTrust and remember this certificate?";

        // Never hold Gate while a modal dialog is open: the UI thread may itself
        // need the lock for another connection.
        if (!Ask(message))
        {
            return false;
        }

        lock (Gate)
        {
            pins[key] = fingerprint;
            Persist();
        }

        try
        {
            LogManager.LogInfo($"[LLM] Trusted certificate for {key} (SHA-256 {FingerprintWithSeparators(fingerprint)}).");
        }
        catch
        {
            // Logging must never block a connection.
        }

        return true;
    }

    private static bool Ask(string message)
    {
        bool accepted = false;
        void Prompt()
        {
            Form? owner = Application.OpenForms.Count > 0 ? Application.OpenForms[0] : null;
            accepted = MessageBox.Show(owner, message, "Trust LLM server certificate",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        SynchronizationContext? context;
        lock (Gate)
        {
            context = uiContext;
        }

        if (context is null || ReferenceEquals(SynchronizationContext.Current, context))
        {
            Prompt();
        }
        else
        {
            context.Send(_ => Prompt(), null);
        }

        return accepted;
    }

    private static void EnsureLoaded()
    {
        if (loaded)
        {
            return;
        }

        loaded = true;
        if (!File.Exists(StorePath))
        {
            return;
        }

        try
        {
            var saved = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(StorePath));
            if (saved is null)
            {
                return;
            }

            foreach (var pair in saved)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                {
                    pins[pair.Key] = pair.Value.Trim().ToUpperInvariant();
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            try
            {
                LogManager.LogWarning($"[LLM] Could not read certificate trust store: {error.Message}");
            }
            catch
            {
                // Ignore logging failures.
            }
        }
    }

    private static void Persist()
    {
        string? directory = Path.GetDirectoryName(StorePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporary = StorePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(pins));
            if (File.Exists(StorePath))
            {
                File.Replace(temporary, StorePath, null);
            }
            else
            {
                File.Move(temporary, StorePath);
            }
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static string FingerprintWithSeparators(string fingerprint)
    {
        if (fingerprint.Length == 0)
        {
            return fingerprint;
        }

        var builder = new System.Text.StringBuilder(fingerprint.Length + fingerprint.Length / 2 - 1);
        for (int index = 0; index < fingerprint.Length; index++)
        {
            if (index > 0 && index % 2 == 0)
            {
                builder.Append(':');
            }

            builder.Append(fingerprint[index]);
        }

        return builder.ToString();
    }
}
