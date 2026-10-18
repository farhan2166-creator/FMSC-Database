using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using FMSC_SMS.Core;

internal static class FilePersistenceChecks
{
    private sealed class ChangedFile(byte[] data, long reportedLength) : MemoryStream(data)
    { public override long Length => reportedLength; }

    internal static async Task<int> RunAsync()
    {
        int checks = 0;
        void Check(bool valid, string message) { if (!valid) throw new Exception(message); checks++; }
        void Refused(Action action)
        { try { action(); } catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or EncoderFallbackException or InvalidOperationException) { checks++; return; } throw new Exception("Unsafe operation was accepted"); }
        async Task RejectImage(Stream stream)
        { try { await BoundedImageFile.ReadAsync(stream, 2_000_000); } catch (InvalidDataException) { checks++; return; } throw new Exception("Invalid logo source was accepted"); }
        string folder = Path.Combine(Path.GetTempPath(), "FMSC-FilePersistence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            string image = Path.Combine(folder, "logo.jpg");
            byte[] logo = { 1, 2, 3, 4 }; await File.WriteAllBytesAsync(image, logo);
            Check((await BoundedImageFile.ReadAsync(image, 2_000_000)).SequenceEqual(logo), "Bounded logo reads retain exact source bytes");
            using (var sparse = new FileStream(image, FileMode.Create)) sparse.SetLength(2_000_000);
            Check((await BoundedImageFile.ReadAsync(image, 2_000_000)).Length == 2_000_000, "The exact 2 MB logo boundary is accepted");
            foreach (long length in new[] { 0L, 2_000_001L, 100_000_000L })
            {
                using (var sparse = new FileStream(image, FileMode.Create)) sparse.SetLength(length);
                try { await BoundedImageFile.ReadAsync(image, 2_000_000); throw new Exception("Oversized/empty logo was accepted"); }
                catch (InvalidDataException) { checks++; }
                using var reported = new ChangedFile(logo, length); await RejectImage(reported);
                Check(reported.Position == 0, "Invalid lengths are rejected before reading/allocating source data");
            }
            using (var grown = new ChangedFile(logo, 3)) await RejectImage(grown);
            using (var shrunk = new ChangedFile(logo, 5)) await RejectImage(shrunk);
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel();
                using var stream = new MemoryStream(logo);
                try { await BoundedImageFile.ReadAsync(stream, 2_000_000, cancelled.Token); throw new Exception("Canceled logo read was accepted"); }
                catch (OperationCanceledException) { checks++; }
                Check(stream.Position == 0, "Canceled reads do not read image data");
            }

            string settings = Path.Combine(folder, "user-1.json");
            var store = new ReportSettingsFile(settings);
            Check(store.Read().Views.Count == 0 && !File.Exists(settings), "Missing report settings use defaults without creating a file");
            Check(store.Update(_ => false) is { Succeeded: true, Changed: false } && !File.Exists(settings), "No-op deletion does not claim a changed persisted view");
            bool AddView(ReportUserData data) { data.Views["Students"] = new() { new() { Name = "Class", State = new() { ["Year"] = "2" } } }; return true; }
            Check(store.Update(AddView) is { Succeeded: true, Changed: true }, "A saved view succeeds only after publication");
            Check(new ReportSettingsFile(settings).Read().Views["Students"][0].State["Year"] == "2", "Saved report views survive reopening");
            byte[] before = File.ReadAllBytes(settings);
            int failedWrites = 0;
            var refused = new ReportSettingsFile(settings, publish: (_, _) => { failedWrites++; throw new IOException("Injected storage refusal"); });
            Check(refused.Update(data => { data.Views.Clear(); return true; }) is { Succeeded: false, Changed: false, Error: IOException }, "Failed view deletion cannot claim persisted success");
            Check(File.ReadAllBytes(settings).SequenceEqual(before) && refused.Read().Views.Count == 1, "Failed deletion preserves the saved view on disk and reopening");
            Check(refused.Update(data => { data.Views["Other"] = new(); return true; }) is { Succeeded: false, Changed: false }, "Failed view save cannot claim persisted success");
            Check(failedWrites == 2 && File.ReadAllBytes(settings).SequenceEqual(before), "Storage failure never mutates the original preferences file");
            Check(store.Update(data => data.Views.Remove("Students")) is { Succeeded: true, Changed: true } && store.Read().Views.Count == 0, "Successful view deletion is actually persisted");
            Check(store.Update(data => data.Views.Remove("Students")) is { Succeeded: true, Changed: false }, "Deleting an absent view is reported as unchanged");
            string blocked = Path.Combine(folder, "blocked-settings.json"); Directory.CreateDirectory(blocked);
            int attempts = 0;
            var unreadable = new ReportSettingsFile(blocked, publish: (_, _) => attempts++);
            Check(unreadable.Read().Preferences.Count == 0, "Read errors can use defaults for rendering");
            Check(!unreadable.Update(data => { data.Preferences["scope"] = "new"; return true; }).Succeeded && attempts == 0,
                "Unreadable settings stop writes rather than becoming an empty overwrite");
            using (var held = new FileStream(settings, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Check(!store.Update(data => { data.Preferences["scope"] = "new"; return true; }).Succeeded, "A locked preferences file stops mutation");
            }

            foreach (byte[] corrupt in new[] { Encoding.UTF8.GetBytes("{damaged"), Array.Empty<byte>(), Encoding.UTF8.GetBytes("null"),
                Encoding.UTF8.GetBytes("{\"Preferences\":null}"), Encoding.UTF8.GetBytes("{\"Views\":{\"Students\":[null]}}"), new byte[] { 0xFF, 0xFE, 0x21 } })
            {
                File.WriteAllBytes(settings, corrupt);
                int backupsBefore = Directory.GetFiles(folder, "*.bak").Length;
                Check(store.Read().Views.Count == 0 && File.ReadAllBytes(settings).SequenceEqual(corrupt), "Corrupt reads use defaults without altering the recoverable file");
                Check(Directory.GetFiles(folder, "*.bak").Length == backupsBefore, "Reads do not repeatedly quarantine settings");
                var recovered = store.Update(data => { data.Preferences["scope"] = "recovered"; return true; });
                Check(recovered is { Succeeded: true, Changed: true, RecoveryPath: not null } && File.ReadAllBytes(recovered.RecoveryPath!).SequenceEqual(corrupt),
                    "All damaged bytes are preserved before replacing corrupt settings");
                Check(store.Read().Preferences["scope"] == "recovered", "New settings remain readable after recovery");
                Check(store.Update(data => { data.Preferences["scope"] = "next"; return true; }).RecoveryPath is null &&
                    Directory.GetFiles(folder, "*.bak").Length == backupsBefore + 1, "Valid subsequent writes do not create redundant corruption backups");
            }
            byte[] damaged = Encoding.UTF8.GetBytes("{partial"); File.WriteAllBytes(settings, damaged);
            int publications = 0;
            var noBackup = new ReportSettingsFile(settings, publish: (_, _) => publications++, preserve: (_, _) => throw new IOException("Cannot back up damaged settings"));
            Check(noBackup.Update(AddView) is { Succeeded: false, RecoveryPath: null } && publications == 0 && File.ReadAllBytes(settings).SequenceEqual(damaged),
                "A failed recovery backup prevents replacement and does not invent a recovery path");
            var failedRecovery = new ReportSettingsFile(settings, publish: (_, _) => throw new IOException("Cannot publish replacement"));
            var recoveryFailure = failedRecovery.Update(AddView);
            Check(!recoveryFailure.Succeeded && recoveryFailure.RecoveryPath is not null && File.ReadAllBytes(recoveryFailure.RecoveryPath).SequenceEqual(damaged) &&
                File.ReadAllBytes(settings).SequenceEqual(damaged), "Failure after backup preserves both the complete backup and original corrupt file");
            Check(store.Update(_ => false) is { Succeeded: true, Changed: false } && File.ReadAllBytes(settings).SequenceEqual(damaged), "A no-op cannot overwrite corrupt settings");
            byte[] valid = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new ReportUserData { Preferences = new() { ["scope"] = "BOM" } }));
            File.WriteAllBytes(settings, new byte[] { 0xEF, 0xBB, 0xBF }.Concat(valid).ToArray());
            Check(store.Read().Preferences["scope"] == "BOM" && store.Update(data => { data.Preferences["scope"] = "valid"; return true; }).RecoveryPath is null,
                "Earlier UTF-8 BOM settings remain compatible without quarantine");

            string export = Path.Combine(folder, "existing.csv"); byte[] old = Encoding.UTF8.GetBytes("previous valid export");
            File.WriteAllBytes(export, old);
            Refused(() => AtomicExportFile.Publish(export, temporary => { File.WriteAllText(temporary, "partial"); throw new IOException("Interrupted export"); }));
            Check(File.ReadAllBytes(export).SequenceEqual(old), "Interrupted generation never truncates the previous export");
            Refused(() => AtomicExportFile.Publish(export, temporary => File.WriteAllText(temporary, "invalid"), _ => throw new InvalidDataException("Invalid PDF")));
            Check(File.ReadAllBytes(export).SequenceEqual(old), "Failed format validation leaves the previous export untouched");
            Refused(() => AtomicExportFile.Publish(export, _ => { }));
            Check(File.ReadAllBytes(export).SequenceEqual(old), "Empty exports cannot replace an existing report");
            string missing = Path.Combine(folder, "new.pdf");
            Refused(() => AtomicExportFile.Publish(missing, _ => throw new IOException("Failed first export")));
            Check(!File.Exists(missing), "A failed first export does not publish a partial destination");
            AtomicExportFile.Publish(export, temporary => {
                Check(Path.GetDirectoryName(temporary) == folder && File.ReadAllBytes(export).SequenceEqual(old), "Export builds beside its unchanged destination");
                File.WriteAllText(temporary, "complete new export");
            }, temporary => Check(File.ReadAllText(temporary) == "complete new export" && File.ReadAllBytes(export).SequenceEqual(old), "Validation happens before final replacement"));
            Check(File.ReadAllText(export) == "complete new export", "Successful export publishes the complete new file");
            string directoryTarget = Path.Combine(folder, "not-a-file"); Directory.CreateDirectory(directoryTarget);
            Refused(() => AtomicExportFile.Publish(directoryTarget, temporary => File.WriteAllText(temporary, "new")));
            Check(Directory.Exists(directoryTarget), "A refused replacement leaves its destination intact");
            Check(!Directory.GetFiles(folder, "*.tmp").Any(), "All success/refusal paths clean up their own temporary exports");

            using var csv = new DataTable(); csv.Columns.Add("Student"); csv.Columns.Add("Amount", typeof(decimal));
            csv.Rows.Add("Ali, \"A\"\nNext line", -250m); csv.Rows.Add("=1+1", DBNull.Value);
            CsvFileExporter.Write(csv, export);
            string content = File.ReadAllText(export);
            Check(content.Contains("\"Ali, \"\"A\"\"\nNext line\",\"-250\"") && content.Contains("\"'=1+1\",\"\""),
                "Streaming CSV preserves quotes, multiline cells, signed money, nulls and formula protection");
            Check(File.ReadAllBytes(export).Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }), "CSV retains the spreadsheet-compatible UTF-8 BOM");
            byte[] validCsv = File.ReadAllBytes(export); csv.Rows.Add("invalid\uD800", 1m);
            Refused(() => CsvFileExporter.Write(csv, export));
            Check(File.ReadAllBytes(export).SequenceEqual(validCsv), "An actual CSV encoder failure preserves the prior complete export");
            using var noColumns = new DataTable(); Refused(() => CsvFileExporter.Write(noColumns, export));
            Check(File.ReadAllBytes(export).SequenceEqual(validCsv) && !Directory.GetFiles(folder, "*.tmp").Any(), "Refused CSV exports preserve originals and clean temporary files");
        }
        finally { CultureInfo.CurrentCulture = previousCulture; Directory.Delete(folder, recursive: true); }
        return checks;
    }
}
