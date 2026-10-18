using System.Data;
using System.Reflection;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Finance.Billing;

internal static partial class Program
{
    private static void CheckBillingGrid()
    {
        var rows = new DataTable();
        rows.Columns.Add("AdmissionId", typeof(int));
        foreach (string column in new[] { "AdmissionNumber", "StudentName", "Reason" }) rows.Columns.Add(column);
        rows.Columns.Add("IsLegacyReference", typeof(bool));
        rows.Rows.Add(1, "10", "One", "Review", true);
        rows.Rows.Add(2, "2a", "Two", "Review", true);
        rows.Rows.Add(3, "2", "Three", "Review", true);
        var students = new DataTable(); students.Columns.Add("AdmissionId", typeof(int));
        foreach (string column in new[] { "FatherName", "ClassName", "SectionName" }) students.Columns.Add(column);
        students.Rows.Add(1, "Father one", "Class 2", "A");
        students.Rows.Add(2, "Father two", DBNull.Value, DBNull.Value);
        students.Rows.Add(3, "Father three", "Class 1", DBNull.Value);
        BillingStatusPage.AddStudentDetails(rows, students);
        Check((string)rows.Rows[0]["FatherName"] == "Father one", "Father name is retained");
        Check((string)rows.Rows[0]["ClassSection"] == "Class 2 - A", "Existing class and section are combined");
        Check((string)rows.Rows[1]["ClassSection"] == "", "Missing placement stays blank");
        Check((string)rows.Rows[2]["ClassSection"] == "Class 1", "Class without a section has no trailing separator");
        var data = new DataSet(); data.Tables.Add(); data.Tables.Add(); data.Tables.Add(rows);
        using var page = new BillingStatusPage(null, () => Task.FromResult(data), false);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(BillingStatusPage).GetField("review", flags)!.SetValue(page, data);
        var view = (ModernComboBox)typeof(BillingStatusPage).GetField("view", flags)!.GetValue(page)!;
        using var form = new Form(); form.Controls.Add(page); form.Show(); Application.DoEvents();
        view.SelectedIndex = 2; Application.DoEvents();
        var grid = (DataGridView)typeof(V2Page).GetField("Grid", flags)!.GetValue(page)!;
        var bound = (DataView)grid.DataSource!;
        Check(bound.Cast<DataRowView>().Select(row => (string)row["AdmissionNumber"]).SequenceEqual(new[] { "2", "2a", "10" }), "Admission numbers use natural numeric and suffix order");
        Check(grid.Columns["AdmissionNumber"].DisplayIndex < grid.Columns["StudentName"].DisplayIndex &&
            grid.Columns["StudentName"].DisplayIndex < grid.Columns["FatherName"].DisplayIndex &&
            grid.Columns["FatherName"].DisplayIndex < grid.Columns["ClassSection"].DisplayIndex,
            "Grid starts with admission number, student, father and placement");
    }
}
