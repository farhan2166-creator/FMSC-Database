using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Security.AccessControl;
using System.Security.Principal;

namespace FMSC_SMS.Core.Updates;

/// <summary>
/// One distinct GitHub read-only deploy key per Windows-user installation.
/// The private RSA key is generated in memory, sealed with DPAPI CurrentUser
/// and saved under LOCALAPPDATA (never in the school SQL database or app DLL).
/// No common credential is embedded in school builds.
/// </summary>
internal static class UpdateDeviceKey
{
    private const string CredentialFile = "device-key.v1.dpapi";
    private static readonly byte[] Entropy = SHA256.HashData(
        Encoding.UTF8.GetBytes("FMSC/private-distribution/ssh-deploy-key/v1"));
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    internal static string StateDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FMSC", "Updates");

    internal static string CredentialPath => Path.Combine(StateDirectory, CredentialFile);

    internal static bool IsEnrolled => File.Exists(CredentialPath);

    /// <summary>
    /// Never overwrite an existing private key automatically. The GitHub deploy
    /// key must be explicitly revoked by the owner if the PC is deauthorized.
    /// </summary>
    internal static string GenerateEnrollment(string? storageRoot = null)
    {
        string root = storageRoot ?? StateDirectory;
        string credential = Path.Combine(root, CredentialFile);
        Directory.CreateDirectory(root);
        RestrictDirectory(root);
        if (File.Exists(credential))
            throw new InvalidOperationException(
                "This Windows user already has an update authorization key. " +
                "Do not replace it without revoking the previous GitHub deploy key.");
        using var rsa = RSA.Create(3072);
        var publicBlob = ExportSshRsaPublicKey(rsa);
        string publicText = "ssh-rsa " + Convert.ToBase64String(publicBlob) +
            " fmsc-update-device";
        char[] privatePem = rsa.ExportRSAPrivateKeyPem().ToCharArray();
        byte[] clear = Array.Empty<byte>();
        byte[] sealedKey = Array.Empty<byte>();
        string temporary = credential + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            clear = JsonSerializer.SerializeToUtf8Bytes(
                new DeviceCredential(1, publicText, new string(privatePem)), JsonOptions);
            sealedKey = ProtectedData.Protect(clear, Entropy, DataProtectionScope.CurrentUser);
            using (var file = new FileStream(temporary, FileMode.CreateNew,
                       FileAccess.Write, FileShare.None))
            {
                file.Write(sealedKey);
                file.Flush(flushToDisk: true);
            }
            // No overwrite: concurrent enrollment attempts cannot replace a
            // key that has already been approved by the repository owner.
            File.Move(temporary, credential, overwrite: false);
            return publicText;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
            CryptographicOperations.ZeroMemory(sealedKey);
            Array.Fill(privatePem, '\0');
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    internal static string GetPublicKey(string? storageRoot = null) => ReadCredential(storageRoot).PublicKey;

    internal static void RestrictDirectory(string root)
    {
        var user = WindowsIdentity.GetCurrent().User
            ?? throw new UnauthorizedAccessException("Cannot identify the Windows update key owner.");
        var security = new DirectorySecurity();
        security.SetOwner(user);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(root).SetAccessControl(security);
    }

    /// <summary>Caller must zero returned bytes after their short-lived use.</summary>
    internal static byte[] OpenPrivateKey(string? storageRoot = null) =>
        Encoding.ASCII.GetBytes(ReadCredential(storageRoot).PrivateKey);

    private static DeviceCredential ReadCredential(string? storageRoot)
    {
        string credential = Path.Combine(storageRoot ?? StateDirectory, CredentialFile);
        byte[] encrypted = File.ReadAllBytes(credential);
        if (encrypted.Length is < 32 or > 65536)
            throw new InvalidDataException("Encrypted update authorization file is invalid.");
        byte[] clear = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
        try
        {
            var value = JsonSerializer.Deserialize<DeviceCredential>(clear, JsonOptions)
                ?? throw new InvalidDataException("Update authorization is empty.");
            if (value.Format != 1 || !value.PublicKey.StartsWith("ssh-rsa ", StringComparison.Ordinal) ||
                value.PublicKey.Length > 2048 ||
                !value.PrivateKey.StartsWith("-----BEGIN RSA PRIVATE KEY-----", StringComparison.Ordinal))
                throw new InvalidDataException("Saved update authorization has an invalid format.");
            return value;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
        }
    }

    private static byte[] ExportSshRsaPublicKey(RSA rsa)
    {
        var parameters = rsa.ExportParameters(false);
        using var output = new MemoryStream();
        WriteSshString(output, Encoding.ASCII.GetBytes("ssh-rsa"));
        WriteMpint(output, parameters.Exponent!);
        WriteMpint(output, parameters.Modulus!);
        return output.ToArray();
    }

    private static void WriteMpint(Stream output, byte[] unsigned)
    {
        int start = 0;
        while (start + 1 < unsigned.Length && unsigned[start] == 0) start++;
        var value = unsigned.AsSpan(start);
        if (value[0] >= 128)
        {
            var positive = new byte[value.Length + 1];
            value.CopyTo(positive.AsSpan(1));
            WriteSshString(output, positive);
        }
        else
            WriteSshString(output, value);
    }

    private static void WriteSshString(Stream output, ReadOnlySpan<byte> field)
    {
        Span<byte> header = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)field.Length);
        output.Write(header);
        output.Write(field);
    }

    private sealed record DeviceCredential(int Format, string PublicKey, string PrivateKey);
}
