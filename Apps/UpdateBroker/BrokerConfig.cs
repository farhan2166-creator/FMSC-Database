using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace FMSC_SMS.UpdateBroker;

internal sealed class BrokerConfig
{
    internal required string ConnectionString { get; init; }
    internal required byte[] KeyPepper { get; init; }
    internal required string GitHubOwner { get; init; }
    internal required string GitHubRepository { get; init; }
    internal required string GitHubAppId { get; init; }
    internal required long GitHubInstallationId { get; init; }
    internal required string GitHubAppPrivateKeyPath { get; init; }
    internal required string PublisherPublicKeyPath { get; init; }
    internal int MaximumDevicesPerSchool { get; init; } = 100;

    internal static BrokerConfig Load()
    {
        string Required(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value : throw new InvalidOperationException("Missing required update broker setting: " + name);
        byte[] pepper = Convert.FromBase64String(Required("FMSC_UPDATE_KEY_PEPPER_BASE64"));
        if (pepper.Length < 32)
            throw new InvalidOperationException("The credential hashing pepper must contain at least 32 random bytes.");
        string owner = Required("FMSC_UPDATE_GITHUB_OWNER");
        string repo = Required("FMSC_UPDATE_GITHUB_REPO");
        if (!Regex.IsMatch(owner, "^[A-Za-z0-9-]{1,39}$") ||
            !Regex.IsMatch(repo, "^[A-Za-z0-9_.-]{1,100}$"))
            throw new InvalidOperationException("Invalid GitHub owner or repository.");
        var connection = new SqlConnectionStringBuilder(Required("FMSC_UPDATE_SQL_CONNECTION"));
        if (connection.AttachDBFilename.Length != 0 || string.IsNullOrWhiteSpace(connection.InitialCatalog) ||
            !connection.Encrypt || connection.TrustServerCertificate)
            throw new InvalidOperationException(
                "Use an explicit broker database with Encrypt=true and TrustServerCertificate=false.");
        int maxDevices = int.TryParse(Environment.GetEnvironmentVariable("FMSC_UPDATE_MAX_DEVICES_PER_SCHOOL"),
            out var specified) ? specified : 100;
        if (maxDevices is < 1 or > 10000)
            throw new InvalidOperationException("Invalid broker device enrollment ceiling.");
        long installationId = long.Parse(Required("FMSC_UPDATE_GITHUB_INSTALLATION_ID"));
        if (installationId <= 0)
            throw new InvalidOperationException("GitHub installation ID must be positive.");
        string appId = Required("FMSC_UPDATE_GITHUB_APP_ID");
        if (!Regex.IsMatch(appId, "^[0-9]{1,20}$"))
            throw new InvalidOperationException("Invalid GitHub App ID.");
        var githubKey = Required("FMSC_UPDATE_GITHUB_APP_PRIVATE_KEY_PATH");
        var publisherKey = Required("FMSC_UPDATE_PUBLISHER_PUBLIC_KEY_PATH");
        if (!File.Exists(githubKey) || !File.Exists(publisherKey))
            throw new InvalidOperationException("A server-only key file is missing.");
        return new BrokerConfig
        {
            ConnectionString = connection.ConnectionString,
            KeyPepper = pepper,
            GitHubOwner = owner,
            GitHubRepository = repo,
            GitHubAppId = appId,
            GitHubInstallationId = installationId,
            GitHubAppPrivateKeyPath = githubKey,
            PublisherPublicKeyPath = publisherKey,
            MaximumDevicesPerSchool = maxDevices
        };
    }
}

/// <summary>Opaque, random, independently revocable credentials. Only keyed hashes are stored in SQL.</summary>
internal static class BrokerTokens
{
    private const int SecretBytes = 32;
    internal const string SchoolAdminPrefix = "fmsc-school-admin-v1";
    internal const string DevicePrefix = "fmsc-device-v1";

    internal static string New(string kind, Guid id)
    {
        if (kind != SchoolAdminPrefix && kind != DevicePrefix)
            throw new ArgumentException("Unknown credential kind.", nameof(kind));
        return kind + "." + id.ToString("N") + "." + Base64Url(RandomNumberGenerator.GetBytes(SecretBytes));
    }

    internal static bool TryParse(string? token, string kind, out Guid id)
    {
        id = default;
        if (string.IsNullOrWhiteSpace(token) || token.Length > 160) return false;
        var parts = token.Split('.');
        if (parts.Length != 3 || parts[0] != kind ||
            parts[1].Length != 32 || !Guid.TryParseExact(parts[1], "N", out id) ||
            parts[2].Length != 43)
            return false;
        try
        {
            return FromBase64Url(parts[2]).Length == SecretBytes;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    internal static byte[] Hash(string token, byte[] pepper) =>
        HMACSHA256.HashData(pepper, Encoding.UTF8.GetBytes(token));

    internal static bool Matches(string token, byte[] pepper, byte[] stored) =>
        stored.Length == 32 &&
        CryptographicOperations.FixedTimeEquals(Hash(token, pepper), stored);

    internal static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string text)
    {
        string ordinary = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(ordinary + new string('=', (4 - ordinary.Length % 4) % 4));
    }
}
