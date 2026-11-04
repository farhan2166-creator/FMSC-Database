using System.Security.Cryptography;
using System.Text.Json;

namespace FMSC_SMS.Core.Updates;

/// <summary>
/// Abstracts a private, read-only source of approved distribution Git blobs.
/// Callers cannot supply arbitrary paths or download unlisted packages.
/// </summary>
internal interface IUpdateArtifactSource
{
    Task<byte[]> ReadSmallAsync(string relativePath, int maximumBytes, CancellationToken cancellationToken);
    Task FetchPartAsync(string relativePath, string destinationFile, long expectedBytes,
        CancellationToken cancellationToken);
}

internal sealed record VerifiedUpdate(UpdateManifest Manifest, string RepositoryVersion,
    byte[] ManifestBytes, byte[] Signature);

internal sealed record PreparedUpdate(
    string Version,
    string StageDirectory,
    string SignedManifestDirectory,
    UpdateDownloadChoice Method,
    bool UsedFullFallback);

/// <summary>
/// A signed release is verified BEFORE selecting or downloading archives.
/// The fetched chunks are checked independently and the reconstructed ZIP is
/// checked again. All output is isolated; activation and SQL are separate.
/// </summary>
internal static class UpdateReleasePreparer
{
    internal static async Task<VerifiedUpdate?> ReadLatestAsync(
        IUpdateArtifactSource source, RSA pinnedPublisherPublicKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(pinnedPublisherPublicKey);
        if (pinnedPublisherPublicKey.KeySize < 3072)
            throw new CryptographicException("FMSC publisher verification key is too short.");

        byte[] pointer = await source.ReadSmallAsync(
            "channel/stable.json", 4096, cancellationToken);
        if (pointer.Length is < 1 or > 4096)
            throw new InvalidDataException("Invalid signed-update channel pointer size.");

        using var document = JsonDocument.Parse(pointer);
        var root = document.RootElement;
        if (root.GetProperty("format").GetString() != "fmsc-git-updates/v1")
            throw new InvalidDataException("Unsupported private update channel.");
        string version = root.GetProperty("latestVersion").GetString() ?? "";
        if (!UpdateManifestPolicy.IsPublishedVersion(version))
            throw new InvalidDataException("Invalid private update version.");
        string folder = "versions/" + version + "/";
        byte[] manifestBytes = await source.ReadSmallAsync(
            folder + "update-manifest.json", 4 * 1024 * 1024, cancellationToken);
        byte[] signature = await source.ReadSmallAsync(
            folder + "update-manifest.sig", 8192, cancellationToken);
        var manifest = UpdateManifestPolicy.ReadVerified(
            manifestBytes, signature, pinnedPublisherPublicKey);
        if (!string.Equals(manifest.Version, version, StringComparison.Ordinal))
            throw new InvalidDataException("Signed application version does not match the published channel.");
        return new VerifiedUpdate(manifest, version, manifestBytes, signature);
    }

    internal static UpdateDownloadChoice Choose(
        VerifiedUpdate release, string installedVersion, int installedSchema,
        string installedFolder)
    {
        ArgumentNullException.ThrowIfNull(release);
        var baseline = release.Manifest.BaseFiles;
        var digestMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (release.Manifest.BaseVersion is not null &&
            UpdateManifestPolicy.CompareVersions(release.Manifest.BaseVersion, installedVersion) == 0 &&
            Directory.Exists(installedFolder))
        {
            string root = Path.GetFullPath(installedFolder);
            var all = Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories);
            // The baseline must be exact, not just a collection of a few
            // conveniently matching binaries.
            if (all.Any(path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0))
                return UpdateDownloadChoice.Full;
            var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
            if (files.Length == baseline.Count)
            {
                var expected = baseline.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
                foreach (string file in files)
                {
                    string name = Path.GetRelativePath(root, file).Replace('\\', '/');
                    if (!expected.TryGetValue(name, out var descriptor) ||
                        new FileInfo(file).Length != descriptor.Length)
                        return UpdateDownloadChoice.Full;
                    digestMap[name] = UpdateChunks.Sha256(file);
                }
            }
        }
        // Do not bypass schema gates: migrations require installer hand-off.
        return UpdateManifestPolicy.Choose(
            release.Manifest, installedVersion, installedSchema, digestMap);
    }

    internal static async Task<PreparedUpdate> PrepareAsync(
        VerifiedUpdate release, IUpdateArtifactSource source,
        UpdateDownloadChoice method, string installedFolder,
        string newStagingRoot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(source);
        if (method is UpdateDownloadChoice.NotNewer or UpdateDownloadChoice.RequiresDatabaseInstaller)
            throw new InvalidOperationException("Application-only preparation is forbidden for this update decision.");
        if (!string.Equals(release.RepositoryVersion, release.Manifest.Version, StringComparison.Ordinal) ||
            !UpdateManifestPolicy.IsPublishedVersion(release.RepositoryVersion))
            throw new InvalidDataException("Requested release is not an approved signed version.");
        if (Directory.Exists(newStagingRoot) || File.Exists(newStagingRoot))
            throw new IOException("The update staging root must not already exist.");
        string stagedRoot = Path.GetFullPath(newStagingRoot);
        string installed = Path.GetFullPath(installedFolder);
        if (IsInside(stagedRoot, installed) || IsInside(installed, stagedRoot))
            throw new InvalidOperationException("Update staging must not overlap the running application.");

        bool created = false;
        try
        {
            Directory.CreateDirectory(stagedRoot);
            created = true;
            string signedMetadata = Path.Combine(stagedRoot, "signed");
            Directory.CreateDirectory(signedMetadata);
            File.WriteAllBytes(Path.Combine(signedMetadata, "update-manifest.json"),
                release.ManifestBytes);
            File.WriteAllBytes(Path.Combine(signedMetadata, "update-manifest.sig"),
                release.Signature);
            // The caller MUST have verified the signature through
            // ReadLatestAsync before entering this function. An authenticated
            // Git transport alone does not make the release trustworthy.
            var manifest = release.Manifest;
            await AttemptAsync(manifest, release.RepositoryVersion, source,
                installed, stagedRoot, method == UpdateDownloadChoice.Incremental,
                cancellationToken);
            return new PreparedUpdate(release.RepositoryVersion,
                Path.Combine(stagedRoot, "application"), signedMetadata, method, false);
        }
        catch (InvalidDataException) when (method == UpdateDownloadChoice.Incremental && created)
        {
            // A delta can stop being applicable if a school-side DLL changed
            // between the check and staging. Rebuild from the signed FULL ZIP.
            // Never accept an invalid manifest: that was checked BEFORE this.
            string application = Path.Combine(stagedRoot, "application");
            if (Directory.Exists(application)) Directory.Delete(application, true);
            string downloaded = Path.Combine(stagedRoot, "download");
            if (Directory.Exists(downloaded)) Directory.Delete(downloaded, true);
            try
            {
                await AttemptAsync(release.Manifest, release.RepositoryVersion, source,
                    installed, stagedRoot, false, cancellationToken);
                return new PreparedUpdate(release.RepositoryVersion,
                    Path.Combine(stagedRoot, "application"), Path.Combine(stagedRoot, "signed"),
                    UpdateDownloadChoice.Full, true);
            }
            catch
            {
                Directory.Delete(stagedRoot, recursive: true);
                throw;
            }
        }
        catch
        {
            if (created && Directory.Exists(stagedRoot))
                Directory.Delete(stagedRoot, recursive: true);
            throw;
        }
    }

    private static async Task AttemptAsync(
        UpdateManifest manifest, string version, IUpdateArtifactSource source,
        string installed, string root, bool incremental, CancellationToken token)
    {
        UpdateArchive archive = incremental
            ? manifest.Incremental ?? throw new InvalidDataException("No signed incremental package.")
            : manifest.Full;
        // Never build a URL/path from unchecked remote metadata.
        UpdateManifestPolicy.ValidateArchive(archive);
        string partDir = Path.Combine(root, "download", "parts");
        string assembledDir = Path.Combine(root, "download", "assembled");
        Directory.CreateDirectory(partDir);
        Directory.CreateDirectory(assembledDir);
        foreach (var part in archive.Parts)
        {
            token.ThrowIfCancellationRequested();
            string relative = "versions/" + version + "/" + part.Name;
            string target = Path.Combine(partDir, part.Name);
            await source.FetchPartAsync(relative, target, part.Length, token);
            var downloaded = new FileInfo(target);
            if (!downloaded.Exists || downloaded.Length != part.Length ||
                !string.Equals(UpdateChunks.Sha256(target), part.Sha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Private Git update part failed its signed SHA-256 digest.");
        }
        string zip = Path.Combine(assembledDir, archive.Name);
        UpdateChunks.ReassembleVerified(archive, partDir, zip);
        UpdateStager.Prepare(manifest, zip, installed,
            Path.Combine(root, "application"), incremental);
    }

    private static bool IsInside(string path, string folder)
    {
        string candidate = Path.TrimEndingDirectorySeparator(path);
        string root = Path.TrimEndingDirectorySeparator(folder);
        return candidate.Equals(root, StringComparison.OrdinalIgnoreCase) ||
            candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>At runtime the publisher key MUST be embedded in the signed FMSC
/// binary at build time. Do not load it from an unsigned Git config or from
/// the update repository itself.</summary>
internal static class UpdatePublisherTrust
{
    internal const string EmbeddedName = "FMSC_SMS.Updates.PublisherPublicKey";
    internal static RSA LoadPinnedPublicKey()
    {
        using Stream resource = typeof(UpdatePublisherTrust).Assembly
            .GetManifestResourceStream(EmbeddedName)
            ?? throw new InvalidOperationException(
                "FMSC publisher public key has not yet been provisioned for this build. " +
                "Signed updates remain disabled.");
        using var reader = new StreamReader(resource);
        string pem = reader.ReadToEnd();
        using var key = RSA.Create();
        key.ImportFromPem(pem);
        if (key.KeySize < 3072)
            throw new CryptographicException("Configured update publisher key is too short.");
        var verified = RSA.Create();
        verified.ImportParameters(key.ExportParameters(false));
        return verified;
    }
}
