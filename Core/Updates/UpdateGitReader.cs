using System.ComponentModel;

namespace FMSC_SMS.Core.Updates;

/// <summary>
/// Manual update check against the owner's private Git distribution.
/// SSH deploy-key access and pinned GitHub host identity protect transport;
/// package SHA-256 checks detect mismatched bytes. No publisher key is needed.
/// No binary download, installation or SQL change is performed here.
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

            VerifiedUpdate? latest = await UpdateReleasePreparer.ReadLatestAsync(
                connection, cancellationToken);
            if (latest is null)
                return new(true, false, null, "No published update available.");
            return new(true, true, latest.Manifest.Version,
                "Private update repository connected; published update metadata is valid. " +
                "No application files were changed.");
        }
        catch (UpdateGitCommandException error)
        {
            return new(false, false, null,
                "Unable to check the private update repository. Check internet " +
                "connectivity and approval of this computer's read-only SSH key. " +
                error.Reason);
        }
        catch (FileNotFoundException)
        {
            return new(false, false, null,
                "FMSC's bundled Git/SSH tools are missing. Repair the complete FMSC installation.");
        }
        catch (Win32Exception)
        {
            return new(false, false, null,
                "FMSC's bundled Git/SSH tools could not start. Repair the complete installation.");
        }
    }
}
