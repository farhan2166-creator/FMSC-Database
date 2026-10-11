global using System.Data;
using FMSC_SMS.Core;
var source = new DataTable();
foreach (string column in new[] { "AdmissionNumber", "StudentName", "FatherName", "RegistrationNumber" }) source.Columns.Add(column);
source.Rows.Add("1", "Muhammad Ali", "Ahmed", "R1");
source.Rows.Add("20", "Ali Hassan", "Usman", "R20");
source.Rows.Add("100", "Ali", "Akbar", "R100");
source.Rows.Add("12A", "Zain", "Ali", "R12A");
source.Rows.Add("12", "Hamza", "Asif", "R12");
source.Rows.Add("120", "Ali", "Asif", "R120");
int checks = 0;
void Check(bool passed, string description) { checks++; if (!passed) throw new Exception(description); }
string Id(DataRow row) => (string)row["AdmissionNumber"];
var names = StudentSearchMatcher.Find(source, "ali");
Check(Id(names[0]) == "12A" && Id(names[1]) == "100", "Exact father and student names precede prefixes");
Check(Id(names[3]) == "20" && Id(names[4]) == "1", "Prefix precedes substring");
Check(Id(StudentSearchMatcher.Find(source, "0012")[0]) == "12", "Normalized admission exact priority");
Check(Id(StudentSearchMatcher.Find(source, "12a")[0]) == "12A", "Admission suffix match");
Check(Id(StudentSearchMatcher.Find(source, "R100")[0]) == "100", "Internal registration search remains supported");
Check(Id(StudentSearchMatcher.Find(source, "ali ahmed").Single()) == "1", "Tokens span student and father fields");
Check(Id(StudentSearchMatcher.Find(source, "ali", 1, r => Id(r) == "20").Single()) == "20", "Scope filter precedes limit");
Check(StudentSearchMatcher.Find(source, "missing").Count == 0, "No false matches");
Check(StudentSearchMatcher.Find(source, "ali", 2).Count == 2, "Limit honored");
Check(string.Join(",", StudentSearchMatcher.Find(source, null).Select(Id)) == "1,12,12A,20,100,120", "Empty query retains natural admission order");
Check(Id(source.Rows[0]) == "1" && source.Rows.Count == 6, "Source remains unchanged");
var sparse = new DataTable(); sparse.Columns.Add("StudentName"); sparse.Rows.Add(DBNull.Value); sparse.Rows.Add("Ali");
Check(StudentSearchMatcher.Find(sparse, "ali").Count == 1, "Optional columns and nulls supported");
source.Columns.Add("AdmissionId", typeof(int));
for (int i = 0; i < source.Rows.Count; i++) source.Rows[i]["AdmissionId"] = i + 1;
StudentPickerMatchBuilder.Result Picker(string term, int cap = 150) => StudentPickerMatchBuilder.Build(
    source, term, cap, null, "AdmissionId", 0, row => (string)row["StudentName"],
    row => new[] { Id(row), (string)row["StudentName"], (string)row["FatherName"], "", "", "", (string)row["RegistrationNumber"] });
Check(Picker("ali").Highlight == -1, "Ambiguous name results remain unhighlighted");
Check(Picker("0012").Entries[Picker("0012").Highlight].Cells[0] == "12", "Unique normalized admission highlighted");
Check(Picker("ali", 2).Capped && Picker("ali", 2).Entries.Count == 2, "Picker cap remains visible");
source.ImportRow(source.Rows[4]);
Check(Picker("12").Highlight == -1, "Duplicate exact admissions remain unhighlighted");
Console.WriteLine($"Student search: {checks} checks passed.");
namespace FMSC_SMS.Core
{
    internal static class HumanUi
    {
        public static List<DataRow> FindStudentMatches(DataTable source, string? term, int maxRows, Func<DataRow, bool>? filter)
            => StudentSearchMatcher.Find(source, term, maxRows, filter);
    }
    internal class StudentPicker
    {
        internal sealed record Entry(int Id, string Display, int AdmissionId, string[] Cells);
    }
    internal static class DatabaseHelper
    {
        public static Microsoft.Data.SqlClient.SqlConnection GetConnection() => throw new NotSupportedException("Search checks must not access the database.");
        public static void OpenConnection(Microsoft.Data.SqlClient.SqlConnection connection) => throw new NotSupportedException();
    }
}
