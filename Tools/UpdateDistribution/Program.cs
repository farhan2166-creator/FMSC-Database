using System.Security.Cryptography;
using System.Text.Json;
using FMSC_SMS.Core.Updates;

try
{
    if (args.SequenceEqual(new[] { "--self-test" }, StringComparer.Ordinal))
    {
        SnapshotTests.Run();
        Console.WriteLine("PASS: two-version signed distribution snapshot and retention tests.");
        return;
    }

    var options = Parse(args);
    options.TryGetValue("previous-snapshot-dir", out var previous);
    var result = DistributionSnapshot.Prepare(
        Required(options, "new-package-dir"),
        previous,
        Required(options, "output-dir"),
        Required(options, "publisher-public-key"));
    Console.WriteLine("Verified private Git distribution snapshot: latest " + result.LatestVersion +
        ", previous " + (result.PreviousVersion ?? "(none)") +
        ". Source packages remain untouched. Nothing was published or pushed.");
}
catch (Exception exception)
{
    Console.Error.WriteLine("UPDATE SNAPSHOT REJECTED: " + exception.Message);
    Environment.ExitCode = 1;
}

static Dictionary<string, string> Parse(string[] args)
{
    if (args.Length == 0 || args.Length % 2 != 0)
        throw new ArgumentException("Use --new-package-dir --output-dir --publisher-public-key " +
            "[--previous-snapshot-dir] or --self-test.");
    var found = new Dictionary<string, string>(StringComparer.Ordinal);
    for (int i = 0; i < args.Length; i += 2)
    {
        if (!args[i].StartsWith("--", StringComparison.Ordinal) ||
            !new[] { "new-package-dir", "previous-snapshot-dir", "output-dir",
                "publisher-public-key" }.Contains(args[i][2..], StringComparer.Ordinal) ||
            !found.TryAdd(args[i][2..], args[i + 1]))
            throw new ArgumentException("Invalid or duplicate distribution option.");
    }
    return found;
}

static string Required(Dictionary<string, string> args, string name) =>
    args.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value : throw new ArgumentException("Missing --" + name);

internal sealed class UpdateChannel
{
    public string Format { get; set; } = "fmsc-git-updates/v1";
    public string LatestVersion { get; set; } = "";
    public string? PreviousVersion { get; set; }
}

internal static class DistributionSnapshot
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        WriteIndented = true
    };

    internal static UpdateChannel Prepare(string newPackageDirectory,
        string? previousSnapshotDirectory, string outputDirectory, string publisherPublicKeyPath)
    {
        string package = Path.GetFullPath(newPackageDirectory);
        string output = Path.GetFullPath(outputDirectory);
        string? previous = previousSnapshotDirectory is null ? null : Path.GetFullPath(previousSnapshotDirectory);
        if (!Directory.Exists(package) || (previous is not null && !Directory.Exists(previous)))
            throw new DirectoryNotFoundException("Release package or previously published snapshot directory is missing.");
        if (Directory.Exists(output) || File.Exists(output) ||
            Overlaps(output, package) || (previous is not null && Overlaps(output, previous)))
            throw new InvalidOperationException("Output must be a new independent snapshot directory.");
        using var key = RSA.Create();
        key.ImportFromPem(File.ReadAllText(publisherPublicKeyPath));
        if (key.KeySize < 3072)
            throw new CryptographicException("Publisher signing public key must be at least 3072 bits.");

        var current = ReadSignedPackage(package, key);
        string? priorVersion = null;
        string? priorPackage = null;
        if (previous is not null)
        {
            string pointer = Path.Combine(previous, "channel", "stable.json");
            if (!File.Exists(pointer))
                throw new InvalidDataException("Previous distribution snapshot has no stable channel pointer.");
            byte[] pointerBytes = File.ReadAllBytes(pointer);
            if (pointerBytes.Length is < 1 or > 4096)
                throw new InvalidDataException("Invalid previous stable channel pointer.");
            var oldPointer = JsonSerializer.Deserialize<UpdateChannel>(pointerBytes, JsonOptions)
                ?? throw new InvalidDataException("Invalid previous update pointer.");
            if (oldPointer.Format != "fmsc-git-updates/v1" ||
                !IsSimpleVersion(oldPointer.LatestVersion) ||
                (oldPointer.PreviousVersion is not null &&
                 (!IsSimpleVersion(oldPointer.PreviousVersion) ||
                  UpdateManifestPolicy.CompareVersions(oldPointer.LatestVersion, oldPointer.PreviousVersion) <= 0)))
                throw new InvalidDataException("Previous distribution pointer is invalid.");
            priorVersion = oldPointer.LatestVersion;
            priorPackage = Path.Combine(previous, "versions", priorVersion);
            var prior = ReadSignedPackage(priorPackage, key);
            if (prior.Version != priorVersion ||
                UpdateManifestPolicy.CompareVersions(current.Version, priorVersion) <= 0)
                throw new InvalidDataException("New approved version must be newer than the previous published version.");
            if (current.Incremental is not null &&
                UpdateManifestPolicy.CompareVersions(current.BaseVersion!, priorVersion) != 0)
                throw new InvalidDataException("Incremental release must be built against the retained previous version.");
        }

        var snapshot = new UpdateChannel
        {
            LatestVersion = current.Version,
            PreviousVersion = priorVersion
        };
        bool created = false;
        try
        {
            Directory.CreateDirectory(output);
            created = true;
            string versions = Path.Combine(output, "versions");
            Directory.CreateDirectory(versions);
            if (priorPackage is not null && priorVersion is not null)
                CopyVerifiedPackage(priorPackage, Path.Combine(versions, priorVersion), key);
            CopyVerifiedPackage(package, Path.Combine(versions, current.Version), key);
            string channelDir = Path.Combine(output, "channel");
            Directory.CreateDirectory(channelDir);
            // A distribution publisher must switch the Git branch in one atomic commit.
            // Never upload a half-complete manifest or promote a partial snapshot.
            File.WriteAllBytes(Path.Combine(channelDir, "stable.json"),
                JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions));
            File.WriteAllText(Path.Combine(output, "README.md"),
                "# FMSC signed update distribution\n\n" +
                "Private Git distribution. Current tree contains at most two approved versions.\n" +
                "Do not place school databases, application source or private credentials here.\n");
            ValidateSnapshot(output, key);
            return snapshot;
        }
        catch
        {
            if (created && Directory.Exists(output))
                Directory.Delete(output, recursive: true);
            throw;
        }
    }

    internal static void ValidateSnapshot(string root, RSA publisherPublicKey)
    {
        string pointer = Path.Combine(root, "channel", "stable.json");
        var snapshot = JsonSerializer.Deserialize<UpdateChannel>(
            File.ReadAllBytes(pointer), JsonOptions)
            ?? throw new InvalidDataException("Snapshot pointer missing.");
        if (snapshot.Format != "fmsc-git-updates/v1" ||
            !IsSimpleVersion(snapshot.LatestVersion))
            throw new InvalidDataException("Unrecognized snapshot format.");
        string versionsDir = Path.Combine(root, "versions");
        var versions = Directory.GetDirectories(versionsDir)
            .Select(path => Path.GetFileName(path) ?? throw new InvalidDataException("Invalid version directory.")).ToArray();
        if (versions.Length is < 1 or > 2 ||
            !versions.Contains(snapshot.LatestVersion, StringComparer.Ordinal) ||
            (snapshot.PreviousVersion is null
                ? versions.Length != 1
                : versions.Length != 2 || !versions.Contains(snapshot.PreviousVersion, StringComparer.Ordinal)))
            throw new InvalidDataException("Only the newest one or two signed versions may appear in the snapshot.");
        foreach (string version in versions)
        {
            var manifest = ReadSignedPackage(Path.Combine(versionsDir, version), publisherPublicKey);
            if (manifest.Version != version)
                throw new InvalidDataException("Signed release version does not match its snapshot folder.");
        }
        if (snapshot.PreviousVersion is not null &&
            UpdateManifestPolicy.CompareVersions(snapshot.LatestVersion, snapshot.PreviousVersion) <= 0)
            throw new InvalidDataException("Stable channel version order is invalid.");
    }

    private static UpdateManifest ReadSignedPackage(string directory, RSA key)
    {
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException("Signed update release is missing.");
        foreach (var path in Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.AllDirectories))
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Symbolic links are prohibited in release packages.");

        string manifestPath = Path.Combine(directory, "update-manifest.json");
        string signaturePath = Path.Combine(directory, "update-manifest.sig");
        if (!File.Exists(manifestPath) || !File.Exists(signaturePath))
            throw new InvalidDataException("Signed release metadata is missing.");
        var manifest = UpdateManifestPolicy.ReadVerified(
            File.ReadAllBytes(manifestPath), File.ReadAllBytes(signaturePath), key);
        VerifyParts(directory, manifest.Full);
        if (manifest.Incremental is not null) VerifyParts(directory, manifest.Incremental);
        return manifest;
    }

    private static void VerifyParts(string directory, UpdateArchive archive)
    {
        UpdateManifestPolicy.ValidateArchive(archive);
        foreach (var part in archive.Parts)
        {
            string path = Path.Combine(directory, part.Name);
            var info = new FileInfo(path);
            if (!info.Exists || info.Attributes.HasFlag(FileAttributes.ReparsePoint) ||
                info.Length != part.Length ||
                !StringComparer.OrdinalIgnoreCase.Equals(UpdateChunks.Sha256(path), part.Sha256))
                throw new InvalidDataException("Update archive part failed signed verification: " + part.Name);
        }
    }

    private static void CopyVerifiedPackage(string inputDir, string destination, RSA key)
    {
        var manifest = ReadSignedPackage(inputDir, key);
        Directory.CreateDirectory(destination);
        foreach (string metadata in new[] { "update-manifest.json", "update-manifest.sig" })
            File.Copy(Path.Combine(inputDir, metadata), Path.Combine(destination, metadata));
        foreach (var archive in new[] { manifest.Full, manifest.Incremental }.OfType<UpdateArchive>())
            foreach (var part in archive.Parts)
                File.Copy(Path.Combine(inputDir, part.Name), Path.Combine(destination, part.Name));
        // Prevent the unchunked, potentially >100MiB ZIP being committed to Git.
        // Re-verify the copied metadata and every new part before promotion.
        ReadSignedPackage(destination, key);
    }

    private static bool IsSimpleVersion(string? version)
    {
        if (string.IsNullOrEmpty(version) || version.Length > 100 ||
            version.Contains('/') || version.Contains('\\') || version.Contains(':') ||
            version == "." || version == "..")
            return false;
        try
        {
            return UpdateManifestPolicy.CompareVersions(version, version) == 0;
        }
        catch (FormatException) { return false; }
        catch (OverflowException) { return false; }
    }

    private static bool Overlaps(string first, string second)
    {
        string a = Path.TrimEndingDirectorySeparator(first);
        string b = Path.TrimEndingDirectorySeparator(second);
        return a.Equals(b, StringComparison.OrdinalIgnoreCase) ||
            a.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            b.StartsWith(a + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}

internal static class SnapshotTests
{
    internal static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "fmsc-snapshot-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var signer = RSA.Create(3072);
            string publicPath = Path.Combine(root, "publisher-public.pem");
            File.WriteAllText(publicPath, signer.ExportRSAPublicKeyPem());

            string first = CreateDummySignedPackage(root, "1.0.0", signer);
            var initial = DistributionSnapshot.Prepare(first, null, Path.Combine(root, "snapshot1"), publicPath);
            Require(initial.LatestVersion == "1.0.0" && initial.PreviousVersion is null, "First snapshot must retain one release.");
            string next = CreateDummySignedPackage(root, "1.1.0", signer);
            var second = DistributionSnapshot.Prepare(next, Path.Combine(root, "snapshot1"),
                Path.Combine(root, "snapshot2"), publicPath);
            Require(second.PreviousVersion == "1.0.0", "Second snapshot must retain the previous release.");
            string third = CreateDummySignedPackage(root, "1.2.0", signer);
            var thirdResult = DistributionSnapshot.Prepare(third, Path.Combine(root, "snapshot2"),
                Path.Combine(root, "snapshot3"), publicPath);
            Require(thirdResult.LatestVersion == "1.2.0" && thirdResult.PreviousVersion == "1.1.0",
                "Third snapshot must keep only the newest two versions.");
            Require(!Directory.Exists(Path.Combine(root, "snapshot3", "versions", "1.0.0")),
                "Third version must prune the first version from the published tree.");
            Require(Directory.GetDirectories(Path.Combine(root, "snapshot3", "versions")).Length == 2,
                "Published Git snapshot must contain exactly two versions.");
            Require(!Directory.EnumerateFiles(Path.Combine(root, "snapshot3"), "*.zip", SearchOption.AllDirectories).Any(),
                "Never include unchunked full ZIPs in Git distribution.");
            string corrupt = Path.Combine(root, "snapshot3", "versions", "1.2.0",
                UpdateChunks.MakeName("FMSC_SMS-app-full.zip", 1));
            File.WriteAllText(corrupt, "tampered download");
            try
            {
                DistributionSnapshot.ValidateSnapshot(Path.Combine(root, "snapshot3"), signer);
                throw new Exception("Tampered published package passed validation.");
            }
            catch (InvalidDataException) { }
            Require(File.Exists(Path.Combine(root, "snapshot2", "versions", "1.0.0", "update-manifest.json")),
                "Snapshot preparation must not modify its predecessor.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateDummySignedPackage(string root, string version, RSA signer)
    {
        string folder = Path.Combine(root, "package-" + version);
        Directory.CreateDirectory(folder);
        string zip = Path.Combine(folder, "FMSC_SMS-app-full.zip");
        File.WriteAllBytes(zip, RandomNumberGenerator.GetBytes(2048));
        var manifest = new UpdateManifest
        {
            Version = version,
            RequiredDatabaseSchema = 75,
            Files = new List<UpdateFile>
            {
                new() { Path = "FMSC_SMS.exe", Length = 4, Sha256 = new string('a', 64) }
            },
            Full = new UpdateArchive
            {
                Name = "FMSC_SMS-app-full.zip",
                Length = new FileInfo(zip).Length,
                Sha256 = UpdateChunks.Sha256(zip),
                Parts = UpdateChunks.SplitArchive(zip, maximumPartBytes: 512)
            }
        };
        byte[] raw = UpdateManifestPolicy.Serialize(manifest);
        File.WriteAllBytes(Path.Combine(folder, "update-manifest.json"), raw);
        File.WriteAllBytes(Path.Combine(folder, "update-manifest.sig"),
            signer.SignData(raw, HashAlgorithmName.SHA256, RSASignaturePadding.Pss));
        return folder;
    }

    private static void Require(bool assertion, string reason)
    {
        if (!assertion) throw new Exception(reason);
    }
}
