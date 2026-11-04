using System.Security.Cryptography;
using System.Text.Json;
using FMSC_SMS.Core.Updates;

/// <summary>Disposable, offline release transport exercising the private Git
/// manifest, bounded SHA-256 checked chunks and isolated application staging.</summary>
internal static class UpdatePreparationTests
{
    internal static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "fmsc-release-preparation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string oldApp = Path.Combine(root, "installed");
            string newApp = Path.Combine(root, "published");
            Directory.CreateDirectory(oldApp);
            Directory.CreateDirectory(newApp);
            File.WriteAllText(Path.Combine(oldApp, "FMSC_SMS.exe"), "original exe");
            File.WriteAllText(Path.Combine(oldApp, "main.dll"), "before");
            File.WriteAllText(Path.Combine(newApp, "FMSC_SMS.exe"), "original exe");
            File.WriteAllText(Path.Combine(newApp, "main.dll"), "after");
            File.WriteAllText(Path.Combine(newApp, "added.txt"), "target");
            string package = Path.Combine(root, "package");
            var packaged = PackageBuilder.Build(newApp, package,
                "1.1.0", 75, oldApp, "1.0.0");

            string feed = Path.Combine(root, "feed");
            string published = Path.Combine(feed, "versions", "1.1.0");
            Directory.CreateDirectory(published);
            Directory.CreateDirectory(Path.Combine(feed, "channel"));
            File.WriteAllText(Path.Combine(feed, "channel", "stable.json"),
                JsonSerializer.Serialize(new
                {
                    format = "fmsc-git-updates/v1",
                    latestVersion = "1.1.0",
                    previousVersion = "1.0.0"
                }));
            File.Copy(Path.Combine(package, "update-manifest.json"),
                Path.Combine(published, "update-manifest.json"));
            foreach (var part in packaged.Full.Parts.Concat(packaged.Incremental!.Parts))
                File.Copy(Path.Combine(package, part.Name), Path.Combine(published, part.Name));

            var source = new FolderUpdateSource(feed);
            VerifiedUpdate verified = UpdateReleasePreparer.ReadLatestAsync(
                source, CancellationToken.None).GetAwaiter().GetResult()
                ?? throw new Exception("Missing published latest release.");
            Require(verified.Manifest.Version == "1.1.0", "Published version mismatch.");
            Require(UpdateReleasePreparer.Choose(verified, "1.0.0", 75, oldApp)
                == UpdateDownloadChoice.Incremental, "Exact baseline should use incremental.");
            Require(UpdateReleasePreparer.Choose(verified, "0.9.0", 75, oldApp)
                == UpdateDownloadChoice.Full, "Old installed version should use full package.");
            Require(UpdateReleasePreparer.Choose(verified, "1.0.0", 74, oldApp)
                == UpdateDownloadChoice.RequiresDatabaseInstaller,
                "Schema mismatch must require existing database migration installer.");

            string staging1 = Path.Combine(root, "staged-incremental");
            PreparedUpdate staged = UpdateReleasePreparer.PrepareAsync(verified, source,
                UpdateDownloadChoice.Incremental, oldApp, staging1, CancellationToken.None)
                .GetAwaiter().GetResult();
            Require(staged.Method == UpdateDownloadChoice.Incremental &&
                File.ReadAllText(Path.Combine(staged.StageDirectory, "main.dll")) == "after" &&
                File.Exists(Path.Combine(staged.StageDirectory, "added.txt")),
                "Verified changed-file package must reconstruct the complete new application.");
            Require(File.ReadAllText(Path.Combine(oldApp, "main.dll")) == "before",
                "Preparation must never modify the installed application.");
            Require(File.Exists(Path.Combine(staged.ManifestDirectory, "update-manifest.json")),
                "Staged updater must preserve validated manifest for later installer checks.");

            // A different local base after the version check may not receive a delta.
            File.WriteAllText(Path.Combine(oldApp, "main.dll"), "modified at school");
            string staging2 = Path.Combine(root, "staged-fallback");
            PreparedUpdate fallback = UpdateReleasePreparer.PrepareAsync(verified, source,
                UpdateDownloadChoice.Incremental, oldApp, staging2, CancellationToken.None)
                .GetAwaiter().GetResult();
            Require(fallback.Method == UpdateDownloadChoice.Full && fallback.UsedFullFallback &&
                File.ReadAllText(Path.Combine(fallback.StageDirectory, "main.dll")) == "after",
                "Mutated installed DLL must trigger a full, hash-verified application fallback.");

            // A damaged remote delta never becomes executable; a complete,
            // independently hash-checked full package can recover.
            string brokenDelta = Path.Combine(published, packaged.Incremental!.Parts[0].Name);
            File.WriteAllBytes(brokenDelta, RandomNumberGenerator.GetBytes(
                (int)packaged.Incremental.Parts[0].Length));
            string staging3 = Path.Combine(root, "staged-corrupt-delta");
            PreparedUpdate recovered = UpdateReleasePreparer.PrepareAsync(verified, source,
                UpdateDownloadChoice.Incremental, oldApp, staging3, CancellationToken.None)
                .GetAwaiter().GetResult();
            Require(recovered.UsedFullFallback &&
                File.ReadAllText(Path.Combine(recovered.StageDirectory, "main.dll")) == "after",
                "Corrupt delta must not be used; valid full fallback is required.");

            // Malformed or unsupported metadata must be rejected. Integrity
            // hashes are deliberately NOT publisher authentication: a writer
            // with Git repository access can change metadata and matching blobs.
            string metadata = Path.Combine(published, "update-manifest.json");
            string original = File.ReadAllText(metadata);
            File.WriteAllText(metadata, original.Replace(
                "fmsc-app-update/v1", "unsupported-update/v1", StringComparison.Ordinal));
            try
            {
                _ = UpdateReleasePreparer.ReadLatestAsync(source, CancellationToken.None)
                    .GetAwaiter().GetResult();
                throw new Exception("Unsupported update manifest was accepted.");
            }
            catch (InvalidDataException) { }

            // An unvalidated release record must NEVER enter full fallback.
            var invalidRecord = new VerifiedUpdate(verified.Manifest,
                verified.RepositoryVersion, File.ReadAllBytes(metadata));
            string invalidStage = Path.Combine(root, "refused-invalid-metadata");
            try
            {
                _ = UpdateReleasePreparer.PrepareAsync(invalidRecord, source,
                    UpdateDownloadChoice.Incremental, oldApp, invalidStage,
                    CancellationToken.None).GetAwaiter().GetResult();
                throw new Exception("Malformed manifest triggered application staging.");
            }
            catch (InvalidDataException) { }
            Require(!Directory.Exists(invalidStage),
                "Rejected manifest must not create staging output.");

            Console.WriteLine("PASS: verified Git release metadata, delta, full fallback, " +
                "schema gating, corrupt part recovery and live-app isolation.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void Require(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
    }
}

internal sealed class FolderUpdateSource : IUpdateArtifactSource
{
    private readonly string _root;
    internal FolderUpdateSource(string root) => _root = Path.GetFullPath(root);

    public Task<byte[]> ReadSmallAsync(string relativePath, int maximumBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = Resolve(relativePath);
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length > maximumBytes)
            throw new InvalidDataException("Excessive metadata length.");
        return Task.FromResult(bytes);
    }

    public Task FetchPartAsync(string relativePath, string destinationFile, long expectedBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = Resolve(relativePath);
        var info = new FileInfo(path);
        if (info.Length != expectedBytes)
            throw new InvalidDataException("Invalid stored part length.");
        File.Copy(path, destinationFile);
        return Task.CompletedTask;
    }

    private string Resolve(string name)
    {
        if (!UpdateManifestPolicy.IsSafeRelativePath(name))
            throw new InvalidDataException("Unsafe synthetic feed path.");
        string full = Path.GetFullPath(Path.Combine(_root, name.Replace('/',
            Path.DirectorySeparatorChar)));
        if (!full.StartsWith(Path.TrimEndingDirectorySeparator(_root) +
            Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Release read escaped its folder.");
        return full;
    }
}
