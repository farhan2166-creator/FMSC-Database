using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using FMSC_SMS.Core.Updates;

if (!OperatingSystem.IsWindows())
    throw new PlatformNotSupportedException("DPAPI must be exercised on Windows.");
UpdateHandoffTests.Run();

int checks = 0;
void Require(bool valid, string reason)
{
    if (!valid) throw new InvalidOperationException(reason);
    checks++;
}
string root = Path.Combine(Path.GetTempPath(), "fmsc-enrollment-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    string first = Path.Combine(root, "device1");
    string second = Path.Combine(root, "device2");
    string pub1 = UpdateDeviceKey.GenerateEnrollment(first);
    Require(pub1.StartsWith("ssh-rsa AAAA", StringComparison.Ordinal),
        "Generated deploy public key must be in GitHub SSH format.");
    Require(File.Exists(Path.Combine(first, "device-key.v1.dpapi")),
        "DPAPI credential must be stored outside the FMSC school database.");
    Require(pub1 == UpdateDeviceKey.GetPublicKey(first),
        "The same authorized public key must be recovered on subsequent checks.");
    try
    {
        _ = UpdateDeviceKey.GenerateEnrollment(first);
        throw new InvalidOperationException("Existing deploy credential was overwritten.");
    }
    catch (InvalidOperationException error) when (error.Message.Contains("already", StringComparison.OrdinalIgnoreCase))
    {
        checks++;
    }
    byte[] privateKey = UpdateDeviceKey.OpenPrivateKey(first);
    try
    {
        Require(Encoding.ASCII.GetString(privateKey).StartsWith(
            "-----BEGIN RSA PRIVATE KEY-----", StringComparison.Ordinal),
            "Only private key PEM is returned during an authenticated Git session.");
        byte[] encryptedBytes = File.ReadAllBytes(Path.Combine(first, "device-key.v1.dpapi"));
        Require(!Encoding.ASCII.GetString(encryptedBytes).Contains("BEGIN RSA PRIVATE KEY",
            StringComparison.Ordinal), "Private credentials must not appear in cleartext on disk.");

        using (var rsa = RSA.Create())
        {
            rsa.ImportFromPem(Encoding.ASCII.GetString(privateKey));
            Require(rsa.KeySize >= 3072, "Deploy RSA key must be at least 3072 bits.");
        }

        // Windows images with OpenSSH Client validate that the generated legacy
        // RSA PEM can actually be read by ssh.exe and matches the displayed key.
        string privateFile = Path.Combine(first, "for-ssh-test.pem");
        File.WriteAllBytes(privateFile, privateKey);
        try
        {
            var info = new ProcessStartInfo("ssh-keygen.exe")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            info.ArgumentList.Add("-y");
            info.ArgumentList.Add("-f");
            info.ArgumentList.Add(privateFile);
            try
            {
                using var process = Process.Start(info) ??
                    throw new InvalidOperationException("ssh-keygen could not be started.");
                string actual = process.StandardOutput.ReadToEnd();
                string errors = process.StandardError.ReadToEnd();
                if (!process.WaitForExit(15000))
                {
                    process.Kill(entireProcessTree: true);
                    throw new InvalidOperationException("SSH key compatibility check timed out.");
                }
                Require(process.ExitCode == 0, "OpenSSH rejected the generated private key: " + errors);
                Require(actual.StartsWith(pub1.Split(' ')[0] + " " + pub1.Split(' ')[1],
                    StringComparison.Ordinal),
                    "OpenSSH public key must match the GitHub enrollment key.");
            }
            catch (System.ComponentModel.Win32Exception)
            {
                Console.WriteLine("SKIP: ssh-keygen.exe is absent; RSA/DPAPI format verified. " +
                    "A bundled SSH helper is still required for a production updater.");
            }
        }
        finally
        {
            File.Delete(privateFile);
        }
    }
    finally
    {
        CryptographicOperations.ZeroMemory(privateKey);
    }
    string pub2 = UpdateDeviceKey.GenerateEnrollment(second);
    Require(pub2 != pub1, "Different school computers must receive independent deploy keys.");
    byte[] damaged = File.ReadAllBytes(Path.Combine(first, "device-key.v1.dpapi"));
    damaged[damaged.Length / 2] ^= 0x77;
    File.WriteAllBytes(Path.Combine(first, "device-key.v1.dpapi"), damaged);
    try
    {
        _ = UpdateDeviceKey.GetPublicKey(first);
        throw new InvalidOperationException("Tampered DPAPI credential was trusted.");
    }
    catch (CryptographicException)
    {
        checks++;
    }
    Require(UpdateGitSession.IsAllowedPartPath("versions/1.1.0/FMSC_SMS-app-full.zip.part0001"),
        "Full archive chunk must be an approved private Git path.");
    Require(UpdateGitSession.IsAllowedPartPath("versions/1.1.0/FMSC_SMS-app-incremental.zip.part0042"),
        "Delta archive chunk must be an approved private Git path.");
    foreach (string unsafePath in new[]
    {
        "versions/1.1.0/../../secrets",
        "versions/1.1.0/FMSC_SMS-app-full.zip",
        "versions/1.1.0/FMSC_SMS-app-full.zip.part00001",
        "versions/1.1.0/FMSC_SMS-app-full.zip.part000A",
        "versions/1.1.0/FMSC_SMS-app-full.zip.part0001/secret",
        "versions/1.1.0/private.pem",
        "versions/not-valid/FMSC_SMS-app-full.zip.part0001"
    })
        Require(!UpdateGitSession.IsAllowedPartPath(unsafePath),
            "Unapproved Git read path was accepted: " + unsafePath);
    Require(UpdateGitSession.IsAllowedMetadataPath("channel/stable.json") &&
        UpdateGitSession.IsAllowedMetadataPath("versions/1.1.0/update-manifest.json") &&
        !UpdateGitSession.IsAllowedMetadataPath("versions/1.1.0/FMSC_SMS.exe") &&
        !UpdateGitSession.IsAllowedMetadataPath("README.md"),
        "Git metadata requests must be limited to approved channel and manifest paths.");
    Console.WriteLine($"PASS: {checks} Git deploy-key enrollment, DPAPI protection and isolation checks.");
}
finally
{
    Directory.Delete(root, recursive: true);
}
