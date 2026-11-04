using System.ComponentModel;
using System.Security.Cryptography;

namespace FMSC_SMS.Core.Updates;

/// <summary>
/// Manual update check only. The private Git session downloads the small
/// channel/manifest blobs and verifies the manifest's publisher signature.
/// It never downloads app binaries until the user explicitly approves later.
/// </summary>
internal static class UpdateGitReader
{
    internal sealed record CheckResult(
        bool Authorized, bool FeedPublished, string? Version, string Message);

    internal static async Task<CheckResult> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        if (!UpdateDeviceKey.IsEnrolled)
            return new(false, false, null,
                "Generate this computer's authorization key and have the owner approve it first.");
        try
        {
            await using var connection = await UpdateGitSession.OpenAsync(cancellationToken);
            if (!await connection.HasStableFeedAsync(cancellationToken))
                return new(true, false, null,
                    "Computer authorized. No approved update package has been published yet.");

            using RSA pinnedKey = UpdatePublisherTrust.LoadPinnedPublicKey();
            VerifiedUpdate? latest = await UpdateReleasePreparer.ReadLatestAsync(
                connection, pinnedKey, cancellationToken);
            if (latest is null)
                return new(true, false, null, "No signed update available.");
            return new(true, true, latest.Manifest.Version,
                "The private update feed is available, and the latest publisher " +
                "signature is verified. No application files were changed.");
        }
        catch (InvalidOperationException error)
            when (error.Message.Contains("publisher public key", StringComparison.OrdinalIgnoreCase))
        {
            return new(true, false, null,
                "Private repository connected, but the FMSC publisher signing " +
                "key has not been provisioned. Updating remains disabled safely.");
        }
        catch (UpdateGitCommandException error)
        {
            return new(false, false, null,
                "Unable to check the private distribution repository. Check " +
                "internet connectivity and approval of this computer's SSH key. " +
                error.Reason);
        }
        catch (Win32Exception)
        {
            return new(false, false, null,
                "The required signed Git/SSH transport is not yet bundled. " +
                "No update was attempted.");
        }
        catch (CryptographicException)
        {
            return new(false, false, null,
                "The update's publisher signature is not trusted. No update was accepted.");
        }
    }
}

internal sealed class UpdateGitCommandException : Exception
{
    internal string Reason { get; }
    internal UpdateGitCommandException(string reason) : base(reason) => Reason = reason;
}
