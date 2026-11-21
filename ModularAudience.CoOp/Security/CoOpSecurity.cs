using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using ModularAudience.CoOp.Protocol;

namespace ModularAudience.CoOp.Security;

public static class CoOpSecurity
{
    public const int PasswordKeyIterations = 180_000;

    public static X509Certificate2 LoadOrCreateHostCertificate(string certificatePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(certificatePath);
        string fullPath = Path.GetFullPath(certificatePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        if (File.Exists(fullPath))
        {
            return X509CertificateLoader.LoadPkcs12FromFile(
                fullPath,
                null,
                X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet);
        }

        using RSA rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=ModularAudience Co-op",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
            true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        using X509Certificate2 certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddYears(5));
        byte[] pfx = certificate.Export(X509ContentType.Pfx);
        string temporaryPath = fullPath + ".tmp";
        File.WriteAllBytes(temporaryPath, pfx);
        File.Move(temporaryPath, fullPath, true);
        CryptographicOperations.ZeroMemory(pfx);
        return X509CertificateLoader.LoadPkcs12FromFile(
            fullPath,
            null,
            X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet);
    }

    public static string GetCertificateFingerprint(X509Certificate certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        return Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData()));
    }

    public static bool IsValidSessionPassword(string? password)
    {
        return !string.IsNullOrWhiteSpace(password) && password.Length is >= 8 and <= 128;
    }

    public static AuthChallengeMessage CreateChallenge(
        bool requiresPassword = true,
        bool isLanOnly = false)
    {
        if (!requiresPassword && !isLanOnly)
            throw new ArgumentException("Passwordless co-op sessions must be restricted to the local network.", nameof(isLanOnly));

        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] nonce = RandomNumberGenerator.GetBytes(32);
        return new AuthChallengeMessage(salt, nonce, PasswordKeyIterations, requiresPassword, isLanOnly);
    }

    public static byte[] CreatePasswordProof(string password, AuthChallengeMessage challenge, Guid peerId)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        if (!IsValidSessionPassword(password) || challenge.Salt.Length < 16
            || challenge.Nonce.Length < 32 || challenge.Iterations is < 100_000 or > 500_000)
        {
            throw new CoOpProtocolException("The co-op authentication challenge is invalid.");
        }

        byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(
            passwordBytes,
            challenge.Salt,
            challenge.Iterations,
            HashAlgorithmName.SHA256,
            32);
        CryptographicOperations.ZeroMemory(passwordBytes);
        byte[] message = new byte[challenge.Nonce.Length + 16];
        challenge.Nonce.CopyTo(message, 0);
        peerId.TryWriteBytes(message.AsSpan(challenge.Nonce.Length));
        byte[] proof = HMACSHA256.HashData(key, message);
        CryptographicOperations.ZeroMemory(key);
        CryptographicOperations.ZeroMemory(message);
        return proof;
    }

    public static bool VerifyPasswordProof(
        string password,
        AuthChallengeMessage challenge,
        Guid peerId,
        ReadOnlySpan<byte> proof)
    {
        if (proof.Length != 32)
            return false;
        byte[] expected = CreatePasswordProof(password, challenge, peerId);
        bool matches = CryptographicOperations.FixedTimeEquals(expected, proof);
        CryptographicOperations.ZeroMemory(expected);
        return matches;
    }
}

public sealed class CoOpCertificateTrustStore
{
    private readonly string path;
    private readonly object gate = new();

    public CoOpCertificateTrustStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = Path.GetFullPath(path);
    }

    public bool IsTrusted(string host, int port, string fingerprint)
    {
        lock (this.gate)
        {
            Dictionary<string, string> pins = this.Load();
            return pins.TryGetValue(GetKey(host, port), out string? saved)
                && string.Equals(saved, fingerprint, StringComparison.OrdinalIgnoreCase);
        }
    }

    public void Trust(string host, int port, string fingerprint)
    {
        lock (this.gate)
        {
            Dictionary<string, string> pins = this.Load();
            pins[GetKey(host, port)] = fingerprint.ToUpperInvariant();
            Directory.CreateDirectory(Path.GetDirectoryName(this.path)!);
            string temporary = this.path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(pins));
            File.Move(temporary, this.path, true);
        }
    }

    private Dictionary<string, string> Load()
    {
        if (!File.Exists(this.path))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(this.path))
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The saved co-op certificate trust store is invalid.", ex);
        }
    }

    private static string GetKey(string host, int port)
        => $"{host.Trim().ToLowerInvariant()}:{port}";
}

public sealed class CoOpCertificateTrustException(
    string host,
    int port,
    string fingerprint) : AuthenticationException(
        $"The certificate presented by {host}:{port} is not trusted. SHA-256 fingerprint: {fingerprint}")
{
    public string Host { get; } = host;
    public int Port { get; } = port;
    public string Fingerprint { get; } = fingerprint;
}
