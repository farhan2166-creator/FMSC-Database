namespace FMSC_SMS.Core;

/// <summary>Parent details carried with report projections for a readable printed student identity.</summary>
internal static class ReportStudentIdentity
{
    private const string MetadataKey="FMSC.ReportStudentParents";
    private sealed record Parent(string Name,string Gender);
    private static string Key(string value)=>new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    internal static bool IsStudent(string name)=>Key(name) is "student" or "studentname";
    internal static bool IsAdmission(string name)=>Key(name) is "admqrno" or "admissionnumber" or "admno" or "admissionnumbertext";
    private static string Text(DataRow row,int index)=>Convert.ToString(row[index])?.Trim()??"";
    private static string IdentityKey(string reference,string name)=>reference+"\u001f"+name;

    internal static void CopyPrintMetadata(DataTable source,DataTable projection)
    {
        if(!source.Columns.Contains("__StudentFatherName")||!source.Columns.Contains("__StudentGender"))return;
        var admission=source.Columns.Cast<DataColumn>().FirstOrDefault(column=>IsAdmission(column.ColumnName));
        var student=source.Columns.Cast<DataColumn>().FirstOrDefault(column=>IsStudent(column.ColumnName));
        if(admission is null||student is null)return;
        var parents=new Dictionary<string,Parent>(StringComparer.OrdinalIgnoreCase);
        foreach(DataRow row in source.Rows)
            parents[IdentityKey(Text(row,admission.Ordinal),Text(row,student.Ordinal))]=
                new(Convert.ToString(row["__StudentFatherName"])??"",Convert.ToString(row["__StudentGender"])??"");
        projection.ExtendedProperties[MetadataKey]=parents;
    }

    internal static string FormatCell(DataRow row,int admission,int student)
    {
        string reference=Text(row,admission),name=Text(row,student),parent="",gender="";
        if(row.Table.ExtendedProperties[MetadataKey] is Dictionary<string,Parent> parents &&
            parents.TryGetValue(IdentityKey(reference,name),out var details))
        {parent=details.Name;gender=details.Gender;}
        return Format(reference,name,parent,gender);
    }

    internal static string Format(string reference,string name,string parent,string gender)
    {
        name=RecordTextPolicy.DisplayText("StudentName",name);
        parent=RecordTextPolicy.DisplayText("FatherName",parent);
        string identity=string.Join(" - ",new[]{reference.Trim(),name}.Where(value=>value.Length>0));
        if(name.Length==0||parent.Length==0)return identity;
        string relation=gender.Trim().ToUpperInvariant() switch {
            "FEMALE" or "F" or "GIRL"=>"d/o", "MALE" or "M" or "BOY"=>"s/o", _=>"child of"
        };
        return $"{identity} {relation} {parent}";
    }
}
