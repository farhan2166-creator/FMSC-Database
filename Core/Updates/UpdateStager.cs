using System.IO.Compression;
using System.Security.Cryptography;

namespace FMSC_SMS.Core.Updates;

/// <summary>
/// Reconstructs a complete new application in a separate, non-live directory.
/// NEVER replaces in-use files, never runs migrations and never activates a build.
/// The caller must first verify the manifest's publisher signature and authorize
/// the download through the private update broker. Activation is a separate,
/// operator-approved operation after FMSC has exited.
/// </summary>
internal static class UpdateStager
{
    internal static void Prepare(UpdateManifest manifest, string archivePath,
        string installedDirectory, string stagingDirectory, bool incremental)
    {
        UpdateManifestPolicy.Validate(manifest);
        string installed = Path.GetFullPath(installedDirectory);
        string staged = Path.GetFullPath(stagingDirectory);
        if (Directory.Exists(staged) || File.Exists(staged) ||
            IsInside(staged, installed) || IsInside(installed, staged))
            throw new InvalidOperationException("Staging must be a new separate directory.");
        UpdateArchive archive = incremental
            ? manifest.Incremental ?? throw new InvalidDataException("Release has no incremental archive.")
            : manifest.Full;
        VerifyArchive(archivePath, archive);
        if (incremental)
        {
            if (manifest.BaseVersion is null)
                throw new InvalidDataException("No verified base is specified for the incremental update.");
            VerifyInstalledBase(installed, manifest.BaseFiles);
        }

        var expected = manifest.Files.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
        var archivePaths = (incremental ? manifest.ChangedFiles : manifest.Files.Select(f => f.Path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool created = false;
        try
        {
            Directory.CreateDirectory(staged);
            created = true;
            if (incremental)
            {
                foreach (var target in manifest.Files)
                {
                    if (archivePaths.Contains(target.Path)) continue;
                    string source = SafePath(installed, target.Path);
                    string destination = SafePath(staged, target.Path);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(source, destination, overwrite: false);
                }
            }
            using (var zip = ZipFile.OpenRead(archivePath))
            {
                var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in zip.Entries)
                {
                    if (!UpdateManifestPolicy.IsSafeRelativePath(entry.FullName) ||
                        !archivePaths.Contains(entry.FullName) ||
                        !found.Add(entry.FullName) ||
                        !expected.TryGetValue(entry.FullName, out var descriptor) ||
                        entry.Length != descriptor.Length ||
                        (entry.ExternalAttributes & 0x400) != 0)
                        throw new InvalidDataException("Unexpected, unsafe or duplicate update ZIP entry.");
                    string destination = SafePath(staged, descriptor.Path);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    using var source = entry.Open();
                    using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    source.CopyTo(target);
                    if (target.Length != descriptor.Length)
                        throw new InvalidDataException("Downloaded application file length mismatch.");
                }
                if (found.Count != archivePaths.Count)
                    throw new InvalidDataException("Update archive is missing application files.");
            }

            // Verify every staged file, including those copied from the installed baseline,
            // before any outside component is permitted to activate the new version.
            var physicalFiles = Directory.GetFiles(staged, "*", SearchOption.AllDirectories);
            if (physicalFiles.Length != manifest.Files.Count)
                throw new InvalidDataException("Staged application inventory differs from the signed manifest.");
            foreach (var file in manifest.Files)
            {
                string path = SafePath(staged, file.Path);
                var info = new FileInfo(path);
                if (!info.Exists || info.Length != file.Length || !SameHash(path, file.Sha256))
                    throw new InvalidDataException("Staged application digest differs from signed release metadata.");
            }
        }
        catch
        {
            if (created && Directory.Exists(staged))
                Directory.Delete(staged, recursive: true);
            throw;
        }
    }

    private static void VerifyInstalledBase(string installed, IReadOnlyList<UpdateFile> files)
    {
        if (!Directory.Exists(installed))
            throw new InvalidDataException("Installed application base is unavailable.");
        foreach (string path in Directory.EnumerateFileSystemEntries(installed, "*", SearchOption.AllDirectories))
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Installed application contains a reparse point; use full package.");
        if (Directory.GetFiles(installed, "*", SearchOption.AllDirectories).Length != files.Count)
            throw new InvalidDataException("Installed application inventory has changed; use full package.");
        foreach (var file in files)
        {
            string path = SafePath(installed, file.Path);
            var info = new FileInfo(path);
            if (!info.Exists || info.Length != file.Length || !SameHash(path, file.Sha256))
                throw new InvalidDataException("Installed application base has changed; use full package.");
        }
    }

    private static void VerifyArchive(string path, UpdateArchive descriptor)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length != descriptor.Length || !SameHash(path, descriptor.Sha256))
            throw new InvalidDataException("Downloaded archive failed its signed SHA-256 integrity check.");
    }

    private static bool SameHash(string path, string expected)
    {
        using var file = File.OpenRead(path);
        string actual = Convert.ToHexString(SHA256.HashData(file));
        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static string SafePath(string root, string relative)
    {
        if (!UpdateManifestPolicy.IsSafeRelativePath(relative))
            throw new InvalidDataException("Unsafe target path.");
        string full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsInside(full, root) || string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Application path escaped its destination directory.");
        return full;
    }

    private static bool IsInside(string path, string root)
    {
        string normalizedRoot = Path.TrimEndingDirectorySeparator(root);
        return string.Equals(path, normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
