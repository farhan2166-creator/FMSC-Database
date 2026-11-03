using System.Data;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Students.Certificates;

internal static partial class Program
{
    private static void CheckStudentCertificatesWorkspace()
    {
        var kinds = StudentCertificateRegisterReader.Kinds;
        Check(kinds.Length == 8 && kinds.Select(x => x.Key).Distinct().Count() == 8,
            "Student workspace includes the eight distinct existing student certificate types.");
        Check(kinds.Count(x => x.Leaving) == 2 && kinds.Count(x => !x.Leaving) == 6,
            "School and college leaving retain their distinct access class.");

        bool General(string permission) => permission is "Students.View" or "Certificates.Create";
        bool Leaving(string permission) => permission is "Students.View" or "Students.Leave";
        bool WithoutStudent(string permission) => permission == "Certificates.Create";
        Check(kinds.Count(x => StudentCertificateRegisterReader.Allowed(x, General)) == 6,
            "General certificate staff do not see leaving types.");
        Check(kinds.Count(x => StudentCertificateRegisterReader.Allowed(x, Leaving)) == 2,
            "Leaving-only staff do not see ordinary certificate types.");
        Check(kinds.All(x => !StudentCertificateRegisterReader.Allowed(x, WithoutStudent)),
            "Certificate access alone does not bypass Students.View.");

        foreach (Type page in new[]
        {
            typeof(RegularStudentCertificatePage), typeof(CharacterCertificatePage),
            typeof(LeaveCertificatePage), typeof(CollegeLeaveCertificatePage),
            typeof(DateOfBirthCertificatePage), typeof(SportsCertificatePage),
            typeof(NoObjectionCertificatePage), typeof(ProvisionalCertificatePage)
        })
            Check(typeof(IStudentCertificateWorkspaceEditor).IsAssignableFrom(page),
                page.Name + " reuses its own student/copy selection via the workspace bridge.");

        var kind = kinds.Single(x => x.Key == "Character");
        using var source = new DataTable();
        source.Columns.Add("AdmissionId", typeof(int));
        source.Columns.Add("CertificateRecordId", typeof(int));
        source.Columns.Add("CharacterCertificateIssueId", typeof(int));
        source.Columns.Add("CertificateNumber");
        source.Columns.Add("CertificateDate", typeof(DateTime));
        source.Columns.Add("IssuedAt", typeof(DateTime));
        source.Columns.Add("CollectionStatus");
        source.Columns.Add("CollectedAt", typeof(DateTime));
        source.Rows.Add(41, 117, 901, "CHAR-117", new DateTime(2026, 10, 12),
            new DateTime(2026, 10, 12, 9, 30, 0), "Collected", new DateTime(2026, 10, 13));
        source.Rows.Add(41, 118, DBNull.Value, "CHAR-118", new DateTime(2026, 11, 1),
            DBNull.Value, DBNull.Value, DBNull.Value);
        var projected = StudentCertificateRegisterReader.CreateTable();
        StudentCertificateRegisterReader.AppendRecords(projected, kind, source, 41);
        Check(projected.Rows.Count == 2, "Projection retains both a logged issue and an older saved-only row.");
        Check(Convert.ToInt32(projected.Rows[0]["RecordId"]) == 117
            && Convert.ToInt32(projected.Rows[0]["IssueId"]) == 901
            && Convert.ToString(projected.Rows[0]["Collection"]) == "Collected",
            "Exact record and issue identities and actual collection state are retained.");
        Check(Convert.ToInt32(projected.Rows[1]["RecordId"]) == 118
            && Convert.ToInt32(projected.Rows[1]["IssueId"]) == 0
            && Convert.ToString(projected.Rows[1]["Entry"]) == "Saved record only",
            "Legacy saved record without issue is never presented as an issued copy.");

        source.Rows[1]["AdmissionId"] = 42;
        bool refusedMixedStudents = false;
        try
        {
            StudentCertificateRegisterReader.AppendRecords(
                StudentCertificateRegisterReader.CreateTable(), kind, source, 41);
        }
        catch (InvalidOperationException) { refusedMixedStudents = true; }
        Check(refusedMixedStudents, "Reader fails closed if a different student's record appears.");

        var leaving = kinds.Single(x => x.Key == "Leave");
        using var leaveSource = new DataTable();
        leaveSource.Columns.Add("AdmissionId", typeof(int));
        leaveSource.Columns.Add("LeaveRecordId", typeof(int));
        leaveSource.Columns.Add("LeaveCertificateIssueId", typeof(int));
        leaveSource.Columns.Add("CertificateType");
        leaveSource.Rows.Add(41, 123, 401, "College");
        bool refusedWrongLeaveType = false;
        try
        {
            StudentCertificateRegisterReader.AppendRecords(
                StudentCertificateRegisterReader.CreateTable(), leaving, leaveSource, 41);
        }
        catch (InvalidOperationException) { refusedWrongLeaveType = true; }
        Check(refusedWrongLeaveType, "School Leave never displays an unrelated College Leaving episode.");

        // A saved, edited or reissued copy should remain selected when the
        // workspace re-reads its register. New issue selects the latest copy
        // of that certificate type; it never selects another student's type.
        using var copies = StudentCertificateRegisterReader.CreateTable();
        copies.Rows.Add("Regular", "Regular Student", "01 Nov 2026", "REG-11",
            "Issue logged", "Not Collected", "—", "01 Nov 2026 09:00",
            11, 301, new DateTime(2026,11,1,9,0,0));
        copies.Rows.Add("Character", "Character", "02 Nov 2026", "CHAR-19",
            "Issue logged", "Collected", "03 Nov 2026", "02 Nov 2026 09:00",
            19, 501, new DateTime(2026,11,2,9,0,0));
        copies.Rows.Add("Character", "Character", "03 Nov 2026", "CHAR-19",
            "Issue logged", "Not Collected", "—", "03 Nov 2026 09:00",
            19, 502, new DateTime(2026,11,3,9,0,0));
        copies.DefaultView.Sort = "RecordedAt DESC, RecordId DESC, IssueId DESC";
        var exact = CertificateWorkspaceSelection.Resolve(copies.DefaultView,
            new CertificateWorkspaceSelection.Key("Character", 19, 501));
        Check(exact is not null && Convert.ToInt32(exact["IssueId"]) == 501,
            "Refresh preserves the exact older selected copy, not merely the certificate reference.");
        Check(CertificateWorkspaceSelection.FromRow(exact) ==
            new CertificateWorkspaceSelection.Key("Character", 19, 501),
            "A selection is represented by its type and exact saved issue ID.");
        var reissued = CertificateWorkspaceSelection.Resolve(copies.DefaultView,
            new CertificateWorkspaceSelection.Key("Character", 19, 501, PreferNewest:true));
        Check(reissued is not null && Convert.ToInt32(reissued["IssueId"]) == 502,
            "Issue Again returns focus to the newest saved copy of the same certificate type.");
        var newlyCreated = CertificateWorkspaceSelection.Resolve(copies.DefaultView,
            new CertificateWorkspaceSelection.Key("Regular", 0, 0, PreferNewest:true));
        Check(newlyCreated is not null && Convert.ToString(newlyCreated["Kind"]) == "Regular",
            "Newly created certificates focus their own type.");
        Check(CertificateWorkspaceSelection.Resolve(copies.DefaultView, null) is { } latest &&
            Convert.ToInt32(latest["IssueId"]) == 502,
            "Selecting a student initially shows their latest saved certificate.");
        var fallback = CertificateWorkspaceSelection.Resolve(copies.DefaultView,
            new CertificateWorkspaceSelection.Key("Character", 999, 999));
        Check(fallback is not null && Convert.ToInt32(fallback["IssueId"]) == 502,
            "If a historical copy disappears, fallback stays inside the same certificate type.");
    }
}
