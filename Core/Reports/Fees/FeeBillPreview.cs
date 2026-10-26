using FMSC_SMS.Pages.Security;
using Microsoft.Data.SqlClient;

namespace FMSC_SMS.Core;

// Shared read-only billing entry point for registration and the family counter.
internal static class FeeBillPreview
{
    private const string MembersSql = """
        EXEC dbo.RequirePermission @UserId,@Permission;
        DECLARE @Family int=(SELECT FamilyId FROM dbo.FamilyMembers WHERE AdmissionId=@Admission);
        SELECT FM.FamilyId,FM.FamilyMemberId,S.AdmissionId,S.StudentName,S.FatherName,S.Gender,
            S.AdmissionNumber,S.QuickReference,S.RegistrationNumber,S.Status,E.EnrollmentId,E.Status EnrollmentStatus,
            E.BillingReady,E.PlacementConfirmedAt,AY.YearName AcademicYear,C.ClassName,SEC.SectionName,
            CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.StudentPostingHolds H
                WHERE H.AdmissionId=S.AdmissionId AND H.ResumeDate IS NULL) THEN 1 ELSE 0 END) IsOnHold
        FROM dbo.Students S
        LEFT JOIN dbo.FamilyMembers FM ON FM.AdmissionId=S.AdmissionId
        OUTER APPLY(SELECT TOP(1) E1.* FROM dbo.Enrollments E1 WHERE E1.AdmissionId=S.AdmissionId
            ORDER BY CASE WHEN E1.Status=N'Active' THEN 0 ELSE 1 END,E1.EnrollmentId DESC) E
        LEFT JOIN dbo.AcademicYears AY ON AY.AcademicYearId=E.AcademicYearId
        LEFT JOIN dbo.CampusClassSections CCS ON CCS.ClassSectionId=E.ClassSectionId
        LEFT JOIN dbo.Classes C ON C.ClassId=CCS.ClassId
        LEFT JOIN dbo.Sections SEC ON SEC.SectionId=CCS.SectionId
        WHERE S.AdmissionId=@Admission OR (@Family IS NOT NULL AND FM.FamilyId=@Family)
        ORDER BY COALESCE(C.DisplayOrder,-1) DESC,S.StudentName,S.AdmissionId;
        """;

    private const string ChargesSql = """
        EXEC dbo.RequirePermission @UserId,N'Fees.View';
        DECLARE @Family int=(SELECT FamilyId FROM dbo.FamilyMembers WHERE AdmissionId=@Admission);
        IF EXISTS(SELECT 1 FROM OPENJSON(@Ids) J WHERE NOT EXISTS(
            SELECT 1 FROM dbo.Students S LEFT JOIN dbo.FamilyMembers FM ON FM.AdmissionId=S.AdmissionId
            WHERE S.AdmissionId=CONVERT(int,J.[value]) AND
                (S.AdmissionId=@Admission OR (@Family IS NOT NULL AND FM.FamilyId=@Family))))
            THROW 51000,'Family membership changed. Refresh the family and select children again.',1;
        IF @WholeFamily=1 AND EXISTS(SELECT 1 FROM dbo.Students S
            LEFT JOIN dbo.FamilyMembers FM ON FM.AdmissionId=S.AdmissionId
            WHERE (S.AdmissionId=@Admission OR (@Family IS NOT NULL AND FM.FamilyId=@Family))
                AND S.AdmissionId NOT IN(SELECT CONVERT(int,[value]) FROM OPENJSON(@Ids)))
            THROW 51000,'The family has new members. Refresh before printing the whole family bill.',1;
        EXEC dbo.GetFeeVisitCharges @UserId,@Ids;
        """;

    internal static Task<DataTable> ReadMembersAsync(int admissionId) => ReadMembersAsync(admissionId, "Students.View");
    internal static Task<DataTable> ReadMembersAsync(int admissionId, string permission)
    {
        if (admissionId <= 0) throw new ArgumentOutOfRangeException(nameof(admissionId));
        return DatabaseHelper.QuerySqlAsync(MembersSql, DatabaseHelper.Int("@UserId", CurrentUser.UserId),
            DatabaseHelper.Int("@Admission", admissionId), DatabaseHelper.P("@Permission", permission));
    }

    internal static Task<DataTable> ReadChargesAsync(int admissionId,int[] selected,bool wholeFamily=false) =>
        DatabaseHelper.QuerySqlAsync(ChargesSql, DatabaseHelper.Int("@UserId",CurrentUser.UserId),
            DatabaseHelper.Int("@Admission",admissionId),
            DatabaseHelper.P("@Ids",System.Text.Json.JsonSerializer.Serialize(selected)),DatabaseHelper.Bit("@WholeFamily",wholeFamily));

    internal static async Task ShowAsync(Form? owner, int admissionId, int[]? selected = null, Func<bool>? isCurrent = null)
    {
        int userId = CurrentUser.UserId;
        bool Current() => owner?.IsDisposed != true && CurrentUser.UserId == userId
            && CurrentUser.HasPermission("Fees.View") && (isCurrent?.Invoke() ?? true);
        if (!Current()) return;
        var members = await ReadMembersAsync(admissionId, "Fees.View");
        if (!Current()) return;
        int[] ids = FamilyBillingData.SelectMembers(members, selected);
        var charges = await ReadChargesAsync(admissionId,ids,wholeFamily:selected is null);
        if (!Current()) return;
        string notice = FamilyBillingData.IncompleteNotice(members, ids);
        if (charges.Rows.Count == 0 || charges.AsEnumerable().Sum(row=>Convert.ToDecimal(row["Due"]))<=0m)
        {
            ModernMessageDialog.ShowInfo(owner, "There are no posted outstanding charges for the selected children." +
                (notice.Length > 0 ? "\n\n" + notice : ""), "Print Bill");
            return;
        }
        if (notice.Length > 0 && !ModernMessageDialog.Confirm(owner, notice +
                "\n\nPrint the currently posted charges only?", "Family Billing Readiness", defaultYes: false)) return;
        if (!Current()) return;
        var institution = await InstitutionProfile.GetPrintIdentityAsync();
        if (!Current()) return;
        string parent = Convert.ToString(members.AsEnumerable().First(row => Convert.ToInt32(row["AdmissionId"]) == admissionId)["FatherName"]) ?? "";
        int[] billedStudents=charges.AsEnumerable().Select(row=>Convert.ToInt32(row["AdmissionId"])).Distinct().OrderBy(id=>id).ToArray();
        int primary=billedStudents.Contains(admissionId)?admissionId:billedStudents[0];
        if(billedStudents.Length>1)await FamilyBillReport.ShowAsync(owner,institution,parent,charges,primary,billedStudents);
        else await StudentBillReport.ShowChargesAsync(owner,institution,parent,charges,primary);
    }
}
