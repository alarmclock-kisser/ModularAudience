using System.Security.Cryptography.X509Certificates;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.CoOp.Protocol;
using ModularAudience.CoOp.Security;

namespace ModularAudience.CoOp.Tests;

[TestClass]
public sealed class CoOpSecurityTests
{
    [TestMethod]
    public void SessionPassword_EnforcesSharedLengthBounds()
    {
        Assert.IsFalse(CoOpSecurity.IsValidSessionPassword("short"));
        Assert.IsTrue(CoOpSecurity.IsValidSessionPassword("12345678"));
        Assert.IsTrue(CoOpSecurity.IsValidSessionPassword(new string('x', 128)));
        Assert.IsFalse(CoOpSecurity.IsValidSessionPassword(new string('x', 129)));
    }

    [TestMethod]
    public void PasswordProof_IsBoundToPasswordAndPeerIdentity()
    {
        AuthChallengeMessage challenge = CoOpSecurity.CreateChallenge();
        Guid peerId = Guid.NewGuid();
        byte[] proof = CoOpSecurity.CreatePasswordProof("correct horse battery", challenge, peerId);

        Assert.IsTrue(CoOpSecurity.VerifyPasswordProof("correct horse battery", challenge, peerId, proof));
        Assert.IsFalse(CoOpSecurity.VerifyPasswordProof("incorrect password", challenge, peerId, proof));
        Assert.IsFalse(CoOpSecurity.VerifyPasswordProof("correct horse battery", challenge, Guid.NewGuid(), proof));
    }

    [TestMethod]
    public void PasswordlessChallenge_IsExplicitlyMarkedLocalOnly()
    {
        AuthChallengeMessage challenge = CoOpSecurity.CreateChallenge(
            requiresPassword: false,
            isLanOnly: true);

        Assert.IsFalse(challenge.RequiresPassword);
        Assert.IsTrue(challenge.IsLanOnly);
        Assert.ThrowsExactly<ArgumentException>(() =>
            CoOpSecurity.CreateChallenge(requiresPassword: false, isLanOnly: false));
    }

    [TestMethod]
    public void HostCertificate_IsPersistentAndHasStableSha256Fingerprint()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ModularAudience-CoOp-Security", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "host.pfx");
        try
        {
            using X509Certificate2 first = CoOpSecurity.LoadOrCreateHostCertificate(path);
            using X509Certificate2 second = CoOpSecurity.LoadOrCreateHostCertificate(path);

            Assert.IsTrue(first.HasPrivateKey);
            Assert.AreEqual(
                CoOpSecurity.GetCertificateFingerprint(first),
                CoOpSecurity.GetCertificateFingerprint(second));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void CertificateTrustStore_PinsFingerprintPerHostAndPort()
    {
        string path = Path.Combine(Path.GetTempPath(), "ModularAudience-CoOp-Security", Guid.NewGuid().ToString("N"), "pins.json");
        var store = new CoOpCertificateTrustStore(path);
        try
        {
            store.Trust("example.local", 47823, "AABBCC");

            Assert.IsTrue(store.IsTrusted("EXAMPLE.LOCAL", 47823, "aabbcc"));
            Assert.IsFalse(store.IsTrusted("example.local", 47824, "AABBCC"));
            Assert.IsFalse(store.IsTrusted("example.local", 47823, "DDEEFF"));
        }
        finally
        {
            string directory = Path.GetDirectoryName(path)!;
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
