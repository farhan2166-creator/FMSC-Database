using System.Text;
using System.Text.Json;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Finance.Collection;

internal static class DurabilityChecks
{
    internal static async Task<int> RunAsync()
    {
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            checks++;
        }
        void Refused(Action action, string message)
        {
            try { action(); }
            catch (CollectionRecoveryException) { checks++; return; }
            throw new Exception(message);
        }
        string folder = Path.Combine(Path.GetTempPath(), "FMSC-DurabilityChecks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string settings = Path.Combine(folder, "settings.txt");
            AtomicTextFile.Write(settings, "original setting");
            Check(File.ReadAllText(settings) == "original setting", "Initial settings are published");
            try
            {
                AtomicTextFile.Write(settings, "invalid\uD800", new UTF8Encoding(false, true));
                throw new Exception("Expected encoding failure");
            }
            catch (EncoderFallbackException) { checks++; }
            Check(File.ReadAllText(settings) == "original setting", "A failed settings write must preserve the original copy");
            string[] versions = Enumerable.Range(0, 24).Select(i => JsonSerializer.Serialize(new { Version = i, Value = new string((char)('A' + i), 4096) })).ToArray();
            await Task.WhenAll(versions.Select(value => Task.Run(() => AtomicTextFile.Write(settings, value))));
            Check(versions.Contains(File.ReadAllText(settings)), "Concurrent settings writers must publish a complete version");
            Check(!Directory.EnumerateFiles(folder, "*.tmp").Any(), "Temporary settings files must be cleaned up");

            if(OperatingSystem.IsWindows())
            {
                File.WriteAllText(settings,"read-only original");File.SetAttributes(settings,FileAttributes.ReadOnly);
                try
                {
                    try{AtomicTextFile.Write(settings,"must not replace");throw new Exception("Expected read-only refusal");}
                    catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){checks++;}
                    Check(File.ReadAllText(settings)=="read-only original","Serializing settings writers must still preserve a genuinely unwritable destination");
                    Check(!Directory.EnumerateFiles(folder,"*.tmp").Any(),"Refused Windows replacement cleans the staged file");
                }
                finally{File.SetAttributes(settings,FileAttributes.Normal);}
            }

            string photo = Path.Combine(folder, "photo.bin");
            byte[] original = { 1, 2, 3, 4, 5 };
            await File.WriteAllBytesAsync(photo, original);
            Check((await StudentPhotoInput.ReadAsync(photo)).SequenceEqual(original), "Photo input must preserve exact bytes");
            using (var file = new FileStream(photo, FileMode.Create)) file.SetLength(StudentPhotoInput.MaximumBytes);
            Check((await StudentPhotoInput.ReadAsync(photo)).Length == StudentPhotoInput.MaximumBytes, "Exactly 25 MB source input remains supported before resizing");
            foreach (long size in new long[] { StudentPhotoInput.MaximumBytes + 1L, 200_000_000 })
            {
                using (var file = new FileStream(photo, FileMode.Create)) file.SetLength(size);
                try { await StudentPhotoInput.ReadAsync(photo); throw new Exception("Oversized input was accepted"); }
                catch (StudentPhotoInputException) { checks++; }
            }
            await File.WriteAllBytesAsync(photo, original);
            foreach (var (width, height, expected) in new[] {
                (4000, 3000, new System.Drawing.Size(600, 450)),
                (3000, 4000, new System.Drawing.Size(450, 600)),
                (1200, 2000, new System.Drawing.Size(360, 600)),
                (120, 90, new System.Drawing.Size(120, 90)),
                (600, 600, new System.Drawing.Size(600, 600)),
                (1001, 2000, new System.Drawing.Size(301, 600)),
                (5000, 1, new System.Drawing.Size(600, 1))
            }) Check(StudentPhotoInput.FittedSize(width, height) == expected, "Photo fitting preserves the whole image's proportions without upscaling");
            StudentPhotoInput.ValidateStoredData(new byte[StudentPhotoInput.MaximumStoredBytes]);
            checks++;
            foreach (int size in new[] { 0, StudentPhotoInput.MaximumStoredBytes + 1 })
            {
                try { StudentPhotoInput.ValidateStoredData(new byte[size]); throw new Exception("Invalid stored photo size was accepted"); }
                catch (StudentPhotoInputException) { checks++; }
            }
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                try { await StudentPhotoInput.ReadAsync(photo, cancellation.Token); throw new Exception("Expected photo cancellation"); }
                catch (OperationCanceledException) { checks++; }
            }

            string journal = Path.Combine(folder, "pending.json");
            using (var intent = CollectionRecoveryJournal.Begin("Student 123; Rs. 1000", journal))
                Refused(() => CollectionRecoveryJournal.Begin("Second payment", journal), "An active payment must exclude a second collection");
            var pending = CollectionRecoveryJournal.ReadPending(journal)!;
            Check(pending.Summary.Contains("Student 123") && pending.Id != Guid.Empty && pending.StartedUtc.Kind == DateTimeKind.Utc, "An interrupted payment retains a usable review record");
            Refused(() => CollectionRecoveryJournal.ClearReviewed(Guid.NewGuid(), journal), "Wrong recovery IDs must not clear a retained payment");
            Check(File.Exists(journal), "A refused review preserves the payment journal");
            CollectionRecoveryJournal.ClearReviewed(pending.Id, journal);
            Check(!File.Exists(journal), "An explicit matching review clears the retained intent");
            foreach (string damaged in new[] { "{", "{}", "null", JsonSerializer.Serialize(new CollectionRecoveryJournal.Pending(Guid.Empty, "payment", DateTime.UtcNow)), JsonSerializer.Serialize(new CollectionRecoveryJournal.Pending(Guid.NewGuid(), "", DateTime.UtcNow)), JsonSerializer.Serialize(new CollectionRecoveryJournal.Pending(Guid.NewGuid(), "payment", default)) })
            {
                File.WriteAllText(journal, damaged);
                Refused(() => CollectionRecoveryJournal.ReadPending(journal), "Damaged or incomplete journals must require administrator review");
                Check(File.ReadAllText(journal) == damaged, "Invalid journals must not be automatically discarded");
            }
            File.Delete(journal);
            using (var confirmed = CollectionRecoveryJournal.Begin("Confirmed payment", journal))
            {
                confirmed.Complete();
                using (var newer = CollectionRecoveryJournal.Begin("New unresolved payment", journal)) { }
                confirmed.Complete();
                Check(CollectionRecoveryJournal.ReadPending(journal)!.Summary == "New unresolved payment", "Repeated cleanup must not clear a newer payment");
            }
            var latest = CollectionRecoveryJournal.ReadPending(journal)!;
            CollectionRecoveryJournal.ClearReviewed(latest.Id, journal);
            using (var old = CollectionRecoveryJournal.Begin("Old payment", journal))
            {
                old.Dispose();
                CollectionRecoveryJournal.ClearReviewed(CollectionRecoveryJournal.ReadPending(journal)!.Id, journal);
                using (var newer = CollectionRecoveryJournal.Begin("Replacement intent", journal)) { }
                Refused(old.Complete, "A stale owner must not clear a replacement journal");
                Check(CollectionRecoveryJournal.ReadPending(journal)!.Summary == "Replacement intent", "Replacement intents survive stale cleanup");
            }
        }
        finally { Directory.Delete(folder, recursive: true); }
        return checks;
    }
}
