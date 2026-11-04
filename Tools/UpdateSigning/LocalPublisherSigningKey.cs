using System.Security.Cryptography;
using System.Text;

namespace FMSC_SMS.UpdateSigning;

/// <summary>
/// Publisher signing authority is created ONLY on the owner's Windows account.
/// The PRIVATE key is protected by DPAPI CurrentUser and never enters Git, the
/// application build, the update repository or a GitHub Actions artifact.
/// The PUBLIC key can be embedded in signed FMSC client builds after review.
/// </summary>
internal static class LocalPublisherSigningKey
{
    private static readonly byte[] Entropy =
        SHA256.HashData(Encoding.UTF8.GetBytes("FMSC/publisher-signing/v1"));

    internal const string ProtectedFileName = "publisher-private.dpapi";
    internal const string PublicFileName = "publisher-public.pem";

    internal static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FMSC", "ReleaseSigning");

    internal static (string ProtectedKey, string PublicKey) Generate(string folder)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Publisher key generation requires Windows DPAPI.");
        string path = Path.GetFullPath(folder);
        string protectedPath = Path.Combine(path, ProtectedFileName);
        string publicPath = Path.Combine(path, PublicFileName);
        if (File.Exists(protectedPath) || File.Exists(publicPath))
            throw new InvalidOperationException(
                "Publisher key files already exist. Refusing to replace the signing identity.");
        Directory.CreateDirectory(path);
        byte[] secret = Array.Empty<byte>();
        byte[] encrypted = Array.Empty<byte>();
        string tempProtected = protectedPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        string tempPublic = publicPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using var rsa = RSA.Create(4096);
            secret = Encoding.UTF8.GetBytes(rsa.ExportRSAPrivateKeyPem());
            encrypted = ProtectedData.Protect(secret, Entropy, DataProtectionScope.CurrentUser);
            using (var file = new FileStream(tempProtected, FileMode.CreateNew,
                FileAccess.Write, FileShare.None))
            {
                file.Write(encrypted);
                file.Flush(flushToDisk: true);
            }
            File.WriteAllText(tempPublic, rsa.ExportRSAPublicKeyPem(), Encoding.ASCII);
            File.Move(tempProtected, protectedPath, overwrite: false);
            File.Move(tempPublic, publicPath, overwrite: false);
            return (protectedPath, publicPath);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
            CryptographicOperations.ZeroMemory(encrypted);
            if (File.Exists(tempProtected)) File.Delete(tempProtected);
            if (File.Exists(tempPublic)) File.Delete(tempPublic);
        }
    }

    internal static RSA Open(string protectedKeyPath)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Publisher key access requires the original Windows user.");
        byte[] sealedBytes = File.ReadAllBytes(protectedKeyPath);
        if (sealedBytes.Length is < 256 or > 32768)
            throw new InvalidDataException("Invalid protected publisher key file.");
        byte[] clear = ProtectedData.Unprotect(sealedBytes, Entropy, DataProtectionScope.CurrentUser);
        try
        {
            using var key = RSA.Create();
            key.ImportFromPem(Encoding.UTF8.GetString(clear));
            if (key.KeySize < 3072)
                throw new CryptographicException("Publisher key is too short.");
            string approvedPublic = Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(protectedKeyPath))!, PublicFileName);
            using var candidate = RSA.Create();
            candidate.ImportFromPem(File.ReadAllText(approvedPublic, Encoding.ASCII));
            if (!CryptographicOperations.FixedTimeEquals(
                key.ExportRSAPublicKey(), candidate.ExportRSAPublicKey()))
                throw new CryptographicException("Publisher public and protected private keys do not match.");
            var result = RSA.Create();
            result.ImportParameters(key.ExportParameters(true));
            return result;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
            CryptographicOperations.ZeroMemory(sealedBytes);
        }
    }

    internal static void SelfTest()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Publisher signing DPAPI test requires Windows.");
        string root = Path.Combine(Path.GetTempPath(),
            "fmsc-publisher-key-" + Guid.NewGuid().ToString("N"));
        try
        {
            var generated = Generate(root);
            if (!File.Exists(generated.ProtectedKey) || !File.Exists(generated.PublicKey))
                throw new Exception("Publisher key pair was not saved.");
            var protectedText = File.ReadAllText(generated.ProtectedKey);
            if (protectedText.Contains("BEGIN RSA PRIVATE KEY", StringComparison.Ordinal))
                throw new Exception("Publisher private key was exposed in cleartext.");
            using var privateKey = Open(generated.ProtectedKey);
            using var publicKey = RSA.Create();
            publicKey.ImportFromPem(File.ReadAllText(generated.PublicKey));
            byte[] payload = Encoding.UTF8.GetBytes("FMSC signed update test");
            byte[] signature = privateKey.SignData(payload, HashAlgorithmName.SHA256,
                RSASignaturePadding.Pss);
            if (!publicKey.VerifyData(payload, signature, HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pss))
                throw new Exception("The publisher key failed RSA-PSS verification.");
            try
            {
                _ = Generate(root);
                throw new Exception("Publisher signing authority was silently replaced.");
            }
            catch (InvalidOperationException) { }
            byte[] damaged = File.ReadAllBytes(generated.ProtectedKey);
            damaged[damaged.Length / 2] ^= 0x31;
            File.WriteAllBytes(generated.ProtectedKey, damaged);
            try
            {
                using var invalid = Open(generated.ProtectedKey);
                throw new Exception("Damaged protected publisher key was accepted.");
            }
            catch (CryptographicException) { }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
