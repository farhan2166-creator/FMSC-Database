using FMSC_SMS.Core;

internal static class RegistrationPhotoChecks
{
    internal static async Task<int> RunAsync()
    {
        int checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
        void Reject(Action action)
        {
            try { action(); throw new Exception("Invalid registration data was accepted"); }
            catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException or InvalidOperationException or FormatException) { checks++; }
        }
        using var details = new DataTable();
        foreach (var (name, type) in new[] { ("AdmissionId", typeof(int)), ("AdmissionDate", typeof(DateTime)),
            ("AcademicYearId", typeof(int)), ("ClassSectionId", typeof(int)), ("Status", typeof(string)),
            ("StudentName", typeof(string)), ("FatherName", typeof(string)), ("ContactNumber", typeof(string)),
            ("Address", typeof(string)), ("QuickReference", typeof(string)), ("AdmissionNumber", typeof(string)), ("RegistrationNumber", typeof(string)), ("FamilyMemberId", typeof(int)), ("FamilyMemberCount", typeof(int)) })
            details.Columns.Add(name, type);
        details.Rows.Add(41, new DateTime(2026, 10, 13), 1, 2, "Pending", "Ali", "Parent", "03001234567", "Address", "QR-41", DBNull.Value, DBNull.Value, 0, 0);
        using var fees = new DataTable();
        foreach (var (name, type) in new[] { ("StudentFeeAssignmentId", typeof(int)), ("HasPayments", typeof(bool)), ("FeeTypeId", typeof(int)), ("FeeTypeName", typeof(string)),
            ("IsMandatory", typeof(bool)), ("IsTransportLinked", typeof(bool)), ("Amount", typeof(decimal)),
            ("Frequency", typeof(string)), ("Include", typeof(bool)), ("Discount", typeof(decimal)) }) fees.Columns.Add(name, type);
        fees.Rows.Add(77, false, 1, "Tuition", true, false, 1000m, "Monthly", true, 100m);
        var view = QuickRegistrationView.Read(41, details, fees, "snapshot")!;
        Check(view.Id == 41 && view.Student == "Ali" && view.FamilyReference == "QR-41", "Prepared views retain authoritative identity and reference");
        Check((decimal)view.Fees.Rows[0]["NetAmount"] == 900m && (decimal)view.Fees.Rows[0]["OriginalDiscount"] == 100m,
            "The complete bill is prepared before binding");
        Check(!fees.Columns.Contains("NetAmount") && !fees.Columns.Contains("OriginalDiscount"), "Preparation never changes the source/current bill");
        using (var quote = fees.Copy())
        {
            quote.Columns.Remove("StudentFeeAssignmentId"); quote.Columns.Remove("HasPayments");
            Check(!(bool)QuickRegistrationView.PrepareFees(quote).Rows[0]["HasPayments"], "Absent payment flags default to false for new quotes");
            Reject(() => QuickRegistrationView.Read(41, details, quote, "snapshot"));
        }
        using var empty = details.Clone();
        Check(QuickRegistrationView.Read(41, empty, fees, "snapshot") is null, "A missing record is distinct from a loaded record");
        Reject(() => QuickRegistrationView.Read(42, details, fees, "snapshot"));
        Reject(() => QuickRegistrationView.Read(41, details, fees, ""));
        foreach (string field in new[] { "AdmissionId", "AdmissionDate", "Status", "ClassSectionId", "StudentName", "FamilyMemberId", "FamilyMemberCount" })
        {
            using var broken = details.Copy(); broken.Columns.Remove(field);
            Reject(() => QuickRegistrationView.Read(41, broken, fees, "snapshot"));
        }
        foreach (var (field, value) in new (string, object)[] { ("AdmissionId", 0), ("AdmissionDate", DBNull.Value),
            ("Status", ""), ("AcademicYearId", 0), ("ClassSectionId", -1) })
        {
            using var broken = details.Copy(); broken.Rows[0][field] = value;
            Reject(() => QuickRegistrationView.Read(41, broken, fees, "snapshot"));
        }
        using (var duplicate = details.Copy())
        { duplicate.ImportRow(details.Rows[0]); Reject(() => QuickRegistrationView.Read(41, duplicate, fees, "snapshot")); }
        foreach(var (member,count) in new (object,object)[]{(DBNull.Value,0),(0,DBNull.Value),(-1,2),(1,-1),(0,2),(1,0)})
        {
            using var broken=details.Copy();broken.Rows[0]["FamilyMemberId"]=member;broken.Rows[0]["FamilyMemberCount"]=count;
            Reject(()=>QuickRegistrationView.Read(41,broken,fees,"snapshot"));
        }
        using(var linked=details.Copy())
        {
            linked.Rows[0]["FamilyMemberId"]=81;linked.Rows[0]["FamilyMemberCount"]=3;
            var familyView=QuickRegistrationView.Read(41,linked,fees,"snapshot")!;
            Check(familyView.FamilyMemberId==81&&familyView.FamilyMemberCount==3,"Saved views retain verified family identity and child count");
        }
        foreach (var (field, value) in new (string, object)[] { ("Amount", 1000.5m), ("Discount", -1m), ("Discount", 1001m), ("Include", DBNull.Value) })
        {
            using var broken = fees.Copy(); broken.Rows[0][field] = value;
            Reject(() => QuickRegistrationView.Read(41, details, broken, "snapshot"));
            Check(!broken.Columns.Contains("NetAmount"), "Even a failed preparation leaves its input unchanged");
        }
        using (var broken = fees.Copy())
        { broken.Columns.Remove("Frequency"); Reject(() => QuickRegistrationView.Read(41, details, broken, "snapshot")); }

        var draft = new StudentPhotoDraft();
        byte[] data = new byte[StudentPhotoInput.MaximumStoredBytes];
        int writes = 0, storedId = 0; byte[]? storedData = null;
        Func<int, byte[], string?, string?, Task> write = (id, bytes, name, type) => {
            writes++; storedId = id; storedData = bytes;
            Check(name == "photo.jpg" && type == "image/jpeg", "Every photo retry retains JPEG metadata");
            return writes == 1 ? Task.FromException(new IOException("Fixture write failure")) : Task.CompletedTask;
        };
        draft.Stage(data, "photo.jpg");
        Check(writes == 0 && draft.Current is not null, "Choosing/preparing a photo does not persist it");
        var failed = await draft.SaveAsync(41, write, () => true);
        Check(failed.Outcome == PhotoSaveOutcome.Failed && draft.Current?.SaveFailed == true && ReferenceEquals(draft.Current.Data, data),
            "A failed photo save retains the exact prepared bytes for retry");
        Check(!draft.IsSaving && writes == 1, "A failed write releases busy state without an automatic retry");
        var saved = await draft.SaveAsync(41, write, () => true);
        Check(saved.Outcome == PhotoSaveOutcome.Saved && draft.Current is null && storedId == 41 && ReferenceEquals(storedData, data),
            "A deliberate retry stores the captured record and data without reopening a file");
        Check((await draft.SaveAsync(41, write, () => true)).Outcome == PhotoSaveOutcome.NoPendingPhoto && writes == 2,
            "A clean photo save does not issue a redundant write");
        draft.Stage(data, "photo.jpg"); draft.Discard();
        Check(draft.Current is null && writes == 2, "Discarding a selection never changes the stored photo");
        draft.Stage(data, "photo.jpg");
        Reject(() => draft.Stage(new byte[StudentPhotoInput.MaximumStoredBytes + 1], "oversize.jpg"));
        Check(ReferenceEquals(draft.Current!.Data, data), "Invalid staging preserves the previous valid selection");
        try { await draft.SaveAsync(0, write, () => true); throw new Exception("A photo was saved before its student exists"); }
        catch (ArgumentOutOfRangeException) { checks++; }
        Check(writes == 2 && draft.Current is not null, "A new student's photo waits for an admission ID");
        Check((await draft.SaveAsync(41, write, () => false)).Outcome == PhotoSaveOutcome.Superseded && writes == 2,
            "An obsolete record cannot start a photo write");

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int delayedWrites = 0;
        var pending = draft.SaveAsync(41, (_, bytes, _, _) => { delayedWrites++; Check(ReferenceEquals(bytes, data), "A delayed write captures its original data"); return gate.Task; }, () => true);
        Check((await draft.SaveAsync(41, write, () => true)).Outcome == PhotoSaveOutcome.Busy && writes == 2,
            "Double-clicking Save cannot start a second photo write");
        byte[] newer = { 1, 2, 3 }; draft.Stage(newer, "newer.jpg"); gate.SetResult();
        Check((await pending).Outcome == PhotoSaveOutcome.Superseded && ReferenceEquals(draft.Current!.Data, newer) && delayedWrites == 1,
            "An old completion cannot erase a newer selection");
        bool current = true;
        var lastGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var leaving = draft.SaveAsync(42, (_, _, _, _) => lastGate.Task, () => current);
        current = false; lastGate.SetResult();
        Check((await leaving).Outcome == PhotoSaveOutcome.Superseded && draft.Current is not null && !draft.IsSaving,
            "Leaving a record suppresses late cleanup and releases the save slot");
        return checks;
    }
}
