using System.Data;
namespace FMSC_SMS.Pages.Reports.Finance;
internal static class ConcessionSummaryData
{
    internal const string Query = """
        EXEC dbo.RequirePermission @UserId,N'Reports.View';
        SELECT A.AdmissionId,CA.CampusName [Campus],C.ClassName [Class],F.FeeTypeName [Fee Type],
            A.Frequency [Frequency],A.ConcessionLabel [Concession],A.DiscountAmount [Assigned Discount]
        FROM dbo.StudentFeeAssignments A
        JOIN dbo.Enrollments E ON E.EnrollmentId=A.EnrollmentId
        JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
        JOIN dbo.CampusClassSections P ON P.ClassSectionId=E.ClassSectionId
        JOIN dbo.Campuses CA ON CA.CampusId=P.CampusId
        JOIN dbo.Classes C ON C.ClassId=P.ClassId
        JOIN dbo.FeeTypes F ON F.FeeTypeId=A.FeeTypeId
        WHERE A.CancelledAt IS NULL AND A.ConcessionLabel IS NOT NULL
          AND (@FromDate IS NULL OR COALESCE(A.EffectiveTo,Y.EndDate)>=@FromDate)
          AND (@ToDate IS NULL OR COALESCE(A.EffectiveFrom,E.BillingStartDate)<=@ToDate)
          AND (@CampusId=0 OR P.CampusId=@CampusId) AND (@ClassId=0 OR P.ClassId=@ClassId)
          AND (@SectionId=0 OR P.SectionId=@SectionId);
        """;
    internal static (DataTable Rows, DataTable Totals) Create(DataTable source)
    {
        var result = new DataTable();
        foreach (string name in new[] { "Campus", "Class", "Fee Type", "Frequency", "Concession" }) result.Columns.Add(name);
        result.Columns.Add("Students",typeof(int)); result.Columns.Add("Assignments",typeof(int));
        result.Columns.Add("Assigned Discount / Cycle (Rs.)",typeof(decimal));
        string Text(DataRow r,string key) => Convert.ToString(r[key]) ?? "";
        var groups = source.AsEnumerable().GroupBy(r => (Campus:Text(r,"Campus"),Class:Text(r,"Class"),Fee:Text(r,"Fee Type"),Frequency:Text(r,"Frequency"),Concession:Text(r,"Concession")));
        foreach (var group in groups.OrderBy(g=>g.Key.Campus).ThenBy(g=>g.Key.Class).ThenBy(g=>g.Key.Fee).ThenBy(g=>g.Key.Frequency).ThenBy(g=>g.Key.Concession))
            result.Rows.Add(group.Key.Campus,group.Key.Class,group.Key.Fee,group.Key.Frequency,group.Key.Concession,
                group.Select(r=>Convert.ToInt32(r["AdmissionId"])).Distinct().Count(),group.Count(),group.Sum(r=>Convert.ToDecimal(r["Assigned Discount"])));
        var totals = new DataTable();totals.Columns.Add("Students",typeof(int));totals.Columns.Add("Assignments",typeof(int));
        totals.Rows.Add(source.AsEnumerable().Select(r=>Convert.ToInt32(r["AdmissionId"])).Distinct().Count(),source.Rows.Count);
        return (result,totals);
    }
}
