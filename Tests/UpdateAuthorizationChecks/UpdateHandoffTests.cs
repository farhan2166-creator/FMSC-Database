using FMSC_SMS.Core.Updates;

internal static class UpdateHandoffTests
{
    internal static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "fmsc-handoff-" + Guid.NewGuid().ToString("N"));
        string app = Path.Combine(root, "application");
        string metadata = Path.Combine(root, "metadata");
        Directory.CreateDirectory(Path.Combine(app, "UpdateSupport", "Migrations"));
        Directory.CreateDirectory(metadata);
        try
        {
            File.WriteAllText(Path.Combine(app, "FMSC_SMS.exe"), "inert fixture");
            File.WriteAllText(Path.Combine(app, "UpdateSupport", "core.ps1"), "$AppVersion = '2.0.0'\n$DatabaseVersion = '123'\n");
            File.WriteAllText(Path.Combine(app, "UpdateSupport", "Migrations", "123_fixture.sql"), "-- inert fixture");
            var manifest = new UpdateManifest { Version = "2.0.0", RequiredDatabaseSchema = 123,
                Files = Directory.GetFiles(app, "*", SearchOption.AllDirectories).Select(path => new UpdateFile {
                    Path = Path.GetRelativePath(app, path).Replace('\\', '/'), Length = new FileInfo(path).Length,
                    Sha256 = UpdateChunks.Sha256(path) }).ToList(),
                Full = new UpdateArchive { Name = "fixture.zip", Length = 1, Sha256 = new string('a', 64),
                    Parts = new() { new UpdatePart { Name = "fixture.zip.part0001", Length = 1, Sha256 = new string('a', 64) } } } };
            void Save() => File.WriteAllBytes(Path.Combine(metadata, "update-manifest.json"), UpdateManifestPolicy.Serialize(manifest));
            var prepared = new PreparedUpdate("2.0.0", app, metadata, UpdateDownloadChoice.Full, false);
            void Reject()
            {
                try { UpdateInstallerHandoff.Validate(prepared); }
                catch (InvalidDataException) { return; }
                throw new Exception("Unsafe installer handoff accepted.");
            }
            Save(); UpdateInstallerHandoff.Validate(prepared);
            File.WriteAllText(Path.Combine(app, "extra.txt"), "extra"); Reject(); File.Delete(Path.Combine(app, "extra.txt"));
            File.AppendAllText(Path.Combine(app, "FMSC_SMS.exe"), "damage"); Reject();
            File.WriteAllText(Path.Combine(app, "FMSC_SMS.exe"), "inert fixture");
            manifest.RequiredDatabaseSchema = 124; Save(); Reject();
            // First production baseline contains the complete approved schema;
            // an application-only update must not require retired SQL scripts.
            Directory.Delete(Path.Combine(app, "UpdateSupport", "Migrations"), true);
            File.WriteAllText(Path.Combine(app, "UpdateSupport", "core.ps1"), "$AppVersion = '2.0.0'\n$DatabaseVersion = '1'\n");
            manifest.RequiredDatabaseSchema = 1;
            manifest.Files = Directory.GetFiles(app, "*", SearchOption.AllDirectories).Select(path => new UpdateFile {
                Path = Path.GetRelativePath(app, path).Replace('\\', '/'), Length = new FileInfo(path).Length,
                Sha256 = UpdateChunks.Sha256(path) }).ToList();
            Save(); UpdateInstallerHandoff.Validate(prepared);
            Console.WriteLine("PASS: baseline-1 handoff without obsolete migration scripts.");
            Console.WriteLine("PASS: prepared handoff integrity, extra-file, damaged-file and installer-schema checks (no activation).");
        }
        finally { Directory.Delete(root, true); }
    }
}
