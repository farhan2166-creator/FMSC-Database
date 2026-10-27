using FMSC_SMS.Core;

namespace FMSC_SMS.Pages.Students.Enrollment;

internal static class PromotionChoices
{
    // Match the active source placements used by GetBulkPromotionCandidates.
    internal const string SourcePlacementsSql = """
        EXEC dbo.RequirePermission @UserId,N'Enrollment.Manage';
        SELECT DISTINCT P.ClassId,P.SectionId,SEC.SectionName,SEC.DisplayOrder SectionDisplayOrder
        FROM dbo.Enrollments E JOIN dbo.Students S ON S.AdmissionId=E.AdmissionId
        JOIN dbo.CampusClassSections P ON P.ClassSectionId=E.ClassSectionId
        JOIN dbo.Sections SEC ON SEC.SectionId=P.SectionId
        WHERE E.Status=N'Active' AND E.AcademicYearId=@Year AND P.CampusId=@Campus;
        """;

    internal static Task<DataTable> ReadSourcesAsync(int campus,int year) => DatabaseHelper.QuerySqlAsync(
        SourcePlacementsSql,DatabaseHelper.Int("@UserId",CurrentUser.UserId),
        DatabaseHelper.Int("@Campus",campus),DatabaseHelper.Int("@Year",year));

    internal static DataTable SourceSections(DataTable placements,int classId)
    {
        var result=placements.Clone();
        foreach(var row in placements.AsEnumerable().Where(row=>Convert.ToInt32(row["ClassId"])==classId)
            .DistinctBy(row=>Convert.ToInt32(row["SectionId"]))) result.ImportRow(row);
        return LookupDisplayOrder.ForBinding(result,"SectionId");
    }

    // Find the next catalog class before applying campus availability: a missing
    // campus assignment must never cause promotion to skip a class.
    internal static DataRow? NextClass(DataTable catalog,int sourceClass)
    {
        var source=catalog.AsEnumerable().SingleOrDefault(row=>Convert.ToInt32(row["ClassId"])==sourceClass);
        if(source is null||!catalog.Columns.Contains("AcademicProgramId")||!catalog.Columns.Contains("DisplayOrder")
            ||source.IsNull("AcademicProgramId")||source.IsNull("DisplayOrder")) return null;
        int program=Convert.ToInt32(source["AcademicProgramId"]),order=Convert.ToInt32(source["DisplayOrder"]);
        return catalog.AsEnumerable().Where(row=>!row.IsNull("AcademicProgramId")&&!row.IsNull("DisplayOrder")
            &&Convert.ToInt32(row["AcademicProgramId"])==program
            &&(Convert.ToInt32(row["DisplayOrder"])>order
                ||(Convert.ToInt32(row["DisplayOrder"])==order&&Convert.ToInt32(row["ClassId"])>sourceClass)))
            .OrderBy(row=>Convert.ToInt32(row["DisplayOrder"])).ThenBy(row=>Convert.ToInt32(row["ClassId"]))
            .FirstOrDefault();
    }

    internal static DataTable DestinationClasses(DataTable catalog,DataTable assignments,int sourceClass)
    {
        var result=catalog.Clone();
        if(NextClass(catalog,sourceClass) is DataRow next&&assignments.AsEnumerable()
            .Any(row=>Convert.ToInt32(row["ClassId"])==Convert.ToInt32(next["ClassId"]))) result.ImportRow(next);
        return result;
    }

    internal static DataTable Decisions()
    {
        var result=new DataTable();result.Columns.Add("Value");result.Columns.Add("Label");
        result.Rows.Add("PROMOTE","Promote");result.Rows.Add("HOLD","Repeat Class");return result;
    }
}
