using System.Data;
using FMSC_SMS.Core;

namespace FMSC_SMS.Pages.Students.Certificates;

/// <summary>
/// Read-only cross-type projection of existing certificate records for one student.
/// It does not provide alternate certificate writers, printing or collection.
/// </summary>
internal static class StudentCertificateRegisterReader
{
    internal sealed record Kind(string Key, string Caption, bool Leaving, string Reader,
        string RecordKey, string? IssueKey = null, string? LeaveType = null, bool NeedsUserId = false);

    internal static readonly Kind[] Kinds =
    {
        new("Regular", "Regular Student", false, "GetRegularStudentCertificateRecords",
            "RegularStudentCertificateId", "RegularStudentCertificateIssueId"),
        new("Character", "Character", false, "GetCharacterCertificateRecords",
            "CertificateRecordId", "CharacterCertificateIssueId"),
        new("Leave", "Leave", true, "GetLeaveCertificateRecords",
            "LeaveRecordId", "LeaveCertificateIssueId", "School"),
        new("College", "College Leaving", true, "GetLeaveCertificateRecords",
            "LeaveRecordId", "LeaveCertificateIssueId", "College"),
        new("DOB", "Date of Birth", false, "GetDateOfBirthCertificateRecords",
            "CertificateRecordId", "DateOfBirthCertificateIssueId"),
        new("Sports", "Sports", false, "GetSportsCertificateRecords", "SportsCertificateId"),
        new("NOC", "No Objection", false, "GetNoObjectionCertificateRecords", "NoObjectionCertificateId"),
        new("Provisional", "Provisional", false, "GetProvisionalCertificateRecords",
            "ProvisionalCertificateId", NeedsUserId: true)
    };

    internal static bool Allowed(Kind kind, Func<string, bool> permission) =>
        CertificateWorkspaceAccess.CanOpenCard(kind.Leaving, permission);

    private static int Number(DataRow row, string field)
    {
        if (!row.Table.Columns.Contains(field) || row.IsNull(field)) return 0;
        return Convert.ToInt32(row[field]);
    }

    private static string Text(DataRow row, string field)
    {
        if (!row.Table.Columns.Contains(field) || row.IsNull(field)) return "";
        return Convert.ToString(row[field])?.Trim() ?? "";
    }

    private static DateTime? Date(DataRow row, params string[] fields)
    {
        foreach (string field in fields)
            if (row.Table.Columns.Contains(field) && !row.IsNull(field))
                return Convert.ToDateTime(row[field]);
        return null;
    }

    internal static DataTable CreateTable()
    {
        var table = new DataTable();
        table.Columns.Add("Kind");
        table.Columns.Add("Certificate Type");
        table.Columns.Add("Certificate Date");
        table.Columns.Add("Reference");
        table.Columns.Add("Entry");
        table.Columns.Add("Collection");
        table.Columns.Add("Collected On");
        table.Columns.Add("Recorded On");
        table.Columns.Add("RecordId", typeof(int));
        table.Columns.Add("IssueId", typeof(int));
        table.Columns.Add("RecordedAt", typeof(DateTime));
        return table;
    }

    /// <summary>Testable, SQL-free projection; the reader is responsible for calling an authorized procedure.</summary>
    internal static void AppendRecords(DataTable result, Kind kind, DataTable source, int admissionId)
    {
        if (!source.Columns.Contains(kind.RecordKey))
            throw new InvalidOperationException(kind.Reader + " did not return " + kind.RecordKey + ".");
        if (kind.IssueKey is not null && !source.Columns.Contains(kind.IssueKey))
            throw new InvalidOperationException(kind.Reader + " did not return " + kind.IssueKey + ".");
        if (!source.Columns.Contains("AdmissionId"))
            throw new InvalidOperationException(kind.Reader + " did not identify the student.");

        foreach (DataRow row in source.Rows)
        {
            if (Number(row, "AdmissionId") != admissionId)
                throw new InvalidOperationException("A certificate reader returned another student's record.");
            if (kind.LeaveType is not null && source.Columns.Contains("CertificateType") &&
                !string.Equals(Text(row, "CertificateType"), kind.LeaveType, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("A leaving certificate reader returned an unexpected certificate type.");

            int recordId = Number(row, kind.RecordKey);
            if (recordId <= 0) continue;
            int issueId = kind.IssueKey is null ? 0 : Number(row, kind.IssueKey);
            bool savedOnly = kind.IssueKey is not null && issueId <= 0;
            DateTime? certificateDate = Date(row, "CertificateDate", "LeaveDate");
            DateTime? activity = Date(row, "IssuedAt", "CreatedAt", "CertificateDate", "LeaveDate");
            DateTime? collected = Date(row, "CollectedAt");
            string collection = savedOnly ? "Not tracked" : Text(row, "CollectionStatus");
            if (string.IsNullOrWhiteSpace(collection))
                collection = collected.HasValue ? "Collected" : "Not Collected";

            result.Rows.Add(
                kind.Key,
                kind.Caption,
                certificateDate?.ToString("dd MMM yyyy") ?? "—",
                Text(row, "CertificateNumber") is { Length: > 0 } certificateNumber
                    ? certificateNumber : "Record #" + recordId,
                savedOnly ? "Saved record only" : "Issue logged",
                collection,
                collected?.ToString("dd MMM yyyy") ?? "—",
                activity?.ToString("dd MMM yyyy HH:mm") ?? "—",
                recordId,
                issueId,
                activity ?? DateTime.MinValue);
        }
    }

    /// <summary>
    /// The underlying procedures already enforce certificate read permissions.
    /// An optional report permission is intentionally NOT required for this workspace.
    /// All data must be for the requested student: a mismatched admission ID is a hard error.
    /// </summary>
    internal static DataTable Read(int admissionId, bool canGeneral, bool canLeaving, int userId)
    {
        if (admissionId <= 0) throw new ArgumentOutOfRangeException(nameof(admissionId));
        if (!canGeneral && !canLeaving) throw new UnauthorizedAccessException("Certificate access is required.");
        var result = CreateTable();
        foreach (var kind in Kinds)
        {
            if (kind.Leaving ? !canLeaving : !canGeneral) continue;

            var parameters = new List<Microsoft.Data.SqlClient.SqlParameter>
            {
                DatabaseHelper.Int("@AdmissionId", admissionId)
            };
            if (kind.LeaveType is not null)
                parameters.Add(DatabaseHelper.P("@CertificateType", kind.LeaveType));
            if (kind.NeedsUserId)
                parameters.Add(DatabaseHelper.Int("@UserId", userId));

            using var source = DatabaseHelper.QueryUser(kind.Reader, parameters.ToArray());
            AppendRecords(result, kind, source, admissionId);
        }
        result.DefaultView.Sort = "RecordedAt DESC, [Certificate Type] ASC, RecordId DESC, IssueId DESC";
        return result;
    }
}
