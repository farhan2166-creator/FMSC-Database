using System.Data;
using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Students.Certificates;

internal static partial class Program
{
    private static void CheckRegularCertificateSearch()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(RegularStudentCertificatePage);
        using var page = new RegularStudentCertificatePage(false, readCertificates: _ => new DataTable());
        T Field<T>(string name) => (T)type.GetField(name, flags)!.GetValue(page)!;
        void Refresh() => type.GetMethod("ApplyWorkflowState", flags)!.Invoke(page, null);
        var picker = Field<StudentPicker>("student");
        var create = Field<ModernButton>("createCertificateButton");
        var status = (Label)typeof(V2Page).GetField("StatusLabel", flags)!.GetValue(page)!;
        using var students = new DataTable();
        students.Columns.Add("AdmissionId", typeof(int));
        foreach (string name in new[] { "AdmissionNumber", "StudentName", "FatherName", "ClassName", "Status" }) students.Columns.Add(name);
        students.Columns.Add("DateOfBirth", typeof(DateTime));
        var row = students.Rows.Add(3334, "3334", "Sample Student", "Sample Parent", "Ten", "Active", DBNull.Value);
        picker.SetSource(students);
        var matches = picker.FindMatches(students, "3334");
        Check(matches.Entries.Count == 1 && matches.Entries[0].Id == 3334,
            "Regular certificate search includes an exact admission-number match with no recorded birth date.");
        picker.SetStudent(row);
        type.GetMethod("StudentChanged", flags)!.Invoke(page, null);
        Check(picker.SelectedId == 3334 && Field<ModernTextBox>("dateOfBirth").Text == "Not on record",
            "A student without a birth date can be selected and the missing value is shown honestly.");
        Check(!create.Enabled && status.Text.Contains("Date of birth is missing") && status.Text.Contains("Admission record"),
            "Missing birth date is explained before the database would refuse creation.");
        row["DateOfBirth"] = new DateTime(2010, 1, 1);
        type.GetMethod("StudentChanged", flags)!.Invoke(page, null);
        Check(create.Enabled, "Recording a birth date enables creation after the selected student reloads.");
        var records = Field<DataTable>("certificateResults");
        records.Columns.Add("RegularStudentCertificateId", typeof(int)); records.Columns.Add("ClassName"); records.Rows.Add(7, "Ten");
        Refresh();
        Check(!create.Enabled, "An existing certificate for the same class still prevents duplicate creation.");
        row["DateOfBirth"] = DBNull.Value;
        type.GetField("selectedRegularStudentCertificateId", flags)!.SetValue(page, 7);
        type.GetField("savedClassName", flags)!.SetValue(page, "Ten");
        type.GetField("savedCertificateDate", flags)!.SetValue(page, DateTime.Today);
        Refresh();
        Check(status.Text == "Certificate ready to print.", "Missing current birth details do not block an existing certificate's saved snapshot.");
        Check(!page.IsHandleCreated, "Search regression checks never start page database loaders.");
    }
}
