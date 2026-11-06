using System.Security.Cryptography;

namespace FMSC_SMS.Core.Updates;

/// <summary>
/// Reuses a staged update ONLY after the caller has authenticated to the private
/// Git feed and read its current validated manifest. Local cached metadata alone
/// never establishes a trusted release, and a saved progress value is not proof
/// that an installer can safely run.
/// </summary>
internal static class UpdatePreparedResume
{
    internal static PreparedUpdate? FindVerified(VerifiedUpdate trustedRelease,
        UpdateDownloadChoice method, string stateRoot)
    {
        if (method is not (UpdateDownloadChoice.Full or UpdateDownloadChoice.Incremental) ||
            trustedRelease.Manifest.Version != trustedRelease.RepositoryVersion ||
            trustedRelease.ManifestBytes.Length is < 1 or > 4 * 1024 * 1024 ||
            !Directory.Exists(stateRoot))
            return null;

        try
        {
            UpdatePartDownload.RejectLinks(stateRoot);
            // A bounded scan avoids repeatedly hashing arbitrarily many old stages.
            foreach (string folder in Directory.EnumerateDirectories(
                stateRoot, "prepared-*", SearchOption.TopDirectoryOnly)
                .OrderByDescending(Directory.GetLastWriteTimeUtc).Take(12))
            {
                string name = Path.GetFileName(folder);
                if (!name.StartsWith("prepared-", StringComparison.Ordinal) ||
                    !Guid.TryParseExact(name["prepared-".Length..], "N", out _))
                    continue;

                try
                {
                    string metadata = Path.Combine(folder, "metadata");
                    string application = Path.Combine(folder, "application");
                    string manifestPath = Path.Combine(metadata, "update-manifest.json");
                    // Do not read through junctions or symlinks in an old stage.
                    UpdatePartDownload.RejectLinks(folder);
                    UpdatePartDownload.RejectLinks(metadata);
                    UpdatePartDownload.RejectLinks(application);
                    UpdatePartDownload.RejectLinks(manifestPath);
                    if (!File.Exists(manifestPath) || !Directory.Exists(application) ||
                        new FileInfo(manifestPath).Length != trustedRelease.ManifestBytes.Length)
                        continue;
                    byte[] saved = File.ReadAllBytes(manifestPath);
                    // Match the exact bytes retrieved from the authenticated,
                    // owner-approved feed before trusting any local staging files.
                    if (!CryptographicOperations.FixedTimeEquals(saved, trustedRelease.ManifestBytes))
                        continue;

                    var prepared = new PreparedUpdate(trustedRelease.RepositoryVersion,
                        application, metadata, method, false);
                    // Rehash EVERY application file and verify installer/version/
                    // schema, extra-file refusal and migration prerequisites.
                    UpdateInstallerHandoff.Validate(prepared);
                    return prepared;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException
                    or InvalidDataException or CryptographicException)
                {
                    // A missing, damaged or altered stage is NOT an install option.
                    // The existing authenticated download flow remains available.
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or InvalidDataException)
        {
            // Cache access must not block an ordinary update check.
        }
        return null;
    }
}
