namespace FMSC_SMS.Core;

/// <summary>Parent details carried with report projections for a readable printed student identity.</summary>
internal static class ReportStudentIdentity
{
    private const string MetadataKey="FMSC.ReportStudentParents";
    internal const string SourceColumnKey=ReportContextProjection.SourceColumnKey;
    private sealed record Parent(string Name,string Gender);
    private static string Key(string value)=>new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    internal static bool IsStudent(string name)=>Key(name) is "student" or "studentname";
    internal static bool IsAdmission(string name)=>Key(name) is "admqrno" or "admissionnumber" or "admno" or "admissionnumbertext";
    internal static bool Matches(DataColumn column,params string[] names)=>names.Any(name=>
        Key(name)==Key(column.ColumnName)||Key(name)==Key(Convert.ToString(column.ExtendedProperties[SourceColumnKey])??""));
    private static string Text(DataRow row,int index)=>Convert.ToString(row[index])?.Trim()??"";
    private static string IdentityKey(string reference,string name)=>reference+"\u001f"+name;

    internal static void CopyPrintMetadata(DataTable source,DataTable projection)
    {
        var father=source.Columns.Cast<DataColumn>().FirstOrDefault(column=>column.ColumnName=="__StudentFatherName")
            ??source.Columns.Cast<DataColumn>().FirstOrDefault(column=>Matches(column,"FatherName","Father"));
        var gender=source.Columns.Cast<DataColumn>().FirstOrDefault(column=>column.ColumnName=="__StudentGender")
            ??source.Columns.Cast<DataColumn>().FirstOrDefault(column=>Matches(column,"Gender"));
        if(father is null || (father.ColumnName!="__StudentFatherName" && !projection.Columns.Cast<DataColumn>().Any(column=>Matches(column,"FatherName","Father"))))return;
        var admission=source.Columns.Cast<DataColumn>().FirstOrDefault(column=>IsAdmission(column.ColumnName));
        var student=source.Columns.Cast<DataColumn>().FirstOrDefault(column=>IsStudent(column.ColumnName));
        if(admission is null||student is null)return;
        var parents=new Dictionary<string,Parent>(StringComparer.OrdinalIgnoreCase);
        foreach(DataRow row in source.Rows)
            parents[IdentityKey(Text(row,admission.Ordinal),Text(row,student.Ordinal))]=
                new(Text(row,father.Ordinal),gender is null?"":Text(row,gender.Ordinal));
        projection.ExtendedProperties[MetadataKey]=parents;
    }

    internal static string FormatCell(DataRow row,int admission,int student,int father=-1,int selectedGender=-1)
    {
        string reference=admission<0?"":Text(row,admission),name=Text(row,student),parent=father<0?"":Text(row,father),gender=selectedGender<0?"":Text(row,selectedGender);
        if(row.Table.ExtendedProperties[MetadataKey] is Dictionary<string,Parent> parents &&
            parents.TryGetValue(IdentityKey(reference,name),out var details))
        {parent=details.Name;gender=details.Gender;}
        return Format(reference,name,parent,gender);
    }

    internal static string Format(string reference,string name,string parent,string gender)
    {
        name=RecordTextPolicy.DisplayText("StudentName",name);
        parent=RecordTextPolicy.DisplayText("FatherName",parent);
        return StudentIdentityText.Format(reference,name,parent,gender);
    }
}
