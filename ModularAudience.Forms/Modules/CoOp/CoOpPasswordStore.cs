using System.Security.Cryptography;
using System.Text;

namespace ModularAudience.Forms.Modules.CoOp;

internal sealed class CoOpPasswordStore(string path)
{
    private static readonly byte[] Purpose = Encoding.UTF8.GetBytes("ModularAudience.CoOp.session-password.v1");
    private readonly string path = Path.GetFullPath(path);

    internal string Load()
    {
        if (!File.Exists(this.path))
        {
            return string.Empty;
        }

        byte[] protectedBytes = Convert.FromBase64String(File.ReadAllText(this.path));
        byte[] passwordBytes = ProtectedData.Unprotect(
            protectedBytes,
            Purpose,
            DataProtectionScope.CurrentUser);
        try
        {
            return Encoding.UTF8.GetString(passwordBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    internal void Save(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
        byte[] protectedBytes = ProtectedData.Protect(
            passwordBytes,
            Purpose,
            DataProtectionScope.CurrentUser);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(this.path)!);
            string temporaryPath = this.path + ".tmp";
            File.WriteAllText(temporaryPath, Convert.ToBase64String(protectedBytes));
            File.Move(temporaryPath, this.path, true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            CryptographicOperations.ZeroMemory(protectedBytes);
        }
    }
}
