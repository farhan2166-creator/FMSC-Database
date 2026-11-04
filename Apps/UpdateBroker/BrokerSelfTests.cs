using System.Security.Cryptography;

namespace FMSC_SMS.UpdateBroker;

internal static class BrokerSelfTests
{
    internal static void Run()
    {
        using var firstPepperRandom = RandomNumberGenerator.Create();
        var firstPepper = RandomNumberGenerator.GetBytes(32);
        var secondPepper = RandomNumberGenerator.GetBytes(32);
        var school = Guid.NewGuid();
        string schoolCredential = BrokerTokens.New(BrokerTokens.SchoolAdminPrefix, school);
        Require(BrokerTokens.TryParse(schoolCredential, BrokerTokens.SchoolAdminPrefix, out var parsed)
            && parsed == school, "School admin credential must retain its school identity.");
        Require(!BrokerTokens.TryParse(schoolCredential, BrokerTokens.DevicePrefix, out _),
            "An admin credential must never authenticate a device endpoint.");
        byte[] hash = BrokerTokens.Hash(schoolCredential, firstPepper);
        Require(hash.Length == 32 && BrokerTokens.Matches(schoolCredential, firstPepper, hash),
            "Correct admin credential must match its stored keyed digest.");
        Require(!BrokerTokens.Matches(schoolCredential, secondPepper, hash),
            "A different server pepper must not authenticate the same token.");
        string rotated = BrokerTokens.New(BrokerTokens.SchoolAdminPrefix, school);
        Require(rotated != schoolCredential && !BrokerTokens.Matches(rotated, firstPepper, hash),
            "Rotated school admin credentials must invalidate old hashes.");
        var deviceId = Guid.NewGuid();
        string deviceCredential = BrokerTokens.New(BrokerTokens.DevicePrefix, deviceId);
        Require(BrokerTokens.TryParse(deviceCredential, BrokerTokens.DevicePrefix, out var parsedDevice)
            && parsedDevice == deviceId, "Device token must have an independently revocable device ID.");
        Require(!BrokerTokens.TryParse(deviceCredential, BrokerTokens.SchoolAdminPrefix, out _),
            "A device credential must never enroll additional devices.");
        foreach (string malformed in new[]
        {
            "", "Bearer " + deviceCredential, "fmsc-device-v1.bad.token",
            deviceCredential + ".extra", deviceCredential.Replace('.', '/'),
            "fmsc-device-v1." + deviceId.ToString("N") + "." + new string('A', 150)
        })
            Require(!BrokerTokens.TryParse(malformed, BrokerTokens.DevicePrefix, out _),
                "Malformed credentials must not parse.");
        Require(GitHubPrivateReleases.IsTrustedReleaseAssetRedirect(
            new Uri("https://release-assets.githubusercontent.com/path?key=test")),
            "Official release asset host should be accepted.");
        foreach (string unsafeUrl in new[]
        {
            "http://release-assets.githubusercontent.com/file",
            "https://release-assets.githubusercontent.com.evil.example/file",
            "https://api.github.com/repos/something",
            "https://localhost/secret",
            "https://objects.githubusercontent.com:8080/file",
            "https://evil.example/releases/file"
        })
            Require(!GitHubPrivateReleases.IsTrustedReleaseAssetRedirect(new Uri(unsafeUrl)),
                "Untrusted redirect must not be followed: " + unsafeUrl);
        Require(EnrollmentStore.ValidateName("  School A ", 160) == "School A",
            "School names must be normalized.");
        foreach (string badName in new[] { "", " ", "hello\r\nworld", new string('x', 161) })
        {
            try
            {
                _ = EnrollmentStore.ValidateName(badName, 160);
                throw new Exception("Invalid enrollment name was accepted.");
            }
            catch (ArgumentException) { }
        }
    }

    private static void Require(bool valid, string message)
    {
        if (!valid) throw new Exception(message);
    }
}
