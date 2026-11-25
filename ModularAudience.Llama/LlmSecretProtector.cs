using System.Security.Cryptography;
using System.Text;

namespace ModularAudience.Llama;

/// <summary>Protects user-supplied LLM API keys before they are written to disk.</summary>
public interface ILlmSecretProtector
{
    string Protect(string secret);
    string Unprotect(string ciphertext);
}

/// <summary>
/// Windows DPAPI protector. Ciphertext is bound to the current Windows user and
/// machine, so a settings file copied to another account cannot reveal the key.
/// </summary>
public sealed class WindowsLlmSecretProtector : ILlmSecretProtector
{
    private static readonly byte[] Purpose = Encoding.UTF8.GetBytes("ModularAudience.Llm.api-key.v1");
    private const string Prefix = "dpapi:v1:";

    public string Protect(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        if (secret.Length == 0)
        {
            return string.Empty;
        }

        byte[] bytes = Encoding.UTF8.GetBytes(secret);
        try
        {
            return Prefix + Convert.ToBase64String(
                ProtectedData.Protect(bytes, Purpose, DataProtectionScope.CurrentUser));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public string Unprotect(string ciphertext)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);
        if (ciphertext.Length == 0)
        {
            return string.Empty;
        }

        if (!ciphertext.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new CryptographicException("Unsupported LLM secret protection format.");
        }

        byte[] protectedBytes = Convert.FromBase64String(ciphertext[Prefix.Length..]);
        byte[] bytes = ProtectedData.Unprotect(protectedBytes, Purpose, DataProtectionScope.CurrentUser);
        try
        {
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
