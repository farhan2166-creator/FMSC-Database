using System.Text.Json;

namespace FMSC_SMS.Core;

// Read-only display projection. Receipt amounts and saved snapshots are untouched.
internal static class CollectionReceiptPresentation
{
    private sealed record Placement(string ClassSection,string Campus);
    private sealed record Student(long Id,string Name,string Father,string Gender,IReadOnlyList<Placement> Placements);
    private static string Text(JsonElement value,string name)=>value.TryGetProperty(name,out var field)&&field.ValueKind==JsonValueKind.String?field.GetString()??"":"";
    private static long Id(JsonElement value)=>value.TryGetProperty("AdmissionId",out var field)&&field.ValueKind==JsonValueKind.Number&&field.TryGetInt64(out long id)?id:0;
    private static IEnumerable<JsonElement> Items(JsonElement value,string name)=>value.TryGetProperty(name,out var array)&&array.ValueKind==JsonValueKind.Array?array.EnumerateArray():Enumerable.Empty<JsonElement>();
    private static Placement Place(JsonElement value)
    {
        string klass=Text(value,"ClassName"),section=Text(value,"SectionName");
        string label=Text(value,"ClassSection");
        if(label.Length==0)label=string.Join(" – ",new[]{klass,section}.Where(text=>text.Length>0));
        return new(label,Text(value,"CampusName"));
    }
    private static string Names(IReadOnlyList<Student> students)=>StudentIdentityText.Family(students.Select(student=>
        new StudentIdentityText.Member("",RecordTextPolicy.DisplayText("StudentName",student.Name),RecordTextPolicy.DisplayText("FatherName",student.Father),student.Gender)));
    internal static void Apply(DataTable rows)
    {
        if(!rows.Columns.Contains("__ReceiptStudents"))return;
        foreach(DataRow row in rows.Rows)
        {
            if(row.IsNull("__ReceiptStudents"))continue;
            using var roster=JsonDocument.Parse(Convert.ToString(row["__ReceiptStudents"])!);
            var current=roster.RootElement.EnumerateArray().Select(value=>new Student(Id(value),Text(value,"StudentName"),Text(value,"FatherName"),Text(value,"Gender"),
                Items(value,"Placements").Select(Place).ToArray())).ToArray();
            var students=new List<Student>();
            if(rows.Columns.Contains("__ReceiptSnapshot")&&!row.IsNull("__ReceiptSnapshot")&&Convert.ToString(row["__ReceiptSnapshot"]) is {Length:>0} json)
            {
                using var snapshot=JsonDocument.Parse(json);
                foreach(var saved in Items(snapshot.RootElement,"Students"))
                {
                    var fallback=current.FirstOrDefault(student=>student.Id==Id(saved));
                    var placements=Items(snapshot.RootElement,"Lines").Where(line=>Id(line)==Id(saved)).Select(Place).Where(place=>place.ClassSection.Length>0).Distinct().ToArray();
                    students.Add(new(Id(saved),saved.TryGetProperty("StudentName",out _)?Text(saved,"StudentName"):fallback?.Name??"",
                        saved.TryGetProperty("FatherName",out _)?Text(saved,"FatherName"):fallback?.Father??"",
                        saved.TryGetProperty("Gender",out _)?Text(saved,"Gender"):fallback?.Gender??"",
                        placements.Length>0?placements:fallback?.Placements??Array.Empty<Placement>()));
                }
            }
            students.AddRange(current.Where(student=>!students.Any(saved=>saved.Id==student.Id)));
            if(students.Count==0)continue;
            row["Student / Family"]=Names(students);
            // Keep one entry per student, including repeated classes, in the same order.
            row["Class / Section"]=string.Join("\n",students.Select(student=>string.Join(" / ",student.Placements.Select(place=>place.ClassSection).Where(text=>text.Length>0).Distinct()) is {Length:>0} label?label:"Not recorded"));
            var campuses=students.SelectMany(student=>student.Placements).Select(place=>place.Campus).Where(text=>text.Length>0).Distinct().ToArray();
            if(campuses.Length>0)row["Campus"]=string.Join(", ",campuses);
        }
    }
}
