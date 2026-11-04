using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FMSC_SMS.Core.Updates;

/// <summary>
/// Immutable, signed RELEASE metadata. No executable is trusted until the raw manifest
/// bytes have passed RSA-PSS verification against the publisher's pinned public key.
/// Access is through per-computer read-only SSH credentials for a separate
/// private distribution repository; no shared key is embedded in FMSC.
/// </summary>
internal sealed class UpdateManifest
{
    public string Format { get; set; } = "fmsc-app-update/v1";
    public string Version { get; set; } = "";
    public string? BaseVersion { get; set; }
    public int RequiredDatabaseSchema { get; set; }
    public List<UpdateFile> BaseFiles { get; set; } = new();
    public List<UpdateFile> Files { get; set; } = new();
    public List<string> ChangedFiles { get; set; } = new();
    public UpdateArchive Full { get; set; } = new();
    public UpdateArchive? Incremental { get; set; }
}

internal sealed class UpdateFile
{
    public string Path { get; set; } = "";
    public long Length { get; set; }
    public string Sha256 { get; set; } = "";
}

internal sealed class UpdateArchive
{
    public string Name { get; set; } = "";
    public long Length { get; set; }
    public string Sha256 { get; set; } = "";
    public List<UpdatePart> Parts { get; set; } = new();
}

internal sealed class UpdatePart
{
    public string Name { get; set; } = "";
    public long Length { get; set; }
    public string Sha256 { get; set; } = "";
}

internal enum UpdateDownloadChoice
{
    NotNewer,
    Incremental,
    Full,
    RequiresDatabaseInstaller
}

internal static class UpdateManifestPolicy
{
    private const int MaximumManifestBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        WriteIndented = true
    };

    internal static byte[] Serialize(UpdateManifest manifest)
    {
        Validate(manifest);
        return JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
    }

    internal static UpdateManifest ReadVerified(byte[] data, byte[] signature, RSA publisherPublicKey)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(publisherPublicKey);
        if (data.Length == 0 || data.Length > MaximumManifestBytes ||
            !publisherPublicKey.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
            throw new CryptographicException("The update manifest publisher signature is invalid.");
        var manifest = JsonSerializer.Deserialize<UpdateManifest>(data, JsonOptions)
            ?? throw new InvalidDataException("The signed update manifest is empty.");
        Validate(manifest);
        return manifest;
    }

    internal static void Validate(UpdateManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.Format != "fmsc-app-update/v1" ||
            !IsVersion(manifest.Version) || manifest.RequiredDatabaseSchema <= 0)
            throw new InvalidDataException("Unsupported update manifest format, version or schema.");
        ValidateArchive(manifest.Full);
        ValidateFiles(manifest.Files, allowEmpty: false);
        if (!manifest.Files.Any(f => string.Equals(f.Path, "FMSC_SMS.exe", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("The update manifest does not contain the application executable.");

        if (manifest.BaseVersion is null)
        {
            if (manifest.BaseFiles.Count != 0 || manifest.ChangedFiles.Count != 0 || manifest.Incremental is not null)
                throw new InvalidDataException("A full-only release must not claim an incremental base.");
            return;
        }
        if (!IsVersion(manifest.BaseVersion) || CompareVersions(manifest.Version, manifest.BaseVersion) <= 0)
            throw new InvalidDataException("The incremental base must be an older application version.");
        ValidateFiles(manifest.BaseFiles, allowEmpty: false);
        ValidateArchive(manifest.Incremental ?? throw new InvalidDataException("Missing incremental archive."));
        var changed = FindChanged(manifest.BaseFiles, manifest.Files);
        if (!changed.SequenceEqual(manifest.ChangedFiles, StringComparer.Ordinal))
            throw new InvalidDataException("The delta file inventory does not match the signed base and target files.");
    }

    internal static UpdateDownloadChoice Choose(
        UpdateManifest manifest,
        string installedVersion,
        int installedSchema,
        IReadOnlyDictionary<string, string> installedFileHashes)
    {
        Validate(manifest);
        if (!IsVersion(installedVersion))
            throw new ArgumentException("Installed version is invalid.", nameof(installedVersion));
        if (CompareVersions(manifest.Version, installedVersion) <= 0)
            return UpdateDownloadChoice.NotNewer;
        // SQL migrations are never applied by the incremental file installer.
        if (installedSchema != manifest.RequiredDatabaseSchema)
            return UpdateDownloadChoice.RequiresDatabaseInstaller;
        if (manifest.BaseVersion is null ||
            CompareVersions(installedVersion, manifest.BaseVersion) != 0 ||
            installedFileHashes.Count != manifest.BaseFiles.Count)
            return UpdateDownloadChoice.Full;

        foreach (var file in manifest.BaseFiles)
        {
            if (!installedFileHashes.TryGetValue(file.Path, out var digest) ||
                !string.Equals(digest, file.Sha256, StringComparison.OrdinalIgnoreCase))
                return UpdateDownloadChoice.Full;
        }
        return UpdateDownloadChoice.Incremental;
    }

    internal static List<string> FindChanged(IReadOnlyList<UpdateFile> previous, IReadOnlyList<UpdateFile> current)
    {
        var old = previous.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
        return current
            .Where(f => !old.TryGetValue(f.Path, out var earlier) ||
                earlier.Length != f.Length ||
                !string.Equals(earlier.Sha256, f.Sha256, StringComparison.OrdinalIgnoreCase))
            .Select(f => f.Path)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
    }

    internal static int CompareVersions(string left, string right)
    {
        if (!IsVersion(left) || !IsVersion(right))
            throw new FormatException("Update versions must use semantic version numbers.");
        var a = left.Split('-', 2);
        var b = right.Split('-', 2);
        var x = a[0].Split('.').Select(int.Parse).ToArray();
        var y = b[0].Split('.').Select(int.Parse).ToArray();
        for (int i = 0; i < 3; i++)
        {
            int result = x[i].CompareTo(y[i]);
            if (result != 0) return result;
        }
        if (a.Length == 1 || b.Length == 1)
            return a.Length == b.Length ? 0 : a.Length == 1 ? 1 : -1;
        var ap = a[1].Split('.');
        var bp = b[1].Split('.');
        for (int i = 0; i < Math.Min(ap.Length, bp.Length); i++)
        {
            bool an = ulong.TryParse(ap[i], out var ai);
            bool bn = ulong.TryParse(bp[i], out var bi);
            int result = an && bn ? ai.CompareTo(bi)
                : an ? -1 : bn ? 1 : StringComparer.Ordinal.Compare(ap[i], bp[i]);
            if (result != 0) return result;
        }
        return ap.Length.CompareTo(bp.Length);
    }

    private static bool IsVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 100 ||
            !Regex.IsMatch(value, @"^\d+\.\d+\.\d+(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$"))
            return false;
        var core = value.Split('-', 2)[0].Split('.');
        return core.All(part => int.TryParse(part, out _));
    }

    private static void ValidateFiles(List<UpdateFile> files, bool allowEmpty)
    {
        if (files is null || (!allowEmpty && files.Count == 0) || files.Count > 20000)
            throw new InvalidDataException("Invalid update file count.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            if (file is null || file.Length < 0 || !IsSafeRelativePath(file.Path) ||
                !IsSha256(file.Sha256) || !seen.Add(file.Path))
                throw new InvalidDataException("Duplicate, unsafe or invalid update file.");
        }
    }

    internal static bool IsSafeRelativePath(string? path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 240 || path.Contains('\\') ||
            path.StartsWith('/') || path.Any(char.IsControl))
            return false;
        foreach (var section in path.Split('/'))
        {
            if (section.Length == 0 || section == "." || section == ".." ||
                section.EndsWith('.') || section.EndsWith(' ') ||
                section.IndexOfAny(new[] { ':', '*', '?', '"', '<', '>', '|' }) >= 0)
                return false;
            var stem = section.Split('.')[0];
            if (Regex.IsMatch(stem, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase))
                return false;
        }
        return true;
    }

    internal static void ValidateArchive(UpdateArchive archive)
    {
        if (archive is null || archive.Length <= 0 || !IsSafeRelativePath(archive.Name) ||
            archive.Name.Contains('/') || !archive.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
            !IsSha256(archive.Sha256) || archive.Parts is null ||
            archive.Parts.Count is < 1 or > 2000)
            throw new InvalidDataException("Unsafe or invalid update archive.");
        long total = 0;
        for (int i = 0; i < archive.Parts.Count; i++)
        {
            var part = archive.Parts[i];
            if (part is null ||
                !string.Equals(part.Name, UpdateChunks.MakeName(archive.Name, i + 1), StringComparison.Ordinal) ||
                !IsSafeRelativePath(part.Name) || part.Name.Contains('/') ||
                part.Length is < 1 or > UpdateChunks.DefaultMaximumPartBytes ||
                !IsSha256(part.Sha256) || total > archive.Length - part.Length)
                throw new InvalidDataException("Unsafe, missing or invalid signed archive part.");
            total += part.Length;
        }
        if (total != archive.Length)
            throw new InvalidDataException("Signed archive parts do not account for the full ZIP length.");
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } &&
        value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');
}
