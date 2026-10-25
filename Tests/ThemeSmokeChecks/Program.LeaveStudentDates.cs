using System.Data;
using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Students.Certificates;

internal static partial class Program
{
    private static void CheckLeaveStudentDates()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        foreach (Control page in new Control[] { new LeaveCertificatePage(null, false), new CollegeLeaveCertificatePage(false) })
        using (page)
        {
            var type = page.GetType();
            T Field<T>(string name) => (T)type.GetField(name, flags)!.GetValue(page)!;
            void Apply(DataRow? row)
            {
                type.GetMethod("SetStudentDates", flags)!.Invoke(page, new object?[] { row });
                type.GetMethod("ApplyWorkflowState", flags)!.Invoke(page, null);
            }
            using var students = new DataTable();
            students.Columns.Add("AdmissionId", typeof(int));
            students.Columns.Add("Status");
            students.Columns.Add("AdmissionDate", typeof(DateTime));
            students.Columns.Add("DateOfBirth", typeof(DateTime));
            var row = students.Rows.Add(41, "Active", new DateTime(2020, 4, 1), new DateTime(2010, 2, 3));
            Field<StudentPicker>("student").SetStudent(row);
            var admission = Field<ModernTextBox>("admissionDateDisplay");
            var birth = Field<ModernTextBox>("dateOfBirthDisplay");
            var create = Field<ModernButton>("createCertificateButton");
            foreach (string status in new[] { "Active", "Left" })
            {
                row["Status"] = status;
                row["AdmissionDate"] = new DateTime(2020, 4, 1);
                row["DateOfBirth"] = new DateTime(2010, 2, 3);
                Apply(row);
                Check(create.Enabled && admission.Text == "01 Apr 2020" && birth.Text == "03 Feb 2010",
                    type.Name + " displays recorded dates and allows creation for " + status);
                row["DateOfBirth"] = DBNull.Value;
                Apply(row);
                Check(!create.Enabled && birth.Text == "Not on record", "Missing birth date blocks creation");
                row["AdmissionDate"] = DBNull.Value;
                Apply(row);
                Check(!create.Enabled, "Both dates missing blocks creation");
                row["DateOfBirth"] = new DateTime(2010, 2, 3);
                Apply(row);
                Check(!create.Enabled && admission.Text == "Not on record", "Missing admission date blocks creation");
            }
            Check(admission.ReadOnly && birth.ReadOnly && !admission.TabStop && !birth.TabStop,
                "Recorded dates are read-only reference fields");
            using var missingColumns = new DataTable();
            missingColumns.Columns.Add("AdmissionId");
            Apply(missingColumns.Rows.Add("42"));
            Check(!create.Enabled && admission.Text == "Not on record" && birth.Text == "Not on record",
                "A record without date columns clears previous dates and blocks creation");
            Field<StudentPicker>("student").Clear();
            type.GetMethod("ClearStudentDetails", flags)!.Invoke(page, null);
            type.GetMethod("ApplyWorkflowState", flags)!.Invoke(page, null);
            Check(!create.Enabled && admission.Text.Length == 0 && birth.Text.Length == 0,
                "Clearing the student clears both dates and disables creation");
            Check(!page.IsHandleCreated, "Date checks start no database loaders");
        }
    }
}
