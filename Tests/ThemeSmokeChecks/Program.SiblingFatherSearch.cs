using System.Data;
using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Students.Dialogs;

internal static partial class Program
{
    private static void CheckSiblingFatherSearch()
    {
        using var students=new DataTable();
        students.Columns.Add("AdmissionId",typeof(int));
        foreach(string column in new[]{"StudentName","FatherName","AdmissionNumber","RegistrationNumber","Status"})students.Columns.Add(column);
        students.Rows.Add(1,"Muhammad Sajid","Muhammad Ayaz","190","R-190","Active");
        students.Rows.Add(2,"Fahad Ahmad","Muhammad Sajid","3728","R-3728","Active");
        students.Rows.Add(3,"Muhammad Fawad","Muhammad Sajid","4434","R-4434","Active");
        students.Rows.Add(4,"Muhammad Faizan","Muhammad Sajid","5649","R-5649","Left");
        students.Rows.Add(5,"Muhammad Faizan","Muhammad Sajid Ali","5949","R-5949","Active");
        students.Rows.Add(6,"Other child","Syed Muhammad Sajid","Muhammad Sajid","Muhammad Sajid","Active");
        students.Rows.Add(7,"No parent",DBNull.Value,"7000","R-7000","Active");
        students.Rows.Add(8,"No parent","","8000","R-8000","Active");
        students.Rows.Add(9,"Other spacing","Muhammad  Sajid","9000","R-9000","Active");
        var found=PossibleSiblingSearchForm.FindFatherMatches(students,"Muhammad Sajid");
        Check(found.Select(row=>row.Field<int>("AdmissionId")).SequenceEqual(new[]{2,3,4}),
            "Possible siblings match the entire father name, excluding student names, numbers and longer names");
        Check(PossibleSiblingSearchForm.FindFatherMatches(students,"Sajid").Count==0,
            "A partial father name cannot produce sibling suggestions");
        Check(PossibleSiblingSearchForm.FindFatherMatches(students,"  muhammad sajid  ").Count==3,
            "Name matching ignores letter case and surrounding spaces");
        Check(found.Any(row=>row.Field<string>("Status")=="Left"),
            "An exact father-name match keeps former students eligible for review");
        Check(PossibleSiblingSearchForm.FindFatherMatches(students,null).Count==0 &&
            PossibleSiblingSearchForm.FindFatherMatches(students,"   ").Count==0,
            "An empty search cannot match missing or blank father names");
        students.Rows.Add(10,"Literal name","O'Neil [A]%","10000","R-10000","Active");
        Check(PossibleSiblingSearchForm.FindFatherMatches(students,"O'Neil [A]%").Single().Field<int>("AdmissionId")==10 &&
            PossibleSiblingSearchForm.FindFatherMatches(students,"%").Count==0,
            "Punctuation is matched literally and cannot act as search wildcards");
        using var missingColumn=new DataTable();missingColumn.Columns.Add("StudentName");missingColumn.Rows.Add("Muhammad Sajid");
        Check(PossibleSiblingSearchForm.FindFatherMatches(missingColumn,"Muhammad Sajid").Count==0,
            "A missing father column never falls back to a student-name match");
        using var form=new PossibleSiblingSearchForm(null,1,"Muhammad Sajid");
        var input=(ModernTextBox)typeof(PossibleSiblingSearchForm).GetField("searchBox",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(form)!;
        Check(input.Text=="Muhammad Sajid" && input.PlaceholderText.Contains("father") && input.AccessibleName=="Father Name",
            "The dialog prefills and labels only the father's full name");
        Check(!form.IsHandleCreated,"Search checks never open the dialog or query the school database");
    }
}
