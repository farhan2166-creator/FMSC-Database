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
    byte[] ManifestBytes);

internal sealed record PreparedUpdate(
    string Version,
    string StageDirectory,
    string ManifestDirectory,
    UpdateDownloadChoice Method,
    bool UsedFullFallback);

/// <summary>
/// The versioned manifest comes from the owner's authenticated private Git repository.
/// Each downloaded chunk, reconstructed ZIP and staged application file is checked
/// against its published SHA-256 digest. Hashes detect mismatches, not compromised
/// repository write access. Activation and SQL are separate.
/// </summary>
internal static class UpdateReleasePreparer
{
    internal static async Task<VerifiedUpdate?> ReadLatestAsync(
        IUpdateArtifactSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        byte[] pointer = await source.ReadSmallAsync(
            "channel/stable.json", 4096, cancellationToken);
        if (pointer.Length is < 1 or > 4096)
            throw new InvalidDataException("Invalid update channel pointer size.");

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
        var manifest = UpdateManifestPolicy.ReadValidated(manifestBytes);
        if (!string.Equals(manifest.Version, version, StringComparison.Ordinal))
            throw new InvalidDataException("Update version does not match the published channel.");
        return new VerifiedUpdate(manifest, version, manifestBytes);
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
        // Reject invalid metadata BEFORE entering the incremental fallback
        // handler; a malformed manifest must never trigger a full-package stage.
        var validatedManifest = UpdateManifestPolicy.ReadValidated(release.ManifestBytes);
        if (!string.Equals(release.RepositoryVersion, validatedManifest.Version, StringComparison.Ordinal) ||
            !UpdateManifestPolicy.IsPublishedVersion(release.RepositoryVersion))
            throw new InvalidDataException("Requested release does not match the repository manifest version.");
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
            string metadata = Path.Combine(stagedRoot, "metadata");
            Directory.CreateDirectory(metadata);
            File.WriteAllBytes(Path.Combine(metadata, "update-manifest.json"),
                release.ManifestBytes);
            await AttemptAsync(validatedManifest, release.RepositoryVersion, source,
                installed, stagedRoot, method == UpdateDownloadChoice.Incremental,
                cancellationToken);
            return new PreparedUpdate(release.RepositoryVersion,
                Path.Combine(stagedRoot, "application"), metadata, method, false);
        }
        catch (InvalidDataException) when (method == UpdateDownloadChoice.Incremental && created)
        {
            // A delta can stop being applicable if a school-side DLL changed
            // between the check and staging. Retry using the full package,
            // whose file and archive digests are checked independently.
            string application = Path.Combine(stagedRoot, "application");
            if (Directory.Exists(application)) Directory.Delete(application, true);
            string downloaded = Path.Combine(stagedRoot, "download");
            if (Directory.Exists(downloaded)) Directory.Delete(downloaded, true);
            try
            {
                await AttemptAsync(validatedManifest, release.RepositoryVersion, source,
                    installed, stagedRoot, false, cancellationToken);
                return new PreparedUpdate(release.RepositoryVersion,
                    Path.Combine(stagedRoot, "application"), Path.Combine(stagedRoot, "metadata"),
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
            ? manifest.Incremental ?? throw new InvalidDataException("No incremental package in manifest.")
            : manifest.Full;
        // Never build a path from unchecked remote metadata.
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
                throw new InvalidDataException("Private Git update part failed its published SHA-256 digest.");
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

