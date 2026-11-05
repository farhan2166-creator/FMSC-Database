using System.Data;
using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Reports.Finance;

internal static class CreditBalanceReportData
{
    // Read-only ledger sum; no deposits, transfers or allocations are performed here.
    internal const string Query = """
        EXEC dbo.RequirePermission @UserId,N'Reports.View';
        EXEC dbo.RequirePermission @UserId,N'Fees.View';
        ;WITH Credit AS (
            SELECT AdmissionId,SUM(Amount) AvailableCredit,MAX(CreatedAt) LastActivity
            FROM dbo.StudentCreditEntries GROUP BY AdmissionId HAVING SUM(Amount)>0
        )
        SELECT S.AdmissionId,COALESCE(S.AdmissionNumber,S.QuickReference,CONCAT(N'ID-',S.AdmissionId)) [Adm. / QR No.],
            S.StudentName [Student],S.FatherName [Father Name],S.Gender [__StudentGender],F.FamilyId,COALESCE(F.FamilyCode,N'') [Family],
            COALESCE(CA.CampusName,N'Unassigned') [Campus],
            CONCAT(CL.ClassName,CASE WHEN NULLIF(SEC.SectionName,N'') IS NULL THEN N'' ELSE CONCAT(N' – ',SEC.SectionName) END) [Class / Section],
            C.AvailableCredit [Available Credit],C.LastActivity [Last Activity],S.Status [Student Status]
        FROM Credit C JOIN dbo.Students S ON S.AdmissionId=C.AdmissionId
        LEFT JOIN dbo.FamilyMembers FM ON FM.AdmissionId=S.AdmissionId
        LEFT JOIN dbo.Families F ON F.FamilyId=FM.FamilyId
        OUTER APPLY (
            SELECT TOP(1) E.ClassSectionId FROM dbo.Enrollments E WHERE E.AdmissionId=S.AdmissionId
            ORDER BY CASE WHEN E.Status=N'Active' THEN 0 ELSE 1 END,E.EnrollmentDate DESC,E.EnrollmentId DESC
        ) Placement
        LEFT JOIN dbo.CampusClassSections CCS ON CCS.ClassSectionId=Placement.ClassSectionId
        LEFT JOIN dbo.Campuses CA ON CA.CampusId=CCS.CampusId
        LEFT JOIN dbo.Classes CL ON CL.ClassId=CCS.ClassId
        LEFT JOIN dbo.Sections SEC ON SEC.SectionId=CCS.SectionId
        WHERE (@CampusId=0 OR CCS.CampusId=@CampusId) AND (@ClassId=0 OR CCS.ClassId=@ClassId)
          AND (@SectionId=0 OR CCS.SectionId=@SectionId)
        ORDER BY CA.DisplayOrder,CL.DisplayOrder,SEC.DisplayOrder,S.StudentName,S.AdmissionId;
        """;

    internal static (DataTable Rows, DataTable Totals) Create(DataTable source, bool families, decimal minimum)
    {
        var selected = source.AsEnumerable().Where(r => Convert.ToDecimal(r["Available Credit"]) >= minimum).ToArray();
        DataTable result;
        if (!families)
        {
            result = new DataTable();
            // Explicit projection also excludes admission-number enrichment/sort columns.
            string[] fields = { "Adm. / QR No.", "Student", "Father Name", "Campus", "Class / Section", "Available Credit", "Last Activity", "Student Status" };
            foreach (string field in fields) result.Columns.Add(field,source.Columns[field]!.DataType);
            foreach (var row in selected) result.Rows.Add(fields.Select(field=>row[field]).ToArray());
            if(source.Columns.Contains("__StudentGender"))
            {
                result.Columns.Add("__StudentGender",typeof(string));
                for(int index=0;index<selected.Length;index++)result.Rows[index]["__StudentGender"]=selected[index]["__StudentGender"];
            }
        }
        else
        {
            result = new DataTable();
            foreach (string c in new[] { "Family / Student", "Members", "Student Advances" }) result.Columns.Add(c);
            result.Columns.Add("Students",typeof(int)); result.Columns.Add("Available Credit",typeof(decimal)); result.Columns.Add("Last Activity",typeof(DateTime));
            result.Columns.Add(FamilyAdvanceLayout.Column);
            foreach (var group in selected.GroupBy(r => r.IsNull("FamilyId") ? "Student:"+r["AdmissionId"] : "Family:"+r["FamilyId"]))
            {
                var first = group.First();
                string[] parents=group.Select(r=>Convert.ToString(r["Father Name"])?.Trim()??"").Where(name=>name.Length>0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                string label=parents.Length==1?parents[0]:Convert.ToString(first["Family"])?.Trim()??"";
                if(label.Length==0)label=first.IsNull("FamilyId")?"Individual student":"Family";
                var children=group.Select(r=>{
                    string identity=StudentIdentityText.Format(Convert.ToString(r["Adm. / QR No."])??"",Convert.ToString(r["Student"])??"",parents.Length==1?"":Convert.ToString(r["Father Name"])??"",
                        source.Columns.Contains("__StudentGender")?Convert.ToString(r["__StudentGender"])??"":"");
                    string campus=Convert.ToString(r["Campus"]) is {Length:>0} place?place:"Not recorded";
                    string placement=Convert.ToString(r["Class / Section"]) is {Length:>0} klass?klass:"Not recorded";
                    return new FamilyAdvanceLayout.Child(identity,$"{campus} · {placement}",Convert.ToDecimal(r["Available Credit"]));
                }).ToArray();
                string members=string.Join("\n\n",children.Select(child=>$"{child.Name}\n{child.Placement}"));
                string advances=string.Join("\n\n\n",children.Select(child=>child.Advance.ToString("#,##0.##")));
                result.Rows.Add(label,members,advances,group.Count(),
                    group.Sum(r => Convert.ToDecimal(r["Available Credit"])),group.Max(r => Convert.ToDateTime(r["Last Activity"])),
                    System.Text.Json.JsonSerializer.Serialize(new FamilyAdvanceLayout.Family(label,children)));
            }
        }
        var totals = new DataTable();
        totals.Columns.Add("Students holding credit",typeof(int)); totals.Columns.Add("Credit held",typeof(decimal));
        totals.Rows.Add(selected.Length,selected.Sum(r => Convert.ToDecimal(r["Available Credit"])));
        if(families){var count=totals.Columns.Add("Families",typeof(int));count.SetOrdinal(0);totals.Rows[0][count]=result.Rows.Count;}
        return (result,totals);
    }
}
