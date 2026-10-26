namespace FMSC_SMS.Core;

// Print-only identity labels; no fee or payment records are changed.
internal static class FeeDocumentParents
{
    internal static string Relation(string gender)=>gender.Trim().ToUpperInvariant() switch {
        "MALE" or "M" or "BOY"=>"S/O",
        "FEMALE" or "F" or "GIRL"=>"D/O",
        _=>"Father"
    };

    private static string Reference(DataRow row)
    {
        foreach(string field in new[]{"BillStudentKey","ChildAdmissionNumberText","AdmissionNumberText"})
            if(row.Table.Columns.Contains(field)&&!row.IsNull(field)&&!string.IsNullOrWhiteSpace(Convert.ToString(row[field])))
                return Convert.ToString(row[field])!.Trim();
        return "";
    }

    internal static void Apply(DataTable document,IReadOnlyDictionary<string,string> genders)
    {
        if(!document.Columns.Contains("ParentRelationLabel"))document.Columns.Add("ParentRelationLabel",typeof(string)).DefaultValue="Father";
        foreach(DataRow row in document.Rows)
            row["ParentRelationLabel"]=genders.TryGetValue(Reference(row),out string? gender)?Relation(gender):"Father";
    }

    internal static async Task ApplyAsync(DataTable document,string permission="Fees.View")
    {
        if(document.Rows.Count==0)return;
        // New saved bills already contain their print labels. Older documents
        // obtain the recorded gender through the same fee-read permission.
        if(document.Columns.Contains("ParentRelationLabel")&&document.AsEnumerable().All(row=>Convert.ToString(row["ParentRelationLabel"]) is "S/O" or "D/O"))return;
        string[] references=document.AsEnumerable().Select(Reference).Where(value=>value.Length>0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        using var students=await DatabaseHelper.QuerySqlAsync("""
            EXEC dbo.RequirePermission @UserId,@Permission;
            SELECT S.AdmissionNumber,S.QuickReference,S.Gender FROM dbo.Students S
            WHERE EXISTS(SELECT 1 FROM OPENJSON(@References) J WHERE S.AdmissionNumber=J.[value] OR S.QuickReference=J.[value]);
            """,DatabaseHelper.Int("@UserId",CurrentUser.UserId),DatabaseHelper.P("@Permission",permission),
            DatabaseHelper.P("@References",System.Text.Json.JsonSerializer.Serialize(references)));
        var genders=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(DataRow student in students.Rows)
            foreach(string field in new[]{"AdmissionNumber","QuickReference"})
                if(!student.IsNull(field)&&!string.IsNullOrWhiteSpace(Convert.ToString(student[field])))
                    genders[Convert.ToString(student[field])!]=Convert.ToString(student["Gender"])??"";
        Apply(document,genders);
    }
}
