using FMSC_SMS.Core.Updates;

internal static class UpdatePackageVerification
{
    internal static string ExtractFull(string package, string output)
    {
        var manifest = Read(package);
        CreateOutput(package, output);
        string zip = Path.Combine(output, manifest.Full.Name);
        UpdateChunks.ReassembleVerified(manifest.Full, package, zip);
        string application = Path.Combine(output, "application");
        UpdateStager.Prepare(manifest, zip, Path.Combine(output, "unused-base"), application, false);
        return application;
    }

    internal static void Verify(string package, string? baseline, string output)
    {
        var manifest = Read(package);
        ExtractFull(package, output);
        if (manifest.Incremental is null) return;
        if (baseline is null) throw new InvalidDataException("Incremental verification needs the verified baseline.");
        string zip = Path.Combine(output, manifest.Incremental.Name);
        UpdateChunks.ReassembleVerified(manifest.Incremental, package, zip);
        UpdateStager.Prepare(manifest, zip, baseline, Path.Combine(output, "incremental-application"), true);
    }

    private static UpdateManifest Read(string package) => UpdateManifestPolicy.ReadValidated(
        File.ReadAllBytes(Path.Combine(package, "update-manifest.json")));

    private static void CreateOutput(string package, string output)
    {
        string source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(package));
        string target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(output));
        if (File.Exists(target) || Directory.Exists(target) || source.Equals(target, StringComparison.OrdinalIgnoreCase) ||
            source.StartsWith(target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            target.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Verification output must be a new independent directory.");
        Directory.CreateDirectory(target);
    }
}
