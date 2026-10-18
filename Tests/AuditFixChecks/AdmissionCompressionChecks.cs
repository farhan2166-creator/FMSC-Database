using FMSC_SMS.Core;
using System.Data;
using System.Drawing;

internal static class AdmissionCompressionChecks
{
    internal static async Task<int> RunAsync()
    {
        int checks = 0;
        void Check(bool valid, string message) { if (!valid) throw new Exception(message); checks++; }
        var submitted = new AdmissionSubmission("125a", "Ali", "Parent", new DateTime(2026, 10, 13), 2, false, 1);
        DataSet Response(object? id = null, string number = "125a", object? registration = null)
        {
            var data = new DataSet(); var table = data.Tables.Add("AdmissionReply");
            table.Columns.Add("AdmissionId", typeof(object)); table.Columns.Add("EnrollmentId", typeof(object));
            table.Columns.Add("AdmissionNumber", typeof(string)); table.Columns.Add("RegistrationNumber", typeof(object));
            table.Rows.Add(id ?? 41, 77, number, registration ?? "R-41"); return data;
        }
        DataTable Saved()
        {
            var table = new DataTable();
            foreach (var (column, type) in new[] { ("AdmissionId", typeof(int)), ("AdmissionNumber", typeof(string)),
                ("RegistrationNumber", typeof(string)), ("StudentName", typeof(string)), ("FatherName", typeof(string)),
                ("AdmissionDate", typeof(DateTime)), ("AdmissionCampusId", typeof(int)) }) table.Columns.Add(column, type);
            table.Rows.Add(41, "125a", "R-41", "Ali", "Parent", submitted.Date, 2); return table;
        }
        using (var valid = Response())
        {
            Check(AdmissionSaveConfirmation.ReadResponse(valid, submitted.Number)?.Id == 41, "A valid admission ID and matching official number confirm the save");
        }
        foreach (object id in new object[] { 0, -1, DBNull.Value, "41", 41L, 41.5m })
        { using var data = Response(id); Check(AdmissionSaveConfirmation.ReadResponse(data, submitted.Number) is null, "Invalid ID never confirms an admission"); }
        using (var historical = Response(registration: DBNull.Value))
            Check(AdmissionSaveConfirmation.ReadResponse(historical, submitted.Number)?.RegistrationNumber == "", "Historical admissions without placement may have no registration number");
        using (var wrong = Response(number: "126")) Check(AdmissionSaveConfirmation.ReadResponse(wrong, submitted.Number) is null, "A different admission number cannot confirm this submission");
        using (var wrong = Response(registration: 100)) Check(AdmissionSaveConfirmation.ReadResponse(wrong, submitted.Number) is null, "Malformed registration output cannot confirm this submission");
        using (var empty = new DataSet()) Check(AdmissionSaveConfirmation.ReadResponse(empty, submitted.Number) is null, "No result table is not a success");
        using (var empty = Response()) { empty.Tables[0].Rows.Clear(); Check(AdmissionSaveConfirmation.ReadResponse(empty, submitted.Number) is null, "An empty result is not a success"); }
        using (var missing = Response()) { missing.Tables[0].Columns.Remove("AdmissionId"); Check(AdmissionSaveConfirmation.ReadResponse(missing, submitted.Number) is null, "A missing ID column is not a success"); }
        using (var duplicate = Response()) { var other = duplicate.Tables[0].Copy(); other.TableName = "AnotherReply"; duplicate.Tables.Add(other); Check(AdmissionSaveConfirmation.ReadResponse(duplicate, submitted.Number) is null, "Ambiguous response tables are not accepted"); }
        using (var multiple = Response()) { multiple.Tables[0].ImportRow(multiple.Tables[0].Rows[0]); Check(AdmissionSaveConfirmation.ReadResponse(multiple, submitted.Number) is null, "Multiple IDs are not accepted"); }
        using (var nested = new DataSet())
        {
            var fees = nested.Tables.Add(); fees.Columns.Add("ChargeId", typeof(int)); fees.Rows.Add(9);
            using var reply = Response(); nested.Tables.Add(reply.Tables[0].Copy());
            Check(AdmissionSaveConfirmation.ReadResponse(nested, submitted.Number)?.Id == 41, "Earlier nested fee results cannot hide the documented admission response");
        }
        using (var matching = Saved()) Check(AdmissionSaveConfirmation.ReadReconciled(matching, submitted)?.Id == 41, "Exact submitted identity confirms a saved record");
        foreach (var (column, value) in new (string, object)[] { ("AdmissionNumber", "126"), ("StudentName", "Bilal"), ("FatherName", "Other parent"),
            ("AdmissionDate", submitted.Date.AddDays(-1)), ("AdmissionCampusId", 3), ("StudentName", DBNull.Value), ("AdmissionId", 0) })
        { using var mismatch = Saved(); mismatch.Rows[0][column] = value; Check(AdmissionSaveConfirmation.ReadReconciled(mismatch, submitted) is null, "Reconciliation cannot claim an unrelated student's record"); }
        using (var missing = Saved()) { missing.Columns.Remove("FatherName"); Check(AdmissionSaveConfirmation.ReadReconciled(missing, submitted) is null, "Incomplete reconciliation cannot confirm identity"); }
        using (var absent = Saved()) { absent.Rows.Clear(); Check(AdmissionSaveConfirmation.ReadReconciled(absent, submitted) is null, "Not found remains unconfirmed"); }
        using (var multiple = Saved()) { multiple.ImportRow(multiple.Rows[0]); Check(AdmissionSaveConfirmation.ReadReconciled(multiple, submitted) is null, "Multiple records cannot confirm one admission"); }
        int writes = 0, reads = 0;
        Task<DataTable> Read(AdmissionSubmission request) { reads++; Check(request == submitted, "Recovery keeps the original submitted identity"); return Task.FromResult(Saved()); }
        var normal = await AdmissionSaveConfirmation.SubmitAsync(submitted, () => { writes++; return Task.FromResult(Response()); }, Read);
        Check(normal.Admission?.Id == 41 && !normal.Reconciled && writes == 1 && reads == 0, "Normal admission does not add a reconciliation query");
        var recovered = await AdmissionSaveConfirmation.SubmitAsync(submitted, () => { writes++; return Task.FromResult(new DataSet()); }, Read);
        Check(recovered.Admission?.Id == 41 && recovered.Reconciled && writes == 2 && reads == 1, "Missing response is recovered with one read and no repeated write");
        var lostReply = await AdmissionSaveConfirmation.SubmitAsync(submitted, () => { writes++; return Task.FromException<DataSet>(new IOException("Lost reply after commit")); }, Read);
        Check(lostReply.Admission?.Id == 41 && lostReply.WriteError is IOException && writes == 3 && reads == 2, "An exception after commit can be reconciled without resubmission");
        var unconfirmed = await AdmissionSaveConfirmation.SubmitAsync(submitted, () => { writes++; return Task.FromResult(Response(0)); }, _ => {
            reads++; var absent = Saved(); absent.Rows.Clear(); return Task.FromResult(absent); });
        Check(unconfirmed.Admission is null && writes == 4 && reads == 3, "A missing record after an invalid result never claims success or retries the write");
        var unavailable = await AdmissionSaveConfirmation.SubmitAsync(submitted, () => { writes++; return Task.FromException<DataSet>(new IOException("Disconnected")); }, _ => {
            reads++; return Task.FromException<DataTable>(new IOException("Still disconnected")); });
        Check(unavailable.Admission is null && unavailable.WriteError is IOException && unavailable.ReadError is IOException && writes == 5 && reads == 4,
            "A failed write reply plus failed reconciliation preserves uncertainty");
        var rechecked = await AdmissionSaveConfirmation.RecheckAsync(submitted, Read);
        Check(rechecked.Admission?.Id == 41 && writes == 5 && reads == 5, "Deliberate recheck issues a read only");

        Check(StudentPhotoInput.MaximumStoredBytes == 80 * 1024, "New stored photos have an 80 KB maximum");
        int encodes = 0;
        var small = StudentPhotoCompression.Prepare(160, 90, (size, quality) => {
            encodes++; Check(size == new Size(160, 90) && quality == 88, "Small photos stay at their original dimensions and high quality"); return new byte[12_000]; });
        Check(small.Length == 12_000 && encodes == 1, "Already compact photos are not padded to a target minimum or re-encoded unnecessarily");
        encodes = 0;
        var qualityFit = StudentPhotoCompression.Prepare(1200, 2000, (size, quality) => {
            encodes++; Check(size == new Size(360, 600) && quality >= StudentPhotoCompression.MinimumQuality, "Quality attempts retain portrait proportions");
            return new byte[quality == 88 ? 90_000 : 70_000]; });
        Check(qualityFit.Length == 70_000 && encodes == 2, "Moderate compression fits a portrait before lowering resolution");
        var dimensions = new List<Size>(); var qualities = new List<int>();
        var detailed = StudentPhotoCompression.Prepare(2000, 1200, (size, quality) => {
            dimensions.Add(size); qualities.Add(quality);
            return new byte[(int)((long)size.Width * size.Height * (quality == 70 ? 0.8 : 1.2))]; });
        Check(detailed.Length <= StudentPhotoInput.MaximumStoredBytes && dimensions.Any(size => size.Width < 600), "High-detail images reduce resolution to respect the byte cap");
        Check(qualities.All(quality => quality >= 70) && dimensions.All(size => Math.Abs((double)size.Width / size.Height - 2000d / 1200) < 0.01),
            "Compression avoids low JPEG quality and retains aspect ratio at every resolution");
        Check(dimensions.Count <= 20 && dimensions.All(size => size.Width <= 600), "Compression attempts remain bounded and never upscale");
        Check(StudentPhotoInput.FittedSize(3000, 4000, 320) == new Size(240, 320), "Fallback dimensions preserve portrait proportions");
        foreach (int length in new[] { 0, 100_000 })
        {
            int attempts = 0;
            try { StudentPhotoCompression.Prepare(100, 100, (_, _) => { attempts++; return new byte[length]; }); throw new Exception("Invalid encoder output was accepted"); }
            catch (StudentPhotoInputException) { Check(attempts <= 4, "Failed encodes cannot loop forever or silently exceed the cap"); }
        }
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { StudentPhotoCompression.Prepare(600, 400, (_, _) => throw new Exception("Cancelled compression called encoder"), cancelled.Token); throw new Exception("Expected cancellation"); }
        catch (OperationCanceledException) { checks++; }
        using var midCancel = new CancellationTokenSource();
        try { StudentPhotoCompression.Prepare(600, 400, (_, _) => { midCancel.Cancel(); return new byte[100_000]; }, midCancel.Token); throw new Exception("Expected cancellation between attempts"); }
        catch (OperationCanceledException) { checks++; }
        return checks;
    }
}
