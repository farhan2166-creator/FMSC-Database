using System.Globalization;
using System.Text;

namespace FMSC_SMS.Core;

internal static class CsvFileExporter
{
    internal static void Write(DataTable table, string path)
    {
        if (table.Columns.Count == 0) throw new InvalidOperationException("There are no report columns to export.");
        AtomicExportFile.Publish(path, temporary =>
        {
            // Stream rows instead of retaining a second, full text copy of the report.
            using var writer = new StreamWriter(temporary, false, new UTF8Encoding(true, true));
            var columns = table.Columns.Cast<DataColumn>().ToArray();
            writer.WriteLine(string.Join(",", columns.Select(column => Cell(column.ColumnName))));
            foreach (DataRow row in table.Rows)
                writer.WriteLine(string.Join(",", columns.Select(column => Cell(row[column]))));
        });
    }

    private static string Cell(object? value)
    {
        string text = value is null || value == DBNull.Value ? ""
            : Convert.ToString(value, CultureInfo.CurrentCulture) ?? "";
        text = CsvSafety.Neutralise(text);
        return $"\"{text.Replace("\"", "\"\"")}\"";
    }
}
