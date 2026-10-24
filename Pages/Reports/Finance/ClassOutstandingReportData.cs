using System.Data;

namespace FMSC_SMS.Pages.Reports.Finance;

internal static class ClassOutstandingReportData
{
    // Same charge eligibility, placement and authoritative balance as GetOutstandingAndAgingReport.
    internal const string Query = """
        EXEC dbo.RequirePermission @UserId, N'Reports.View';
        SELECT CCS.CampusId, CAMP.CampusName, CAMP.DisplayOrder CampusOrder,
               CCS.ClassId, C.ClassName, C.DisplayOrder ClassOrder,
               CCS.SectionId, SEC.SectionName, SEC.DisplayOrder SectionOrder,
               S.AdmissionId, S.AdmissionNumber, S.StudentName, S.FatherName,
               SUM(B.Balance) Outstanding
        FROM dbo.BillingCharges BC
        CROSS APPLY (SELECT dbo.GetFeeChargeBalance(BC.BillingChargeId) Balance) B
        JOIN dbo.Students S ON S.AdmissionId = BC.AdmissionId
        JOIN dbo.Enrollments E ON E.EnrollmentId = BC.EnrollmentId
        JOIN dbo.CampusClassSections CCS ON CCS.ClassSectionId = E.ClassSectionId
        JOIN dbo.Campuses CAMP ON CAMP.CampusId = CCS.CampusId
        JOIN dbo.Classes C ON C.ClassId = CCS.ClassId
        JOIN dbo.Sections SEC ON SEC.SectionId = CCS.SectionId
        JOIN dbo.FeeTypes FT ON FT.FeeTypeId = BC.FeeTypeId
        WHERE BC.Status <> N'Cancelled' AND B.Balance > 0
          AND (@CampusId = 0 OR CCS.CampusId = @CampusId)
          AND (@AcademicYearId = 0 OR E.AcademicYearId = @AcademicYearId)
          AND (@ClassId = 0 OR CCS.ClassId = @ClassId)
          AND (@SectionId = 0 OR CCS.SectionId = @SectionId)
        GROUP BY CCS.CampusId, CAMP.CampusName, CAMP.DisplayOrder,
                 CCS.ClassId, C.ClassName, C.DisplayOrder,
                 CCS.SectionId, SEC.SectionName, SEC.DisplayOrder,
                 S.AdmissionId, S.AdmissionNumber, S.StudentName, S.FatherName;
        """;

    internal static DataTable Create(DataTable students, string schoolName, string yearSelection, DateTime generated)
    {
        var data = students.Clone();
        data.Columns.Add("GroupKey", typeof(string));
        data.Columns.Add("SerialNumber", typeof(int));
        foreach (string column in new[] { "SchoolName", "YearSelection", "Generated", "Remarks" })
            data.Columns.Add(column, typeof(string));
        var groups = students.AsEnumerable().Where(row => Convert.ToDecimal(row["Outstanding"]) > 0)
            .GroupBy(row => (Campus: Convert.ToInt32(row["CampusId"]), Class: Convert.ToInt32(row["ClassId"]),
                Section: Convert.ToInt32(row["SectionId"])))
            .OrderBy(group => Convert.ToInt32(group.First()["CampusOrder"]))
            .ThenBy(group => group.Key.Campus)
            .ThenBy(group => Convert.ToInt32(group.First()["ClassOrder"]))
            .ThenBy(group => group.Key.Class)
            .ThenBy(group => Convert.ToInt32(group.First()["SectionOrder"]))
            .ThenBy(group => group.Key.Section);
        foreach (var group in groups)
        {
            int serial = 0;
            foreach (var student in group.OrderBy(row => Convert.ToString(row["StudentName"]), StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(row => Convert.ToString(row["FatherName"]), StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(row => Convert.ToString(row["AdmissionNumber"]), StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => Convert.ToInt32(row["AdmissionId"])))
            {
                var row = data.NewRow();
                foreach (DataColumn column in students.Columns) row[column.ColumnName] = student[column];
                row["GroupKey"] = $"{group.Key.Campus}:{group.Key.Class}:{group.Key.Section}";
                row["SerialNumber"] = ++serial;
                row["SchoolName"] = schoolName;
                row["YearSelection"] = yearSelection;
                row["Generated"] = generated.ToString("dd-MM-yyyy HH:mm");
                row["Remarks"] = "";
                data.Rows.Add(row);
            }
        }
        return data;
    }
}
