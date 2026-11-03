namespace FMSC_SMS.Core;

// PDF/print context is separate from raw exports. Never rewrite identifiers or source rows.
internal static class ReportContextProjection
{
    internal const string SourceColumnKey="FMSC.ReportSourceColumn";
    private static string Key(string value)=>new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    private static bool IsCampus(DataColumn column)=>Key(column.ColumnName) is "campus" or "campusname"
        || Key(Convert.ToString(column.ExtendedProperties[SourceColumnKey])??"") is "campus" or "campusname";

    internal static DataTable ForPdf(DataTable source,string? campusContext=null)
    {
        var result=source.Copy();
        var layout=CollectionReceiptGrouping.Layout(source);
        int[] details=Enumerable.Range(0,source.Rows.Count).Where(index=>layout is not null
            ? layout[index].Kind=="Detail" : !ReportPrintSummary.IsTotalRow(source.Rows[index])).ToArray();
        bool grouped=layout is not null&&layout.Any(row=>row.Kind=="Heading")
            &&details.Length>0&&details.All(index=>layout[index].CampusInHeading);
        foreach(var campus in source.Columns.Cast<DataColumn>().Where(IsCampus))
        {
            bool sameScope=!string.IsNullOrWhiteSpace(campusContext)&&details.Length>0&&details.All(index=>
                string.Equals(Convert.ToString(source.Rows[index][campus])?.Trim(),campusContext.Trim(),StringComparison.OrdinalIgnoreCase));
            // A mixed-campus row keeps its location unless its own group heading covers it.
            if((grouped||sameScope)&&result.Columns.Count>1)result.Columns.Remove(campus.ColumnName);
        }
        return result;
    }
}
