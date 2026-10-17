namespace FMSC_SMS.Core;

internal static class SubjectDisplayOrder
{
    internal static DataTable Order(DataTable subjects)
    {
        if (!subjects.Columns.Contains("DisplayOrder")) return subjects;
        var ordered = subjects.Clone();
        foreach (var row in subjects.AsEnumerable().OrderBy(row => row.IsNull("DisplayOrder") ? int.MaxValue : Convert.ToInt32(row["DisplayOrder"])))
            ordered.ImportRow(row);
        return ordered;
    }

    internal static string OrderLine(string line, DataTable subjects)
    {
        var ordered = Order(subjects);
        var positions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < ordered.Rows.Count; i++)
            positions.TryAdd(CertificateUi.GetString(ordered.Rows[i], "SubjectName"), i);
        return string.Join(", ", line.Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .OrderBy(name => positions.TryGetValue(name, out int index) ? index : int.MaxValue));
    }
}
