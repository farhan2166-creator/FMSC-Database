using FMSC_SMS.Pages.Security;
using Microsoft.Data.SqlClient;

namespace FMSC_SMS.Core;

// Shared read-only billing entry point for registration and the family counter.
internal static class FamilyBillPreview
{
    private const string MembersSql = """
        EXEC dbo.RequirePermission @UserId,@Permission;
        DECLARE @Family int=(SELECT FamilyId FROM dbo.FamilyMembers WHERE AdmissionId=@Admission);
        SELECT FM.FamilyId,FM.FamilyMemberId,S.AdmissionId,S.StudentName,S.FatherName,S.Gender,
            S.AdmissionNumber,S.QuickReference,S.RegistrationNumber,S.Status,E.EnrollmentId,E.Status EnrollmentStatus,
            E.BillingReady,E.PlacementConfirmedAt,AY.YearName AcademicYear,C.ClassName,SEC.SectionName
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
        SELECT COALESCE(S.AdmissionNumber,S.QuickReference) Reference,S.StudentName,
            CASE WHEN A.ConcessionLabel IS NULL THEN F.FeeTypeName ELSE CONCAT(F.FeeTypeName,N' — ',A.ConcessionLabel) END FeeTypeName,F.DisplayOrder FeeDisplayOrder,B.BillingMonth,B.DiscountAmount,
            dbo.GetFeeChargeBalance(B.BillingChargeId) Due
        FROM dbo.Students S JOIN dbo.BillingCharges B ON B.AdmissionId=S.AdmissionId
        JOIN dbo.FeeTypes F ON F.FeeTypeId=B.FeeTypeId
        LEFT JOIN dbo.StudentFeeAssignments A ON A.StudentFeeAssignmentId=B.StudentFeeAssignmentId
        WHERE S.AdmissionId IN(SELECT CONVERT(int,[value]) FROM OPENJSON(@Ids))
            AND B.Status<>N'Cancelled' AND (dbo.GetFeeChargeBalance(B.BillingChargeId)>0 OR
             (B.NetAmount=0 AND A.ConcessionLabel IS NOT NULL AND B.BillingMonth=DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1)))
        UNION ALL
        SELECT COALESCE(S.AdmissionNumber,S.QuickReference),S.StudentName,
            CONCAT(F.FeeTypeName,N' — ',A.ConcessionLabel),F.DisplayOrder,
            DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1),A.DiscountAmount,CONVERT(decimal(12,2),0)
        FROM dbo.StudentFeeAssignments A JOIN dbo.FeeTypes F ON F.FeeTypeId=A.FeeTypeId
        JOIN dbo.Students S ON S.AdmissionId=A.AdmissionId
        JOIN dbo.Enrollments E ON E.EnrollmentId=A.EnrollmentId JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
        WHERE A.AdmissionId IN(SELECT CONVERT(int,[value]) FROM OPENJSON(@Ids))
            AND S.LegacySetupPending=0 AND E.Status IN(N'Active',N'Pending') AND E.BillingStartDate IS NOT NULL
            AND (E.Status=N'Pending' OR (E.BillingReady=1 AND E.PlacementConfirmedAt IS NOT NULL))
            AND CONVERT(date,GETDATE()) BETWEEN Y.StartDate AND Y.EndDate
            AND A.CancelledAt IS NULL AND A.Frequency=N'Monthly' AND A.ConcessionLabel IS NOT NULL AND A.Amount=A.DiscountAmount
            AND DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1)>=DATEFROMPARTS(YEAR(E.BillingStartDate),MONTH(E.BillingStartDate),1)
            AND (A.EffectiveFrom IS NULL OR A.EffectiveFrom<=DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1))
            AND (A.EffectiveTo IS NULL OR A.EffectiveTo>=DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1))
            AND NOT EXISTS(SELECT 1 FROM dbo.BillingCharges B WHERE B.EnrollmentId=A.EnrollmentId AND B.FeeTypeId=A.FeeTypeId
                AND B.Status<>N'Cancelled' AND B.BillingMonth=DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1))
        ORDER BY Reference,BillingMonth,FeeDisplayOrder,FeeTypeName;
        """;

    internal static Task<DataTable> ReadMembersAsync(int admissionId) => ReadMembersAsync(admissionId, "Students.View");
    private static Task<DataTable> ReadMembersAsync(int admissionId, string permission)
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
        if (charges.Rows.Count == 0)
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
        AdmissionBillReport.ShowFamily(owner, institution, parent, charges);
    }
}
