using System.Net.Security;
using System.Security.Authentication;

namespace ModularAudience.Llama;

/// <summary>
/// HTTP setup shared by everything that talks to a local llama-server.
/// llama-server can be served over HTTPS with a self-signed certificate
/// (for example https://localhost:9931). Such a certificate never validates
/// against the public trust chain, so loopback endpoints are allowed to present
/// any certificate: the traffic never leaves the machine.
/// </summary>
public static class LlamaHttp
{
    /// <summary>
    /// Certificate callback for llama-server clients. Accepts every certificate
    /// for loopback hosts (localhost, 127.0.0.1, ::1) and requires a valid chain
    /// for everything else.
    /// </summary>
    public static bool AcceptCertificate(HttpRequestMessage? request, SslPolicyErrors errors) =>
        errors == SslPolicyErrors.None || request?.RequestUri?.IsLoopback == true;

    /// <summary>
    /// Creates an <see cref="HttpClient"/> that can talk to an HTTPS
    /// llama-server with a self-signed loopback certificate.
    /// </summary>
    public static HttpClient CreateClient(TimeSpan timeout)
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = static (request, _, _, errors) =>
                AcceptCertificate(request, errors),
        };

        return new HttpClient(handler) { Timeout = timeout };
    }

    /// <summary>
    /// Flattens the exception chain so the dialog shows the real cause
    /// (for example "The remote certificate is invalid") instead of the generic
    /// "An error occurred while sending the request."
    /// </summary>
    public static string DescribeError(Exception error)
    {
        var messages = new List<string>();
        bool tlsRelated = false;
        for (Exception? current = error; current != null; current = current.InnerException)
        {
            tlsRelated |= current is AuthenticationException
                || current.Message.Contains("SSL", StringComparison.OrdinalIgnoreCase)
                || current.Message.Contains("TLS", StringComparison.OrdinalIgnoreCase)
                || current.Message.Contains("certificate", StringComparison.OrdinalIgnoreCase);

            string message = current.Message.Trim();
            if (message.Length > 0 && !messages.Contains(message, StringComparer.Ordinal))
            {
                messages.Add(message);
            }
        }

        string text = messages.Count == 0 ? error.GetType().Name : string.Join(" ", messages);
        if (tlsRelated)
        {
            text += " (If the server only offers plain HTTP, change the address to http://.)";
        }

        return text;
    }
}
