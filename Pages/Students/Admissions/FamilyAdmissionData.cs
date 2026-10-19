using FMSC_SMS.Core;

namespace FMSC_SMS.Pages.Students.Admissions;

internal static class FamilyAdmissionData
{
    internal static bool CanUse(Func<string,bool> permission)=>permission("Students.View") &&
        (permission("Students.Admit") || permission("Students.Administer"));

    internal static Task<DataTable> ReadAsync(int admission)=>DatabaseHelper.QuerySqlAsync("""
        EXEC dbo.RequirePermission @UserId,N'Students.View';
        IF @Fees=1 EXEC dbo.RequirePermission @UserId,N'Fees.View';
        DECLARE @Family int=(SELECT FamilyId FROM dbo.FamilyMembers WHERE AdmissionId=@Admission);
        SELECT S.AdmissionId,S.StudentName,S.FatherName,S.ContactNumber,S.Address,S.Status,
          COALESCE(S.AdmissionNumber,S.QuickReference) Reference,FM.FamilyId,
          E.EnrollmentId,E.Status EnrollmentStatus,E.BillingReady,E.PlacementConfirmedAt,
          Y.YearName AcademicYear,CP.CampusName,C.ClassName,SE.SectionName,
          CASE WHEN @Fees=1 THEN (SELECT COALESCE(SUM(dbo.GetFeeChargeBalance(B.BillingChargeId)),0)
            FROM dbo.BillingCharges B WHERE B.AdmissionId=S.AdmissionId AND B.Status<>N'Cancelled') ELSE NULL END Outstanding
        FROM dbo.Students S LEFT JOIN dbo.FamilyMembers FM ON FM.AdmissionId=S.AdmissionId
        OUTER APPLY(SELECT TOP(1) X.* FROM dbo.Enrollments X WHERE X.AdmissionId=S.AdmissionId
          ORDER BY CASE WHEN X.Status=N'Active' THEN 0 WHEN X.Status=N'Pending' THEN 1 ELSE 2 END,X.EnrollmentId DESC) E
        LEFT JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
        LEFT JOIN dbo.CampusClassSections P ON P.ClassSectionId=E.ClassSectionId
        LEFT JOIN dbo.Campuses CP ON CP.CampusId=P.CampusId
        LEFT JOIN dbo.Classes C ON C.ClassId=P.ClassId LEFT JOIN dbo.Sections SE ON SE.SectionId=P.SectionId
        WHERE S.AdmissionId=@Admission OR (@Family IS NOT NULL AND FM.FamilyId=@Family)
        ORDER BY C.DisplayOrder DESC,S.StudentName,S.AdmissionId;
        """,DatabaseHelper.Int("@UserId",CurrentUser.UserId),DatabaseHelper.Int("@Admission",admission),
        DatabaseHelper.Bit("@Fees",CurrentUser.HasPermission("Fees.View")));

    // Extend the standard bill read with fully waived charges from the selected
    // current enrollments. Zero-value rows are evidence of a concession, not payment.
    internal static async Task<DataTable> ReadBillAsync(int anchor,int[] ids)
    {
        using var members=await FeeBillPreview.ReadMembersAsync(anchor,"Fees.View");
        FamilyBillingData.SelectMembers(members,ids);
        var charges=await FeeBillPreview.ReadChargesAsync(anchor,ids);
        using var zero=await DatabaseHelper.QuerySqlAsync("""
            EXEC dbo.RequirePermission @UserId,N'Fees.View';
            DECLARE @Family int=(SELECT FamilyId FROM dbo.FamilyMembers WHERE AdmissionId=@Anchor);
            IF EXISTS(SELECT 1 FROM OPENJSON(@Ids) J WHERE CONVERT(int,J.value)<>@Anchor AND NOT EXISTS(
              SELECT 1 FROM dbo.FamilyMembers FM WHERE FM.AdmissionId=CONVERT(int,J.value) AND FM.FamilyId=@Family))
              THROW 51000,'The selected family changed. Refresh before printing.',1;
            SELECT COALESCE(S.AdmissionNumber,S.QuickReference) Reference,S.StudentName,
              CONCAT(F.FeeTypeName,N' — Waived') FeeTypeName,F.DisplayOrder FeeDisplayOrder,
              B.BillingMonth,B.DiscountAmount,CONVERT(decimal(12,2),0) Due,B.BillingChargeId,S.AdmissionId
            FROM dbo.BillingCharges B JOIN dbo.Students S ON S.AdmissionId=B.AdmissionId
            JOIN dbo.Enrollments E ON E.EnrollmentId=B.EnrollmentId JOIN dbo.FeeTypes F ON F.FeeTypeId=B.FeeTypeId
            WHERE B.AdmissionId IN(SELECT CONVERT(int,[value]) FROM OPENJSON(@Ids))
              AND E.Status IN(N'Active',N'Pending') AND B.Status<>N'Cancelled' AND B.NetAmount=0 AND B.DiscountAmount>0
              AND (B.ChargeKind<>N'Monthly' OR B.BillingMonth>=DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1))
            UNION ALL
            SELECT COALESCE(S.AdmissionNumber,S.QuickReference),S.StudentName,CONCAT(F.FeeTypeName,N' — Waived'),F.DisplayOrder,
              DATEFROMPARTS(YEAR(E.EnrollmentDate),MONTH(E.EnrollmentDate),1),A.DiscountAmount,
              CONVERT(decimal(12,2),0),-CONVERT(bigint,A.StudentFeeAssignmentId),S.AdmissionId
            FROM dbo.StudentFeeAssignments A JOIN dbo.Students S ON S.AdmissionId=A.AdmissionId
            JOIN dbo.Enrollments E ON E.EnrollmentId=A.EnrollmentId JOIN dbo.FeeTypes F ON F.FeeTypeId=A.FeeTypeId
            WHERE A.AdmissionId IN(SELECT CONVERT(int,[value]) FROM OPENJSON(@Ids))
              AND E.Status IN(N'Active',N'Pending') AND S.LegacySetupPending=0
              AND (E.Status=N'Pending' OR (E.BillingReady=1 AND E.PlacementConfirmedAt IS NOT NULL))
              AND A.CancelledAt IS NULL AND A.Frequency<>N'Monthly' AND A.Amount>0 AND A.Amount=A.DiscountAmount
              AND NOT EXISTS(SELECT 1 FROM dbo.BillingCharges B WHERE B.StudentFeeAssignmentId=A.StudentFeeAssignmentId AND B.Status<>N'Cancelled');
            """,DatabaseHelper.Int("@UserId",CurrentUser.UserId),DatabaseHelper.Int("@Anchor",anchor),DatabaseHelper.P("@Ids",System.Text.Json.JsonSerializer.Serialize(ids)));
        var existing=charges.AsEnumerable().Select(r=>Convert.ToInt64(r["BillingChargeId"])).ToHashSet();
        foreach(DataRow row in zero.Rows)if(existing.Add(Convert.ToInt64(row["BillingChargeId"])))charges.ImportRow(row);
        return charges;
    }
}
