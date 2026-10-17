namespace FMSC_SMS.Core;

internal sealed record AdmissionSubmission(string Number, string Student, string Parent, DateTime Date,
    int CampusId, bool Historical, int DiscountCount);
internal sealed record ConfirmedAdmission(int Id, string RegistrationNumber);
internal sealed record AdmissionConfirmation(ConfirmedAdmission? Admission, bool Reconciled,
    Exception? WriteError = null, Exception? ReadError = null);

/// <summary>A submitted admission is never retried automatically, including after a lost reply.</summary>
internal static class AdmissionSaveConfirmation
{
    internal static ConfirmedAdmission? ReadResponse(DataSet response, string number)
    {
        // Fee procedures may emit earlier tables; select the documented admission response.
        var tables = response.Tables.Cast<DataTable>().Where(table =>
            table.Columns.Contains("AdmissionId") && table.Columns.Contains("AdmissionNumber") &&
            table.Columns.Contains("RegistrationNumber") && table.Columns.Contains("EnrollmentId")).ToArray();
        return tables.Length == 1 ? ReadIdentity(tables[0], number) : null;
    }

    private static ConfirmedAdmission? ReadIdentity(DataTable table, string number)
    {
        if (table.Rows.Count != 1 || !table.Columns.Contains("AdmissionId") ||
            !table.Columns.Contains("AdmissionNumber") || !table.Columns.Contains("RegistrationNumber")) return null;
        var row = table.Rows[0];
        if (row["AdmissionId"] is not int id || id <= 0 || row["AdmissionNumber"] is not string savedNumber ||
            !string.Equals(savedNumber, number, StringComparison.OrdinalIgnoreCase)) return null;
        if (row["RegistrationNumber"] is not DBNull && row["RegistrationNumber"] is not string) return null;
        // A historical record without a verified first placement can have no registration number.
        return new(id, row["RegistrationNumber"] as string ?? "");
    }

    internal static ConfirmedAdmission? ReadReconciled(DataTable table, AdmissionSubmission submitted)
    {
        var identity = ReadIdentity(table, submitted.Number);
        if (identity is null) return null;
        foreach (string column in new[] { "StudentName", "FatherName", "AdmissionDate", "AdmissionCampusId" })
            if (!table.Columns.Contains(column)) return null;
        var row = table.Rows[0];
        return row["AdmissionDate"] is DateTime date && date.Date == submitted.Date.Date &&
            row["AdmissionCampusId"] is int campus && campus == submitted.CampusId &&
            row["StudentName"] is string student && string.Equals(student.Trim(), submitted.Student, StringComparison.Ordinal) &&
            row["FatherName"] is string parent && string.Equals(parent.Trim(), submitted.Parent, StringComparison.Ordinal)
            ? identity : null;
    }

    internal static async Task<AdmissionConfirmation> SubmitAsync(AdmissionSubmission submitted,
        Func<Task<DataSet>> write, Func<AdmissionSubmission, Task<DataTable>> read)
    {
        Exception? writeError = null;
        try
        {
            using var response = await write();
            if (ReadResponse(response, submitted.Number) is { } confirmed) return new(confirmed, false);
        }
        catch (Exception ex) { writeError = ex; }
        // A malformed response or exception can follow a server commit. Read; never repeat the write.
        return await RecheckAsync(submitted, read, writeError);
    }

    internal static async Task<AdmissionConfirmation> RecheckAsync(AdmissionSubmission submitted,
        Func<AdmissionSubmission, Task<DataTable>> read, Exception? writeError = null)
    {
        try
        {
            using var table = await read(submitted);
            return new(ReadReconciled(table, submitted), true, writeError);
        }
        catch (Exception ex) { return new(null, true, writeError, ex); }
    }
}
