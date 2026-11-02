using FMSC_SMS.Pages.Security;
using Microsoft.Data.SqlClient;

namespace FMSC_SMS.Core;

// Shared read-only billing entry point for registration and the family counter.
internal static class FeeBillPreview
{
    private const string MemberSelect = """
        SELECT FM.FamilyId,FM.FamilyMemberId,S.AdmissionId,S.StudentName,S.FatherName,S.Gender,
            S.AdmissionNumber,S.QuickReference,S.RegistrationNumber,S.Status,E.EnrollmentId,E.Status EnrollmentStatus,
            E.BillingReady,E.PlacementConfirmedAt,E.ReturnConfirmedAt,AY.YearName AcademicYear,
            AY.StartDate AcademicYearStartDate,AY.EndDate AcademicYearEndDate,C.ClassName,SEC.SectionName,
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
        """;

    private const string MembersSql = """
        EXEC dbo.RequirePermission @UserId,@Permission;
        DECLARE @Family int=(SELECT FamilyId FROM dbo.FamilyMembers WHERE AdmissionId=@Admission);
        """ + "\n" + MemberSelect + "\n" + """
        WHERE S.AdmissionId=@Admission OR (@Family IS NOT NULL AND FM.FamilyId=@Family)
        ORDER BY COALESCE(C.DisplayOrder,-1) DESC,S.StudentName,S.AdmissionId;
        """;

    // Initial bulk review needs only the explicitly selected children. Issuance
    // still uses the family-scoped reads below to recheck membership and dues.
    internal static Task<DataTable> ReadSelectedMembersAsync(int[] selected) => DatabaseHelper.QuerySqlAsync(
        "EXEC dbo.RequirePermission @UserId,N'Fees.View';\n" + MemberSelect + "\n" +
        "WHERE S.AdmissionId IN (SELECT CONVERT(int,[value]) FROM OPENJSON(@Ids));",
        DatabaseHelper.Int("@UserId",CurrentUser.UserId),
        DatabaseHelper.P("@Ids",System.Text.Json.JsonSerializer.Serialize(selected)));

    internal static async Task<DataTable> ReadSelectedChargesAsync(int[] selected)
    {
        var charges=await DatabaseHelper.QueryUserAsync("GetFeeVisitCharges",
            DatabaseHelper.P("@StudentIdsJson",System.Text.Json.JsonSerializer.Serialize(selected)));
        try{await FeeCarryForwardLabels.ApplyAsync(charges);return charges;}
        catch{charges.Dispose();throw;}
    }

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

    internal static async Task<DataTable> ReadChargesAsync(int admissionId,int[] selected,bool wholeFamily=false)
    {
        var charges=await DatabaseHelper.QuerySqlAsync(ChargesSql, DatabaseHelper.Int("@UserId",CurrentUser.UserId),
            DatabaseHelper.Int("@Admission",admissionId),
            DatabaseHelper.P("@Ids",System.Text.Json.JsonSerializer.Serialize(selected)),DatabaseHelper.Bit("@WholeFamily",wholeFamily));
        try{await FeeCarryForwardLabels.ApplyAsync(charges);return charges;}
        catch{charges.Dispose();throw;}
    }

    internal static async Task ShowAsync(Form? owner, int admissionId, int[]? selected = null, Func<bool>? isCurrent = null, bool byFamily=false)
    {
        int user=CurrentUser.UserId;
        bool Current()=>owner?.IsDisposed!=true&&CurrentUser.UserId==user&&CurrentUser.HasPermission("Fees.View")&&(isCurrent?.Invoke()??true);
        if(!Current())return;
        using var members=await ReadMembersAsync(admissionId,"Fees.View");
        if(!Current())return;
        // Registration prints this child; collection supplies its explicitly checked children.
        int[] ids=selected??new[]{admissionId};
        _=FamilyBillingData.SelectMembers(members,ids);

        await OutstandingBillsPreview.ShowAsync(owner,ids,Current,
            message=>ModernMessageDialog.ShowInfo(owner,message,"Outstanding Bills"),byFamily:byFamily);
    }
}
