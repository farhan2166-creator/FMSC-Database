using System.Globalization;

namespace FMSC_SMS.Core;

internal static class ReportExportBuilder
{
    internal static DataTable Build(
        DataTable source,
        IEnumerable<DataGridViewColumn> gridColumns,
        IEnumerable<string> dateColumnKeys)
    {
        DataGridViewColumn[] visible = gridColumns.Where(c => c.Visible)
            .OrderBy(c => c.DisplayIndex).ToArray();
        var export = new DataTable();
        var sourceColumns = new List<DataColumn>();
        var dates = new HashSet<string>(dateColumnKeys, StringComparer.OrdinalIgnoreCase);
        var dateOrdinals = new HashSet<int>();
        foreach (DataGridViewColumn column in visible)
        {
            string sourceName = string.IsNullOrEmpty(column.DataPropertyName)
                ? column.Name
                : column.DataPropertyName;
            if (!source.Columns.Contains(sourceName) || source.Columns[sourceName] is not DataColumn sourceColumn)
                continue;
            sourceColumns.Add(sourceColumn);
            string header = column.HeaderText;
            string uniqueHeader = header;
            int suffix = 1;
            while (export.Columns.Contains(uniqueHeader))
                uniqueHeader = $"{header} ({++suffix})";
            bool asDate = dates.Contains(sourceColumn.ColumnName);
            export.Columns.Add(uniqueHeader, asDate ? typeof(string) : sourceColumn.DataType);
            if (asDate)
                dateOrdinals.Add(sourceColumns.Count - 1);
        }
        foreach (DataRow sourceRow in source.Rows)
        {
            DataRow row = export.NewRow();
            for (int index = 0; index < sourceColumns.Count; index++)
            {
                object value = sourceRow[sourceColumns[index]];
                row[index] = dateOrdinals.Contains(index)
                    ? value is DateTime date
                        ? date.ToString("dd-MM-yyyy", CultureInfo.CurrentCulture)
                        : DBNull.Value
                    : value;
            }
            export.Rows.Add(row);
        }
        if (source.ExtendedProperties[CollectionReceiptGrouping.LayoutKey] is CollectionReceiptGrouping.RowLayout[] layout)
            export.ExtendedProperties[CollectionReceiptGrouping.LayoutKey] = layout;
        return export;
    }

    internal static void WriteCsv(DataTable table, string filePath)
        => CsvFileExporter.Write(table, filePath);
}
