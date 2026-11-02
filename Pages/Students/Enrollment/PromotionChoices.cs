namespace FMSC_SMS.Pages.Students.Enrollment;

internal static class PromotionChoices
{
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

}
