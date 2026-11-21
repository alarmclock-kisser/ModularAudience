using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;
using ModularAudience.CoOp.Protocol;
using ModularAudience.CoOp.Security;

namespace ModularAudience.CoOp.Networking;

public static class CoOpClient
{
    public static async Task<CoOpConnection> ConnectAsync(
        string host,
        int port,
        ICoOpMessageSerializer serializer,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentNullException.ThrowIfNull(serializer);
        if (port is <= 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        IPAddress[] resolved = IPAddress.TryParse(host, out IPAddress? literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, linked.Token).ConfigureAwait(false);
        IReadOnlyList<IPAddress> addresses = CoOpNetworkUtilities.SelectConnectionAddresses(
            resolved, CoOpNetworkUtilities.GetLocalIPv4Networks());
        SocketException? lastError = null;
        foreach (IPAddress address in addresses)
        {
            linked.Token.ThrowIfCancellationRequested();
            TcpClient? client = null;
            SslStream? secureStream = null;
            try
            {
                client = new TcpClient(address.AddressFamily) { NoDelay = true };
                await client.ConnectAsync(address, port, linked.Token).ConfigureAwait(false);
                string? fingerprint = null;
                secureStream = new SslStream(
                    client.GetStream(),
                    leaveInnerStreamOpen: false,
                    (_, certificate, _, _) =>
                    {
                        if (certificate == null)
                            return false;
                        fingerprint = CoOpSecurity.GetCertificateFingerprint(certificate);
                        return true;
                    });
                try
                {
                    await secureStream.AuthenticateAsClientAsync(
                        new SslClientAuthenticationOptions
                        {
                            TargetHost = host,
                            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                        },
                        linked.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is AuthenticationException or IOException)
                {
                    throw new AuthenticationException(
                        $"The TLS handshake with {host}:{port} failed. Verify the co-op port, update both applications, and allow ModularAudience through the host firewall. Details: {ex.Message}",
                        ex);
                }
                if (fingerprint == null)
                    throw new AuthenticationException("The co-op host did not present a TLS certificate.");
                return new CoOpConnection(client, serializer, secureStream, fingerprint);
            }
            catch (SocketException error)
            {
                secureStream?.Dispose();
                client?.Dispose();
                lastError = error;
            }
            catch
            {
                secureStream?.Dispose();
                client?.Dispose();
                throw;
            }
        }

        if (lastError != null)
        {
            throw new IOException(
                $"Could not connect to co-op host {host}:{port}. Check the address, port, network connection, and host firewall. Details: {lastError.Message}",
                lastError);
        }

        throw new IOException($"No reachable address was found for co-op host {host}:{port}.");
    }
}
