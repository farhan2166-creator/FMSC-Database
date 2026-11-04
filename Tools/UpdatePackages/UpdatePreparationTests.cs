using System.Security.Cryptography;
using System.Text.Json;
using FMSC_SMS.Core.Updates;

/// <summary>Disposable, offline release transport exercising the same signed
/// manifest, bounded chunks, and staging paths as the real Git transport.</summary>
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
            using var signer = RSA.Create(3072);
            using var verifier = RSA.Create();
            verifier.ImportFromPem(signer.ExportRSAPublicKeyPem());
            string package = Path.Combine(root, "package");
            var signed = PackageBuilder.Build(newApp, package,
                "1.1.0", 75, oldApp, "1.0.0", signer);

            string feed = Path.Combine(root, "feed");
            string published = Path.Combine(feed, "versions", "1.1.0");
            Directory.CreateDirectory(published);
            File.WriteAllText(Path.Combine(feed, "channel", "placeholder"), "", System.Text.Encoding.UTF8);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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
