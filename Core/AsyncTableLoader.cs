using System.Data;
using System.Data.Common;

namespace FMSC_SMS.Core;

internal static class AsyncTableLoader
{
    internal static async Task<DataTable> ReadAsync(DbDataReader reader, string tableName, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var table = new DataTable(tableName);
        for (int i = 0; i < reader.FieldCount; i++)
        {
            string baseName = reader.GetName(i);
            if (string.IsNullOrEmpty(baseName)) baseName = $"Column{i + 1}";
            string name = baseName;
            for (int suffix = 1; table.Columns.Contains(name); suffix++) name = baseName + suffix;
            table.Columns.Add(name, reader.GetFieldType(i));
        }
        // Rows.Add copies these values into the table's own storage. Reuse the
        // transfer buffer instead of allocating another array for every row.
        var values = new object[reader.FieldCount];
        table.BeginLoadData();
        try
        {
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                token.ThrowIfCancellationRequested();
                reader.GetValues(values);
                table.Rows.Add(values);
            }
        }
        finally { table.EndLoadData(); }
        table.AcceptChanges();
        return table;
    }
}
